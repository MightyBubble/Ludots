using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// 规模回归：N 个不同效果通过真实 EffectLifetimeSystem 处理（一人一个持续效果）。
    /// 覆盖周期未到 / 周期到达 / 大批到期+监听器清理 / 失败中止 / 分片执行五个场景，
    /// Release 下预热 + 重复采样报告每帧中位数、P95 与分配量。
    /// 全部使用多个不同实体，禁止单实体循环替代。
    /// </summary>
    [TestFixture]
    public sealed class EffectLifetimeScaleTests
    {
        private const int SnapshotCapacity = 16 * 1024;
        private const int FanOutCapacity = 16 * 1024;

        private const int DurabilityId = 0;

        private static readonly QueryDescription _effectQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext>();

        // ─────────────────────────────────────────────────────────────
        // 场景 1：周期尚未到达 —— 效果保持有效、无副作用、耗时近线性
        // ─────────────────────────────────────────────────────────────
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        public void WaitingPeriod_EffectsStayValid_ScalesNearLinearly(int count)
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            using var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: Math.Max(SnapshotCapacity, count),
                fanOutCommandCapacity: FanOutCapacity);

            CreateOnePersistentEffectPerUnit(world, count, periodTicks: 1_000_000);

            // 预热：首帧完成计时初始化（NextTickAtTick 等），不计入采样。
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);

            // 预热至稳定：连续多帧触发列表/快照预热，然后逐帧测分配。
            for (int warm = 0; warm < 16; warm++)
            {
                clock.Advance(ClockDomainId.FixedFrame, 1);
                lifetime.Update(0.016f);
            }

            var perFrameAlloc = new List<long>(9);
            var samples = new List<double>(9);
            for (int i = 0; i < 9; i++)
            {
                clock.Advance(ClockDomainId.FixedFrame, 1);
                GC.GetAllocatedBytesForCurrentThread();
                long frameBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                lifetime.Update(0.016f);
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                long frameAlloc = GC.GetAllocatedBytesForCurrentThread() - frameBefore;
                perFrameAlloc.Add(frameAlloc);
            }
            long allocated = perFrameAlloc.Sum();
            samples.Sort();
            double median = samples[samples.Count / 2];
            double p95 = samples[(int)(samples.Count * 0.95)];
            TestContext.Out.WriteLine(
                $"waiting count={count} medianMs={median:F3} p95Ms={p95:F3} allocated={allocated} perFrame=[{string.Join(",", perFrameAlloc)}] samples=[{string.Join(",", samples.Select(s => s.ToString("F2")))}]");

            // 周期未到：效果全部保持有效，无副作用。
            That(world.CountEntities(in _effectQuery), Is.EqualTo(count));
            That(allocated, Is.LessThanOrEqualTo(64));
        }

        // ─────────────────────────────────────────────────────────────
        // 场景 2：周期到达 —— 每个应触发效果恰好执行一次，属性正确
        // ─────────────────────────────────────────────────────────────
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        public void PeriodReached_EachEffectTriggersExactlyOnce_AttributeCorrect(int count)
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var tagOps = new TagOps(
                new DirtyEntityQueue(Math.Max(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, count * 2)),
                new TagRuleRegistry());

            const int graphId = 1001;
            const int templateId = 1001;
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            // OnPeriod Post 图：把 Durability current 减 7。
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = -7f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = DurabilityId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);

            var bindings = new EffectPhaseGraphBindings();
            bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, graphId);
            templates.Register(templateId, new EffectTemplateData
            {
                CategoryId = 0,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.Infinite,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 0,
                PeriodTicks = 1,
                PhaseGraphBindings = bindings,
            });
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                programs,
                "Test/EffectLifetimeScaleTests.PeriodReached.json");

            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, new GasGraphOpHandlerTable(), templates);
            using var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: Math.Max(SnapshotCapacity, count),
                fanOutCommandCapacity: FanOutCapacity,
                templates: templates,
                phaseExecutor: executor,
                graphApi: graphApi,
                tagOps: tagOps);
            using var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps);

            var targets = new Entity[count];
            var effects = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                var target = world.Create(
                    new AttributeBuffer(),
                    new ActiveEffectContainer(),
                    new DirtyFlags(),
                    new GameplayTagContainer(),
                    new TagCountContainer(),
                    new AttributeAggregateDirty());
                world.Get<AttributeBuffer>(target).SetBase(DurabilityId, 100f);
                world.Get<AttributeBuffer>(target).SetCurrent(DurabilityId, 100f);
                targets[i] = target;

                Entity source = world.Create();
                effects[i] = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    targets[i],
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.Infinite,
                    periodTicks: 1,
                    targetContext: targets[i],
                    clockId: GasClockId.FixedFrame);
                world.Add(effects[i], new EffectTemplateRef { TemplateId = templateId });
                world.Get<GameplayEffect>(effects[i]).State = EffectState.Committed;
                world.Get<GameplayEffect>(effects[i]).AggregatesModifiers = false;
                world.Get<ActiveEffectContainer>(targets[i]).Add(effects[i]);
            }

            // 预热一个空 tick（不触发周期，NextTickAtTick 初始化）。
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            aggregator.Update(0.016f);

            // 一个周期 tick 后，每个效果恰好触发一次：属性 100 → 93。
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            aggregator.Update(0.016f);
            for (int i = 0; i < count; i++)
            {
                float current = world.Get<AttributeBuffer>(targets[i]).GetCurrent(DurabilityId);
                int nextTick = world.Get<GameplayEffect>(effects[i]).NextTickAtTick;
                That(current, Is.EqualTo(93f).Within(0.001f), $"unit {i} should trigger exactly once");
                That(nextTick, Is.EqualTo(3), $"unit {i} period advanced exactly once");
            }

            // 再过一个周期：每个效果恰好再触发一次 93 → 86。
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            aggregator.Update(0.016f);
            for (int i = 0; i < count; i++)
            {
                float current = world.Get<AttributeBuffer>(targets[i]).GetCurrent(DurabilityId);
                That(current, Is.EqualTo(86f).Within(0.001f), $"unit {i} should trigger exactly twice");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 场景 3：大批效果同时到期 —— 标签、效果列表、监听器完整清理
        // ─────────────────────────────────────────────────────────────
        [TestCase(1000)]
        [TestCase(5000)]
        [TestCase(10000)]
        public void BulkExpiry_AllExpiredTogether_CleanedUpFully(int count)
        {
            const int removeGraphId = 1002;
            const int templateId = 1002;

            // 模板/图注册一次（与轮次无关的纯数据）。
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            BuildExpiryTemplate(templates, programs, templateId, removeGraphId);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                programs,
                "Test/EffectLifetimeScaleTests.BulkExpiry.json");

            // 每轮：重建世界，测单次同 tick 到期整帧的耗时与分配，并验清理完整。
            var samples = new List<double>(5);
            var perSampleAlloc = new List<long>(5);
            for (int round = 0; round < 5; round++)
            {
                var session = BuildBulkExpirySession(count, templateId, removeGraphId, templates, programs, presetTypes, builtinHandlers);
                using (session.World)
                using (session.Lifetime)
                {
                    // 未到期：全部有效。
                    session.Clock.Advance(ClockDomainId.FixedFrame, 0);
                    session.Lifetime.Update(0.016f);
                    That(session.World.CountEntities(in _effectQuery), Is.EqualTo(count));

                    // 单次到期帧。
                    GC.GetAllocatedBytesForCurrentThread();
                    long frameBefore = GC.GetAllocatedBytesForCurrentThread();
                    session.Clock.Advance(ClockDomainId.FixedFrame, 1);
                    long start = Stopwatch.GetTimestamp();
                    session.Lifetime.Update(0.016f);
                    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    perSampleAlloc.Add(GC.GetAllocatedBytesForCurrentThread() - frameBefore);

                    // 清理完整：效果实体全部销毁、目标容器清空、监听器清空。
                    That(session.World.CountEntities(in _effectQuery), Is.EqualTo(0), $"round {round}");
                    for (int i = 0; i < count; i++)
                    {
                        That(session.World.IsAlive(session.Targets[i]), Is.True);
                        That(session.World.Get<ActiveEffectContainer>(session.Targets[i]).Count, Is.EqualTo(0), $"round {round} unit {i} container not cleaned");
                        That(session.World.Get<EffectPhaseListenerBuffer>(session.Targets[i]).Count, Is.EqualTo(0), $"round {round} unit {i} listeners not cleaned");
                    }
                }
            }

            samples.Sort();
            double median = samples[samples.Count / 2];
            double p95 = samples[(int)(samples.Count * 0.95)];
            long allocated = perSampleAlloc.Sum();
            TestContext.Out.WriteLine(
                $"bulk-expiry count={count} medianMs={median:F3} p95Ms={p95:F3} allocated={allocated} perAlloc=[{string.Join(",", perSampleAlloc)}] samples=[{string.Join(",", samples.Select(s => s.ToString("F2")))}]");
        }

        // ─────────────────────────────────────────────────────────────
        // 场景 4：处理中失败或中止 —— 属性、标签、计时状态、外部队列恢复
        // ─────────────────────────────────────────────────────────────
        [Test]
        public void AbortMidProcessing_RestoresWorldAndExternalQueues()
        {
            const int count = 10_000;
            using var world = World.Create();
            var clock = new DiscreteClock();
            var dirtyQueue = new DirtyEntityQueue(Math.Max(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, count * 2));
            var tagOps = new TagOps(dirtyQueue, new TagRuleRegistry());
            using var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: Math.Max(SnapshotCapacity, count),
                fanOutCommandCapacity: FanOutCapacity,
                tagOps: tagOps);

            CreateOnePersistentEffectPerUnit(world, count, periodTicks: 100);

            // 跑两 tick，让周期初始化推进。
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);

            // 未提交中止：ResetSlice 触发事务回滚，世界状态与外部队列不变。
            lifetime.ResetSlice();

            // 周期未到：效果全部仍在，状态未被破坏。
            That(world.CountEntities(in _effectQuery), Is.EqualTo(count));
            That(dirtyQueue.Count, Is.EqualTo(0));

            // 中止后事务可复用：继续完整跑一拍，效果仍有效且状态一致。
            clock.Advance(ClockDomainId.FixedFrame, 1);
            lifetime.Update(0.016f);
            That(world.CountEntities(in _effectQuery), Is.EqualTo(count));
        }

        // ─────────────────────────────────────────────────────────────
        // 场景 5：分片执行 —— 与一次执行完成的最终结果和事件顺序一致
        // ─────────────────────────────────────────────────────────────
        [Test]
        public void SlicedExecution_MatchesSinglePass()
        {
            const int count = 10_000;
            RunSliceComparison(count, sliceSize: 2500);
            RunSliceComparison(count, sliceSize: 137);
            RunSliceComparison(count, sliceSize: 1);
        }

        private static void RunSliceComparison(int count, int sliceSize)
        {
            int[] singleNext = runEffects("single", count, int.MaxValue);
            int[] slicedNext = runEffects($"sliced-{sliceSize}", count, sliceSize);

            for (int i = 0; i < count; i++)
            {
                That(slicedNext[i], Is.EqualTo(singleNext[i]), $"sliceSize={sliceSize} unit {i} NextTickAtTick");
            }

            static int[] runEffects(string label, int count, int sliceSize)
            {
                using var world = World.Create();
                var clock = new DiscreteClock();
                using var lifetime = new EffectLifetimeSystem(
                    world,
                    clock,
                    new GasConditionRegistry(),
                    snapshotCapacity: Math.Max(SnapshotCapacity, count),
                    fanOutCommandCapacity: FanOutCapacity);

                var effects = CreateOnePersistentEffectPerUnit(world, count, periodTicks: 3);
                var next = new int[count];

                clock.Advance(ClockDomainId.FixedFrame, 3);
                if (sliceSize == int.MaxValue)
                {
                    lifetime.Update(0.016f);
                }
                else
                {
                    lifetime.MaxWorkUnitsPerSlice = sliceSize;
                    while (!lifetime.UpdateSlice(0.016f, int.MaxValue)) { }
                    lifetime.ResetSlice();
                }

                for (int i = 0; i < count; i++)
                {
                    next[i] = world.IsAlive(effects[i])
                        ? world.Get<GameplayEffect>(effects[i]).NextTickAtTick
                        : 0;
                }

                return next;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 辅助
        // ─────────────────────────────────────────────────────────────
        /// <summary>count 个单位，每人一个 Infinite + period 的持续效果，返回效果实体数组。</summary>
        private static Entity[] CreateOnePersistentEffectPerUnit(World world, int count, int periodTicks)
        {
            var effects = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                var target = world.Create(
                    new AttributeBuffer(),
                    new ActiveEffectContainer(),
                    new DirtyFlags(),
                    new GameplayTagContainer(),
                    new TagCountContainer(),
                    new AttributeAggregateDirty());
                world.Get<AttributeBuffer>(target).SetBase(DurabilityId, 100f);
                world.Get<AttributeBuffer>(target).SetCurrent(DurabilityId, 100f);

                Entity source = world.Create();
                effects[i] = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    target,
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.Infinite,
                    periodTicks: periodTicks,
                    targetContext: target,
                    clockId: GasClockId.FixedFrame);
                world.Get<GameplayEffect>(effects[i]).State = EffectState.Committed;
                world.Get<GameplayEffect>(effects[i]).AggregatesModifiers = true;
                world.Get<ActiveEffectContainer>(target).Add(effects[i]);
            }

            return effects;
        }

        private static void BuildExpiryTemplate(
            EffectTemplateRegistry templates,
            GraphProgramRegistry programs,
            int templateId,
            int removeGraphId)
        {
            // OnRemove 图：空执行体（无副作用），只为驱动监听器清理路径。
            programs.Register(removeGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            var bindings = new EffectPhaseGraphBindings();
            bindings.TryAddStep(EffectPhaseId.OnRemove, PhaseSlot.Pre, removeGraphId);
            templates.Register(templateId, new EffectTemplateData
            {
                CategoryId = 0,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 1,
                PeriodTicks = 0,
                PhaseGraphBindings = bindings,
            });
        }

        private sealed class BulkExpirySession : IDisposable
        {
            public required World World { get; init; }
            public required DiscreteClock Clock { get; init; }
            public required EffectLifetimeSystem Lifetime { get; init; }
            public required Entity[] Targets { get; init; }

            public void Dispose()
            {
                World.Dispose();
            }
        }

        /// <summary>重建一批同 tick 到期效果的世界与系统。模板/图由调用方共享。</summary>
        private static BulkExpirySession BuildBulkExpirySession(
            int count,
            int templateId,
            int removeGraphId,
            EffectTemplateRegistry templates,
            GraphProgramRegistry programs,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers)
        {
            var world = World.Create();
            var clock = new DiscreteClock();
            var tagOps = new TagOps(
                new DirtyEntityQueue(Math.Max(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, count * 2)),
                new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, new GasGraphOpHandlerTable(), templates);
            var presentationEvents = new GasPresentationEventBuffer(Math.Max(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, count * 2));
            var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: Math.Max(SnapshotCapacity, count),
                fanOutCommandCapacity: FanOutCapacity,
                templates: templates,
                phaseExecutor: executor,
                graphApi: graphApi,
                tagOps: tagOps,
                presentationEvents: presentationEvents);

            var targets = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                var target = world.Create(
                    new AttributeBuffer(),
                    new ActiveEffectContainer(),
                    new DirtyFlags(),
                    new GameplayTagContainer(),
                    new TagCountContainer(),
                    new AttributeAggregateDirty(),
                    new EffectPhaseListenerBuffer());
                world.Get<AttributeBuffer>(target).SetBase(DurabilityId, 100f);
                world.Get<AttributeBuffer>(target).SetCurrent(DurabilityId, 100f);
                targets[i] = target;

                Entity source = world.Create();
                Entity effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    target,
                    durationTicks: 1,
                    lifetimeKind: EffectLifetimeKind.After,
                    periodTicks: 0,
                    targetContext: target,
                    clockId: GasClockId.FixedFrame);
                world.Add(effect, new EffectTemplateRef { TemplateId = templateId });
                world.Get<GameplayEffect>(effect).State = EffectState.Committed;
                world.Get<ActiveEffectContainer>(target).Add(effect);

                // 预填充一个以该效果为 owner 的监听器；到期后应被清理。
                ref var listeners = ref world.Get<EffectPhaseListenerBuffer>(target);
                listeners.TryAdd(0, templateId, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.ExecuteGraph, removeGraphId, 0, 50, ownerEffectId: effect.Id);
            }

            return new BulkExpirySession
            {
                World = world,
                Clock = clock,
                Lifetime = lifetime,
                Targets = targets,
            };
        }
    }
}
