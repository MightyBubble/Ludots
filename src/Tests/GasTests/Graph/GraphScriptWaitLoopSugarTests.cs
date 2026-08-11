using System;
using System.Linq;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphScriptWaitLoopSugarTests
    {
        [Test]
        public void Compile_Wait_EmitsYieldOpcode()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateWaitOnceThenHaltGraph());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Yield));
        }

        [Test]
        public void ExecuteScriptSlice_Wait_YieldsThenResumesToHalt()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateWaitOnceThenHaltGraph(value: 9));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var cursor = new GraphExecutionCursor();

            GraphSliceResult first = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, compiled.Program, api: null!, programs: null,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Assert.That(first.Yielded, Is.True);
            Assert.That(first.Halted, Is.False);

            GraphSliceResult second = GraphExecutor.ExecuteScriptSlice(
                world, caster, Entity.Null, default, compiled.Program, api: null!, programs: null,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 64);
            Assert.That(second.Halted, Is.True);
            Assert.That(second.ReturnInt, Is.EqualTo(9));
        }

        [Test]
        public void GraphKindOperationPolicy_NonScriptKinds_RejectYield()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Yield }
            };

            Assert.DoesNotThrow(() =>
                GraphKindOperationPolicy.RequireAllowed(GraphKind.Script, program, GasGraphOpHandlerTable.Instance));

            foreach (GraphKind kind in new[]
                     {
                         GraphKind.Effect,
                         GraphKind.Query,
                         GraphKind.Score,
                         GraphKind.Validation,
                         GraphKind.Derived
                     })
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    GraphKindOperationPolicy.RequireAllowed(kind, program, GasGraphOpHandlerTable.Instance));
                Assert.That(ex!.Message, Does.Contain("Yield").Or.Contain("OperationNotAllowed"));
            }
        }

        [Test]
        public void GraphCompiler_EffectWithWait_FailsClosed()
        {
            var cfg = new GraphConfig
            {
                Id = "bad.effect.wait",
                Kind = "Effect",
                Entry = "wait",
                Nodes =
                {
                    new GraphNodeConfig
                    {
                        Id = "wait",
                        Op = GraphControlFlowCompiler.WaitOp
                    }
                }
            };

            var (pkg, _, diags) = GraphCompiler.CompileWithOutputs(cfg);
            Assert.That(pkg.HasValue, Is.False);
            Assert.That(diags, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Severity == GraphDiagnosticSeverity.Error &&
                d.Code == GraphDiagnosticCodes.UnknownNodeOp));
        }

        [Test]
        public void GraphCompiler_EffectWithYield_CompilesThenPolicyRejects()
        {
            var cfg = new GraphConfig
            {
                Id = "bad.effect.yield",
                Kind = "Effect",
                Entry = "y",
                Nodes =
                {
                    new GraphNodeConfig
                    {
                        Id = "y",
                        Op = nameof(GraphNodeOp.Yield)
                    }
                }
            };

            var (pkg, _, diags) = GraphCompiler.CompileWithOutputs(cfg);
            // Yield may compile as an opcode on next-chain; policy must still fail-closed for Effect.
            if (pkg.HasValue)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    GraphKindOperationPolicy.RequireAllowed(
                        GraphKind.Effect,
                        pkg.Value.Program,
                        GasGraphOpHandlerTable.Instance));
            }
            else
            {
                Assert.That(diags, Has.Some.Matches<GraphDiagnostic>(d =>
                    d.Severity == GraphDiagnosticSeverity.Error));
            }
        }

        [Test]
        public void CompileExecute_While_RunsNTimesThenExits()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateCountWhileGraph(limit: 3));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.JumpIfFalse));
            Assert.That(compiled.Program.Select(i => (GraphNodeOp)i.Op), Does.Contain(GraphNodeOp.Jump));
            // No While opcode exists — sugar only.
            Assert.That(
                compiled.Program.All(i => Enum.IsDefined(typeof(GraphNodeOp), (GraphNodeOp)i.Op)),
                Is.True);

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
                world, caster, Entity.Null, default, compiled.Program, api: null!, programs: null,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 256);
            Assert.That(result.Halted, Is.True);
            Assert.That(result.ReturnInt, Is.EqualTo(3));
        }

        [Test]
        public void CompileExecute_Until_LoopsUntilPredicateTrue()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateCountUntilGraph(limit: 3));
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

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
                world, caster, Entity.Null, default, compiled.Program, api: null!, programs: null,
                floats, ints, bools, entities, targets, callStack, ref cursor, budgetSteps: 256);
            Assert.That(result.Halted, Is.True);
            // until (limit < counter): body runs while false; exits when counter becomes 4
            Assert.That(result.ReturnInt, Is.EqualTo(4));
        }

        [Test]
        public void Execute_InfiniteWhile_HitsMaxInstructionsAndThrows()
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
                GraphScriptTestGraphs.CreateInfiniteWhileGraph());
            Assert.That(compiled.Succeeded, Is.True, GraphScriptTestGraphs.FormatDiagnostics(compiled.Diagnostics));

            using var world = World.Create();
            Entity caster = world.Create();
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            e[0] = caster;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = callStack
            };

            string? message = null;
            try
            {
                GasGraphOpHandlerTable.Execute(ref state, compiled.Program, GasGraphOpHandlerTable.Instance);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.That(message, Is.Not.Null);
            Assert.That(message, Does.Contain("MaxInstructionsPerExecution"));
        }

        [Test]
        public void Compile_QueryKind_RejectsWaitWhileAndUntil()
        {
            var waitDoc = new GraphControlFlowDocument
            {
                Id = "tests.query.wait-forbidden",
                Kind = "Query",
                Entry = "wait",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "wait", Op = GraphControlFlowCompiler.WaitOp }
                }
            };
            GraphControlFlowCompileResult waitCompiled = GraphControlFlowCompiler.Compile(waitDoc);
            Assert.That(waitCompiled.Succeeded, Is.False);
            Assert.That(waitCompiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp && d.NodeId == "wait"));

            var whileDoc = new GraphControlFlowDocument
            {
                Id = "tests.query.while-forbidden",
                Kind = "Query",
                Entry = "loop",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new GraphControlFlowNode { Id = "loop", Op = GraphControlFlowCompiler.WhileOp }
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("loop", GraphControlFlowPorts.Body, "one"),
                    new GraphControlFlowEdge("loop", GraphControlFlowPorts.Next, "one"),
                    new GraphControlFlowEdge("one", GraphControlFlowPorts.Next, "loop")
                }
            };
            GraphControlFlowCompileResult whileCompiled = GraphControlFlowCompiler.Compile(whileDoc);
            Assert.That(whileCompiled.Succeeded, Is.False);
            Assert.That(whileCompiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp && d.NodeId == "loop"));

            var untilDoc = new GraphControlFlowDocument
            {
                Id = "tests.query.until-forbidden",
                Kind = "Query",
                Entry = "until",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "one", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new GraphControlFlowNode { Id = "until", Op = GraphControlFlowCompiler.UntilOp }
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("until", GraphControlFlowPorts.Body, "one"),
                    new GraphControlFlowEdge("until", GraphControlFlowPorts.Next, "one"),
                    new GraphControlFlowEdge("one", GraphControlFlowPorts.Next, "until")
                }
            };
            GraphControlFlowCompileResult untilCompiled = GraphControlFlowCompiler.Compile(untilDoc);
            Assert.That(untilCompiled.Succeeded, Is.False);
            Assert.That(untilCompiled.Diagnostics, Has.Some.Matches<GraphDiagnostic>(d =>
                d.Code == GraphDiagnosticCodes.UnknownNodeOp && d.NodeId == "until"));
        }
    }
}
