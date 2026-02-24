namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Defines per-Phase Graph bindings for an Effect.
    /// Each step is a (Phase, Slot, GraphProgramId) triple — no StepKind needed,
    /// because all behavior (trigger child effect, compute formula, write BB) is
    /// expressed as Graph nodes.
    /// Replaces the legacy EffectCallbackComponent.
    /// </summary>
    public unsafe struct EffectPhaseGraphBindings
    {
        public const int MAX_STEPS = GasConstants.EFFECT_PHASE_GRAPH_MAX_STEPS; // 8 phases × 2 user slots (Pre/Post)

        /// <summary>Number of configured steps.</summary>
        public int StepCount;

        /// <summary>EffectPhaseId for each step.</summary>
        public fixed byte StepPhases[MAX_STEPS];

        /// <summary>PhaseSlot (Pre/Post) for each step. Main is never stored here — it comes from PresetBehaviorRegistry.</summary>
        public fixed byte StepSlots[MAX_STEPS];

        /// <summary>GraphProgramId for each step (resolved at load time).</summary>
        public fixed int StepGraphIds[MAX_STEPS];

        /// <summary>
        /// Bitmask: bit N = 1 means SkipMain for EffectPhaseId N.
        /// When set, the Preset's Main graph for that phase is skipped;
        /// the user's Pre/Post graphs take full control.
        /// </summary>
        public byte SkipMainFlags;

        /// <summary>Check whether Main should be skipped for a given phase.</summary>
        public readonly bool IsSkipMain(EffectPhaseId phase)
        {
            return (SkipMainFlags & (1 << (int)phase)) != 0;
        }

        /// <summary>Set the SkipMain flag for a phase.</summary>
        public void SetSkipMain(EffectPhaseId phase)
        {
            SkipMainFlags |= (byte)(1 << (int)phase);
        }

        /// <summary>Add a step. Returns false if capacity exceeded.</summary>
        public bool TryAddStep(EffectPhaseId phase, PhaseSlot slot, int graphProgramId)
        {
            if (StepCount >= MAX_STEPS) return false;
            StepPhases[StepCount] = (byte)phase;
            StepSlots[StepCount] = (byte)slot;
            StepGraphIds[StepCount] = graphProgramId;
            StepCount++;
            return true;
        }

        /// <summary>Get the GraphProgramId for a specific (phase, slot). Returns 0 if not found.</summary>
        public readonly int GetGraphId(EffectPhaseId phase, PhaseSlot slot)
        {
            byte p = (byte)phase;
            byte s = (byte)slot;
            for (int i = 0; i < StepCount; i++)
            {
                if (StepPhases[i] == p && StepSlots[i] == s)
                    return StepGraphIds[i];
            }
            return 0;
        }
    }
}
