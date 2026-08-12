using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public sealed class GraphFunctionCatalogLoaderTests
    {
        [Test]
        public void Compile_InvokeScript_ByFunctionName_PatchesToGraphId()
        {
            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            var catalog = new GraphFunctionCatalog();

            var calleeDoc = new GraphControlFlowDocument
            {
                Id = "Scripts.Demo.Seven",
                Kind = "Script",
                Entry = "c",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "c", Op = nameof(GraphNodeOp.ConstInt), IntValue = 7 },
                    new GraphControlFlowNode { Id = "h", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = { new GraphControlFlowEdge("c", GraphControlFlowPorts.Next, "h") },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("c", GraphControlFlowPorts.Value, "h", GraphControlFlowPorts.Value)
                }
            };
            var callee = GraphControlFlowCompiler.CompileWithOutputs(calleeDoc);
            Assert.That(callee.Package.HasValue, Is.True);
            int calleeId = GraphIdRegistry.Register(calleeDoc.Id);
            programs.Register(calleeId, callee.Package!.Value.Program, GraphKind.Script);
            catalog.Register("demo.seven", calleeId, GraphKind.Script);

            var callerDoc = new GraphControlFlowDocument
            {
                Id = "Scripts.Demo.Caller",
                Kind = "Script",
                Entry = "invoke",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "invoke", Op = nameof(GraphNodeOp.InvokeScript), FunctionName = "demo.seven" },
                    new GraphControlFlowNode { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = { new GraphControlFlowEdge("invoke", GraphControlFlowPorts.Next, "done") },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("invoke", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
            var caller = GraphControlFlowCompiler.CompileWithOutputs(callerDoc);
            Assert.That(caller.Package.HasValue, Is.True);
            Assert.That(caller.Diagnostics, Has.Count.EqualTo(0));

            GraphInstruction[] program = caller.Package!.Value.Program;
            string[] symbols = caller.Package.Value.Symbols;
            GraphProgramSymbolPatcher.PatchFuncLib(symbols, program, catalog);

            bool found = false;
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op != (ushort)GraphNodeOp.InvokeScript) continue;
                Assert.That(program[i].Flags & GraphInstructionFlags.FuncLibName, Is.EqualTo(0));
                Assert.That(program[i].Imm, Is.EqualTo(calleeId));
                found = true;
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void CoreCatalogs_MoveL2ActionsToActionLib()
        {
            _ = GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out GraphFunctionCatalog functions,
                out GraphActionCatalog actions);

            Assert.That(functions.TryGet("bt.patrol", out _), Is.False);
            Assert.That(functions.TryGet("hfsm.combat.onTick", out _), Is.False);
            Assert.That(functions.TryGet("level.phaseAdvance", out _), Is.False);
            Assert.That(functions.TryGet("script.drinkUntilFull", out _), Is.False);
            Assert.That(functions.Require("ability.slash").GraphId, Is.GreaterThan(0));
            Assert.That(actions.Require("bt.patrol"), Is.GreaterThan(0));
            Assert.That(actions.Require("hfsm.combat.onTick"), Is.GreaterThan(0));
            Assert.That(actions.Require("level.phaseAdvance"), Is.GreaterThan(0));
            Assert.That(actions.Require("script.drinkUntilFull"), Is.GreaterThan(0));
        }

        [Test]
        public void FrontDoor_RejectsLegacyNextChain()
        {
            var obj = JsonNode.Parse("""
                {
                  "kind": "Effect",
                  "entry": "a",
                  "nodes": [ { "id": "a", "op": "ConstInt", "intValue": 1, "next": "b" }, { "id": "b", "op": "HaltReturnInt" } ],
                  "controlEdges": [],
                  "valueEdges": []
                }
                """)!.AsObject();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphProgramAuthoringFrontDoor.RequireControlFlowAuthoringShape(obj, "bad.next", GraphKind.Effect));
            Assert.That(ex!.Message, Does.Contain("nodes[].next"));
        }

        [Test]
        public void FuncCatalogLoader_RejectsYieldProgram()
        {
            GraphIdRegistry.Clear();
            string root = string.Empty;
            try
            {
                const string graphName = "Graph.Script.YieldFunction";
                int graphId = GraphIdRegistry.Register(graphName);
                var programs = new GraphProgramRegistry();
                programs.Register(
                    graphId,
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.Yield } },
                    GraphKind.Script);

                var (pipeline, configCatalog, tempRoot) = CreateCatalogPipeline(
                    "GAS/func_lib.json",
                    """
                    [
                      { "name": "script.yieldFunction", "graph": "Graph.Script.YieldFunction", "kind": "Script", "purity": "pure" }
                    ]
                    """);
                root = tempRoot;

                var functionCatalog = new GraphFunctionCatalog();
                var ex = Assert.Throws<AggregateException>(() =>
                    new GraphFunctionCatalogLoader(pipeline, functionCatalog, programs).Load(configCatalog));

                Assert.That(functionCatalog.Count, Is.EqualTo(0));
                Assert.That(ex!.InnerExceptions[0].Message, Does.Contain("contains Yield"));
                Assert.That(ex.InnerExceptions[0].Message, Does.Contain("ActionLib"));
            }
            finally
            {
                GraphIdRegistry.Clear();
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void FuncCatalogLoader_RejectsNonPurePurity()
        {
            GraphIdRegistry.Clear();
            string root = string.Empty;
            try
            {
                const string graphName = "Graph.Script.ImpureFunction";
                int graphId = GraphIdRegistry.Register(graphName);
                var programs = new GraphProgramRegistry();
                programs.Register(
                    graphId,
                    new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt } },
                    GraphKind.Script);

                var (pipeline, configCatalog, tempRoot) = CreateCatalogPipeline(
                    "GAS/func_lib.json",
                    """
                    [
                      { "name": "script.impure", "graph": "Graph.Script.ImpureFunction", "kind": "Script", "purity": "impure" }
                    ]
                    """);
                root = tempRoot;

                var functionCatalog = new GraphFunctionCatalog();
                var ex = Assert.Throws<AggregateException>(() =>
                    new GraphFunctionCatalogLoader(pipeline, functionCatalog, programs).Load(configCatalog));

                Assert.That(functionCatalog.Count, Is.EqualTo(0));
                Assert.That(ex!.InnerExceptions[0].Message, Does.Contain("purity 'impure' must be pure"));
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
                "Ludots_GraphFunctionCatalogLoaderTests",
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
