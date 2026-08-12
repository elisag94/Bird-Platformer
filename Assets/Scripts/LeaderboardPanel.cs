using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Renders GET /api/scores/top into a single text field.
///
/// Deliberately one TMP_Text and not ten row prefabs. Rows would look tidier
/// and cost an afternoon of layout work that teaches nothing about the system
/// being built; this shows the same data and can be replaced later without
/// touching anything else. Use a MONOSPACED font on the entries field, or the
/// columns will not line up.
///
/// Drop it on any panel in any scene — the main menu, the win overlay, both.
/// It finds the API through LeaderboardClient.Instance, which creates itself,
/// so there is nothing else to wire.
/// </summary>
public class LeaderboardPanel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The object to show and hide. Leave empty to use this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Where the rows are written.")]
    [SerializeField] private TMP_Text entriesText;

    [Tooltip("Forces every character to the same width so the columns line up, without needing a " +
             "monospaced font asset. 0 turns it off. 0.6em suits LiberationSans.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float monospaceEm = 0.6f;

    [Tooltip("Optional. 'Loading…', 'Leaderboard unreachable.', and so on.")]
    [SerializeField] private TMP_Text statusText;

    [Header("Query")]
    [Tooltip("Which level's board to show. Leave empty to use the current scene name.")]
    [SerializeField] private string levelIdOverride = "Level01";

    [Range(1, 50)]
    [SerializeField] private int limit = 10;

    [Tooltip("Fetch automatically whenever this panel becomes visible.")]
    [SerializeField] private bool refreshOnEnable = true;

    [Header("Appearance")]
    [Tooltip("Colour for the row belonging to the player at this machine. Sunset orange by default, " +
             "to match the horizon.")]
    [SerializeField] private Color highlightColor = new Color(0.949f, 0.698f, 0.475f);

    private bool requestInFlight;

    // Set when Refresh() is called while a fetch is already running. The
    // running fetch's answer is then known to be out of date before it even
    // arrives.
    private bool requestSuperseded;

    private GameObject Root => panelRoot != null ? panelRoot : gameObject;

    private string LevelId =>
        string.IsNullOrWhiteSpace(levelIdOverride)
            ? (GameManager.Instance != null ? GameManager.Instance.CurrentLevelId : "Level01")
            : levelIdOverride;

    private void OnEnable()
    {
        if (refreshOnEnable)
        {
            Refresh();
        }
    }

    /// <summary>Show the panel and fetch. Wire a button's OnClick() to this.</summary>
    public void Show()
    {
        Root.SetActive(true);

        // If Root is this object, SetActive(true) already triggered OnEnable
        // and therefore a refresh — guarding avoids firing two requests for
        // one click.
        if (!refreshOnEnable || Root != gameObject)
        {
            Refresh();
        }
    }

    /// <summary>Hide the panel. Wire a Close button's OnClick() to this.</summary>
    public void Hide()
    {
        Root.SetActive(false);
    }

    public void Toggle()
    {
        if (Root.activeSelf)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    /// <summary>
    /// Re-fetch the board.
    ///
    /// Safe to call at any time, including while a fetch is already running.
    /// A call made mid-flight does NOT get dropped — it marks the running
    /// request as superseded, and a fresh one is issued the moment that one
    /// lands.
    ///
    /// THE BUG THIS FIXES, because it is a good one:
    ///
    ///   t0  win panel opens  → GET #1 leaves      (the board as it was)
    ///   t1  score submitted  → POST leaves
    ///   t2  POST returns     → Refresh() called
    ///   t3  GET #1 returns   → data from BEFORE the write
    ///
    /// The old code dropped the t2 refresh because a request was in flight,
    /// then painted the t3 response — a board that predates the run you just
    /// finished. The rank was correct, because that came from the POST, so
    /// only the list looked wrong, and only until something reopened the
    /// panel. That is the shape of every read-after-write race: the answer is
    /// not wrong, it is just from a moment ago.
    /// </summary>
    public void Refresh()
    {
        if (LeaderboardClient.Instance == null)
        {
            SetStatus("Leaderboard unavailable.");
            return;
        }

        if (requestInFlight)
        {
            requestSuperseded = true;
            return;
        }

        Fetch();
    }

    private void Fetch()
    {
        requestInFlight = true;
        requestSuperseded = false;
        SetStatus("Loading…");

        LeaderboardClient.Instance.GetTopScores(LevelId, limit, OnLoaded, OnFailed);
    }

    /// <summary>
    /// Called first in both completion handlers. If a refresh was asked for
    /// while this request was travelling, its answer is already out of date:
    /// throw it away unpainted and go again. Discarding rather than rendering
    /// it avoids a visible flash of stale rows.
    /// </summary>
    private bool ResponseIsStale()
    {
        requestInFlight = false;

        if (!requestSuperseded || LeaderboardClient.Instance == null)
        {
            return false;
        }

        Fetch();
        return true;
    }

    private void OnLoaded(LeaderboardClient.LeaderboardResponse response)
    {
        if (ResponseIsStale())
        {
            return;
        }

        // The panel may have been closed, or the scene reloaded, while the
        // request was in flight — the client outlives both. Unity's overloaded
        // null check covers the destroyed case.
        if (entriesText == null)
        {
            return;
        }

        if (response.entries == null || response.entries.Length == 0)
        {
            entriesText.text = string.Empty;
            SetStatus("No times yet. Be the first.");
            return;
        }

        string me = PlayerIdentity.Name;
        StringBuilder builder = new StringBuilder();

        foreach (LeaderboardClient.LeaderboardEntry entry in response.entries)
        {
            // Same Format() the in-game timer uses, so the number on the win
            // screen and the number on the board are formatted by one piece of
            // code. Two formatters drift, and the drift looks like a bug in
            // the timer.
            string line = $"{entry.rank,2}. {Truncate(entry.player_name, 16),-16} {RunTimer.Format(entry.duration_ms)}";

            // Rich text, not a second text object: marking the local player is
            // presentation, and TMP already does presentation. Bold AND a
            // colour shift, because bold alone is easy to miss at 15pt and
            // colour alone disappears for anyone who can't distinguish it —
            // two signals cost nothing here.
            bool isMe = !string.IsNullOrEmpty(entry.player_name)
                        && string.Equals(entry.player_name, me, System.StringComparison.Ordinal);

            builder.AppendLine(isMe
                ? $"<b><color=#{ColorUtility.ToHtmlStringRGB(highlightColor)}>{line}</color></b>"
                : line);
        }

        // <mspace> is TMP's rich-text override for character width. Padding
        // with String.Format only lines up in a monospaced font, and shipping a
        // second font asset to align ten rows is not a good trade — this gets
        // the same result out of the font already in the project.
        entriesText.text = monospaceEm > 0f
            ? $"<mspace={monospaceEm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}em>{builder}</mspace>"
            : builder.ToString();

        SetStatus(string.Empty);
    }

    private void OnFailed(string message)
    {
        if (ResponseIsStale())
        {
            return;
        }

        if (entriesText != null)
        {
            entriesText.text = string.Empty;
        }

        SetStatus(message);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
