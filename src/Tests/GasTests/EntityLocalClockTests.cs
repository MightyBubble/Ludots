using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class EntityLocalClockTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
        }

        [Test]
        public void EntityLocalClockSystem_CombinesConsumedGasStepsWithEntityScale()
        {
            using var world = World.Create();
            int timeScaleId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            var attributes = new AttributeBuffer();
            attributes.SetBase(timeScaleId, 2000f);
            Entity entity = world.Create(new EntityLocalClock(), attributes);

            var globalClock = new DiscreteClock();
            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 2);
            var globalSystem = new GasClockSystem(globalClock, policy);
            var localSystem = new EntityLocalClockSystem(world, policy, timeScaleId);

            for (int i = 0; i < 4; i++)
            {
                globalSystem.Update(0.016f);
                localSystem.Update(0.016f);
            }

            That(globalClock.Now(ClockDomainId.Step), Is.EqualTo(2));
            That(world.Get<EntityLocalClock>(entity).LocalStep, Is.EqualTo(4));
        }

        [Test]
        public void EntityLocalClockSystem_DoesNotAdvanceWithoutManualRequest()
        {
            using var world = World.Create();
            int timeScaleId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            var attributes = new AttributeBuffer();
            attributes.SetBase(timeScaleId, 1000f);
            Entity entity = world.Create(new EntityLocalClock(), attributes);

            var globalClock = new DiscreteClock();
            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1, mode: GasStepMode.Manual);
            var globalSystem = new GasClockSystem(globalClock, policy);
            var localSystem = new EntityLocalClockSystem(world, policy, timeScaleId);

            globalSystem.Update(0.016f);
            localSystem.Update(0.016f);
            That(world.Get<EntityLocalClock>(entity).LocalStep, Is.EqualTo(0));

            policy.RequestStep();
            globalSystem.Update(0.016f);
            localSystem.Update(0.016f);

            That(world.Get<EntityLocalClock>(entity).LocalStep, Is.EqualTo(1));
        }

        [Test]
        public void EntityLocalClockSystem_FreezeScaleDoesNotAdvance()
        {
            using var world = World.Create();
            int timeScaleId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            var attributes = new AttributeBuffer();
            attributes.SetBase(timeScaleId, 0f);
            Entity entity = world.Create(new EntityLocalClock(), attributes);

            var globalClock = new DiscreteClock();
            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1);
            var globalSystem = new GasClockSystem(globalClock, policy);
            var localSystem = new EntityLocalClockSystem(world, policy, timeScaleId);

            for (int i = 0; i < 3; i++)
            {
                globalSystem.Update(0.016f);
                localSystem.Update(0.016f);
            }

            That(globalClock.Now(ClockDomainId.Step), Is.EqualTo(3));
            That(world.Get<EntityLocalClock>(entity).LocalStep, Is.EqualTo(0));
        }

        [Test]
        public void EntityLocalClockSystem_ThrowsWhenAttributeBufferIsMissing()
        {
            using var world = World.Create();
            int timeScaleId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            world.Create(new EntityLocalClock());

            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1);
            var localSystem = new EntityLocalClockSystem(world, policy, timeScaleId);

            InvalidOperationException? error = Throws<InvalidOperationException>(() => localSystem.Update(0.016f));

            That(error!.Message, Does.Contain("AttributeBuffer.time.scale_permille"));
        }

        [Test]
        public void EntityLocalClockSystem_ThrowsWhenScaleAttributeIsMissing()
        {
            using var world = World.Create();
            int timeScaleId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            world.Create(new EntityLocalClock(), new AttributeBuffer());

            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1);
            var localSystem = new EntityLocalClockSystem(world, policy, timeScaleId);

            InvalidOperationException? error = Throws<InvalidOperationException>(() => localSystem.Update(0.016f));

            That(error!.Message, Does.Contain("AttributeBuffer.time.scale_permille"));
        }

        [Test]
        public void EntityLocalClockSystem_ThrowsWhenScaleAttributeExceedsMaximum()
        {
            using var world = World.Create();
            int timeScaleId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            var attributes = new AttributeBuffer();
            attributes.SetBase(timeScaleId, TimeFlowService.MaxScalePermille + 1f);
            world.Create(new EntityLocalClock(), attributes);

            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1);
            var localSystem = new EntityLocalClockSystem(world, policy, timeScaleId);

            InvalidOperationException? error = Throws<InvalidOperationException>(() => localSystem.Update(0.016f));

            That(error!.Message, Does.Contain($"<= {TimeFlowService.MaxScalePermille}"));
        }

        [Test]
        public void EffectLifetime_EntityLocal_ExpiresFromTargetLocalStep()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var lifetime = new EffectLifetimeSystem(world, clock, new GasConditionRegistry(), snapshotCapacity: 4096);
            Entity source = world.Create();
            Entity target = world.Create(new EntityLocalClock(), new ActiveEffectContainer());
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 3,
                lifetimeKind: EffectLifetimeKind.After,
                clockId: GasClockId.EntityLocal);
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);

            lifetime.Update(0.016f);
            world.Get<EntityLocalClock>(target).LocalStep = 2;
            lifetime.Update(0.016f);
            That(world.IsAlive(effect), Is.True);

            world.Get<EntityLocalClock>(target).LocalStep = 3;
            lifetime.Update(0.016f);
            That(world.IsAlive(effect), Is.False);
        }

        [Test]
        public void TimedTag_EntityLocal_ExpiresFromOwningEntityLocalStep()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            Entity entity = world.Create(
                new EntityLocalClock(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags());
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            ref var timed = ref world.Get<TimedTagBuffer>(entity);
            tags.AddTag(42);
            counts.AddCount(42, 1);
            timed.TryAdd(42, 2, GasClockId.EntityLocal);
            var system = new TimedTagExpirationSystem(world, clock, new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            world.Get<EntityLocalClock>(entity).LocalStep = 1;
            system.Update(0.016f);
            That(world.Get<GameplayTagContainer>(entity).HasTag(42), Is.True);

            world.Get<EntityLocalClock>(entity).LocalStep = 2;
            system.Update(0.016f);
            That(world.Get<GameplayTagContainer>(entity).HasTag(42), Is.False);
        }
    }
}
