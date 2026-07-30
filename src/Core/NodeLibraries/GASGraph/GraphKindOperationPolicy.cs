using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphKindOperationPolicy
    {
        public const string OperationNotAllowedError = "GAS.GRAPH_KIND.ERR.OperationNotAllowed";
        public const string MissingOperationMetadataError = "GAS.GRAPH_KIND.ERR.MissingOperationMetadata";

        public static void RequireAllowed(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            int graphId = 0,
            string entrypoint = "Graph")
        {
            ArgumentNullException.ThrowIfNull(handlers);
            if (kind is not (GraphKind.Effect or GraphKind.Query or GraphKind.Score or GraphKind.Validation or GraphKind.Derived))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Graph operation policy requires an explicit supported kind.");
            }

            for (int instructionIndex = 0; instructionIndex < program.Length; instructionIndex++)
            {
                ushort encodedOp = program[instructionIndex].Op;
                if (encodedOp == 0)
                {
                    continue;
                }

                GraphNodeOp op = (GraphNodeOp)encodedOp;
                if (!handlers.TryGetOperationMetadata(op, out EffectOperationMetadata metadata))
                {
                    throw CreateError(
                        MissingOperationMetadataError,
                        kind,
                        op,
                        encodedOp,
                        instructionIndex,
                        graphId,
                        entrypoint,
                        "Opcode has no registered operation metadata.");
                }

                if (IsAllowed(kind, op, in metadata))
                {
                    continue;
                }

                throw CreateError(
                    OperationNotAllowedError,
                    kind,
                    op,
                    encodedOp,
                    instructionIndex,
                    graphId,
                    entrypoint,
                    $"Operation metadata kind is '{metadata.Kind}'.");
            }
        }

        private static bool IsAllowed(
            GraphKind kind,
            GraphNodeOp op,
            in EffectOperationMetadata metadata)
        {
            if (kind == GraphKind.Effect)
            {
                return true;
            }

            if (metadata.Kind == EffectOperationKind.Pure)
            {
                return true;
            }

            return kind == GraphKind.Derived && op == GraphNodeOp.WriteSelfAttribute;
        }

        private static InvalidOperationException CreateError(
            string errorCode,
            GraphKind kind,
            GraphNodeOp op,
            ushort encodedOp,
            int instructionIndex,
            int graphId,
            string entrypoint,
            string reason)
        {
            string operation = Enum.IsDefined(typeof(GraphNodeOp), op)
                ? op.ToString()
                : encodedOp.ToString();
            return new InvalidOperationException(
                $"{errorCode}: entrypoint='{entrypoint}', graphId={graphId}, kind='{kind}', operation='{operation}', instructionIndex={instructionIndex}. {reason}");
        }
    }
}
