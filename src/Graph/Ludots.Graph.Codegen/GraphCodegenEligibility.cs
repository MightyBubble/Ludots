using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Graph.Codegen
{
    public sealed class GraphCodegenUnsupportedOp
    {
        public required string Op { get; init; }
        public required int InstructionIndex { get; init; }
        public string? NodeId { get; init; }
        public required string Reason { get; init; }
    }

    public sealed record GraphCodegenEligibilityReport(
        bool Eligible,
        IReadOnlyList<GraphCodegenUnsupportedOp> UnsupportedOps,
        IReadOnlyList<int> YieldPoints,
        string BackendRecommended,
        string EmitMode,
        int InstructionCount);

    public sealed class GraphCodegenEmitResult
    {
        public required string Source { get; init; }
        public required GraphCodegenEligibilityReport Eligibility { get; init; }
        public required IReadOnlyList<string> Diagnostics { get; init; }
        public required bool UsesSpecialize { get; init; }
        public required bool EmitsTightEntry { get; init; }
    }

    public static class GraphCodegenEligibility
    {
        public static GraphCodegenEligibilityReport Analyze(
            ReadOnlySpan<GraphInstruction> program,
            IReadOnlyList<string>? sourceNodeIds = null)
        {
            var unsupported = new List<GraphCodegenUnsupportedOp>();
            var yieldPoints = new List<int>();
            bool allSpecialize = program.Length > 0;

            for (int i = 0; i < program.Length; i++)
            {
                GraphNodeOp op = (GraphNodeOp)program[i].Op;
                if (!Enum.IsDefined(typeof(GraphNodeOp), op))
                {
                    unsupported.Add(new GraphCodegenUnsupportedOp
                    {
                        Op = $"Op#{program[i].Op}",
                        InstructionIndex = i,
                        NodeId = ResolveNodeId(sourceNodeIds, i),
                        Reason = "Opcode value is not a defined GraphNodeOp.",
                    });
                    allSpecialize = false;
                    continue;
                }

                if (!GraphCodegenStrategyCatalog.TryGet(op, out GraphCodegenStrategy strategy))
                {
                    unsupported.Add(new GraphCodegenUnsupportedOp
                    {
                        Op = op.ToString(),
                        InstructionIndex = i,
                        NodeId = ResolveNodeId(sourceNodeIds, i),
                        Reason = "No codegen emit strategy registered.",
                    });
                    allSpecialize = false;
                    continue;
                }

                if (strategy.Kind == GraphCodegenEmitKind.Exempt && op != GraphNodeOp.None)
                {
                    unsupported.Add(new GraphCodegenUnsupportedOp
                    {
                        Op = op.ToString(),
                        InstructionIndex = i,
                        NodeId = ResolveNodeId(sourceNodeIds, i),
                        Reason = "Op is marked exempt and cannot be executed.",
                    });
                    allSpecialize = false;
                }

                if (strategy.Kind != GraphCodegenEmitKind.Specialize && op != GraphNodeOp.None)
                {
                    allSpecialize = false;
                }

                if (op is GraphNodeOp.Yield or GraphNodeOp.AwaitCallback)
                {
                    yieldPoints.Add(i);
                }
            }

            if (program.Length == 0)
            {
                allSpecialize = false;
            }

            bool eligible = unsupported.Count == 0;
            string emitMode = !eligible
                ? "rejected"
                : allSpecialize
                    ? "specialize"
                    : "handler-forward";

            return new GraphCodegenEligibilityReport(
                Eligible: eligible,
                UnsupportedOps: unsupported,
                YieldPoints: yieldPoints,
                BackendRecommended: eligible ? nameof(GraphExecutionBackend.Codegen) : nameof(GraphExecutionBackend.Interpret),
                EmitMode: emitMode,
                InstructionCount: program.Length);
        }

        private static string? ResolveNodeId(IReadOnlyList<string>? sourceNodeIds, int index)
        {
            if (sourceNodeIds == null || (uint)index >= (uint)sourceNodeIds.Count)
            {
                return null;
            }

            string id = sourceNodeIds[index];
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
    }
}
