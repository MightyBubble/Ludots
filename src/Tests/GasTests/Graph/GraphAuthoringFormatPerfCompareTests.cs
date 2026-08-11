using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    public sealed class GraphAuthoringFormatPerfCompareTests
    {
        private const int WarmupIterations = 2_000;
        private const int MeasuredIterations = 50_000;

        // Volatile so native/script baselines cannot constant-fold the whole chain to `return 6`.
        private static volatile int OpaqueA = 1;
        private static volatile int OpaqueB = 2;

        [Test]
        [Category("ci-gate")]
        public void Compare_LinearIntChain_NextChainVsControlFlow_InstructionShapeAndRuntime()
        {
            CompiledLinearPrograms compiled = CompileLinearPrograms();
            using var world = World.Create();
            Entity caster = world.Create();

            RuntimeSample nativeSample = MeasureNative(WarmupIterations, MeasuredIterations);
            RuntimeSample nextSample = MeasureExecute(world, caster, compiled.NextProgram, WarmupIterations, MeasuredIterations);
            RuntimeSample cfSample = MeasureExecute(world, caster, compiled.ControlFlowProgram, WarmupIterations, MeasuredIterations);

            TestContext.WriteLine("=== Graph authoring format compare (linear int chain) ===");
            TestContext.WriteLine("Native C# (same arithmetic, NoInlining, opaque operands):");
            TestContext.WriteLine($"  Result check: {NativeLinearIntChain(OpaqueA, OpaqueB)}");
            WriteRuntimeLine(nativeSample);
            WriteCompiledProgramLines(compiled, nextSample, cfSample);
            TestContext.WriteLine(
                $"Delta vs native C#: Next={Ratio(nextSample, nativeSample):F2}x, CF={Ratio(cfSample, nativeSample):F2}x");
            TestContext.WriteLine(
                $"Delta instructions CF-Next={compiled.ControlFlowProgram.Length - compiled.NextProgram.Length}, " +
                $"Jump CF-Next={compiled.ControlFlowJumpCount - compiled.NextJumpCount}");

            Assert.That(compiled.ControlFlowProgram.Length, Is.GreaterThan(compiled.NextProgram.Length));
        }

        [Test]
        [Category("benchmark")]
        public void Compare_LinearIntChain_IncludesPythonAndNodeBaselines()
        {
            CompiledLinearPrograms compiled = CompileLinearPrograms();
            using var world = World.Create();
            Entity caster = world.Create();

            RuntimeSample nativeSample = MeasureNative(WarmupIterations, MeasuredIterations);
            RuntimeSample pythonSample = MeasureExternalScript(
                "Python3",
                ResolveExecutable("python3"),
                new[] { "-c", BuildPythonProbe(WarmupIterations, MeasuredIterations) });
            RuntimeSample nodeSample = MeasureExternalScript(
                "Node.js",
                ResolveExecutable("node"),
                new[] { "-e", BuildNodeProbe(WarmupIterations, MeasuredIterations) });
            RuntimeSample nextSample = MeasureExecute(world, caster, compiled.NextProgram, WarmupIterations, MeasuredIterations);
            RuntimeSample cfSample = MeasureExecute(world, caster, compiled.ControlFlowProgram, WarmupIterations, MeasuredIterations);

            TestContext.WriteLine("=== Graph authoring format compare + script baselines ===");
            TestContext.WriteLine("Native C#:");
            WriteRuntimeLine(nativeSample);
            TestContext.WriteLine("Python3 (in-process; excludes spawn):");
            WriteRuntimeLine(pythonSample);
            TestContext.WriteLine("Node.js (in-process; excludes spawn):");
            WriteRuntimeLine(nodeSample);
            WriteCompiledProgramLines(compiled, nextSample, cfSample);
            TestContext.WriteLine(
                $"Delta vs native C#: Python={Ratio(pythonSample, nativeSample):F2}x, Node={Ratio(nodeSample, nativeSample):F2}x, " +
                $"Next={Ratio(nextSample, nativeSample):F2}x, CF={Ratio(cfSample, nativeSample):F2}x");
            TestContext.WriteLine(
                $"Delta vs Python: Node={Ratio(nodeSample, pythonSample):F2}x, Next={Ratio(nextSample, pythonSample):F2}x, CF={Ratio(cfSample, pythonSample):F2}x");

            Assert.That(pythonSample.Result, Is.EqualTo(6));
            Assert.That(nodeSample.Result, Is.EqualTo(6));
        }

        private static CompiledLinearPrograms CompileLinearPrograms()
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

            Assert.That(nextArithCount, Is.EqualTo(5));
            Assert.That(cfArithCount, Is.EqualTo(5));
            Assert.That(CountOp(cfProgram, GraphNodeOp.HaltReturnInt), Is.EqualTo(1));
            Assert.That(cfJumpCount, Is.GreaterThan(nextJumpCount));
            Assert.That(NativeLinearIntChain(OpaqueA, OpaqueB), Is.EqualTo(6));

            return new CompiledLinearPrograms(nextProgram, cfProgram, nextJumpCount, cfJumpCount, nextArithCount, cfArithCount);
        }

        private static void WriteCompiledProgramLines(
            CompiledLinearPrograms compiled,
            RuntimeSample nextSample,
            RuntimeSample cfSample)
        {
            TestContext.WriteLine("Next-chain (GraphConfig + GraphCompiler, kind=Score):");
            TestContext.WriteLine(
                $"  Instructions: {compiled.NextProgram.Length} (arith={compiled.NextArithCount}, Jump={compiled.NextJumpCount})");
            TestContext.WriteLine($"  Ops: {FormatOps(compiled.NextProgram)}");
            WriteRuntimeLine(nextSample);
            TestContext.WriteLine("ControlFlow (GraphControlFlowDocument + GraphControlFlowCompiler, kind=Script):");
            TestContext.WriteLine(
                $"  Instructions: {compiled.ControlFlowProgram.Length} (arith={compiled.ControlFlowArithCount}, Jump={compiled.ControlFlowJumpCount}, HaltReturnInt={CountOp(compiled.ControlFlowProgram, GraphNodeOp.HaltReturnInt)})");
            TestContext.WriteLine($"  Ops: {FormatOps(compiled.ControlFlowProgram)}");
            WriteRuntimeLine(cfSample);
        }

        private readonly struct CompiledLinearPrograms
        {
            public CompiledLinearPrograms(
                GraphInstruction[] nextProgram,
                GraphInstruction[] controlFlowProgram,
                int nextJumpCount,
                int controlFlowJumpCount,
                int nextArithCount,
                int controlFlowArithCount)
            {
                NextProgram = nextProgram;
                ControlFlowProgram = controlFlowProgram;
                NextJumpCount = nextJumpCount;
                ControlFlowJumpCount = controlFlowJumpCount;
                NextArithCount = nextArithCount;
                ControlFlowArithCount = controlFlowArithCount;
            }

            public GraphInstruction[] NextProgram { get; }
            public GraphInstruction[] ControlFlowProgram { get; }
            public int NextJumpCount { get; }
            public int ControlFlowJumpCount { get; }
            public int NextArithCount { get; }
            public int ControlFlowArithCount { get; }
        }

        private static void WriteRuntimeLine(RuntimeSample sample)
        {
            TestContext.WriteLine(
                $"  Execute x{MeasuredIterations}: {sample.ElapsedMs:F3} ms, {sample.PerExecNs:F1} ns/exec, alloc={sample.AllocatedBytes}");
        }

        private static double Ratio(RuntimeSample numerator, RuntimeSample denominator)
            => numerator.ElapsedMs / Math.Max(1e-9, denominator.ElapsedMs);

        /// <summary>Same values as the graph: a=1, b=2, c=a+b, d=c+b, e=d+a → 6.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int NativeLinearIntChain(int a, int b)
        {
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
                sink ^= NativeLinearIntChain(OpaqueA, OpaqueB);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < measuredIterations; i++)
            {
                sink ^= NativeLinearIntChain(OpaqueA, OpaqueB);
            }

            sw.Stop();
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();

            // Keep sink live so the loop cannot be deleted.
            Assert.That(sink, Is.Not.EqualTo(int.MinValue));

            return new RuntimeSample(
                NativeLinearIntChain(OpaqueA, OpaqueB),
                sw.Elapsed.TotalMilliseconds,
                sw.Elapsed.TotalMilliseconds * 1_000_000.0 / measuredIterations,
                allocAfter - allocBefore);
        }

        private static string ResolveExecutable(string name)
        {
            string? path = FindOnPath(name);
            if (string.IsNullOrWhiteSpace(path))
            {
                Assert.Fail($"Required script runtime '{name}' was not found on PATH.");
            }

            return path!;
        }

        private static string? FindOnPath(string name)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
            {
                return null;
            }

            foreach (string directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string BuildPythonProbe(int warmupIterations, int measuredIterations)
            => string.Join(
                "\n",
                "import time",
                "ops = [1, 2]  # list read keeps operands opaque to constant folding",
                "def chain():",
                "    a = ops[0]",
                "    b = ops[1]",
                "    c = a + b",
                "    d = c + b",
                "    e = d + a",
                "    return e",
                "assert chain() == 6",
                $"warmup = {warmupIterations}",
                $"measured = {measuredIterations}",
                "sink = 0",
                "for _ in range(warmup):",
                "    sink ^= chain()",
                "t0 = time.perf_counter()",
                "for _ in range(measured):",
                "    sink ^= chain()",
                "elapsed_ms = (time.perf_counter() - t0) * 1000.0",
                "per_exec_ns = elapsed_ms * 1_000_000.0 / measured",
                "print(f'result={chain()} elapsed_ms={elapsed_ms:.6f} per_exec_ns={per_exec_ns:.3f} sink={sink}')");

        private static string BuildNodeProbe(int warmupIterations, int measuredIterations)
            => string.Join(
                "\n",
                "const ops = [1, 2]; // array read keeps operands opaque to constant folding",
                "function chain() {",
                "  const a = ops[0];",
                "  const b = ops[1];",
                "  const c = a + b;",
                "  const d = c + b;",
                "  const e = d + a;",
                "  return e;",
                "}",
                "if (chain() !== 6) throw new Error('native script result mismatch');",
                $"const warmup = {warmupIterations};",
                $"const measured = {measuredIterations};",
                "let sink = 0;",
                "for (let i = 0; i < warmup; i++) sink ^= chain();",
                "const t0 = performance.now();",
                "for (let i = 0; i < measured; i++) sink ^= chain();",
                "const elapsed_ms = performance.now() - t0;",
                "const per_exec_ns = elapsed_ms * 1_000_000.0 / measured;",
                "console.log(`result=${chain()} elapsed_ms=${elapsed_ms.toFixed(6)} per_exec_ns=${per_exec_ns.toFixed(3)} sink=${sink}`);");

        private static RuntimeSample MeasureExternalScript(string label, string executable, string[] args)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            for (int i = 0; i < args.Length; i++)
            {
                start.ArgumentList.Add(args[i]);
            }

            using var process = Process.Start(start);
            Assert.That(process, Is.Not.Null, $"{label}: failed to start '{executable}'.");
            string stdout = process!.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.EqualTo(0), $"{label} exited {process.ExitCode}. stderr={stderr}\nstdout={stdout}");

            Match match = Regex.Match(
                stdout,
                @"result=(?<result>-?\d+)\s+elapsed_ms=(?<ms>[0-9.]+)\s+per_exec_ns=(?<ns>[0-9.]+)");
            Assert.That(match.Success, Is.True, $"{label}: could not parse timing line from stdout:\n{stdout}");

            int result = int.Parse(match.Groups["result"].Value, CultureInfo.InvariantCulture);
            double elapsedMs = double.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture);
            double perExecNs = double.Parse(match.Groups["ns"].Value, CultureInfo.InvariantCulture);
            return new RuntimeSample(result, elapsedMs, perExecNs, allocatedBytes: -1);
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
                result: 0,
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
            public RuntimeSample(int result, double elapsedMs, double perExecNs, long allocatedBytes)
            {
                Result = result;
                ElapsedMs = elapsedMs;
                PerExecNs = perExecNs;
                AllocatedBytes = allocatedBytes;
            }

            public int Result { get; }
            public double ElapsedMs { get; }
            public double PerExecNs { get; }
            public long AllocatedBytes { get; }
        }
    }
}
