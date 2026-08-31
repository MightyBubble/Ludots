using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS.Graph
{
    [TestFixture]
    [NonParallelizable]
    public sealed class GraphContractTests
    {
        private string? _tempRoot;

        [TearDown]
        public void TearDown()
        {
            if (_tempRoot != null && Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }

            _tempRoot = null;
            GraphIdRegistry.Clear();
        }

        [Test]
        public void GraphProgramConfigLoader_RejectsUnknownJsonMember_WithAssetContext()
        {
            const string json = """
[
  {
    "id": "tests.graph.unknown-member",
    "kind": "Effect",
    "entry": "n0",
    "notARealField": true,
    "nodes": [ { "id": "n0", "op": "ConstFloat", "floatValue": 1.0 } ],
    "controlEdges": [],
    "valueEdges": []
  }
]
""";

            AggregateException ex = Assert.Throws<AggregateException>(() => LoadPrograms(json))!;
            Assert.That(ex.Message, Does.Contain("GAS/graphs.json"));
            Assert.That(ex.InnerExceptions[0].Message, Does.Contain("tests.graph.unknown-member"));
            Assert.That(ex.InnerExceptions[0].Message, Does.Contain("notARealField").IgnoreCase);
        }

        [Test]
        public void GraphProgramAuthoringFrontDoor_RejectsNumericKindToken()
        {
            var obj = JsonNode.Parse("""
{
  "kind": "1",
  "entry": "n0",
  "nodes": [ { "id": "n0", "op": "ConstBool", "boolValue": true } ],
  "controlEdges": [],
  "valueEdges": []
}
""")!.AsObject();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                GraphProgramAuthoringFrontDoor.RequireKind(obj, "tests.graph.numeric-kind"))!;
            Assert.That(ex.Message, Does.Contain("tests.graph.numeric-kind"));
        }

        [Test]
        public void GraphProgramAuthoringFrontDoor_RejectsUnsupportedKind()
        {
            var obj = JsonNode.Parse("""
{
  "kind": "LegacyCompat",
  "entry": "n0",
  "nodes": [ { "id": "n0", "op": "ConstBool", "boolValue": true } ],
  "controlEdges": [],
  "valueEdges": []
}
""")!.AsObject();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                GraphProgramAuthoringFrontDoor.RequireKind(obj, "tests.graph.bad-kind"))!;
            Assert.That(ex.Message, Does.Contain("requires an authored kind"));
        }

        [Test]
        public void GraphProgramConfigLoader_PersistsAuthoredKind_AndRegistryRejectsDuplicates()
        {
            const string json = """
[
  {
    "id": "tests.graph.kind-score",
    "kind": "Score",
    "entry": "n0",
    "nodes": [ { "id": "n0", "op": "ConstFloat", "floatValue": 1.5 } ],
    "controlEdges": [],
    "valueEdges": []
  }
]
""";
            GraphProgramRegistry programs = LoadPrograms(json);
            int graphId = GraphIdRegistry.GetId("tests.graph.kind-score");
            Assert.That(programs.TryGetKind(graphId, out GraphKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(GraphKind.Score));
            Assert.That(programs.RequireKind(graphId, GraphKind.Score), Is.EqualTo(GraphKind.Score));

            Assert.Throws<InvalidOperationException>(() =>
                programs.Register(graphId, Array.Empty<GraphInstruction>(), GraphKind.Effect));
        }

        [Test]
        public void GraphProgramConfigLoader_RejectsQueryGraphWithoutControlFlowEdges()
        {
            const string json = """
[
  {
    "id": "tests.graph.query-legacy-shape",
    "kind": "Query",
    "entry": "allMap",
    "nodes": [
      { "id": "allMap", "op": "QueryAllMapEntities" }
    ]
  }
]
""";

            AggregateException error = Assert.Throws<AggregateException>(() => LoadPrograms(json))!;
            Assert.That(error.InnerExceptions, Has.Some.Matches<Exception>(ex =>
                ex.Message.Contains("must author controlEdges and valueEdges", StringComparison.Ordinal) ||
                ex.Message.Contains("uses nodes[].next", StringComparison.Ordinal)));
        }

        [Test]
        public void GraphProgramConfigLoader_RejectsEffectGraphWithNodesNext()
        {
            const string json = """
[
  {
    "id": "tests.graph.effect-legacy-next",
    "kind": "Effect",
    "entry": "c0",
    "nodes": [
      { "id": "c0", "op": "ConstFloat", "floatValue": 1.0, "next": "c1" },
      { "id": "c1", "op": "ConstFloat", "floatValue": 2.0 }
    ]
  }
]
""";

            AggregateException error = Assert.Throws<AggregateException>(() => LoadPrograms(json))!;
            Assert.That(error.InnerExceptions, Has.Some.Matches<Exception>(ex =>
                ex.Message.Contains("uses nodes[].next", StringComparison.Ordinal)));
        }

        [Test]
        public void GraphProgramConfigLoader_CompilesEffectControlFlowByKind()
        {
            const string json = """
[
  {
    "id": "tests.graph.effect-control-flow",
    "kind": "Effect",
    "entry": "target",
    "nodes": [
      { "id": "target", "op": "LoadExplicitTarget" },
      { "id": "delta", "op": "ConstFloat", "floatValue": 3.0 },
      { "id": "modify", "op": "ModifyAttributeAdd", "attribute": "Health" }
    ],
    "controlEdges": [
      { "from": "target", "fromPort": "next", "to": "delta" },
      { "from": "delta", "fromPort": "next", "to": "modify" }
    ],
    "valueEdges": [
      { "from": "target", "fromPort": "value", "to": "modify", "toPort": "target" },
      { "from": "delta", "fromPort": "value", "to": "modify", "toPort": "value" }
    ]
  }
]
""";

            GraphProgramRegistry programs = LoadPrograms(json);
            int graphId = GraphIdRegistry.GetId("tests.graph.effect-control-flow");
            Assert.That(programs.TryGetKind(graphId, out GraphKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(GraphKind.Effect));
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
            Assert.That(program.Length, Is.GreaterThan(0));
        }

        [Test]
        public void GraphKindOperationPolicy_RejectsQueryGraphWithGameplayWrite()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 1, B = 0, Imm = 1 }
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Query,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    graphId: 1,
                    entrypoint: nameof(GraphContractTests)))!;

            Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
            Assert.That(error.Message, Does.Contain("kind='Query'"));
            Assert.That(error.Message, Does.Contain("operation='ModifyAttributeAdd'"));
        }

        [Test]
        public void GraphExecutor_ExecuteValidation_FailsClosed_UntilB0WrittenTrue()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();

            Assert.Throws<InvalidOperationException>(() =>
                GraphExecutor.ExecuteValidation(world, caster, target, default, ReadOnlySpan<GraphInstruction>.Empty, null!));

            var rejectOnly = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 9f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };
            Assert.That(
                GraphExecutor.ExecuteValidation(world, caster, target, default, rejectOnly, null!),
                Is.False);

            var pass = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 }
            };
            Assert.That(
                GraphExecutor.ExecuteValidation(world, caster, target, default, pass, null!),
                Is.True);
        }

        [Test]
        public void GraphExecutor_KindOverloads_RejectMismatchedKinds()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 }
            };

            Assert.Throws<InvalidOperationException>(() =>
                GraphExecutor.ExecuteValidation(world, caster, Entity.Null, default, program, null!, GraphKind.Effect));
            Assert.Throws<InvalidOperationException>(() =>
                GraphExecutor.ExecuteScore(world, caster, Entity.Null, default, program, null!, GraphKind.Validation));
            Assert.Throws<InvalidOperationException>(() =>
                GraphExecutor.Execute(world, caster, Entity.Null, default, program, null!, GraphKind.Query));
        }

        [TestCase(GraphKind.Validation)]
        [TestCase(GraphKind.Score)]
        [TestCase(GraphKind.Query)]
        public void GraphKindOperationPolicy_ReadOnlyKindsRejectGameplayWrites(GraphKind kind)
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd }
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                GraphKindOperationPolicy.RequireAllowed(kind, program, GasGraphOpHandlerTable.Instance, 17, "Test"))!;

            Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
            Assert.That(error.Message, Does.Contain($"kind='{kind}'"));
        }

        [TestCase(GraphKind.Validation, GraphNodeOp.ShowPanel)]
        [TestCase(GraphKind.Validation, GraphNodeOp.SpawnTemplate)]
        [TestCase(GraphKind.Validation, GraphNodeOp.WriteMapVarInt)]
        [TestCase(GraphKind.Validation, GraphNodeOp.StartDialogue)]
        [TestCase(GraphKind.Score, GraphNodeOp.ShowPanel)]
        [TestCase(GraphKind.Score, GraphNodeOp.SpawnTemplate)]
        [TestCase(GraphKind.Score, GraphNodeOp.WriteMapVarInt)]
        [TestCase(GraphKind.Score, GraphNodeOp.StartDialogue)]
        [TestCase(GraphKind.Query, GraphNodeOp.ShowPanel)]
        [TestCase(GraphKind.Query, GraphNodeOp.SpawnTemplate)]
        [TestCase(GraphKind.Query, GraphNodeOp.WriteMapVarInt)]
        [TestCase(GraphKind.Query, GraphNodeOp.StartDialogue)]
        public void GraphKindOperationPolicy_ReadOnlyKindsRejectPureLabeledSideEffects(
            GraphKind kind,
            GraphNodeOp forbidden)
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)forbidden, A = 0, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    kind,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    graphId: 1410,
                    entrypoint: nameof(GraphKindOperationPolicy_ReadOnlyKindsRejectPureLabeledSideEffects)))!;

            Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
            Assert.That(error.Message, Does.Contain($"kind='{kind}'"));
            Assert.That(error.Message, Does.Contain($"operation='{forbidden}'"));
        }

        [Test]
        public void GraphProgramRegistry_RejectsHandRegisteredQueryWithShowPanel()
        {
            var programs = new GraphProgramRegistry();
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ShowPanel, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                programs.Register(14101, program, GraphKind.Query))!;

            Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
            Assert.That(error.Message, Does.Contain("ShowPanel"));
        }

        [Test]
        public void GraphKindOperationPolicy_ScriptStillAllowsShowPanel()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ShowPanel, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Script,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId: 14102,
                entrypoint: nameof(GraphKindOperationPolicy_ScriptStillAllowsShowPanel)));
        }

        [Test]
        public void GraphKindOperationPolicy_DerivedAllowsOnlySelfAttributeWrite()
        {
            var allowed = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteSelfAttribute },
            };
            var rejected = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat },
            };

            Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Derived,
                allowed,
                GasGraphOpHandlerTable.Instance));
            Assert.Throws<InvalidOperationException>(() => GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Derived,
                rejected,
                GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void GraphKindOperationPolicy_EffectDefersOperationClassificationToEffectPlan()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd },
                new GraphInstruction { Op = (ushort)GraphNodeOp.BeginLifecycleTransaction },
            };

            Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Effect,
                program,
                GasGraphOpHandlerTable.Instance));
        }

        [Test]
        public void GraphKindOperationPolicy_AllowedProgram_DoesNotAllocateAfterWarmup()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat },
            };
            for (int i = 0; i < 32; i++)
            {
                GraphKindOperationPolicy.RequireAllowed(GraphKind.Score, program, GasGraphOpHandlerTable.Instance);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                GraphKindOperationPolicy.RequireAllowed(GraphKind.Score, program, GasGraphOpHandlerTable.Instance);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void GraphExecutor_ValidationRejectsGameplayWriteBeforeExecution()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd }
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                GraphExecutor.ExecuteValidation(world, caster, Entity.Null, default, program, null!))!;

            Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
        }

        [Test]
        public void GasGraphOpHandlerTable_Registration_RequiresDescription_AndRejectsDuplicates()
        {
            Assert.That(GasGraphOpHandlerTable.Instance.TryGetDescription(GraphNodeOp.ConstBool, out string description), Is.True);
            Assert.That(description, Is.Not.Empty);

            Assert.Throws<ArgumentException>(() =>
                GasGraphOpHandlerTable.Instance.Register(
                    GraphNodeOp.ConstBool,
                    static (ref GraphExecutionState state, in GraphInstruction ins, ref int pc) => { },
                    " "));

            Assert.Throws<InvalidOperationException>(() =>
                GasGraphOpHandlerTable.Instance.Register(
                    GraphNodeOp.ConstBool,
                    static (ref GraphExecutionState state, in GraphInstruction ins, ref int pc) => { },
                    "duplicate attempt"));
        }

        [Test]
        public void GasGraphRuntimeApi_BindLoadedGraph_PreflightsEdgeSnapScratch_WithoutHotPathResize()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null, new Ludots.Core.Gameplay.GAS.EffectRequestQueue());
            api.BindLoadedGraphRuntime(null);

            IntVector2 pos = default;
            Assert.That(api.TrySnapTargetToNearestGraphEdge(ref pos, 100f, out _), Is.False);
            Assert.That(
                typeof(GasGraphRuntimeApi)
                    .GetMethod("EnsureGraphProjectionCandidateCapacity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                Is.Null);
        }

        private GraphProgramRegistry LoadPrograms(string graphJson)
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphContractTests", Guid.NewGuid().ToString("N"));
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

            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            var loader = new GraphProgramConfigLoader(pipeline, programs, new PassthroughGraphSymbolResolver());
            var packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
            loader.PatchAndRegister(packages);
            return programs;
        }

        private sealed class PassthroughGraphSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => 1;
            public int ResolveAttribute(string name) => 1;
            public int ResolveEffectTemplate(string name) => 1;
            public int ResolveRelationshipType(string name) => 1;
            public int ResolveRelationshipMetric(string name) => 1;
            public int ResolveRelationshipFlag(string name) => 1;
            public int ResolveRelationshipReason(string name) => 1;
            public int ResolveTargetDispatchPreset(string name) => 1;
            public int ResolveEntityTemplate(string name) => 1;
        }
    }
}
