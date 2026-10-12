using Arch.Core;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;

namespace CrowdSimulationTests.Spawning;

[TestFixture]
public class LifecycleConsumptionContractTests
{
    [Test]
    public void ConsumeEntity_WithoutDeclaration_StaysPending()
    {
        using World world = World.Create();
        Entity entity = world.Create(new PresentationStableId { Value = 1 });
        EntityLifecycleAtomicOps.ConsumeEntity(world, entity, "test");
        Assert.That(world.IsAlive(entity), Is.True);
        Assert.That(world.Has<PresentationDestroyPending>(entity), Is.True);
        Assert.That(world.Has<PresentationDestroyEventPublished>(entity), Is.False);
    }

    [Test]
    public void ConsumeEntity_DeclaredNoConsumer_DestroysImmediately()
    {
        using World world = World.Create();
        PresentationLifecycleConsumption.DeclareNoConsumer(world);
        Entity entity = world.Create(new PresentationStableId { Value = 1 });
        EntityLifecycleAtomicOps.ConsumeEntity(world, entity, "test");
        Assert.That(world.IsAlive(entity), Is.False);
        Assert.That(world.Size, Is.EqualTo(0));
    }
}
