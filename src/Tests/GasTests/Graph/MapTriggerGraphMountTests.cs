using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class MapTriggerGraphMountTests
    {
        private const string MapId = "map_trigger_mount_probe";
        private const string GraphName = "Graph.MapTrigger.Probe";
        private const string TemplateId = "map_trigger_mount_entity";
        private const string ScopeInstanceId = "mount-hero";

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void ParseList_MountWithUnknownField_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                $$"""{ "graph": "{{GraphName}}", "priority": 1 }""")!;

            string? message = null;
            try
            {
                MapTriggerGraphMount.ParseList(new JsonArray(node), MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("priority"));
        }

        [Test]
        public void ParseList_MountWithoutGraph_Rejected()
        {
            JsonNode node = JsonNode.Parse("""{ "scopeInstanceId": "hero" }""")!;

            string? message = null;
            try
            {
                MapTriggerGraphMount.ParseList(new JsonArray(node), MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("graph"));
        }

        [Test]
        public void ParseList_EmptyGraph_Rejected()
        {
            JsonNode node = JsonNode.Parse("""{ "graph": "" }""")!;

            string? message = null;
            try
            {
                MapTriggerGraphMount.ParseList(new JsonArray(node), MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
        }

        [Test]
        public void ParseList_WhitespaceScopeInstanceId_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                $$"""{ "graph": "{{GraphName}}", "scopeInstanceId": "   " }""")!;

            string? message = null;
            try
            {
                MapTriggerGraphMount.ParseList(new JsonArray(node), MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("scopeInstanceId"));
        }

        [Test]
        public void ParseList_UntrimmedGraph_Rejected()
        {
            JsonNode node = JsonNode.Parse("""{ "graph": " Graph.Probe " }""")!;

            Assert.That(
                () => MapTriggerGraphMount.ParseList(new JsonArray(node), MapId),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ParseList_NonArrayNode_Rejected()
        {
            JsonNode node = JsonNode.Parse("""{ "graph": "Graph.Probe" }""")!;

            Assert.That(
                () => MapTriggerGraphMount.ParseList(node, MapId),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ParseList_MinimalMountsWithoutScope_Accepted()
        {
            List<MapTriggerGraphMount> mounts = MapTriggerGraphMount.ParseList(
                JsonNode.Parse($$"""[ { "graph": "{{GraphName}}" } ]"""),
                MapId);

            Assert.That(mounts.Count, Is.EqualTo(1));
            Assert.That(mounts[0].Graph, Is.EqualTo(GraphName));
            Assert.That(mounts[0].ScopeInstanceId, Is.Null);
        }

        [Test]
        public void ParseList_MissingNode_YieldsNoMounts()
        {
            List<MapTriggerGraphMount> mounts = MapTriggerGraphMount.ParseList(null, MapId);

            Assert.That(mounts.Count, Is.EqualTo(0));
        }

        [Test]
        public void BuildTriggers_UnregisteredGraphName_Throws()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world,
                $$"""[ { "graph": "Graph.Missing", "scopeInstanceId": "{{ScopeInstanceId}}" } ]""");

            string? message = null;
            try
            {
                MapTriggerGraphMounting.BuildTriggers(session, new GraphProgramRegistry());
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("Graph.Missing"));
        }

        [Test]
        public void BuildTriggers_NonMapTriggerKind_Throws()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world, $$"""[ { "graph": "{{GraphName}}" } ]""");
            var programs = new GraphProgramRegistry();
            RegisterProgram(programs, GraphName, GraphKind.Script, HaltProgram(), entries: null);

            string? message = null;
            try
            {
                MapTriggerGraphMounting.BuildTriggers(session, programs);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain(GraphName));
            Assert.That(message, Does.Contain("Script"));
        }

        [Test]
        public void BuildTriggers_UnresolvedScopeInstanceId_Throws()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world,
                $$"""[ { "graph": "{{GraphName}}", "scopeInstanceId": "ghost-hero" } ]""");
            var programs = new GraphProgramRegistry();
            RegisterProgram(programs, GraphName, GraphKind.MapTrigger, HaltProgram(), entries: ProbeEntries());

            string? message = null;
            try
            {
                MapTriggerGraphMounting.BuildTriggers(session, programs);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("ghost-hero"));
        }

        [Test]
        public void BuildTriggers_ValidMount_BuildsOneTriggerPerEntryWithResolvedScope()
        {
            using var world = World.Create();
            MapSession session = CreateSession(world,
                $$"""[ { "graph": "{{GraphName}}", "scopeInstanceId": "{{ScopeInstanceId}}" } ]""");
            var programs = new GraphProgramRegistry();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 3, Imm = 4242 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 3 },
            };
            RegisterProgram(programs, GraphName, GraphKind.MapTrigger, program, entries: new[]
            {
                new MapTriggerGraphEntry("open", GameEvents.MapLoaded.Value, 0, once: true),
                new MapTriggerGraphEntry("close", GameEvents.MapUnloaded.Value, 0, once: false),
            });

            List<MapTriggerGraphMountTrigger> triggers = MapTriggerGraphMounting.BuildTriggers(session, programs);

            Assert.That(triggers.Count, Is.EqualTo(2));
            Assert.That(triggers[0].Name, Is.EqualTo($"MapTriggerGraph:{GraphName}:open"));
            Assert.That(triggers[0].EventKey, Is.EqualTo(GameEvents.MapLoaded));
            Assert.That(triggers[1].Name, Is.EqualTo($"MapTriggerGraph:{GraphName}:close"));
            Assert.That(triggers[1].EventKey, Is.EqualTo(GameEvents.MapUnloaded));
        }

        [Test]
        public void ExecuteAsync_HaltsWithReturnIntFromEntryStartPc_AndOnceSkipsSecondRun()
        {
            using var fixture = MapTriggerEngineFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 4242 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 2 },
            };
            int graphId = fixture.RegisterMapTriggerGraph(engine, program, new[]
            {
                new MapTriggerGraphEntry("probe", GameEvents.MapLoaded.Value, startPc: 1, once: true),
            });
            var trigger = new MapTriggerGraphMountTrigger(graphId, GraphName,
                new MapTriggerGraphEntry("probe", GameEvents.MapLoaded.Value, startPc: 1, once: true),
                Entity.Null);

            ScriptContext context = engine.CreateContext();
            trigger.ExecuteAsync(context);

            Assert.That(trigger.LastSliceResult.Halted, Is.True);
            Assert.That(trigger.LastSliceResult.ReturnInt, Is.EqualTo(0), "Execution must start at the entry start pc, skipping the ConstInt 4242.");
            Assert.That(trigger.CheckConditions(context), Is.False, "Once entries must not fire twice.");

            trigger.ExecuteAsync(context);
            Assert.That(trigger.LastSliceResult.Steps, Is.EqualTo(1));
        }

        [Test]
        public void ExecuteAsync_WithoutOnce_RerunsOnEveryFire()
        {
            using var fixture = MapTriggerEngineFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 3, Imm = 4242 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 3 },
            };
            int graphId = fixture.RegisterMapTriggerGraph(engine, program, new[]
            {
                new MapTriggerGraphEntry("probe", GameEvents.MapLoaded.Value, startPc: 0, once: false),
            });
            var trigger = new MapTriggerGraphMountTrigger(graphId, GraphName,
                new MapTriggerGraphEntry("probe", GameEvents.MapLoaded.Value, startPc: 0, once: false),
                Entity.Null);

            ScriptContext context = engine.CreateContext();
            trigger.ExecuteAsync(context);
            trigger.ExecuteAsync(context);

            Assert.That(trigger.LastSliceResult.Halted, Is.True);
            Assert.That(trigger.LastSliceResult.ReturnInt, Is.EqualTo(4242));
            Assert.That(trigger.CheckConditions(context), Is.True);
        }

        [Test]
        public void ExecuteAsync_BudgetSuspendedInsteadOfHalt_Throws()
        {
            using var fixture = MapTriggerEngineFixture.Create(includeMapMount: false);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            int graphId = fixture.RegisterMapTriggerGraph(engine, program, new[]
            {
                new MapTriggerGraphEntry("spin", GameEvents.MapLoaded.Value, startPc: 0, once: false),
            });
            var trigger = new MapTriggerGraphMountTrigger(graphId, GraphName,
                new MapTriggerGraphEntry("spin", GameEvents.MapLoaded.Value, startPc: 0, once: false),
                Entity.Null);

            string? message = null;
            try
            {
                trigger.ExecuteAsync(engine.CreateContext());
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(GraphName));
            Assert.That(message, Does.Contain("spin"));
            Assert.That(message, Does.Contain("BudgetSuspended"));
        }

        [Test]
        public void LoadMap_WithMapTriggerGraphs_RegistersMountsAndFiresMapLoadedEntry()
        {
            using var fixture = MapTriggerEngineFixture.Create(includeMapMount: true);
            using GameEngine engine = fixture.CreateEngine();
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 3, Imm = 4242 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 3 },
            };
            fixture.RegisterMapTriggerGraph(engine, program, new[]
            {
                new MapTriggerGraphEntry("open", GameEvents.MapLoaded.Value, startPc: 0, once: true),
            });

            engine.LoadMap(MapId);

            MapTriggerGraphMountTrigger? mount = FindMountTrigger(engine);
            Assert.That(mount, Is.Not.Null, "LoadMap must instantiate MapTriggerGraph mount triggers.");
            Assert.That(mount!.Name, Is.EqualTo($"MapTriggerGraph:{GraphName}:open"));
            Assert.That(mount.EventKey, Is.EqualTo(GameEvents.MapLoaded));
            Assert.That(mount.LastSliceResult.Halted, Is.True, "The MapLoaded entry must run during map load.");
            Assert.That(mount.LastSliceResult.ReturnInt, Is.EqualTo(4242));
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(engine.TriggerManager.Get<MapTriggerGraphMountTrigger>(), Is.SameAs(mount));

            engine.UnloadMap(MapId);
            Assert.That(engine.TriggerManager.Get<MapTriggerGraphMountTrigger>(), Is.Null, "Map unload must unregister mount triggers.");
        }

        [Test]
        public void LoadMap_WithUnknownMountedGraph_Throws()
        {
            using var fixture = MapTriggerEngineFixture.Create(includeMapMount: true, graphName: "Graph.Missing");
            using GameEngine engine = fixture.CreateEngine();

            string? message = null;
            try
            {
                engine.LoadMap(MapId);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapId));
            Assert.That(message, Does.Contain("Graph.Missing"));
        }

        private static MapTriggerGraphMountTrigger? FindMountTrigger(GameEngine engine)
        {
            IReadOnlyList<Trigger> triggers = engine.CurrentMapSession?.Triggers ?? Array.Empty<Trigger>();
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i] is MapTriggerGraphMountTrigger mount)
                {
                    return mount;
                }
            }

            return null;
        }

        private static MapSession CreateSession(World world, string mountsJson)
        {
            var config = new MapConfig { Id = MapId };
            config.MapTriggerGraphs = JsonNode.Parse(mountsJson);
            var session = new MapSession(new MapId(MapId), config);
            var index = new MapLoadEntityIndex();
            index.Register(MapId, ScopeInstanceId, world.Create());
            session.EntityIndex = index;
            return session;
        }

        private static GraphInstruction[] HaltProgram()
            => new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

        private static MapTriggerGraphEntry[] ProbeEntries()
            => new[] { new MapTriggerGraphEntry("probe", GameEvents.MapLoaded.Value, 0, once: false) };

        private static void RegisterProgram(
            GraphProgramRegistry programs,
            string name,
            GraphKind kind,
            GraphInstruction[] program,
            MapTriggerGraphEntry[]? entries)
        {
            int id = GraphIdRegistry.Register(name);
            programs.Register(id, program, kind, GraphInstructionSourceMap.Empty, null, entries);
        }

        private sealed class MapTriggerEngineFixture : IDisposable
        {
            private const string ModId = "MapTriggerMountFixtureMod";

            private MapTriggerEngineFixture(string root)
            {
                Root = root;
            }

            public string Root { get; }

            public static MapTriggerEngineFixture Create(bool includeMapMount, string? graphName = null)
            {
                string effectiveGraphName = graphName ?? GraphName;
                string root = Path.Combine(Path.GetTempPath(), "Ludots_MapTriggerGraphMountTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Entities"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Maps"));

                File.WriteAllText(
                    Path.Combine(root, ModId, "mod.json"),
                    $$"""
                    {
                      "name": "{{ModId}}",
                      "version": "1.0.0",
                      "description": "Asset-only MapTriggerGraph mount fixture",
                      "priority": 0,
                      "dependencies": {}
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "game.json"),
                    """
                    {
                      "startupMapId": "map_trigger_mount_probe",
                      "startupInputContexts": [],
                      "presentation": {
                        "presenterInstanceCapacity": 16,
                        "gasPresentationEventCapacity": 16,
                        "presentationEventStreamCapacity": 16,
                        "presentationOwnerChangeCapacity": 16,
                        "presenterCommandCapacity": 16,
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
                          "Name": { "Value": "Mount Probe Entity" }
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
                        "MapTriggerGraphs": [ { "graph": "{{effectiveGraphName}}", "scopeInstanceId": "{{ScopeInstanceId}}" } ]
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
                return new MapTriggerEngineFixture(root);
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
                return engine;
            }

            public int RegisterMapTriggerGraph(
                GameEngine engine,
                GraphInstruction[] program,
                MapTriggerGraphEntry[] entries)
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
                    .Register(graphId, program, GraphKind.MapTrigger, GraphInstructionSourceMap.Empty, null, entries);
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

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
            }
        }
    }
}
