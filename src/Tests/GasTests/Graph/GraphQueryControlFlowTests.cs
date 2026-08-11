using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphQueryControlFlowTests
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
                Directory.Delete(_tempRoot, recursive: true);
            }

            _tempRoot = null;
        }

        [Test]
        public void CompileWithOutputs_AggregateControlFlowQuery_ProducesProgramAndSummarySchema()
        {
            var (package, schema, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(CreateAggregateDocument());

            Assert.That(package.HasValue, Is.True, FormatDiagnostics(diagnostics));
            Assert.That(package!.Value.Kind, Is.EqualTo(GraphKind.Query));
            Assert.That(package.Value.Program.Length, Is.GreaterThan(0));
            Assert.That(package.Value.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QueryAllMapEntities));
            Assert.That(schema.Bindings.Select(b => b.Key), Is.EquivalentTo(new[]
            {
                "ui.panel.player.ore.total",
                "ui.panel.player.crystal.total"
            }));
            Assert.That(schema.Bindings, Has.All.Matches<GraphOutputBinding>(b => b.ValueKind == GraphOutputValueKind.Float));
        }

        [Test]
        public void CompileWithOutputs_MissingListEdge_FailsClosed()
        {
            GraphControlFlowDocument doc = CreateAggregateDocument();
            doc.ValueEdges.RemoveAll(e => e.To == "oreSum");

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(doc);

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.MissingValueInput &&
                d.NodeId == "oreSum"));
        }

        [Test]
        public void CompileWithOutputs_QueryRejectsUnknownOrWrongOp()
        {
            GraphControlFlowDocument doc = CreateAggregateDocument();
            doc.Nodes[1].Op = nameof(GraphNodeOp.AddInt);

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(doc);

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "allMap"));
        }

        [Test]
        public void GraphCompiler_RejectsQueryKind_OnNextChainPath()
        {
            var cfg = new GraphConfig
            {
                Id = "bad.query.next",
                Kind = "Query",
                Entry = "allMap",
                Nodes =
                {
                    new GraphNodeConfig
                    {
                        Id = "allMap",
                        Op = nameof(GraphNodeOp.QueryAllMapEntities)
                    }
                }
            };

            var (package, _, diagnostics) = GraphCompiler.CompileWithOutputs(cfg);

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.UnsupportedGraphKind &&
                d.Message.Contains("GraphControlFlowCompiler", StringComparison.Ordinal)));
        }

        [Test]
        public void CompileWithOutputs_CityEconomyControlFlowQuery_CompilesFullOpSurface()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.graph.4x.cityEconomy",
                Kind = "Query",
                Entry = "minProduction",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "minProduction", Op = nameof(GraphNodeOp.ConstFloat), FloatValue = 0 },
                    new() { Id = "maxProduction", Op = nameof(GraphNodeOp.ConstFloat), FloatValue = 100 },
                    new() { Id = "allMapEntities", Op = nameof(GraphNodeOp.QueryAllMapEntities) },
                    new() { Id = "team", Op = nameof(GraphNodeOp.QueryFilterTeam), TeamId = 1 },
                    new() { Id = "template", Op = nameof(GraphNodeOp.QueryFilterTemplate), Template = "tests.graph.city" },
                    new() { Id = "notBlocked", Op = nameof(GraphNodeOp.QueryFilterTagNone), Tag = "Tests.GraphQuery.Blocked" },
                    new() { Id = "productionRange", Op = nameof(GraphNodeOp.QueryFilterAttributeRange), Attribute = "Health" },
                    new() { Id = "sortProduction", Op = nameof(GraphNodeOp.QuerySortByAttribute), Attribute = "Health", Descending = true },
                    new() { Id = "countCities", Op = nameof(GraphNodeOp.AggCount) },
                    new() { Id = "sumProduction", Op = nameof(GraphNodeOp.AggSumAttribute), Attribute = "Health" },
                    new() { Id = "bestProductionCity", Op = nameof(GraphNodeOp.AggMaxEntityByAttribute), Attribute = "Health" }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("minProduction", GraphControlFlowPorts.Next, "maxProduction"),
                    new("maxProduction", GraphControlFlowPorts.Next, "allMapEntities"),
                    new("allMapEntities", GraphControlFlowPorts.Next, "team"),
                    new("team", GraphControlFlowPorts.Next, "template"),
                    new("template", GraphControlFlowPorts.Next, "notBlocked"),
                    new("notBlocked", GraphControlFlowPorts.Next, "productionRange"),
                    new("productionRange", GraphControlFlowPorts.Next, "sortProduction"),
                    new("sortProduction", GraphControlFlowPorts.Next, "countCities"),
                    new("countCities", GraphControlFlowPorts.Next, "sumProduction"),
                    new("sumProduction", GraphControlFlowPorts.Next, "bestProductionCity")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("allMapEntities", GraphControlFlowPorts.List, "team", GraphControlFlowPorts.List),
                    new("team", GraphControlFlowPorts.List, "template", GraphControlFlowPorts.List),
                    new("template", GraphControlFlowPorts.List, "notBlocked", GraphControlFlowPorts.List),
                    new("notBlocked", GraphControlFlowPorts.List, "productionRange", GraphControlFlowPorts.List),
                    new("minProduction", GraphControlFlowPorts.Value, "productionRange", GraphControlFlowPorts.Min),
                    new("maxProduction", GraphControlFlowPorts.Value, "productionRange", GraphControlFlowPorts.Max),
                    new("productionRange", GraphControlFlowPorts.List, "sortProduction", GraphControlFlowPorts.List),
                    new("sortProduction", GraphControlFlowPorts.List, "countCities", GraphControlFlowPorts.List),
                    new("sortProduction", GraphControlFlowPorts.List, "sumProduction", GraphControlFlowPorts.List),
                    new("sortProduction", GraphControlFlowPorts.List, "bestProductionCity", GraphControlFlowPorts.List)
                },
                Outputs = new List<GraphOutputConfig>
                {
                    new()
                    {
                        Id = "cities",
                        Destination = nameof(GraphOutputDestinationKind.EntityCollection),
                        Type = nameof(GraphOutputValueKind.TargetList),
                        CollectionKey = "tests.graph.collection.cities",
                        Role = "Display"
                    },
                    new()
                    {
                        Id = "cityCount",
                        Destination = nameof(GraphOutputDestinationKind.Summary),
                        Type = nameof(GraphOutputValueKind.Int),
                        Source = "countCities",
                        Key = "tests.graph.cityCount"
                    },
                    new()
                    {
                        Id = "bestProductionCity",
                        Destination = nameof(GraphOutputDestinationKind.Summary),
                        Type = nameof(GraphOutputValueKind.Entity),
                        Source = "bestProductionCity",
                        Key = "tests.graph.bestProductionCity"
                    }
                }
            };

            var (package, schema, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(doc);

            Assert.That(package.HasValue, Is.True, FormatDiagnostics(diagnostics));
            Assert.That(schema.Bindings, Has.Some.Matches<GraphOutputBinding>(b =>
                b.Destination == GraphOutputDestinationKind.EntityCollection &&
                b.CollectionKey == "tests.graph.collection.cities"));
            Assert.That(package!.Value.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.QueryFilterAttributeRange));
            Assert.That(package.Value.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.AggMaxEntityByAttribute));
        }

        [Test]
        public void Compile_ScriptKindRejectsQueryAllMapEntities()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.script.reject-query-op",
                Kind = "Script",
                Entry = "allMap",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "allMap", Op = nameof(GraphNodeOp.QueryAllMapEntities) }
                }
            };

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);

            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.UnknownNodeOp &&
                d.NodeId == "allMap"));
        }

        [Test]
        public void GraphProgramConfigLoader_CompilesControlFlowQueryGraphsJson()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphQueryControlFlowTests", Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(_tempRoot, "Core");
            string graphDir = Path.Combine(coreRoot, "Configs", "GAS");
            Directory.CreateDirectory(graphDir);
            File.WriteAllText(Path.Combine(graphDir, "graphs.json"), AggregateGraphJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

            var programs = new GraphProgramRegistry();
            var outputs = new GraphOutputSchemaRegistry();
            var outputKeys = new StringIntRegistry();
            var loader = new GraphProgramConfigLoader(pipeline, programs, new TestGraphSymbolResolver(), outputs, outputKeys);
            List<GraphProgramPackage> packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
            loader.PatchAndRegister(packages);

            int graphId = GraphIdRegistry.GetId("ui.panel.player.resource.aggregate");
            Assert.That(graphId, Is.GreaterThan(0));
            Assert.That(programs.RequireKind(graphId, GraphKind.Query), Is.EqualTo(GraphKind.Query));
            Assert.That(outputs.Get(graphId).Bindings.Select(b => b.Key), Does.Contain("ui.panel.player.ore.total"));
            Assert.That(outputKeys.GetId("ui.panel.player.crystal.total"), Is.GreaterThan(0));
        }

        [Test]
        public void GraphProgramConfigLoader_ReloadExistingAndReplace_UpdatesProgramWithoutRenumbering()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphQueryControlFlowTests", Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(_tempRoot, "Core");
            string graphDir = Path.Combine(coreRoot, "Configs", "GAS");
            Directory.CreateDirectory(graphDir);
            string graphPath = Path.Combine(graphDir, "graphs.json");
            File.WriteAllText(graphPath, AggregateGraphJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

            var programs = new GraphProgramRegistry();
            var outputs = new GraphOutputSchemaRegistry();
            var outputKeys = new StringIntRegistry();
            var loader = new GraphProgramConfigLoader(pipeline, programs, new TestGraphSymbolResolver(), outputs, outputKeys);
            List<GraphProgramPackage> packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
            loader.PatchAndRegister(packages);

            int graphId = GraphIdRegistry.GetId("ui.panel.player.resource.aggregate");
            Assert.That(graphId, Is.GreaterThan(0));
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> before), Is.True);
            int beforeLength = before.Length;

            File.WriteAllText(graphPath, AggregateGraphOreOnlyJson);
            loader.ReloadExistingAndReplace(catalog, relativePath: "GAS/graphs.json");

            Assert.That(GraphIdRegistry.GetId("ui.panel.player.resource.aggregate"), Is.EqualTo(graphId));
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> after), Is.True);
            Assert.That(after.Length, Is.Not.EqualTo(beforeLength));
            Assert.That(outputs.Get(graphId).Bindings.Select(b => b.Key), Does.Contain("ui.panel.player.ore.total"));
            Assert.That(outputs.Get(graphId).Bindings.Select(b => b.Key), Does.Not.Contain("ui.panel.player.crystal.total"));
        }

        [Test]
        public void GraphProgramConfigLoader_ReloadExistingAndReplace_RejectsNewGraphId()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphQueryControlFlowTests", Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(_tempRoot, "Core");
            string graphDir = Path.Combine(coreRoot, "Configs", "GAS");
            Directory.CreateDirectory(graphDir);
            string graphPath = Path.Combine(graphDir, "graphs.json");
            File.WriteAllText(graphPath, AggregateGraphJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

            var programs = new GraphProgramRegistry();
            var loader = new GraphProgramConfigLoader(pipeline, programs, new TestGraphSymbolResolver());
            loader.PatchAndRegister(loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json"));

            File.WriteAllText(graphPath, AggregateGraphWithExtraIdJson);
            AggregateException ex = Assert.Throws<AggregateException>(
                () => loader.ReloadExistingAndReplace(catalog, relativePath: "GAS/graphs.json"));
            Assert.That(ex!.Message, Does.Contain("reload error"));
            Assert.That(ex.InnerExceptions[0].Message, Does.Contain("hot reload cannot introduce new graph ids"));
        }

        private static GraphControlFlowDocument CreateAggregateDocument()
        {
            return new GraphControlFlowDocument
            {
                Id = "ui.panel.player.resource.aggregate",
                Kind = "Query",
                Entry = "owner",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "owner", Op = nameof(GraphNodeOp.LoadCaster) },
                    new() { Id = "allMap", Op = nameof(GraphNodeOp.QueryAllMapEntities) },
                    new() { Id = "team", Op = nameof(GraphNodeOp.QueryFilterTeam), TeamId = 1 },
                    new() { Id = "oreSum", Op = nameof(GraphNodeOp.AggSumAttribute), Attribute = "Showcase.Resource.Ore" },
                    new() { Id = "crystalSum", Op = nameof(GraphNodeOp.AggSumAttribute), Attribute = "Showcase.Resource.Crystal" }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("owner", GraphControlFlowPorts.Next, "allMap"),
                    new("allMap", GraphControlFlowPorts.Next, "team"),
                    new("team", GraphControlFlowPorts.Next, "oreSum"),
                    new("oreSum", GraphControlFlowPorts.Next, "crystalSum")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("allMap", GraphControlFlowPorts.List, "team", GraphControlFlowPorts.List),
                    new("team", GraphControlFlowPorts.List, "oreSum", GraphControlFlowPorts.List),
                    new("team", GraphControlFlowPorts.List, "crystalSum", GraphControlFlowPorts.List)
                },
                Outputs = new List<GraphOutputConfig>
                {
                    new()
                    {
                        Id = "oreTotal",
                        Destination = nameof(GraphOutputDestinationKind.Summary),
                        Type = nameof(GraphOutputValueKind.Float),
                        Source = "oreSum",
                        Key = "ui.panel.player.ore.total"
                    },
                    new()
                    {
                        Id = "crystalTotal",
                        Destination = nameof(GraphOutputDestinationKind.Summary),
                        Type = nameof(GraphOutputValueKind.Float),
                        Source = "crystalSum",
                        Key = "ui.panel.player.crystal.total"
                    }
                }
            };
        }

        private static string FormatDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
            => string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Code}:{d.NodeId}:{d.Message}"));

        private const string AggregateGraphJson = """
[
  {
    "id": "ui.panel.player.resource.aggregate",
    "kind": "Query",
    "entry": "owner",
    "nodes": [
      { "id": "owner", "op": "LoadCaster" },
      { "id": "allMap", "op": "QueryAllMapEntities" },
      { "id": "team", "op": "QueryFilterTeam", "teamId": 1 },
      { "id": "oreSum", "op": "AggSumAttribute", "attribute": "Showcase.Resource.Ore" },
      { "id": "crystalSum", "op": "AggSumAttribute", "attribute": "Showcase.Resource.Crystal" }
    ],
    "controlEdges": [
      { "from": "owner", "fromPort": "next", "to": "allMap" },
      { "from": "allMap", "fromPort": "next", "to": "team" },
      { "from": "team", "fromPort": "next", "to": "oreSum" },
      { "from": "oreSum", "fromPort": "next", "to": "crystalSum" }
    ],
    "valueEdges": [
      { "from": "allMap", "fromPort": "list", "to": "team", "toPort": "list" },
      { "from": "team", "fromPort": "list", "to": "oreSum", "toPort": "list" },
      { "from": "team", "fromPort": "list", "to": "crystalSum", "toPort": "list" }
    ],
    "outputs": [
      {
        "id": "oreTotal",
        "destination": "Summary",
        "type": "Float",
        "source": "oreSum",
        "key": "ui.panel.player.ore.total"
      },
      {
        "id": "crystalTotal",
        "destination": "Summary",
        "type": "Float",
        "source": "crystalSum",
        "key": "ui.panel.player.crystal.total"
      }
    ]
  }
]
""";

        private const string AggregateGraphOreOnlyJson = """
[
  {
    "id": "ui.panel.player.resource.aggregate",
    "kind": "Query",
    "entry": "owner",
    "nodes": [
      { "id": "owner", "op": "LoadCaster" },
      { "id": "allMap", "op": "QueryAllMapEntities" },
      { "id": "team", "op": "QueryFilterTeam", "teamId": 1 },
      { "id": "oreSum", "op": "AggSumAttribute", "attribute": "Showcase.Resource.Ore" }
    ],
    "controlEdges": [
      { "from": "owner", "fromPort": "next", "to": "allMap" },
      { "from": "allMap", "fromPort": "next", "to": "team" },
      { "from": "team", "fromPort": "next", "to": "oreSum" }
    ],
    "valueEdges": [
      { "from": "allMap", "fromPort": "list", "to": "team", "toPort": "list" },
      { "from": "team", "fromPort": "list", "to": "oreSum", "toPort": "list" }
    ],
    "outputs": [
      {
        "id": "oreTotal",
        "destination": "Summary",
        "type": "Float",
        "source": "oreSum",
        "key": "ui.panel.player.ore.total"
      }
    ]
  }
]
""";

        private const string AggregateGraphWithExtraIdJson = """
[
  {
    "id": "ui.panel.player.resource.aggregate",
    "kind": "Query",
    "entry": "owner",
    "nodes": [
      { "id": "owner", "op": "LoadCaster" }
    ],
    "controlEdges": [],
    "valueEdges": [],
    "outputs": []
  },
  {
    "id": "ui.panel.player.resource.aggregate.v2",
    "kind": "Query",
    "entry": "owner",
    "nodes": [
      { "id": "owner", "op": "LoadCaster" }
    ],
    "controlEdges": [],
    "valueEdges": [],
    "outputs": []
  }
]
""";

        private sealed class TestGraphSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => TagRegistry.Register(name);
            public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
            public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
            public int ResolveConfigKey(string name) => ConfigKeyRegistry.Register(name);
            public int ResolveRelationshipType(string name) => ConfigKeyRegistry.Register($"relationship.type.{name}");
            public int ResolveRelationshipMetric(string name) => ConfigKeyRegistry.Register($"relationship.metric.{name}");
            public int ResolveRelationshipFlag(string name) => ConfigKeyRegistry.Register($"relationship.flag.{name}");
            public int ResolveRelationshipReason(string name) => ConfigKeyRegistry.Register($"relationship.reason.{name}");
            public int ResolveTargetDispatchPreset(string name) => ConfigKeyRegistry.Register($"targetDispatch.{name}");
            public int ResolveEntityTemplate(string name) => ConfigKeyRegistry.Register($"entityTemplate.{name}");
        }
    }
}
