namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Describes which Main-slot Graph programs a PresetType injects into each Phase.
    /// Indexed by EffectPhaseId (0–7). A value of 0 means "no Main graph for this phase".
    /// </summary>
    public unsafe struct PresetBehaviorDescriptor
    {
        public fixed int MainGraphIds[EffectPhaseConstants.PhaseCount]; // indexed by EffectPhaseId

        public int GetMainGraphId(EffectPhaseId phase)
        {
            return MainGraphIds[(int)phase];
        }

        public void SetMainGraphId(EffectPhaseId phase, int graphProgramId)
        {
            MainGraphIds[(int)phase] = graphProgramId;
        }
    }

    /// <summary>
    /// Registry mapping PresetType → PresetBehaviorDescriptor.
    /// Each PresetType contributes a set of Main-slot Graph programs across the 8 lifecycle phases.
    /// </summary>
    public sealed class PresetBehaviorRegistry
    {
        /// <summary>Max number of preset types we can register (aligned with EffectPresetType byte range).</summary>
        public const int MaxPresetTypes = 256;

        private readonly PresetBehaviorDescriptor[] _descriptors = new PresetBehaviorDescriptor[MaxPresetTypes];

        /// <summary>Register Main Graph programs for a preset type.</summary>
        public void Register(EffectPresetType presetType, PresetBehaviorDescriptor descriptor)
        {
            _descriptors[(byte)presetType] = descriptor;
        }

        /// <summary>Get the descriptor for a preset type. Returns a zero-filled descriptor if not registered (= no Main graphs).</summary>
        public ref readonly PresetBehaviorDescriptor Get(EffectPresetType presetType)
        {
            return ref _descriptors[(byte)presetType];
        }

        /// <summary>Get the Main graph ID for a specific preset type + phase. Returns 0 if none.</summary>
        public int GetMainGraphId(EffectPresetType presetType, EffectPhaseId phase)
        {
            return _descriptors[(byte)presetType].GetMainGraphId(phase);
        }
    }
}
