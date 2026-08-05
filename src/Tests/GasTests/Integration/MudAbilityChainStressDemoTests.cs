using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class MudAbilityChainStressDemoTests
    {
        private const string ChainHealthAttributeName = "tests.mud.ability-chain.health";
        private const string StressHealthAttributeName = "tests.mud.ability-stress.health";

        [Test]
        public void MudCombat_AbilityRelease_ChainAndDot_WritesLogFile()
        {
            var world = World.Create();
            try
            {
                int attrHealth = ResolveAttributeId(ChainHealthAttributeName);

                int tagFireboltHit = 10;
                int tagBurning = 11;
                int tagBurnTick = 12;
                int tagHeal = 13;

                int tplFirebolt = 1;
                int tplBurning = 2;
                int tplBurnTick = 3;
                int tplHeal = 4;

                var templates = new EffectTemplateRegistry();
                var requests = new EffectRequestQueue();
                var budget = new GasBudget();
                var clock = new DiscreteClock();
                var clocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();

                var fireboltMods = default(EffectModifiers);
                fireboltMods.Add(attrId: attrHealth, ModifierOp.Add, -10f);
                templates.Register(tplFirebolt, new EffectTemplateData
                {
                    TagId = tagFireboltHit,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = fireboltMods
                });

                templates.Register(tplBurning, new EffectTemplateData
                {
                    TagId = tagBurning,
                    LifetimeKind = EffectLifetimeKind.After,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 5,
                    PeriodTicks = 1,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    // TODO: OnPeriodEffectId = tplBurnTick was removed; migrate to Phase Graph architecture
                    Modifiers = default
                });

                var burnTickMods = default(EffectModifiers);
                burnTickMods.Add(attrId: attrHealth, ModifierOp.Add, -2f);
                templates.Register(tplBurnTick, new EffectTemplateData
                {
                    TagId = tagBurnTick,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = burnTickMods
                });

                var healMods = default(EffectModifiers);
                healMods.Add(attrId: attrHealth, ModifierOp.Add, 8f);
                templates.Register(tplHeal, new EffectTemplateData
                {
                    TagId = tagHeal,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = healMods
                });
                FinalizeEffectTemplates(templates);

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(tagFireboltHit, ResponseType.Modify, priority: 50, modifyValue: 1.5f, modifyOp: ModifierOp.Multiply);
                    listener.Add(tagFireboltHit, ResponseType.Chain, priority: 40, effectTemplateId: tplBurning);
                    listener.Add(tagHeal, ResponseType.Modify, priority: 10, modifyValue: 1.25f, modifyOp: ModifierOp.Multiply);
                    world.Add(listenerEntity, listener);
                }

                var abilityDefs = new AbilityDefinitionRegistry();
                abilityDefs.Register(6001, new AbilityDefinition
                {
                    ExecSpec = CreateEffectSignalSpec(tplFirebolt, ExecEffectDispatchTarget.Target)
                });
                abilityDefs.Register(6002, new AbilityDefinition
                {
                    ExecSpec = CreateEffectSignalSpec(tplHeal, ExecEffectDispatchTarget.Target)
                });

                var player = world.Create(
                    OrderBuffer.CreateEmpty(),
                    new BlackboardIntBuffer(),
                    new BlackboardEntityBuffer(),
                    new AbilityStateBuffer(),
                    new AttributeBuffer(),
                    new DirtyFlags());
                ref var playerAbilities = ref world.Get<AbilityStateBuffer>(player);
                playerAbilities.AddAbility(6001);
                playerAbilities.AddAbility(6002);
                world.Get<AttributeBuffer>(player).SetBase(attrHealth, 100f);

                var goblinA = world.Create(new AttributeBuffer(), new DirtyFlags());
                var goblinB = world.Create(new AttributeBuffer(), new DirtyFlags());
                world.Get<AttributeBuffer>(goblinA).SetBase(attrHealth, 100f);
                world.Get<AttributeBuffer>(goblinB).SetBase(attrHealth, 100f);

                var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
                const int castAbilityOrderTypeId = 100;
                var terminalResults = new OrderTerminalResultBuffer(capacity: 64);
                var orderTypes = CreateCastOrderTypes(castAbilityOrderTypeId, terminalResults);
                var abilityExecSystem = new AbilityExecSystem(
                    world,
                    clock,
                    new InputRequestQueue(),
                    new InputResponseBuffer(),
                    requests,
                    snapshotCapacity: 16,
                    abilityDefinitions: abilityDefs,
                    castAbilityOrderTypeId: castAbilityOrderTypeId,
                    orderTypeRegistry: orderTypes,
                    tagOps: tagOps);
                var processing = new EffectProcessingLoopSystem(
                    world,
                    requests,
                    clock,
                    conditions,
                    16384,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    budget,
                    templates,
                    new InputRequestQueue(),
                    new OrderQueue(4, new OrderAdmissionResultBuffer(4, 4)),
                    new ResponseChainTelemetryBuffer(),
                    new OrderRequestQueue(),
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: tagOps)
                {
                    MaxWorkUnitsPerSlice = 2048
                };

                var sb = new StringBuilder();
                string logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mud_ability_chain_demo.log");

                float dt = 1f;
                sb.AppendLine("[MUD] 你进入地牢。");
                sb.AppendLine("[MUD] 两只哥布林冲了出来。");
                sb.AppendLine("[MUD] 你的法术条：1) 火矢 2) 治疗");

                float hpA0 = world.Get<AttributeBuffer>(goblinA).GetCurrent(attrHealth);
                float hpB0 = world.Get<AttributeBuffer>(goblinB).GetCurrent(attrHealth);

                for (int frame = 0; frame < 8; frame++)
                {
                    budget.Reset();
                    clocks.AdvanceFixedFrame();
                    clocks.AdvanceStep();

                    if (frame == 0)
                    {
                        sb.AppendLine("[MUD] 你对哥布林A释放【火矢】。");
                        SubmitAndRunCast(
                            world,
                            abilityExecSystem,
                            terminalResults,
                            player,
                            goblinA,
                            castAbilityOrderTypeId,
                            slotIndex: 0,
                            orderId: 1);
                    }
                    else if (frame == 1)
                    {
                        sb.AppendLine("[MUD] 哥布林B冲上来，你对自己释放【治疗】。");
                        SubmitAndRunCast(
                            world,
                            abilityExecSystem,
                            terminalResults,
                            player,
                            player,
                            castAbilityOrderTypeId,
                            slotIndex: 1,
                            orderId: 2);
                    }
                    else if (frame == 2)
                    {
                        sb.AppendLine("[MUD] 你对哥布林B释放【火矢】。");
                        SubmitAndRunCast(
                            world,
                            abilityExecSystem,
                            terminalResults,
                            player,
                            goblinB,
                            castAbilityOrderTypeId,
                            slotIndex: 0,
                            orderId: 3);
                    }

                    processing.Update(dt);
                    clocks.AdvanceFixedFrame();

                    float hpA = world.Get<AttributeBuffer>(goblinA).GetCurrent(attrHealth);
                    float hpB = world.Get<AttributeBuffer>(goblinB).GetCurrent(attrHealth);
                    float hpP = world.Get<AttributeBuffer>(player).GetCurrent(attrHealth);

                    sb.AppendLine($"[MUD][Frame={frame}] AHP={hpA:F1} BHP={hpB:F1} PHP={hpP:F1} Windows={budget.ResponseWindows} Steps={budget.ResponseSteps} Creates={budget.ResponseCreates}");
                }

                File.WriteAllText(logPath, sb.ToString());
                Console.WriteLine($"[MUD] LogFile={logPath}");
                Console.WriteLine(sb.ToString());

                That(world.Get<AttributeBuffer>(goblinA).GetCurrent(attrHealth), Is.LessThan(hpA0));
                That(world.Get<AttributeBuffer>(goblinB).GetCurrent(attrHealth), Is.LessThan(hpB0));
                Pass("MUD ability chain demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void MudCombat_Stress_ArcaneVolleyWithBurning_ReportsThroughput()
        {
            var world = World.Create();
            try
            {
                int attrHealth = ResolveAttributeId(StressHealthAttributeName);

                int tagVolleyHit = 20;
                int tagBurning = 21;
                int tagBurnTick = 22;

                int tplVolleyHit = 1;
                int tplBurning = 2;
                int tplBurnTick = 3;

                var templates = new EffectTemplateRegistry();
                var requests = new EffectRequestQueue();
                var budget = new GasBudget();
                var clock = new DiscreteClock();
                var clocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();

                var volleyMods = default(EffectModifiers);
                volleyMods.Add(attrId: attrHealth, ModifierOp.Add, -3f);
                templates.Register(tplVolleyHit, new EffectTemplateData
                {
                    TagId = tagVolleyHit,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = volleyMods
                });

                templates.Register(tplBurning, new EffectTemplateData
                {
                    TagId = tagBurning,
                    LifetimeKind = EffectLifetimeKind.After,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 3,
                    PeriodTicks = 1,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    // TODO: OnPeriodEffectId = tplBurnTick was removed; migrate to Phase Graph architecture
                    Modifiers = default
                });

                var burnTickMods = default(EffectModifiers);
                burnTickMods.Add(attrId: attrHealth, ModifierOp.Add, -1f);
                templates.Register(tplBurnTick, new EffectTemplateData
                {
                    TagId = tagBurnTick,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = burnTickMods
                });
                FinalizeEffectTemplates(templates);

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(tagVolleyHit, ResponseType.Modify, priority: 50, modifyValue: 1.2f, modifyOp: ModifierOp.Multiply);
                    listener.Add(tagVolleyHit, ResponseType.Chain, priority: 40, effectTemplateId: tplBurning);
                    world.Add(listenerEntity, listener);
                }

                var abilityDefs = new AbilityDefinitionRegistry();
                abilityDefs.Register(7001, new AbilityDefinition
                {
                    ExecSpec = CreateEffectSignalSpec(tplVolleyHit, ExecEffectDispatchTarget.Target)
                });

                int targetsCount = 2000;
                var targets = new Entity[targetsCount];
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i] = world.Create(new AttributeBuffer(), new DirtyFlags());
                    ref var attr = ref world.Get<AttributeBuffer>(targets[i]);
                    attr.SetBase(attrHealth, 1000f);
                }

                var player = world.Create(
                    OrderBuffer.CreateEmpty(),
                    new BlackboardIntBuffer(),
                    new BlackboardEntityBuffer(),
                    new AbilityStateBuffer());
                ref var abilities = ref world.Get<AbilityStateBuffer>(player);
                abilities.AddAbility(7001);

                var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
                const int castAbilityOrderTypeId = 100;
                var terminalResults = new OrderTerminalResultBuffer(capacity: 64);
                var orderTypes = CreateCastOrderTypes(castAbilityOrderTypeId, terminalResults);
                var abilityExecSystem = new AbilityExecSystem(
                    world,
                    clock,
                    new InputRequestQueue(),
                    new InputResponseBuffer(),
                    requests,
                    snapshotCapacity: 16,
                    abilityDefinitions: abilityDefs,
                    castAbilityOrderTypeId: castAbilityOrderTypeId,
                    orderTypeRegistry: orderTypes,
                    tagOps: tagOps);
                var processing = new EffectProcessingLoopSystem(
                    world,
                    requests,
                    clock,
                    conditions,
                    16384,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    budget,
                    templates,
                    new InputRequestQueue(),
                    new OrderQueue(4, new OrderAdmissionResultBuffer(4, 4)),
                    new ResponseChainTelemetryBuffer(),
                    new OrderRequestQueue(),
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: tagOps)
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };

                float dt = 1f;
                int nextOrderId = 1;

                for (int i = 0; i < 2; i++)
                {
                    SubmitCastForTargets(
                        world,
                        abilityExecSystem,
                        terminalResults,
                        player,
                        targets,
                        castAbilityOrderTypeId,
                        slotIndex: 0,
                        ref nextOrderId);
                    processing.Update(dt);
                    clocks.AdvanceFixedFrame();
                    clocks.AdvanceStep();
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int logicFrames = 5;
                var sw = Stopwatch.StartNew();
                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_0 = GC.CollectionCount(0);
                int gen1_0 = GC.CollectionCount(1);
                int gen2_0 = GC.CollectionCount(2);
                long ticksAdvance = 0;
                long ticksActivate = 0;
                long ticksProcess = 0;

                int totalWindows = 0;
                int totalSteps = 0;
                int totalCreates = 0;

                for (int frame = 0; frame < logicFrames; frame++)
                {
                    budget.Reset();
                    long t0 = Stopwatch.GetTimestamp();
                    clocks.AdvanceFixedFrame();
                    clocks.AdvanceStep();
                    ticksAdvance += Stopwatch.GetTimestamp() - t0;

                    t0 = Stopwatch.GetTimestamp();
                    SubmitCastForTargets(
                        world,
                        abilityExecSystem,
                        terminalResults,
                        player,
                        targets,
                        castAbilityOrderTypeId,
                        slotIndex: 0,
                        ref nextOrderId);
                    ticksActivate += Stopwatch.GetTimestamp() - t0;

                    t0 = Stopwatch.GetTimestamp();
                    processing.Update(dt);
                    ticksProcess += Stopwatch.GetTimestamp() - t0;
                    totalWindows += budget.ResponseWindows;
                    totalSteps += budget.ResponseSteps;
                    totalCreates += budget.ResponseCreates;
                }

                long alloc1 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_1 = GC.CollectionCount(0);
                int gen1_1 = GC.CollectionCount(1);
                int gen2_1 = GC.CollectionCount(2);
                sw.Stop();

                double totalRoots = (double)targetsCount * logicFrames;
                double perRootUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / totalRoots;

                Console.WriteLine($"[MUD][STRESS] 你在大厅释放【奥术齐射】，{targetsCount} 个目标同时受击并触发燃烧。");
                Console.WriteLine($"[MUD][STRESS] LogicFrames={logicFrames} Roots={totalRoots:F0} ElapsedMs={sw.Elapsed.TotalMilliseconds:F1} PerRootUs={perRootUs:F3}");
                Console.WriteLine($"[MUD][STRESS] ResponseWindows={totalWindows} ResponseSteps={totalSteps} ResponseCreates={totalCreates}");
                Console.WriteLine($"[MUD][STRESS] AllocBytes(CurrentThread)={alloc1 - alloc0}");
                Console.WriteLine($"[MUD][STRESS] GC Collections Δ: Gen0={gen0_1 - gen0_0} Gen1={gen1_1 - gen1_0} Gen2={gen2_1 - gen2_0}");
                double msAdvance = ticksAdvance * 1000.0 / Stopwatch.Frequency;
                double msActivate = ticksActivate * 1000.0 / Stopwatch.Frequency;
                double msProcess = ticksProcess * 1000.0 / Stopwatch.Frequency;
                Console.WriteLine($"[MUD][STRESS] TimeMs: AdvanceClock={msAdvance:F2} ActivateAbility={msActivate:F2} EffectLoop={msProcess:F2} Sum={msAdvance + msActivate + msProcess:F2}");

                That(sw.Elapsed.TotalSeconds, Is.LessThan(10));
                Pass("MUD stress demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        private static AbilityExecSpec CreateEffectSignalSpec(int templateId, ExecEffectDispatchTarget dispatchTarget)
        {
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.FixedFrame;
            spec.SetItem(
                0,
                ExecItemKind.EffectSignal,
                tick: 0,
                templateId: templateId,
                payloadA: (int)dispatchTarget);
            spec.SetItem(1, ExecItemKind.End, tick: 0);
            return spec;
        }

        private static OrderTypeRegistry CreateCastOrderTypes(
            int castAbilityOrderTypeId,
            OrderTerminalResultBuffer terminalResults)
        {
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                PayloadKind = OrderPayloadKind.CastAbility,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                SpatialBlackboardKey = -1,
            });
            return orderTypes;
        }

        private static void SubmitCastForTargets(
            World world,
            AbilityExecSystem abilityExecSystem,
            OrderTerminalResultBuffer terminalResults,
            Entity actor,
            Entity[] targets,
            int castAbilityOrderTypeId,
            int slotIndex,
            ref int nextOrderId)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                SubmitAndRunCast(
                    world,
                    abilityExecSystem,
                    terminalResults,
                    actor,
                    targets[i],
                    castAbilityOrderTypeId,
                    slotIndex,
                    nextOrderId++);
            }
        }

        private static void SubmitAndRunCast(
            World world,
            AbilityExecSystem abilityExecSystem,
            OrderTerminalResultBuffer terminalResults,
            Entity actor,
            Entity target,
            int castAbilityOrderTypeId,
            int slotIndex,
            int orderId)
        {
            terminalResults.Clear();
            var order = OrderBuilder.CreateCastAbility(
                castAbilityOrderTypeId,
                playerId: 0,
                actor,
                target,
                Entity.Null,
                slotIndex,
                OrderSubmitMode.Immediate,
                submitStep: 0);
            order.OrderId = orderId;
            world.Get<OrderBuffer>(actor).SetActiveDirect(in order, priority: 100);
            world.Get<BlackboardIntBuffer>(actor).Set(OrderBlackboardKeys.Cast_SlotIndex, slotIndex);
            world.Get<BlackboardEntityBuffer>(actor).Set(OrderBlackboardKeys.Cast_TargetEntity, target);
            abilityExecSystem.Update(0f);

            That(terminalResults.Count, Is.EqualTo(1));
            That(terminalResults[0].OrderId, Is.EqualTo(orderId));
            That(terminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
        }

        private static int ResolveAttributeId(string name)
        {
            int id = AttributeRegistry.GetId(name);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(name);
        }

        private static void FinalizeEffectTemplates(EffectTemplateRegistry templates)
        {
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                new PresetTypeRegistry(),
                builtinHandlers,
                new GraphProgramRegistry(),
                "Test/MudAbilityChainStressDemoTests.json");
        }
    }
}
