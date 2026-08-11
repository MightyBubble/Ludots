using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBranchSwitchSugarTests
    {
        [Test]
        public void BranchBool_TruePath_ReturnsTrueArmValue()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateBranchBoolGraph(left: 1, right: 2));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(10));
        }

        [Test]
        public void BranchBool_FalsePath_ReturnsFalseArmValue()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateBranchBoolGraph(left: 2, right: 1));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(20));
        }

        [Test]
        public void SwitchInt_HitsMatchingCaseArm()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateSwitchIntGraph(selectorValue: 1));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.CompareEqInt));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(101));
        }

        [Test]
        public void SwitchInt_HitsDefaultWhenNoCaseMatches()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                CreateSwitchIntGraph(selectorValue: 99));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            Assert.That(ExecuteHaltReturn(compiled.Program), Is.EqualTo(900));
        }

        [Test]
        public void SwitchInt_MissingDefault_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ControlEdges.RemoveAll(e =>
                e.From == "sw" && e.FromPort == GraphControlFlowPorts.Default);

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge && d.NodeId == "sw"));
        }

        [Test]
        public void SwitchInt_NoCaseArms_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ControlEdges.RemoveAll(e =>
                e.From == "sw" && GraphControlFlowPorts.TryParseCasePort(e.FromPort, out _));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.MissingControlEdge &&
                d.NodeId == "sw" &&
                d.Message.Contains("case:", StringComparison.Ordinal)));
        }

        [Test]
        public void SwitchInt_MalformedCasePort_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            graph.ControlEdges.Add(new GraphControlFlowEdge("sw", "case:not-int", "ret0"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnexpectedControlEdge && d.NodeId == "sw"));
        }

        [Test]
        public void SwitchInt_DuplicateCaseValue_FailsClosed()
        {
            GraphControlFlowDocument graph = CreateSwitchIntGraph(selectorValue: 0);
            // Distinct port spellings that parse to the same int value.
            graph.ControlEdges.Add(new GraphControlFlowEdge("sw", "case:01", "retDefault"));

            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.False);
            Assert.That(compiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.DuplicateControlEdge &&
                d.NodeId == "sw" &&
                d.Message.Contains("case value 1", StringComparison.Ordinal)));
        }

        private static GraphControlFlowDocument CreateBranchBoolGraph(int left, int right)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.branch-bool-paths",
                Entry = "left",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "left", Op = nameof(GraphNodeOp.ConstInt), IntValue = left },
                    new() { Id = "right", Op = nameof(GraphNodeOp.ConstInt), IntValue = right },
                    new() { Id = "trueValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 10 },
                    new() { Id = "falseValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 20 },
                    new() { Id = "pred", Op = nameof(GraphNodeOp.CompareLtInt) },
                    new() { Id = "branch", Op = GraphControlFlowCompiler.BranchBoolOp },
                    new() { Id = "retTrue", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "retFalse", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("left", GraphControlFlowPorts.Next, "right"),
                    new("right", GraphControlFlowPorts.Next, "trueValue"),
                    new("trueValue", GraphControlFlowPorts.Next, "falseValue"),
                    new("falseValue", GraphControlFlowPorts.Next, "pred"),
                    new("pred", GraphControlFlowPorts.Next, "branch"),
                    new("branch", GraphControlFlowPorts.True, "retTrue"),
                    new("branch", GraphControlFlowPorts.False, "retFalse")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("left", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.A),
                    new("right", GraphControlFlowPorts.Value, "pred", GraphControlFlowPorts.B),
                    new("pred", GraphControlFlowPorts.Value, "branch", GraphControlFlowPorts.Condition),
                    new("trueValue", GraphControlFlowPorts.Value, "retTrue", GraphControlFlowPorts.Value),
                    new("falseValue", GraphControlFlowPorts.Value, "retFalse", GraphControlFlowPorts.Value)
                }
            };
        }

        private static GraphControlFlowDocument CreateSwitchIntGraph(int selectorValue)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.script.switch-int-arms",
                Entry = "retV0",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "retV0", Op = nameof(GraphNodeOp.ConstInt), IntValue = 100 },
                    new() { Id = "retV1", Op = nameof(GraphNodeOp.ConstInt), IntValue = 101 },
                    new() { Id = "retV2", Op = nameof(GraphNodeOp.ConstInt), IntValue = 102 },
                    new() { Id = "retVD", Op = nameof(GraphNodeOp.ConstInt), IntValue = 900 },
                    new() { Id = "sel", Op = nameof(GraphNodeOp.ConstInt), IntValue = selectorValue },
                    new() { Id = "sw", Op = GraphControlFlowCompiler.SwitchIntOp },
                    new() { Id = "ret0", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "ret1", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "ret2", Op = nameof(GraphNodeOp.HaltReturnInt) },
                    new() { Id = "retDefault", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("retV0", GraphControlFlowPorts.Next, "retV1"),
                    new("retV1", GraphControlFlowPorts.Next, "retV2"),
                    new("retV2", GraphControlFlowPorts.Next, "retVD"),
                    new("retVD", GraphControlFlowPorts.Next, "sel"),
                    new("sel", GraphControlFlowPorts.Next, "sw"),
                    new("sw", GraphControlFlowPorts.Case(0), "ret0"),
                    new("sw", GraphControlFlowPorts.Case(1), "ret1"),
                    new("sw", GraphControlFlowPorts.Case(2), "ret2"),
                    new("sw", GraphControlFlowPorts.Default, "retDefault")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("sel", GraphControlFlowPorts.Value, "sw", GraphControlFlowPorts.Selector),
                    new("retV0", GraphControlFlowPorts.Value, "ret0", GraphControlFlowPorts.Value),
                    new("retV1", GraphControlFlowPorts.Value, "ret1", GraphControlFlowPorts.Value),
                    new("retV2", GraphControlFlowPorts.Value, "ret2", GraphControlFlowPorts.Value),
                    new("retVD", GraphControlFlowPorts.Value, "retDefault", GraphControlFlowPorts.Value)
                }
            };
        }

        private static int ExecuteHaltReturn(GraphInstruction[] program)
        {
            var registry = new GraphProgramRegistry();
            registry.Register(1, program, GraphKind.Script);

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();

            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);

            Assert.That(result.Halted, Is.True);
            return result.ReturnInt;
        }
    }
}
