using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// ISSUE-1521 验收基准：已提交周期效果的履约域单事件边际成本。
    /// 形态：每效果 period 在 [8,64] 均匀随机 + 哈希初相（聚合到货近似泊松），
    /// 走生产 EffectLifetimeSystem（时间轮+编译内核+事务提交）与聚合器整链。
    /// 断言：精简目标（履约车道本体）单事件边际 ≤2µs；富目标（含稠密通道
    /// GameplayAttributeChangedChannel 变更广播标记的完整组件集）单独计量并设回归护栏。
    /// </summary>
    [TestFixture]
    public sealed class EffectPeriodFulfillmentMarginalCostTests
    {
        private const int ThinTargetCount = 4096;
        private const int FatTargetCount = 2048;
        private const int WarmupTicks = 256;
        private const int MeasuredTicks = 1024;

        private static GraphInstruction[] PureDriftProgram(int attributeId) => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.RandomFloat01, Dst = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 0.5f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.SubFloat, Dst = 3, A = 1, B = 2 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 4, ImmF = 16f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 5, A = 3, B = 4 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 5, Imm = attributeId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
        };

        [Test]
        public void CommittedPeriodFulfillment_MarginalCostPerEvent_StaysWithinBudget()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("FulfillmentMarginalHealth");
            double thinMarginalUs = MeasureMarginalCost(ThinTargetCount, healthId, fatTargets: false, out long thinEvents);
            double fatMarginalUs = MeasureMarginalCost(FatTargetCount, healthId, fatTargets: true, out long fatEvents);

            TestContext.Out.WriteLine(
                $"fulfillment marginal: thin={thinMarginalUs:F2}us/event over {thinEvents} events; " +
                $"fat(with dense broadcast channel)={fatMarginalUs:F2}us/event over {fatEvents} events");

            That(thinMarginalUs, Is.LessThanOrEqualTo(2.0d),
                "履约车道单事件边际（精简目标，不含变更广播）超过 2µs 预算。");
            That(fatMarginalUs, Is.LessThanOrEqualTo(10.0d),
                "含变更广播通道的单事件边际超出回归护栏（10µs）。");
        }

        private static double MeasureMarginalCost(int targetCount, int healthId, bool fatTargets, out long totalEvents)
        {
            var world = World.Create();
            var tagOps = new TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new TagRuleRegistry(),
                aggregateDirty: new AttributeAggregateDirtyRegistry());
            var clock = new DiscreteClock();
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            Ludots.Core.Gameplay.GAS.BuiltinHandlers.RegisterAll(builtinHandlers);

            const int graphId = 9101;
            const int templateId = 811;
            programs.Register(graphId, PureDriftProgram(healthId), GraphKind.Effect);
            var bindings = new EffectPhaseGraphBindings();
            That(bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, graphId), Is.True);
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.Infinite,
                ClockId = GasClockId.FixedFrame,
                PeriodTicks = 8,
                PhaseGraphBindings = bindings,
            });
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates, presetTypes, builtinHandlers, programs, "Test/EffectPeriodFulfillmentMarginalCostTests.json");
            That(
                templates.PeriodKernelTable!.GetClassification(templateId),
                Is.EqualTo(EffectPeriodKernelClassification.Compiled),
                "基准模板必须命中编译内核，否则测的是解释路径。");

            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, new GasGraphOpHandlerTable(), templates);
            var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                targetCount * 2,
                64,
                templates: templates,
                phaseExecutor: executor,
                graphApi: graphApi,
                tagOps: tagOps);
            var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps, aggregateDirty: tagOps.AggregateDirty);

            var random = new Random(20260913);
            for (int i = 0; i < targetCount; i++)
            {
                Entity target = fatTargets
                    ? world.Create(
                        new AttributeBuffer(),
                        new DirtyFlags(),
                        new ActiveEffectContainer(),
                        new GameplayTagContainer(),
                        new TagCountContainer(),
                        new Ludots.Core.Components.WorldPositionCm(),
                        new Ludots.Core.Components.PreviousWorldPositionCm(),
                        new Ludots.Core.Components.FacingDirection(),
                        new EffectModifiers())
                    : world.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer());
                ref var attributes = ref world.Get<AttributeBuffer>(target);
                attributes.SetBase(healthId, 1000f);
                attributes.SetCurrent(healthId, 1000f);

                int periodTicks = 8 + random.Next(57);
                var effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    target,
                    target,
                    durationTicks: 0,
                    EffectLifetimeKind.Infinite,
                    periodTicks,
                    clockId: GasClockId.FixedFrame);
                world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            }

            // 温车：让时间轮、内核表、JIT 与事务结构进入稳态。
            for (int tick = 0; tick < WarmupTicks; tick++)
            {
                lifetime.Update(1f);
                aggregator.Update(1f);
                clock.Advance(ClockDomainId.FixedFrame);
            }

            long events = 0;
            var stopwatch = Stopwatch.StartNew();
            for (int tick = 0; tick < MeasuredTicks; tick++)
            {
                lifetime.Update(1f);
                aggregator.Update(1f);
                events += lifetime.LastSliceProcessed;
                clock.Advance(ClockDomainId.FixedFrame);
            }

            stopwatch.Stop();
            totalEvents = events;
            World.Destroy(world);
            That(events, Is.GreaterThan(0), "基准窗口内必须发生到期事件。");
            return stopwatch.Elapsed.TotalMilliseconds * 1000d / events;
        }
    }
}
