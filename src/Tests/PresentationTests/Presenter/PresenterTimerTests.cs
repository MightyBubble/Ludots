using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    [TestFixture]
    public sealed class PresenterTimerTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterTimerTests", Guid.NewGuid().ToString("N"));
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

        // ── PresenterTimerTable 单元 ──

        [Test]
        public void Table_SetThenTickPastDuration_ExpiresWithPayload()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 16);
            Entity presenter = world.Create();
            Entity owner = world.Create();
            int nameId = PresenterTimerNameRegistry.Register("unit.phase");

            table.Set(ownerStableId: 7, presenter, owner, nameId, durationSeconds: 1.0f, durationRangeSeconds: 0f);

            Assert.That(table.Tick(0.5f), Is.EqualTo(0));
            Assert.That(table.Tick(0.6f), Is.EqualTo(1));
            Assert.That(table.GetExpiredStableId(0), Is.EqualTo(7));
            Assert.That(table.GetExpiredNameId(0), Is.EqualTo(nameId));
            Assert.That(table.GetExpiredPresenter(0), Is.EqualTo(presenter));
            Assert.That(table.GetExpiredOwner(0), Is.EqualTo(owner));
            Assert.That(table.Count, Is.EqualTo(0));
        }

        [Test]
        public void Table_SetSameNameTwice_ReplacesAndExpiresOnce()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 16);
            Entity presenter = world.Create();
            Entity owner = world.Create();
            int nameId = PresenterTimerNameRegistry.Register("unit.replace");

            table.Set(7, presenter, owner, nameId, 5.0f, 0f);
            table.Set(7, presenter, owner, nameId, 1.0f, 0f);

            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.Tick(2.0f), Is.EqualTo(1));
            Assert.That(table.Tick(10.0f), Is.EqualTo(0));
        }

        [Test]
        public void Table_Kill_PreventsExpiry()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 16);
            Entity presenter = world.Create();
            Entity owner = world.Create();
            int nameId = PresenterTimerNameRegistry.Register("unit.kill");

            table.Set(7, presenter, owner, nameId, 1.0f, 0f);
            Assert.That(table.Kill(7, nameId), Is.True);
            Assert.That(table.Tick(5.0f), Is.EqualTo(0));
            Assert.That(table.Kill(7, nameId), Is.False);
        }

        [Test]
        public void Table_KillAll_RemovesOnlyMatchingOwner()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 16);
            Entity presenter = world.Create();
            Entity owner = world.Create();
            int a = PresenterTimerNameRegistry.Register("unit.a");
            int b = PresenterTimerNameRegistry.Register("unit.b");
            int c = PresenterTimerNameRegistry.Register("unit.c");

            table.Set(7, presenter, owner, a, 1.0f, 0f);
            table.Set(7, presenter, owner, b, 1.0f, 0f);
            table.Set(9, presenter, owner, c, 1.0f, 0f);

            Assert.That(table.KillAll(7), Is.EqualTo(2));
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.Tick(2.0f), Is.EqualTo(1));
            Assert.That(table.GetExpiredNameId(0), Is.EqualTo(c));
        }

        [Test]
        public void Table_RandomRange_StaysWithinBaseAndBasePlusRange()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 16, randomSeed: 12345u);
            Entity presenter = world.Create();
            Entity owner = world.Create();
            int nameId = PresenterTimerNameRegistry.Register("unit.rng");

            for (int i = 0; i < 200; i++)
            {
                table.Set(7, presenter, owner, nameId, durationSeconds: 1.0f, durationRangeSeconds: 0.5f);
                // 1.0s 必不到期，1.6s 必到期 → 有效时长恒在 [1.0, 1.5]
                Assert.That(table.Tick(0.99f), Is.EqualTo(0));
                Assert.That(table.Tick(0.61f), Is.EqualTo(1));
            }
        }

        [Test]
        public void Table_SetBeyondCapacity_Throws()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 2);
            Entity presenter = world.Create();
            Entity owner = world.Create();

            table.Set(1, presenter, owner, PresenterTimerNameRegistry.Register("cap.a"), 1.0f, 0f);
            table.Set(2, presenter, owner, PresenterTimerNameRegistry.Register("cap.b"), 1.0f, 0f);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                table.Set(3, presenter, owner, PresenterTimerNameRegistry.Register("cap.c"), 1.0f, 0f));
            Assert.That(ex!.Message, Does.Contain("capacity"));
        }

        [Test]
        public void Table_SetWithInvalidArguments_Throws()
        {
            using var world = World.Create();
            var table = new PresenterTimerTable(capacity: 16);
            Entity presenter = world.Create();
            Entity owner = world.Create();
            int nameId = PresenterTimerNameRegistry.Register("unit.invalid");

            Assert.Throws<InvalidOperationException>(() => table.Set(0, presenter, owner, nameId, 1.0f, 0f));
            Assert.Throws<InvalidOperationException>(() => table.Set(7, presenter, owner, 0, 1.0f, 0f));
            Assert.Throws<InvalidOperationException>(() => table.Set(7, presenter, owner, nameId, 0f, 0f));
            Assert.Throws<InvalidOperationException>(() => table.Set(7, presenter, owner, nameId, 1.0f, -0.1f));
            Assert.Throws<InvalidOperationException>(() => table.Set(7, presenter, owner, nameId, float.NaN, 0f));
        }

        // ── 管线集成 ──

        [Test]
        public void Pipeline_TimerSetExpires_RuleCreatesNextPresenter()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithPhaseRule(out int spawnedDefId);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.GetId("it.phase2");
            Assert.That(phaseNameId, Is.GreaterThan(0));

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            Assert.That(fixture.Timers.Count, Is.EqualTo(1));

            fixture.TickAll(0.03f);
            Assert.That(fixture.CreatedKeyIds, Does.Not.Contain(spawnedDefId), "未到期不应触发下一段");

            // 第二帧到期，TimerExpired 当帧进规则，当帧尾由 runtime 建出下一段 presenter
            fixture.TickTimerOnly(0.03f);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0), "timer 应已到期出表");
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(1), "TimerExpired 事件应已发布");

            fixture.Rules.Update(0.016f);
            Assert.That(fixture.Commands.Count, Is.EqualTo(1), "规则应产出 CreatePresenter 命令");

            fixture.Runtime.Update(0.016f);
            fixture.CaptureEvents();
            Assert.That(fixture.CreatedKeyIds.Contains(spawnedDefId), Is.True, "到期规则应建出下一段 presenter");
        }

        [Test]
        public void Pipeline_DestroyPresenter_ClearsTimersWithoutExpiryEvent()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithPhaseRule(out _);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.GetId("it.phase2");

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            fixture.DestroyPresenter(presenter);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));

            fixture.TickAll(0.10f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(0));
        }

        [Test]
        public void Pipeline_TimerKillCommand_PreventsExpiry()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithPhaseRule(out int spawnedDefId);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.GetId("it.phase2");

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            fixture.KillTimer(presenter, phaseNameId);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));

            fixture.TickAll(0.10f);
            Assert.That(fixture.CreatedKeyIds, Does.Not.Contain(spawnedDefId), "TimerKill 后不应触发下一段");
        }

        [Test]
        public void Pipeline_TimerKillWildcard_RemovesAllTimersOnInstance()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithPhaseRule(out _);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.GetId("it.phase2");
            int otherNameId = PresenterTimerNameRegistry.Register("it.other");

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            fixture.SetTimer(presenter, otherNameId, durationSeconds: 0.05f);
            Assert.That(fixture.Timers.Count, Is.EqualTo(2));

            fixture.KillTimer(presenter, PresenterTimerNameRegistry.AllTimersId);
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));
        }

        [Test]
        public void Pipeline_TimerExpiredEvent_CarriesNameOwnerPresenterStableId()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithoutRules();
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.Register("it.payload");
            int stableId = fixture.World.Get<PresenterState>(presenter).StableId;

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            fixture.TickTimerOnly(0.10f);

            Assert.That(fixture.LastExpiredEvent, Is.Not.Null);
            PresentationEvent evt = fixture.LastExpiredEvent!.Value;
            Assert.That(evt.Kind, Is.EqualTo(PresentationEventKind.TimerExpired));
            Assert.That(evt.KeyId, Is.EqualTo(phaseNameId));
            Assert.That(evt.Source, Is.EqualTo(fixture.Owner));
            Assert.That(evt.PresenterEntity, Is.EqualTo(presenter));
            Assert.That(evt.Magnitude, Is.EqualTo(stableId));
        }

        [Test]
        public void Pipeline_OwnerPendingDestroy_TimerExpiryIsSuppressed()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithPhaseRule(out int spawnedDefId);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.GetId("it.phase2");

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            // 生命周期系统本帧早些时候已把 owner 标记为待销毁，销毁命令要等同帧尾才执行
            fixture.World.Add(fixture.Owner, new PresentationDestroyPending());

            fixture.TickAll(0.10f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(0), "owner 待销毁时不应发布 TimerExpired");
            Assert.That(fixture.Timers.Count, Is.EqualTo(0), "到期 timer 应随销毁抑制一并出表");
            Assert.That(fixture.CreatedKeyIds, Does.Not.Contain(spawnedDefId), "不应给将死实例接续创建 presenter");
        }

        [Test]
        public void Pipeline_OwnerDead_TimerExpiryIsSuppressed()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithPhaseRule(out int spawnedDefId);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int phaseNameId = PresenterTimerNameRegistry.GetId("it.phase2");

            fixture.SetTimer(presenter, phaseNameId, durationSeconds: 0.05f);
            fixture.World.Destroy(fixture.Owner);

            fixture.TickAll(0.10f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(0), "owner 已销毁时不应发布 TimerExpired");
            Assert.That(fixture.Timers.Count, Is.EqualTo(0));
            Assert.That(fixture.CreatedKeyIds, Does.Not.Contain(spawnedDefId));
        }

        [Test]
        public void Pipeline_TimerExpiredWildcardRule_MatchesAnyTimerName()
        {
            using var fixture = TimerFixture.Create();
            int rootDefId = fixture.RegisterRootWithWildcardPhaseRule(out int spawnedDefId);
            Entity presenter = fixture.CreateRoot(rootDefId, scopeTag: 100);
            int nameA = PresenterTimerNameRegistry.Register("it.wild.a");
            int nameB = PresenterTimerNameRegistry.Register("it.wild.b");

            fixture.SetTimer(presenter, nameA, durationSeconds: 0.05f);
            fixture.SetTimer(presenter, nameB, durationSeconds: 0.05f);
            fixture.TickTimerOnly(0.10f);
            Assert.That(fixture.ExpiredEventCount, Is.EqualTo(2), "两个命名 timer 都应到期");

            fixture.Rules.Update(0.016f);
            Assert.That(fixture.Commands.Count, Is.EqualTo(2), "通配规则应对每个到期事件各产一条命令");

            fixture.Runtime.Update(0.016f);
            fixture.CaptureEvents();
            Assert.That(fixture.CreatedKeyIds, Does.Contain(spawnedDefId), "通配规则命中的到期应建出后续 presenter");
        }

        // ── 配置加载 ──

        [Test]
        public void ConfigLoader_TimerSetCommand_ParsesNameAndDurations()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  {
                    "id": "cfg_timer_root",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "TimerCfg.Flash" },
                        "command": { "kind": "TimerSet", "timerName": "cfg.phase2", "durationSeconds": 2.0, "durationRangeSeconds": 0.5 }
                      },
                      {
                        "event": { "kind": "TimerExpired", "keyId": "cfg.phase2" },
                        "command": { "kind": "TimerKill", "timerName": "*" }
                      }
                    ]
                  }
                ]
                """);

            var registry = LoadDefinitions();
            PresenterDefinition def = registry.Get(registry.GetId("cfg_timer_root"));

            Assert.That(def.Rules, Has.Length.EqualTo(2));

            PresenterCommand set = def.Rules[0].Command;
            Assert.That(set.CommandKind, Is.EqualTo(PresenterCommandKind.TimerSet));
            Assert.That(set.RouteStrategy, Is.EqualTo(PerformerCommandRouteStrategy.ExistingInstances));
            Assert.That(set.TimerNameId, Is.EqualTo(PresenterTimerNameRegistry.GetId("cfg.phase2")));
            Assert.That(set.TimerDurationSeconds, Is.EqualTo(2.0f));
            Assert.That(set.TimerDurationRangeSeconds, Is.EqualTo(0.5f));

            Assert.That(def.Rules[1].Event.Kind, Is.EqualTo(PresentationEventKind.TimerExpired));
            Assert.That(def.Rules[1].Event.KeyId, Is.EqualTo(PresenterTimerNameRegistry.GetId("cfg.phase2")));
            Assert.That(def.Rules[1].Command.CommandKind, Is.EqualTo(PresenterCommandKind.TimerKill));
            Assert.That(def.Rules[1].Command.TimerNameId, Is.EqualTo(PresenterTimerNameRegistry.AllTimersId));
        }

        [Test]
        public void ConfigLoader_TimerSetMissingName_Throws()
        {
            var ex = AssertLoaderThrows("""
                [
                  {
                    "id": "cfg_bad_noname",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "TimerCfg.Bad" },
                        "command": { "kind": "TimerSet", "durationSeconds": 1.0 }
                      }
                    ]
                  }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("timerName"));
        }

        [Test]
        public void ConfigLoader_TimerSetNonPositiveDuration_Throws()
        {
            var ex = AssertLoaderThrows("""
                [
                  {
                    "id": "cfg_bad_duration",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "TimerCfg.Bad" },
                        "command": { "kind": "TimerSet", "timerName": "cfg.bad", "durationSeconds": 0 }
                      }
                    ]
                  }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("durationSeconds"));
        }

        [Test]
        public void ConfigLoader_TimerFieldsOnOtherCommand_Throws()
        {
            var ex = AssertLoaderThrows("""
                [
                  {
                    "id": "cfg_bad_scope",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "TimerCfg.Bad" },
                        "command": { "kind": "DestroyPresenter", "timerName": "cfg.misplaced" }
                      }
                    ]
                  }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("timerName"));
        }

        [Test]
        public void ConfigLoader_NumericTimerName_Throws()
        {
            var ex = AssertLoaderThrows("""
                [
                  {
                    "id": "cfg_bad_numeric",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "TimerCfg.Bad" },
                        "command": { "kind": "TimerKill", "timerName": 7 }
                      }
                    ]
                  }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("timerName"));
        }

        [Test]
        public void ConfigLoader_TimerSetWildcardName_Throws()
        {
            var ex = AssertLoaderThrows("""
                [
                  {
                    "id": "cfg_bad_wildcard_set",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "TimerCfg.Bad" },
                        "command": { "kind": "TimerSet", "timerName": "*", "durationSeconds": 1.0 }
                      }
                    ]
                  }
                ]
                """);
            Assert.That(ex!.Message, Does.Contain("reserved"));
        }

        [Test]
        public void ConfigLoader_TimerExpiredWildcard_ParsesAsMatchAnyKey()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  {
                    "id": "cfg_timer_wildcard",
                    "rules": [
                      {
                        "event": { "kind": "TimerExpired", "keyId": "*" },
                        "command": { "kind": "DestroyPresenter" }
                      }
                    ]
                  }
                ]
                """);

            var registry = LoadDefinitions();
            PresenterDefinition def = registry.Get(registry.GetId("cfg_timer_wildcard"));

            Assert.That(def.Rules, Has.Length.EqualTo(1));
            Assert.That(def.Rules[0].Event.Kind, Is.EqualTo(PresentationEventKind.TimerExpired));
            Assert.That(def.Rules[0].Event.KeyId, Is.EqualTo(-1), "keyId \"*\" 应解析为匹配任意 timer 名的通配");
        }

        // ── Headless E2E 验收：SC2 受击闪黄同构（命名 timer 原语，真实 JSON 配置驱动） ──

        [Test]
        public void Acceptance_HitFlash_HappyPathAndTagLostInterrupt_WriteArtifacts()
        {
            WriteCatalog();
            WritePresenters("""
                [
                  {
                    "id": "accept.hit_flash_unit",
                    "rules": [
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Accept.HitFlash" },
                        "command": { "kind": "TimerSet", "timerName": "accept.flash", "durationSeconds": 0.6 }
                      },
                      {
                        "event": { "kind": "GameplayEvent", "keyId": "Accept.HitFlash" },
                        "command": { "kind": "SetParam", "paramKey": "accept.flash.yellow", "paramLane": "Float", "valueSource": "Fixed", "paramValue": 1.0 }
                      },
                      {
                        "event": { "kind": "TimerExpired", "keyId": "accept.flash" },
                        "command": { "kind": "SetParam", "paramKey": "accept.flash.yellow", "paramLane": "Float", "valueSource": "Fixed", "paramValue": 0.0 }
                      },
                      {
                        "event": { "kind": "TagEffectiveChanged", "keyId": "Accept.Suppressed" },
                        "condition": { "inline": "TagLost" },
                        "command": { "kind": "TimerKill", "timerName": "*" }
                      },
                      {
                        "event": { "kind": "TagEffectiveChanged", "keyId": "Accept.Suppressed" },
                        "condition": { "inline": "TagLost" },
                        "command": { "kind": "SetParam", "paramKey": "accept.flash.yellow", "paramLane": "Float", "valueSource": "Fixed", "paramValue": 0.0 }
                      }
                    ]
                  }
                ]
                """);
            PresenterDefinitionRegistry registry = LoadDefinitions();
            int defId = registry.GetId("accept.hit_flash_unit");
            Assert.That(defId, Is.GreaterThan(0), "配置加载后应能取到 definition id");

            int hitFlashEventId = TagRegistry.GetId("Accept.HitFlash");
            int suppressedTagId = TagRegistry.GetId("Accept.Suppressed");
            int flashTimerId = PresenterTimerNameRegistry.GetId("accept.flash");
            Assert.That(hitFlashEventId, Is.GreaterThan(0), "GameplayEvent 语义键应已注册");
            Assert.That(suppressedTagId, Is.GreaterThan(0), "TagEffectiveChanged 语义键应已注册");
            Assert.That(flashTimerId, Is.GreaterThan(0), "TimerExpired/TimerSet 语义键应已注册");

            using var fixture = TimerFixture.Create(registry);
            Entity unitA = fixture.World.Create();
            Entity unitB = fixture.World.Create();
            var unitNames = new Dictionary<Entity, string> { [unitA] = "A", [unitB] = "B" };
            var presenterUnit = new Dictionary<Entity, string>();
            var trace = new List<string>();
            var timeline = new List<string>();
            var expiredSources = new List<Entity>();
            var commandLog = new List<(int Tick, PresenterCommandKind Kind, string Unit, int TimerNameId, float ParamValue)>();
            int tick = 0;
            int expiryTick = -1;
            int maxTimerCount = 0;

            string UnitOf(Entity e) => unitNames.TryGetValue(e, out string? name) ? name : $"e{e.Id}";

            void CaptureEvents()
            {
                ReadOnlySpan<PresentationEvent> span = fixture.Events.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    PresentationEvent evt = span[i];
                    if (evt.Kind == PresentationEventKind.PresenterCreated)
                    {
                        ref readonly PresenterState state = ref fixture.World.Get<PresenterState>(evt.PresenterEntity);
                        string unit = UnitOf(state.OwnerEntity);
                        presenterUnit[evt.PresenterEntity] = unit;
                        trace.Add(FormattableString.Invariant(
                            $$"""{"tick":{{tick}},"type":"event","kind":"PresenterCreated","unit":"{{unit}}","presenter_stable_id":{{state.StableId}}}"""));
                        timeline.Add($"- [T+{tick:000}] 单位 {unit} 的受击闪黄 presenter 上线（stable id {state.StableId}）");
                    }
                    else if (evt.Kind == PresentationEventKind.TimerExpired)
                    {
                        string unit = UnitOf(evt.Source);
                        expiredSources.Add(evt.Source);
                        expiryTick = tick;
                        trace.Add(FormattableString.Invariant(
                            $$"""{"tick":{{tick}},"type":"event","kind":"TimerExpired","unit":"{{unit}}","timer":"{{PresenterTimerNameRegistry.GetName(evt.KeyId)}}","presenter_stable_id":{{(int)evt.Magnitude}}}"""));
                        timeline.Add($"- [T+{tick:000}] 单位 {unit} 的 accept.flash 到时 → TimerExpired（正常复原窗口，当帧进规则）");
                    }
                }
            }

            void CaptureCommands()
            {
                ReadOnlySpan<PresenterCommand> span = fixture.Commands.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    PresenterCommand cmd = span[i];
                    string unit = presenterUnit.TryGetValue(cmd.PresenterEntity, out string? name) ? name : "?";
                    commandLog.Add((tick, cmd.CommandKind, unit, cmd.TimerNameId, cmd.ParamValue));
                    switch (cmd.CommandKind)
                    {
                        case PresenterCommandKind.TimerSet:
                            trace.Add(FormattableString.Invariant(
                                $$"""{"tick":{{tick}},"type":"command","kind":"TimerSet","unit":"{{unit}}","timer":"{{PresenterTimerNameRegistry.GetName(cmd.TimerNameId)}}","duration_seconds":{{cmd.TimerDurationSeconds}}}"""));
                            timeline.Add($"- [T+{tick:000}] 单位 {unit} 受击 → TimerSet accept.flash（{cmd.TimerDurationSeconds.ToString("0.0#", CultureInfo.InvariantCulture)}s）启动");
                            break;
                        case PresenterCommandKind.TimerKill:
                            string killed = cmd.TimerNameId == PresenterTimerNameRegistry.AllTimersId ? "*" : PresenterTimerNameRegistry.GetName(cmd.TimerNameId);
                            trace.Add(FormattableString.Invariant(
                                $$"""{"tick":{{tick}},"type":"command","kind":"TimerKill","unit":"{{unit}}","timer":"{{killed}}"}"""));
                            timeline.Add($"- [T+{tick:000}] 单位 {unit} 的 Suppressed tag 丢失 → TimerKill \"{killed}\" 清掉实例全部 timer（打断，不会再有 TimerExpired）");
                            break;
                        case PresenterCommandKind.SetParam:
                            trace.Add(FormattableString.Invariant(
                                $$"""{"tick":{{tick}},"type":"command","kind":"SetParam","unit":"{{unit}}","param":"accept.flash.yellow","value":{{cmd.ParamValue}}}"""));
                            timeline.Add(cmd.ParamValue > 0f
                                ? $"- [T+{tick:000}] 单位 {unit} 闪黄参数 = 1（受击高亮）"
                                : $"- [T+{tick:000}] 单位 {unit} 闪黄参数 = 0（复原）");
                            break;
                    }
                }
            }

            void EnqueueCreate(Entity owner, int scopeTag)
            {
                Assert.That(fixture.Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PerformerCommandRouteStrategy.CreatePerformer,
                    PresenterDefinitionId = defId,
                    ParentEntity = Entity.Null,
                    ScopeTag = scopeTag,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = owner,
                    Target = owner,
                }), Is.True);
            }

            // 生产序：Timer → Rules → Runtime；事件当帧消费完清流
            void RunTick()
            {
                tick++;
                fixture.TimerSystem.Update(0.125f);
                CaptureEvents();
                fixture.Rules.Update(0.125f);
                CaptureCommands();
                fixture.Runtime.Update(0.125f);
                CaptureEvents();
                maxTimerCount = Math.Max(maxTimerCount, fixture.Timers.Count);
                fixture.Events.Clear();
            }

            EnqueueCreate(unitA, 1001);
            EnqueueCreate(unitB, 1002);
            RunTick();
            Assert.That(presenterUnit, Has.Count.EqualTo(2), "两个单位的 presenter 都应建出来");

            fixture.FireEvent(new PresentationEvent { Kind = PresentationEventKind.GameplayEvent, KeyId = hitFlashEventId, Source = unitA, Target = unitA, Magnitude = 1f });
            fixture.FireEvent(new PresentationEvent { Kind = PresentationEventKind.GameplayEvent, KeyId = hitFlashEventId, Source = unitB, Target = unitB, Magnitude = 1f });
            RunTick();
            Assert.That(fixture.Timers.Count, Is.EqualTo(2), "两个实例应各挂一个 accept.flash timer");

            RunTick();

            int interruptTick = tick + 1;
            fixture.FireEvent(new PresentationEvent { Kind = PresentationEventKind.TagEffectiveChanged, KeyId = suppressedTagId, Source = unitB, Target = unitB, Magnitude = 0f });
            RunTick();
            Assert.That(fixture.Timers.Count, Is.EqualTo(1), "B 的 timer 应被 TimerKill \"*\" 清掉，A 的不受影响");

            int guard = 0;
            while (expiredSources.Count == 0 && guard++ < 20)
            {
                RunTick();
            }

            RunTick();

            Assert.That(expiredSources, Has.Count.EqualTo(1), "只有 A 应走到 TimerExpired");
            Assert.That(expiredSources[0], Is.EqualTo(unitA), "B 被打断后不应再有 TimerExpired");
            Assert.That(expiryTick, Is.GreaterThan(interruptTick), "到期应晚于打断帧");
            Assert.That(fixture.Timers.Count, Is.EqualTo(0), "结束后 timer 表应为空");

            Assert.That(commandLog.Count(c => c.Kind == PresenterCommandKind.TimerSet), Is.EqualTo(2), "两个单位各一条 TimerSet");
            Assert.That(commandLog.Count(c => c.Kind == PresenterCommandKind.TimerKill), Is.EqualTo(1), "只有 B 一条 TimerKill");
            Assert.That(commandLog.Single(c => c.Kind == PresenterCommandKind.TimerKill).Unit, Is.EqualTo("B"), "TimerKill 应只落在 B 的实例上");
            Assert.That(commandLog.Count(c => c.Kind == PresenterCommandKind.SetParam && c.ParamValue == 1f), Is.EqualTo(2), "两个单位各闪黄一次");
            Assert.That(commandLog.Count(c => c.Kind == PresenterCommandKind.SetParam && c.ParamValue == 0f), Is.EqualTo(2), "两个单位各复原一次");
            Assert.That(commandLog.First(c => c.Kind == PresenterCommandKind.SetParam && c.ParamValue == 0f && c.Unit == "A").Tick, Is.EqualTo(expiryTick), "A 的复原应发生在到期帧");
            Assert.That(commandLog.First(c => c.Kind == PresenterCommandKind.SetParam && c.ParamValue == 0f && c.Unit == "B").Tick, Is.EqualTo(interruptTick), "B 的复原应发生在打断帧");
            foreach (Entity presenter in presenterUnit.Keys)
            {
                Assert.That(fixture.World.IsAlive(presenter), Is.True, "打断只清 timer，不应销毁 presenter");
            }

            string artifactDir = Path.Combine(
                PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "acceptance",
                "presenter-timer");
            Directory.CreateDirectory(artifactDir);

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), string.Join('\n', trace) + "\n");

            const string PathMmd = """
                flowchart TD
                    A[GameplayEvent Accept.HitFlash] --> B[TimerSet accept.flash 0.6s]
                    A --> P1[SetParam flash.yellow = 1 受击高亮]
                    B --> D{timer 随渲染 dt 推进}
                    D -->|到时| E[TimerExpired accept.flash]
                    E --> F[SetParam flash.yellow = 0 正常复原]
                    D -->|中途 Suppressed tag 丢失| G[condition TagLost]
                    G --> H[TimerKill * 清掉实例全部 timer]
                    G --> P0[SetParam flash.yellow = 0 强制复原]
                    H --> X[链路终止: 无 TimerExpired]
                """;
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), PathMmd + "\n");

            var report = new StringBuilder();
            report.AppendLine("# Scenario: presenter-timer-hit-flash");
            report.AppendLine();
            report.AppendLine("## Header");
            report.AppendLine("- scenario name: SC2-style hit-flash sequencing via presenter named timer primitives (TimerSet / TimerExpired / TimerKill)");
            report.AppendLine("- build/version: local PresentationTests, real JSON config pipeline (PresenterDefinitionConfigLoader)");
            report.AppendLine("- seed/map/clock: deterministic fixture / in-memory world / render dt 0.125s per tick");
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
            report.AppendLine("- reason codes: happy_path_expiry, taglost_interrupt_no_expiry, per_instance_isolation");
            report.AppendLine();
            report.AppendLine("## Summary Stats");
            report.AppendLine(FormattableString.Invariant($"- TimerExpired events: {expiredSources.Count} (unit A only, at T+{expiryTick:000})"));
            report.AppendLine(FormattableString.Invariant($"- TimerSet commands: {commandLog.Count(c => c.Kind == PresenterCommandKind.TimerSet)} | TimerKill commands: {commandLog.Count(c => c.Kind == PresenterCommandKind.TimerKill)} (unit B, wildcard, at T+{interruptTick:000})"));
            report.AppendLine(FormattableString.Invariant($"- SetParam flash.yellow: {commandLog.Count(c => c.Kind == PresenterCommandKind.SetParam)} (A: 1→0 via expiry; B: 1→0 via interrupt)"));
            report.AppendLine(FormattableString.Invariant($"- timer table high-water: {maxTimerCount}; final: {fixture.Timers.Count}"));
            string reportText = report.ToString();
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), reportText);
            TestContext.Out.WriteLine(reportText);
        }

        // ── 配置夹具辅助 ──

        private PresenterDefinitionRegistry LoadDefinitions()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(pipeline, registry).Load(catalog);
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

        private sealed class TimerFixture : IDisposable
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
            public readonly Entity Owner;

            private int _expiredEventCount;
            private PresentationEvent? _lastExpiredEvent;
            public readonly List<int> CreatedKeyIds = new();

            private TimerFixture(PresenterDefinitionRegistry? definitions = null)
            {
                World = Arch.Core.World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Instances = new PresenterEntityRuntime(World);
                Definitions = definitions ?? new PresenterDefinitionRegistry();
                Timers = new PresenterTimerTable(capacity: 64);
                Owner = this.World.Create();
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
                    new System.Collections.Generic.Dictionary<string, object>());
            }

            public static TimerFixture Create() => new();

            public static TimerFixture Create(PresenterDefinitionRegistry definitions) => new(definitions);

            public void FireEvent(in PresentationEvent evt)
            {
                Assert.That(Events.TryAdd(in evt), Is.True, "事件流应能容纳验收场景事件");
            }

            public int ExpiredEventCount => _expiredEventCount;

            public PresentationEvent? LastExpiredEvent => _lastExpiredEvent;

            public int RegisterRootWithoutRules()
            {
                return Definitions.Register("it.root", new PresenterDefinition());
            }

            public int RegisterRootWithPhaseRule(out int spawnedDefId)
            {
                int phaseNameId = PresenterTimerNameRegistry.Register("it.phase2");
                spawnedDefId = Definitions.Register("it.spawned", new PresenterDefinition());
                return Definitions.Register("it.root", new PresenterDefinition
                {
                    Rules = new[]
                    {
                        new PresenterRule
                        {
                            Event = new EventFilter
                            {
                                Kind = PresentationEventKind.TimerExpired,
                                KeyId = phaseNameId,
                            },
                            Command = new PresenterCommand
                            {
                                CommandKind = PresenterCommandKind.CreatePresenter,
                                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                                RouteStrategy = PerformerCommandRouteStrategy.CreatePerformer,
                                PresenterDefinitionId = spawnedDefId,
                                ScopeTag = 200,
                                ScopeSource = PresenterCommandScopeSource.Fixed,
                            },
                        },
                    },
                });
            }

            public int RegisterRootWithWildcardPhaseRule(out int spawnedDefId)
            {
                spawnedDefId = Definitions.Register("it.wildspawned", new PresenterDefinition());
                return Definitions.Register("it.wildroot", new PresenterDefinition
                {
                    Rules = new[]
                    {
                        new PresenterRule
                        {
                            Event = new EventFilter
                            {
                                Kind = PresentationEventKind.TimerExpired,
                                KeyId = -1,
                            },
                            Command = new PresenterCommand
                            {
                                CommandKind = PresenterCommandKind.CreatePresenter,
                                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                                RouteStrategy = PerformerCommandRouteStrategy.CreatePerformer,
                                PresenterDefinitionId = spawnedDefId,
                                ScopeTag = 300,
                                ScopeSource = PresenterCommandScopeSource.Fixed,
                            },
                        },
                    },
                });
            }

            public Entity CreateRoot(int definitionId, int scopeTag)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PerformerCommandRouteStrategy.CreatePerformer,
                    PresenterDefinitionId = definitionId,
                    ParentEntity = Entity.Null,
                    ScopeTag = scopeTag,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = Owner,
                    Target = Owner,
                });
                Runtime.Update(0.016f);

                ReadOnlySpan<PresentationEvent> events = Events.GetSpan();
                Assert.That(events.Length, Is.GreaterThan(0));
                Entity presenter = events[^1].PresenterEntity;
                Rules.Update(0.016f);
                return presenter;
            }

            public void SetTimer(Entity presenter, int nameId, float durationSeconds)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.TimerSet,
                    CommandKindId = (byte)PresenterCommandKind.TimerSet,
                    RouteStrategy = PerformerCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                    TimerNameId = nameId,
                    TimerDurationSeconds = durationSeconds,
                });
                Runtime.Update(0.016f);
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
            }

            public void DestroyPresenter(Entity presenter)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.DestroyPresenter,
                    CommandKindId = (byte)PresenterCommandKind.DestroyPresenter,
                    RouteStrategy = PerformerCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                });
                Runtime.Update(0.016f);
            }

            // 生产序：Timer → Rules → Runtime，到期事件当帧进规则
            public void TickAll(float dt)
            {
                TickTimerOnly(dt);
                Rules.Update(dt);
                Runtime.Update(dt);
            }

            public void TickTimerOnly(float dt)
            {
                TimerSystem.Update(dt);
                CaptureEvents();
            }

            public void CaptureEvents()
            {
                ReadOnlySpan<PresentationEvent> span = Events.GetSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i].Kind == PresentationEventKind.TimerExpired)
                    {
                        _expiredEventCount++;
                        _lastExpiredEvent = span[i];
                    }
                    else if (span[i].Kind == PresentationEventKind.PresenterCreated)
                    {
                        CreatedKeyIds.Add(span[i].KeyId);
                    }
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
