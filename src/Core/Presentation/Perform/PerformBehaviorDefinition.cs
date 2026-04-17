using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// Behavior-level authored contract for the unified perform architecture.
    /// This first-wave type is additive and can be populated from existing presentation behavior assets.
    /// </summary>
    public sealed class PerformBehaviorDefinition
    {
        public int DefinitionId;
        public string Id = string.Empty;
        public PerformBehaviorKind Kind;
        public int LegacyPresentationBehaviorId;
        /// <summary>
        /// Transitional asset binding target consumed by perform behaviors.
        /// This remains additive until legacy presentation-behavior-to-prefab resolution is removed.
        /// </summary>
        public int PrefabAssetId;
        public ConditionRef ActivationCondition;
    }
}
