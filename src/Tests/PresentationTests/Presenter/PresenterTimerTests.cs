using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
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

            private TimerFixture()
            {
                World = Arch.Core.World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Instances = new PresenterEntityRuntime(World);
                Definitions = new PresenterDefinitionRegistry();
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
