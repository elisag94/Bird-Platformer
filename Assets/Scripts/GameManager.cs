using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string level01SceneName = "Level01";

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

#if UNITY_EDITOR
        Debug.Log("GameManager initialized and set to DontDestroyOnLoad.", this);
#endif
    }

    public void LoadMainMenu()
    {
#if UNITY_EDITOR
        Debug.Log($"Loading Main Menu scene: {mainMenuSceneName}", this);
#endif
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void PlayLevel01()
    {
#if UNITY_EDITOR
        Debug.Log($"Loading Level scene: {level01SceneName}", this);
#endif
        SceneManager.LoadScene(level01SceneName);
    }

    public void RestartCurrentScene()
    {
        Scene current = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        Debug.Log($"Restarting current scene: {current.name}", this);
#endif
        SceneManager.LoadScene(current.name);
    }
}
