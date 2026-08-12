using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The player's display name — the only identity this game has.
///
/// There is no account, no password and no session. The leaderboard's `players`
/// table is a real table with a UNIQUE constraint on the name, but a name is
/// not a credential: anyone who types "elisa" is elisa. That was a deliberate
/// scoping decision (see the Day 6 design table), and it is a much better
/// interview answer than a half-built auth system.
///
/// Storage is PlayerPrefs, which sounds like a file but is not one in a Web
/// build — Unity maps it onto the browser's IndexedDB for the page's origin.
/// Practical consequences worth knowing:
///   * it is per-browser and per-origin, so a name saved on bird.local does not
///     follow you to a CloudFront URL in Week 4;
///   * clearing site data clears it;
///   * it is not a security boundary, which is fine, because neither is a name.
///
/// Static with no MonoBehaviour: there is nothing to tick and nothing to draw,
/// so there is nothing for a component to own. Same reasoning as RunTimer and
/// GameStateMachine.
/// </summary>
public static class PlayerIdentity
{
    private const string PlayerPrefsKey = "bird.player_name";

    /// <summary>Used when the player has not typed anything yet.</summary>
    public const string DefaultName = "anonymous";

    /// <summary>
    /// Mirrors MAX_NAME_LENGTH in api/app.py. Duplicated constants across a
    /// network boundary are a known smell — the honest framing is that the
    /// server is the authority and this copy exists only to fail fast in the
    /// UI. The client check is a courtesy; the server check is the rule.
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>
    /// Same character class as NAME_PATTERN in api/app.py: letters, digits,
    /// space, underscore, hyphen. Rejecting rather than silently stripping is
    /// deliberate — quietly renaming someone's player is worse than telling
    /// them no.
    /// </summary>
    /// (No RegexOptions.Compiled: compiling a regex needs Reflection.Emit,
    /// which does not exist under IL2CPP — the backend every WebGL build uses.
    /// It is silently ignored at best, and this pattern runs once per name.)
    private static readonly Regex NamePattern = new Regex(@"^[\w \-]+$");

    /// <summary>
    /// The stored name, or <see cref="DefaultName"/> if none has been saved.
    /// Never returns null or empty, so callers never need a fallback.
    /// </summary>
    public static string Name
    {
        get
        {
            string stored = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            return string.IsNullOrWhiteSpace(stored) ? DefaultName : stored;
        }
    }

    /// <summary>True once the player has chosen a name of their own.</summary>
    public static bool HasName =>
        !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PlayerPrefsKey, string.Empty));

    /// <summary>
    /// Validate and store. Returns false with a reason if the name is not
    /// acceptable, so the caller can put the reason on screen rather than
    /// guessing.
    ///
    /// PlayerPrefs.Save() is called explicitly. Unity flushes on a clean quit,
    /// but a browser tab is very often not a clean quit.
    /// </summary>
    public static bool TrySetName(string raw, out string error)
    {
        string name = (raw ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            error = "Enter a name first.";
            return false;
        }

        if (name.Length > MaxNameLength)
        {
            error = $"Name must be {MaxNameLength} characters or fewer.";
            return false;
        }

        if (!NamePattern.IsMatch(name))
        {
            error = "Letters, digits, spaces, hyphens and underscores only.";
            return false;
        }

        PlayerPrefs.SetString(PlayerPrefsKey, name);
        PlayerPrefs.Save();

        error = null;
        return true;
    }

    /// <summary>Forget the stored name. Handy while testing a fresh player.</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(PlayerPrefsKey);
        PlayerPrefs.Save();
    }
}
