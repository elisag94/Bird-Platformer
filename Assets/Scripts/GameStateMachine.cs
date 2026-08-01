using System;

/// <summary>
/// The win/lose rules, as a plain C# class with no MonoBehaviour attached.
///
/// This exists so the rules can be tested directly. A MonoBehaviour's Awake
/// doesn't run in EditMode tests, so anything living inside GameManager could
/// only be tested by spinning up play mode — slow, and awkward for logic this
/// simple. Pulling it out means the tests are plain NUnit and run instantly.
///
/// GameManager owns one of these and forwards to it.
/// </summary>
public class GameStateMachine
{
    public GameManager.GameState State { get; private set; } = GameManager.GameState.Playing;

    public event Action<GameManager.GameState> StateChanged;

    /// <returns>true if the state actually changed, false if the game was already over.</returns>
    public bool Win()
    {
        return TryFinish(GameManager.GameState.Won);
    }

    /// <returns>true if the state actually changed, false if the game was already over.</returns>
    public bool Lose()
    {
        return TryFinish(GameManager.GameState.Lost);
    }

    /// <summary>Called on every scene load, so a restart begins cleanly.</summary>
    public void ResetToPlaying()
    {
        Set(GameManager.GameState.Playing);
    }

    // Win and Lose are only legal from Playing. This is what stops the goal
    // trigger firing twice, and what stops a hazard touched on the victory
    // frame from flipping a win into a loss.
    private bool TryFinish(GameManager.GameState target)
    {
        if (State != GameManager.GameState.Playing)
        {
            return false;
        }

        Set(target);
        return true;
    }

    private void Set(GameManager.GameState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }
}
