using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
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
    /// lifecycle.durationSeconds 迁移验收：duration 在加载期编译为
    /// PresenterCreated→TimerSet 与 TimerExpired→DestroyPresenter 两条规则，
    /// 到期链路严格为 TimerSet → TimerExpired → Rule → DestroyPresenter，
    /// EmitSystem 不再按 lifetime 直接销毁。
    /// </summary>
    [TestFixture]
    public sealed class PresenterDurationChainTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterDurationChainTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterTimerNameRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterTimerNameRegistry.Clear();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        // ── 编译期：duration 只产生 TimerSet 计划与 Destroy 规则 ──

        [Test]
        public void ConfigLoader_Duration_CompilesToTimerSetAndDestroyRules()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  {
                    "id": "dur.marker",
                    "lifecycle": { "durationSeconds": 0.4 },
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Dur.Hit" },
                        "command": { "kind": "SetParam", "paramKey": "dur.tint", "paramLane": "Float", "valueSource": "Fixed", "paramValue": 1.0 }
                      }
                    ]
                  }
                ]
                """);

            PresenterDefinitionRegistry registry = LoadDefinitions();
            PresenterDefinition def = registry.Get(registry.GetId("dur.marker"));

            Assert.That(def.DefaultLifetime, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(def.Rules, Has.Length.EqualTo(3), "authored rule + compiled TimerSet/Destroy pair");

            PresenterRule setRule = def.Rules[1];
            Assert.That(setRule.Event.Kind, Is.EqualTo(PresentationEventKind.PresenterCreated));
            Assert.That(setRule.Event.KeyId, Is.EqualTo(def.Id));
            Assert.That(setRule.Command.CommandKind, Is.EqualTo(PresenterCommandKind.TimerSet));
            Assert.That(setRule.Command.RouteStrategy, Is.EqualTo(PerformerCommandRouteStrategy.ExistingInstances));
            Assert.That(setRule.Command.TimerNameId,
                Is.EqualTo(PresenterTimerNameRegistry.GetId(PresenterTimerNameRegistry.DurationTimerName)));
            Assert.That(setRule.Command.TimerDurationSeconds, Is.EqualTo(0.4f).Within(0.0001f));

            PresenterRule destroyRule = def.Rules[2];
            Assert.That(destroyRule.Event.Kind, Is.EqualTo(PresentationEventKind.TimerExpired));
            Assert.That(destroyRule.Event.KeyId,
                Is.EqualTo(PresenterTimerNameRegistry.GetId(PresenterTimerNameRegistry.DurationTimerName)));
            Assert.That(destroyRule.Command.CommandKind, Is.EqualTo(PresenterCommandKind.DestroyPresenter));
            Assert.That(destroyRule.Command.RouteStrategy, Is.EqualTo(PerformerCommandRouteStrategy.ExistingInstances));

            Assert.That(registry.HasPresenterCreatedRules, Is.True);
        }

        [Test]
        public void ConfigLoader_DurationWithoutRules_StillCompilesChain()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.bare", "lifecycle": { "durationSeconds": 1.2 } } ]
                """);

            PresenterDefinitionRegistry registry = LoadDefinitions();
            PresenterDefinition def = registry.Get(registry.GetId("dur.bare"));

            Assert.That(def.Rules, Has.Length.EqualTo(2));
            Assert.That(def.Rules[0].Command.CommandKind, Is.EqualTo(PresenterCommandKind.TimerSet));
            Assert.That(def.Rules[1].Command.CommandKind, Is.EqualTo(PresenterCommandKind.DestroyPresenter));
        }

        [TestCase(0)]
        [TestCase(-0.5)]
        public void ConfigLoader_NonPositiveDuration_Throws(double durationSeconds)
        {
            string presentersJson = string.Create(
                CultureInfo.InvariantCulture,
                $"[ {{ \"id\": \"dur.bad\", \"lifecycle\": {{ \"durationSeconds\": {durationSeconds} }} }} ]");
            InvalidOperationException ex = AssertLoaderThrows(presentersJson);
            Assert.That(ex!.Message, Does.Contain("durationSeconds"));
        }

        [Test]
        public void ConfigLoader_AuthoredReservedTimerName_Throws()
        {
            InvalidOperationException ex = AssertLoaderThrows($$"""
                [
                  {
                    "id": "dur.reserved",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Dur.Hit" },
                        "command": { "kind": "TimerSet", "timerName": "{{PresenterTimerNameRegistry.DurationTimerName}}", "durationSeconds": 0.5 }
                      }
                    ]
                  }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("reserved"));
        }

        [Test]
        public void ConfigLoader_DurationPresenterAsChild_Throws()
        {
            InvalidOperationException ex = AssertLoaderThrows("""
                [
                  { "id": "dur.child", "lifecycle": { "durationSeconds": 0.5 } },
                  { "id": "dur.parent", "children": [ { "definitionId": "dur.child" } ] }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("duration-authored"));
        }

        // ── 运行期：TimerSet → TimerExpired → Rule → DestroyPresenter 逐拍链路 ──

        [Test]
        public void Chain_DurationPresenter_ExpiresThroughTimerRuleDestroy()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.expire", "lifecycle": { "durationSeconds": 0.1 } } ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("dur.expire");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();
            fixture.EnqueueCreate(defId, owner);

            // T1: runtime drains create -> instance + PresenterCreated event
            fixture.RunTick(0.05f);
            Entity presenter = fixture.SingleInstanceOf(defId, owner);
            Assert.That(fixture.World.Get<PresenterState>(presenter).Transient, Is.True);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0), "TimerSet 尚未入表：PresenterCreated 要下一帧进规则");

            // T2: rules compile PresenterCreated -> TimerSet command; runtime arms it
            fixture.RunTick(0.05f);
            Assert.That(fixture.Timers.Count, Is.EqualTo(1), "编译的 TimerSet 计划应已武装 duration timer");
            Assert.That(fixture.DestroyedCount, Is.EqualTo(0));

            // T3: timer advances, not yet expired
            fixture.RunTick(0.05f);
            Assert.That(fixture.Timers.Count, Is.EqualTo(1));
            Assert.That(fixture.World.IsAlive(presenter), Is.True);

            // T4: expiry tick -> TimerExpired event -> rule -> DestroyPresenter command -> destroyed, same frame
            fixture.RunTick(0.05f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(1));
            Assert.That(fixture.DestroyCommandCount, Is.EqualTo(1), "到期只允许经规则产出的 DestroyPresenter 命令销毁");
            Assert.That(fixture.World.IsAlive(presenter), Is.False);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0), "销毁漏斗应清空实例 timer");
            Assert.That(fixture.DestroyedCount, Is.EqualTo(1));
            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Chain_MultipleInstancesSameOwner_ExpireIndependently()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.multi", "lifecycle": { "durationSeconds": 0.5 } } ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("dur.multi");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();

            // T1 建实例，T2 武装 timer(0.5)；dt=0.125 为二进制精确步长
            fixture.EnqueueCreate(defId, owner);
            fixture.RunTick(0.125f);
            fixture.RunTick(0.125f);
            Entity first = fixture.SingleInstanceOf(defId, owner);

            // T3/T4：第二个实例创建并武装；TimerSet 规则按事件携带的实例精确路由，
            // 不得重置 first 的 duration timer（first 此时已消耗 0.125s）
            fixture.EnqueueCreate(defId, owner);
            fixture.RunTick(0.125f);
            fixture.RunTick(0.125f);
            Assert.That(fixture.Timers.Count, Is.EqualTo(2), "两个实例各自武装 duration timer");
            Assert.That(fixture.World.IsAlive(first), Is.True, "second TimerSet 不得顺带重置 first");

            // T5-T6：first 到期（0.125×4）；second 才消耗 0.25s
            fixture.RunTick(0.125f);
            fixture.RunTick(0.125f);
            Assert.That(fixture.World.IsAlive(first), Is.False, "first 应在自身 duration 后销毁");
            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(1));
            Assert.That(fixture.Timers.Count, Is.EqualTo(1));

            // T7-T8：second 走完自己的 duration
            fixture.RunTick(0.125f);
            fixture.RunTick(0.125f);
            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(0), "second 应在自身 duration 后销毁");
            Assert.That(fixture.DestroyedCount, Is.EqualTo(2));
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(2));
        }

        [Test]
        public void Chain_TimerKillCancelsDuration_DestroysImmediatelyWithoutExpiry()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.kill", "lifecycle": { "durationSeconds": 0.5 } } ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("dur.kill");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();
            fixture.EnqueueCreate(defId, owner);
            fixture.RunTick(0.05f);
            fixture.RunTick(0.05f);
            Entity presenter = fixture.SingleInstanceOf(defId, owner);
            Assert.That(fixture.Timers.Count, Is.EqualTo(1));

            fixture.KillTimer(presenter, PresenterTimerNameRegistry.GetId(PresenterTimerNameRegistry.DurationTimerName));
            Assert.That(fixture.World.IsAlive(presenter), Is.False, "取消唯一销毁计划的 duration timer 必须立即走销毁漏斗");
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));
            Assert.That(fixture.DestroyedCount, Is.EqualTo(1));

            fixture.RunTick(0.5f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(0), "已取消的 timer 不得再发 TimerExpired");
        }

        [Test]
        public void Chain_TimerKillWildcard_AlsoDestroysDurationPresenter()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.killwild", "lifecycle": { "durationSeconds": 0.5 } } ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("dur.killwild");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();
            fixture.EnqueueCreate(defId, owner);
            fixture.RunTick(0.05f);
            fixture.RunTick(0.05f);
            Entity presenter = fixture.SingleInstanceOf(defId, owner);

            fixture.KillTimer(presenter, PresenterTimerNameRegistry.AllTimersId);
            Assert.That(fixture.World.IsAlive(presenter), Is.False);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));
        }

        [Test]
        public void Chain_RepeatedDestroyPresenter_IsIdempotent()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.repeat", "lifecycle": { "durationSeconds": 5 } } ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("dur.repeat");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();
            fixture.EnqueueCreate(defId, owner);
            fixture.RunTick(0.05f);
            fixture.RunTick(0.05f);
            Entity presenter = fixture.SingleInstanceOf(defId, owner);

            fixture.EnqueueDestroy(presenter);
            fixture.RunTick(0.05f);
            Assert.That(fixture.DestroyedCount, Is.EqualTo(1));

            fixture.EnqueueDestroy(presenter);
            Assert.DoesNotThrow(() => fixture.RunTick(0.05f));
            Assert.That(fixture.DestroyedCount, Is.EqualTo(1), "重复 destroy 不得二次释放或重复发事件");
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));
        }

        [Test]
        public void Chain_EmitSystemHasNoLifetimeDestroyBranch()
        {
            // 直接走 runtime.Create（不经编译链）的 duration 实例：EmitSystem 更新多帧后仍存活，
            // 证明销毁只可能来自 DestroyPresenter 命令链
            using var world = World.Create();
            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer();
            int defId = definitions.Register("dur.emitonly", new PresenterDefinition
            {
                DefaultLifetime = 0.05f,
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 10,
                            MaterialId = 20,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                        },
                    },
                ],
            });
            instances.BindDefinitions(definitions);
            Entity owner = world.Create();
            Entity presenter = instances.Create(defId, owner, scopeId: 0);
            world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            instances.SyncCullVisibility();

            using var emit = new PresenterEmitSystem(
                world,
                instances,
                definitions,
                requests,
                new Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!);

            for (int i = 0; i < 10; i++)
            {
                emit.Update(0.016f);
                Assert.That(world.IsAlive(presenter), Is.True, "EmitSystem 不得再按 lifetime 直接销毁");
            }

            Assert.That(world.Get<PresenterState>(presenter).Elapsed, Is.GreaterThan(0.05f), "Elapsed 仍按帧推进供 fade 消费");
        }

        [Test]
        public void Chain_TransientInstancesAreExcludedFromScopedReuse()
        {
            WriteCatalog();
            WritePresenters("""
                [ { "id": "dur.scoped", "lifecycle": { "durationSeconds": 5 } } ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("dur.scoped");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();

            fixture.EnqueueCreate(defId, owner, scopeTag: 900);
            fixture.RunTick(0.05f);
            fixture.EnqueueCreate(defId, owner, scopeTag: 900);
            fixture.RunTick(0.05f);

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(2), "transient 实例不得被 scoped 复用去重");
        }

        [Test]
        public void BatchCreate_DurationDefinition_Throws()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("dur.batch", new PresenterDefinition { DefaultLifetime = 1f });
            var instances = new PresenterEntityRuntime(world);
            instances.BindDefinitions(definitions);

            Assert.Throws<InvalidOperationException>(() => instances.CreateEntityAnchoredRootBatch(
                definitions,
                defId,
                owners: new[] { owner },
                scopeIds: new[] { 1 },
                stableIds: new[] { 42 },
                ownerTransforms: new[] { VisualTransform.Default },
                ownerCulls: new[] { default(CullState) },
                definition: null,
                created: new Entity[1]));
        }

        // ── 配置 fixture：显式 Rule → DestroyPresenter（非编译链的作者视图） ──

        [Test]
        public void AuthoredChain_TimerExpiredRuleToDestroyPresenter()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  {
                    "id": "auth.chain_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Auth.Pulse" },
                        "command": { "kind": "TimerSet", "timerName": "auth.pulse", "durationSeconds": 0.1 }
                      },
                      {
                        "event": { "kind": "TimerExpired", "keyId": "auth.pulse" },
                        "command": { "kind": "DestroyPresenter" }
                      }
                    ]
                  }
                ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("auth.chain_unit");
            int pulseEventId = TagRegistry.GetId("Auth.Pulse");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();
            fixture.EnqueueCreate(defId, owner);
            fixture.RunTick(0.05f);

            fixture.FireEvent(new PresentationEvent
            {
                Kind = PresentationEventKind.GameplayEvent,
                KeyId = pulseEventId,
                Source = owner,
                Target = owner,
            });
            fixture.RunTick(0.05f);
            Entity presenter = fixture.SingleInstanceOf(defId, owner);
            Assert.That(fixture.Timers.Count, Is.EqualTo(1), "authored TimerSet 规则应武装 auth.pulse");
            Assert.That(fixture.Timers.Contains(
                fixture.World.Get<PresenterState>(presenter).StableId,
                PresenterTimerNameRegistry.GetId("auth.pulse")), Is.True);

            fixture.RunTick(0.05f);
            fixture.RunTick(0.05f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(1));
            Assert.That(fixture.DestroyCommandCount, Is.EqualTo(1), "authored TimerExpired 规则产出 DestroyPresenter");
            Assert.That(fixture.World.IsAlive(presenter), Is.False);
        }

        // ── FadeOverLifetime 语义保真：definition 上的 authored duration 仍是 fade 分母 ──

        [Test]
        public void FadeOverLifetime_StillReadsAuthoredDurationFromDefinition()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  {
                    "id": "dur.fade",
                    "lifecycle": { "durationSeconds": 2 },
                    "behaviors": [
                      {
                        "slot": "body",
                        "kind": "AssetBinding",
                        "activeByDefault": true,
                        "style": { "alphaPolicy": "FadeOverLifetime" },
                        "assetBinding": { "assetKind": "Mesh", "assetId": "sphere", "renderPath": "StaticMesh", "mobility": "Movable" }
                      }
                    ]
                  }
                ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions(withAssets: true);
            PresenterDefinition def = registry.Get(registry.GetId("dur.fade"));

            Assert.That(def.HasOutputMotionOrFade, Is.True);
            Assert.That(def.DefaultLifetime, Is.EqualTo(2f).Within(0.0001f), "fade 分母仍是 definition 上的 authored duration");
        }

        // ── Headless E2E 验收：真实 JSON 驱动全链路 + 逐拍 trace 落盘 ──

        [Test]
        public void Acceptance_DurationChain_HappyKillAndRepeatDestroy_WriteArtifacts()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  { "id": "accept.duration.unit", "lifecycle": { "durationSeconds": 0.5 } },
                  { "id": "accept.duration.victim", "lifecycle": { "durationSeconds": 5 } }
                ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int unitDefId = registry.GetId("accept.duration.unit");
            int victimDefId = registry.GetId("accept.duration.victim");

            using var fixture = DurationFixture.Create(registry);
            Entity owner = fixture.World.Create();
            var trace = new List<string>();
            var timeline = new List<string>();
            int tick = 0;
            const float dt = 0.125f;

            void Beat(string type, string kind, string detail)
            {
                trace.Add(FormattableString.Invariant(
                    $$"""{"tick":{{tick}},"type":"{{type}}","kind":"{{kind}}","detail":"{{detail}}"}"""));
                timeline.Add($"- [T+{tick:000}] {type} {kind}: {detail}");
            }

            fixture.OnCommand += cmd =>
            {
                if (cmd.CommandKind == PresenterCommandKind.TimerSet)
                {
                    Beat("command", "TimerSet",
                        $"timer='{PresenterTimerNameRegistry.GetName(cmd.TimerNameId)}' duration={cmd.TimerDurationSeconds.ToString("0.0#", CultureInfo.InvariantCulture)}s instance={cmd.PresenterEntity.Id}");
                }
                else if (cmd.CommandKind == PresenterCommandKind.DestroyPresenter)
                {
                    Beat("command", "DestroyPresenter", $"instance={cmd.PresenterEntity.Id}");
                }
                else if (cmd.CommandKind == PresenterCommandKind.TimerKill)
                {
                    string killed = cmd.TimerNameId == PresenterTimerNameRegistry.AllTimersId ? "*" : PresenterTimerNameRegistry.GetName(cmd.TimerNameId);
                    Beat("command", "TimerKill", $"timer='{killed}' instance={cmd.PresenterEntity.Id}");
                }
            };
            fixture.OnEvent += evt =>
            {
                if (evt.Kind == PresentationEventKind.TimerExpired)
                {
                    Beat("event", "TimerExpired", $"timer='{PresenterTimerNameRegistry.GetName(evt.KeyId)}' instance={evt.PresenterEntity.Id}");
                }
                else if (evt.Kind == PresentationEventKind.PresenterDestroyed)
                {
                    Beat("event", "PresenterDestroyed", $"instance={evt.PresenterEntity.Id}");
                }
            };

            // happy path: unit 到期经规则销毁（T1 建，T2 武装 0.5s，T3-T6 四拍到期）
            fixture.EnqueueCreate(unitDefId, owner, scopeTag: 1);
            fixture.RunTickWith(dt, () => tick++);
            Entity unit = fixture.SingleInstanceOf(unitDefId, owner);

            fixture.RunTickWith(dt, () => tick++);
            fixture.RunTickWith(dt, () => tick++);
            fixture.RunTickWith(dt, () => tick++);
            fixture.RunTickWith(dt, () => tick++);
            fixture.RunTickWith(dt, () => tick++);
            Assert.That(fixture.World.IsAlive(unit), Is.False, "unit 应在 duration 后经 TimerExpired→Rule→DestroyPresenter 销毁");
            Assert.That(fixture.DestroyedCount, Is.EqualTo(1));

            // timer 被取消: victim 的 duration timer 被 TimerKill → 立即销毁、不再到期
            fixture.EnqueueCreate(victimDefId, owner, scopeTag: 2);
            fixture.RunTickWith(dt, () => tick++);
            fixture.RunTickWith(dt, () => tick++);
            Entity victim = fixture.SingleInstanceOf(victimDefId, owner);

            int expiredBeforeKill = fixture.ExpiredEventCount;
            fixture.KillTimer(victim, PresenterTimerNameRegistry.GetId(PresenterTimerNameRegistry.DurationTimerName));
            Beat("action", "TimerKill", "cancel duration timer -> immediate destroy funnel");
            Assert.That(fixture.World.IsAlive(victim), Is.False);

            int destroyedBeforeRepeat = fixture.DestroyedCount;
            fixture.EnqueueDestroy(victim);
            fixture.RunTickWith(dt, () => tick++);
            Assert.That(fixture.DestroyedCount, Is.EqualTo(destroyedBeforeRepeat), "重复 destroy 是幂等 no-op");
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(expiredBeforeKill), "取消后不得再发 TimerExpired");
            Assert.That(fixture.Timers.Count, Is.EqualTo(0), "结束时 timer 表为空，无泄漏");

            Beat("assert", "chain", "TimerSet → TimerExpired → Rule → DestroyPresenter 逐拍成立");

            string artifactDir = Path.Combine(
                PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "evidence",
                "presenter-1095");
            Directory.CreateDirectory(artifactDir);
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), string.Join('\n', trace) + "\n");

            const string PathMmd = """
                flowchart TD
                    A[lifecycle.durationSeconds] -->|loader compile| B[Rule: PresenterCreated -> TimerSet presenter.duration]
                    B --> C[timer 武装于新实例]
                    C --> D{渲染 dt 推进}
                    D -->|到时| E[TimerExpired presenter.duration]
                    E --> F[Rule: TimerExpired -> DestroyPresenter]
                    F --> G[销毁漏斗: PresenterDestroyed + KillAll timers]
                    D -->|TimerKill 取消| H[销毁漏斗: 立即销毁 不再到期]
                    G --> I[重复 DestroyPresenter: 幂等 no-op]
                """;
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), PathMmd + "\n");

            var report = new StringBuilder();
            report.AppendLine("# Presenter duration → Timer/Rule/Destroy 链路验收");
            report.AppendLine();
            report.AppendLine("## Header");
            report.AppendLine("- scenario: lifecycle.durationSeconds 编译为 TimerSet 计划，唯一销毁链路 TimerSet → TimerExpired → Rule → DestroyPresenter");
            report.AppendLine("- build: local PresentationTests, 真实 JSON 配置管线（PresenterDefinitionConfigLoader）");
            report.AppendLine("- clock: headless fixture, 生产系统序 Timer → Rules → Runtime, dt=0.125s/拍");
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
            report.AppendLine("- reason codes: compiled_timerset, rule_destroy_only_entry, timerkill_immediate_destroy, repeat_destroy_idempotent");
            report.AppendLine();
            report.AppendLine("## Summary Stats");
            report.AppendLine(FormattableString.Invariant($"- TimerExpired events: {fixture.ExpiredEventCount} (unit only; victim cancelled)"));
            report.AppendLine(FormattableString.Invariant($"- PresenterDestroyed events: {fixture.DestroyedCount}"));
            report.AppendLine(FormattableString.Invariant($"- final timer table: {fixture.Timers.Count} (no leak)"));
            string reportText = report.ToString();
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), reportText);
            TestContext.Out.WriteLine(reportText);
        }

        // ── 配置夹具辅助 ──

        private PresenterDefinitionRegistry LoadDefinitions(bool withAssets = false)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: withAssets ? (_, _) => 1 : null).Load(catalog);
            return registry;
        }

        private InvalidOperationException? AssertLoaderThrows(string presentersJson)
        {
            WriteCatalog();
            WritePresenters(presentersJson);
            return Assert.Throws<InvalidOperationException>(() => LoadDefinitions());
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

        private sealed class DurationFixture : IDisposable
        {
            public readonly World World;
            public readonly PresenterCommandBuffer Commands;
            public readonly PresentationEventStream Events;
            public readonly PresenterEntityRuntime Instances;
            public readonly PresenterDefinitionRegistry Definitions;
            public readonly PresenterTimerTable Timers;
            public readonly PresenterTimerSystem TimerSystem;
            public readonly PresenterRuntimeSystem Runtime;
            public readonly PresenterRuleSystem Rules;

            public event Action<PresenterCommand>? OnCommand;
            public event Action<PresentationEvent>? OnEvent;

            private int _expiredEventCount;
            private int _destroyCommandCount;
            private int _destroyedEventCount;

            private DurationFixture(PresenterDefinitionRegistry definitions)
            {
                World = Arch.Core.World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Instances = new PresenterEntityRuntime(World);
                Definitions = definitions;
                Timers = new PresenterTimerTable(capacity: 64);
                TimerSystem = new PresenterTimerSystem(World, Timers, Events);
                Runtime = new PresenterRuntimeSystem(
                    World,
                    Commands,
                    Events,
                    new TransientMarkerBuffer(),
                    new PresentationRequestBuffer(),
                    Instances,
                    new PresentationStableIdAllocator(),
                    Definitions,
                    timers: Timers);
                Rules = new PresenterRuleSystem(
                    World,
                    Events,
                    Commands,
                    Definitions,
                    Instances,
                    new Ludots.Core.GraphRuntime.GraphProgramRegistry(),
                    new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(World, spatialQueries: null, coords: null, eventBus: null),
                    new Dictionary<string, object>());
            }

            public static DurationFixture Create(PresenterDefinitionRegistry definitions) => new(definitions);

            public int ExpiredEventCount => _expiredEventCount;

            public int DestroyCommandCount => _destroyCommandCount;

            public int DestroyedCount => _destroyedEventCount;

            public void EnqueueCreate(int defId, Entity owner, int scopeTag = 0)
            {
                Assert.That(Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PerformerCommandRouteStrategy.CreatePerformer,
                    PresenterDefinitionId = defId,
                    ParentEntity = Entity.Null,
                    ScopeTag = scopeTag,
                    ScopeSource = PresenterCommandScopeSource.Fixed,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = owner,
                    Target = owner,
                }), Is.True);
            }

            public void EnqueueDestroy(Entity presenter)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.DestroyPresenter,
                    CommandKindId = (byte)PresenterCommandKind.DestroyPresenter,
                    RouteStrategy = PerformerCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                });
            }

            public void KillTimer(Entity presenter, int nameId)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.TimerKill,
                    CommandKindId = (byte)PresenterCommandKind.TimerKill,
                    RouteStrategy = PerformerCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                    TimerNameId = nameId,
                });
                Runtime.Update(0.016f);
                CaptureRuntimePhaseEvents();
            }

            public void FireEvent(in PresentationEvent evt)
            {
                Assert.That(Events.TryAdd(in evt), Is.True);
            }

            public Entity SingleInstanceOf(int defId, Entity owner)
            {
                IReadOnlyList<Entity> active = Instances.GetActiveByOwnerDefinition(defId, owner);
                Assert.That(active.Count, Is.EqualTo(1));
                return active[0];
            }

            // 生产序：Timer → Rules → Runtime。Rules 段消费并清空事件流，
            // Runtime 发出的 PresenterCreated/PresenterDestroyed 存活到下一拍进规则，
            // 因此各阶段只捕获本阶段新增的事件，避免跨拍重复计数。
            public void RunTick(float dt)
            {
                TimerSystem.Update(dt);
                CaptureTimerPhaseEvents();
                Rules.Update(dt);
                CaptureCommands();
                Runtime.Update(dt);
                CaptureRuntimePhaseEvents();
            }

            public void RunTickWith(float dt, Action tickHook)
            {
                tickHook();
                RunTick(dt);
            }

            private void CaptureTimerPhaseEvents()
            {
                ReadOnlySpan<PresentationEvent> span = Events.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i].Kind == PresentationEventKind.TimerExpired)
                    {
                        _expiredEventCount++;
                        OnEvent?.Invoke(span[i]);
                    }
                }
            }

            private void CaptureRuntimePhaseEvents()
            {
                ReadOnlySpan<PresentationEvent> span = Events.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i].Kind == PresentationEventKind.PresenterDestroyed)
                    {
                        _destroyedEventCount++;
                        OnEvent?.Invoke(span[i]);
                    }
                }
            }

            private void CaptureCommands()
            {
                ReadOnlySpan<PresenterCommand> span = Commands.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i].CommandKind == PresenterCommandKind.DestroyPresenter)
                    {
                        _destroyCommandCount++;
                    }

                    OnCommand?.Invoke(span[i]);
                }
            }

            public void Dispose()
            {
                Rules.Dispose();
                Runtime.Dispose();
                TimerSystem.Dispose();
                World.Dispose();
            }
        }
    }
}
