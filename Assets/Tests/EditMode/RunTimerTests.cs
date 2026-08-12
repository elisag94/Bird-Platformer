using NUnit.Framework;

/// <summary>
/// Plain NUnit, no play mode. RunTimer has no Unity dependencies at all, which
/// is the entire reason it isn't a MonoBehaviour.
/// </summary>
public class RunTimerTests
{
    [Test]
    public void NewTimer_StartsAtZeroAndRunning()
    {
        var timer = new RunTimer();

        Assert.AreEqual(0, timer.ElapsedMilliseconds);
        Assert.IsTrue(timer.IsRunning);
    }

    [Test]
    public void Tick_AccumulatesElapsedTime()
    {
        var timer = new RunTimer();

        timer.Tick(0.5);
        timer.Tick(0.25);

        Assert.AreEqual(750, timer.ElapsedMilliseconds);
    }

    [Test]
    public void Tick_AfterStop_DoesNotAccumulate()
    {
        var timer = new RunTimer();
        timer.Tick(1.0);

        timer.Stop();
        timer.Tick(5.0);

        // This is the one that matters: the run ended when the bird reached the
        // nest, not when the player finally clicked away from the win screen.
        Assert.AreEqual(1000, timer.ElapsedMilliseconds);
    }

    [Test]
    public void Stop_SetsIsRunningFalse()
    {
        var timer = new RunTimer();

        timer.Stop();

        Assert.IsFalse(timer.IsRunning);
    }

    [Test]
    public void Resume_ContinuesFromWhereItStopped()
    {
        var timer = new RunTimer();
        timer.Tick(2.0);
        timer.Stop();
        timer.Tick(99.0);

        timer.Resume();
        timer.Tick(1.0);

        Assert.AreEqual(3000, timer.ElapsedMilliseconds);
    }

    [Test]
    public void Reset_ClearsElapsedAndResumes()
    {
        var timer = new RunTimer();
        timer.Tick(10.0);
        timer.Stop();

        timer.Reset();

        Assert.AreEqual(0, timer.ElapsedMilliseconds);
        Assert.IsTrue(timer.IsRunning);
    }

    [Test]
    public void Tick_IgnoresZeroAndNegativeDeltas()
    {
        var timer = new RunTimer();
        timer.Tick(1.0);

        timer.Tick(0.0);
        timer.Tick(-5.0);

        // A negative delta must never be able to make a run look faster.
        Assert.AreEqual(1000, timer.ElapsedMilliseconds);
    }

    [Test]
    public void ElapsedMilliseconds_RoundsRatherThanTruncates()
    {
        var timer = new RunTimer();

        // 0.0006 s is 0.6 ms — truncation would report 0.
        timer.Tick(0.0006);

        Assert.AreEqual(1, timer.ElapsedMilliseconds);
    }

    [Test]
    public void ManySmallTicks_DoNotDriftMeaningfully()
    {
        var timer = new RunTimer();

        // 60 seconds at 120 fps. With a float accumulator this drifts by
        // several milliseconds; with a double it does not.
        for (int i = 0; i < 7200; i++)
        {
            timer.Tick(1.0 / 120.0);
        }

        Assert.AreEqual(60000, timer.ElapsedMilliseconds);
    }

    [Test]
    public void Format_UnderOneMinute_OmitsMinutes()
    {
        Assert.AreEqual("42.310", RunTimer.Format(42310));
    }

    [Test]
    public void Format_OverOneMinute_PadsSecondsAndMilliseconds()
    {
        Assert.AreEqual("1:23.456", RunTimer.Format(83456));
        Assert.AreEqual("2:05.007", RunTimer.Format(125007));
    }

    [Test]
    public void Format_HandlesZeroAndNegative()
    {
        Assert.AreEqual("0.000", RunTimer.Format(0));
        Assert.AreEqual("0.000", RunTimer.Format(-1));
    }
}
