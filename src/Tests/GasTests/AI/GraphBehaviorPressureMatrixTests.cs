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
            PressureThinkRow[] m1 = WriteM1();
            PressureThinkRow[] m2 = WriteM2();
            PressureSliceRow[] m3 = WriteM3();
            PressureSliceRow[] m6 = WriteM6();
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m1.csv")));
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m2.csv")));
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m3.csv")));
            Assert.That(File.Exists(Path.Combine(ArtifactDir, "matrix-m6.csv")));

            for (int i = 0; i < m1.Length; i++)
            {
                PressureThinkRow row = m1[i];
                Assert.That(row.TimeMs, Is.GreaterThan(0.0), $"M1 A={row.Agents} N={row.Topology} produced no timing sample.");
                Assert.That(row.TimeMs, Is.LessThan(15.0),
                    $"M1 A={row.Agents} N={row.Topology} think wave exceeded 15ms: {row.TimeMs:F3}");
            }

            for (int i = 0; i < m2.Length; i++)
            {
                PressureThinkRow row = m2[i];
                Assert.That(row.TimeMs, Is.GreaterThan(0.0), $"M2 G={row.Topology} produced no timing sample.");
                Assert.That(row.TimeMs, Is.LessThan(15.0),
                    $"M2 A={row.Agents} G={row.Topology} think-wave sum exceeded 15ms: {row.TimeMs:F3}");
            }

            for (int i = 0; i < m3.Length; i++)
            {
                PressureSliceRow row = m3[i];
                Assert.That(row.Halted, Is.EqualTo(row.Agents),
                    $"M3 I={row.Instructions} halted {row.Halted}/{row.Agents} programs.");
                Assert.That(row.TimeMs, Is.GreaterThan(0.0), $"M3 I={row.Instructions} produced no timing sample.");
                Assert.That(row.TimeMs, Is.LessThan(1000.0),
                    $"M3 I={row.Instructions} ExecuteSlice wave exceeded 1000ms: {row.TimeMs:F3}");
            }

            for (int i = 0; i < m6.Length; i++)
            {
                PressureSliceRow row = m6[i];
                Assert.That(row.TimeMs, Is.GreaterThan(0.0),
                    $"M6 targets={row.Agents} I={row.Instructions} produced no timing sample.");
                Assert.That(row.TimeMs, Is.LessThan(15.0),
                    $"M6 targets={row.Agents} I={row.Instructions} cast wave exceeded 15ms: {row.TimeMs:F3}");
            }
        }

        private readonly record struct PressureThinkRow(int Agents, int Topology, double TimeMs);
        private readonly record struct PressureSliceRow(int Agents, int Instructions, double TimeMs, int Halted);

        private static PressureThinkRow[] WriteM1()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,N_topo,G,I,T_ai_ms,notes");
            int[] agents = { 500, 2000, 10000 };
            int[] leaves = { 7, 15, 31, 63 };
            var rows = new PressureThinkRow[agents.Length * leaves.Length];
            int rowIndex = 0;
            foreach (int a in agents)
            {
                foreach (int leaf in leaves)
                {
                    int nTopo = leaf + 1;
                    BehaviorTreeDefinition tree = BehaviorTreeFactory.CreateAlwaysSuccessSequence($"m1.{nTopo}", leaf);
                    var world = new BehaviorTreeWorld(tree, a);
                    for (int i = 0; i < a; i++) world.AddAgent();
                    world.TickAll(8);
                    var sw = Stopwatch.StartNew();
                    world.TickAll(8);
                    sw.Stop();
                    rows[rowIndex++] = new PressureThinkRow(a, nTopo, sw.Elapsed.TotalMilliseconds);
                    sb.AppendLine($"{a},{nTopo},1,0,{sw.Elapsed.TotalMilliseconds:F3},AlwaysSuccess sequence");
                }
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m1.csv"), sb.ToString());
            return rows;
        }

        private static PressureThinkRow[] WriteM2()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,G,N_topo,T_ai_ms,notes");
            const int a = 10_000;
            const int nTopo = 16;
            int[] gs = { 1, 4, 16, 64, 256 };
            var rows = new PressureThinkRow[gs.Length];
            for (int gIndex = 0; gIndex < gs.Length; gIndex++)
            {
                int g = gs[gIndex];
                var trees = new BehaviorTreeDefinition[g];
                for (int i = 0; i < g; i++)
                {
                    trees[i] = BehaviorTreeFactory.CreateAlwaysSuccessSequence($"m2.g{g}.t{i}", leafCount: nTopo - 1);
                }

                int per = a / g;
                double totalMs = 0;
                for (int i = 0; i < g; i++)
                {
                    var world = new BehaviorTreeWorld(trees[i], per);
                    for (int j = 0; j < per; j++) world.AddAgent();
                    world.TickAll(8);
                    var sw = Stopwatch.StartNew();
                    world.TickAll(8);
                    sw.Stop();
                    totalMs += sw.Elapsed.TotalMilliseconds;
                }

                rows[gIndex] = new PressureThinkRow(a, g, totalMs);
                sb.AppendLine($"{a},{g},{nTopo},{totalMs:F3},G worlds share shape; sum of think waves");
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m2.csv"), sb.ToString());
            return rows;
        }

        private static PressureSliceRow[] WriteM3()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,I,T_ms,halted,notes");
            const int a = 10_000;
            int[] instrTargets = { 32, 128, 256, 1024 };
            var rows = new PressureSliceRow[instrTargets.Length];
            for (int targetIndex = 0; targetIndex < instrTargets.Length; targetIndex++)
            {
                int targetI = instrTargets[targetIndex];
                GraphInstruction[] program = BuildNopChainThenHalt(targetI);
                Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
                Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
                Span<int> call = stackalloc int[GraphVmLimits.MaxCallStackDepth];
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
                rows[targetIndex] = new PressureSliceRow(a, program.Length, sw.Elapsed.TotalMilliseconds, halted);
                sb.AppendLine($"{a},{program.Length},{sw.Elapsed.TotalMilliseconds:F3},{halted},L1 ExecuteSlice chain");
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m3.csv"), sb.ToString());
            return rows;
        }

        private static PressureSliceRow[] WriteM6()
        {
            var sb = new StringBuilder();
            sb.AppendLine("targets,I,casts_per_wave,T_ms,notes");
            int[] targets = { 250, 1000, 10000 };
            int[] instr = { 8, 32, 128 };
            var rows = new PressureSliceRow[targets.Length * instr.Length];
            int rowIndex = 0;
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
                    rows[rowIndex++] = new PressureSliceRow(t, program.Length, sw.Elapsed.TotalMilliseconds, t);
                    sb.AppendLine($"{t},{program.Length},1,{sw.Elapsed.TotalMilliseconds:F3},Ability sandbox cast wave");
                }
            }

            File.WriteAllText(Path.Combine(ArtifactDir, "matrix-m6.csv"), sb.ToString());
            return rows;
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
