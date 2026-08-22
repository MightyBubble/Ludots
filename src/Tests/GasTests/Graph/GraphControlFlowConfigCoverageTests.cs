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
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class GraphControlFlowConfigCoverageTests
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

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
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
            var (package, _, diagnostics) = CompileFrontDoor("""
{
  "id": "tests.graph.radius-capacity",
  "kind": "Query",
  "entry": "query",
  "nodes": [
    { "id": "query", "op": "QueryRadius", "radiusCm": 100 }
  ],
  "controlEdges": [],
  "valueEdges": []
}
""");

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Message.Contains("queryCapacityPolicy", StringComparison.Ordinal)));
        }

        [Test]
        public void SpatialQueryCompiler_RejectsAllowTruncatedWithoutDroppedOutput()
        {
            var (package, _, diagnostics) = CompileFrontDoor("""
{
  "id": "tests.graph.radius-capacity",
  "kind": "Query",
  "entry": "query",
  "nodes": [
    { "id": "query", "op": "QueryRadius", "radiusCm": 100, "queryCapacityPolicy": "AllowTruncated" }
  ],
  "controlEdges": [],
  "valueEdges": []
}
""");

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
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, Flags = 0, ImmF = 100f },
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.Execute(
                    world,
                    Entity.Null,
                    Entity.Null,
                    default,
                    program,
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
            int droppedKey = ConfigKeyRegistry.Register("tests.bb.dropped");
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, Flags = 1, Dst = 0, ImmF = 100f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardInt, A = 0, B = 0, Imm = droppedKey },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.Execute(
                world,
                caster,
                Entity.Null,
                default,
                program,
                api);

            Assert.That(api.IntBlackboard[(caster, droppedKey)], Is.EqualTo(7));
        }

        [Test]
        public void RelationshipQueryCompiler_RejectsAllowTruncatedWithoutDroppedOutput()
        {
            var (package, _, diagnostics) = CompileFrontDoor("""
{
  "id": "tests.graph.rel-capacity",
  "kind": "Query",
  "entry": "query",
  "nodes": [
    { "id": "self", "op": "LoadCaster" },
    { "id": "query", "op": "RelationshipQueryOutgoing", "relationshipType": "SocialBond", "queryCapacityPolicy": "AllowTruncated" }
  ],
  "controlEdges": [
    { "from": "self", "fromPort": "next", "to": "query" }
  ],
  "valueEdges": [
    { "from": "self", "fromPort": "value", "to": "query", "toPort": "source" }
  ]
}
""");

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Message.Contains("droppedOutput", StringComparison.Ordinal)));
        }

        [Test]
        public void SnapWithoutValidOutput_DoesNotOverwriteValidationResult()
        {
            using var world = World.Create();
            var api = new RecordingGraphApi(world);
            var (package, _, diagnostics) = CompileFrontDoor("""
{
  "id": "tests.graph.snap-valid",
  "kind": "Validation",
  "entry": "self",
  "nodes": [
    { "id": "self", "op": "LoadCaster" },
    { "id": "distance", "op": "ConstFloat", "floatValue": 100 },
    { "id": "snap", "op": "SnapToNearestInCollection", "collectionKey": "tests.collection.snap" }
  ],
  "controlEdges": [
    { "from": "self", "fromPort": "next", "to": "distance" },
    { "from": "distance", "fromPort": "next", "to": "snap" }
  ],
  "valueEdges": [
    { "from": "self", "fromPort": "value", "to": "snap", "toPort": "source" },
    { "from": "distance", "fromPort": "value", "to": "snap", "toPort": "value" }
  ]
}
""");

            Assert.That(package.HasValue, Is.True, string.Join(Environment.NewLine, diagnostics));
            GraphInstruction snap = Array.Find(package!.Value.Program, ins => ins.Op == (ushort)GraphNodeOp.SnapToNearestInCollection);
            Assert.That(snap.Flags, Is.EqualTo(byte.MaxValue));

            bool valid = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteValidation(
                world,
                Entity.Null,
                Entity.Null,
                default,
                package.Value.Program,
                api);

            Assert.That(valid, Is.False, "Validation graphs fail closed when B[0] is never written.");
        }

        [Test]
        public void SnapWithValidOutput_AllocatesDedicatedBoolRegister()
        {
            var (package, _, diagnostics) = CompileFrontDoor("""
{
  "id": "tests.graph.snap-valid",
  "kind": "Validation",
  "entry": "self",
  "nodes": [
    { "id": "self", "op": "LoadCaster" },
    { "id": "distance", "op": "ConstFloat", "floatValue": 100 },
    { "id": "snap", "op": "SnapToNearestInCollection", "collectionKey": "tests.collection.snap", "validOutput": "snapValid" }
  ],
  "controlEdges": [
    { "from": "self", "fromPort": "next", "to": "distance" },
    { "from": "distance", "fromPort": "next", "to": "snap" }
  ],
  "valueEdges": [
    { "from": "self", "fromPort": "value", "to": "snap", "toPort": "source" },
    { "from": "distance", "fromPort": "value", "to": "snap", "toPort": "value" }
  ]
}
""");

            Assert.That(package.HasValue, Is.True, string.Join(Environment.NewLine, diagnostics));
            GraphInstruction snap = Array.Find(package!.Value.Program, ins => ins.Op == (ushort)GraphNodeOp.SnapToNearestInCollection);
            Assert.That(snap.Flags, Is.Not.EqualTo(byte.MaxValue));
            Assert.That(snap.Flags, Is.Not.EqualTo(0), "B[0] is reserved for the validation result contract.");
        }

        private static (GraphProgramPackage? Package, GraphOutputSchema OutputSchema, List<GraphDiagnostic> Diagnostics) CompileFrontDoor(string graphJson)
        {
            JsonObject graph = JsonNode.Parse(graphJson)!.AsObject();
            string graphId = graph["id"]!.GetValue<string>();
            return GraphProgramAuthoringFrontDoor.CompileJsonObject(
                graph,
                graphId,
                StrictJsonOptions.CreateCamelCase(includeFields: true));
        }

        private GraphProgramRegistry LoadPrograms(string graphJson)
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphControlFlowConfigCoverageTests", Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(_tempRoot, "Core");
            string graphDir = Path.Combine(coreRoot, "GAS");
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
    "kind": "Effect",
    "entry": "self",
    "nodes": [
      { "id": "self", "op": "LoadCaster" },
      { "id": "target", "op": "LoadExplicitTarget" },
      { "id": "two", "op": "ConstInt", "intValue": 2 },
      { "id": "three", "op": "ConstInt", "intValue": 3 },
      { "id": "sum", "op": "AddInt" },
      { "id": "writeSum", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.sum" },
      { "id": "lt", "op": "CompareLtInt" },
      { "id": "ltSelected", "op": "SelectEntity" },
      { "id": "writeLtSelected", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.ltSelected" },
      { "id": "eq", "op": "CompareEqInt" },
      { "id": "eqSelected", "op": "SelectEntity" },
      { "id": "writeEqSelected", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.eqSelected" },
      { "id": "rawFloat", "op": "ConstFloat", "floatValue": 4.5 },
      { "id": "writeRawFloat", "op": "WriteBlackboardFloat", "blackboardKey": "tests.bb.rawFloat" },
      { "id": "readRawFloat", "op": "ReadBlackboardFloat", "blackboardKey": "tests.bb.rawFloat" },
      { "id": "writeSelfAttr", "op": "WriteSelfAttribute", "attribute": "tests.attr.copiedFloat" },
      { "id": "readSum", "op": "ReadBlackboardInt", "blackboardKey": "tests.bb.sum" },
      { "id": "writeSumCopy", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.sumCopy" },
      { "id": "readEntity", "op": "ReadBlackboardEntity", "blackboardKey": "tests.bb.eqSelected" },
      { "id": "writeEntityCopy", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.entityCopy" },
      { "id": "loadCfgFloat", "op": "LoadConfigFloat", "configKey": "tests.config.float" },
      { "id": "writeCfgFloat", "op": "WriteBlackboardFloat", "blackboardKey": "tests.bb.configFloat" },
      { "id": "loadCfgInt", "op": "LoadConfigInt", "configKey": "tests.config.int" },
      { "id": "writeCfgInt", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.configInt" },
      { "id": "loadCfgEffect", "op": "LoadConfigEffectId", "configKey": "tests.config.effect" },
      { "id": "writeCfgEffect", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.configEffect" }
    ],
    "controlEdges": [
      { "from": "self", "fromPort": "next", "to": "target" },
      { "from": "target", "fromPort": "next", "to": "two" },
      { "from": "two", "fromPort": "next", "to": "three" },
      { "from": "three", "fromPort": "next", "to": "sum" },
      { "from": "sum", "fromPort": "next", "to": "writeSum" },
      { "from": "writeSum", "fromPort": "next", "to": "lt" },
      { "from": "lt", "fromPort": "next", "to": "ltSelected" },
      { "from": "ltSelected", "fromPort": "next", "to": "writeLtSelected" },
      { "from": "writeLtSelected", "fromPort": "next", "to": "eq" },
      { "from": "eq", "fromPort": "next", "to": "eqSelected" },
      { "from": "eqSelected", "fromPort": "next", "to": "writeEqSelected" },
      { "from": "writeEqSelected", "fromPort": "next", "to": "rawFloat" },
      { "from": "rawFloat", "fromPort": "next", "to": "writeRawFloat" },
      { "from": "writeRawFloat", "fromPort": "next", "to": "readRawFloat" },
      { "from": "readRawFloat", "fromPort": "next", "to": "writeSelfAttr" },
      { "from": "writeSelfAttr", "fromPort": "next", "to": "readSum" },
      { "from": "readSum", "fromPort": "next", "to": "writeSumCopy" },
      { "from": "writeSumCopy", "fromPort": "next", "to": "readEntity" },
      { "from": "readEntity", "fromPort": "next", "to": "writeEntityCopy" },
      { "from": "writeEntityCopy", "fromPort": "next", "to": "loadCfgFloat" },
      { "from": "loadCfgFloat", "fromPort": "next", "to": "writeCfgFloat" },
      { "from": "writeCfgFloat", "fromPort": "next", "to": "loadCfgInt" },
      { "from": "loadCfgInt", "fromPort": "next", "to": "writeCfgInt" },
      { "from": "writeCfgInt", "fromPort": "next", "to": "loadCfgEffect" },
      { "from": "loadCfgEffect", "fromPort": "next", "to": "writeCfgEffect" }
    ],
    "valueEdges": [
      { "from": "two", "fromPort": "value", "to": "sum", "toPort": "a" },
      { "from": "three", "fromPort": "value", "to": "sum", "toPort": "b" },
      { "from": "self", "fromPort": "value", "to": "writeSum", "toPort": "source" },
      { "from": "sum", "fromPort": "value", "to": "writeSum", "toPort": "value" },
      { "from": "two", "fromPort": "value", "to": "lt", "toPort": "a" },
      { "from": "three", "fromPort": "value", "to": "lt", "toPort": "b" },
      { "from": "lt", "fromPort": "value", "to": "ltSelected", "toPort": "condition" },
      { "from": "target", "fromPort": "value", "to": "ltSelected", "toPort": "a" },
      { "from": "self", "fromPort": "value", "to": "ltSelected", "toPort": "b" },
      { "from": "self", "fromPort": "value", "to": "writeLtSelected", "toPort": "source" },
      { "from": "ltSelected", "fromPort": "value", "to": "writeLtSelected", "toPort": "value" },
      { "from": "two", "fromPort": "value", "to": "eq", "toPort": "a" },
      { "from": "two", "fromPort": "value", "to": "eq", "toPort": "b" },
      { "from": "eq", "fromPort": "value", "to": "eqSelected", "toPort": "condition" },
      { "from": "target", "fromPort": "value", "to": "eqSelected", "toPort": "a" },
      { "from": "self", "fromPort": "value", "to": "eqSelected", "toPort": "b" },
      { "from": "self", "fromPort": "value", "to": "writeEqSelected", "toPort": "source" },
      { "from": "eqSelected", "fromPort": "value", "to": "writeEqSelected", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeRawFloat", "toPort": "source" },
      { "from": "rawFloat", "fromPort": "value", "to": "writeRawFloat", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "readRawFloat", "toPort": "source" },
      { "from": "readRawFloat", "fromPort": "value", "to": "writeSelfAttr", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "readSum", "toPort": "source" },
      { "from": "self", "fromPort": "value", "to": "writeSumCopy", "toPort": "source" },
      { "from": "readSum", "fromPort": "value", "to": "writeSumCopy", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "readEntity", "toPort": "source" },
      { "from": "self", "fromPort": "value", "to": "writeEntityCopy", "toPort": "source" },
      { "from": "readEntity", "fromPort": "value", "to": "writeEntityCopy", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeCfgFloat", "toPort": "source" },
      { "from": "loadCfgFloat", "fromPort": "value", "to": "writeCfgFloat", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeCfgInt", "toPort": "source" },
      { "from": "loadCfgInt", "fromPort": "value", "to": "writeCfgInt", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeCfgEffect", "toPort": "source" },
      { "from": "loadCfgEffect", "fromPort": "value", "to": "writeCfgEffect", "toPort": "value" }
    ]
  }
]
""";

        private const string QueryCoverageGraphJson = """
[
  {
    "id": "tests.graph.query-coverage",
    "kind": "Effect",
    "entry": "self",
    "nodes": [
      { "id": "self", "op": "LoadCaster" },
      { "id": "coneDir", "op": "ConstInt", "intValue": 90 },
      { "id": "coneHalf", "op": "ConstInt", "intValue": 30 },
      { "id": "cone", "op": "QueryCone", "queryCapacityPolicy": "RequireComplete", "rangeCm": 800 },
      { "id": "layer", "op": "QueryFilterLayer", "layerMask": 2 },
      { "id": "notSelf", "op": "QueryFilterNotEntity" },
      { "id": "hostile", "op": "QueryFilterRelationship", "relationshipMode": "Hostile" },
      { "id": "zero", "op": "ConstInt", "intValue": 0 },
      { "id": "first", "op": "TargetListGet" },
      { "id": "filteredCount", "op": "AggCount" },
      { "id": "writeFirst", "op": "WriteBlackboardEntity", "blackboardKey": "tests.bb.firstTarget" },
      { "id": "writeFilteredCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.filteredCount" },
      { "id": "rectW", "op": "ConstInt", "intValue": 120 },
      { "id": "rectH", "op": "ConstInt", "intValue": 60 },
      { "id": "rect", "op": "QueryRectangle", "queryCapacityPolicy": "RequireComplete", "rotationDeg": 15 },
      { "id": "rectCount", "op": "AggCount" },
      { "id": "writeRectCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.rectCount" },
      { "id": "lineDir", "op": "ConstInt", "intValue": 45 },
      { "id": "lineLen", "op": "ConstInt", "intValue": 500 },
      { "id": "line", "op": "QueryLine", "queryCapacityPolicy": "RequireComplete", "halfWidthCm": 25 },
      { "id": "lineCount", "op": "AggCount" },
      { "id": "writeLineCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.lineCount" },
      { "id": "hexRange", "op": "QueryHexRange", "queryCapacityPolicy": "RequireComplete", "hexRadius": 2 },
      { "id": "hexRangeCount", "op": "AggCount" },
      { "id": "writeHexRangeCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.hexRangeCount" },
      { "id": "hexRing", "op": "QueryHexRing", "queryCapacityPolicy": "RequireComplete", "hexRadius": 3 },
      { "id": "hexRingCount", "op": "AggCount" },
      { "id": "writeHexRingCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.hexRingCount" },
      { "id": "hexNeighbors", "op": "QueryHexNeighbors", "queryCapacityPolicy": "RequireComplete" },
      { "id": "hexNeighborCount", "op": "AggCount" },
      { "id": "writeHexNeighborCount", "op": "WriteBlackboardInt", "blackboardKey": "tests.bb.hexNeighborCount" }
    ],
    "controlEdges": [
      { "from": "self", "fromPort": "next", "to": "coneDir" },
      { "from": "coneDir", "fromPort": "next", "to": "coneHalf" },
      { "from": "coneHalf", "fromPort": "next", "to": "cone" },
      { "from": "cone", "fromPort": "next", "to": "layer" },
      { "from": "layer", "fromPort": "next", "to": "notSelf" },
      { "from": "notSelf", "fromPort": "next", "to": "hostile" },
      { "from": "hostile", "fromPort": "next", "to": "zero" },
      { "from": "zero", "fromPort": "next", "to": "first" },
      { "from": "first", "fromPort": "next", "to": "filteredCount" },
      { "from": "filteredCount", "fromPort": "next", "to": "writeFirst" },
      { "from": "writeFirst", "fromPort": "next", "to": "writeFilteredCount" },
      { "from": "writeFilteredCount", "fromPort": "next", "to": "rectW" },
      { "from": "rectW", "fromPort": "next", "to": "rectH" },
      { "from": "rectH", "fromPort": "next", "to": "rect" },
      { "from": "rect", "fromPort": "next", "to": "rectCount" },
      { "from": "rectCount", "fromPort": "next", "to": "writeRectCount" },
      { "from": "writeRectCount", "fromPort": "next", "to": "lineDir" },
      { "from": "lineDir", "fromPort": "next", "to": "lineLen" },
      { "from": "lineLen", "fromPort": "next", "to": "line" },
      { "from": "line", "fromPort": "next", "to": "lineCount" },
      { "from": "lineCount", "fromPort": "next", "to": "writeLineCount" },
      { "from": "writeLineCount", "fromPort": "next", "to": "hexRange" },
      { "from": "hexRange", "fromPort": "next", "to": "hexRangeCount" },
      { "from": "hexRangeCount", "fromPort": "next", "to": "writeHexRangeCount" },
      { "from": "writeHexRangeCount", "fromPort": "next", "to": "hexRing" },
      { "from": "hexRing", "fromPort": "next", "to": "hexRingCount" },
      { "from": "hexRingCount", "fromPort": "next", "to": "writeHexRingCount" },
      { "from": "writeHexRingCount", "fromPort": "next", "to": "hexNeighbors" },
      { "from": "hexNeighbors", "fromPort": "next", "to": "hexNeighborCount" },
      { "from": "hexNeighborCount", "fromPort": "next", "to": "writeHexNeighborCount" }
    ],
    "valueEdges": [
      { "from": "coneDir", "fromPort": "value", "to": "cone", "toPort": "a" },
      { "from": "coneHalf", "fromPort": "value", "to": "cone", "toPort": "b" },
      { "from": "self", "fromPort": "value", "to": "notSelf", "toPort": "source" },
      { "from": "self", "fromPort": "value", "to": "hostile", "toPort": "source" },
      { "from": "zero", "fromPort": "value", "to": "first", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeFirst", "toPort": "source" },
      { "from": "first", "fromPort": "value", "to": "writeFirst", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeFilteredCount", "toPort": "source" },
      { "from": "filteredCount", "fromPort": "value", "to": "writeFilteredCount", "toPort": "value" },
      { "from": "rectW", "fromPort": "value", "to": "rect", "toPort": "a" },
      { "from": "rectH", "fromPort": "value", "to": "rect", "toPort": "b" },
      { "from": "self", "fromPort": "value", "to": "writeRectCount", "toPort": "source" },
      { "from": "rectCount", "fromPort": "value", "to": "writeRectCount", "toPort": "value" },
      { "from": "lineDir", "fromPort": "value", "to": "line", "toPort": "a" },
      { "from": "lineLen", "fromPort": "value", "to": "line", "toPort": "b" },
      { "from": "self", "fromPort": "value", "to": "writeLineCount", "toPort": "source" },
      { "from": "lineCount", "fromPort": "value", "to": "writeLineCount", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeHexRangeCount", "toPort": "source" },
      { "from": "hexRangeCount", "fromPort": "value", "to": "writeHexRangeCount", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeHexRingCount", "toPort": "source" },
      { "from": "hexRingCount", "fromPort": "value", "to": "writeHexRingCount", "toPort": "value" },
      { "from": "self", "fromPort": "value", "to": "writeHexNeighborCount", "toPort": "source" },
      { "from": "hexNeighborCount", "fromPort": "value", "to": "writeHexNeighborCount", "toPort": "value" }
    ]
  }
]
""";

        private const string DerivedAttributeGraphJson = """
[
  {
    "id": "tests.graph.derived-attribute",
    "kind": "Derived",
    "entry": "source",
    "nodes": [
      { "id": "source", "op": "LoadSelfAttribute", "attribute": "tests.attr.source" },
      { "id": "bonus", "op": "ConstFloat", "floatValue": 2.5 },
      { "id": "sum", "op": "AddFloat" },
      { "id": "writeDerived", "op": "WriteSelfAttribute", "attribute": "tests.attr.derived" }
    ],
    "controlEdges": [
      { "from": "source", "fromPort": "next", "to": "bonus" },
      { "from": "bonus", "fromPort": "next", "to": "sum" },
      { "from": "sum", "fromPort": "next", "to": "writeDerived" }
    ],
    "valueEdges": [
      { "from": "source", "fromPort": "value", "to": "sum", "toPort": "a" },
      { "from": "bonus", "fromPort": "value", "to": "sum", "toPort": "b" },
      { "from": "sum", "fromPort": "value", "to": "writeDerived", "toPort": "value" }
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
            public void SpawnTemplate(int templateKeyId, Arch.Core.Entity source, float xCm, float yCm, bool hasPosition)
            {
            }

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
