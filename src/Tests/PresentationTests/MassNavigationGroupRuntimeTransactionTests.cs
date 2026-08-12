using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationGroupRuntimeTransactionTests
{
    [Test]
    public void NewOrderGroupCapacityFailure_PreservesExistingGroupMembershipAndTargets()
    {
        MassNavigationProfileRegistry.Reset();
        using World world = World.Create();
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.ScenarioRuntime.RuntimeCapacity.NavigationGroupCapacity = 1;
        config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 2;
        config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = 2;

        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(simulation));

        int profileId = MassNavigationProfileRegistry.Register("test.massNavigation.groupTransaction");
        Entity first = world.Create(new MassNavigationAgent { ProfileId = profileId });
        Entity second = world.Create(new MassNavigationAgent { ProfileId = profileId });
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        simulation.RebuildFromAuthoredAgents(
            world,
            new[] { first, second },
            new[]
            {
                new MassNavigationAgentSeed(1, 1_000f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
                new MassNavigationAgentSeed(1, 1_200f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
            },
            new[] { true, true });

        int[] originalMembers = { 0, 1 };
        MassNavigationOrderChainTests.CommitPreparedOrderMove(
            simulation,
            orderToken: 101,
            originalMembers,
            teamId: 1,
            destinationWorldCm: new Vector2(4_000f, 4_000f));
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float firstBeforeX, out float firstBeforeY), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(1, out float secondBeforeX, out float secondBeforeY), Is.True);

        int[] splitMembers = { 0 };
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            MassNavigationOrderChainTests.CommitPreparedOrderMove(
                simulation,
                orderToken: 202,
                splitMembers,
                teamId: 1,
                destinationWorldCm: new Vector2(5_000f, 5_000f)))!;

        Assert.That(ex.Message, Does.Contain("navigationGroupCapacity"));
        Assert.That(simulation.NavGroupRuntime.TryGetOrderGroup(101, out _), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetOrderGroup(202, out _), Is.False);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float firstAfterX, out float firstAfterY), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(1, out float secondAfterX, out float secondAfterY), Is.True);
        Assert.That(firstAfterX, Is.EqualTo(firstBeforeX));
        Assert.That(firstAfterY, Is.EqualTo(firstBeforeY));
        Assert.That(secondAfterX, Is.EqualTo(secondBeforeX));
        Assert.That(secondAfterY, Is.EqualTo(secondBeforeY));
    }

    [Test]
    public void ExistingOrderInvalidReplacementMember_PreservesExistingGroupMembershipAndTargets()
    {
        MassNavigationProfileRegistry.Reset();
        using World world = World.Create();
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.ScenarioRuntime.RuntimeCapacity.NavigationGroupCapacity = 2;
        config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 3;
        config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = 2;

        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(simulation));

        int profileId = MassNavigationProfileRegistry.Register("test.massNavigation.groupReplacement");
        Entity first = world.Create(new MassNavigationAgent { ProfileId = profileId });
        Entity second = world.Create(new MassNavigationAgent { ProfileId = profileId });
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        simulation.RebuildFromAuthoredAgents(
            world,
            new[] { first, second },
            new[]
            {
                new MassNavigationAgentSeed(1, 1_000f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
                new MassNavigationAgentSeed(1, 1_200f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
            },
            new[] { true, true });

        int[] originalMembers = { 0, 1 };
        MassNavigationOrderChainTests.CommitPreparedOrderMove(
            simulation,
            orderToken: 101,
            originalMembers,
            teamId: 1,
            destinationWorldCm: new Vector2(4_000f, 4_000f));
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float firstBeforeX, out float firstBeforeY), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(1, out float secondBeforeX, out float secondBeforeY), Is.True);

        int[] replacementMembers = { 0, 2 };
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            MassNavigationOrderChainTests.CommitPreparedOrderMove(
                simulation,
                orderToken: 101,
                replacementMembers,
                teamId: 1,
                destinationWorldCm: new Vector2(5_000f, 5_000f)))!;

        Assert.That(ex.Message, Does.Contain("not bound to a live entity"));
        Assert.That(simulation.NavGroupRuntime.TryGetOrderGroup(101, out _), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float firstAfterX, out float firstAfterY), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(1, out float secondAfterX, out float secondAfterY), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(2, out _, out _), Is.False);
        Assert.That(firstAfterX, Is.EqualTo(firstBeforeX));
        Assert.That(firstAfterY, Is.EqualTo(firstBeforeY));
        Assert.That(secondAfterX, Is.EqualTo(secondBeforeX));
        Assert.That(secondAfterY, Is.EqualTo(secondBeforeY));
    }

    [Test]
    public void ExistingOrderGroupMemberCapacityFailure_PreservesExistingGroupMembershipAndTargets()
    {
        MassNavigationProfileRegistry.Reset();
        using World world = World.Create();
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.ScenarioRuntime.RuntimeCapacity.NavigationGroupCapacity = 2;
        config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 3;
        config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = 1;

        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(simulation));

        int profileId = MassNavigationProfileRegistry.Register("test.massNavigation.groupMemberCapacity");
        Entity first = world.Create(new MassNavigationAgent { ProfileId = profileId });
        Entity second = world.Create(new MassNavigationAgent { ProfileId = profileId });
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        simulation.RebuildFromAuthoredAgents(
            world,
            new[] { first, second },
            new[]
            {
                new MassNavigationAgentSeed(1, 1_000f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
                new MassNavigationAgentSeed(1, 1_200f, 1_000f, false, 1f, 1f, 20f, 800f, layer),
            },
            new[] { true, true });

        int[] originalMembers = { 0 };
        MassNavigationOrderChainTests.CommitPreparedOrderMove(
            simulation,
            orderToken: 101,
            originalMembers,
            teamId: 1,
            destinationWorldCm: new Vector2(4_000f, 4_000f));
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float firstBeforeX, out float firstBeforeY), Is.True);

        int[] replacementMembers = { 0, 1 };
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            MassNavigationOrderChainTests.CommitPreparedOrderMove(
                simulation,
                orderToken: 101,
                replacementMembers,
                teamId: 1,
                destinationWorldCm: new Vector2(5_000f, 5_000f)))!;

        Assert.That(ex.Message, Does.Contain("groupMemberCapacity"));
        Assert.That(simulation.NavGroupRuntime.TryGetOrderGroup(101, out _), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float firstAfterX, out float firstAfterY), Is.True);
        Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(1, out _, out _), Is.False);
        Assert.That(firstAfterX, Is.EqualTo(firstBeforeX));
        Assert.That(firstAfterY, Is.EqualTo(firstBeforeY));
    }
}
