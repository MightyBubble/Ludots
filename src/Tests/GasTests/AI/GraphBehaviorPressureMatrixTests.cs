using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBehaviorPressureMatrixTests
    {
        private static string ArtifactDir
        {
            get
            {
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null &&
                       !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
                {
                    dir = dir.Parent;
                }

                if (dir == null)
                {
                    throw new InvalidOperationException("Could not locate repo root from test directory.");
                }

                return Path.Combine(dir.FullName, "docs", "benchmarks", "graph-behavior-pressure");
            }
        }

        [Test]
        public void WritePressureMatrices_M1_M2_M3_M6()
        {
            Directory.CreateDirectory(ArtifactDir);
            WriteM1();
            WriteM2();
            WriteM3();
            WriteM6();
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m2.csv")));
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m3.csv")));
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m6.csv")));
        }

        private static void WriteM1()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,N_topo,G,I,T_ai_ms,notes");
            int[] agents = { 500, 2000, 10000 };
            int[] leaves = { 7, 15, 31, 63 }; // N_topo = leaves+1
            foreach (int a in agents)
            {
                foreach (int leaf in leaves)
                {
                    int nTopo = leaf + 1;
                    BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence($"m1.{nTopo}", leaf);
                    var world = new BehaviorTreeWorld(tree, a);
                    for (int i = 0; i < a; i++) world.AddAgent();
                    world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 8);
                    var sw = Stopwatch.StartNew();
                    world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 8);
                    sw.Stop();
                    sb.AppendLine($"{a},{nTopo},1,0,{sw.Elapsed.TotalMilliseconds:F3},AlwaysSuccess sequence");
                }
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m1.csv"), sb.ToString());
        }

        private static void WriteM2()
        {
            // Fixed N_topo=16; G distinct trees (same shape, distinct ids) — agents round-robin.
            var sb = new StringBuilder();
            sb.AppendLine("A,G,N_topo,T_ai_ms,notes");
            const int a = 10_000;
            const int nTopo = 16;
            int[] gs = { 1, 4, 16, 64, 256 };
            foreach (int g in gs)
            {
                var trees = new BehaviorTreeDefinition[g];
                for (int i = 0; i < g; i++)
                {
                    trees[i] = BehaviorTreeFactory.CreateAlwaysSuccessSequence($"m2.g{g}.t{i}", leafCount: nTopo - 1);
                }

                // Measure sum of G independent worlds each with A/G agents (shared compile, id lookup flat).
                int per = a / g;
                double totalMs = 0;
                for (int i = 0; i < g; i++)
                {
                    var world = new BehaviorTreeWorld(trees[i], per);
                    for (int j = 0; j < per; j++) world.AddAgent();
                    world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 8);
                    var sw = Stopwatch.StartNew();
                    world.TickAll(ReadOnlySpan<GraphInstruction>.Empty, 8);
                    sw.Stop();
                    totalMs += sw.Elapsed.TotalMilliseconds;
                }

                sb.AppendLine($"{a},{g},{nTopo},{totalMs:F3},G worlds share shape; sum of think waves");
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m2.csv"), sb.ToString());
        }

        private static void WriteM3()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,I,T_ms,halted,notes");
            const int a = 10_000;
            int[] instrTargets = { 32, 128, 256, 1024 };
            foreach (int targetI in instrTargets)
            {
                GraphInstruction[] program = BuildNopChainThenHalt(targetI);
                Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
                Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
                Span<int> call = stackalloc int[GraphVmLimits.MaxCallStackDepth];
                // warmup
                for (int i = 0; i < 64; i++)
                {
                    RunHalt(program, ints, bools, call, budget: targetI + 8);
                }

                var sw = Stopwatch.StartNew();
                int halted = 0;
                for (int i = 0; i < a; i++)
                {
                    GraphSliceResult r = RunHalt(program, ints, bools, call, budget: targetI + 8);
                    if (r.Halted) halted++;
                }

                sw.Stop();
                sb.AppendLine($"{a},{program.Length},{sw.Elapsed.TotalMilliseconds:F3},{halted},L1 ExecuteSlice chain");
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m3.csv"), sb.ToString());
        }

        private static void WriteM6()
        {
            var sb = new StringBuilder();
            sb.AppendLine("targets,I,casts_per_wave,T_ms,notes");
            int[] targets = { 250, 1000, 10000 };
            int[] instr = { 8, 32, 128 };
            foreach (int t in targets)
            {
                foreach (int iCount in instr)
                {
                    GraphInstruction[] program = BuildNopChainThenHalt(iCount);
                    Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
                    Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
                    Span<int> call = stackalloc int[GraphVmLimits.MaxCallStackDepth];
                    for (int w = 0; w < 8; w++)
                    {
                        for (int j = 0; j < t; j++) RunHalt(program, ints, bools, call, iCount + 8);
                    }

                    var sw = Stopwatch.StartNew();
                    for (int j = 0; j < t; j++) RunHalt(program, ints, bools, call, iCount + 8);
                    sw.Stop();
                    sb.AppendLine($"{t},{program.Length},1,{sw.Elapsed.TotalMilliseconds:F3},Ability sandbox cast wave");
                }
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m6.csv"), sb.ToString());
        }

        private static GraphInstruction[] BuildNopChainThenHalt(int minLength)
        {
            // ConstInt + MoveInt* + HaltReturnInt; pad with MoveInt self copies.
            if (minLength < 3) minLength = 3;
            var list = new System.Collections.Generic.List<GraphInstruction>(minLength)
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
            };
            while (list.Count < minLength - 1)
            {
                list.Add(new GraphInstruction { Op = (ushort)GraphNodeOp.MoveInt, Dst = 0, A = 0 });
            }

            list.Add(new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 });
            return list.ToArray();
        }

        private static GraphSliceResult RunHalt(
            GraphInstruction[] program,
            Span<int> ints,
            Span<byte> bools,
            Span<int> call,
            int budget)
        {
            ints.Clear();
            bools.Clear();
            var cursor = new GraphExecutionCursor();
            var state = new GraphExecutionState
            {
                I = ints,
                B = bools,
                CallStack = call,
                Status = GraphExecutionStatus.Running
            };
            return GasGraphOpHandlerTable.ExecuteSlice(
                ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, budget);
        }
    }
}
