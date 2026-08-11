using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Objective compare: next-chain <see cref="GraphCompiler"/> vs pin-edge <see cref="GraphControlFlowCompiler"/>
    /// vs equivalent native C# for the same linear arithmetic. Reports costs; does not guess.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphAuthoringFormatPerfCompareTests
    {
        private const int WarmupIterations = 2_000;
        private const int MeasuredIterations = 50_000;

        [Test]
        public void Compare_LinearIntChain_NextChainVsControlFlow_InstructionShapeAndRuntime()
        {
            GraphConfig nextChainDoc = CreateNextChainLinearIntGraph();
            GraphControlFlowDocument controlFlowDoc = CreateControlFlowLinearIntGraph();

            var (nextPackage, _, nextDiags) = GraphCompiler.CompileWithOutputs(nextChainDoc);
            var (cfPackage, _, cfDiags) = GraphControlFlowCompiler.CompileWithOutputs(controlFlowDoc);

            Assert.That(nextPackage.HasValue, Is.True, FormatDiagnostics(nextDiags));
            Assert.That(cfPackage.HasValue, Is.True, FormatDiagnostics(cfDiags));

            GraphInstruction[] nextProgram = nextPackage!.Value.Program;
            GraphInstruction[] cfProgram = cfPackage!.Value.Program;

            int nextJumpCount = CountOp(nextProgram, GraphNodeOp.Jump);
            int cfJumpCount = CountOp(cfProgram, GraphNodeOp.Jump);
            int nextArithCount = CountOp(nextProgram, GraphNodeOp.ConstInt) + CountOp(nextProgram, GraphNodeOp.AddInt);
            int cfArithCount = CountOp(cfProgram, GraphNodeOp.ConstInt) + CountOp(cfProgram, GraphNodeOp.AddInt);

            // Same authored arithmetic: ConstInt x2 + AddInt x3. Script CF also needs HaltReturnInt terminal.
            Assert.That(nextArithCount, Is.EqualTo(5));
            Assert.That(cfArithCount, Is.EqualTo(5));
            Assert.That(CountOp(cfProgram, GraphNodeOp.HaltReturnInt), Is.EqualTo(1),
                "Script ControlFlow requires an explicit halt/return terminal.");
            Assert.That(cfJumpCount, Is.GreaterThan(nextJumpCount),
                "ControlFlow lowers controlEdges to Jump; linear next-chain should not need those Jumps.");
            Assert.That(NativeLinearIntChain(), Is.EqualTo(6), "Native baseline must match graph arithmetic ((1+2)+2)+1.");

            using var world = World.Create();
            Entity caster = world.Create();

            RuntimeSample nativeSample = MeasureNative(WarmupIterations, MeasuredIterations);
            RuntimeSample nextSample = MeasureExecute(world, caster, nextProgram, WarmupIterations, MeasuredIterations);
            RuntimeSample cfSample = MeasureExecute(world, caster, cfProgram, WarmupIterations, MeasuredIterations);

            TestContext.WriteLine("=== Graph authoring format compare (linear int chain) ===");
            TestContext.WriteLine("Native C# (same arithmetic, NoInlining):");
            TestContext.WriteLine($"  Result check: {NativeLinearIntChain()}");
            TestContext.WriteLine($"  Execute x{MeasuredIterations}: {nativeSample.ElapsedMs:F3} ms, {nativeSample.PerExecNs:F1} ns/exec, alloc={nativeSample.AllocatedBytes}");
            TestContext.WriteLine("Next-chain (GraphConfig + GraphCompiler, kind=Score):");
            TestContext.WriteLine($"  Instructions: {nextProgram.Length} (arith={nextArithCount}, Jump={nextJumpCount})");
            TestContext.WriteLine($"  Ops: {FormatOps(nextProgram)}");
            TestContext.WriteLine($"  Execute x{MeasuredIterations}: {nextSample.ElapsedMs:F3} ms, {nextSample.PerExecNs:F1} ns/exec, alloc={nextSample.AllocatedBytes}");
            TestContext.WriteLine("ControlFlow (GraphControlFlowDocument + GraphControlFlowCompiler, kind=Script):");
            TestContext.WriteLine($"  Instructions: {cfProgram.Length} (arith={cfArithCount}, Jump={cfJumpCount}, HaltReturnInt={CountOp(cfProgram, GraphNodeOp.HaltReturnInt)})");
            TestContext.WriteLine($"  Ops: {FormatOps(cfProgram)}");
            TestContext.WriteLine($"  Execute x{MeasuredIterations}: {cfSample.ElapsedMs:F3} ms, {cfSample.PerExecNs:F1} ns/exec, alloc={cfSample.AllocatedBytes}");
            TestContext.WriteLine(
                $"Delta vs native: Next/Native={nextSample.ElapsedMs / Math.Max(1e-9, nativeSample.ElapsedMs):F2}x, " +
                $"CF/Native={cfSample.ElapsedMs / Math.Max(1e-9, nativeSample.ElapsedMs):F2}x, " +
                $"CF/Next={cfSample.ElapsedMs / Math.Max(1e-9, nextSample.ElapsedMs):F3}x");
            TestContext.WriteLine(
                $"Delta instructions CF-Next={cfProgram.Length - nextProgram.Length}, Jump CF-Next={cfJumpCount - nextJumpCount}");

            // Timing/alloc are host-noisy; only report them. Structural Jump delta is the stable contract.
            Assert.That(cfProgram.Length, Is.GreaterThan(nextProgram.Length));
        }

        /// <summary>Same values as the graph: a=1, b=2, c=a+b, d=c+b, e=d+a → 6.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int NativeLinearIntChain()
        {
            int a = 1;
            int b = 2;
            int c = a + b;
            int d = c + b;
            int e = d + a;
            return e;
        }

        private static RuntimeSample MeasureNative(int warmupIterations, int measuredIterations)
        {
            int sink = 0;
            for (int i = 0; i < warmupIterations; i++)
            {
                sink ^= NativeLinearIntChain();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < measuredIterations; i++)
            {
                sink ^= NativeLinearIntChain();
            }

            sw.Stop();
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();

            // Keep sink live so the loop cannot be deleted.
            Assert.That(sink, Is.Not.EqualTo(int.MinValue));

            return new RuntimeSample(
                sw.Elapsed.TotalMilliseconds,
                sw.Elapsed.TotalMilliseconds * 1_000_000.0 / measuredIterations,
                allocAfter - allocBefore);
        }

        private static GraphConfig CreateNextChainLinearIntGraph()
        {
            // Kind=Score: GraphCompiler accepts it; ops are Pure. Same 5-node linear arithmetic as CF Script.
            return new GraphConfig
            {
                Id = "tests.compare.nextchain.linear-int",
                Kind = "Score",
                Entry = "a",
                Nodes =
                {
                    new GraphNodeConfig { Id = "a", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1, Next = "b" },
                    new GraphNodeConfig { Id = "b", Op = nameof(GraphNodeOp.ConstInt), IntValue = 2, Next = "c" },
                    new GraphNodeConfig
                    {
                        Id = "c",
                        Op = nameof(GraphNodeOp.AddInt),
                        Inputs = { "a", "b" },
                        Next = "d"
                    },
                    new GraphNodeConfig
                    {
                        Id = "d",
                        Op = nameof(GraphNodeOp.AddInt),
                        Inputs = { "c", "b" },
                        Next = "e"
                    },
                    new GraphNodeConfig
                    {
                        Id = "e",
                        Op = nameof(GraphNodeOp.AddInt),
                        Inputs = { "d", "a" }
                    }
                }
            };
        }

        private static GraphControlFlowDocument CreateControlFlowLinearIntGraph()
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.compare.controlflow.linear-int",
                Kind = "Script",
                Entry = "a",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "a", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1 },
                    new() { Id = "b", Op = nameof(GraphNodeOp.ConstInt), IntValue = 2 },
                    new() { Id = "c", Op = nameof(GraphNodeOp.AddInt) },
                    new() { Id = "d", Op = nameof(GraphNodeOp.AddInt) },
                    new() { Id = "e", Op = nameof(GraphNodeOp.AddInt) },
                    new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("a", GraphControlFlowPorts.Next, "b"),
                    new("b", GraphControlFlowPorts.Next, "c"),
                    new("c", GraphControlFlowPorts.Next, "d"),
                    new("d", GraphControlFlowPorts.Next, "e"),
                    new("e", GraphControlFlowPorts.Next, "halt")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("a", GraphControlFlowPorts.Value, "c", GraphControlFlowPorts.A),
                    new("b", GraphControlFlowPorts.Value, "c", GraphControlFlowPorts.B),
                    new("c", GraphControlFlowPorts.Value, "d", GraphControlFlowPorts.A),
                    new("b", GraphControlFlowPorts.Value, "d", GraphControlFlowPorts.B),
                    new("d", GraphControlFlowPorts.Value, "e", GraphControlFlowPorts.A),
                    new("a", GraphControlFlowPorts.Value, "e", GraphControlFlowPorts.B),
                    new("e", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value)
                }
            };
        }

        private static RuntimeSample MeasureExecute(
            World world,
            Entity caster,
            GraphInstruction[] program,
            int warmupIterations,
            int measuredIterations)
        {
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var targetList = new GraphTargetList(targets);

            for (int i = 0; i < warmupIterations; i++)
            {
                ExecuteOnce(world, caster, program, floats, ints, bools, entities, targets, targetList, callStack);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < measuredIterations; i++)
            {
                ExecuteOnce(world, caster, program, floats, ints, bools, entities, targets, targetList, callStack);
            }

            sw.Stop();
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();

            return new RuntimeSample(
                sw.Elapsed.TotalMilliseconds,
                sw.Elapsed.TotalMilliseconds * 1_000_000.0 / measuredIterations,
                allocAfter - allocBefore);
        }

        private static void ExecuteOnce(
            World world,
            Entity caster,
            GraphInstruction[] program,
            Span<float> floats,
            Span<int> ints,
            Span<byte> bools,
            Span<Entity> entities,
            Span<Entity> targets,
            GraphTargetList targetList,
            Span<int> callStack)
        {
            floats.Clear();
            ints.Clear();
            bools.Clear();
            entities.Clear();
            targets.Clear();
            callStack.Clear();
            entities[0] = caster;
            targetList.SetCount(0);

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack,
                CallStackCount = 0
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        }

        private static int CountOp(GraphInstruction[] program, GraphNodeOp op)
        {
            int count = 0;
            for (int i = 0; i < program.Length; i++)
            {
                if ((GraphNodeOp)program[i].Op == op)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FormatOps(GraphInstruction[] program)
            => string.Join(" -> ", program.Select(i => ((GraphNodeOp)i.Op).ToString()));

        private static string FormatDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
            => string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Code}:{d.NodeId}:{d.Message}"));

        private readonly struct RuntimeSample
        {
            public RuntimeSample(double elapsedMs, double perExecNs, long allocatedBytes)
            {
                ElapsedMs = elapsedMs;
                PerExecNs = perExecNs;
                AllocatedBytes = allocatedBytes;
            }

            public double ElapsedMs { get; }
            public double PerExecNs { get; }
            public long AllocatedBytes { get; }
        }
    }
}
