using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Graph.Codegen;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphCodegenProductTests
    {
        [Test]
        public void EveryExecutableOp_HasCodegenStrategy()
        {
            var missing = new List<string>();
            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                if (!GraphCodegenStrategyCatalog.TryGet(op, out _))
                {
                    missing.Add(op.ToString());
                }
            }

            That(missing, Is.Empty, "Missing codegen strategies:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Registry_CodegenStatus_RequiredAndCovered_ForExecutableOps()
        {
            string repoRoot = FindRepoRoot();
            string path = Path.Combine(repoRoot, GraphCodegenCoverageProjection.RegistryRelativePath);
            GraphCodegenCoverageSummary summary = GraphCodegenCoverageProjection.FromRegistryFile(path);
            That(summary.Pending, Is.EqualTo(0));
            That(summary.Covered, Is.EqualTo(summary.Total));

            var enumOps = Enum.GetValues<GraphNodeOp>()
                .Where(op => op != GraphNodeOp.None)
                .Select(op => op.ToString())
                .ToHashSet(StringComparer.Ordinal);
            var registryOps = summary.Entries.Select(e => e.Op).ToHashSet(StringComparer.Ordinal);
            That(enumOps.Except(registryOps), Is.Empty);
            That(registryOps.Except(enumOps), Is.Empty);
        }

        [Test]
        public void HandlerForward_FullOpProgram_MatchesInterpret()
        {
            GraphInstruction[] program =
            [
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 4 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 5 },
                new() { Op = (ushort)GraphNodeOp.AddInt, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 2 },
            ];

            using var host = new GraphCodegenCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(program, "hf-int", forceHandlerForward: true);
            GraphCodegenParityDiff diff = GraphCodegenParity.CompareRunToHalt(program, execute);
            That(diff.Matches, Is.True, diff.Detail);
            That(diff.CodegenReturnInt, Is.EqualTo(9));
            That(host.ActiveSource, Does.Contain("RunToHalt"));
        }

        [Test]
        public void Specialize_BackwardJumpLoop_CanEmitAndHaltWithBudgetPath()
        {
            // I[0]=0; L1: I[0]+=1; if I[0]<3 goto L1; halt I[0]
            GraphInstruction[] program =
            [
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 0 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.AddInt, Dst = 0, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 3 },
                new() { Op = (ushort)GraphNodeOp.CompareLtInt, Dst = 0, A = 0, B = 2 },
                new() { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.Jump, Imm = -5 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            ];

            GraphCodegenEmitResult emit = GraphCsharpEmitter.Emit(program, "loop");
            That(emit.UsesSpecialize, Is.True);
            That(emit.Source, Does.Contain("goto L"));

            using var host = new GraphCodegenCompilerHost();
            GraphGeneratedExecute execute = host.CompileAndActivate(program, "loop");
            GraphCodegenParityDiff diff = GraphCodegenParity.CompareRunToHalt(program, execute);
            That(diff.Matches, Is.True, diff.Detail);
            That(diff.CodegenReturnInt, Is.EqualTo(3));
        }

        [Test]
        public void Specialize_FloatAndTextFamily_EmitsInlineOps()
        {
            GraphInstruction[] program =
            [
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1.5f },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2.5f },
                new() { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.FloatToText, Dst = 0, A = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstText, Dst = 1, Imm = 0 },
                new() { Op = (ushort)GraphNodeOp.ConcatText, Dst = 2, A = 1, B = 0 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            ];

            GraphCodegenEmitResult emit = GraphCsharpEmitter.Emit(
                program,
                "text-float",
                symbols: new[] { "dmg=" });
            That(emit.UsesSpecialize, Is.True);
            That(emit.Source, Does.Contain("state.F["));
            That(emit.Source, Does.Contain("state.Text"));
            That(emit.Source, Does.Contain("Symbols["));
        }

        [Test]
        public void CodegenMode_RejectsUndefinedOpcode_FailClosed()
        {
            GraphInstruction[] program =
            [
                new() { Op = 65000, Dst = 0, Imm = 1 },
            ];

            var ex = Throws<InvalidOperationException>(() =>
                GraphCsharpEmitter.Emit(program, "bad-op"));
            That(ex!.Message, Does.Contain("fail-closed").IgnoreCase.Or.Contain("rejected"));
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")) &&
                    File.Exists(Path.Combine(dir, GraphCodegenCoverageProjection.RegistryRelativePath)))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Repo root not found from test directory.");
        }
    }
}
