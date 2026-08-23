using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
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
    public sealed class MapVariableStoreTests
    {
        private const string MapIdValue = "map_variable_store_probe";
        private const string GraphName = "Graph.MapVar.Probe";
        private const string TemplateId = "map_variable_store_entity";
        private const string ScopeInstanceId = "store-hero";

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
        }

        // ── Declaration strictness ──

        [Test]
        public void Parse_UnknownField_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                """[ { "name": "kills", "type": "int", "initial": 0, "priority": 1 } ]""");

            string? message = null;
            try
            {
                MapVariableDeclarations.Parse(node, MapIdValue);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapIdValue));
            Assert.That(message, Does.Contain("priority"));
        }

        [Test]
        public void Parse_NonArrayNode_Rejected()
        {
            JsonNode node = JsonNode.Parse("""{ "name": "kills" }""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("array"));
        }

        [Test]
        public void Parse_NonObjectItem_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ 42 ]""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("Variables[0]"));
        }

        [Test]
        public void Parse_EmptyName_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "name": "   ", "type": "int", "initial": 0 } ]""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("name"));
        }

        [Test]
        public void Parse_DuplicateNames_Rejected()
        {
            JsonNode node = JsonNode.Parse(
                """
                [
                  { "name": "kills", "type": "int", "initial": 0 },
                  { "name": "kills", "type": "int", "initial": 3 }
                ]
                """);

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("kills"));
        }

        [Test]
        public void Parse_MissingInitial_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "name": "kills", "type": "int" } ]""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("initial"));
        }

        [Test]
        public void Parse_MissingType_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "name": "kills", "initial": 0 } ]""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("type"));
        }

        [Test]
        public void Parse_UnknownType_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "name": "kills", "type": "string", "initial": 0 } ]""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("int"));
        }

        [Test]
        public void Parse_NonIntegralIntInitial_Rejected()
        {
            JsonNode node = JsonNode.Parse("""[ { "name": "kills", "type": "int", "initial": 1.5 } ]""");

            Assert.That(
                () => MapVariableDeclarations.Parse(node, MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("integral"));
        }

        [Test]
        public void Parse_MissingNode_YieldsNoDeclarations()
        {
            List<MapVariableDeclaration> declarations = MapVariableDeclarations.Parse(null, MapIdValue);

            Assert.That(declarations.Count, Is.EqualTo(0));
        }

        [Test]
        public void Parse_ValidDeclarations_TrimNamesAndKeepPhaseFlag()
        {
            JsonNode node = JsonNode.Parse(
                """
                [
                  { "name": " kills ", "type": "int", "initial": 2, "phase": true },
                  { "name": "ammo", "type": "float", "initial": 1.5 }
                ]
                """);

            List<MapVariableDeclaration> declarations = MapVariableDeclarations.Parse(node, MapIdValue);

            Assert.That(declarations.Count, Is.EqualTo(2));
            Assert.That(declarations[0].Name, Is.EqualTo("kills"));
            Assert.That(declarations[0].Type, Is.EqualTo(MapVariableType.Int));
            Assert.That(declarations[0].Initial, Is.EqualTo(2));
            Assert.That(declarations[0].Phase, Is.True);
            Assert.That(declarations[1].Type, Is.EqualTo(MapVariableType.Float));
            Assert.That(declarations[1].Initial, Is.EqualTo(1.5));
            Assert.That(declarations[1].Phase, Is.False);
        }

        // ── Store fail-closed + revisions + phase events ──

        [Test]
        public void Store_UndeclaredRead_ThrowsNamingMapAndVar()
        {
            MapVariableStore store = MapVariableStore.Create(new MapId(MapIdValue), null);

            string? message = null;
            try
            {
                store.ReadInt("kills");
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain(MapIdValue));
            Assert.That(message, Does.Contain("kills"));
        }

        [Test]
        public void Store_UndeclaredWrite_ThrowsNamingMapAndVar()
        {
            MapVariableStore store = MapVariableStore.Create(new MapId(MapIdValue), null);

            Assert.That(
                () => store.WriteFloat("morale", 1f),
                Throws.InvalidOperationException.With.Message.Contains(MapIdValue).And.Message.Contains("morale"));
        }

        [Test]
        public void Store_WrongTypeAccess_Throws()
        {
            MapVariableStore store = MapVariableStore.Create(new MapId(MapIdValue), new[]
            {
                new MapVariableDeclaration { Name = "kills", Type = MapVariableType.Int, Initial = 0 },
            });

            Assert.That(() => store.ReadFloat("kills"), Throws.InvalidOperationException);
            MapVariableStore floatStore = MapVariableStore.Create(new MapId(MapIdValue), new[]
            {
                new MapVariableDeclaration { Name = "morale", Type = MapVariableType.Float, Initial = 0.5 },
            });
            Assert.That(() => floatStore.WriteInt("morale", 1), Throws.InvalidOperationException);
        }

        [Test]
        public void Store_Revision_MonotonicOnWrite_UnchangedOnRead()
        {
            MapVariableStore store = MapVariableStore.Create(new MapId(MapIdValue), new[]
            {
                new MapVariableDeclaration { Name = "kills", Type = MapVariableType.Int, Initial = 0 },
            });

            Assert.That(store.GetRevision("kills"), Is.EqualTo(0u));
            _ = store.ReadInt("kills");
            Assert.That(store.GetRevision("kills"), Is.EqualTo(0u), "reads must not bump the revision");
            store.WriteInt("kills", 1);
            uint afterFirstWrite = store.GetRevision("kills");
            Assert.That(afterFirstWrite, Is.GreaterThan(0u));
            store.WriteInt("kills", 1);
            Assert.That(store.GetRevision("kills"), Is.GreaterThan(afterFirstWrite), "every write bumps the revision");
        }

        [Test]
        public void Store_PhaseChanged_FiresOnChangeOnly_AndNotForNonPhaseVars()
        {
            MapVariableStore store = MapVariableStore.Create(new MapId(MapIdValue), new[]
            {
                new MapVariableDeclaration { Name = "phase", Type = MapVariableType.Int, Initial = 0, Phase = true },
                new MapVariableDeclaration { Name = "plain", Type = MapVariableType.Int, Initial = 0 },
            });
            var changes = new List<(string Name, int Value)>();
            store.PhaseChangedDispatcher = (_, name, value) => changes.Add((name, value));

            store.WriteInt("phase", 1);
            store.WriteInt("phase", 1);
            store.WriteInt("plain", 5);
            store.WriteInt("phase", 2);

            Assert.That(changes, Is.EqualTo(new[] { ("phase", 1), ("phase", 2) }));
        }

        // ── Session lifecycle ──

        [Test]
        public void LoadMap_DeclaringVariables_CreatesStore_AndUnloadDestroysIt()
        {
            using var fixture = MapVarEngineFixture.Create(includeMount: false);
            using GameEngine engine = fixture.CreateEngine();

            engine.LoadMap(MapIdValue);

            MapSession session = engine.CurrentMapSession;
            Assert.That(session, Is.Not.Null);
            Assert.That(session.Variables, Is.Not.Null);
            Assert.That(session.Variables!.Count, Is.EqualTo(2));
            Assert.That(session.Variables.ReadInt("probe.kills"), Is.EqualTo(0));
            Assert.That(session.Variables.ReadFloat("probe.ammo"), Is.EqualTo(1.5f).Within(0.0001f));

            engine.UnloadMap(MapIdValue);

            Assert.That(engine.MapSessions.GetSession(new MapId(MapIdValue)), Is.Null);
            Assert.That(session.Variables, Is.Null, "map unload must destroy the store");
        }

        [Test]
        public void LoadMap_UnknownVariableField_Rejected()
        {
            using var fixture = MapVarEngineFixture.Create(includeMount: false, variablesJson: """
                [ { "name": "probe.kills", "type": "int", "initial": 0, "priority": 1 } ]
                """);
            using GameEngine engine = fixture.CreateEngine();

            Assert.That(
                () => engine.LoadMap(MapIdValue),
                Throws.InvalidOperationException.With.Message.Contains("priority"));
        }

        // ── Graph ops end-to-end through the TriggerGraph front door ──

        [Test]
        public void TriggerGraph_ReadAddWrite_RunsFromEntryPc_AndFiresPhaseChangedWithPayload()
        {
            using var fixture = MapVarEngineFixture.Create(includeMount: true);
            using GameEngine engine = fixture.CreateEngine();

            GraphControlFlowCompileResult compiled = CompileFrontDoor(MapVarGraphJson);
            Assert.That(compiled.Succeeded, Is.True, FormatDiagnostics(compiled));
            GraphProgramPackage package = compiled.Package!.Value;
            var resolver = new GasGraphSymbolResolver(
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipReasonRegistry(),
                new TargetDispatchPresetRegistry(),
                new EntityTemplateKeyRegistry());
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);

            int graphId = RegisterTriggerGraph(engine, package);
            engine.LoadMap(MapIdValue);

            MapVariableStore store = engine.CurrentMapSession!.Variables!;
            Assert.That(store.ReadInt("probe.kills"), Is.EqualTo(1), "entry must run read → +1 → write");
            Assert.That(store.GetRevision("probe.kills"), Is.EqualTo(1u));

            var probe = new PhaseChangedProbeTrigger();
            var combined = new List<Trigger>(engine.CurrentMapSession.Triggers) { probe };
            engine.TriggerManager.RegisterMapTriggers(new MapId(MapIdValue), combined);

            store.WriteInt("probe.kills", 2);

            Assert.That(probe.Fires, Is.EqualTo(1), "PhaseChanged must be map-scoped and dispatched through TriggerManager");
            Assert.That(probe.LastContext!.Get<string>(MapVariableStore.PayloadKeyVarName), Is.EqualTo("probe.kills"));
            Assert.That(probe.LastContext.Get<int>(MapVariableStore.PayloadKeyPhase), Is.EqualTo(2));
            Assert.That(probe.LastContext.Get<int>(MapVariableStore.PayloadKeyVarValueInt), Is.EqualTo(2));

            store.WriteInt("probe.kills", 2);
            store.WriteFloat("probe.ammo", 2.5f);
            Assert.That(probe.Fires, Is.EqualTo(1), "same-value writes and non-phase vars must not fire PhaseChanged");
        }

        [Test]
        public void TriggerGraph_OpsResolveScopeFromEntryCasterEntity()
        {
            using var fixture = MapVarEngineFixture.Create(includeMount: true);
            using GameEngine engine = fixture.CreateEngine();

            GraphControlFlowCompileResult compiled = CompileFrontDoor(MapVarGraphJson);
            Assert.That(compiled.Succeeded, Is.True, FormatDiagnostics(compiled));
            GraphProgramPackage package = compiled.Package!.Value;
            var resolver = new GasGraphSymbolResolver(
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipReasonRegistry(),
                new TargetDispatchPresetRegistry(),
                new EntityTemplateKeyRegistry());
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);
            RegisterTriggerGraph(engine, package);

            engine.LoadMap(MapIdValue);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "the mounted entry must execute without trigger errors");
        }

        /// <summary>
        /// The engine freezes GraphIdRegistry during init, so re-register the engine's own
        /// mappings and then claim this fixture's graph name (mirrors TriggerGraphMountTests).
        /// </summary>
        private static int RegisterTriggerGraph(GameEngine engine, GraphProgramPackage package)
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
                .Register(graphId, package.Program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, package.Symbols, package.TriggerGraphEntries);
            return graphId;
        }

        [Test]
        public void TriggerGraph_WaitAuthoring_NowCompilesAndRegisters()
        {
            string json = $$"""
                {
                  "kind": "TriggerGraph",
                  "entries": [
                    { "label": "on_map_loaded", "event": "MapLoaded", "start": "a1" }
                  ],
                  "nodes": [
                    { "id": "a1", "op": "ConstInt", "intValue": 1 },
                    { "id": "pause", "op": "Wait" },
                    { "id": "aHalt", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "a1", "fromPort": "next", "to": "pause" },
                    { "from": "pause", "fromPort": "next", "to": "aHalt" }
                  ],
                  "valueEdges": [
                    { "from": "a1", "fromPort": "value", "to": "aHalt", "toPort": "value" }
                  ]
                }
                """;

            GraphControlFlowCompileResult compiled = CompileFrontDoor(json);
            Assert.That(compiled.Succeeded, Is.True, FormatDiagnostics(compiled));
            Assert.That(
                compiled.Program.Any(i => i.Op == (ushort)GraphNodeOp.Yield),
                Is.True,
                "Wait must lower to Yield for TriggerGraph");

            GraphProgramPackage package = compiled.Package!.Value;
            var registry = new GraphProgramRegistry();
            Assert.That(
                () => registry.Register(
                    907,
                    package.Program,
                    GraphKind.TriggerGraph,
                    GraphInstructionSourceMap.Empty,
                    Array.Empty<string>(),
                    package.TriggerGraphEntries),
                Throws.Nothing,
                "registration policy must accept Yield in TriggerGraph programs now");
        }

        private static string FormatDiagnostics(GraphControlFlowCompileResult compiled)
            => string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

        private static GraphControlFlowCompileResult CompileFrontDoor(string json)
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            JsonObject obj = JsonNode.Parse(json)!.AsObject();
            return GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, GraphName, options);
        }

        private static string MapVarGraphJson => $$"""
            {
              "kind": "TriggerGraph",
              "entries": [
                { "label": "on_map_loaded", "event": "MapLoaded", "start": "readKills", "once": true }
              ],
              "nodes": [
                { "id": "readKills", "op": "ReadMapVarInt", "var": "probe.kills" },
                { "id": "one", "op": "ConstInt", "intValue": 1 },
                { "id": "add", "op": "AddInt" },
                { "id": "writeKills", "op": "WriteMapVarInt", "var": "probe.kills" },
                { "id": "done", "op": "HaltReturnInt" }
              ],
              "controlEdges": [
                { "from": "readKills", "fromPort": "next", "to": "one" },
                { "from": "one", "fromPort": "next", "to": "add" },
                { "from": "add", "fromPort": "next", "to": "writeKills" },
                { "from": "writeKills", "fromPort": "next", "to": "done" }
              ],
              "valueEdges": [
                { "from": "readKills", "fromPort": "value", "to": "add", "toPort": "a" },
                { "from": "one", "fromPort": "value", "to": "add", "toPort": "b" },
                { "from": "add", "fromPort": "value", "to": "writeKills", "toPort": "value" },
                { "from": "add", "fromPort": "value", "to": "done", "toPort": "value" }
              ]
            }
            """;

        private sealed class PhaseChangedProbeTrigger : Trigger
        {
            public ScriptContext? LastContext { get; private set; }
            public int Fires { get; private set; }

            public PhaseChangedProbeTrigger()
            {
                EventKey = new EventKey(MapVariableStore.PhaseChangedEventName);
                Priority = 0;
            }

            public override Task ExecuteAsync(ScriptContext context)
            {
                Fires++;
                LastContext = context;
                return Task.CompletedTask;
            }
        }

        private sealed class MapVarEngineFixture : IDisposable
        {
            private const string ModId = "MapVariableStoreFixtureMod";

            private MapVarEngineFixture(string root)
            {
                Root = root;
            }

            public string Root { get; }

            public static MapVarEngineFixture Create(bool includeMount, string? variablesJson = null)
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_MapVariableStoreTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Entities"));
                Directory.CreateDirectory(Path.Combine(root, ModId, "assets", "Maps"));

                File.WriteAllText(
                    Path.Combine(root, ModId, "mod.json"),
                    $$"""
                    {
                      "name": "{{ModId}}",
                      "version": "1.0.0",
                      "description": "Asset-only map variable store fixture",
                      "priority": 0,
                      "dependencies": {}
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(root, ModId, "assets", "game.json"),
                    """
                    {
                      "startupMapId": "map_variable_store_probe",
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
                          "Name": { "Value": "Map Variable Probe Entity" }
                        }
                      }
                    ]
                    """);
                string variables = variablesJson ?? """
                    [
                      { "name": "probe.kills", "type": "int", "initial": 0, "phase": true },
                      { "name": "probe.ammo", "type": "float", "initial": 1.5 }
                    ]
                    """;
                string mountJson = includeMount
                    ? $$""", "TriggerGraphs": [ { "graph": "{{GraphName}}", "scopeInstanceId": "{{ScopeInstanceId}}" } ]"""
                    : string.Empty;
                string mapJson = $$"""
                    {
                      "Id": "{{MapIdValue}}",
                      "Tags": [ "camera.skip_default_on_load" ],
                      "Entities": [
                        { "InstanceId": "{{ScopeInstanceId}}", "Template": "{{TemplateId}}" }
                      ],
                      "Variables": {{variables}}{{mountJson}}
                    }
                    """;
                File.WriteAllText(Path.Combine(root, ModId, "assets", "Maps", $"{MapIdValue}.json"), mapJson);
                return new MapVarEngineFixture(root);
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
