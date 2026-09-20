using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1116 InvokeGraph: StoreArg* staging → subgraph LoadEntryPayload* reads →
    /// HaltReturnInt return value; entry label selection (explicit / default [0] /
    /// unknown fail closed); registration guards (kind mismatch, A↔B cycle, unknown
    /// entry label); runtime guards (subgraph Yield, invoke depth, staging consumed
    /// by the call); compile-side encoding of the entry label symbol.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphInvokeGraphTests
    {
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

        // ── Runtime: staging → subgraph read → return ──

        [Test]
        public void StoreArg_Int_ReachesSubgraph_AndHaltReturnIntFlowsBack()
        {
            int argKeyId = ConfigKeyRegistry.Register("Probe.Invoke.Arg.Int");
            var programs = new GraphProgramRegistry();
            int subId = RegisterArgReader(programs, "Graph.Probe.Invoke.Sub", argKeyId);
            int callerId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.Caller",
                new[]
                {
                    Ins(GraphNodeOp.ConstInt, dst: 1, imm: 42),
                    Ins(GraphNodeOp.StoreArgInt, a: 1, imm: argKeyId),
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            int result = ExecuteToHalt(programs, callerId);
            Assert.That(result, Is.EqualTo(42), "the subgraph must read the staged argument and return it");
        }

        [Test]
        public void StoreArg_FloatAndEntity_ReachSubgraph()
        {
            int floatKeyId = ConfigKeyRegistry.Register("Probe.Invoke.Arg.Float");
            int entityKeyId = ConfigKeyRegistry.Register("Probe.Invoke.Arg.Entity");
            var programs = new GraphProgramRegistry();

            GraphInstruction[] sub =
            {
                Ins(GraphNodeOp.LoadEntryPayloadFloat, dst: 0, imm: floatKeyId),
                Ins(GraphNodeOp.HaltReturnInt, a: 0)
            };
            int subId = RegisterTriggerGraph(programs, "Graph.Probe.Invoke.FloatSub", sub);

            GraphInstruction[] caller =
            {
                Ins(GraphNodeOp.ConstFloat, dst: 1, immF: 2.5f),
                Ins(GraphNodeOp.StoreArgFloat, a: 1, imm: floatKeyId),
                Ins(GraphNodeOp.StoreArgEntity, a: 2, imm: entityKeyId),
                Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId),
                Ins(GraphNodeOp.HaltReturnInt, a: 0)
            };
            int callerId = RegisterTriggerGraph(programs, "Graph.Probe.Invoke.FloatCaller", caller);

            Assert.DoesNotThrow(() => ExecuteToHalt(programs, callerId),
                "entity staging rides along even when the subgraph only reads the float");
        }

        [Test]
        public void MissingStoreArg_SubgraphReadFailsClosed()
        {
            int argKeyId = ConfigKeyRegistry.Register("Probe.Invoke.Missing.Int");
            var programs = new GraphProgramRegistry();
            int subId = RegisterArgReader(programs, "Graph.Probe.Invoke.MissingSub", argKeyId);
            int callerId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.MissingCaller",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteToHalt(programs, callerId));
            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.EntryPayload"),
                "an unstaged key must fail closed at first read, never default to zero");
        }

        [Test]
        public void InvokeGraph_ConsumesStaging_SecondCallWithoutStoreArgFailsClosed()
        {
            int argKeyId = ConfigKeyRegistry.Register("Probe.Invoke.Consume.Int");
            var programs = new GraphProgramRegistry();
            int readerId = RegisterArgReader(programs, "Graph.Probe.Invoke.ConsumeSub", argKeyId);
            int callerId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.ConsumeCaller",
                new[]
                {
                    Ins(GraphNodeOp.ConstInt, dst: 1, imm: 7),
                    Ins(GraphNodeOp.StoreArgInt, a: 1, imm: argKeyId),
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: readerId),
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: readerId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteToHalt(programs, callerId));
            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.EntryPayloadKeyNotCarried"),
                "InvokeGraph must clear the staging table after the call; anonymous leftover payloads are forbidden");
        }

        // ── Entry selection ──

        [Test]
        public void InvokeGraph_WithoutLabel_RunsEntryZero()
        {
            var programs = new GraphProgramRegistry();
            int subId = RegisterTwoEntrySubgraph(programs, "Graph.Probe.Invoke.TwoEntries");
            int callerId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.NoLabelCaller",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            Assert.That(ExecuteToHalt(programs, callerId), Is.EqualTo(1));
        }

        [Test]
        public void InvokeGraph_ExplicitLabel_SelectsThatEntry()
        {
            var programs = new GraphProgramRegistry();
            int subId = RegisterTwoEntrySubgraph(programs, "Graph.Probe.Invoke.TwoEntries.Label");
            int callerId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.LabelCaller",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId, flags: 2),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                },
                symbols: new[] { "alt" });

            Assert.That(ExecuteToHalt(programs, callerId), Is.EqualTo(2));
        }

        [Test]
        public void InvokeGraph_UnknownLabel_FailsClosedAtRegistration()
        {
            var programs = new GraphProgramRegistry();
            int subId = RegisterTwoEntrySubgraph(programs, "Graph.Probe.Invoke.TwoEntries.Ghost");
            var ex = Assert.Throws<InvalidOperationException>(() => RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.GhostCaller",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId, flags: 2),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                },
                symbols: new[] { "ghost" }));

            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.InvokeGraphEntryNotFound").Or.Contain("not an entry"));
        }

        // ── Registration guards ──

        [Test]
        public void InvokeGraph_CycleAcrossGraphs_IsRejectedAtRegistration()
        {
            int graphA = GraphIdRegistry.Register("Graph.Probe.Invoke.Cycle.A");
            int graphB = GraphIdRegistry.Register("Graph.Probe.Invoke.Cycle.B");
            var programs = new GraphProgramRegistry();

            RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.Cycle.A",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: graphB),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            var ex = Assert.Throws<InvalidOperationException>(() => RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.Cycle.B",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: graphA),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                }));

            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.InvokeCycle"),
                "InvokeGraph cycles must be named at load time, same error path as InvokeScript");
            Assert.That(programs.TryGetProgram(graphB, out _), Is.False);
        }

        [Test]
        public void InvokeGraph_ScriptKindTarget_IsRejectedAtRegistration()
        {
            int scriptId = GraphIdRegistry.Register("Graph.Probe.Invoke.NotTrigger");
            var programs = new GraphProgramRegistry();
            programs.Register(
                scriptId,
                new[]
                {
                    Ins(GraphNodeOp.ConstInt, dst: 0, imm: 1),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                },
                GraphKind.Script);

            var ex = Assert.Throws<InvalidOperationException>(() => RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.KindCaller",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: scriptId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                }));

            Assert.That(ex!.Message, Does.Contain("must be TriggerGraph"));
        }

        [Test]
        public void InvokeGraph_NestedThreeLevels_PassesArgumentThrough()
        {
            int argKeyId = ConfigKeyRegistry.Register("Probe.Invoke.Deep.Int");
            var programs = new GraphProgramRegistry();
            int leaf = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.Deep.Leaf",
                new[]
                {
                    Ins(GraphNodeOp.LoadEntryPayloadInt, dst: 0, imm: argKeyId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            int middle = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.Deep.Middle",
                new[]
                {
                    Ins(GraphNodeOp.LoadEntryPayloadInt, dst: 1, imm: argKeyId),
                    Ins(GraphNodeOp.StoreArgInt, a: 1, imm: argKeyId),
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: leaf),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            int root = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.Deep.Root",
                new[]
                {
                    Ins(GraphNodeOp.ConstInt, dst: 1, imm: 9),
                    Ins(GraphNodeOp.StoreArgInt, a: 1, imm: argKeyId),
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: middle),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            Assert.That(ExecuteToHalt(programs, root), Is.EqualTo(9));
        }

        [Test]
        public void InvokeGraph_DepthOverLimit_ThrowsAtRuntime()
        {
            int graphCount = GraphVmLimits.MaxInvokeDepth + 2;
            int[] ids = new int[graphCount];
            for (int i = 0; i < graphCount; i++)
            {
                ids[i] = GraphIdRegistry.Register($"Graph.Probe.Invoke.Depth.{i}");
            }

            var programs = new GraphProgramRegistry();
            for (int i = 0; i < graphCount; i++)
            {
                GraphInstruction[] program = i == graphCount - 1
                    ? new[]
                    {
                        Ins(GraphNodeOp.ConstInt, dst: 0, imm: 1),
                        Ins(GraphNodeOp.HaltReturnInt, a: 0)
                    }
                    : new[]
                    {
                        Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: ids[i + 1]),
                        Ins(GraphNodeOp.HaltReturnInt, a: 0)
                    };
                RegisterTriggerGraph(programs, $"Graph.Probe.Invoke.Depth.{i}", program);
            }

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteToHalt(programs, ids[0]));
            Assert.That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.InvokeDepthExceeded"));
        }

        [Test]
        public void InvokeGraph_SubgraphContainingYield_IsRejectedAtRuntime()
        {
            var programs = new GraphProgramRegistry();
            int subId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.YieldSub",
                new[]
                {
                    Ins(GraphNodeOp.ConstInt, dst: 0, imm: 5),
                    Ins(GraphNodeOp.Yield),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });
            int callerId = RegisterTriggerGraph(
                programs,
                "Graph.Probe.Invoke.YieldCaller",
                new[]
                {
                    Ins(GraphNodeOp.InvokeGraph, dst: 0, imm: subId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });

            var ex = Assert.Throws<InvalidOperationException>(() => ExecuteToHalt(programs, callerId));
            Assert.That(ex!.Message, Does.Contain("contains Yield"),
                "a suspended subgraph cannot ride the synchronous InvokeGraph frame");
        }

        // ── Compile side ──

        [Test]
        public void Compile_InvokeGraph_EncodesGraphIdAndLabelSymbol()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "Graph.Probe.Invoke.Compile",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_map_loaded", Event = "MapLoaded", Start = "call" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "call", Op = "InvokeGraph", GraphId = 77, EntryLabel = "main" },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge> { new("call", "next", "done") },
            };

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList(), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));

            GraphProgramPackage package = result.Package!.Value;
            GraphInstruction invoke = package.Program.Single(i => i.Op == (ushort)GraphNodeOp.InvokeGraph);
            Assert.That(invoke.Imm, Is.EqualTo(77));
            Assert.That(invoke.Flags & 2, Is.EqualTo(2), "an authored entry label must set the label flag (bit 1)");
            Assert.That(invoke.Flags & GraphInstructionFlags.FuncLibName, Is.EqualTo(0), "a literal graphId must not carry the functionName flag");
            int symbolIndex = invoke.B | (invoke.C << 8);
            Assert.That(package.Symbols[symbolIndex], Is.EqualTo("main"));
        }

        [TestCase(0, "graphId")]
        [TestCase(-3, "graphId")]
        public void Compile_InvokeGraph_WithoutPositiveGraphId_FailsClosed(int graphId, string fieldName)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "Graph.Probe.Invoke.Compile.Bad",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_map_loaded", Event = "MapLoaded", Start = "call" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "call", Op = "InvokeGraph", GraphId = graphId },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge> { new("call", "next", "done") },
            };

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains(fieldName)), Is.True,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void Compile_StoreArg_RequiresArgKeyAndValueEdge()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "Graph.Probe.StoreArg.Compile",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_map_loaded", Event = "MapLoaded", Start = "store" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "store", Op = "StoreArgInt", ArgKey = "Probe.Store.Key" },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge> { new("store", "next", "done") },
            };

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(
                result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("value")),
                Is.True,
                () => "StoreArgInt without a value edge must fail compile: " +
                      string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        }

        // ── Helpers ──

        private static GraphInstruction Ins(GraphNodeOp op, byte dst = 0, byte a = 0, int imm = 0, byte flags = 0, float immF = 0f)
        {
            return new GraphInstruction { Op = (ushort)op, Dst = dst, A = a, Imm = imm, Flags = flags, ImmF = immF };
        }

        private static int RegisterTriggerGraph(
            GraphProgramRegistry registry,
            string graphKey,
            GraphInstruction[] program,
            string[]? symbols = null,
            TriggerGraphEntry[]? entries = null)
        {
            int graphId = GraphIdRegistry.GetId(graphKey);
            if (graphId <= 0)
            {
                graphId = GraphIdRegistry.Register(graphKey);
            }

            TriggerGraphEntry[] table = entries ?? new[] { new TriggerGraphEntry("main", "MapLoaded", 0, once: false) };
            registry.Register(
                graphId,
                program,
                GraphKind.TriggerGraph,
                GraphInstructionSourceMap.Empty,
                symbols ?? Array.Empty<string>(),
                table);
            return graphId;
        }

        private static int RegisterArgReader(GraphProgramRegistry registry, string graphKey, int argKeyId)
        {
            return RegisterTriggerGraph(
                registry,
                graphKey,
                new[]
                {
                    Ins(GraphNodeOp.LoadEntryPayloadInt, dst: 0, imm: argKeyId),
                    Ins(GraphNodeOp.HaltReturnInt, a: 0)
                });
        }

        private static int RegisterTwoEntrySubgraph(GraphProgramRegistry registry, string graphKey)
        {
            GraphInstruction[] program =
            {
                Ins(GraphNodeOp.ConstInt, dst: 0, imm: 1),
                Ins(GraphNodeOp.HaltReturnInt, a: 0),
                Ins(GraphNodeOp.ConstInt, dst: 0, imm: 2),
                Ins(GraphNodeOp.HaltReturnInt, a: 0)
            };
            TriggerGraphEntry[] entries =
            {
                new("main", "MapLoaded", 0, once: false),
                new("alt", "MapLoaded", 2, once: false),
            };
            return RegisterTriggerGraph(registry, graphKey, program, entries: entries);
        }

        private static int ExecuteToHalt(GraphProgramRegistry registry, int rootGraphId)
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;
            e[2] = caster;

            Assert.That(registry.TryGetProgram(rootGraphId, out ReadOnlySpan<GraphInstruction> root), Is.True);
            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                Programs = registry,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };

            GasGraphOpHandlerTable.Execute(ref state, root, GasGraphOpHandlerTable.Instance);
            return state.ReturnInt;
        }
    }
}
