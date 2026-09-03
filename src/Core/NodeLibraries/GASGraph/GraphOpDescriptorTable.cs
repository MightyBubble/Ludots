using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphOpDescriptorTable
    {
        private static GraphOpDescriptor[]? _byCode;

        private static GraphOpDescriptor[] ByCode => _byCode ??= Build();

        public static GraphOpDescriptor Get(GraphNodeOp op)
        {
            if (!TryGet(op, out GraphOpDescriptor descriptor))
            {
                throw new InvalidOperationException($"Graph opcode '{op}' has no descriptor.");
            }

            return descriptor;
        }

        public static bool TryGet(GraphNodeOp op, out GraphOpDescriptor descriptor)
        {
            ushort code = (ushort)op;
            if (op != GraphNodeOp.None &&
                code < ByCode.Length &&
                ByCode[code].Op == op)
            {
                descriptor = ByCode[code];
                return true;
            }

            descriptor = default;
            return false;
        }

        public static bool IsAuthorable(GraphKind kind, GraphNodeOp op)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.IsAuthorable(kind);

        public static GraphValueType GetLinearOutputType(GraphNodeOp op)
            => TryGet(op, out GraphOpDescriptor descriptor) ? descriptor.LinearOutputType : GraphValueType.Void;

        public static GraphValueType GetQueryOutputType(GraphNodeOp op)
            => TryGet(op, out GraphOpDescriptor descriptor) ? descriptor.QueryOutputType : GraphValueType.Void;

        public static bool IsAllowedLinearInputPort(GraphNodeOp op, string port)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.AllowsLinearInput(port);

        public static bool IsAllowedQueryInputPort(GraphNodeOp op, string port)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.AllowsQueryInput(port);

        public static bool IsAllowedScriptInputPort(GraphNodeOp op, string port)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.AllowsScriptInput(port);

        public static bool IsAllowedLinearOutputPort(GraphNodeOp op, string port)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.AllowsLinearOutput(port);

        public static bool IsAllowedQueryOutputPort(GraphNodeOp op, string port)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.AllowsQueryOutput(port);

        public static bool IsPolicyAllowed(GraphKind kind, GraphNodeOp op, in EffectOperationMetadata metadata)
        {
            GraphOpDescriptor descriptor = Get(op);
            if (descriptor.ScriptSliceOnly)
            {
                return kind is GraphKind.Script or GraphKind.TriggerGraph;
            }

            if (kind == GraphKind.Effect)
            {
                return true;
            }

            if (kind is GraphKind.Script or GraphKind.TriggerGraph)
            {
                // TriggerGraph already carves ModifyAttributeSet; WriteBlackboardFloat must also
                // stay GasTransactional for Effect-phase rollback while still authorable on
                // Script/TriggerGraph (Case E box_begin stores press corners on the operator rep).
                if (metadata.Kind == EffectOperationKind.GasTransactional &&
                    ((kind == GraphKind.TriggerGraph && op == GraphNodeOp.ModifyAttributeSet) ||
                     op == GraphNodeOp.WriteBlackboardFloat))
                {
                    return true;
                }

                return metadata.Kind == EffectOperationKind.Pure;
            }

            // Query / Score / Validation: Pure metadata alone is not enough — some world/UI
            // ops stay Pure so Script policy can host them (#1410). Reject WorldSideEffect
            // unless the op is explicitly authorable on this kind (Query may
            // DispatchCollectionEvent to write a collection itself).
            // Do not gate the Pure path on AuthorableKinds: compilers emit Jump etc.
            if (kind is GraphKind.Query or GraphKind.Score or GraphKind.Validation)
            {
                if (metadata.Kind != EffectOperationKind.Pure)
                {
                    return false;
                }

                if (!descriptor.WorldSideEffect)
                {
                    return true;
                }

                return descriptor.IsAuthorable(kind);
            }

            if (metadata.Kind == EffectOperationKind.Pure)
            {
                return true;
            }

            return kind == GraphKind.Derived && descriptor.DerivedAttributeWrite;
        }

        public static bool RequiresListenerOwnerContext(GraphNodeOp op)
            => TryGet(op, out GraphOpDescriptor descriptor) && descriptor.RequiresListenerOwnerContext;

        public static string[] ProjectCoverageAuthorableKinds(GraphNodeOp op)
        {
            GraphOpDescriptor descriptor = Get(op);
            var names = new List<string>(7);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.Derived);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.Effect);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.TriggerGraph);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.Query);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.Score);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.Script);
            AppendKind(names, descriptor.AuthorableKinds, GraphKind.Validation);
            return names.ToArray();
        }

        public static IEnumerable<GraphNodeOp> EnumerateAuthorable(GraphKind kind)
        {
            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                if (op != GraphNodeOp.None && IsAuthorable(kind, op))
                {
                    yield return op;
                }
            }
        }

        private static void AppendKind(List<string> names, GraphKindMask mask, GraphKind kind)
        {
            if ((mask & GraphOpDescriptor.ToMask(kind)) != 0)
            {
                names.Add(kind.ToString());
            }
        }
    }
}
