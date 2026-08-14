using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphKindOperationPolicy
    {
        public enum ViolationKind : byte
        {
            None = 0,
            MissingOperationMetadata = 1,
            OperationNotAllowed = 2,
            ListenerOperationNotAllowed = 3,
        }

        public readonly struct Violation
        {
            public Violation(
                ViolationKind kind,
                GraphNodeOp operation,
                ushort encodedOperation,
                int instructionIndex,
                in EffectOperationMetadata metadata)
            {
                Kind = kind;
                Operation = operation;
                EncodedOperation = encodedOperation;
                InstructionIndex = instructionIndex;
                Metadata = metadata;
            }

            public ViolationKind Kind { get; }
            public GraphNodeOp Operation { get; }
            public ushort EncodedOperation { get; }
            public int InstructionIndex { get; }
            public EffectOperationMetadata Metadata { get; }
            public bool HasMetadata => Metadata.Kind != EffectOperationKind.None;
        }

        public const string OperationNotAllowedError = "GAS.GRAPH_KIND.ERR.OperationNotAllowed";
        public const string MissingOperationMetadataError = "GAS.GRAPH_KIND.ERR.MissingOperationMetadata";
        public const string ListenerOperationNotAllowedError = "GAS.GRAPH_KIND.ERR.ListenerOperationNotAllowed";
        public const string JumpOutOfRangeError = "GAS.GRAPH.ERR.JumpOutOfRange";
        public const string MissingHaltError = "GAS.GRAPH.ERR.MissingHalt";
        public const string RegisterOutOfRangeError = "GAS.GRAPH.ERR.RegisterOutOfRange";
        public const string KindMismatchError = "GAS.GRAPH.ERR.KindMismatch";
        public const string PcOutOfRangeError = "GAS.GRAPH.ERR.PcOutOfRange";

        public static void RequireAllowed(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            int graphId = 0,
            string entrypoint = "Graph")
            => RequireAllowedCore(
                kind,
                program,
                handlers,
                graphId,
                entrypoint,
                requireListenerCompatibility: false,
                requirePureListenerOperations: false);

        public static void RequireListenerCompatible(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            bool requirePureOperations,
            int graphId = 0,
            string entrypoint = "EffectListener")
            => RequireAllowedCore(
                kind,
                program,
                handlers,
                graphId,
                entrypoint,
                requireListenerCompatibility: true,
                requirePureListenerOperations: requirePureOperations);

        public static bool TryFindListenerViolation(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            bool requirePureOperations,
            out Violation violation)
            => TryFindViolationCore(
                kind,
                program,
                handlers,
                requireListenerCompatibility: true,
                requirePureListenerOperations: requirePureOperations,
                out violation);

        public static bool TryFindViolation(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            out Violation violation)
            => TryFindViolationCore(
                kind,
                program,
                handlers,
                requireListenerCompatibility: false,
                requirePureListenerOperations: false,
                out violation);

        public static void ValidateProgram(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            int graphId = 0,
            string entrypoint = "GraphProgramRegistry")
        {
            RequireAllowed(kind, program, handlers, graphId, entrypoint);
            ValidateRegisterBounds(program, graphId, entrypoint);
            ValidateBranchTargets(program, graphId, entrypoint);
            ValidateHasHalt(program, graphId, entrypoint);
        }

        public static void ValidateRegisterBounds(
            ReadOnlySpan<GraphInstruction> program,
            int graphId = 0,
            string entrypoint = "GraphProgramRegistry")
        {
            for (int i = 0; i < program.Length; i++)
            {
                ref readonly GraphInstruction instruction = ref program[i];
                GraphNodeOp op = (GraphNodeOp)instruction.Op;
                if (!GraphOpDescriptorTable.TryGet(op, out GraphOpDescriptor descriptor) ||
                    descriptor.DstRole == GraphOperandRole.DstRegister)
                {
                    RequireRegisterIndex(graphId, i, nameof(GraphInstruction.Dst), instruction.Dst, entrypoint);
                }

                RequireRegisterIndex(graphId, i, nameof(GraphInstruction.A), instruction.A, entrypoint);
                RequireRegisterIndex(graphId, i, nameof(GraphInstruction.B), instruction.B, entrypoint);
                RequireRegisterIndex(graphId, i, nameof(GraphInstruction.C), instruction.C, entrypoint);
            }
        }

        public static void ValidateBranchTargets(
            ReadOnlySpan<GraphInstruction> program,
            int graphId = 0,
            string entrypoint = "GraphProgramRegistry")
        {
            for (int i = 0; i < program.Length; i++)
            {
                GraphNodeOp op = (GraphNodeOp)program[i].Op;
                if (op is GraphNodeOp.Jump or GraphNodeOp.JumpIfFalse)
                {
                    int target = i + 1 + program[i].Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        throw new InvalidOperationException(
                            $"{JumpOutOfRangeError}: entrypoint='{entrypoint}', graphId={graphId}, instructionIndex={i}, op='{op}', target={target}, length={program.Length}. 第 {i} 条指令跳到了程序外面。");
                    }
                }
                else if (op == GraphNodeOp.Call)
                {
                    int target = program[i].Imm;
                    if ((uint)target >= (uint)program.Length)
                    {
                        throw new InvalidOperationException(
                            $"{JumpOutOfRangeError}: entrypoint='{entrypoint}', graphId={graphId}, instructionIndex={i}, op='Call', target={target}, length={program.Length}. 第 {i} 条指令跳到了程序外面。");
                    }
                }
            }
        }

        public static void ValidateHasHalt(
            ReadOnlySpan<GraphInstruction> program,
            int graphId = 0,
            string entrypoint = "GraphProgramRegistry")
        {
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op == (ushort)GraphNodeOp.HaltReturnInt)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"{MissingHaltError}: entrypoint='{entrypoint}', graphId={graphId}. 这张图没有显式终结指令 HaltReturnInt。");
        }

        private static void RequireRegisterIndex(
            int graphId,
            int instructionIndex,
            string operand,
            byte registerIndex,
            string entrypoint)
        {
            if (registerIndex < GraphVmLimits.MaxFloatRegisters)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{RegisterOutOfRangeError}: entrypoint='{entrypoint}', graphId={graphId}, instructionIndex={instructionIndex}, operand={operand}, registerIndex={registerIndex}, capacity={GraphVmLimits.MaxFloatRegisters}.");
        }

        private static void RequireAllowedCore(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            int graphId,
            string entrypoint,
            bool requireListenerCompatibility,
            bool requirePureListenerOperations)
        {
            if (!TryFindViolationCore(
                    kind,
                    program,
                    handlers,
                    requireListenerCompatibility,
                    requirePureListenerOperations,
                    out Violation violation))
            {
                return;
            }

            string errorCode = violation.Kind switch
            {
                ViolationKind.MissingOperationMetadata => MissingOperationMetadataError,
                ViolationKind.OperationNotAllowed => OperationNotAllowedError,
                ViolationKind.ListenerOperationNotAllowed => ListenerOperationNotAllowedError,
                _ => throw new ArgumentOutOfRangeException(nameof(violation)),
            };
            string reason = violation.Kind switch
            {
                ViolationKind.MissingOperationMetadata => "Opcode has no registered operation metadata.",
                ViolationKind.OperationNotAllowed => $"Operation metadata kind is '{violation.Metadata.Kind}'.",
                ViolationKind.ListenerOperationNotAllowed when
                    violation.Metadata.Kind == EffectOperationKind.DelegatedBuiltin
                    => "InvokeBuiltin is not accepted in listener graphs because listener execution has no owner EffectTemplate context.",
                ViolationKind.ListenerOperationNotAllowed when RequiresListenerOwnerContext(violation.Operation)
                    => $"{violation.Operation} is not accepted in listener graphs because listener execution has no owner EffectTemplate config context.",
                ViolationKind.ListenerOperationNotAllowed when requirePureListenerOperations
                    => $"Listener graphs in pure phases require statically classified Pure operations; operation metadata kind is '{violation.Metadata.Kind}'.",
                ViolationKind.ListenerOperationNotAllowed
                    => $"Listener graphs require statically classified Pure or GasTransactional operations; operation metadata kind is '{violation.Metadata.Kind}'.",
                _ => throw new ArgumentOutOfRangeException(nameof(violation)),
            };
            throw CreateError(
                errorCode,
                kind,
                violation.Operation,
                violation.EncodedOperation,
                violation.InstructionIndex,
                graphId,
                entrypoint,
                reason);
        }

        private static bool TryFindViolationCore(
            GraphKind kind,
            ReadOnlySpan<GraphInstruction> program,
            GasGraphOpHandlerTable handlers,
            bool requireListenerCompatibility,
            bool requirePureListenerOperations,
            out Violation violation)
        {
            ArgumentNullException.ThrowIfNull(handlers);
            if (kind is not (GraphKind.Effect or GraphKind.Query or GraphKind.Score or GraphKind.Validation or GraphKind.Derived or GraphKind.Script))
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
                    violation = new Violation(
                        ViolationKind.MissingOperationMetadata,
                        op,
                        encodedOp,
                        instructionIndex,
                        default);
                    return true;
                }

                if (!IsAllowed(kind, op, in metadata))
                {
                    violation = new Violation(
                        ViolationKind.OperationNotAllowed,
                        op,
                        encodedOp,
                        instructionIndex,
                        in metadata);
                    return true;
                }

                if (requireListenerCompatibility &&
                    !IsListenerCompatible(op, in metadata, requirePureListenerOperations))
                {
                    violation = new Violation(
                        ViolationKind.ListenerOperationNotAllowed,
                        op,
                        encodedOp,
                        instructionIndex,
                        in metadata);
                    return true;
                }
            }

            violation = default;
            return false;
        }

        private static bool IsListenerCompatible(
            GraphNodeOp operation,
            in EffectOperationMetadata metadata,
            bool requirePureOperations)
        {
            if (RequiresListenerOwnerContext(operation))
            {
                return false;
            }

            return requirePureOperations
                ? metadata.Kind == EffectOperationKind.Pure
                : metadata.Kind is EffectOperationKind.Pure or EffectOperationKind.GasTransactional;
        }

        private static bool RequiresListenerOwnerContext(GraphNodeOp operation)
            => GraphOpDescriptorTable.RequiresListenerOwnerContext(operation);

        private static bool IsAllowed(
            GraphKind kind,
            GraphNodeOp op,
            in EffectOperationMetadata metadata)
            => GraphOpDescriptorTable.IsPolicyAllowed(kind, op, in metadata);

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
