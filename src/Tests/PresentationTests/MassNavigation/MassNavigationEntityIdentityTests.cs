using Arch.Core;
using Ludots.Core.MassNavigation.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationEntityIdentityTests
{
    [Test]
    public void AgentState_SeparatesVersionReusedEntities()
    {
        using var world = World.Create();
        Entity retired = world.Create();
        world.Destroy(retired);
        Entity replacement = world.Create();

        Assert.That(replacement.Id, Is.EqualTo(retired.Id));
        Assert.That(replacement.Version, Is.Not.EqualTo(retired.Version));

        var state = new MassNavigationAgentState(agentCapacity: 2);
        state.RegisterAgentAtIndex(retired, agentIndex: 0, controllable: true);
        state.RegisterAgentAtIndex(replacement, agentIndex: 1, controllable: true);

        Assert.That(state.SpawnedEntities, Has.Count.EqualTo(2));
        Assert.That(state.ControllableAgentCount, Is.EqualTo(2));
        Assert.That(state.TryGetControllableIndex(retired, out int retiredIndex), Is.True);
        Assert.That(state.TryGetControllableIndex(replacement, out int replacementIndex), Is.True);
        Assert.That(retiredIndex, Is.EqualTo(0));
        Assert.That(replacementIndex, Is.EqualTo(1));
    }

    [Test]
    public void AgentState_SeparatesEntitiesFromDifferentWorlds()
    {
        Entity first = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id: 7, worldId: 11, version: 1);
        Entity second = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id: 7, worldId: 12, version: 1);

        var state = new MassNavigationAgentState(agentCapacity: 2);
        state.RegisterAgentAtIndex(first, agentIndex: 0, controllable: true);
        state.RegisterAgentAtIndex(second, agentIndex: 1, controllable: true);

        Assert.That(state.SpawnedEntities, Has.Count.EqualTo(2));
        Assert.That(state.ControllableAgentCount, Is.EqualTo(2));
        Assert.That(state.TryGetControllableIndex(first, out int firstIndex), Is.True);
        Assert.That(state.TryGetControllableIndex(second, out int secondIndex), Is.True);
        Assert.That(firstIndex, Is.EqualTo(0));
        Assert.That(secondIndex, Is.EqualTo(1));
    }

    [Test]
    public void IdentityHash_ChangesWhenAnyEntityIdentityFieldChanges()
    {
        Entity baseline = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id: 7, worldId: 11, version: 1);
        Entity differentId = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id: 8, worldId: 11, version: 1);
        Entity differentWorld = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id: 7, worldId: 12, version: 1);
        Entity differentVersion = Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id: 7, worldId: 11, version: 2);

        long baselineHash = MassNavigationEntityIdentityHash.Mix(1469598103934665603L, baseline);

        Assert.That(
            MassNavigationEntityIdentityHash.Mix(1469598103934665603L, differentId),
            Is.Not.EqualTo(baselineHash));
        Assert.That(
            MassNavigationEntityIdentityHash.Mix(1469598103934665603L, differentWorld),
            Is.Not.EqualTo(baselineHash));
        Assert.That(
            MassNavigationEntityIdentityHash.Mix(1469598103934665603L, differentVersion),
            Is.Not.EqualTo(baselineHash));
    }
}
