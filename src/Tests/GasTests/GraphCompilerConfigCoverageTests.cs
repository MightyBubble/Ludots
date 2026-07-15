using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class GraphCompilerConfigCoverageTests
    {
        private string? _tempRoot;

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            ConfigKeyRegistry.Clear();
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
            ConfigKeyRegistry.Clear();
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();

            if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup for temp config roots.
                }
            }

            _tempRoot = null;
        }

        [Test]
        public void GraphsJson_CompilesAndExecutesIntBlackboardConfigAndSelfAttributeOps()
        {
            using var world = World.Create();
            var caster = world.Create(new AttributeBuffer(), new DirtyFlags());
            var target = world.Create();
            int copiedAttr = AttributeRegistry.Register("tests.attr.copiedFloat");
            var api = new RecordingGraphApi(world);
            int cfgFloatKey = ConfigKeyRegistry.Register("tests.config.float");
            int cfgIntKey = ConfigKeyRegistry.Register("tests.config.int");
            int cfgEffectKey = ConfigKeyRegistry.Register("tests.config.effect");
            api.ConfigFloats[cfgFloatKey] = 6.25f;
            api.ConfigInts[cfgIntKey] = 17;
            api.ConfigInts[cfgEffectKey] = 321;

            GraphProgramRegistry programs = LoadPrograms(IntBlackboardConfigGraphJson);
            Execute(programs, "tests.graph.int-blackboard-config", world, caster, target, api);

            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.sum"))], Is.EqualTo(5));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.sumCopy"))], Is.EqualTo(5));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.configInt"))], Is.EqualTo(17));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.configEffect"))], Is.EqualTo(321));
            Assert.That(api.FloatBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.rawFloat"))], Is.EqualTo(4.5f));
            Assert.That(api.FloatBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.configFloat"))], Is.EqualTo(6.25f));
            Assert.That(api.EntityBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.ltSelected"))], Is.EqualTo(target));
            Assert.That(api.EntityBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.eqSelected"))], Is.EqualTo(target));
            Assert.That(api.EntityBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.entityCopy"))], Is.EqualTo(target));
            Assert.That(world.Get<AttributeBuffer>(caster).GetCurrent(copiedAttr), Is.EqualTo(4.5f));
        }

        [Test]
        public void GraphsJson_CompilesAndExecutesSpatialQueryFiltersHexAndTargetListGetOps()
        {
            using var world = World.Create();
            var caster = world.Create();
            var kept = world.Create();
            var filteredByLayer = world.Create();
            var filteredByRelationship = world.Create();
            var api = new RecordingGraphApi(world);
            api.QueryConeResult = new[] { caster, kept, filteredByLayer, filteredByRelationship };
            api.QueryRectangleResult = new[] { kept, filteredByLayer };
            api.QueryLineResult = new[] { kept, filteredByLayer, filteredByRelationship };
            api.QueryHexRangeResult = new[] { kept };
            api.QueryHexRingResult = new[] { kept, filteredByRelationship };
            api.QueryHexNeighborsResult = new[] { kept, filteredByLayer, filteredByRelationship };
            api.Layers[kept] = 0b0010;
            api.Layers[filteredByLayer] = 0b0100;
            api.Layers[filteredByRelationship] = 0b0010;
            api.Relationships[(caster, kept)] = RelationshipFilter.Hostile;
            api.Relationships[(caster, filteredByRelationship)] = RelationshipFilter.Friendly;

            GraphProgramRegistry programs = LoadPrograms(QueryCoverageGraphJson);
            Execute(programs, "tests.graph.query-coverage", world, caster, kept, api);

            Assert.That(api.LastConeDirectionDeg, Is.EqualTo(90));
            Assert.That(api.LastConeHalfAngleDeg, Is.EqualTo(30));
            Assert.That(api.LastConeRangeCm, Is.EqualTo(800f));
            Assert.That(api.LastRectangleHalfWidthCm, Is.EqualTo(120));
            Assert.That(api.LastRectangleHalfHeightCm, Is.EqualTo(60));
            Assert.That(api.LastRectangleRotationDeg, Is.EqualTo(15));
            Assert.That(api.LastLineDirectionDeg, Is.EqualTo(45));
            Assert.That(api.LastLineLengthCm, Is.EqualTo(500));
            Assert.That(api.LastLineHalfWidthCm, Is.EqualTo(25));
            Assert.That(api.LastHexRangeRadius, Is.EqualTo(2));
            Assert.That(api.LastHexRingRadius, Is.EqualTo(3));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.filteredCount"))], Is.EqualTo(1));
            Assert.That(api.EntityBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.firstTarget"))], Is.EqualTo(kept));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.rectCount"))], Is.EqualTo(2));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.lineCount"))], Is.EqualTo(3));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.hexRangeCount"))], Is.EqualTo(1));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.hexRingCount"))], Is.EqualTo(2));
            Assert.That(api.IntBlackboard[(caster, ConfigKeyRegistry.GetId("tests.bb.hexNeighborCount"))], Is.EqualTo(3));
        }

        [Test]
        public void AttributeDerivedGraphBinding_ConfigResolvesGraphNamesAndAggregatorExecutesDerivedGraph()
        {
            using var world = World.Create();
            int sourceAttr = AttributeRegistry.Register("tests.attr.source");
            int derivedAttr = AttributeRegistry.Register("tests.attr.derived");
            var entity = world.Create(
                new AttributeBuffer(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags());
            ref AttributeBuffer buffer = ref world.Get<AttributeBuffer>(entity);
            buffer.SetBase(sourceAttr, 10f);
            buffer.SetCurrent(sourceAttr, 10f);

            GraphProgramRegistry programs = LoadPrograms(DerivedAttributeGraphJson);
            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "AttributeDerivedGraphBinding",
                JsonNode.Parse("""{ "graphs": [ "tests.graph.derived-attribute" ] }""")!);

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            using var system = new AttributeAggregatorSystem(
                world,
                programs,
                new GasGraphRuntimeApi(world, tagOps: tagOps),
                tagOps);
            system.Update(0f);

            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(derivedAttr), Is.EqualTo(12.5f));
        }

        [Test]
        public void AttributeDerivedGraphBinding_ConfigRejectsNumericGraphIds()
        {
            using var world = World.Create();
            var entity = world.Create();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    "AttributeDerivedGraphBinding",
                    JsonNode.Parse("""{ "graphProgramIds": [ 1 ] }""")!))!;

            Assert.That(ex.Message, Does.Contain("numeric graph ids are internal only"));
        }

        [Test]
        public void SpatialQueryCompiler_RejectsMissingCapacityPolicy()
        {
            GraphConfig graph = CreateRadiusGraph(queryCapacityPolicy: null, droppedOutput: null);

            var (package, diagnostics) = GraphCompiler.Compile(graph);

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Message.Contains("queryCapacityPolicy", StringComparison.Ordinal)));
        }

        [Test]
        public void SpatialQueryCompiler_RejectsAllowTruncatedWithoutDroppedOutput()
        {
            GraphConfig graph = CreateRadiusGraph("AllowTruncated", droppedOutput: null);

            var (package, diagnostics) = GraphCompiler.Compile(graph);

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Message.Contains("droppedOutput", StringComparison.Ordinal)));
        }

        [Test]
        public void SpatialQuery_RequireComplete_ThrowsWhenRuntimeDropsTargets()
        {
            using var world = World.Create();
            var api = new RecordingGraphApi(world)
            {
                QueryRadiusResponse = new SpatialQueryResult(count: 0, dropped: 3)
            };
            var (package, diagnostics) = GraphCompiler.Compile(CreateRadiusGraph("RequireComplete", droppedOutput: null));
            Assert.That(package.HasValue, Is.True, string.Join(Environment.NewLine, diagnostics));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.Execute(
                    world,
                    Entity.Null,
                    Entity.Null,
                    default,
                    package!.Value.Program,
                    api))!;

            Assert.That(ex.Message, Does.Contain("GAS.GRAPH.ERR.SpatialQueryIncomplete"));
        }

        [Test]
        public void SpatialQuery_AllowTruncated_PublishesDroppedCount()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            var api = new RecordingGraphApi(world)
            {
                QueryRadiusResponse = new SpatialQueryResult(count: 0, dropped: 7)
            };
            GraphConfig graph = CreateRadiusGraph("AllowTruncated", "dropped");
            graph.Nodes[0].Next = "writeDropped";
            graph.Nodes.Insert(0, new GraphNodeConfig { Id = "self", Op = "LoadCaster", Next = "query" });
            graph.Entry = "self";
            graph.Nodes.Add(new GraphNodeConfig
            {
                Id = "writeDropped",
                Op = "WriteBlackboardInt",
                BlackboardKey = "tests.bb.dropped",
                Inputs = new List<string> { "self", "dropped" }
            });
            var (package, diagnostics) = GraphCompiler.Compile(graph);
            Assert.That(package.HasValue, Is.True, string.Join(Environment.NewLine, diagnostics));

            Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.Execute(
                world,
                caster,
                Entity.Null,
                default,
                package!.Value.Program,
                api);

            Assert.That(api.IntBlackboard[(caster, 0)], Is.EqualTo(7));
        }

        [Test]
        public void SnapWithoutValidOutput_DoesNotOverwriteValidationResult()
        {
            using var world = World.Create();
            var api = new RecordingGraphApi(world);
            GraphConfig graph = CreateSnapGraph(validOutput: null, includeOccupiedBool: false);
            var (package, diagnostics) = GraphCompiler.Compile(graph);
            Assert.That(package.HasValue, Is.True, string.Join(Environment.NewLine, diagnostics));

            bool valid = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteValidation(
                world,
                Entity.Null,
                Entity.Null,
                default,
                package!.Value.Program,
                api);

            Assert.That(valid, Is.True);
            GraphInstruction snap = Array.Find(package.Value.Program, ins => ins.Op == (ushort)GraphNodeOp.SnapToNearestInCollection);
            Assert.That(snap.Flags, Is.EqualTo(byte.MaxValue));
        }

        [Test]
        public void SnapWithValidOutput_AllocatesDedicatedBoolRegister()
        {
            GraphConfig graph = CreateSnapGraph(validOutput: "snapValid", includeOccupiedBool: false);

            var (package, diagnostics) = GraphCompiler.Compile(graph);

            Assert.That(package.HasValue, Is.True, string.Join(Environment.NewLine, diagnostics));
            GraphInstruction snap = Array.Find(package!.Value.Program, ins => ins.Op == (ushort)GraphNodeOp.SnapToNearestInCollection);
            Assert.That(snap.Flags, Is.Not.EqualTo(byte.MaxValue));
            Assert.That(snap.Flags, Is.Not.EqualTo(0), "B[0] is reserved for the validation result contract.");
        }

        private static GraphConfig CreateRadiusGraph(string? queryCapacityPolicy, string? droppedOutput)
        {
            return new GraphConfig
            {
                Id = "tests.graph.radius-capacity",
                Entry = "query",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig
                    {
                        Id = "query",
                        Op = "QueryRadius",
                        RadiusCm = 100f,
                        QueryCapacityPolicy = queryCapacityPolicy,
                        DroppedOutput = droppedOutput
                    }
                }
            };
        }

        private static GraphConfig CreateSnapGraph(string? validOutput, bool includeOccupiedBool)
        {
            var nodes = new List<GraphNodeConfig>
            {
                new GraphNodeConfig { Id = "self", Op = "LoadCaster", Next = "distance" },
                new GraphNodeConfig
                {
                    Id = "distance",
                    Op = "ConstFloat",
                    FloatValue = 100f,
                    Next = includeOccupiedBool ? "occupied" : "snap"
                }
            };
            if (includeOccupiedBool)
            {
                nodes.Add(new GraphNodeConfig { Id = "occupied", Op = "ConstBool", BoolValue = true, Next = "snap" });
            }
            nodes.Add(new GraphNodeConfig
            {
                Id = "snap",
                Op = "SnapToNearestInCollection",
                CollectionKey = "tests.collection.snap",
                Inputs = new List<string> { "self", "distance" },
                ValidOutput = validOutput
            });
            return new GraphConfig
            {
                Id = "tests.graph.snap-valid",
                Entry = "self",
                Nodes = nodes
            };
        }

        private GraphProgramRegistry LoadPrograms(string graphJson)
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphCompilerConfigCoverageTests", Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(_tempRoot, "Core");
            string graphDir = Path.Combine(coreRoot, "Configs", "GAS");
            Directory.CreateDirectory(graphDir);
            File.WriteAllText(Path.Combine(graphDir, "graphs.json"), graphJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

            var programs = new GraphProgramRegistry();
            var loader = new GraphProgramConfigLoader(pipeline, programs, new TestGraphSymbolResolver());
            var packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
            loader.PatchAndRegister(packages);
            return programs;
        }

        private static void Execute(
            GraphProgramRegistry programs,
            string graphName,
            World world,
            Entity caster,
            Entity explicitTarget,
            RecordingGraphApi api)
        {
            int graphId = GraphIdRegistry.GetId(graphName);
            Assert.That(graphId, Is.GreaterThan(0));
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
            Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.Execute(world, caster, explicitTarget, default, program, api);
        }

        private const string IntBlackboardConfigGraphJson = """
[
  {
    "id": "tests.graph.int-blackboard-config",
    "entry": "self",
    "nodes": [
      { "id": "self", "op": "LoadCaster", "next": "target" },
      { "id": "target", "op": "LoadExplicitTarget", "next": "two" },
      { "id": "two", "op": "ConstInt", "intValue": 2, "next": "three" },
      { "id": "three", "op": "ConstInt", "intValue": 3, "next": "sum" },
      { "id": "sum", "op": "AddInt", "inputs": [ "two", "three" ], "next": "writeSum" },
      { "id": "writeSum", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.sum", "inputs": [ "self", "sum" ], "next": "lt" },
      { "id": "lt", "op": "CompareLtInt", "inputs": [ "two", "three" ], "next": "ltSelected" },
      { "id": "ltSelected", "op": "SelectEntity", "inputs": [ "lt", "target", "self" ], "next": "writeLtSelected" },
      { "id": "writeLtSelected", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.ltSelected", "inputs": [ "self", "ltSelected" ], "next": "eq" },
      { "id": "eq", "op": "CompareEqInt", "inputs": [ "two", "two" ], "next": "eqSelected" },
      { "id": "eqSelected", "op": "SelectEntity", "inputs": [ "eq", "target", "self" ], "next": "writeEqSelected" },
      { "id": "writeEqSelected", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.eqSelected", "inputs": [ "self", "eqSelected" ], "next": "rawFloat" },
      { "id": "rawFloat", "op": "ConstFloat", "floatValue": 4.5, "next": "writeRawFloat" },
      { "id": "writeRawFloat", "op": "WriteBlackboardFloat", "blackboardKey": "tests.bb.rawFloat", "inputs": [ "self", "rawFloat" ], "next": "readRawFloat" },
      { "id": "readRawFloat", "op": "ReadBlackboardFloat", "blackboardKey": "tests.bb.rawFloat", "inputs": [ "self" ], "next": "writeSelfAttr" },
      { "id": "writeSelfAttr", "op": "WriteSelfAttribute", "attribute": "tests.attr.copiedFloat", "inputs": [ "readRawFloat" ], "next": "readSum" },
      { "id": "readSum", "op": "ReadBlackboardInt", "blackboardKey": "tests.bb.sum", "inputs": [ "self" ], "next": "writeSumCopy" },
      { "id": "writeSumCopy", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.sumCopy", "inputs": [ "self", "readSum" ], "next": "readEntity" },
      { "id": "readEntity", "op": "ReadBlackboardEntity", "blackboardKey": "tests.bb.eqSelected", "inputs": [ "self" ], "next": "writeEntityCopy" },
      { "id": "writeEntityCopy", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.entityCopy", "inputs": [ "self", "readEntity" ], "next": "loadCfgFloat" },
      { "id": "loadCfgFloat", "op": "LoadConfigFloat", "configKey": "tests.config.float", "next": "writeCfgFloat" },
      { "id": "writeCfgFloat", "op": "WriteBlackboardFloat", "blackboardKey": "tests.bb.configFloat", "inputs": [ "self", "loadCfgFloat" ], "next": "loadCfgInt" },
      { "id": "loadCfgInt", "op": "LoadConfigInt", "configKey": "tests.config.int", "next": "writeCfgInt" },
      { "id": "writeCfgInt", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.configInt", "inputs": [ "self", "loadCfgInt" ], "next": "loadCfgEffect" },
      { "id": "loadCfgEffect", "op": "LoadConfigEffectId", "configKey": "tests.config.effect", "next": "writeCfgEffect" },
      { "id": "writeCfgEffect", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.configEffect", "inputs": [ "self", "loadCfgEffect" ] }
    ]
  }
]
""";

        private const string QueryCoverageGraphJson = """
[
  {
    "id": "tests.graph.query-coverage",
    "entry": "self",
    "nodes": [
      { "id": "self", "op": "LoadCaster", "next": "cone" },
      { "id": "cone", "op": "QueryCone", "queryCapacityPolicy": "RequireComplete", "directionDeg": 90, "halfAngleDeg": 30, "rangeCm": 800, "next": "layer" },
      { "id": "layer", "op": "QueryFilterLayer", "layerMask": 2, "next": "notSelf" },
      { "id": "notSelf", "op": "QueryFilterNotEntity", "inputs": [ "self" ], "next": "hostile" },
      { "id": "hostile", "op": "QueryFilterRelationship", "relationshipMode": "Hostile", "inputs": [ "self" ], "next": "zero" },
      { "id": "zero", "op": "ConstInt", "intValue": 0, "next": "first" },
      { "id": "first", "op": "TargetListGet", "inputs": [ "zero" ], "next": "filteredCount" },
      { "id": "filteredCount", "op": "AggCount", "next": "writeFirst" },
      { "id": "writeFirst", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.firstTarget", "inputs": [ "self", "first" ], "next": "writeFilteredCount" },
      { "id": "writeFilteredCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.filteredCount", "inputs": [ "self", "filteredCount" ], "next": "rect" },
      { "id": "rect", "op": "QueryRectangle", "queryCapacityPolicy": "RequireComplete", "halfWidthCm": 120, "halfHeightCm": 60, "rotationDeg": 15, "next": "rectCount" },
      { "id": "rectCount", "op": "AggCount", "next": "writeRectCount" },
      { "id": "writeRectCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.rectCount", "inputs": [ "self", "rectCount" ], "next": "line" },
      { "id": "line", "op": "QueryLine", "queryCapacityPolicy": "RequireComplete", "directionDeg": 45, "lengthCm": 500, "halfWidthCm": 25, "next": "lineCount" },
      { "id": "lineCount", "op": "AggCount", "next": "writeLineCount" },
      { "id": "writeLineCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.lineCount", "inputs": [ "self", "lineCount" ], "next": "hexRange" },
      { "id": "hexRange", "op": "QueryHexRange", "queryCapacityPolicy": "RequireComplete", "hexRadius": 2, "next": "hexRangeCount" },
      { "id": "hexRangeCount", "op": "AggCount", "next": "writeHexRangeCount" },
      { "id": "writeHexRangeCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.hexRangeCount", "inputs": [ "self", "hexRangeCount" ], "next": "hexRing" },
      { "id": "hexRing", "op": "QueryHexRing", "queryCapacityPolicy": "RequireComplete", "hexRadius": 3, "next": "hexRingCount" },
      { "id": "hexRingCount", "op": "AggCount", "next": "writeHexRingCount" },
      { "id": "writeHexRingCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.hexRingCount", "inputs": [ "self", "hexRingCount" ], "next": "hexNeighbors" },
      { "id": "hexNeighbors", "op": "QueryHexNeighbors", "queryCapacityPolicy": "RequireComplete", "next": "hexNeighborCount" },
      { "id": "hexNeighborCount", "op": "AggCount", "next": "writeHexNeighborCount" },
      { "id": "writeHexNeighborCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.hexNeighborCount", "inputs": [ "self", "hexNeighborCount" ] }
    ]
  }
]
""";

        private const string DerivedAttributeGraphJson = """
[
  {
    "id": "tests.graph.derived-attribute",
    "entry": "source",
    "nodes": [
      { "id": "source", "op": "LoadSelfAttribute", "attribute": "tests.attr.source", "next": "bonus" },
      { "id": "bonus", "op": "ConstFloat", "floatValue": 2.5, "next": "sum" },
      { "id": "sum", "op": "AddFloat", "inputs": [ "source", "bonus" ], "next": "writeDerived" },
      { "id": "writeDerived", "op": "WriteSelfAttribute", "attribute": "tests.attr.derived", "inputs": [ "sum" ] }
    ]
  }
]
""";

        private sealed class TestGraphSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => TagRegistry.Register(name);
            public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
            public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
            public int ResolveRelationshipType(string name) => ConfigKeyRegistry.Register($"relationship.type.{name}");
            public int ResolveRelationshipMetric(string name) => ConfigKeyRegistry.Register($"relationship.metric.{name}");
            public int ResolveRelationshipFlag(string name) => ConfigKeyRegistry.Register($"relationship.flag.{name}");
            public int ResolveRelationshipReason(string name) => ConfigKeyRegistry.Register($"relationship.reason.{name}");
            public int ResolveTargetDispatchPreset(string name) => ConfigKeyRegistry.Register($"targetDispatch.{name}");
            public int ResolveEntityTemplate(string name) => ConfigKeyRegistry.Register($"entityTemplate.{name}");
        }

        private sealed class RecordingGraphApi : IGraphRuntimeApi
        {
            private readonly World _world;

            public RecordingGraphApi(World world)
            {
                _world = world;
            }

            public Dictionary<(Entity Entity, int Key), float> FloatBlackboard { get; } = new();
            public Dictionary<(Entity Entity, int Key), int> IntBlackboard { get; } = new();
            public Dictionary<(Entity Entity, int Key), Entity> EntityBlackboard { get; } = new();
            public Dictionary<int, float> ConfigFloats { get; } = new();
            public Dictionary<int, int> ConfigInts { get; } = new();
            public Dictionary<Entity, uint> Layers { get; } = new();
            public Dictionary<(Entity Reference, Entity Target), RelationshipFilter> Relationships { get; } = new();
            public Entity[] QueryConeResult { get; set; } = Array.Empty<Entity>();
            public Entity[] QueryRectangleResult { get; set; } = Array.Empty<Entity>();
            public Entity[] QueryLineResult { get; set; } = Array.Empty<Entity>();
            public Entity[] QueryHexRangeResult { get; set; } = Array.Empty<Entity>();
            public Entity[] QueryHexRingResult { get; set; } = Array.Empty<Entity>();
            public Entity[] QueryHexNeighborsResult { get; set; } = Array.Empty<Entity>();
            public int LastConeDirectionDeg { get; private set; }
            public int LastConeHalfAngleDeg { get; private set; }
            public float LastConeRangeCm { get; private set; }
            public int LastRectangleHalfWidthCm { get; private set; }
            public int LastRectangleHalfHeightCm { get; private set; }
            public int LastRectangleRotationDeg { get; private set; }
            public int LastLineDirectionDeg { get; private set; }
            public int LastLineLengthCm { get; private set; }
            public int LastLineHalfWidthCm { get; private set; }
            public int LastHexRangeRadius { get; private set; }
            public int LastHexRingRadius { get; private set; }

            public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
            {
                gridPos = default;
                return false;
            }

            public bool HasTag(Entity entity, int tagId) => false;

            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
            {
                if (_world.IsAlive(entity) && _world.Has<AttributeBuffer>(entity))
                {
                    value = _world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
                    return true;
                }

                value = 0f;
                return false;
            }

            public SpatialQueryResult QueryRadiusResponse { get; set; }

            public SpatialQueryResult QueryRadius(IntVector2 center, float radius, Span<Entity> buffer) => QueryRadiusResponse;

            public SpatialQueryResult QueryCone(IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer)
            {
                LastConeDirectionDeg = directionDeg;
                LastConeHalfAngleDeg = halfAngleDeg;
                LastConeRangeCm = rangeCm;
                int count = Copy(QueryConeResult, buffer);
                return new SpatialQueryResult(count, 0);
            }

            public SpatialQueryResult QueryRectangle(IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
            {
                LastRectangleHalfWidthCm = halfWidthCm;
                LastRectangleHalfHeightCm = halfHeightCm;
                LastRectangleRotationDeg = rotationDeg;
                int count = Copy(QueryRectangleResult, buffer);
                return new SpatialQueryResult(count, 0);
            }

            public SpatialQueryResult QueryLine(IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
            {
                LastLineDirectionDeg = directionDeg;
                LastLineLengthCm = lengthCm;
                LastLineHalfWidthCm = halfWidthCm;
                int count = Copy(QueryLineResult, buffer);
                return new SpatialQueryResult(count, 0);
            }

            public SpatialQueryResult QueryHexRange(IntVector2 center, int hexRadius, Span<Entity> buffer)
            {
                LastHexRangeRadius = hexRadius;
                int count = Copy(QueryHexRangeResult, buffer);
                return new SpatialQueryResult(count, 0);
            }

            public SpatialQueryResult QueryHexRing(IntVector2 center, int hexRadius, Span<Entity> buffer)
            {
                LastHexRingRadius = hexRadius;
                int count = Copy(QueryHexRingResult, buffer);
                return new SpatialQueryResult(count, 0);
            }

            public SpatialQueryResult QueryHexNeighbors(IntVector2 center, Span<Entity> buffer)
            {
                int count = Copy(QueryHexNeighborsResult, buffer);
                return new SpatialQueryResult(count, 0);
            }
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => Layers.TryGetValue(entity, out uint layer) ? layer : 0u;
            public int GetRelationship(int teamA, int teamB) => GraphRelationship.Neutral;

            public int FilterLayer(Span<Entity> entities, int count, uint requiredMask)
            {
                int write = 0;
                for (int i = 0; i < count; i++)
                {
                    if ((GetEntityLayerCategory(entities[i]) & requiredMask) != 0u)
                    {
                        entities[write++] = entities[i];
                    }
                }

                return write;
            }

            public int FilterNotEntity(Span<Entity> entities, int count, Entity exclude)
            {
                int write = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!entities[i].Equals(exclude))
                    {
                        entities[write++] = entities[i];
                    }
                }

                return write;
            }

            public int FilterTeamRelationship(Span<Entity> entities, int count, Entity reference, RelationshipFilter filter)
            {
                int write = 0;
                for (int i = 0; i < count; i++)
                {
                    Relationships.TryGetValue((reference, entities[i]), out RelationshipFilter actual);
                    if (Matches(filter, actual))
                    {
                        entities[write++] = entities[i];
                    }
                }

                return write;
            }

            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void RemoveEffectTemplate(Entity target, int templateId) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
            public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value)
            {
                _world.Get<AttributeBuffer>(target).SetCurrent(attributeId, value);
            }
            public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value) => FloatBlackboard.TryGetValue((entity, keyId), out value);
            public bool TryReadBlackboardInt(Entity entity, int keyId, out int value) => IntBlackboard.TryGetValue((entity, keyId), out value);
            public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value) => EntityBlackboard.TryGetValue((entity, keyId), out value);
            public void WriteBlackboardFloat(Entity entity, int keyId, float value) => FloatBlackboard[(entity, keyId)] = value;
            public void WriteBlackboardInt(Entity entity, int keyId, int value) => IntBlackboard[(entity, keyId)] = value;
            public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) => EntityBlackboard[(entity, keyId)] = value;
            public bool TryLoadConfigFloat(int keyId, out float value) => ConfigFloats.TryGetValue(keyId, out value);
            public bool TryLoadConfigInt(int keyId, out int value) => ConfigInts.TryGetValue(keyId, out value);

            private static bool Matches(RelationshipFilter filter, RelationshipFilter actual)
            {
                return filter switch
                {
                    RelationshipFilter.Hostile => actual == RelationshipFilter.Hostile,
                    RelationshipFilter.Friendly => actual == RelationshipFilter.Friendly,
                    RelationshipFilter.Neutral => actual == RelationshipFilter.Neutral,
                    RelationshipFilter.NotFriendly => actual != RelationshipFilter.Friendly,
                    RelationshipFilter.NotHostile => actual != RelationshipFilter.Hostile,
                    _ => false,
                };
            }

            private static int Copy(Entity[] source, Span<Entity> target)
            {
                int count = Math.Min(source.Length, target.Length);
                for (int i = 0; i < count; i++)
                {
                    target[i] = source[i];
                }

                return count;
            }
        }
    }
}
