using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphResumeTests
    {
        private const string MapId = "map_trigger_resume_probe";
        private const string GraphName = "Graph.TriggerGraph.ResumeProbe";
        private const string TemplateId = "map_trigger_resume_entity";
        private const string ScopeInstanceId = "resume-hero";
        private const string EntryEventName = "TriggerGraph.Resume.Probe";

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void BudgetSuspension_ResumesOnThinkWave_AndHaltsWithSeededRegister()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: true);
            using GameEngine engine = fixture.CreateEngine();
            fixture.RegisterTriggerGraph(engine, BudgetSuspensionProgram(haltRegister: 1), new[]
            {
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: false),
            });
            engine.LoadMap(MapId);

            TriggerGraphMountTrigger mount = FindMountTrigger(engine, "probe");
            TriggerGraphResumeTrigger resume = FindResumeTrigger(engine, mount);
            Assert.That(resume.CheckConditions(engine.CreateContext()), Is.False,
                "A wave with no suspended run must not resume anything.");

            ScriptContext entryContext = engine.CreateContext();
            entryContext.Set(MapTriggerEventPayloadKeys.Count, 4242);
            engine.TriggerManager.FireMapEvent(new MapId(MapId), new EventKey(EntryEventName), entryContext);

            Assert.That(mount.IsSuspended, Is.True, "Slice-budget suspension must park the run.");
            Assert.That(mount.LastSliceResult.BudgetSuspended, Is.True);
            Assert.That(mount.DroppedCount, Is.EqualTo(0));

            var heartbeatContext = engine.CreateContext();
            heartbeatContext.Set(MapTriggerEventPayloadKeys.HeartbeatIndex, 1);
            engine.TriggerManager.FireMapEvent(new MapId(MapId), GameEvents.MapHeartbeat, heartbeatContext);

            Assert.That(mount.IsSuspended, Is.False, "The think wave must resume the suspended run.");
            Assert.That(mount.LastSliceResult.Halted, Is.True);
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(4242),
                "Registers seeded at entry dispatch must survive the suspension and feed the halt value.");
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        [Test]
        public void YieldSuspension_ResumesOnThinkWave_AndHaltsWithSeededRegister()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Yield },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            };
            int graphId = fixture.RegisterTriggerGraph(engine, program, new[]
            {
                new TriggerGraphEntry("wait", EntryEventName, startPc: 0, once: false),
            });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName,
                new TriggerGraphEntry("wait", EntryEventName, startPc: 0, once: false), Entity.Null);
            var resume = new TriggerGraphResumeTrigger(mount);
            Assert.That(resume.CheckConditions(engine.CreateContext()), Is.False,
                "A wave with no suspended run must not resume anything.");

            ScriptContext entryContext = engine.CreateContext();
            entryContext.Set(MapTriggerEventPayloadKeys.Count, 4242);
            mount.ExecuteAsync(entryContext);

            Assert.That(mount.IsSuspended, Is.True, "Yield must park the run until the think wave.");
            Assert.That(mount.LastSliceResult.Yielded, Is.True);

            resume.ExecuteAsync(engine.CreateContext());

            Assert.That(mount.IsSuspended, Is.False);
            Assert.That(mount.LastSliceResult.Halted, Is.True);
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(4242),
                "Registers seeded at entry dispatch must survive the yield and feed the halt value.");
        }

        [Test]
        public void RefireIgnore_SecondEventWhileSuspended_DroppedAndOriginalRunCompletes()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            int graphId = fixture.RegisterTriggerGraph(engine, BudgetSuspensionProgram(haltRegister: 1), new[]
            {
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: false),
            });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName,
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: false), Entity.Null);
            var resume = new TriggerGraphResumeTrigger(mount);

            ScriptContext first = engine.CreateContext();
            first.Set(MapTriggerEventPayloadKeys.Count, 41);
            mount.ExecuteAsync(first);

            Assert.That(mount.IsSuspended, Is.True);

            ScriptContext second = engine.CreateContext();
            second.Set(MapTriggerEventPayloadKeys.Count, 99);
            mount.ExecuteAsync(second);

            Assert.That(mount.DroppedCount, Is.EqualTo(1), "The ignored refire must be counted.");
            Assert.That(mount.IsSuspended, Is.True, "An ignored refire must not disturb the suspended run.");

            resume.ExecuteAsync(engine.CreateContext());

            Assert.That(mount.LastSliceResult.Halted, Is.True);
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(41),
                "The original run completes with its own seeded registers, not the dropped event's.");
            Assert.That(mount.DroppedCount, Is.EqualTo(1));
        }

        [Test]
        public void RefireRestart_SecondEventWhileSuspended_RestartsFromStartPc()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            int graphId = fixture.RegisterTriggerGraph(engine, BudgetSuspensionProgram(haltRegister: 1), new[]
            {
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: false),
            });
            var mount = new TriggerGraphMountTrigger(
                graphId,
                GraphName,
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: false),
                Entity.Null,
                TriggerGraphRefirePolicy.Restart);
            var resume = new TriggerGraphResumeTrigger(mount);

            ScriptContext first = engine.CreateContext();
            first.Set(MapTriggerEventPayloadKeys.Count, 41);
            mount.ExecuteAsync(first);

            Assert.That(mount.IsSuspended, Is.True);

            ScriptContext second = engine.CreateContext();
            second.Set(MapTriggerEventPayloadKeys.Count, 99);
            mount.ExecuteAsync(second);

            Assert.That(mount.DroppedCount, Is.EqualTo(0));
            Assert.That(mount.IsSuspended, Is.True,
                "The restarted run replays from StartPc and suspends on the same slice budget.");

            resume.ExecuteAsync(engine.CreateContext());

            Assert.That(mount.LastSliceResult.Halted, Is.True);
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(99),
                "The restart re-seeds registers from the restarting event before re-executing from StartPc.");
        }

        [Test]
        public void Once_CompletedRun_SecondEventDoesNothing()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            };
            int graphId = fixture.RegisterTriggerGraph(engine, program, new[]
            {
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: true),
            });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName,
                new TriggerGraphEntry("probe", EntryEventName, startPc: 0, once: true), Entity.Null);

            ScriptContext first = engine.CreateContext();
            first.Set(MapTriggerEventPayloadKeys.Count, 41);
            mount.ExecuteAsync(first);

            Assert.That(mount.LastSliceResult.Halted, Is.True);
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(41));

            ScriptContext second = engine.CreateContext();
            second.Set(MapTriggerEventPayloadKeys.Count, 99);
            mount.ExecuteAsync(second);

            Assert.That(mount.CheckConditions(engine.CreateContext()), Is.False,
                "A once entry allows one halted run per map lifetime.");
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(41));
            Assert.That(mount.LastSliceResult.Steps, Is.EqualTo(1));
        }

        [Test]
        public void PerRunCap_LoopAcrossWaves_FailsNamingGraphAndEntry()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: true);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            fixture.RegisterTriggerGraph(engine, program, new[]
            {
                new TriggerGraphEntry("spin", EntryEventName, startPc: 0, once: false),
            });
            engine.LoadMap(MapId);

            engine.TriggerManager.FireMapEvent(new MapId(MapId), new EventKey(EntryEventName), engine.CreateContext());

            var waveKey = GameEvents.MapHeartbeat;
            for (int wave = 0; wave < 200 && engine.TriggerManager.Errors.Count == 0; wave++)
            {
                var waveContext = engine.CreateContext();
                waveContext.Set(MapTriggerEventPayloadKeys.HeartbeatIndex, wave + 1);
                engine.TriggerManager.FireMapEvent(new MapId(MapId), waveKey, waveContext);
            }

            Assert.That(engine.TriggerManager.Errors.Count, Is.GreaterThan(0),
                "A run that never halts across waves must fail once the per-run instruction cap is reached.");
            TriggerError error = engine.TriggerManager.Errors[engine.TriggerManager.Errors.Count - 1];
            Assert.That(error.TriggerName, Is.EqualTo($"TriggerGraph:{GraphName}:spin:Resume"),
                "The cap failure surfaces on the resume companion that executed the failing slice.");
            Assert.That(error.Exception.Message, Does.Contain(GraphName));
            Assert.That(error.Exception.Message, Does.Contain("spin"));
            Assert.That(error.Exception.Message, Does.Contain(nameof(GraphVmLimits.MaxInstructionsPerExecution)));
        }

        [Test]
        public void RegisterSeeding_PayloadRegistersVisibleToGraph()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            int graphId = fixture.RegisterTriggerGraph(engine, program, new[]
            {
                new TriggerGraphEntry("byCount", EntryEventName, startPc: 0, once: false),
                new TriggerGraphEntry("byTeam", EntryEventName, startPc: 1, once: false),
            });
            var byCount = new TriggerGraphMountTrigger(graphId, GraphName,
                new TriggerGraphEntry("byCount", EntryEventName, startPc: 0, once: false), Entity.Null);
            var byTeam = new TriggerGraphMountTrigger(graphId, GraphName,
                new TriggerGraphEntry("byTeam", EntryEventName, startPc: 1, once: false), Entity.Null);

            ScriptContext countContext = engine.CreateContext();
            countContext.Set(MapTriggerEventPayloadKeys.Count, 41);
            byCount.ExecuteAsync(countContext);

            ScriptContext teamContext = engine.CreateContext();
            teamContext.Set(MapTriggerEventPayloadKeys.SourceTeamId, 7);
            byTeam.ExecuteAsync(teamContext);

            Assert.That(byCount.LastSliceResult.ReturnInt, Is.EqualTo(41),
                "I[1] must be seeded from the TriggerGraph.Count payload.");
            Assert.That(byTeam.LastSliceResult.ReturnInt, Is.EqualTo(7),
                "I[0] must be seeded from the TriggerGraph.SourceTeamId payload.");
        }

        [Test]
        public void ResumeEventEntry_SelfResumesOnItsOwnTick_WithoutCompanion()
        {
            using var fixture = TriggerGraphResumeFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            int graphId = fixture.RegisterTriggerGraph(engine, BudgetSuspensionProgram(haltRegister: 1), new[]
            {
                new TriggerGraphEntry("wave", GameEvents.MapHeartbeat.Value, startPc: 0, once: false),
            });
            var mount = new TriggerGraphMountTrigger(graphId, GraphName,
                new TriggerGraphEntry("wave", GameEvents.MapHeartbeat.Value, startPc: 0, once: false),
                Entity.Null);

            Assert.That(mount.EntryIsResumeEvent, Is.True);

            ScriptContext first = engine.CreateContext();
            first.Set(MapTriggerEventPayloadKeys.Count, 41);
            mount.ExecuteAsync(first);

            Assert.That(mount.IsSuspended, Is.True);

            ScriptContext second = engine.CreateContext();
            second.Set(MapTriggerEventPayloadKeys.Count, 99);
            mount.ExecuteAsync(second);

            Assert.That(mount.LastSliceResult.Halted, Is.True,
                "An entry named as the resume event resumes the suspended run on its own dispatch.");
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(41),
                "The resume continues the parked run instead of restarting with the new dispatch payload.");
            Assert.That(mount.DroppedCount, Is.EqualTo(0));
        }

        private static TriggerGraphMountTrigger FindMountTrigger(GameEngine engine, string label)
        {
            IReadOnlyList<Trigger> triggers = engine.CurrentMapSession?.Triggers ?? Array.Empty<Trigger>();
            TriggerGraphMountTrigger? mount = triggers
                .OfType<TriggerGraphMountTrigger>()
                .FirstOrDefault(t => t.Name == $"TriggerGraph:{GraphName}:{label}");
            Assert.That(mount, Is.Not.Null, $"Mount trigger for entry '{label}' must be registered.");
            return mount!;
        }

        private static TriggerGraphResumeTrigger FindResumeTrigger(GameEngine engine, TriggerGraphMountTrigger mount)
        {
            IReadOnlyList<Trigger> triggers = engine.CurrentMapSession?.Triggers ?? Array.Empty<Trigger>();
            TriggerGraphResumeTrigger? resume = triggers
                .OfType<TriggerGraphResumeTrigger>()
                .FirstOrDefault(t => t.Name == $"{mount.Name}:Resume");
            Assert.That(resume, Is.Not.Null, "Each non-resume entry must register a think-wave companion.");
            return resume!;
        }

        private static GraphInstruction[] BudgetSuspensionProgram(byte haltRegister)
        {
            var program = new GraphInstruction[TriggerGraphLimits.SliceBudgetSteps + 1];
            for (int i = 0; i < TriggerGraphLimits.SliceBudgetSteps; i++)
            {
                program[i] = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 5, Imm = i };
            }

            program[TriggerGraphLimits.SliceBudgetSteps] =
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = haltRegister };
            return program;
        }

        private sealed class TriggerGraphResumeFixture : IDisposable
        {
            private const string ModId = "TriggerGraphResumeFixtureMod";

            private TriggerGraphResumeFixture(string root)
            {
                Root = root;
            }

            public string Root { get; }

            public static TriggerGraphResumeFixture Create(bool includeMapMount)
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_TriggerGraphResumeTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Entities"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Maps"));

                File.WriteAllText(
                    Path.Combine(root, ModId, "mod.json"),
                    $$"""
                    {
                      "name": "{{ModId}}",
                      "version": "1.0.0",
                      "description": "Asset-only TriggerGraph resume fixture",
                      "priority": 0,
                      "dependencies": {}
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "game.json"),
                    """
                    {
                      "startupMapId": "map_trigger_resume_probe",
                      "startupInputContexts": [],
                      "presentation": {
                        "presenterInstanceCapacity": 16,
                        "gasPresentationEventCapacity": 16,
                        "presentationEventStreamCapacity": 16,
                        "presentationOwnerChangeCapacity": 16,
                        "presenterCommandCapacity": 16,
                        "presenterTimerCapacity": 16,
                        "primitiveDrawBufferCapacity": 16,
                        "visualSnapshotBufferCapacity": 16,
                        "visualProxyBufferCapacity": 16,
                        "skinnedVisualBatchCapacity": 16,
                        "presentationRequestCapacity": 16,
                        "instancedBatchRequestCapacity": 16,
                        "instancedBatchOperationCapacity": 16,
                        "groundOverlayCapacity": 16,
                        "splineRibbonCapacity": 16,
                        "worldHudCapacity": 16,
                        "screenHudCapacity": 16,
                        "minimapMarkerCapacity": 16,
                        "runtimeEntitySpawnQueueCapacity": 16,
                        "runtimeEntitySpawnReceiptQueueCapacity": 16,
                        "cameraCulling": {
                          "highLodDistanceCm": 1000.0,
                          "mediumLodDistanceCm": 2000.0,
                          "lowLodDistanceCm": 3000.0
                        },
                        "minimap": {
                          "initialZoomNormalized": 1.0,
                          "wheelZoomNormalizedStep": 0.1,
                          "buttonZoomNormalizedStep": 0.2,
                          "zoomSliderEnabled": true,
                          "modeToggleEnabled": true,
                          "rotateToggleEnabled": true,
                          "debugMarkerSampleCapacity": 0,
                          "minZoomExtentMode": "OneChunk",
                          "maxZoomExtentMode": "FullMap",
                          "minZoomExplicitHalfExtentCm": 0.0,
                          "maxZoomExplicitHalfExtentCm": 0.0
                        }
                      },
                      "constants": {
                        "orderTypeIds": {
                          "castAbility": 100,
                          "moveTo": 101,
                          "attackTarget": 102,
                          "stop": 103
                        },
                        "responseChainOrderTypeIds": {
                          "chainPass": 1,
                          "chainNegate": 2,
                          "chainActivateEffect": 3
                        },
                        "attributes": {
                          "health": "Health"
                        }
                      }
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "Entities", "templates.json"),
                    $$"""
                    [
                      {
                        "id": "{{TemplateId}}",
                        "components": {
                          "Name": { "Value": "Resume Probe Entity" }
                        }
                      }
                    ]
                    """);
                string mapJson = includeMapMount
                    ? $$"""
                      {
                        "Id": "{{MapId}}",
                        "Tags": [ "camera.skip_default_on_load" ],
                        "Entities": [
                          { "InstanceId": "{{ScopeInstanceId}}", "Template": "{{TemplateId}}" }
                        ],
                        "TriggerGraphs": [ { "graph": "{{GraphName}}", "scopeInstanceId": "{{ScopeInstanceId}}" } ]
                      }
                      """
                    : $$"""
                      {
                        "Id": "{{MapId}}",
                        "Tags": [ "camera.skip_default_on_load" ],
                        "Entities": [
                          { "InstanceId": "{{ScopeInstanceId}}", "Template": "{{TemplateId}}" }
                        ]
                      }
                      """;
                File.WriteAllText(Path.Combine(root, ModId, "assets", "Maps", $"{MapId}.json"), mapJson);
                return new TriggerGraphResumeFixture(root);
            }

            public GameEngine CreateEngine()
            {
                string repoRoot = FindRepoRoot();
                var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    new List<string>
                    {
                        Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                        Path.Combine(Root, ModId),
                    },
                    Path.Combine(repoRoot, "assets"));
                engine.GetService(CoreServiceKeys.CustomEventNameRegistry)?.Register(EntryEventName);
                return engine;
            }

            public int RegisterTriggerGraph(
                GameEngine engine,
                GraphInstruction[] program,
                TriggerGraphEntry[] entries)
            {
                RegistryMapping[] mappings = GraphIdRegistry.SnapshotMappings();
                GraphIdRegistry.Clear();
                Array.Sort(mappings, (a, b) => a.Id.CompareTo(b.Id));
                for (int i = 0; i < mappings.Length; i++)
                {
                    GraphIdRegistry.Register(mappings[i].Name);
                }

                int graphId = GraphIdRegistry.Register(GraphName);
                engine.GetService(CoreServiceKeys.GraphProgramRegistry)
                    .Register(graphId, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, null, entries);
                return graphId;
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }

            private static string FindRepoRoot()
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "assets")) &&
                        Directory.Exists(Path.Combine(dir.FullName, "src")))
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent!;
                }

                throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
            }
        }
    }
}
