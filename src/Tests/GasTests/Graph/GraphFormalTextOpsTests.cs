using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphFormalTextOpsTests
    {
        [Test]
        public void ConstText_ConcatText_SinkPresentationText_PushesComposedSentence()
        {
            var sink = new GraphPresentationTextSink();
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindPresentationTextSink(sink);

            GraphControlFlowDocument doc = ScriptDoc(
                "Graph.FormalText.Concat",
                nodes: new[]
                {
                    Node("a", "ConstText", text: "守卫"),
                    Node("b", "ConstText", text: "倒下了"),
                    Node("c", "ConcatText"),
                    Node("sink", "SinkPresentationText", surface: "Subtitle"),
                    Node("halt", "HaltReturnInt"),
                },
                control: new[]
                {
                    Edge("a", "next", "b"),
                    Edge("b", "next", "c"),
                    Edge("c", "next", "sink"),
                    Edge("sink", "next", "halt"),
                },
                values: new[]
                {
                    Value("a", "value", "c", "a"),
                    Value("b", "value", "c", "b"),
                    Value("c", "value", "sink", "a"),
                });

            ExecuteScript(world, doc, api, out int graphId, out GraphProgramRegistry programs);

            That(sink.TryDequeue(out GraphPresentationTextSurface surface, out string text), Is.True);
            That(surface, Is.EqualTo(GraphPresentationTextSurface.Subtitle));
            That(text, Is.EqualTo("守卫倒下了"));
            That(programs.TryGetRegistration(graphId, out _), Is.True);
        }

        [Test]
        public void IntToText_And_FormatText_ComposeWithBracePin()
        {
            var sink = new GraphPresentationTextSink();
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindPresentationTextSink(sink);

            GraphControlFlowDocument doc = ScriptDoc(
                "Graph.FormalText.Format",
                nodes: new[]
                {
                    Node("n", "ConstInt", intValue: 3),
                    Node("t", "IntToText"),
                    Node("fmt", "FormatText", text: "击杀 {0}"),
                    Node("sink", "SinkPresentationText", surface: "Dialogue"),
                    Node("halt", "HaltReturnInt"),
                },
                control: new[]
                {
                    Edge("n", "next", "t"),
                    Edge("t", "next", "fmt"),
                    Edge("fmt", "next", "sink"),
                    Edge("sink", "next", "halt"),
                },
                values: new[]
                {
                    Value("n", "value", "t", "a"),
                    Value("t", "value", "fmt", "arg:0"),
                    Value("fmt", "value", "sink", "a"),
                });

            ExecuteScript(world, doc, api, out _, out _);

            That(sink.TryDequeue(out GraphPresentationTextSurface surface, out string text), Is.True);
            That(surface, Is.EqualTo(GraphPresentationTextSurface.Dialogue));
            That(text, Is.EqualTo("击杀 3"));
        }

        [Test]
        public void ConcatText_Overflow_FailsClosed()
        {
            string left = new string('甲', GraphVmLimits.MaxTextCharsPerRegister - 1);
            string right = "乙丙";
            GraphControlFlowDocument doc = ScriptDoc(
                "Graph.FormalText.Overflow",
                nodes: new[]
                {
                    Node("a", "ConstText", text: left),
                    Node("b", "ConstText", text: right),
                    Node("c", "ConcatText"),
                    Node("halt", "HaltReturnInt"),
                },
                control: new[]
                {
                    Edge("a", "next", "b"),
                    Edge("b", "next", "c"),
                    Edge("c", "next", "halt"),
                },
                values: new[]
                {
                    Value("a", "value", "c", "a"),
                    Value("b", "value", "c", "b"),
                });

            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindPresentationTextSink(new GraphPresentationTextSink());
            var ex = Throws<InvalidOperationException>(() => ExecuteScript(world, doc, api, out _, out _));
            That(ex!.Message, Does.Contain(GraphTextHeap.OverflowError));
        }

        [Test]
        public void FormatText_UnterminatedBrace_FailsClosedAtExpand()
        {
            GraphControlFlowDocument doc = ScriptDoc(
                "Graph.FormalText.BadTemplate",
                nodes: new[]
                {
                    Node("fmt", "FormatText", text: "坏掉的 {"),
                    Node("halt", "HaltReturnInt"),
                },
                control: new[] { Edge("fmt", "next", "halt") },
                values: Array.Empty<GraphControlFlowValueEdge>());

            var ex = Throws<InvalidOperationException>(() =>
                GraphControlFlowCompiler.Compile(doc, eventSchemas: null, enums: null));
            That(ex!.Message, Does.Contain(GraphFormalTextTemplate.ParseError));
        }

        [Test]
        public void DescriptorTable_ExposesFormalTextOps_ForScriptAndTriggerGraph()
        {
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.ConstText), Is.True);
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.ConcatText), Is.True);
            That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.SinkPresentationText), Is.True);
            That(GraphOpDescriptorTable.GetLinearOutputType(GraphNodeOp.ConstText), Is.EqualTo(GraphValueType.Text));
            That(GraphAuthoringSugar.FormatText, Is.EqualTo("FormatText"));
        }

        private static void ExecuteScript(
            World world,
            GraphControlFlowDocument doc,
            IGraphRuntimeApi api,
            out int graphId,
            out GraphProgramRegistry programs)
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc, eventSchemas: null, enums: null);
            That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
            That(compiled.Package, Is.Not.Null);

            programs = new GraphProgramRegistry();
            graphId = 42;
            programs.Register(
                graphId,
                compiled.Package!.Value.Program,
                GraphKind.Script,
                compiled.SourceMap,
                compiled.Package.Value.Symbols);

            var caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();
            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                world,
                caster,
                Entity.Null,
                default,
                compiled.Package.Value.Program,
                api,
                programs,
                floats,
                ints,
                bools,
                entities,
                targets,
                callStack,
                ref cursor,
                budgetSteps: 256,
                graphId: graphId);
            That(result.Halted, Is.True, "formal text script must halt");
        }

        private static GraphControlFlowDocument ScriptDoc(
            string id,
            GraphControlFlowNode[] nodes,
            GraphControlFlowEdge[] control,
            GraphControlFlowValueEdge[] values)
        {
            return new GraphControlFlowDocument
            {
                Id = id,
                Kind = "Script",
                Entry = nodes[0].Id,
                Nodes = new System.Collections.Generic.List<GraphControlFlowNode>(nodes),
                ControlEdges = new System.Collections.Generic.List<GraphControlFlowEdge>(control),
                ValueEdges = new System.Collections.Generic.List<GraphControlFlowValueEdge>(values),
            };
        }

        private static GraphControlFlowNode Node(
            string id,
            string op,
            string? text = null,
            string? surface = null,
            int intValue = 0)
        {
            return new GraphControlFlowNode
            {
                Id = id,
                Op = op,
                Text = text,
                PresentationSurface = surface,
                IntValue = intValue,
            };
        }

        private static GraphControlFlowEdge Edge(string from, string port, string to)
            => new(from, port, to);

        private static GraphControlFlowValueEdge Value(string from, string fromPort, string to, string toPort)
            => new(from, fromPort, to, toPort);
    }
}
