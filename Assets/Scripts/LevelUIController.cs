using UnityEngine;

/// <summary>
/// Phase 5: shows the Win / Game Over overlay in response to the GameManager's
/// state, and exposes Restart / BackToMenu for the buttons to call.
/// Both panels start hidden and only one is ever visible.
/// </summary>
public class LevelUIController : MonoBehaviour
{
    [Header("Panels (leave hidden in the scene; this script shows them)")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;

    // Subscribing in OnEnable / unsubscribing in OnDisable is the safe pattern —
    // it survives scene reloads without leaking listeners.
    private void OnEnable()
    {
        GameManager.StateChanged += HandleStateChanged;

        if (GameManager.Instance != null)
        {
            HandleStateChanged(GameManager.Instance.State);
        }
        else
        {
            HandleStateChanged(GameManager.GameState.Playing);
        }
    }

    private void OnDisable()
    {
        GameManager.StateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(GameManager.GameState state)
    {
        if (winPanel != null)
        {
            winPanel.SetActive(state == GameManager.GameState.Won);
        }

        if (losePanel != null)
        {
            losePanel.SetActive(state == GameManager.GameState.Lost);
        }
    }

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