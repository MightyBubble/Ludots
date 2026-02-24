namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Handler type: C# builtin function or Graph program.
    /// Builtin and Graph are interchangeable at the configuration level.
    /// </summary>
    public enum PhaseHandlerKind : byte
    {
        None = 0,
        Builtin = 1,
        Graph = 2,
    }

    /// <summary>
    /// Unified phase handler reference. Does not distinguish between C# and Graph —
    /// both are just "a function that runs at a phase".
    /// </summary>
    public struct PhaseHandler
    {
        public PhaseHandlerKind Kind;
        /// <summary>BuiltinHandlerId (when Kind=Builtin) or GraphProgramId (when Kind=Graph).</summary>
        public int HandlerId;

        public static PhaseHandler Builtin(BuiltinHandlerId id) => new() { Kind = PhaseHandlerKind.Builtin, HandlerId = (int)id };
        public static PhaseHandler Graph(int graphId) => new() { Kind = PhaseHandlerKind.Graph, HandlerId = graphId };
        public static PhaseHandler None => default;
        public bool IsValid => Kind != PhaseHandlerKind.None;
    }

    /// <summary>
    /// Fixed-size map from EffectPhaseId (0–7) to PhaseHandler.
    /// Stored inline in PresetTypeDefinition — zero heap allocation.
    /// </summary>
    public unsafe struct PhaseHandlerMap
    {
        private fixed long _data[EffectPhaseConstants.PhaseCount]; // PhaseHandler is 8 bytes (Kind:1 + pad:3 + HandlerId:4)

        public PhaseHandler this[EffectPhaseId phase]
        {
            get
            {
                int idx = (int)phase;
                if ((uint)idx >= EffectPhaseConstants.PhaseCount) return default;
                long raw = _data[idx];
                return *(PhaseHandler*)&raw;
            }
            set
            {
                int idx = (int)phase;
                if ((uint)idx >= EffectPhaseConstants.PhaseCount) return;
                _data[idx] = *(long*)&value;
            }
        }
    }
}
