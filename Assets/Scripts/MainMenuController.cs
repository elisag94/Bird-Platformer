using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    // Matches the scene name you will create/save: Assets/Scenes/Level01.unity
    [SerializeField] private string levelSceneName = "Level01";

    // Hook this up to the UI Button's OnClick().
    public void Play()
    {
        // Prefer the central GameManager if present.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayLevel01();
            return;
        }

        // Fallback (useful while you're still wiring things up).
        SceneManager.LoadScene(levelSceneName);
    }
}
