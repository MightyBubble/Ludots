using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class MudAbilityChainStressDemoTests
    {
        [Test]
        public void MudCombat_AbilityRelease_ChainAndDot_WritesLogFile()
        {
            var world = World.Create();
            try
            {
                int attrHealth = 0;

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

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(tagFireboltHit, ResponseType.Modify, priority: 50, modifyValue: 1.5f, modifyOp: ModifierOp.Multiply);
                    listener.Add(tagFireboltHit, ResponseType.Chain, priority: 40, effectTemplateId: tplBurning);
                    listener.Add(tagHeal, ResponseType.Modify, priority: 10, modifyValue: 1.25f, modifyOp: ModifierOp.Multiply);
                    world.Add(listenerEntity, listener);
                }

                var abilityFirebolt = world.Create();
                world.Add(abilityFirebolt, new AbilityTemplate());
                world.Add(abilityFirebolt, new AbilityOnActivateEffects());
                world.Add(abilityFirebolt, new AbilityExecSpec());
                unsafe
                {
                    ref var onActivate = ref world.Get<AbilityOnActivateEffects>(abilityFirebolt);
                    onActivate.Add(tplFirebolt);
                }

                var abilityHeal = world.Create();
                world.Add(abilityHeal, new AbilityTemplate());
                world.Add(abilityHeal, new AbilityOnActivateEffects());
                world.Add(abilityHeal, new AbilityExecSpec());
                unsafe
                {
                    ref var onActivate = ref world.Get<AbilityOnActivateEffects>(abilityHeal);
                    onActivate.Add(tplHeal);
                }

                var player = world.Create(new AbilityStateBuffer(), new AttributeBuffer());
                ref var playerAbilities = ref world.Get<AbilityStateBuffer>(player);
                var abilityDefs = new AbilityDefinitionRegistry();
                abilityDefs.RegisterFromEntity(world, abilityFirebolt, 6001);
                abilityDefs.RegisterFromEntity(world, abilityHeal, 6002);
                playerAbilities.AddAbility(6001);
                playerAbilities.AddAbility(6002);
                world.Get<AttributeBuffer>(player).SetCurrent(attrHealth, 100f);

                var goblinA = world.Create(new AttributeBuffer());
                var goblinB = world.Create(new AttributeBuffer());
                world.Get<AttributeBuffer>(goblinA).SetCurrent(attrHealth, 100f);
                world.Get<AttributeBuffer>(goblinB).SetCurrent(attrHealth, 100f);

                var abilitySystem = new AbilitySystem(world, requests, abilityDefs);
                var processing = new EffectProcessingLoopSystem(world, requests, clock, conditions, budget, templates, null, null, new ResponseChainTelemetryBuffer(), new OrderRequestQueue())
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
                        abilitySystem.TryActivateAbility(player, slotIndex: 0, explicitTarget: goblinA);
                    }
                    else if (frame == 1)
                    {
                        sb.AppendLine("[MUD] 哥布林B冲上来，你对自己释放【治疗】。");
                        abilitySystem.TryActivateAbility(player, slotIndex: 1, explicitTarget: player);
                    }
                    else if (frame == 2)
                    {
                        sb.AppendLine("[MUD] 你对哥布林B释放【火矢】。");
                        abilitySystem.TryActivateAbility(player, slotIndex: 0, explicitTarget: goblinB);
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
                int attrHealth = 0;

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

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(tagVolleyHit, ResponseType.Modify, priority: 50, modifyValue: 1.2f, modifyOp: ModifierOp.Multiply);
                    listener.Add(tagVolleyHit, ResponseType.Chain, priority: 40, effectTemplateId: tplBurning);
                    world.Add(listenerEntity, listener);
                }

                var abilityVolley = world.Create();
                world.Add(abilityVolley, new AbilityTemplate());
                world.Add(abilityVolley, new AbilityOnActivateEffects());
                world.Add(abilityVolley, new AbilityExecSpec());
                unsafe
                {
                    ref var onActivate = ref world.Get<AbilityOnActivateEffects>(abilityVolley);
                    onActivate.Add(tplVolleyHit);
                }

                int targetsCount = 2000;
                var targets = new Entity[targetsCount];
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i] = world.Create(new AttributeBuffer());
                    ref var attr = ref world.Get<AttributeBuffer>(targets[i]);
                    attr.SetCurrent(attrHealth, 1000f);
                }

                var player = world.Create(new AbilityStateBuffer());
                ref var abilities = ref world.Get<AbilityStateBuffer>(player);
                var abilityDefs = new AbilityDefinitionRegistry();
                abilityDefs.RegisterFromEntity(world, abilityVolley, 7001);
                abilities.AddAbility(7001);

                var abilitySystem = new AbilitySystem(world, requests, abilityDefs);
                var processing = new EffectProcessingLoopSystem(world, requests, clock, conditions, budget, templates, null, null, new ResponseChainTelemetryBuffer(), new OrderRequestQueue())
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };

                float dt = 1f;
                var args = new AbilitySystem.AbilityActivationArgs(targetEntities: targets);

                for (int i = 0; i < 2; i++)
                {
                    abilitySystem.TryActivateAbility(player, 0, in args);
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
                    abilitySystem.TryActivateAbility(player, 0, in args);
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
    }
}
