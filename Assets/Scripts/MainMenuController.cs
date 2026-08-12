using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Main menu: choose a display name, then play.
///
/// The name is captured here rather than on the win screen on purpose. Asking
/// for it at the end means the moment the run finishes is spent typing instead
/// of submitting, and a slow typist's score sits in memory waiting to be lost
/// to a page refresh.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // Matches the scene name you will create/save: Assets/Scenes/Level01.unity
    [SerializeField] private string levelSceneName = "Level01";

    [Header("Player name (all optional — leave empty to skip)")]
    [Tooltip("Where the player types their leaderboard name.")]
    [SerializeField] private TMP_InputField nameInput;

    [Tooltip("Shows validation errors, e.g. a name with punctuation in it.")]
    [SerializeField] private TMP_Text nameStatusText;

    private void Start()
    {
        if (nameInput == null)
        {
            return;
        }

        // Character limit enforced by the field itself, so the player is
        // stopped at 32 rather than told off after typing 40. The server still
        // checks — the UI is a courtesy, not the rule.
        nameInput.characterLimit = PlayerIdentity.MaxNameLength;

        // Pre-fill with the saved name so a returning player never retypes it.
        // In a Web build this survives a page reload, because PlayerPrefs is
        // backed by browser storage for this origin.
        if (PlayerIdentity.HasName)
        {
            nameInput.text = PlayerIdentity.Name;
        }

        // Saving on every keystroke would be wasteful; saving on submit only
        // would lose the name if the player clicks Play without pressing Enter.
        // onEndEdit fires on both blur and Enter, which covers it.
        nameInput.onEndEdit.AddListener(_ => SaveName());

        SetStatus(string.Empty);
    }

    private void OnDestroy()
    {
        if (nameInput != null)
        {
            nameInput.onEndEdit.RemoveAllListeners();
        }
    }

    /// <summary>
    /// Validate and persist whatever is currently in the field. Returns false
    /// if the name was rejected, and puts the reason on screen.
    /// </summary>
    public bool SaveName()
    {
        if (nameInput == null)
        {
            return true; // no field wired: fall back to the default name
        }

        if (string.IsNullOrWhiteSpace(nameInput.text))
        {
            SetStatus(string.Empty);
            return true; // an empty box is not an error; it just means "anonymous"
        }

        if (!PlayerIdentity.TrySetName(nameInput.text, out string error))
        {
            SetStatus(error);
            return false;
        }

        SetStatus($"Playing as {PlayerIdentity.Name}");
        return true;
    }

    // Hook this up to the UI Button's OnClick().
    public void Play()
    {
        // A bad name blocks the start rather than silently submitting scores
        // under "anonymous". Failing at the menu is cheap; failing after a
        // good run is not.
        if (!SaveName())
        {
            return;
        }

        // Prefer the central GameManager if present.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayLevel01();
            return;
        }

        // Fallback (useful while you're still wiring things up).
        SceneManager.LoadScene(levelSceneName);
    }

    private void SetStatus(string message)
    {
        if (nameStatusText != null)
        {
            nameStatusText.text = message;
        }
    }
}
