using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Covers the four invariants the win/lose flow depends on. Each of these
/// guards against a bug that is genuinely awkward to catch by playing the game,
/// because it needs a specific and fairly rare sequence of events.
/// </summary>
public class GameStateMachineTests
{
    private GameStateMachine machine;
    private List<GameManager.GameState> raised;

    [SetUp]
    public void SetUp()
    {
        machine = new GameStateMachine();
        raised = new List<GameManager.GameState>();
        machine.StateChanged += state => raised.Add(state);
    }

    [Test]
    public void StartsInPlaying()
    {
        Assert.AreEqual(GameManager.GameState.Playing, machine.State);
    }

    [Test]
    public void Win_FromPlaying_MovesToWon()
    {
        bool changed = machine.Win();

        Assert.IsTrue(changed, "Win() from Playing should report that it changed the state.");
        Assert.AreEqual(GameManager.GameState.Won, machine.State);
        CollectionAssert.AreEqual(new[] { GameManager.GameState.Won }, raised);
    }

    [Test]
    public void Win_CalledTwice_RaisesStateChangedOnce()
    {
        // Two colliders on the nest, or a trigger firing on consecutive frames,
        // would otherwise show the win screen twice.
        machine.Win();
        bool secondCall = machine.Win();

        Assert.IsFalse(secondCall, "The second Win() should be ignored.");
        Assert.AreEqual(1, raised.Count, "StateChanged should only fire once.");
        Assert.AreEqual(GameManager.GameState.Won, machine.State);
    }

    [Test]
    public void Lose_AfterWin_IsIgnored()
    {
        // Clipping a hazard on the same frame you reach the nest must not
        // turn a win into a Game Over.
        machine.Win();
        bool lost = machine.Lose();

        Assert.IsFalse(lost, "Lose() after a win should be ignored.");
        Assert.AreEqual(GameManager.GameState.Won, machine.State, "The win should stand.");
        Assert.AreEqual(1, raised.Count);
    }

    [Test]
    public void ResetToPlaying_AfterLoss_ReturnsToPlaying()
    {
        // GameManager calls this on every sceneLoaded. Without it, restarting
        // after a death reloads the level still flagged as Lost and the Game
        // Over panel reappears immediately.
        machine.Lose();
        machine.ResetToPlaying();

        Assert.AreEqual(GameManager.GameState.Playing, machine.State);
        CollectionAssert.AreEqual(
            new[] { GameManager.GameState.Lost, GameManager.GameState.Playing },
            raised);
    }

    [Test]
    public void AfterReset_WinIsPossibleAgain()
    {
        machine.Lose();
        machine.ResetToPlaying();

        Assert.IsTrue(machine.Win(), "A fresh attempt should be winnable.");
        Assert.AreEqual(GameManager.GameState.Won, machine.State);
    }
}
