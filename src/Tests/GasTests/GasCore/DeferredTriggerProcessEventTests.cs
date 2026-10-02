using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class DeferredTriggerProcessEventTests
    {
        [Test]
        public void DeferredTriggerProcessSystem_AttributeChanged_PublishesMappedEventTag()
        {
            using var world = World.Create();
            var bus = new GameplayEventBus();
            var queue = new DeferredTriggerQueue();

            int healthId = AttributeRegistry.Register("Health");
            int evtTagId = TagRegistry.Register("Event.Attribute.Health.Changed");
            AttributeEventTagRegistry.Register(healthId, evtTagId);

            var target = world.Create();
            queue.EnqueueAttributeChanged(new AttributeChangedTrigger
            {
                Target = target,
                AttributeId = healthId,
                OldValue = 10f,
                NewValue = 20f
            });

            var system = new DeferredTriggerProcessSystem(world, queue, bus);
            system.Update(0.016f);
            bus.Update();

            That(bus.Events.Count, Is.EqualTo(1));
            That(bus.Events[0].TagId, Is.EqualTo(evtTagId));
            That(bus.Events[0].Source, Is.EqualTo(target));
            That(bus.Events[0].Target, Is.EqualTo(target));
            That(bus.Events[0].Magnitude, Is.EqualTo(20f));
        }

        [Test]
        public void DeferredTriggerProcessSystem_AttributeChanged_WithoutMapping_DoesNotPublish()
        {
            using var world = World.Create();
            var bus = new GameplayEventBus();
            var queue = new DeferredTriggerQueue();

            int energyId = AttributeRegistry.Register("Energy");
            var target = world.Create();
            queue.EnqueueAttributeChanged(new AttributeChangedTrigger
            {
                Target = target,
                AttributeId = energyId,
                OldValue = 1f,
                NewValue = 2f
            });

            var system = new DeferredTriggerProcessSystem(world, queue, bus);
            system.Update(0.016f);
            bus.Update();

            That(bus.Events.Count, Is.EqualTo(0));
        }
    }
}

