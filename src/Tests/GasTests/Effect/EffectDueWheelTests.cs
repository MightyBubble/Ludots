using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// EffectDueWheel（GAS 效果到期时间轮 A 档）单元与系统级回归：
    /// 桶环到期顺序、双 kind 合并、幂等替换、取消、溢出道晋升、tick 回退重建、
    /// 惰性删除、遗留道分类，以及 30K 远期注册的稳态 AdvanceTo 微基准。
    /// </summary>
    [TestFixture]
    public sealed class EffectDueWheelTests
    {
        private const int SnapshotCapacity = 64;
        private const int FanOutCapacity = 64;

        private static readonly QueryDescription EffectQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext>();

        [Test]
        public void RegisterAdvanceTo_DeliversEffectsInDueTickOrder()
        {
            using var world = World.Create();
            var wheel = new EffectDueWheel(new DiscreteClock(), SnapshotCapacity);
            wheel.Rebuild(world);
            Entity early = world.Create();
            Entity middle = world.Create();
            Entity late = world.Create();

            wheel.Register(middle, 2, EffectDueKind.PeriodDue);
            wheel.Register(early, 1, EffectDueKind.PeriodDue);
            wheel.Register(late, 3, EffectDueKind.PeriodDue);

            var due = new List<Entity>();
            wheel.AdvanceTo(1, due);
            That(due, Is.EqualTo(new[] { early }));
            wheel.AdvanceTo(2, due);
            That(due, Is.EqualTo(new[] { middle }));
            wheel.AdvanceTo(3, due);
            That(due, Is.EqualTo(new[] { late }));
            That(wheel.Count, Is.Zero);
        }

        [Test]
        public void SameEffectDualKindSameTick_VisitedOnce_BothRegistrationsConsumed()
        {
            using var world = World.Create();
            var wheel = new EffectDueWheel(new DiscreteClock(), SnapshotCapacity);
            wheel.Rebuild(world);
            Entity effect = world.Create();

            wheel.Register(effect, 7, EffectDueKind.PeriodDue);
            wheel.Register(effect, 7, EffectDueKind.ExpireDue);
            That(wheel.Count, Is.EqualTo(2));

            var due = new List<Entity>();
            wheel.AdvanceTo(7, due);
            That(due, Is.EqualTo(new[] { effect }));
            That(wheel.Count, Is.Zero);
        }

        [Test]
        public void Reregistration_IdempotentlyReplacesDueTick()
        {
            using var world = World.Create();
            var wheel = new EffectDueWheel(new DiscreteClock(), SnapshotCapacity);
            wheel.Rebuild(world);
            Entity effect = world.Create();

            wheel.Register(effect, 10, EffectDueKind.ExpireDue);
            wheel.Register(effect, 10, EffectDueKind.ExpireDue);
            That(wheel.Count, Is.EqualTo(1));

            wheel.Register(effect, 20, EffectDueKind.ExpireDue);
            That(wheel.Count, Is.EqualTo(1));

            var due = new List<Entity>();
            wheel.AdvanceTo(10, due);
            That(due, Is.Empty);
            wheel.AdvanceTo(20, due);
            That(due, Is.EqualTo(new[] { effect }));
        }

        [Test]
        public void CancelledRegistration_IsNotDelivered()
        {
            using var world = World.Create();
            var wheel = new EffectDueWheel(new DiscreteClock(), SnapshotCapacity);
            wheel.Rebuild(world);
            Entity effect = world.Create();

            wheel.Register(effect, 5, EffectDueKind.ExpireDue);
            wheel.Cancel(effect, EffectDueKind.ExpireDue);
            That(wheel.Count, Is.Zero);

            var due = new List<Entity>();
            wheel.AdvanceTo(5, due);
            That(due, Is.Empty);
        }

        [Test]
        public void OverflowLaneEntry_ExpiresExactlyWhenWindowReachesIt()
        {
            using var world = World.Create();
            var wheel = new EffectDueWheel(new DiscreteClock(), SnapshotCapacity);
            wheel.Rebuild(world);
            Entity effect = world.Create();

            int farDue = EffectDueWheel.RingSize + 100;
            wheel.Register(effect, farDue, EffectDueKind.ExpireDue);
            That(wheel.Count, Is.EqualTo(1));

            var due = new List<Entity>();
            for (int tick = 1; tick < farDue; tick++)
            {
                wheel.AdvanceTo(tick, due);
                That(due, Is.Empty, $"tick {tick} must not deliver a far-future registration");
            }

            wheel.AdvanceTo(farDue, due);
            That(due, Is.EqualTo(new[] { effect }));
            That(wheel.Count, Is.Zero);
        }

        [Test]
        public void TickRegression_MarksDirty_AndRebuildResyncsWatermark()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            Entity effect = GameplayEffectFactory.CreateEffect(
                world, rootId: 1, source, target,
                durationTicks: 5, lifetimeKind: EffectLifetimeKind.After,
                clockId: GasClockId.FixedFrame);
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);

            using var lifetime = new EffectLifetimeSystem(
                world, clock, new GasConditionRegistry(),
                snapshotCapacity: SnapshotCapacity, fanOutCommandCapacity: FanOutCapacity);

            clock.Advance(ClockDomainId.FixedFrame, 10);
            lifetime.Update(0.016f);
            That(lifetime.DueWheel!.Watermark, Is.EqualTo(10));
            That(world.IsAlive(effect), Is.True);

            clock.RestoreSnapshot(new DiscreteClockSnapshot(
                new[] { new DiscreteClockDomainSnapshot(ClockDomainId.FixedFrame, 3) }));

            var wheelProbe = lifetime.DueWheel!;
            var dueProbe = new List<Entity>();
            wheelProbe.AdvanceTo(3, dueProbe);
            That(wheelProbe.IsDirty, Is.True, "clock regression below watermark must dirty the wheel");

            lifetime.Update(0.016f);
            That(lifetime.DueWheel!.IsDirty, Is.False);
            That(lifetime.DueWheel!.Watermark, Is.EqualTo(3));
            That(world.IsAlive(effect), Is.True);

            clock.Advance(ClockDomainId.FixedFrame, 12);
            lifetime.Update(0.016f);
            That(world.IsAlive(effect), Is.False, "effect must still expire at its restored-clock expiry tick");
        }

        [Test]
        public void DestroyedEffectEntity_BucketDeliveryIsDroppedWithoutThrow()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            Entity effect = GameplayEffectFactory.CreateEffect(
                world, rootId: 1, source, target,
                durationTicks: 3, lifetimeKind: EffectLifetimeKind.After,
                clockId: GasClockId.FixedFrame);
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);

            using var lifetime = new EffectLifetimeSystem(
                world, clock, new GasConditionRegistry(),
                snapshotCapacity: SnapshotCapacity, fanOutCommandCapacity: FanOutCapacity);

            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            That(lifetime.DueWheel!.Count, Is.EqualTo(1), "fast-lane After effect must hold one expire registration");

            world.Destroy(effect);

            clock.Advance(ClockDomainId.FixedFrame, 3);
            DoesNotThrow(() => lifetime.Update(0.016f));
            That(world.CountEntities(in EffectQuery), Is.Zero);
            That(lifetime.DueWheel!.Count, Is.Zero, "lazy deletion must consume the stale entry at bucket drain");
        }

        [Test]
        public void ExpireConditionAndAnchorlessInfiniteEffects_RideLegacyLane()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var conditions = new GasConditionRegistry();
            var tagOps = new TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new TagRuleRegistry());
            var keepAlive = conditions.Register(new GasCondition(GasConditionKind.TagPresent, 3, TagSense.Present));

            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer(), new DirtyFlags());
            world.Add(target, new GameplayTagContainer());
            world.Add(target, new TagCountContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            tagOps.AddTag(ref tags, ref counts, 3);

            Entity conditional = GameplayEffectFactory.CreateEffect(
                world, rootId: 1, source, target,
                durationTicks: 0, lifetimeKind: EffectLifetimeKind.Infinite,
                clockId: GasClockId.FixedFrame, expireCondition: keepAlive);
            world.Get<GameplayEffect>(conditional).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(conditional);

            Entity anchorless = GameplayEffectFactory.CreateEffect(
                world, rootId: 2, source, target,
                durationTicks: 0, lifetimeKind: EffectLifetimeKind.Infinite,
                clockId: GasClockId.FixedFrame);
            world.Get<GameplayEffect>(anchorless).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(anchorless);

            using var lifetime = new EffectLifetimeSystem(
                world, clock, conditions,
                snapshotCapacity: SnapshotCapacity, fanOutCommandCapacity: FanOutCapacity,
                tagOps: tagOps);

            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);

            var legacy = lifetime.DueWheel!.LegacyEffects;
            That(legacy.Count, Is.EqualTo(2));
            That(legacy, Does.Contain(conditional));
            That(legacy, Does.Contain(anchorless));
            That(lifetime.DueWheel!.Count, Is.Zero);
            That(world.IsAlive(conditional), Is.True);
            That(world.IsAlive(anchorless), Is.True);
        }

        [Test]
        public void FastLaneAfterEffect_ExpiresExactlyAtRestoredDueTick()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            Entity effect = GameplayEffectFactory.CreateEffect(
                world, rootId: 1, source, target,
                durationTicks: 2, lifetimeKind: EffectLifetimeKind.After,
                clockId: GasClockId.FixedFrame);
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);

            using var lifetime = new EffectLifetimeSystem(
                world, clock, new GasConditionRegistry(),
                snapshotCapacity: SnapshotCapacity, fanOutCommandCapacity: FanOutCapacity);

            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            That(world.Get<GameplayEffect>(effect).ExpiresAtTick, Is.EqualTo(3));
            That(world.IsAlive(effect), Is.True);

            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            That(world.IsAlive(effect), Is.True, "one tick before expiry the effect must survive");

            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            That(world.IsAlive(effect), Is.False, "at the expiry tick the fast-lane wheel must deliver the effect");
            That(lifetime.DueWheel!.Count, Is.Zero);
        }

        /// <summary>30K 远期注册的稳态推进：1000 次 AdvanceTo 平均耗时须远低于 0.05ms。</summary>
        [Test]
        public void SteadyStateThirtyThousandFarFutureRegistrations_AdvanceToStaysCheap()
        {
            const int count = 30_000;
            const int measuredTicks = 1000;
            using var world = World.Create();
            var entities = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                entities[i] = world.Create();
            }

            var wheel = new EffectDueWheel(new DiscreteClock(), count);
            wheel.Rebuild(world);
            int farDue = EffectDueWheel.RingSize * 4;
            for (int i = 0; i < count; i++)
            {
                wheel.Register(entities[i], farDue, (i & 1) == 0 ? EffectDueKind.PeriodDue : EffectDueKind.ExpireDue);
            }

            That(wheel.Count, Is.EqualTo(count));

            var due = new List<Entity>(count);
            for (int tick = 1; tick <= 200; tick++)
            {
                wheel.AdvanceTo(tick, due);
            }

            long start = Stopwatch.GetTimestamp();
            for (int tick = 201; tick <= 200 + measuredTicks; tick++)
            {
                wheel.AdvanceTo(tick, due);
            }

            double averageMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds / measuredTicks;
            TestContext.Out.WriteLine($"EffectDueWheel steady state: {count} far-future registrations, {measuredTicks} AdvanceTo calls, average {averageMs:F6} ms/call");
            That(averageMs, Is.LessThan(0.05), "steady-state AdvanceTo must eliminate full-scan cost");
            That(wheel.Count, Is.EqualTo(count), "far-future registrations must survive window advancement");
        }
    }
}
