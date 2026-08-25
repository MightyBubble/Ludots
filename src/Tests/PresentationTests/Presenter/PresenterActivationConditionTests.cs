using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Arch.Core;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// BehaviorSlot.activationCondition 闭环：配置 → loader 编译的 PresenterCreated 规则对 →
    /// PresenterRuleSystem 消费条件结果 → ActivateBehavior/DeactivateBehavior 命令 → active mask →
    /// PresenterBehaviorSystem 输出。条件求值复用 ConditionRef，不新增状态机；激活条件是
    /// root-presenter 契约（实例创建时一次性求值），tag/attribute 驱动的运行时切换走
    /// authored keyed 规则（TagEffectiveChanged + TagGained/TagLost）。
    /// </summary>
    [TestFixture]
    public sealed class PresenterActivationConditionTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterActivationConditionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            TagRegistry.Clear();
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private const int ConditionedSlot = 6;
        private const int ConditionedSoundAssetId = 501;

        private const string ConditionedDefinitionJson = """
            [
              {
                "id": "accept.activation.beacon",
                "behaviors": [
                  {
                    "slot": "sound",
                    "kind": "Sound",
                    "activeByDefault": false,
                    "activationCondition": { "inline": "SourceHasVisualTransform" },
                    "sound": { "soundAssetId": "sfx_cond", "loop": false, "volume": 1.0 }
                  }
                ]
              }
            ]
            """;

        [Test]
        public void ConditionFalse_KeepsSlotInactive_AndEmitsDeactivateCommand()
        {
            using var fixture = CreateConfigFixture(ConditionedDefinitionJson);
            int defId = fixture.Definitions.GetId("accept.activation.beacon");
            Assert.That(defId, Is.GreaterThan(0));

            Entity owner = fixture.World.Create();
            var (presenter, commands, sounds) = fixture.CreateAndDecide(defId, owner, scopeTag: 1);

            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commands[0].TargetBehaviorSlot, Is.EqualTo(ConditionedSlot));

            uint mask = fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask;
            Assert.That(mask & (1u << ConditionedSlot), Is.EqualTo(0u), "条件 false 时槽位必须保持不激活");
            Assert.That(CountPlayRequests(sounds), Is.EqualTo(0), "条件 false 时不得有输出");
        }

        [Test]
        public void ConditionTrue_ActivatesSlot_AndEmitsActivateCommand()
        {
            using var fixture = CreateConfigFixture(ConditionedDefinitionJson);
            int defId = fixture.Definitions.GetId("accept.activation.beacon");

            Entity owner = fixture.World.Create(new VisualTransform { Position = new System.Numerics.Vector3(1f, 0f, 1f) });
            var (presenter, commands, sounds) = fixture.CreateAndDecide(defId, owner, scopeTag: 1);

            Assert.That(commands.Count, Is.EqualTo(2));
            Assert.That(commands[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commands[1].CommandKind, Is.EqualTo(PresenterCommandKind.ActivateBehavior));
            Assert.That(commands[1].TargetBehaviorSlot, Is.EqualTo(ConditionedSlot));

            uint mask = fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask;
            Assert.That(mask & (1u << ConditionedSlot), Is.Not.EqualTo(0u), "条件 true 时槽位必须激活");
            Assert.That(CountPlayRequests(sounds), Is.EqualTo(1), "条件 true 时输出必须出现");
        }

        [Test]
        public void ConditionFlip_FalseThenTrue_EmitsDeactivateThenActivateCommands()
        {
            using var fixture = CreateConfigFixture(ConditionedDefinitionJson);
            int defId = fixture.Definitions.GetId("accept.activation.beacon");

            Entity ownerA = fixture.World.Create();
            var (presenterA, commandsA, soundsA) = fixture.CreateAndDecide(defId, ownerA, scopeTag: 10);
            Assert.That(commandsA.Count, Is.EqualTo(1));
            Assert.That(commandsA[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenterA).BehaviorActiveMask & (1u << ConditionedSlot), Is.EqualTo(0u));
            Assert.That(CountPlayRequests(soundsA), Is.EqualTo(0));
            uint maskA = fixture.World.Get<PresenterState>(presenterA).BehaviorActiveMask;

            Entity ownerB = fixture.World.Create(new VisualTransform { Position = new System.Numerics.Vector3(5f, 0f, 5f) });
            var (presenterB, commandsB, soundsB) = fixture.CreateAndDecide(defId, ownerB, scopeTag: 11);
            Assert.That(commandsB.Count, Is.EqualTo(2));
            Assert.That(commandsB[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commandsB[1].CommandKind, Is.EqualTo(PresenterCommandKind.ActivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenterB).BehaviorActiveMask & (1u << ConditionedSlot), Is.Not.EqualTo(0u));
            Assert.That(CountPlayRequests(soundsB), Is.EqualTo(1));

            Assert.That(fixture.World.Get<PresenterState>(presenterA).OwnerEntity, Is.EqualTo(ownerA));
            Assert.That(fixture.World.Get<PresenterState>(presenterB).OwnerEntity, Is.EqualTo(ownerB));
            Assert.That(
                fixture.World.Get<PresenterState>(presenterA).BehaviorActiveMask,
                Is.EqualTo(maskA),
                "创建 presenter B 后，presenter A 的 BehaviorActiveMask 必须保持不变："
                + "编译的 PresenterCreated 规则只路由到刚创建的实例（PresenterRuleSystem 对 PresenterCreated 事件 "
                + "解析事件携带的 presenter 实体），不得扇出到同一 definition 的姐妹实例。");
        }

        [Test]
        public void ConditionFlip_TrueThenFalse_KeepsSiblingActive()
        {
            using var fixture = CreateConfigFixture(ConditionedDefinitionJson);
            int defId = fixture.Definitions.GetId("accept.activation.beacon");

            Entity ownerA = fixture.World.Create(new VisualTransform { Position = new System.Numerics.Vector3(5f, 0f, 5f) });
            var (presenterA, commandsA, soundsA) = fixture.CreateAndDecide(defId, ownerA, scopeTag: 20);
            Assert.That(commandsA.Count, Is.EqualTo(2));
            Assert.That(commandsA[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commandsA[1].CommandKind, Is.EqualTo(PresenterCommandKind.ActivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenterA).BehaviorActiveMask & (1u << ConditionedSlot), Is.Not.EqualTo(0u));
            Assert.That(CountPlayRequests(soundsA), Is.EqualTo(1));
            uint maskA = fixture.World.Get<PresenterState>(presenterA).BehaviorActiveMask;

            Entity ownerB = fixture.World.Create(); // 无 VisualTransform → 条件 false
            var (presenterB, commandsB, soundsB) = fixture.CreateAndDecide(defId, ownerB, scopeTag: 21);
            Assert.That(commandsB.Count, Is.EqualTo(1));
            Assert.That(commandsB[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenterB).BehaviorActiveMask & (1u << ConditionedSlot), Is.EqualTo(0u));
            Assert.That(CountPlayRequests(soundsB), Is.EqualTo(CountPlayRequests(soundsA)),
                "B 条件 false 不得新增任何输出：缓冲里唯一的声音请求仍是 A 的常驻 loop 声（数量与 A 创建后一致）。");

            Assert.That(
                fixture.World.Get<PresenterState>(presenterA).BehaviorActiveMask,
                Is.EqualTo(maskA),
                "创建条件 false 的 presenter B 后，条件 true 的 presenter A 必须保持激活："
                + "编译的 PresenterCreated 规则只路由到事件携带的 presenter 实体（PresenterRuleSystem 对 PresenterCreated "
                + "解析事件携带的 presenter 实体），不得扇出到同一 definition 的姐妹实例。");
        }

        [Test]
        public void EngineSchedule_RulesBeforeRuntimeBeforeBehavior_PreventsOneFrameActivationOutput()
        {
            // 生产注册序（GameEngine.InitializeWithConfigPipelineInternal）：
            //   presenterRuleSystem → presenterRuntimeSystem → … → presenterBehaviorSystem
            // 同一帧内：规则产出命令 → 运行时按序应用到 mask（无条件 Deactivate 先于条件 Activate，
            // PresenterRuntimeSystem.Update 在同一遍历中按序消费整批命令）→ 行为系统才读 mask 输出。
            // 创建帧的 mask 由 BuildDefaultBehaviorMask 决定，而 loader 对带 activationCondition 的槽强制
            // ActiveByDefault=false，因此条件首次求值（下一帧规则序）之前不可能出现一帧激活输出。
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine("LudotsCoreMod");

            FieldInfo field = typeof(GameEngine).GetField("_presentationSystems", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine._presentationSystems field missing.");
            var systems = field.GetValue(engine) as List<ISystem<float>>
                ?? throw new InvalidOperationException("GameEngine presentation systems missing.");

            int rulesIndex = FindSystemIndex<PresenterRuleSystem>(systems);
            int runtimeIndex = FindSystemIndex<PresenterRuntimeSystem>(systems);
            int behaviorIndex = FindSystemIndex<PresenterBehaviorSystem>(systems);

            Assert.That(rulesIndex, Is.GreaterThanOrEqualTo(0), "PresenterRuleSystem 必须注册。");
            Assert.That(runtimeIndex, Is.GreaterThan(rulesIndex),
                "PresenterRuleSystem 必须先于 PresenterRuntimeSystem：activationCondition 编译出的命令必须在同一帧内应用到 mask。");
            Assert.That(behaviorIndex, Is.GreaterThan(runtimeIndex),
                "PresenterRuntimeSystem 必须先于 PresenterBehaviorSystem：行为输出前 mask 必须是最终条件结果，否则条件槽会出现一帧激活输出。");
        }

        private static int FindSystemIndex<T>(List<ISystem<float>> systems)
        {
            for (int i = 0; i < systems.Count; i++)
            {
                if (systems[i] is T)
                {
                    return i;
                }
            }

            return -1;
        }

        [Test]
        public void UnconditionalBehavior_IsUnchangedByCompiledRules()
        {
            using var fixture = CreateConfigFixture(
                """
                [
                  {
                    "id": "accept.activation.plain",
                    "behaviors": [
                      {
                        "slot": "sound",
                        "kind": "Sound",
                        "activeByDefault": true,
                        "sound": { "soundAssetId": "sfx_plain", "loop": false, "volume": 1.0 }
                      }
                    ]
                  }
                ]
                """);
            int defId = fixture.Definitions.GetId("accept.activation.plain");

            Entity owner = fixture.World.Create(new VisualTransform { Position = default });
            var (presenter, commands, sounds) = fixture.CreateAndDecide(defId, owner, scopeTag: 1);

            Assert.That(commands.Count, Is.EqualTo(0), "无 activationCondition 的槽位不得产生编译规则命令");
            Assert.That(fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask & (1u << ConditionedSlot), Is.Not.EqualTo(0u), "activeByDefault 行为保持原样");
            Assert.That(CountPlayRequests(sounds), Is.EqualTo(1), "activeByDefault 输出保持原样");
        }

        [Test]
        public void GraphCondition_TrueProgram_ActivatesSlot()
        {
            using var fixture = CreateConfigFixture(
                """
                [
                  {
                    "id": "accept.activation.graph.true",
                    "behaviors": [
                      {
                        "slot": "sound",
                        "kind": "Sound",
                        "activeByDefault": false,
                        "activationCondition": { "graphProgramId": 9001 },
                        "sound": { "soundAssetId": "sfx_cond", "loop": false, "volume": 1.0 }
                      }
                    ]
                  }
                ]
                """,
                graphs => graphs.Register(9001, ValidationProgramInstructions(result: true), GraphKind.Validation));
            int defId = fixture.Definitions.GetId("accept.activation.graph.true");

            Entity owner = fixture.World.Create();
            var (presenter, commands, sounds) = fixture.CreateAndDecide(defId, owner, scopeTag: 1);

            Assert.That(commands.Count, Is.EqualTo(2));
            Assert.That(commands[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commands[1].CommandKind, Is.EqualTo(PresenterCommandKind.ActivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask & (1u << ConditionedSlot), Is.Not.EqualTo(0u), "graph 条件 true 时槽位必须激活");
            Assert.That(CountPlayRequests(sounds), Is.EqualTo(1), "graph 条件 true 时输出必须出现");
        }

        [Test]
        public void GraphCondition_FalseProgram_KeepsSlotInactive()
        {
            using var fixture = CreateConfigFixture(
                """
                [
                  {
                    "id": "accept.activation.graph.false",
                    "behaviors": [
                      {
                        "slot": "sound",
                        "kind": "Sound",
                        "activeByDefault": false,
                        "activationCondition": { "graphProgramId": 9002 },
                        "sound": { "soundAssetId": "sfx_cond", "loop": false, "volume": 1.0 }
                      }
                    ]
                  }
                ]
                """,
                graphs => graphs.Register(9002, ValidationProgramInstructions(result: false), GraphKind.Validation));
            int defId = fixture.Definitions.GetId("accept.activation.graph.false");

            Entity owner = fixture.World.Create();
            var (presenter, commands, sounds) = fixture.CreateAndDecide(defId, owner, scopeTag: 1);

            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask & (1u << ConditionedSlot), Is.EqualTo(0u), "graph 条件 false 时槽位必须保持不激活");
            Assert.That(CountPlayRequests(sounds), Is.EqualTo(0), "graph 条件 false 时不得有输出");
        }

        [Test]
        public void GraphCondition_WithoutResolver_IsRejectedAtLoad()
        {
            WriteCatalog();
            WritePresenters(
                """
                [
                  {
                    "id": "accept.activation.graph.noresolver",
                    "behaviors": [
                      {
                        "slot": "sound",
                        "kind": "Sound",
                        "activeByDefault": false,
                        "activationCondition": { "graphProgramId": 777 },
                        "sound": { "soundAssetId": "sfx_cond", "loop": false, "volume": 1.0 }
                      }
                    ]
                  }
                ]
                """);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (_, key) => string.Equals(key, "sfx_cond", StringComparison.Ordinal)
                    ? ConditionedSoundAssetId
                    : 1);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("graphProgramId"));
            Assert.That(ex.Message, Does.Contain("777"));
            Assert.That(ex.Message, Does.Contain("resolveGraphProgramKind"), "缺 resolver 时错误信息必须给出可行动的修复指引。");
        }

        [Test]
        public void Acceptance_ConditionDrivenActivation_FlipsCommandsMaskAndOutput_WriteArtifacts()
        {
            using var fixture = CreateConfigFixture(ConditionedDefinitionJson);
            int defId = fixture.Definitions.GetId("accept.activation.beacon");
            Assert.That(defId, Is.GreaterThan(0), "配置加载后应能取到 definition id");

            var trace = new List<string>();
            var timeline = new List<string>();
            var commandLog = new List<(int Step, PresenterCommandKind Kind, string Unit, int Slot)>();
            int step = 0;

            void Decide(Entity owner, string unit, bool withTransform)
            {
                step++;
                if (withTransform)
                {
                    fixture.World.Add(owner, new VisualTransform { Position = new System.Numerics.Vector3(step, 0f, 0f) });
                }

                Assert.That(fixture.Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                    PresenterDefinitionId = defId,
                    ParentEntity = Entity.Null,
                    ScopeTag = step,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = owner,
                    Target = owner,
                }), Is.True);
                fixture.Runtime.Update(0.016f);

                Entity presenter = Entity.Null;
                ReadOnlySpan<PresentationEvent> events = fixture.Events.GetSpan();
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].Kind == PresentationEventKind.PresenterCreated && events[i].PresenterEntity != Entity.Null)
                    {
                        presenter = events[i].PresenterEntity;
                    }
                }

                Assert.That(presenter, Is.Not.EqualTo(Entity.Null), "每个 owner 都应建出 presenter 实例");
                ref readonly PresenterState state = ref fixture.World.Get<PresenterState>(presenter);
                trace.Add(FormattableString.Invariant(
                    $$"""{"step":{{step}},"type":"event","kind":"PresenterCreated","unit":"{{unit}}","transform":{{withTransform.ToString().ToLowerInvariant()}},"presenter_stable_id":{{state.StableId}}}"""));
                timeline.Add($"- [S{step:000}] 单位 {unit}（VisualTransform={(withTransform ? "有" : "无")}）→ PresenterCreated");

                fixture.Rules.Update(0.016f);
                ReadOnlySpan<PresenterCommand> commands = fixture.Commands.GetSpan();
                for (int i = 0; i < commands.Length; i++)
                {
                    commandLog.Add((step, commands[i].CommandKind, unit, commands[i].TargetBehaviorSlot));
                    trace.Add(FormattableString.Invariant(
                        $$"""{"step":{{step}},"type":"command","kind":"{{commands[i].CommandKind}}","unit":"{{unit}}","targetBehaviorSlot":{{commands[i].TargetBehaviorSlot}}}"""));
                    timeline.Add($"- [S{step:000}] 单位 {unit} → {commands[i].CommandKind}（slot {commands[i].TargetBehaviorSlot}）");
                }

                fixture.Runtime.Update(0.016f);
                uint mask = fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask;
                bool active = (mask & (1u << ConditionedSlot)) != 0u;
                trace.Add(FormattableString.Invariant(
                    $$"""{"step":{{step}},"type":"mask","unit":"{{unit}}","activeMask":{{mask}},"slotActive":{{active.ToString().ToLowerInvariant()}}}"""));
                timeline.Add($"- [S{step:000}] 单位 {unit} → active mask=0x{mask:X8}，声音槽 {(active ? "激活" : "不激活")}");

                fixture.SoundRequests.Clear();
                fixture.Behavior.Update(0f);
                int plays = 0;
                ReadOnlySpan<SoundRequest> sounds = fixture.SoundRequests.GetSpan();
                for (int i = 0; i < sounds.Length; i++)
                {
                    if (sounds[i].Kind == SoundRequestKind.PlayOrUpdate && sounds[i].SoundAssetId == ConditionedSoundAssetId)
                    {
                        plays++;
                    }
                }

                trace.Add(FormattableString.Invariant(
                    $$"""{"step":{{step}},"type":"output","unit":"{{unit}}","playOrUpdate":{{plays}}}"""));
                timeline.Add($"- [S{step:000}] 单位 {unit} → 输出 {(plays > 0 ? "出现（声音请求）" : "不出现")}");

                Assert.That(active, Is.EqualTo(withTransform), "active mask 必须与条件结果一致");
                Assert.That(plays, Is.EqualTo(withTransform ? 1 : 0), "输出可见性必须与行为状态一致");
            }

            Entity unitA = fixture.World.Create();
            Decide(unitA, "A", withTransform: false);
            Entity unitB = fixture.World.Create();
            Decide(unitB, "B", withTransform: true);

            Assert.That(commandLog, Has.Count.EqualTo(3), "false 一条 Deactivate，true 两条（Deactivate+Activate）");
            Assert.That(commandLog[0].Kind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commandLog[1].Kind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(commandLog[2].Kind, Is.EqualTo(PresenterCommandKind.ActivateBehavior));
            Assert.That(commandLog[0].Unit, Is.EqualTo("A"));
            Assert.That(commandLog[2].Unit, Is.EqualTo("B"));

            string artifactDir = Path.Combine(
                PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "evidence",
                "presenter-1096");
            Directory.CreateDirectory(artifactDir);

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), string.Join('\n', trace) + "\n");

            File.WriteAllText(
                Path.Combine(artifactDir, "path.mmd"),
                """
                flowchart TD
                    CFG[BehaviorSlot.activationCondition] --> COMPILE[loader 编译 PresenterCreated 规则对]
                    COMPILE --> D1[规则1: 无条件 DeactivateBehavior]
                    COMPILE --> A1[规则2: condition → ActivateBehavior]
                    CREATE[PresenterCreated 事件] --> RULES[PresenterRuleSystem 消费条件结果]
                    RULES --> D1
                    RULES --> A1
                    D1 --> MASK[active mask 同一表现周期更新]
                    A1 --> MASK
                    MASK --> OUT[PresenterBehaviorSystem 按 mask 输出]
                    A1 -. 条件 true .-> ACT[槽激活 → 输出出现]
                    D1 -. 条件 false .-> DEACT[槽不激活 → 无输出]
                """ + "\n");

            var report = new StringBuilder();
            report.AppendLine("# Scenario: presenter-1096-activation-condition");
            report.AppendLine();
            report.AppendLine("## Header");
            report.AppendLine("- scenario name: BehaviorSlot.activationCondition closed loop (config → compiled rule pair → RuleSystem → command → active mask → output)");
            report.AppendLine("- build/version: local PresentationTests, real JSON config pipeline (PresenterDefinitionConfigLoader)");
            report.AppendLine("- seed/map/clock: deterministic fixture / in-memory world / render dt 0.016s per step");
            report.AppendLine(FormattableString.Invariant($"- execution timestamp: {DateTimeOffset.UtcNow:O}"));
            report.AppendLine();
            report.AppendLine("## Timeline");
            foreach (string entry in timeline)
            {
                report.AppendLine(entry);
            }

            report.AppendLine();
            report.AppendLine("## Outcome");
            report.AppendLine("- success/failure decision: success");
            report.AppendLine("- failed assertions: none");
            report.AppendLine("- reason codes: condition_false_inactive, condition_true_active, flip_emits_deactivate_then_activate, mask_matches_condition");
            report.AppendLine();
            report.AppendLine("## Summary Stats");
            report.AppendLine(FormattableString.Invariant($"- DeactivateBehavior commands: {CountCommands(commandLog, PresenterCommandKind.DeactivateBehavior)} (unit A false + unit B reset)"));
            report.AppendLine(FormattableString.Invariant($"- ActivateBehavior commands: {CountCommands(commandLog, PresenterCommandKind.ActivateBehavior)} (unit B true only)"));
            report.AppendLine("- slot under test: sound (index 6)");
            string reportText = report.ToString();
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), reportText);
            TestContext.Out.WriteLine(reportText);
        }

        [Test]
        public void ConditionFalse_OverridesActiveByDefault_KeepsSlotInactive()
        {
            using var fixture = CreateConfigFixture(
                """
                [
                  {
                    "id": "accept.activation.default",
                    "behaviors": [
                      {
                        "slot": "sound",
                        "kind": "Sound",
                        "activeByDefault": true,
                        "activationCondition": { "inline": "SourceHasVisualTransform" },
                        "sound": { "soundAssetId": "sfx_cond", "loop": false, "volume": 1.0 }
                      }
                    ]
                  }
                ]
                """);
            int defId = fixture.Definitions.GetId("accept.activation.default");

            // loader 对带 activationCondition 的槽强制 ActiveByDefault=false（条件为唯一权威）：
            // 真实引擎里创建帧的 mask 由 BuildDefaultBehaviorMask 决定且 BehaviorSystem 在 RuntimeSystem
            // 之后运行，若保持 activeByDefault=true 会产生条件首次求值前的一帧激活输出窗口。
            Assert.That(fixture.Definitions.Get(defId).Behaviors[0].ActiveByDefault, Is.False,
                "activationCondition 存在时 loader 必须强制 ActiveByDefault=false");

            Entity owner = fixture.World.Create(); // 无 VisualTransform → 条件 false
            var (presenter, commands, sounds) = fixture.CreateAndDecide(defId, owner, scopeTag: 1);

            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].CommandKind, Is.EqualTo(PresenterCommandKind.DeactivateBehavior));
            Assert.That(fixture.World.Get<PresenterState>(presenter).BehaviorActiveMask & (1u << ConditionedSlot), Is.EqualTo(0u), "条件 false 必须压过 activeByDefault: true");
            Assert.That(CountPlayRequests(sounds), Is.EqualTo(0), "条件 false 时不得有输出");
        }

        private static int CountPlayRequests(IReadOnlyList<SoundRequest> sounds)
        {
            int count = 0;
            for (int i = 0; i < sounds.Count; i++)
            {
                if (sounds[i].Kind == SoundRequestKind.PlayOrUpdate)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountCommands(
            IReadOnlyList<(int Step, PresenterCommandKind Kind, string Unit, int Slot)> commandLog,
            PresenterCommandKind kind)
        {
            int count = 0;
            for (int i = 0; i < commandLog.Count; i++)
            {
                if (commandLog[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Validation 图：B[0] = result，然后停机。与 PresenterRuleSystem.EvaluateGraph 的求值契约一致。</summary>
        private static GraphInstruction[] ValidationProgramInstructions(bool result)
        {
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = result ? 1 : 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
        }

        private ActivationFixture CreateConfigFixture(string presentersJson, Action<GraphProgramRegistry> registerPrograms = null)
        {
            WriteCatalog();
            WritePresenters(presentersJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var graphs = new GraphProgramRegistry();
            registerPrograms?.Invoke(graphs);
            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (_, key) => string.Equals(key, "sfx_cond", StringComparison.Ordinal)
                    ? ConditionedSoundAssetId
                    : 1,
                resolveGraphProgramKind: graphId => graphs.TryGetKind(graphId, out GraphKind kind) ? kind : GraphKind.None).Load(catalog);
            return new ActivationFixture(registry, graphs);
        }

        private void WriteCatalog()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
        }

        private void WritePresenters(string content)
        {
            WriteFile("Core", "Presentation/presenters.json", content);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private sealed class ActivationFixture : IDisposable
        {
            public readonly World World;
            public readonly PresenterCommandBuffer Commands;
            public readonly PresentationEventStream Events;
            public readonly PresenterEntityRuntime Instances;
            public readonly PresenterDefinitionRegistry Definitions;
            public readonly SoundRequestBuffer SoundRequests;
            public readonly PresenterBehaviorSystem Behavior;
            public readonly PresenterRuntimeSystem Runtime;
            public readonly PresenterRuleSystem Rules;
            public readonly GraphProgramRegistry Graphs;

            public ActivationFixture(PresenterDefinitionRegistry definitions, GraphProgramRegistry graphs)
            {
                World = World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Instances = new PresenterEntityRuntime(World);
                Definitions = definitions;
                SoundRequests = new SoundRequestBuffer();
                Graphs = graphs;
                var ownerChanges = new PresentationOwnerChangeBuffer(64);
                Runtime = new PresenterRuntimeSystem(
                    World,
                    Commands,
                    Events,
                    new TransientMarkerBuffer(),
                    new PresentationRequestBuffer(),
                    Instances,
                    new PresentationStableIdAllocator(),
                    Definitions);
                Rules = new PresenterRuleSystem(
                    World,
                    Events,
                    Commands,
                    Definitions,
                    Instances,
                    Graphs,
                    new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(World, spatialQueries: null, coords: null, eventBus: null),
                    new Dictionary<string, object>());
                Behavior = new PresenterBehaviorSystem(World, Instances, Definitions, Events, ownerChanges, SoundRequests);
            }

            /// <summary>生产序：Runtime 建实例并发 PresenterCreated → Rules 求值条件发命令 → Runtime 应用 mask → Behavior 输出。</summary>
            public (Entity Presenter, List<PresenterCommand> Commands, List<SoundRequest> Sounds) CreateAndDecide(
                int defId,
                Entity owner,
                int scopeTag)
            {
                Assert.That(Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                    PresenterDefinitionId = defId,
                    ParentEntity = Entity.Null,
                    ScopeTag = scopeTag,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = owner,
                    Target = owner,
                }), Is.True);

                Runtime.Update(0.016f);
                Entity presenter = CaptureCreatedPresenter();
                Rules.Update(0.016f);
                var commands = new List<PresenterCommand>();
                ReadOnlySpan<PresenterCommand> cmdSpan = Commands.GetSpan();
                for (int i = 0; i < cmdSpan.Length; i++)
                {
                    commands.Add(cmdSpan[i]);
                }

                Runtime.Update(0.016f);
                SoundRequests.Clear();
                Behavior.Update(0f);
                var sounds = new List<SoundRequest>();
                ReadOnlySpan<SoundRequest> soundSpan = SoundRequests.GetSpan();
                for (int i = 0; i < soundSpan.Length; i++)
                {
                    sounds.Add(soundSpan[i]);
                }

                return (presenter, commands, sounds);
            }

            private Entity CaptureCreatedPresenter()
            {
                ReadOnlySpan<PresentationEvent> span = Events.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i].Kind == PresentationEventKind.PresenterCreated && span[i].PresenterEntity != Entity.Null)
                    {
                        return span[i].PresenterEntity;
                    }
                }

                Assert.Fail("CreatePresenter 应发布 PresenterCreated 事件");
                return Entity.Null;
            }

            public void Dispose()
            {
                Rules.Dispose();
                Runtime.Dispose();
                Behavior.Dispose();
                World.Dispose();
            }
        }
    }
}
