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

    // Every scene load puts us back into Playing. Without this, restarting after
    // a loss would reload the level with the state still stuck on Lost.
    //
    // The timer resets here too, which also covers the MainMenu — harmless,
    // since loading Level01 resets it again immediately afterwards.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stateMachine.ResetToPlaying();
        runTimer.Reset();
    }

    public void Win()
    {
        if (!stateMachine.Win())
        {
            return; // already won or lost — the goal trigger fired twice
        }

        // Stop BEFORE anything else, so the recorded time is the moment the
        // bird reached the nest rather than the moment the UI finished
        // reacting to it.
        runTimer.Stop();

        Debug.Log($"WIN — reunited with the family in {RunTimer.Format(ElapsedMilliseconds)}.", this);
    }

    public void Lose()
    {
        if (!stateMachine.Lose())
        {
            return; // already won or lost — multiple hazard contacts in one frame
        }

        runTimer.Stop();

        Debug.Log("GAME OVER — hit a hazard.", this);
    }

    public void LoadMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void PlayLevel01()
    {
        // A fresh attempt from the menu is attempt one. Resetting here rather
        // than on every scene load is the whole point: a restart must NOT
        // clear the count, or it would always be zero.
        RestartCount = 0;
        SceneManager.LoadScene(level01SceneName);
    }

    public void RestartCurrentScene()
    {
        RestartCount++;
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.name);
    }
}
