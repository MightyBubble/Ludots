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
    public sealed class GraphScriptControlFlowTests
    {
        [Test]
        public void CompileRegisterSlice_DrinkUntilFull_YieldsThenReturnsThree()
        {
            GraphControlFlowDocument graph = GraphScriptTestGraphs.CreateDrinkUntilFullGraph();
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(graph);
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Call));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Return));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Yield));

            var registry = new GraphProgramRegistry();
            registry.Register(7, compiled.Program, GraphKind.Script, compiled.SourceMap);
            Assert.That(registry.RequireKind(7, GraphKind.Script), Is.EqualTo(GraphKind.Script));
            Assert.That(registry.TryGetSourceMap(7, out GraphInstructionSourceMap sourceMap), Is.True);

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();
            var tracedNodes = new List<string>();

            GraphSliceResult first = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, compiled.Program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Trace(sourceMap, cursor, tracedNodes);
            Assert.That(first.Yielded, Is.True);
            Assert.That(cursor.CallStackCount, Is.EqualTo(1));

            GraphSliceResult second = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, compiled.Program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Trace(sourceMap, cursor, tracedNodes);
            Assert.That(second.Yielded, Is.True);

            GraphSliceResult third = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, compiled.Program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Trace(sourceMap, cursor, tracedNodes);
            Assert.That(third.Yielded, Is.True);

            GraphSliceResult last = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, compiled.Program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Trace(sourceMap, cursor, tracedNodes);
            Assert.That(last.Halted, Is.True);
            Assert.That(last.ReturnInt, Is.EqualTo(3));
            Assert.That(cursor.CallStackCount, Is.EqualTo(0));
            Assert.That(tracedNodes, Does.Contain("drinkYield"));
        }

        [Test]
        public void InvokeScript_RunsHaltOnlyCallee_WritesReturnInt()
        {
            GraphControlFlowCompileResult callee = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateHaltOnlyScript(11));
            Assert.That(callee.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(callee.Diagnostics));

            var callerDoc = new GraphControlFlowDocument
            {
                Id = "tests.script.invoke-caller",
                Entry = "invoke",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "invoke", Op = nameof(GraphNodeOp.InvokeScript), GraphId = 21 },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("invoke", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("invoke", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };

            GraphControlFlowCompileResult caller = GraphControlFlowCompiler.Compile(callerDoc);
            Assert.That(caller.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(caller.Diagnostics));

            var registry = new GraphProgramRegistry();
            registry.Register(21, callee.Program, GraphKind.Script);
            registry.Register(22, caller.Program, GraphKind.Script);

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
                world, caster, Entity.Null, default, caller.Program, api: null!, registry,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);

            Assert.That(result.Halted, Is.True);
            Assert.That(result.ReturnInt, Is.EqualTo(11));
        }

        [Test]
        public void InvokeScript_RejectsCalleeThatContainsYield()
        {
            GraphControlFlowCompileResult yielding = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateDrinkUntilFullGraph());
            Assert.That(yielding.Succeeded, Is.True);

            var callerDoc = new GraphControlFlowDocument
            {
                Id = "tests.script.invoke-yield-callee",
                Entry = "invoke",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "invoke", Op = nameof(GraphNodeOp.InvokeScript), GraphId = 31 },
                    new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("invoke", GraphControlFlowPorts.Next, "done")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("invoke", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value)
                }
            };
            GraphControlFlowCompileResult caller = GraphControlFlowCompiler.Compile(callerDoc);
            Assert.That(caller.Succeeded, Is.True);

            var registry = new GraphProgramRegistry();
            registry.Register(31, yielding.Program, GraphKind.Script);

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();

            string? message = null;
            try
            {
                GraphExecutor.ExecuteScriptSlice(
                    world, caster, Entity.Null, default, caller.Program, api: null!, registry,
                    floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("Yield"));
        }

        [Test]
        public void GraphKindOperationPolicy_EffectRejectsYield_ScriptAllowsYield()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Yield }
            };

            Assert.Throws<InvalidOperationException>(() =>
                GraphKindOperationPolicy.RequireAllowed(GraphKind.Effect, program, GasGraphOpHandlerTable.Instance));
            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(GraphKind.Script, program, GasGraphOpHandlerTable.Instance));
        }

        private static void Trace(
            GraphInstructionSourceMap sourceMap,
            in GraphExecutionCursor cursor,
            List<string> tracedNodes)
        {
            if (sourceMap.TryGetSource(Math.Max(0, cursor.Pc - 1), out GraphInstructionSource source) ||
                sourceMap.TryGetSource(cursor.Pc, out source))
            {
                tracedNodes.Add(source.NodeId);
            }
        }
    }
}
