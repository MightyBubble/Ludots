using System;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class MudSc2AndYgoDemoTests
    {
        private readonly TagOps _tagOps = new TagOps();

        [Test]
        public void MudSc2ClassicSkills_DemoLog()
        {
            var world = World.Create();
            try
            {
                int attrHealth = 0;
                int attrEnergy = 1;
                int attrAttackSpeed = 2;
                int attrMoveSpeed = 3;
                int attrShield = 4;

                int tagCloaked = 2;
                int tagRevealed = 3;
                int tagStim = 4;

                var cloakRule = new TagRuleSet();
                unsafe
                {
                    cloakRule.BlockedTags[0] = tagRevealed;
                }
                cloakRule.BlockedCount = 1;
                _tagOps.RegisterTagRuleSet(tagCloaked, cloakRule);

                var revealRule = new TagRuleSet();
                unsafe
                {
                    revealRule.RemovedTags[0] = tagCloaked;
                }
                revealRule.RemovedCount = 1;
                _tagOps.RegisterTagRuleSet(tagRevealed, revealRule);

                int tplStimSelfDamage = 10;
                int tplCloakDrain = 21;
                int tplEmp = 30;
                int tplHealTick = 40;

                var templates = new EffectTemplateRegistry();

                var stimDmgMods = default(EffectModifiers);
                stimDmgMods.Add(attrHealth, ModifierOp.Add, -10f);
                templates.Register(tplStimSelfDamage, new EffectTemplateData
                {
                    TagId = 100,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = stimDmgMods
                });

                var drainMods = default(EffectModifiers);
                drainMods.Add(attrEnergy, ModifierOp.Add, -5f);
                templates.Register(tplCloakDrain, new EffectTemplateData
                {
                    TagId = 103,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = drainMods
                });

                var empMods = default(EffectModifiers);
                empMods.Add(attrEnergy, ModifierOp.Add, -20f);
                empMods.Add(attrShield, ModifierOp.Add, -40f);
                templates.Register(tplEmp, new EffectTemplateData
                {
                    TagId = 104,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = empMods
                });

                var healTickMods = default(EffectModifiers);
                healTickMods.Add(attrHealth, ModifierOp.Add, 2f);
                templates.Register(tplHealTick, new EffectTemplateData
                {
                    TagId = 105,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = healTickMods
                });

                var clock = new DiscreteClock();
                var gasClocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var budget = new GasBudget();
                var effectRequests = new EffectRequestQueue();
                var inputReq = new InputRequestQueue();
                var inputResp = new InputResponseBuffer();
                var selReq = new SelectionRequestQueue();
                var selResp = new SelectionResponseBuffer();
                var incomingOrders = new OrderQueue();
                var chainOrders = new OrderQueue();
                var eventBus = new GameplayEventBus();
                var abilityDefs = new AbilityDefinitionRegistry();

                const int orderCastAbility = 100;
                const int orderRespondChain = 101;

                var orderTypeRegistry = new OrderTypeRegistry();
                var tagRuleRegistry = new TagRuleRegistry();
                OrderTypeConfigLoader.RegisterDefaults(orderTypeRegistry, tagRuleRegistry);
                var orderBufferSystem = new OrderBufferSystem(world, clock, orderTypeRegistry, tagRuleRegistry, incomingOrders, 30, chainOrders, orderRespondChain);
                var clockPolicy = new GasClockStepPolicy(1);
                var clockSystem = new GasClockSystem(clock, clockPolicy);
                var timedTags = new TimedTagExpirationSystem(world, clock);
                var abilityExec = new AbilityExecSystem(world, clock, inputReq, inputResp, selReq, selResp, effectRequests, abilityDefs, eventBus, orderCastAbility, orderTypeRegistry: orderTypeRegistry);
                var effectLoop = new EffectProcessingLoopSystem(world, effectRequests, clock, conditions, budget, templates, inputReq, chainOrders, new ResponseChainTelemetryBuffer(), new OrderRequestQueue())
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };
                var agg = new AttributeAggregatorSystem(world);

                var player = world.Create(new AttributeBuffer(), new AbilityStateBuffer(), new GameplayTagContainer(), new TagCountContainer(), new TimedTagBuffer(), OrderBuffer.CreateEmpty(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer(), new BlackboardIntBuffer());
                ref var playerAttr = ref world.Get<AttributeBuffer>(player);
                playerAttr.SetBase(attrHealth, 100f);
                playerAttr.SetBase(attrEnergy, 75f);
                playerAttr.SetBase(attrAttackSpeed, 1f);
                playerAttr.SetBase(attrMoveSpeed, 1f);
                playerAttr.SetBase(attrShield, 0f);

                var enemy = world.Create(new AttributeBuffer(), new AbilityStateBuffer(), new GameplayTagContainer(), new TagCountContainer(), new TimedTagBuffer(), OrderBuffer.CreateEmpty(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer(), new BlackboardIntBuffer());
                ref var enemyAttr = ref world.Get<AttributeBuffer>(enemy);
                enemyAttr.SetBase(attrHealth, 60f);
                enemyAttr.SetBase(attrEnergy, 50f);
                enemyAttr.SetBase(attrAttackSpeed, 1f);
                enemyAttr.SetBase(attrMoveSpeed, 1f);
                enemyAttr.SetBase(attrShield, 40f);

                var enemy2 = world.Create(new AttributeBuffer(), new GameplayTagContainer(), new TagCountContainer());
                ref var enemy2Attr = ref world.Get<AttributeBuffer>(enemy2);
                enemy2Attr.SetBase(attrHealth, 60f);
                enemy2Attr.SetBase(attrEnergy, 50f);
                enemy2Attr.SetBase(attrShield, 40f);

                var stimExec = default(AbilityExecSpec);
                stimExec.ClockId = GasClockId.Step;
                stimExec.SetItem(0, ExecItemKind.TagClip, tick: 0, durationTicks: 5, clockId: GasClockId.Step, tagId: tagStim);
                stimExec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: tplStimSelfDamage);
                stimExec.SetItem(2, ExecItemKind.End, tick: 0);
                var stimAbility = world.Create(new AbilityTemplate(), stimExec);

                var cloakExec = default(AbilityExecSpec);
                cloakExec.ClockId = GasClockId.Step;
                cloakExec.SetItem(0, ExecItemKind.TagClip, tick: 0, durationTicks: 3, clockId: GasClockId.Step, tagId: tagCloaked);
                cloakExec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: tplCloakDrain);
                cloakExec.SetItem(2, ExecItemKind.EffectSignal, tick: 1, templateId: tplCloakDrain);
                cloakExec.SetItem(3, ExecItemKind.EffectSignal, tick: 2, templateId: tplCloakDrain);
                cloakExec.SetItem(4, ExecItemKind.End, tick: 2);
                var cloakAbility = world.Create(new AbilityTemplate(), cloakExec);

                var revealExec = default(AbilityExecSpec);
                revealExec.ClockId = GasClockId.Step;
                revealExec.SetItem(0, ExecItemKind.TagClipTarget, tick: 0, durationTicks: 2, clockId: GasClockId.Step, tagId: tagRevealed);
                revealExec.SetItem(1, ExecItemKind.End, tick: 0);
                var revealAbility = world.Create(new AbilityTemplate(), revealExec);

                var empExec = default(AbilityExecSpec);
                empExec.ClockId = GasClockId.Step;
                empExec.SetItem(0, ExecItemKind.SelectionGate, tick: 0);
                empExec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: tplEmp);
                empExec.SetItem(2, ExecItemKind.End, tick: 0);
                var empAbility = world.Create(new AbilityTemplate(), empExec);

                var healExec = default(AbilityExecSpec);
                healExec.ClockId = GasClockId.Step;
                healExec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: tplHealTick);
                healExec.SetItem(1, ExecItemKind.EffectSignal, tick: 1, templateId: tplHealTick);
                healExec.SetItem(2, ExecItemKind.EffectSignal, tick: 2, templateId: tplHealTick);
                healExec.SetItem(3, ExecItemKind.End, tick: 2);
                var healAbility = world.Create(new AbilityTemplate(), healExec);

                abilityDefs.RegisterFromEntity(world, stimAbility, 1001);
                abilityDefs.RegisterFromEntity(world, cloakAbility, 1002);
                abilityDefs.RegisterFromEntity(world, empAbility, 1003);
                abilityDefs.RegisterFromEntity(world, healAbility, 1004);

                ref var playerAbilities = ref world.Get<AbilityStateBuffer>(player);
                playerAbilities.AddAbility(1001);
                playerAbilities.AddAbility(1002);
                playerAbilities.AddAbility(1003);
                playerAbilities.AddAbility(1004);

                ref var enemyAbilities = ref world.Get<AbilityStateBuffer>(enemy);
                abilityDefs.RegisterFromEntity(world, revealAbility, 2001);
                enemyAbilities.AddAbility(2001);

                string logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mud_sc2_classics_demo.log");
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][SC2] 你进入战场。");
                sb.AppendLine("[MUD][SC2] 你是陆战队员，敌人有探测与护盾单位。");
                sb.AppendLine("[MUD][SC2] 技能栏：0) 兴奋剂 1) 隐形 2) EMP 3) 治疗");

                void RunFrame(int frame)
                {
                    budget.Reset();
                    clockSystem.Update(1f);
                    timedTags.Update(1f);
                    orderBufferSystem.Update(1f);
                    abilityExec.Update(1f);
                    effectLoop.Update(1f);
                    agg.Update(1f);

                    ref var pa = ref world.Get<AttributeBuffer>(player);
                    ref var ea = ref world.Get<AttributeBuffer>(enemy);
                    ref var eb = ref world.Get<AttributeBuffer>(enemy2);
                    bool cloaked = world.Get<GameplayTagContainer>(player).HasTag(tagCloaked);
                    bool stim = world.Get<GameplayTagContainer>(player).HasTag(tagStim);
                    bool revealed = world.Get<GameplayTagContainer>(player).HasTag(tagRevealed);

                    float asValue = pa.GetCurrent(attrAttackSpeed) * (stim ? 1.5f : 1f);
                    float msValue = pa.GetCurrent(attrMoveSpeed) * (stim ? 1.5f : 1f);
                    sb.AppendLine($"[MUD][SC2][Step={gasClocks.StepNow}] PHP={pa.GetCurrent(attrHealth):F1} PEN={pa.GetCurrent(attrEnergy):F1} AS={asValue:F2} MS={msValue:F2} Cloaked={cloaked} Stim={stim} Revealed={revealed} | E1EN={ea.GetCurrent(attrEnergy):F1} E1SH={ea.GetCurrent(attrShield):F1} E2EN={eb.GetCurrent(attrEnergy):F1} E2SH={eb.GetCurrent(attrShield):F1} | Windows={budget.ResponseWindows} Steps={budget.ResponseSteps} Creates={budget.ResponseCreates}");
                    eventBus.Update();
                }

                incomingOrders.TryEnqueue(new Order { OrderId = 1, OrderTagId = orderCastAbility, Actor = player, Target = player, Args = new OrderArgs { I0 = 0 } });
                sb.AppendLine("[MUD][SC2] 你注射【兴奋剂】。");
                RunFrame(0);

                incomingOrders.TryEnqueue(new Order { OrderId = 2, OrderTagId = orderCastAbility, Actor = player, Target = player, Args = new OrderArgs { I0 = 1 } });
                sb.AppendLine("[MUD][SC2] 你启动【隐形】。");
                RunFrame(1);

                incomingOrders.TryEnqueue(new Order { OrderId = 3, OrderTagId = orderCastAbility, Actor = enemy, Target = player, Args = new OrderArgs { I0 = 0 } });
                sb.AppendLine("[MUD][SC2] 敌方探测对你施加【显形】。");
                RunFrame(2);

                var resp = default(SelectionResponse);
                resp.RequestId = 4;
                resp.ResponseTagId = 900;
                resp.Count = 2;
                unsafe
                {
                    resp.EntityIds[0] = enemy.Id;
                    resp.WorldIds[0] = enemy.WorldId;
                    resp.Versions[0] = enemy.Version;
                    resp.EntityIds[1] = enemy2.Id;
                    resp.WorldIds[1] = enemy2.WorldId;
                    resp.Versions[1] = enemy2.Version;
                }
                selResp.TryAdd(resp);
                incomingOrders.TryEnqueue(new Order { OrderId = 4, OrderTagId = orderCastAbility, Actor = player, Target = enemy, Args = new OrderArgs { I0 = 2 } });
                sb.AppendLine("[MUD][SC2] 你投掷【EMP】覆盖两名敌人。");
                RunFrame(3);

                incomingOrders.TryEnqueue(new Order { OrderId = 5, OrderTagId = orderCastAbility, Actor = player, Target = player, Args = new OrderArgs { I0 = 3 } });
                sb.AppendLine("[MUD][SC2] 你开始【治疗】自己。");
                RunFrame(4);
                RunFrame(5);
                RunFrame(6);

                File.WriteAllText(logPath, sb.ToString());
                Console.WriteLine(sb.ToString());
                Console.WriteLine($"[MUD][SC2] LogFile={logPath}");

                That(world.Get<AttributeBuffer>(player).GetCurrent(attrHealth), Is.LessThan(100f));
                That(world.Get<AttributeBuffer>(enemy).GetCurrent(attrEnergy), Is.LessThan(50f));
                That(world.Get<AttributeBuffer>(enemy).GetCurrent(attrShield), Is.LessThan(40f));
                That(world.Get<GameplayTagContainer>(player).HasTag(tagCloaked), Is.False);
                Pass("SC2 classics demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void MudYgoChainWindow_LifoNegate_DemoLog()
        {
            var world = World.Create();
            try
            {
                int attrHealth = 0;

                int tplFireball = 70;
                int tplChainOpen = 71;
                var templates = new EffectTemplateRegistry();
                var dmg = default(EffectModifiers);
                dmg.Add(attrHealth, ModifierOp.Add, -12f);
                templates.Register(tplFireball, new EffectTemplateData
                {
                    TagId = 200,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = false,
                    Modifiers = dmg
                });
                templates.Register(tplChainOpen, new EffectTemplateData
                {
                    TagId = 220,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                var clock = new DiscreteClock();
                var gasClocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var budget = new GasBudget();
                var effectRequests = new EffectRequestQueue();
                var inputReq = new InputRequestQueue();
                var inputResp = new InputResponseBuffer();
                var selResp = new SelectionResponseBuffer();
                var incomingOrders = new OrderQueue();
                var chainOrders = new OrderQueue();
                var eventBus = new GameplayEventBus();

                const int orderCastAbility = 100;
                const int orderRespondChain = 101;
                const int chainOpenEvent = 220;
                const int inputRequestTag = 221;

                var orderTypeRegistry2 = new OrderTypeRegistry();
                var tagRuleRegistry2 = new TagRuleRegistry();
                OrderTypeConfigLoader.RegisterDefaults(orderTypeRegistry2, tagRuleRegistry2);
                var orderBufferSystem2 = new OrderBufferSystem(world, clock, orderTypeRegistry2, tagRuleRegistry2, incomingOrders, 30, chainOrders, orderRespondChain);
                var effectLoop = new EffectProcessingLoopSystem(world, effectRequests, clock, conditions, budget, templates, inputReq, chainOrders, new ResponseChainTelemetryBuffer(), new OrderRequestQueue());
                var agg = new AttributeAggregatorSystem(world);
                var clockPolicy = new GasClockStepPolicy(1);
                var clockSystem = new GasClockSystem(clock, clockPolicy);

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(chainOpenEvent, ResponseType.PromptInput, priority: 100, effectTemplateId: inputRequestTag);
                    world.Add(listenerEntity, listener);
                }

                var player = world.Create(new AttributeBuffer());
                world.Get<AttributeBuffer>(player).SetBase(attrHealth, 50f);
                var opponent = world.Create(new AttributeBuffer());
                world.Get<AttributeBuffer>(opponent).SetBase(attrHealth, 50f);

                string logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mud_ygo_chain_demo.log");
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][YGO] 你进入决斗。");
                sb.AppendLine("[MUD][YGO] 规则：打开连锁窗口后，双方轮流响应；双方连续 Pass 后 LIFO 结算。");

                byte lastPhase = 0;
                int lastWindows = 0;

                void RunStep()
                {
                    budget.Reset();
                    clockSystem.Update(1f);
                    orderBufferSystem2.Update(1f);
                    effectLoop.Update(1f);
                    agg.Update(1f);
                    lastPhase = effectLoop.DebugProposalWindowPhase;
                    lastWindows = budget.ResponseWindows;
                    sb.AppendLine($"[MUD][YGO][Step={gasClocks.StepNow}] PHP={world.Get<AttributeBuffer>(player).GetCurrent(attrHealth):F1} OPHP={world.Get<AttributeBuffer>(opponent).GetCurrent(attrHealth):F1} Windows={budget.ResponseWindows} Steps={budget.ResponseSteps} Creates={budget.ResponseCreates}");
                    eventBus.Update();
                }

                effectRequests.Publish(new EffectRequest { Source = player, Target = opponent, TemplateId = tplChainOpen });
                sb.AppendLine("[MUD][YGO] 你发动【火球】。连锁窗口打开。");
                RunStep();
                That(lastPhase, Is.EqualTo((byte)2));
                That(lastWindows, Is.EqualTo(1));

                chainOrders.TryEnqueue(new Order { OrderId = 1, OrderTagId = TestGasOrderTags.ChainActivateEffect, Actor = player, Args = new OrderArgs { I0 = tplFireball } });
                sb.AppendLine("[MUD][YGO] 你追加连锁：结算【火球】伤害。");
                RunStep();

                chainOrders.TryEnqueue(new Order { OrderId = 2, OrderTagId = TestGasOrderTags.ChainNegate, Actor = opponent });
                sb.AppendLine("[MUD][YGO] 对手连锁：发动【无效】（negate 上一个链结）。");
                RunStep();

                chainOrders.TryEnqueue(new Order { OrderId = 3, OrderTagId = TestGasOrderTags.ChainPass, Actor = player });
                chainOrders.TryEnqueue(new Order { OrderId = 4, OrderTagId = TestGasOrderTags.ChainPass, Actor = opponent });
                sb.AppendLine("[MUD][YGO] 双方 Pass，连锁窗口关闭，开始 LIFO 结算。");
                RunStep();
                That(lastPhase, Is.EqualTo((byte)0));

                File.WriteAllText(logPath, sb.ToString());
                Console.WriteLine(sb.ToString());
                Console.WriteLine($"[MUD][YGO] LogFile={logPath}");

                That(world.Get<AttributeBuffer>(opponent).GetCurrent(attrHealth), Is.EqualTo(50f));
                Pass("YGO chain demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void MudSc2Stress_Emp2000Targets_ReportsThroughput()
        {
            var world = World.Create();
            try
            {
                int attrEnergy = 1;
                int attrShield = 4;

                int tplEmp = 30;
                var templates = new EffectTemplateRegistry();
                var empMods = default(EffectModifiers);
                empMods.Add(attrEnergy, ModifierOp.Add, -20f);
                empMods.Add(attrShield, ModifierOp.Add, -40f);
                templates.Register(tplEmp, new EffectTemplateData
                {
                    TagId = 104,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = empMods
                });

                var clock = new DiscreteClock();
                var gasClocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var budget = new GasBudget();
                var effectRequests = new EffectRequestQueue();
                var incomingOrders = new OrderQueue();
                var chainOrders = new OrderQueue();
                var eventBus = new GameplayEventBus();
                var inputResp = new InputResponseBuffer();
                var selResp = new SelectionResponseBuffer();
                var abilityDefs = new AbilityDefinitionRegistry();

                const int orderCastAbility = 100;
                const int orderRespondChain = 101;

                var inputReq = new InputRequestQueue();
                var selReq = new SelectionRequestQueue();
                var orderTypeRegistry3 = new OrderTypeRegistry();
                var tagRuleRegistry3 = new TagRuleRegistry();
                OrderTypeConfigLoader.RegisterDefaults(orderTypeRegistry3, tagRuleRegistry3);
                var orderBufferSystem3 = new OrderBufferSystem(world, clock, orderTypeRegistry3, tagRuleRegistry3, incomingOrders, 30, chainOrders, orderRespondChain);
                var abilityExec = new AbilityExecSystem(world, clock, inputReq, inputResp, selReq, selResp, effectRequests, abilityDefs, eventBus, orderCastAbility, orderTypeRegistry: orderTypeRegistry3);
                var effectLoop = new EffectProcessingLoopSystem(world, effectRequests, clock, conditions, budget, templates, null, chainOrders, new ResponseChainTelemetryBuffer(), new OrderRequestQueue())
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };
                var clockPolicy = new GasClockStepPolicy(1);
                var clockSystem = new GasClockSystem(clock, clockPolicy);

                var player = world.Create(new AbilityStateBuffer(), new GameplayTagContainer(), OrderBuffer.CreateEmpty(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer(), new BlackboardIntBuffer());
                var stressEmpExec = default(AbilityExecSpec);
                stressEmpExec.ClockId = GasClockId.Step;
                stressEmpExec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: tplEmp);
                stressEmpExec.SetItem(1, ExecItemKind.End, tick: 0);
                var empAbility = world.Create(new AbilityTemplate(), stressEmpExec);
                abilityDefs.RegisterFromEntity(world, empAbility, 3001);
                ref var abilities = ref world.Get<AbilityStateBuffer>(player);
                abilities.AddAbility(3001);

                int targetsCount = 2000;
                var targets = new Entity[targetsCount];
                for (int i = 0; i < targets.Length; i++)
                {
                    targets[i] = world.Create(new AttributeBuffer());
                    ref var attr = ref world.Get<AttributeBuffer>(targets[i]);
                    attr.SetBase(attrEnergy, 50f);
                    attr.SetBase(attrShield, 40f);
                }

                for (int i = 0; i < 2; i++)
                {
                    clockSystem.Update(1f);
                    for (int t = 0; t < targets.Length; t++)
                    {
                        incomingOrders.TryEnqueue(new Order { OrderId = (i * targets.Length) + t + 1, OrderTagId = orderCastAbility, Actor = player, Target = targets[t], Args = new OrderArgs { I0 = 0 } });
                    }
                    orderBufferSystem3.Update(1f);
                    abilityExec.Update(1f);
                    effectLoop.Update(1f);
                    eventBus.Update();
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int logicFrames = 5;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_0 = GC.CollectionCount(0);
                int gen1_0 = GC.CollectionCount(1);
                int gen2_0 = GC.CollectionCount(2);
                long ticksClock = 0;
                long ticksEnqueue = 0;
                long ticksDispatch = 0;
                long ticksAbility = 0;
                long ticksEffect = 0;
                long ticksEvent = 0;

                int totalRoots = 0;
                int totalWindows = 0;
                int totalSteps = 0;
                int totalCreates = 0;

                for (int frame = 0; frame < logicFrames; frame++)
                {
                    budget.Reset();
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    clockSystem.Update(1f);
                    ticksClock += System.Diagnostics.Stopwatch.GetTimestamp() - t0;

                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (int t = 0; t < targets.Length; t++)
                    {
                        incomingOrders.TryEnqueue(new Order { OrderId = (frame * targets.Length) + t + 100000, OrderTagId = orderCastAbility, Actor = player, Target = targets[t], Args = new OrderArgs { I0 = 0 } });
                    }
                    ticksEnqueue += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    totalRoots += targets.Length;

                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    orderBufferSystem3.Update(1f);
                    ticksDispatch += System.Diagnostics.Stopwatch.GetTimestamp() - t0;

                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    abilityExec.Update(1f);
                    ticksAbility += System.Diagnostics.Stopwatch.GetTimestamp() - t0;

                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    effectLoop.Update(1f);
                    ticksEffect += System.Diagnostics.Stopwatch.GetTimestamp() - t0;

                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    eventBus.Update();
                    ticksEvent += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    totalWindows += budget.ResponseWindows;
                    totalSteps += budget.ResponseSteps;
                    totalCreates += budget.ResponseCreates;
                }

                long alloc1 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_1 = GC.CollectionCount(0);
                int gen1_1 = GC.CollectionCount(1);
                int gen2_1 = GC.CollectionCount(2);
                sw.Stop();

                double perRootUs = (sw.Elapsed.TotalMilliseconds * 1000.0) / totalRoots;
                Console.WriteLine($"[MUD][SC2][STRESS] Roots={totalRoots} Frames={logicFrames} ElapsedMs={sw.Elapsed.TotalMilliseconds:F1} PerRootUs={perRootUs:F3}");
                Console.WriteLine($"[MUD][SC2][STRESS] ResponseWindows={totalWindows} ResponseSteps={totalSteps} ResponseCreates={totalCreates}");
                Console.WriteLine($"[MUD][SC2][STRESS] AllocBytes(CurrentThread)={alloc1 - alloc0}");
                Console.WriteLine($"[MUD][SC2][STRESS] GC Collections Δ: Gen0={gen0_1 - gen0_0} Gen1={gen1_1 - gen1_0} Gen2={gen2_1 - gen2_0}");

                double msClock = ticksClock * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msEnqueue = ticksEnqueue * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msDispatch = ticksDispatch * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msAbility = ticksAbility * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msEffect = ticksEffect * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msEvent = ticksEvent * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msTotalSegments = msClock + msEnqueue + msDispatch + msAbility + msEffect + msEvent;
                Console.WriteLine($"[MUD][SC2][STRESS] TimeMs: Clock={msClock:F2} Enqueue={msEnqueue:F2} Dispatch={msDispatch:F2} AbilityTask={msAbility:F2} EffectLoop={msEffect:F2} EventBus={msEvent:F2} Sum={msTotalSegments:F2}");

                Pass("SC2 stress demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
