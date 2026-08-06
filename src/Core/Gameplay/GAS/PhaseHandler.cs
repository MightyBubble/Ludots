namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Handler type for preset phase defaults.
    /// Preset types reference Graph programs; Graph ops own the lowest-level behavior.
    /// </summary>
    public enum PhaseHandlerKind : byte
    {
        None = 0,
        Graph = 1,
    }

    /// <summary>
    /// Preset phase handler reference.
    /// </summary>
    public struct PhaseHandler
    {
        public PhaseHandlerKind Kind;
        /// <summary>GraphProgramId when Kind=Graph.</summary>
        public int HandlerId;

        public static PhaseHandler Graph(int graphId) => new() { Kind = PhaseHandlerKind.Graph, HandlerId = graphId };
        public static PhaseHandler None => default;
        public bool IsValid => Kind != PhaseHandlerKind.None;
    }

    /// <summary>
    /// Fixed-size map from EffectPhaseId (0..N) to PhaseHandler.
    /// Stored inline in PresetTypeDefinition; zero heap allocation.
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
