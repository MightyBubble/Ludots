using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ResponseChainListenerCacheTests
    {
        [Test]
        public void RuntimeListenerRegistration_RebuildsCacheAndAffectsNextRequest()
        {
            using var world = World.Create();
            const int healthAttributeId = 0;
            const int effectTagId = 71;
            const int effectTemplateId = 81;

            var modifiers = default(EffectModifiers);
            modifiers.Add(healthAttributeId, ModifierOp.Add, -10f);
            var templates = new EffectTemplateRegistry();
            templates.Register(effectTemplateId, new EffectTemplateData
            {
                TagId = effectTagId,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                ParticipatesInResponse = true,
                Modifiers = modifiers,
            });

            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(healthAttributeId, 100f);
            var queue = new EffectRequestQueue();
            var system = new EffectProposalProcessingSystem(
                world,
                queue,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: new TagOps());

            Publish(queue, target, effectTemplateId);
            system.Update(0f);

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthAttributeId), Is.EqualTo(90f));
            Assert.That(system.ListenerCacheRebuildCount, Is.EqualTo(1));

            Entity listenerEntity = world.Create();
            var listener = default(ResponseChainListener);
            listener.Add(effectTagId, ResponseType.Hook, priority: 100);
            ResponseChainListenerOps.Add(world, listenerEntity, in listener, queue);

            Publish(queue, target, effectTemplateId);
            system.Update(0f);

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthAttributeId), Is.EqualTo(90f));
            Assert.That(system.ListenerCacheRebuildCount, Is.EqualTo(2));
        }

        [Test]
        public void RemovedListener_IsExcludedFromNextRequest()
        {
            using var world = World.Create();
            var queue = new EffectRequestQueue();
            Entity listenerEntity = world.Create();
            var listener = default(ResponseChainListener);
            listener.Add(1, ResponseType.Hook, priority: 1);

            ResponseChainListenerOps.Add(world, listenerEntity, in listener, queue);
            int afterAdd = queue.ResponseChainListenerRevision;
            ResponseChainListenerOps.Remove(world, listenerEntity, queue);

            Assert.That(world.Has<ResponseChainListener>(listenerEntity), Is.False);
            Assert.That(queue.ResponseChainListenerRevision, Is.EqualTo(afterAdd + 1));
        }

        private static void Publish(EffectRequestQueue queue, Entity target, int effectTemplateId)
        {
            queue.Publish(new EffectRequest
            {
                Source = target,
                Target = target,
                TemplateId = effectTemplateId,
            });
        }
    }
}
