using Ludots.Core.Gameplay;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.TimeFlowCore;

[TestFixture]
public sealed class AuthoritativeSimulationTickStateTests
{
    [Test]
    public void BeginAndCommit_PublishOnlyTheCompletedTick()
    {
        var ticks = new AuthoritativeSimulationTickState();

        Assert.Multiple(() =>
        {
            Assert.That(ticks.IsExecuting, Is.False);
            Assert.That(ticks.CommittedTick, Is.Zero);
        });

        ticks.Begin(1);

        Assert.Multiple(() =>
        {
            Assert.That(ticks.IsExecuting, Is.True);
            Assert.That(ticks.ExecutingTick, Is.EqualTo(1));
            Assert.That(ticks.CommittedTick, Is.Zero);
        });

        ticks.Commit(1);

        Assert.Multiple(() =>
        {
            Assert.That(ticks.IsExecuting, Is.False);
            Assert.That(ticks.CommittedTick, Is.EqualTo(1));
        });
    }

    [Test]
    public void Begin_RejectsDuplicateAndSkippedTicks()
    {
        var ticks = new AuthoritativeSimulationTickState();

        Assert.That(() => ticks.Begin(2), Throws.InvalidOperationException);

        ticks.Begin(1);

        Assert.That(() => ticks.Begin(1), Throws.InvalidOperationException);
    }

    [Test]
    public void Commit_RejectsMissingWrongAndDuplicateCommits()
    {
        var ticks = new AuthoritativeSimulationTickState();

        Assert.That(() => ticks.Commit(1), Throws.InvalidOperationException);

        ticks.Begin(1);

        Assert.That(() => ticks.Commit(2), Throws.InvalidOperationException);

        ticks.Commit(1);

        Assert.That(() => ticks.Commit(1), Throws.InvalidOperationException);
    }

    [Test]
    public void BeginCommit_SteadyStateDoesNotAllocate()
    {
        var ticks = new AuthoritativeSimulationTickState();
        ticks.Begin(1);
        ticks.Commit(1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 2; tick <= 10_001; tick++)
        {
            ticks.Begin(tick);
            ticks.Commit(tick);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void GameSessionSystem_CollectsInsideTickOpenedByFixedStepBoundary()
    {
        var session = new GameSession();
        var system = new GameSessionSystem(session);

        Assert.That(() => system.Update(1f / 30f), Throws.InvalidOperationException);

        session.BeginSimulationTick();
        system.Update(1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(session.SimulationTicks.ExecutingTick, Is.EqualTo(1));
            Assert.That(session.SimulationTicks.CommittedTick, Is.Zero);
            Assert.That(session.CurrentTick, Is.EqualTo(1));
        });

        session.CommitFixedUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(session.SimulationTicks.IsExecuting, Is.False);
            Assert.That(session.SimulationTicks.CommittedTick, Is.EqualTo(1));
            Assert.That(session.CurrentTick, Is.EqualTo(1));
        });
    }

    [Test]
    public void GameSessionFixedUpdate_RemainsASynchronousCompatibilityStep()
    {
        var session = new GameSession();

        session.FixedUpdate();
        session.FixedUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(session.SimulationTicks.IsExecuting, Is.False);
            Assert.That(session.CurrentTick, Is.EqualTo(2));
            Assert.That(session.SimulationTicks.CommittedTick, Is.EqualTo(2));
        });
    }

    [Test]
    public void GameSessionSnapshot_RejectsAnUncommittedTick()
    {
        var session = new GameSession();
        session.BeginSimulationTick();

        Assert.That(() => session.CaptureSnapshot(), Throws.InvalidOperationException);
    }
}
