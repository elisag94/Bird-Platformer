using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the game state machine (Playing / Won / Lost), the run timer, and
/// scene transitions. Everything else — UI, player input, hazards — reacts to
/// StateChanged instead of holding direct references to each other.
/// </summary>
public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Playing,
        Won,
        Lost
    }

    public static GameManager Instance { get; private set; }

    /// <summary>
    /// Static so listeners in a freshly loaded scene can subscribe in OnEnable
    /// without worrying about whether Instance exists yet.
    /// </summary>
    public static event Action<GameState> StateChanged;

    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string level01SceneName = "Level01";

    // The actual rules live in plain C# classes so they can be unit tested
    // without play mode. See Assets/Tests/EditMode/.
    private readonly GameStateMachine stateMachine = new GameStateMachine();
    private readonly RunTimer runTimer = new RunTimer();

    public GameState State => stateMachine.State;

    /// <summary>
    /// Elapsed time for the current run, in integer milliseconds. This is the
    /// value the win screen displays and the leaderboard client submits.
    /// </summary>
    public int ElapsedMilliseconds => runTimer.ElapsedMilliseconds;

    /// <summary>
    /// The level identifier sent to the leaderboard. The scene name is the
    /// natural key and needs no separate registry to drift out of sync.
    /// </summary>
    public string CurrentLevelId => SceneManager.GetActiveScene().name;

    /// <summary>
    /// How many times the player has restarted since the level was last
    /// entered from the menu. Submitted to the API as `deaths`.
    ///
    /// The API has a `deaths` column but the game has no concept of dying
    /// mid-run: touching a hazard ends the run outright and the only way
    /// forward is a restart. Rather than send a permanently-zero field —
    /// worse than not having the field at all — "deaths" is defined as
    /// "attempts before the one that worked", which is the same information a
    /// player would give you if you asked how it went.
    ///
    /// This counter lives here because GameManager is DontDestroyOnLoad: it is
    /// the only object that survives the scene reload a restart performs.
    /// Anything in the level scene forgets by definition.
    /// </summary>
    public int RestartCount { get; private set; }

    private void Awake()
    {
        // Simple singleton so we don't end up with duplicates when switching scenes.
        if (Instance != null && Instance != this)
        {
#if UNITY_EDITOR
            Debug.Log("Duplicate GameManager detected; destroying the newer one.", this);
#endif
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        stateMachine.StateChanged += RaiseStateChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            stateMachine.StateChanged -= RaiseStateChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }
    }

    // Ticked unconditionally. RunTimer ignores ticks once stopped, so there is
    // no state check to forget here — the timer owns that rule, not the caller.
    //
    // Time.deltaTime is 0 while Time.timeScale is 0, so a pause menu costs the
    // player nothing without any extra code.
    private void Update()
    {
        runTimer.Tick(Time.deltaTime);
    }

    private static void RaiseStateChanged(GameState newState)
    {
        StateChanged?.Invoke(newState);
    }

    // A safety net for scene loads that did NOT go through this class. The real
    // reset happens in BeginSceneLoad, before the load is requested — see the
    // comment there for why waiting until the scene has loaded is too late.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stateMachine.ResetToPlaying();
        runTimer.Reset();
    }

    /// <summary>
    /// Clear the run BEFORE asking Unity to load a scene, not after.
    ///
    /// THIS ORDERING IS A BUG FIX, and the bug was a good one.
    ///
    /// Unity runs Awake and OnEnable on every object in the NEW scene, and only
    /// then raises sceneLoaded. GameManager survives the load, so during that
    /// window it is still carrying the finished run: State is still Won and
    /// ElapsedMilliseconds is still the previous time.
    ///
    /// LevelUIController reads GameManager.State in its OnEnable so that a
    /// freshly-created panel matches reality. Landing in that window, it saw
    /// Won, believed the run had just finished, and submitted the PREVIOUS
    /// run's time — burning its single-submit guard. The real win a few seconds
    /// later was then silently ignored.
    ///
    /// The symptom was a leaderboard that always showed the run before last.
    /// The cause was reading state from an object that had not been told the
    /// level restarted yet. Resetting at the point the load is REQUESTED closes
    /// the window entirely: there is never a moment where a new scene can
    /// observe a stale finished run.
    /// </summary>
    private void BeginSceneLoad()
    {
        stateMachine.ResetToPlaying();
        runTimer.Reset();
    }

    public void Win()
    {
        // The guard is checked here rather than relying on the return value of
        // stateMachine.Win(), because stateMachine.Win() RAISES StateChanged
        // from inside itself. Anything reacting to that event — the win panel,
        // the score submission — reads ElapsedMilliseconds while it runs, so
        // the timer has to already be stopped by then. Checking first lets
        // Stop() genuinely be the first thing that happens.
        if (stateMachine.State != GameState.Playing)
        {
            return; // already won or lost — the goal trigger fired twice
        }

        runTimer.Stop();
        stateMachine.Win();

        Debug.Log($"WIN — reunited with the family in {RunTimer.Format(ElapsedMilliseconds)}.", this);
    }

    public void Lose()
    {
        if (stateMachine.State != GameState.Playing)
        {
            return; // already won or lost — multiple hazard contacts in one frame
        }

        runTimer.Stop();
        stateMachine.Lose();

        Debug.Log("GAME OVER — hit a hazard.", this);
    }

    public void LoadMainMenu()
    {
        BeginSceneLoad();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void PlayLevel01()
    {
        // A fresh attempt from the menu is attempt one. Resetting here rather
        // than on every scene load is the whole point: a restart must NOT
        // clear the count, or it would always be zero.
        RestartCount = 0;
        BeginSceneLoad();
        SceneManager.LoadScene(level01SceneName);
    }

    public void RestartCurrentScene()
    {
        RestartCount++;
        BeginSceneLoad();

        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.name);
    }
}
