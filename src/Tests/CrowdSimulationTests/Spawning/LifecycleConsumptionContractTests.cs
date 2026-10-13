using System;
using Arch.Core;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
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

    [Test]
    public void DeclareNoConsumer_AfterLifecycleSystem_Throws()
    {
        using World world = World.Create();
        _ = new PresentationEntityLifecycleSystem(world, new PresentationEventStream(4));
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            PresentationLifecycleConsumption.DeclareNoConsumer(world))!;
        Assert.That(ex.Message, Does.Contain("LIFECYCLE.ERR.ConsumerAlreadyInstalled"));
    }

    [Test]
    public void LifecycleSystem_AfterDeclareNoConsumer_Throws()
    {
        using World world = World.Create();
        PresentationLifecycleConsumption.DeclareNoConsumer(world);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new PresentationEntityLifecycleSystem(world, new PresentationEventStream(4)))!;
        Assert.That(ex.Message, Does.Contain("LIFECYCLE.ERR.NoConsumerDeclared"));
    }

    [Test]
    public void DeclareNoConsumer_AfterFinalizeSystem_Throws()
    {
        using World world = World.Create();
        _ = new PresentationEntityFinalizeDestroySystem(world);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            PresentationLifecycleConsumption.DeclareNoConsumer(world))!;
        Assert.That(ex.Message, Does.Contain("LIFECYCLE.ERR.ConsumerAlreadyInstalled"));
    }

    [Test]
    public void FinalizeSystem_AfterDeclareNoConsumer_Throws()
    {
        using World world = World.Create();
        PresentationLifecycleConsumption.DeclareNoConsumer(world);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new PresentationEntityFinalizeDestroySystem(world))!;
        Assert.That(ex.Message, Does.Contain("LIFECYCLE.ERR.NoConsumerDeclared"));
    }

    [Test]
    public void BothLifecycleSystems_InstallOnTheSameWorld()
    {
        using World world = World.Create();
        _ = new PresentationEntityLifecycleSystem(world, new PresentationEventStream(4));
        Assert.DoesNotThrow(() => new PresentationEntityFinalizeDestroySystem(world));
        Assert.That(PresentationLifecycleConsumption.HasNoConsumer(world), Is.False);
    }
}
