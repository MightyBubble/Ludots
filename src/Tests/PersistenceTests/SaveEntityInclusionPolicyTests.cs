using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Persistence;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class SaveEntityInclusionPolicyTests
{
    [Test]
    public void DefaultPolicyIncludesPersistentEntitiesAndExcludesEventOrExplicitlyExcludedEntities()
    {
        using World world = World.Create();
        Entity persistent = world.Create(new Name { Value = "persistent" });
        Entity gameplayEvent = world.Create(new GameplayEvent { TagId = 7, Source = persistent, Target = persistent });
        Entity budgetFuseEvent = world.Create(new SimulationBudgetFuseEvent { LogicTick = 3, BudgetMs = 4, SliceLimit = 5, Reason = 1 });
        Entity explicitlyExcluded = world.Create(new Name { Value = "transient" }, new SaveExcludedTag());

        var policy = SaveEntityInclusionPolicy.Default;

        Assert.That(policy.ShouldInclude(world, persistent), Is.True);
        Assert.That(policy.ShouldInclude(world, gameplayEvent), Is.False);
        Assert.That(policy.ShouldInclude(world, budgetFuseEvent), Is.False);
        Assert.That(policy.ShouldInclude(world, explicitlyExcluded), Is.False);
    }

    [Test]
    public void DefaultPolicyExcludesPresentationDestroyPendingEntitiesAtSnapshotBoundary()
    {
        using World world = World.Create();
        Entity alive = world.Create(new Name { Value = "alive" });
        Entity pendingDestroy = world.Create(new Name { Value = "pending-destroy" }, new PresentationDestroyPending());

        var policy = SaveEntityInclusionPolicy.Default;

        Assert.That(policy.ShouldInclude(world, alive), Is.True);
        Assert.That(policy.ShouldInclude(world, pendingDestroy), Is.False);
    }

    [Test]
    public void SnapshotBoundaryRejectsIncompleteSimulationStep()
    {
        var boundary = SaveSnapshotBoundary.InProgress(SystemGroup.Cleanup);

        var error = Assert.Throws<SaveContextException>(() => boundary.EnsureClean());

        Assert.That(error!.Message, Does.Contain("clean tick boundary"));
        Assert.That(error.Message, Does.Contain(SystemGroup.Cleanup.ToString()));
    }

    [Test]
    public void SnapshotBoundaryAcceptsCompletedClearPresentationFlagsPhase()
    {
        var boundary = SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags);

        Assert.DoesNotThrow(() => boundary.EnsureClean());
    }
}
