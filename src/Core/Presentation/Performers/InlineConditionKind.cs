namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Lightweight built-in predicate checks evaluated without the Graph runtime.
    /// Only pure infrastructure concepts — no gameplay/business terms.
    /// Anything involving team relationships, attribute thresholds, etc. must use
    /// a Graph program via <see cref="ConditionRef.GraphProgramId"/>.
    /// </summary>
    public enum InlineConditionKind : byte
    {
        /// <summary>Always true (no condition).</summary>
        None = 0,

        /// <summary>True when the event Source entity is the local player.</summary>
        SourceIsLocalPlayer = 1,

        /// <summary>True when the event Target entity is the local player.</summary>
        TargetIsLocalPlayer = 2,

        /// <summary>True when the event Source entity is alive in the ECS world.</summary>
        SourceIsAlive = 3,

        /// <summary>True when the event Target entity is alive in the ECS world.</summary>
        TargetIsAlive = 4,

        /// <summary>True when the event is TagEffectiveChanged with Magnitude > 0 (tag gained).</summary>
        TagGained = 5,

        /// <summary>True when the event is TagEffectiveChanged with Magnitude == 0 (tag lost).</summary>
        TagLost = 6,

        /// <summary>True when the Owner entity's CullState.IsVisible is true.</summary>
        OwnerCullVisible = 7,
    }
}
