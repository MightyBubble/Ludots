using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class EffectTemplateConfig : IIdentifiable
    {
        public string Id { get; set; }
        public List<string> Tags { get; set; }

        // ── Top-level structural fields ──
        public string PresetType { get; set; }

        /// <summary>Lifetime kind: "Instant" | "After" | "Infinite".</summary>
        public string Lifetime { get; set; }
        /// <summary>
        /// Whether this effect participates in the ResponseChain.
        /// Default: true. Set to false to exclude from response processing.
        /// </summary>
        public bool ParticipatesInResponse { get; set; } = true;

        /// <summary>
        /// Optional tag-based expire condition. Effect expires when condition is no longer satisfied.
        /// Example: { "kind": "TagPresent", "tag": "Status.Stun" } — expires when tag is removed.
        /// </summary>
        public ExpireConditionConfig ExpireCondition { get; set; }

        // ── Component blocks (new architecture) ──

        /// <summary>Duration component: durationTicks, periodTicks, clockId.</summary>
        public DurationConfig Duration { get; set; }
        /// <summary>Attribute modifier list.</summary>
        public List<ModifierConfig> Modifiers { get; set; }
        /// <summary>Target query strategy.</summary>
        public TargetQueryConfig TargetQuery { get; set; }
        /// <summary>Target filter conditions.</summary>
        public TargetFilterConfig TargetFilter { get; set; }
        /// <summary>Target dispatch configuration.</summary>
        public TargetDispatchConfig TargetDispatch { get; set; }
        /// <summary>Projectile parameters.</summary>
        public ProjectileConfig Projectile { get; set; }
        /// <summary>Unit creation parameters.</summary>
        public UnitCreationConfig UnitCreation { get; set; }

        // ── Capability blocks ──

        /// <summary>Per-phase graph bindings (Pre/Post/SkipMain).</summary>
        public Dictionary<string, PhaseGraphConfig> PhaseGraphs { get; set; }
        /// <summary>Phase listeners bound to this effect's lifecycle.</summary>
        public List<PhaseListenerConfig> PhaseListeners { get; set; }

        /// <summary>
        /// Config parameters for graph parameterization.
        /// Keys are arbitrary config key names; values have "type" and "value".
        /// </summary>
        public Dictionary<string, ConfigParamConfig> ConfigParams { get; set; }

        /// <summary>
        /// Tags granted by this effect to the target entity.
        /// Tag counts are contributed based on formula and stack count.
        /// </summary>
        public List<GrantedTagConfig> GrantedTags { get; set; }

        /// <summary>
        /// Stack configuration for duration effects (optional).
        /// If omitted, each application creates a separate effect entity.
        /// </summary>
        public StackConfig Stack { get; set; }

        // Legacy fields (ForceXAttribute, ForceYAttribute, DurationType, DurationFrames,
        // PeriodFrames, OnApplyEffect, OnPeriodEffect, OnExpireEffect, OnRemoveEffect) have
        // been fully removed. ApplyForce2D now uses configParams with type "attribute" for
        // target attributes. Callbacks use phaseGraphs. Duration uses the "duration" block.
    }

    public sealed class ModifierConfig
    {
        public string Attribute { get; set; }
        public string Op { get; set; }
        public float Value { get; set; }
    }

    public sealed class ContextMappingConfig
    {
        public string PayloadSource { get; set; }
        public string PayloadTarget { get; set; }
        public string PayloadTargetContext { get; set; }
    }

    /// <summary>
    /// Per-phase graph binding configuration.
    /// </summary>
    public sealed class PhaseGraphConfig
    {
        /// <summary>Pre-slot graph program name (runs before preset Main).</summary>
        public string Pre { get; set; }
        /// <summary>Post-slot graph program name (runs after preset Main).</summary>
        public string Post { get; set; }
        /// <summary>If true, skip the preset Main graph for this phase.</summary>
        public bool SkipMain { get; set; }
    }

    /// <summary>
    /// Single config parameter entry.
    /// </summary>
    public sealed class ConfigParamConfig
    {
        /// <summary>"float", "int", or "effectTemplate"</summary>
        public string Type { get; set; }
        /// <summary>Value (parsed based on Type). For effectTemplate, this is a template name string.</summary>
        public object Value { get; set; }
    }

    /// <summary>
    /// Per-listener configuration for effect-bound phase listeners.
    /// </summary>
    public sealed class PhaseListenerConfig
    {
        public string ListenTag { get; set; }
        public string ListenEffectId { get; set; }
        public string Phase { get; set; }
        public string Scope { get; set; }
        public string Action { get; set; }
        public string GraphProgram { get; set; }
        public string EventTag { get; set; }
        public int Priority { get; set; }
    }

    // ── New component config blocks ──

    /// <summary>Duration component configuration.</summary>
    public sealed class DurationConfig
    {
        public int DurationTicks { get; set; }
        public int PeriodTicks { get; set; }
        public string ClockId { get; set; }
    }

    /// <summary>Target query configuration (how to FIND targets).</summary>
    public sealed class TargetQueryConfig
    {
        public string Kind { get; set; }          // "BuiltinSpatial" | "GraphProgram"
        public string Shape { get; set; }         // "Circle" | "Cone" | "Rectangle" | "Line" | "Ring"
        public int Radius { get; set; }
        public int InnerRadius { get; set; }
        public int HalfAngle { get; set; }
        public int HalfWidth { get; set; }
        public int HalfHeight { get; set; }
        public int Rotation { get; set; }
        public int Length { get; set; }
        public int GraphProgramId { get; set; }
    }

    /// <summary>Target filter configuration (how to FILTER candidates).</summary>
    public sealed class TargetFilterConfig
    {
        public string RelationFilter { get; set; }
        public bool ExcludeSource { get; set; }
        public int MaxTargets { get; set; }
        public List<string> LayerMask { get; set; }
    }

    /// <summary>Target dispatch configuration (how to DISPATCH payload effects).</summary>
    public sealed class TargetDispatchConfig
    {
        public string PayloadEffect { get; set; }
        public ContextMappingConfig ContextMapping { get; set; }
    }

    /// <summary>Projectile component configuration.</summary>
    public sealed class ProjectileConfig
    {
        public int Speed { get; set; }
        public int Range { get; set; }
        public int ArcHeight { get; set; }
        public string ImpactEffect { get; set; }
    }

    /// <summary>Unit creation component configuration.</summary>
    public sealed class UnitCreationConfig
    {
        public string UnitType { get; set; }
        public int Count { get; set; } = 1;
        public int OffsetRadius { get; set; }
        public string OnSpawnEffect { get; set; }
    }

    /// <summary>
    /// Tag-based expire condition. The effect stays alive while the condition is satisfied.
    /// </summary>
    public sealed class ExpireConditionConfig
    {
        /// <summary>"TagPresent" or "TagAbsent".</summary>
        public string Kind { get; set; }
        /// <summary>Tag name to check (e.g., "Status.Stun").</summary>
        public string Tag { get; set; }
        /// <summary>"Raw" or "Effective". Defaults to "Effective".</summary>
        public string Sense { get; set; }
    }

    /// <summary>
    /// Stack policy configuration for duration effects.
    /// </summary>
    public sealed class StackConfig
    {
        /// <summary>Maximum number of stacks allowed (0 = no limit).</summary>
        public int Limit { get; set; }
        /// <summary>"RefreshDuration" | "AddDuration" | "KeepDuration". Defaults to "RefreshDuration".</summary>
        public string Policy { get; set; }
        /// <summary>"RejectNew" | "RemoveOldest". Defaults to "RejectNew".</summary>
        public string OverflowPolicy { get; set; }
    }

    /// <summary>
    /// Configuration for a single granted tag contribution.
    /// </summary>
    public sealed class GrantedTagConfig
    {
        /// <summary>Tag name (e.g., "Status.Slow").</summary>
        public string Tag { get; set; }
        /// <summary>"Fixed" | "Linear" | "LinearPlusBase" | "GraphProgram". Defaults to "Fixed".</summary>
        public string Formula { get; set; }
        /// <summary>Coefficient for formula computation.</summary>
        public int Amount { get; set; } = 1;
        /// <summary>Base value for LinearPlusBase formula.</summary>
        public int Base { get; set; }
        /// <summary>Graph program name for GraphProgram formula.</summary>
        public string GraphProgram { get; set; }
    }
}
