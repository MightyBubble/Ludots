using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Graph.Codegen;
using Ludots.Tests.Gas.Graph.Codegen;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #860 R0 + Track C spike: linear/branch int graph → C# codegen → Roslyn → Collectible ALC → hot swap,
    /// with fail-closed compile errors and native/interpret/codegen microbench reporting.
    /// Correctness tests carry <c>ci-gate</c>; microbench is <c>benchmark</c> only (no hard ratio asserts).
    /// </summary>
    [TestFixture]
    public sealed class GraphRoslynAlcCodegenSpikeTests
    {
        private const int ExpectedLinearResult = 6;
        private const int ExpectedIfElseTrueResult = 10;
        private const int ExpectedIfElseFalseResult = 20;

        [Test]
        [Category("ci-gate")]
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
        [Category("ci-gate")]
        public void Codegen_FromInstructionArray_MatchesInterpretVm()
        {
            // Spike boundary: consume lowered GraphInstruction[] only, not authoring JSON.
            GraphInstruction[] program = BuildLinearIntChainProgram();

            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(program, "from-ir");
            That(ExecuteCodegen(execute), Is.EqualTo(ExecuteInterpret(program)));
            That(ExecuteCodegen(execute), Is.EqualTo(ExpectedLinearResult));
        }

        [Test]
        [Category("ci-gate")]
        public void HotReload_ReplacesEntrypoint_AndPreviousAlcCanUnload()
        {
            GraphInstruction[] programA =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 30 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            GraphInstruction[] programB =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 7 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
        [Category("ci-gate")]
        public void CompileFailure_FailClosed_KeepsPreviousEntrypoint_NoInterpreterFallback()
        {
            GraphInstruction[] goodProgram =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 42 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
        [Category("ci-gate")]
        public void Emitter_HandlerForward_AcceptsNonSpecializeOp()
        {
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                // QueryRadius alone would need Api; mix with Halt is enough to force handler-forward emit shape.
                new() { Op = (ushort)GraphNodeOp.QueryRadius, Imm = 0 },
            };

            // Eligibility allows HandlerForward; emit itself succeeds (does not execute).
            string source = LinearIntGraphCsharpEmitter.Emit(program[..2], "hf-ok");
            That(source, Does.Contain("RunToHalt").Or.Contain("state.I["));
            GraphCodegenEmitResult mixed = GraphCsharpEmitter.Emit(
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                    new() { Op = (ushort)GraphNodeOp.QueryRadius, Imm = 0 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                },
                "hf-query");
            That(mixed.UsesSpecialize, Is.False);
            That(mixed.Source, Does.Contain("RunToHalt"));
            That(mixed.Source, Does.Contain("QueryRadius").Or.Contain("Op = 100"));
        }

        [Test]
        [Category("ci-gate")]
        public void Emitter_AllowsBackwardJump_ForLoops()
        {
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.Jump, Imm = -1 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            string source = LinearIntGraphCsharpEmitter.Emit(program, "back-jump");
            That(source, Does.Contain("goto L1"));
        }

        [Test]
        [Category("ci-gate")]
        public void Codegen_IfElseIntProgram_MatchesInterpretVm_TrueBranch()
        {
            // if (3 < 7) I[2]=10; else I[2]=20;
            GraphInstruction[] program = BuildIfElseLtProgram(left: 3, right: 7);
            int interpret = ExecuteInterpretRegister(program, register: 2);

            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(program, "ifelse-true");
            int codegen = ExecuteCodegenRegister(execute, register: 2);

            That(interpret, Is.EqualTo(ExpectedIfElseTrueResult));
            That(codegen, Is.EqualTo(ExpectedIfElseTrueResult));
            That(host.ActiveSource, Does.Contain("goto L"));
            That(host.ActiveSource, Does.Contain("== 0) goto"));
            That(host.ActiveTightExecute!(), Is.EqualTo(ExpectedIfElseTrueResult));
        }

        [Test]
        [Category("ci-gate")]
        public void Codegen_IfElseIntProgram_MatchesInterpretVm_FalseBranch()
        {
            // if (9 < 4) I[2]=10; else I[2]=20;
            GraphInstruction[] program = BuildIfElseLtProgram(left: 9, right: 4);
            int interpret = ExecuteInterpretRegister(program, register: 2);

            using var host = new GraphRoslynAlcCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(program, "ifelse-false");
            int codegen = ExecuteCodegenRegister(execute, register: 2);

            That(interpret, Is.EqualTo(ExpectedIfElseFalseResult));
            That(codegen, Is.EqualTo(ExpectedIfElseFalseResult));
            That(host.ActiveTightExecute!(), Is.EqualTo(ExpectedIfElseFalseResult));
        }

        [Test]
        [Category("ci-gate")]
        public void Codegen_CompareEqInt_Branch_MatchesInterpretVm()
        {
            GraphInstruction[] eq = BuildIfElseEqProgram(left: 5, right: 5);
            GraphInstruction[] ne = BuildIfElseEqProgram(left: 5, right: 3);

            using var host = new GraphRoslynAlcCompilerHost();
            That(ExecuteCodegenRegister(host.CompileAndActivate(eq, "eq"), 2),
                Is.EqualTo(ExecuteInterpretRegister(eq, 2)));
            That(ExecuteInterpretRegister(eq, 2), Is.EqualTo(1));

            That(ExecuteCodegenRegister(host.CompileAndActivate(ne, "ne"), 2),
                Is.EqualTo(ExecuteInterpretRegister(ne, 2)));
            That(ExecuteInterpretRegister(ne, 2), Is.EqualTo(0));
            That(host.ActiveSource, Does.Contain("=="));
        }

        [Test]
        [Category("ci-gate")]
        public void HotReload_BranchedProgram_ReplacesEntrypoint()
        {
            GraphInstruction[] programA = BuildIfElseLtProgram(left: 1, right: 2); // → 10
            GraphInstruction[] programB = BuildIfElseLtProgram(left: 8, right: 2); // → 20

            using var host = new GraphRoslynAlcCompilerHost();
            That(ExecuteCodegenRegister(host.CompileAndActivate(programA, "branch-a"), 2),
                Is.EqualTo(ExpectedIfElseTrueResult));
            That(ExecuteCodegenRegister(host.CompileAndActivate(programB, "branch-b"), 2),
                Is.EqualTo(ExpectedIfElseFalseResult));
            That(host.ActiveAssemblyMarker, Is.EqualTo("branch-b"));
        }

        [Test]
        [Category("ci-gate")]
        public void Emitter_IfElse_EmitsLabelsAndGoto_NotStructuredSugarOnly()
        {
            GraphInstruction[] program = BuildIfElseLtProgram(left: 3, right: 7);
            string source = LinearIntGraphCsharpEmitter.Emit(program, "sample-ifelse");

            That(source, Does.Contain("L0:"));
            That(source, Does.Contain("L3:"));
            That(source, Does.Contain("if (state.B[0] == 0) goto L6;"));
            That(source, Does.Contain("goto L7;"));
            That(source, Does.Contain("state.B[0] = (byte)(state.I[0] < state.I[1] ? 1 : 0);"));
            TestContext.Out.WriteLine("[TrackC] sample generated C# for if/else int program:");
            TestContext.Out.WriteLine(source);
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
            // Timing ratios are research signals only — do not fail ci-gate on machine noise.
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
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 4 },
            ];
        }

        /// <summary>
        /// if (I[0] &lt; I[1]) I[2]=10; else I[2]=20;
        /// IR PC: after JumpIfFalse at index 3, Imm=2 skips then-arm; Jump Imm=1 skips else-arm.
        /// </summary>
        private static GraphInstruction[] BuildIfElseLtProgram(int left, int right)
        {
            return
            [
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = left },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = right },
                new() { Op = (ushort)GraphNodeOp.CompareLtInt, Dst = 0, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 10 },
                new() { Op = (ushort)GraphNodeOp.Jump, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 20 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 2 },
            ];
        }

        /// <summary>
        /// if (I[0] == I[1]) I[2]=1; else I[2]=0;
        /// </summary>
        private static GraphInstruction[] BuildIfElseEqProgram(int left, int right)
        {
            return
            [
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = left },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = right },
                new() { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = 0, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.Jump, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 0 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 2 },
            ];
        }

        private static int ExecuteInterpret(ReadOnlySpan<GraphInstruction> program)
        {
            return ExecuteInterpretRegister(program, register: 4);
        }

        private static int ExecuteInterpretRegister(ReadOnlySpan<GraphInstruction> program, int register)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                CallStack = callStack,
                CallStackCount = 0,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return state.I[register];
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
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                CallStack = callStack,
                CallStackCount = 0,
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
