using UnityEngine;

public class LevelUIController : MonoBehaviour
{
    public void Restart()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("GameManager.Instance is null. Add a GameManager to your initial scene (or use GameManagerBootstrap).", this);
            return;
        }

        GameManager.Instance.RestartCurrentScene();
    }

    public void BackToMenu()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("GameManager.Instance is null. Add a GameManager to your initial scene (or use GameManagerBootstrap).", this);
            return;
        }

        GameManager.Instance.LoadMainMenu();
    }
}
