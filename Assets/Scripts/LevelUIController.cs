using TMPro;
using UnityEngine;

/// <summary>
/// Phase 5: shows the Win / Game Over overlay in response to the GameManager's
/// state, and exposes Restart / BackToMenu for the buttons to call.
/// Both panels start hidden and only one is ever visible.
///
/// Also displays the run timer: a live HUD readout while playing, and the
/// frozen final time on the win panel.
/// </summary>
public class LevelUIController : MonoBehaviour
{
    [Header("Panels (leave hidden in the scene; this script shows them)")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;

    [Header("Timer (all optional — leave empty to skip)")]
    // TMP_Text rather than TextMeshProUGUI: it's the base type, so either a
    // UI text or a world-space text can be dragged in without changing code.
    [SerializeField] private TMP_Text hudTimeText;
    [SerializeField] private TMP_Text winTimeText;

    [Tooltip("Prefix shown on the win panel, e.g. \"Time: \"")]
    [SerializeField] private string winTimePrefix = "Time: ";

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

    // The HUD readout is polled rather than event-driven: the timer changes
    // every single frame, so an event per change would just be Update with
    // extra steps.
    //
    // Reading GameManager.ElapsedMilliseconds unconditionally is safe because
    // RunTimer freezes on its own once the run ends — the HUD simply stops
    // changing, showing the final time, with no state check needed here.
    private void Update()
    {
        if (hudTimeText == null || GameManager.Instance == null)
        {
            return;
        }

        hudTimeText.text = RunTimer.Format(GameManager.Instance.ElapsedMilliseconds);
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

        // Written once, on the transition into Won, rather than every frame.
        // By this point GameManager.Win() has already stopped the timer, so the
        // value read here is final — that ordering is deliberate and is why
        // runTimer.Stop() is the first thing Win() does.
        if (state == GameManager.GameState.Won && winTimeText != null && GameManager.Instance != null)
        {
            winTimeText.text = winTimePrefix + RunTimer.Format(GameManager.Instance.ElapsedMilliseconds);
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
