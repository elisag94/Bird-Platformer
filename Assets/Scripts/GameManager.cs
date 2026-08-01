using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the game state machine (Playing / Won / Lost) and scene transitions.
/// Everything else — UI, player input, hazards — reacts to StateChanged
/// instead of holding direct references to each other.
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

    // The actual rules live in a plain C# class so they can be unit tested
    // without play mode. See Assets/Tests/EditMode/GameStateMachineTests.cs.
    private readonly GameStateMachine stateMachine = new GameStateMachine();

    public GameState State => stateMachine.State;

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

    private static void RaiseStateChanged(GameState newState)
    {
        StateChanged?.Invoke(newState);
    }

    // Every scene load puts us back into Playing. Without this, restarting after
    // a loss would reload the level with the state still stuck on Lost.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stateMachine.ResetToPlaying();
    }

    public void Win()
    {
        if (!stateMachine.Win())
        {
            return; // already won or lost — the goal trigger fired twice
        }

        Debug.Log("WIN — reunited with the family.", this);
    }

    public void Lose()
    {
        if (!stateMachine.Lose())
        {
            return; // already won or lost — multiple hazard contacts in one frame
        }

        Debug.Log("GAME OVER — hit a hazard.", this);
    }

    public void LoadMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void PlayLevel01()
    {
        SceneManager.LoadScene(level01SceneName);
    }

    public void RestartCurrentScene()
    {
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.name);
    }
}