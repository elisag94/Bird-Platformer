using System;

/// <summary>
/// Measures how long a run takes, as a plain C# class with no MonoBehaviour
/// attached — same reasoning as GameStateMachine. The rules can then be tested
/// with plain NUnit in EditMode instead of needing play mode to spin up.
///
/// GameManager owns one of these and feeds it Time.deltaTime.
///
/// WHY ACCUMULATE deltaTime RATHER THAN SUBTRACT TIMESTAMPS:
/// The obvious approach is to record Time.time at the start and subtract at the
/// end. That measures WALL CLOCK time, which counts every second the game was
/// paused, sitting on a menu, or stalled in a background browser tab.
/// Accumulating deltaTime only advances while something is actually ticking us,
/// so pauses are excluded for free — with Time.timeScale = 0, deltaTime is 0.
///
/// Time is kept as a double. A float has ~7 significant digits, so by the time
/// a run reaches a few minutes the accumulated rounding error is visible in the
/// milliseconds — and milliseconds are the whole point of a speedrun timer.
/// </summary>
public class RunTimer
{
    private double elapsedSeconds;

    /// <summary>True while the timer is accumulating. False once stopped.</summary>
    public bool IsRunning { get; private set; } = true;

    public double ElapsedSeconds => elapsedSeconds;

    /// <summary>
    /// The value sent to the leaderboard API. Integer milliseconds, never a
    /// float: floats make ties and sorting subtly wrong, and a UI showing
    /// 42.31000000001 looks like a bug.
    /// </summary>
    public int ElapsedMilliseconds
    {
        get
        {
            double ms = Math.Round(elapsedSeconds * 1000.0);

            // A run long enough to overflow an int would be ~24 days, so this
            // is paranoia rather than expectation — but silently wrapping
            // negative is a far worse failure than clamping.
            if (ms > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)ms;
        }
    }

    /// <summary>
    /// Advance the timer. Ignored when stopped, so the caller can tick
    /// unconditionally every frame without checking state first.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        if (!IsRunning)
        {
            return;
        }

        // A negative delta should be impossible from Time.deltaTime, but
        // guarding here means a bad caller can never make a run look faster
        // than it was.
        if (deltaSeconds <= 0.0)
        {
            return;
        }

        elapsedSeconds += deltaSeconds;
    }

    /// <summary>Freeze the clock. Called when the run ends, win or lose.</summary>
    public void Stop()
    {
        IsRunning = false;
    }

    /// <summary>
    /// Resume without clearing. Not used by the current game loop, but it makes
    /// the class honest: Stop is a pause, and Reset is what clears.
    /// </summary>
    public void Resume()
    {
        IsRunning = true;
    }

    /// <summary>Back to zero and running. Called on every scene load.</summary>
    public void Reset()
    {
        elapsedSeconds = 0.0;
        IsRunning = true;
    }

    /// <summary>
    /// Format for display: "1:23.456", or "23.456" under a minute.
    ///
    /// Static and taking a plain int so the win screen and the leaderboard
    /// panel can both use it — the leaderboard formats durations that came
    /// back from the API and never went through a RunTimer at all.
    /// </summary>
    public static string Format(int milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        int totalSeconds = milliseconds / 1000;
        int ms = milliseconds % 1000;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        if (minutes > 0)
        {
            return $"{minutes}:{seconds:D2}.{ms:D3}";
        }

        return $"{seconds}.{ms:D3}";
    }
}
