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

    [Header("Leaderboard (all optional)")]
    [Tooltip("Where 'Rank #3 — personal best!' or an error message appears.")]
    [SerializeField] private TMP_Text winRankText;

    [Tooltip("Optional. Refreshed after a successful submit so the new time is already in the list.")]
    [SerializeField] private LeaderboardPanel leaderboardPanel;

    [Tooltip("Turn off to play without touching the API — useful when the cluster is down.")]
    [SerializeField] private bool submitScores = true;

    // Single-submit guard. GameStateMachine already refuses a second Win(), but
    // this object also replays the current state in OnEnable, and a panel being
    // re-enabled must not post the same run twice. The guard is a field on a
    // per-scene component, so a restart clears it by destroying the component —
    // no reset logic to forget.
    private bool hasSubmitted;

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

        if (state == GameManager.GameState.Won)
        {
            SubmitRun();
        }
    }

    /// <summary>
    /// Send the finished run to the leaderboard.
    ///
    /// Fired from the state transition rather than from a "Submit" button: the
    /// run is over and its numbers are already final, so asking the player to
    /// confirm adds a step and a way to lose the score.
    /// </summary>
    private void SubmitRun()
    {
        if (!submitScores || hasSubmitted)
        {
            return;
        }

        hasSubmitted = true;

        if (GameManager.Instance == null || LeaderboardClient.Instance == null)
        {
            SetRankText("Leaderboard unavailable.");
            return;
        }

        GameManager manager = GameManager.Instance;

        SetRankText("Submitting…");

        // The client holds the stopwatch, which is the known weakness of this
        // design: ElapsedMilliseconds is whatever the browser says it is. The
        // API's MIN_RUN_MS / MAX_RUN_MS bounds make the crudest cheating
        // obvious, and the real fix is a server-issued run token so the
        // duration comes from the server's own clock. Named, not hidden.
        LeaderboardClient.Instance.SubmitScore(
            PlayerIdentity.Name,
            manager.CurrentLevelId,
            manager.ElapsedMilliseconds,
            manager.RestartCount,
            OnScoreAccepted,
            OnScoreRejected);
    }

    private void OnScoreAccepted(LeaderboardClient.ScoreResponse response)
    {
        SetRankText(response.personal_best
            ? $"Rank #{response.rank} — personal best!"
            : $"Rank #{response.rank}");

        // Refreshed only after the POST returns, never in parallel with it.
        // Fetching the board while the write is still in flight is a race that
        // shows a list without the run that just finished — and looks exactly
        // like a dropped submission.
        if (leaderboardPanel != null)
        {
            leaderboardPanel.Refresh();
        }
    }

    private void OnScoreRejected(string message)
    {
        // The server's own words. A 400 here reads "duration_ms must be at
        // least 3000" — which is the API telling you the run was too short to
        // be plausible, not a bug in the game.
        SetRankText(message);
    }

    private void SetRankText(string message)
    {
        if (winRankText != null)
        {
            winRankText.text = message;
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
