using System;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    [Flags]
    public enum GraphKindMask : byte
    {
        None = 0,
        Effect = 1 << 0,
        Query = 1 << 1,
        Score = 1 << 2,
        Validation = 1 << 3,
        Derived = 1 << 4,
        Script = 1 << 5,
        TriggerGraph = 1 << 6
    }

    public enum GraphOperandRole : byte
    {
        None = 0,
        DstRegister = 1,
        SrcRegisterA = 2,
        SrcRegisterB = 3,
        SrcRegisterC = 4,
        BoolScratchFlags = 5,
        SymbolImm = 6,
        SymbolFlags = 7,
        SymbolDst = 8,
        Immediate = 9,
        ImmediateFloat = 10,
        FuncLibNameFlags = 11,
        SpatialCapacityFlags = 12,
        SortDescendingFlags = 13,
        TeamIdSourceFlags = 14,
        RelationshipTypeFlags = 15,
        ReasonIdDst = 16,
        DispatchPresetDst = 17,
        SrcRegisterFlags = 18
    }

    public readonly struct GraphOpDescriptor
    {
        public GraphOpDescriptor(
            GraphNodeOp op,
            GraphKindMask authorableKinds,
            GraphValueType linearOutputType,
            GraphValueType queryOutputType,
            string[] linearInputPorts,
            string[] queryInputPorts,
            string[] scriptInputPorts,
            GraphOperandRole dstRole,
            GraphOperandRole flagsRole,
            GraphOperandRole immRole,
            bool scriptSliceOnly,
            bool derivedAttributeWrite,
            bool requiresListenerOwnerContext,
            bool worldSideEffect = false)
        {
            Op = op;
            AuthorableKinds = authorableKinds;
            LinearOutputType = linearOutputType;
            QueryOutputType = queryOutputType;
            LinearInputPorts = linearInputPorts;
            QueryInputPorts = queryInputPorts;
            ScriptInputPorts = scriptInputPorts;
            DstRole = dstRole;
            FlagsRole = flagsRole;
            ImmRole = immRole;
            ScriptSliceOnly = scriptSliceOnly;
            DerivedAttributeWrite = derivedAttributeWrite;
            RequiresListenerOwnerContext = requiresListenerOwnerContext;
            WorldSideEffect = worldSideEffect;
        }

        public GraphNodeOp Op { get; }
        public GraphKindMask AuthorableKinds { get; }
        public GraphValueType LinearOutputType { get; }
        public GraphValueType QueryOutputType { get; }
        public string[] LinearInputPorts { get; }
        public string[] QueryInputPorts { get; }
        public string[] ScriptInputPorts { get; }
        public GraphOperandRole DstRole { get; }
        public GraphOperandRole FlagsRole { get; }
        public GraphOperandRole ImmRole { get; }
        /// <summary>True for host-slice control-flow ops (Yield): only Script and host-resumed TriggerGraph may contain them.</summary>
        public bool ScriptSliceOnly { get; }
        public bool DerivedAttributeWrite { get; }
        public bool RequiresListenerOwnerContext { get; }
        /// <summary>
        /// True when the op mutates world/UI/map state despite Pure effect metadata
        /// (Script policy still needs Pure). Query/Score/Validation must reject these.
        /// </summary>
        public bool WorldSideEffect { get; }

        public bool IsAuthorable(GraphKind kind)
            => (AuthorableKinds & ToMask(kind)) != 0;

        public bool AllowsLinearInput(string port)
            => ContainsPort(LinearInputPorts, port);

        public bool AllowsQueryInput(string port)
            => ContainsPort(QueryInputPorts, port);

        public bool AllowsScriptInput(string port)
            => ContainsPort(ScriptInputPorts, port);

        public bool AllowsLinearOutput(string port)
            => LinearOutputType != GraphValueType.Void &&
               port == GraphControlFlowPorts.Value;

        public bool AllowsQueryOutput(string port)
        {
            if (QueryOutputType == GraphValueType.TargetList)
            {
                return port == GraphControlFlowPorts.List;
            }

            return QueryOutputType != GraphValueType.Void && port == GraphControlFlowPorts.Value;
        }

        public static GraphKindMask ToMask(GraphKind kind)
            => kind switch
            {
                GraphKind.Effect => GraphKindMask.Effect,
                GraphKind.Query => GraphKindMask.Query,
                GraphKind.Score => GraphKindMask.Score,
                GraphKind.Validation => GraphKindMask.Validation,
                GraphKind.Derived => GraphKindMask.Derived,
                GraphKind.Script => GraphKindMask.Script,
                GraphKind.TriggerGraph => GraphKindMask.TriggerGraph,
                _ => GraphKindMask.None
            };

        private static bool ContainsPort(string[] ports, string port)
        {
            if (ports == null || ports.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < ports.Length; i++)
            {
                if (string.Equals(ports[i], port, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
