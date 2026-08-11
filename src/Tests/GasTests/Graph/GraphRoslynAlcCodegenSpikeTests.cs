using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Tests.Gas.Graph.Codegen;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #860 R0 spike: linear int graph → C# codegen → Roslyn → Collectible ALC → hot swap,
    /// with fail-closed compile errors and native/interpret/codegen microbench reporting.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphRoslynAlcCodegenSpikeTests
    {
        private const int ExpectedLinearResult = 6;

        [Test]
        public void Codegen_LinearIntChain_MatchesInterpretVm()
        {
            GraphInstruction[] program = BuildLinearIntChainProgram();
            int interpret = ExecuteInterpret(program);

            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(program, "linear-v1");
            int codegen = ExecuteCodegen(execute);

            That(interpret, Is.EqualTo(ExpectedLinearResult));
            That(codegen, Is.EqualTo(ExpectedLinearResult));
            That(host.ActiveAssemblyMarker, Is.EqualTo("linear-v1"));
            That(host.ActiveSource, Does.Contain("state.I["));
        }

        [Test]
        public void Codegen_FromGraphConfig_MatchesCompilerIr()
        {
            GraphConfig cfg = BuildLinearIntChainConfig();
            var (package, diagnostics) = GraphCompiler.Compile(cfg);
            That(diagnostics.Exists(d => d.Severity == GraphDiagnosticSeverity.Error), Is.False);
            That(package, Is.Not.Null);

            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(package!.Value.Program, "from-config");
            That(ExecuteCodegen(execute), Is.EqualTo(ExecuteInterpret(package.Value.Program)));
        }

        [Test]
        public void HotReload_ReplacesEntrypoint_AndPreviousAlcCanUnload()
        {
            GraphInstruction[] programA =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 30 },
            };
            GraphInstruction[] programB =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 7 },
            };

            WeakReference weakAssembly = LoadHotReloadAndDrop(programA, programB);

            for (int i = 0; i < 16 && weakAssembly.IsAlive; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }

            That(weakAssembly.IsAlive, Is.False,
                "Collectible ALC assembly must unload after the host drops the execute entry and no delegate roots remain.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference LoadHotReloadAndDrop(
            GraphInstruction[] programA,
            GraphInstruction[] programB)
        {
            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute first = host.CompileAndActivate(programA, "sum-all");
            That(ExecuteCodegenRegister(first, register: 0), Is.EqualTo(30));

            GraphGeneratedExecute second = host.CompileAndActivate(programB, "rare-only");
            That(ExecuteCodegenRegister(second, register: 0), Is.EqualTo(7));
            That(host.ActiveAssemblyMarker, Is.EqualTo("rare-only"));

            // Do not keep local delegate roots across the unload probe frame.
            return host.DropActiveForUnloadProbe();
        }

        [Test]
        public void CompileFailure_FailClosed_KeepsPreviousEntrypoint_NoInterpreterFallback()
        {
            GraphInstruction[] goodProgram =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 42 },
            };

            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute good = host.CompileAndActivate(goodProgram, "good");
            That(ExecuteCodegenRegister(good, register: 0), Is.EqualTo(42));
            string? previousMarker = host.ActiveAssemblyMarker;

            string poisonedSource = """
                using Ludots.Core.NodeLibraries.GASGraph;
                namespace Ludots.Graph.Generated;
                public static class GraphEntry
                {
                    public const string AssemblyMarker = "poison";
                    public static void Execute(ref GraphExecutionState state)
                    {
                        // Unwhitelisted API: not provided in R0 metadata references → compile must fail.
                        _ = UnwhitelistedWorldScanner.TouchEverything();
                        state.I[0] = 99;
                    }
                    public static int ExecuteLinearInt() => 99;
                }
                """;

            var failure = Throws<GraphRoslynCompileFailureException>(() =>
                host.CompileSourceAndActivate(poisonedSource, "poison"));

            That(failure!.Diagnostics, Is.Not.Empty);
            That(host.ActiveAssemblyMarker, Is.EqualTo(previousMarker));
            That(ExecuteCodegenRegister(host.ActiveExecute!, register: 0), Is.EqualTo(42),
                "Compile failure must keep the previous successful entry; must not silently switch to interpret VM.");
        }

        [Test]
        public void Emitter_RejectsUnsupportedOp_FailClosed()
        {
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            };

            var ex = Throws<InvalidOperationException>(() =>
                LinearIntGraphCsharpEmitter.Emit(program, "bad-op"));
            That(ex!.Message, Does.Contain("Jump"));
            That(ex.Message, Does.Contain("whitelist"));
        }

        [Test]
        [Category("benchmark")]
        public void Microbench_Native_Interpret_Codegen_ReportsTimings()
        {
            GraphInstruction[] program = BuildLinearIntChainProgram();
            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute codegenState = host.CompileAndActivate(program, "bench");
            Func<int> codegenTight = host.ActiveTightExecute
                ?? throw new InvalidOperationException("Tight execute entry was not bound.");

            const int warmup = 50_000;
            const int iterations = 500_000;

            for (int i = 0; i < warmup; i++)
            {
                Consume(NativeLinearIntChain());
                Consume(ExecuteInterpret(program));
                Consume(ExecuteCodegen(codegenState));
                Consume(codegenTight());
            }

            long nativeNs = MeasureNs(iterations, static () => NativeLinearIntChain());
            long interpretNs = MeasureNs(iterations, () => ExecuteInterpret(program));
            long codegenStateNs = MeasureNs(iterations, () => ExecuteCodegen(codegenState));
            long codegenTightNs = MeasureNs(iterations, () => codegenTight());

            double nativeUs = nativeNs / 1000.0 / iterations;
            double interpretUs = interpretNs / 1000.0 / iterations;
            double codegenStateUs = codegenStateNs / 1000.0 / iterations;
            double codegenTightUs = codegenTightNs / 1000.0 / iterations;

            TestContext.Out.WriteLine("[GraphRoslynAlcR0] linear int chain microbench");
            TestContext.Out.WriteLine($"  Iterations: {iterations}");
            TestContext.Out.WriteLine($"  NativeCSharp_us_per_op:     {nativeUs:F6}");
            TestContext.Out.WriteLine($"  InterpretVm_us_per_op:      {interpretUs:F6}");
            TestContext.Out.WriteLine($"  CodegenState_us_per_op:     {codegenStateUs:F6}");
            TestContext.Out.WriteLine($"  CodegenTight_us_per_op:     {codegenTightUs:F6}");
            TestContext.Out.WriteLine($"  TightVsInterpret_ratio:     {(interpretUs / Math.Max(codegenTightUs, 1e-12)):F2}x");
            TestContext.Out.WriteLine($"  TightVsNative_ratio:        {(codegenTightUs / Math.Max(nativeUs, 1e-12)):F2}x");

            That(NativeLinearIntChain(), Is.EqualTo(ExpectedLinearResult));
            That(ExecuteInterpret(program), Is.EqualTo(ExpectedLinearResult));
            That(ExecuteCodegen(codegenState), Is.EqualTo(ExpectedLinearResult));
            That(codegenTight(), Is.EqualTo(ExpectedLinearResult));

            // Research gate (#860 UAT): tight codegen must beat interpret by roughly an order of magnitude,
            // and stay within the same order of magnitude as handwritten C#.
            That(interpretUs / Math.Max(codegenTightUs, 1e-12), Is.GreaterThan(8.0),
                "Tight codegen path must show clear (near order-of-magnitude) improvement over interpret VM.");
            That(codegenTightUs / Math.Max(nativeUs, 1e-12), Is.LessThan(20.0),
                "Tight codegen path must remain within the same order of magnitude as handwritten C#.");
        }

        private static GraphInstruction[] BuildLinearIntChainProgram()
        {
            // a=1, b=2, c=a+b, d=c+b, e=d+a → 6
            return
            [
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 2 },
                new() { Op = (ushort)GraphNodeOp.AddInt, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.AddInt, Dst = 3, A = 2, B = 1 },
                new() { Op = (ushort)GraphNodeOp.AddInt, Dst = 4, A = 3, B = 0 },
            ];
        }

        private static GraphConfig BuildLinearIntChainConfig()
        {
            return new GraphConfig
            {
                Id = "tests.graph.roslyn-alc.linear-int",
                Kind = "Score",
                Entry = "a",
                Nodes =
                [
                    new GraphNodeConfig { Id = "a", Op = "ConstInt", IntValue = 1, Next = "b" },
                    new GraphNodeConfig { Id = "b", Op = "ConstInt", IntValue = 2, Next = "c" },
                    new GraphNodeConfig { Id = "c", Op = "AddInt", Inputs = ["a", "b"], Next = "d" },
                    new GraphNodeConfig { Id = "d", Op = "AddInt", Inputs = ["c", "b"], Next = "e" },
                    new GraphNodeConfig { Id = "e", Op = "AddInt", Inputs = ["d", "a"] },
                ],
                Outputs =
                [
                    new GraphOutputConfig
                    {
                        Id = "score",
                        Destination = nameof(GraphOutputDestinationKind.Summary),
                        Type = "Int",
                        Source = "e",
                        Key = "score",
                    },
                ],
            };
        }

        private static int ExecuteInterpret(ReadOnlySpan<GraphInstruction> program)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return state.I[4];
        }

        private static int ExecuteCodegen(GraphGeneratedExecute execute)
        {
            return ExecuteCodegenRegister(execute, register: 4);
        }

        private static int ExecuteCodegenRegister(GraphGeneratedExecute execute, int register)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
            };
            execute(ref state);
            return state.I[register];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int NativeLinearIntChain()
        {
            // volatile-style sinks via NoInlining + local chain matching the IR.
            int a = 1;
            int b = 2;
            int c = a + b;
            int d = c + b;
            int e = d + a;
            return e;
        }

        private static long MeasureNs(int iterations, Func<int> body)
        {
            var sw = Stopwatch.StartNew();
            int sink = 0;
            for (int i = 0; i < iterations; i++)
            {
                sink ^= body();
            }

            sw.Stop();
            Consume(sink);
            return (long)(sw.Elapsed.TotalMilliseconds * 1_000_000.0);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Consume(int value)
        {
            if (value == int.MinValue)
            {
                throw new InvalidOperationException("unreachable sink");
            }
        }
    }
}
