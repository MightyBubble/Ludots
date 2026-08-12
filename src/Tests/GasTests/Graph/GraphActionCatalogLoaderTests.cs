using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphActionCatalogLoaderTests
    {
        [Test]
        public void LoadsScriptAction_WithYieldProgram()
        {
            GraphIdRegistry.Clear();
            string root = string.Empty;
            try
            {
                const string graphName = "Graph.Script.YieldAction";
                int graphId = GraphIdRegistry.Register(graphName);
                var programs = new GraphProgramRegistry();
                programs.Register(
                    graphId,
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.Yield } },
                    GraphKind.Script);

                var (pipeline, configCatalog, tempRoot) = CreateCatalogPipeline(
                    "GAS/action_lib.json",
                    """
                    [
                      { "name": "script.yieldAction", "graph": "Graph.Script.YieldAction", "kind": "Script" }
                    ]
                    """);
                root = tempRoot;

                var actionCatalog = new GraphActionCatalog();
                new GraphActionCatalogLoader(pipeline, actionCatalog, programs, new GraphFunctionCatalog()).Load(configCatalog);

                Assert.That(actionCatalog.Count, Is.EqualTo(1));
                Assert.That(actionCatalog.Require("script.yieldAction"), Is.EqualTo(graphId));
            }
            finally
            {
                GraphIdRegistry.Clear();
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void RejectsFuncLibNameClash()
        {
            GraphIdRegistry.Clear();
            string root = string.Empty;
            try
            {
                const string graphName = "Graph.Script.Shared";
                int graphId = GraphIdRegistry.Register(graphName);
                var programs = new GraphProgramRegistry();
                programs.Register(
                    graphId,
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt } },
                    GraphKind.Script);

                var functions = new GraphFunctionCatalog();
                functions.Register("shared.name", graphId, GraphKind.Script);

                var (pipeline, configCatalog, tempRoot) = CreateCatalogPipeline(
                    "GAS/action_lib.json",
                    """
                    [
                      { "name": "shared.name", "graph": "Graph.Script.Shared", "kind": "Script" }
                    ]
                    """);
                root = tempRoot;

                var actionCatalog = new GraphActionCatalog();
                var ex = Assert.Throws<AggregateException>(() =>
                    new GraphActionCatalogLoader(pipeline, actionCatalog, programs, functions).Load(configCatalog));

                Assert.That(actionCatalog.Count, Is.EqualTo(0));
                Assert.That(ex!.InnerExceptions[0].Message, Does.Contain("duplicates FuncLib name"));
            }
            finally
            {
                GraphIdRegistry.Clear();
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void RejectsNonScriptKind()
        {
            GraphIdRegistry.Clear();
            string root = string.Empty;
            try
            {
                const string graphName = "Graph.Effect.BadAction";
                int graphId = GraphIdRegistry.Register(graphName);
                var programs = new GraphProgramRegistry();
                programs.Register(
                    graphId,
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt } },
                    GraphKind.Effect);

                var (pipeline, configCatalog, tempRoot) = CreateCatalogPipeline(
                    "GAS/action_lib.json",
                    """
                    [
                      { "name": "bad.effectAction", "graph": "Graph.Effect.BadAction", "kind": "Effect" }
                    ]
                    """);
                root = tempRoot;

                var actionCatalog = new GraphActionCatalog();
                var ex = Assert.Throws<AggregateException>(() =>
                    new GraphActionCatalogLoader(pipeline, actionCatalog, programs, new GraphFunctionCatalog()).Load(configCatalog));

                Assert.That(actionCatalog.Count, Is.EqualTo(0));
                Assert.That(ex!.InnerExceptions[0].Message, Does.Contain("must be Script"));
            }
            finally
            {
                GraphIdRegistry.Clear();
                DeleteTempRoot(root);
            }
        }

        private static (ConfigPipeline Pipeline, ConfigCatalog Catalog, string TempRoot) CreateCatalogPipeline(
            string relativePath,
            string json)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "Ludots_GraphActionCatalogLoaderTests",
                Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(root, "Core");
            string fullPath = Path.Combine(coreRoot, "Configs", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry(relativePath, ConfigMergePolicy.ArrayById, "name"));
            return (pipeline, catalog, root);
        }

        private static void DeleteTempRoot(string root)
        {
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
