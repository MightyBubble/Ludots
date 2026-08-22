using System.Numerics;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Systems;
using Ludots.Core.Persistence;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class ReplayInputSnapshotTests
{
    [Test]
    public void ReplayActionsReplaceLiveSnapshotAndAreConsumedOnce()
    {
        var snapshot = new FrozenInputActionReader();
        snapshot.SetActionState("LiveOnly", Vector3.One, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
        var accumulator = new AuthoritativeInputAccumulator();
        var system = new AuthoritativeInputSnapshotSystem(snapshot, accumulator);
        var replay = new[]
        {
            new AuthoritativeAction("ReplayOnly", new Vector3(2f, 0f, 0f), true, true, false),
        };

        snapshot.QueueReplayActions(replay);
        system.Update(1f / 60f);

        Assert.That(snapshot.IsDown("LiveOnly"), Is.False);
        Assert.That(snapshot.IsDown("ReplayOnly"), Is.True);
        Assert.That(snapshot.ReadAction<float>("ReplayOnly"), Is.EqualTo(2f));

        system.Update(1f / 60f);

        Assert.That(snapshot.IsDown("ReplayOnly"), Is.False);
        Assert.That(snapshot.PressedThisFrame("ReplayOnly"), Is.False);
    }

    [Test]
    public void ClearingReplayActionsBeforeTheNextTickPreventsStaleInjection()
    {
        var snapshot = new FrozenInputActionReader();
        var system = new AuthoritativeInputSnapshotSystem(snapshot, new AuthoritativeInputAccumulator());
        snapshot.QueueReplayActions(new[]
        {
            new AuthoritativeAction("Stale", Vector3.UnitX, true, true, false),
        });
        snapshot.ClearReplayActions();

        system.Update(1f / 60f);

        Assert.That(snapshot.IsDown("Stale"), Is.False);
    }
}
