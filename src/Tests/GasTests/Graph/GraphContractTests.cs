using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Graph
{
    [TestFixture]
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
    "nodes": [ { "id": "n0", "op": "ConstBool", "boolValue": true } ]
  }
]
""";

            AggregateException ex = Assert.Throws<AggregateException>(() => LoadPrograms(json))!;
            Assert.That(ex.Message, Does.Contain("GAS/graphs.json"));
            Assert.That(ex.InnerExceptions[0].Message, Does.Contain("tests.graph.unknown-member"));
            Assert.That(ex.InnerExceptions[0].Message, Does.Contain("notARealField").IgnoreCase);
        }

        [Test]
        public void GraphCompiler_RejectsUnsupportedKind()
        {
            var cfg = new GraphConfig
            {
                Id = "tests.graph.bad-kind",
                Kind = "LegacyCompat",
                Entry = "n0",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig { Id = "n0", Op = "ConstBool", BoolValue = true }
                }
            };

            var (package, diagnostics) = GraphCompiler.Compile(cfg);
            Assert.That(package, Is.Null);
            Assert.That(diagnostics.Exists(d =>
                d.Code == GraphDiagnosticCodes.UnsupportedGraphKind &&
                d.Message.Contains("LegacyCompat", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void GraphCompiler_PersistsAuthoredKind_AndRegistryRejectsDuplicates()
        {
            const string json = """
[
  {
    "id": "tests.graph.kind-score",
    "kind": "Score",
    "entry": "n0",
    "nodes": [ { "id": "n0", "op": "ConstFloat", "floatValue": 1.5 } ]
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
        public void GraphExecutor_ExecuteValidation_FailsClosed_UntilB0WrittenTrue()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();

            Assert.That(
                GraphExecutor.ExecuteValidation(world, caster, target, default, ReadOnlySpan<GraphInstruction>.Empty, null!),
                Is.False);

            var rejectOnly = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ConstFloat,
                Dst = 0,
                ImmF = 9f
            };
            Assert.That(
                GraphExecutor.ExecuteValidation(world, caster, target, default, new[] { rejectOnly }, null!),
                Is.False);

            var pass = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ConstBool,
                Dst = 0,
                Imm = 1
            };
            Assert.That(
                GraphExecutor.ExecuteValidation(world, caster, target, default, new[] { pass }, null!),
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
