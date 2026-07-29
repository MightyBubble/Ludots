using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Vision;

namespace Ludots.Core.Gameplay.GAS
{
    public enum EffectPresetType : byte
    {
        None = 0,
        ApplyForce2D = 1,
        InstantDamage = 2,
        DoT = 3,
        Heal = 4,
        HoT = 5,
        Buff = 6,
        /// <summary>One-shot area search + dispatch payload.</summary>
        Search = 7,
        /// <summary>Periodic area search + dispatch payload (requires DurationParams).</summary>
        PeriodicSearch = 8,
        LaunchProjectile = 9,
        CreateUnit = 10,
        /// <summary>Displacement effect: dash, knockback, pull, blink.</summary>
        Displacement = 11,
        /// <summary>Entity relation effect: garrison, detach, or link parent-child state.</summary>
        Relation = 12,
        /// <summary>Rule-constrained settlement through the Exchange runtime.</summary>
        Exchange = 13,
        /// <summary>Completes an entity-scoped progression through the Progression runtime.</summary>
        CompleteProgression = 14,
        /// <summary>Reads configured blackboard stored target keys and submits orders to a spawned unit.</summary>
        SubmitOrderFromBlackboard = 15,
        /// <summary>Atomic entity template replacement with inheritance profile.</summary>
        DeployConsumeSource = 16,
    }

    // ── TargetResolver: pluggable target fan-out for effects ──

    /// <summary>
    /// How the resolver collects targets.
    /// None = no fan-out (default, current 1:1 behavior).
    /// BuiltinSpatial = built-in spatial shape query (circle/cone/rect/line/ring).
    /// GraphProgram = run a graph VM program for arbitrary logic
    ///                (relationship, hex, connectivity, etc.).
    /// </summary>
    public enum TargetResolverKind : byte
    {
        None = 0,
        BuiltinSpatial = 1,
        GraphProgram = 2,
    }

    /// <summary>
    /// Built-in spatial shapes for BuiltinSpatial resolvers.
    /// </summary>
    public enum SpatialShape : byte
    {
        Circle = 0,
        Cone = 1,
        Rectangle = 2,
        Line = 3,
        Ring = 4,   // donut / nova wave
    }

    public enum SpatialQueryOrigin : byte
    {
        Default = 0,
        Source = 1,
    }

    /// <summary>
    /// Identifies which entity from the original EffectContext fills a role
    /// in the payload EffectRequest.
    /// </summary>
    public enum ContextSlot : byte
    {
        OriginalSource = 0,
        OriginalTarget = 1,
        OriginalTargetContext = 2,
        ResolvedEntity = 3,   // the entity found by the resolver
    }

    /// <summary>
    /// Configures how Source/Target/TargetContext are mapped when creating
    /// a payload EffectRequest for each resolved target.
    ///
    /// Example presets:
    ///   AOE damage:    PayloadSource=OriginalSource, PayloadTarget=ResolvedEntity, PayloadTargetContext=OriginalTarget
    ///   Reflect:       PayloadSource=OriginalTarget, PayloadTarget=OriginalSource, PayloadTargetContext=OriginalTarget
    ///   Redirect:      PayloadSource=OriginalSource, PayloadTarget=OriginalTargetContext, PayloadTargetContext=OriginalTarget
    /// </summary>
    public struct TargetResolverContextMapping
    {
        public ContextSlot PayloadSource;
        public ContextSlot PayloadTarget;
        public ContextSlot PayloadTargetContext;

        /// <summary>Default: Source=caster, Target=each resolved entity, TargetContext=original target (AOE center).</summary>
        public static TargetResolverContextMapping Default => new()
        {
            PayloadSource = ContextSlot.OriginalSource,
            PayloadTarget = ContextSlot.ResolvedEntity,
            PayloadTargetContext = ContextSlot.OriginalTarget,
        };
    }

    /// <summary>
    /// Parameters for BuiltinSpatial resolvers.
    /// All lengths are in centimeters; angles in degrees.
    /// </summary>
    public struct BuiltinSpatialDescriptor
    {
        public SpatialShape Shape;
        public int RadiusCm;           // Circle/Cone range, Ring outer radius
        public int InnerRadiusCm;      // Ring inner radius (Nova)
        public int HalfAngleDeg;       // Cone half-angle
        public int HalfWidthCm;        // Rectangle half-width, Line half-width
        public int HalfHeightCm;       // Rectangle half-height
        public int RotationDeg;        // Rectangle rotation
        public int LengthCm;           // Line length
        public SpatialQueryOrigin Origin;
        public RelationshipFilter RelationFilter;
        public bool ExcludeSource;
        public int MaxTargets;         // 0 = unlimited (budget-limited only)
        public uint LayerMask;         // 0 = no layer filter
    }

    // ── Three-layer Target Resolution ──

    /// <summary>
    /// How to FIND targets (spatial query, hex, graph, relationship...).
    /// </summary>
    public struct TargetQueryDescriptor
    {
        public TargetResolverKind Kind;
        public BuiltinSpatialDescriptor Spatial;   // used when Kind=BuiltinSpatial
        public int GraphProgramId;                  // used when Kind=GraphProgram
    }

    /// <summary>
    /// How to FILTER found candidates (relationship, layer, max count...).
    /// </summary>
    public struct TargetFilterDescriptor
    {
        public RelationshipFilter RelationFilter;
        public bool ExcludeSource;
        public int MaxTargets;                     // 0 = unlimited
        public uint LayerMask;                     // 0 = no layer filter
    }

    /// <summary>
    /// How to DISPATCH payload effects to each filtered target.
    /// </summary>
    public struct TargetDispatchDescriptor
    {
        public int PayloadEffectTemplateId;
        public TargetResolverContextMapping ContextMapping;
    }

    // ── Projectile + UnitCreation descriptors ──

    /// <summary>
    /// Parameters for LaunchProjectile effects.
    /// </summary>
    public struct ProjectileDescriptor
    {
        /// <summary>Projectile speed in cm/tick.</summary>
        public int Speed;
        /// <summary>Maximum range in cm.</summary>
        public int Range;
        /// <summary>Arc height for parabolic trajectories (0 = straight line).</summary>
        public int ArcHeight;
        /// <summary>Effect template ID to apply when the projectile completes its travel.</summary>
        public int ImpactEffectTemplateId;
        /// <summary>Effect template ID to apply to entities hit during travel.</summary>
        public int HitEffectTemplateId;
        /// <summary>Effect template ID used only for presentation startup binding.</summary>
        public int PresentationEffectTemplateId;
        /// <summary>How the projectile computes movement.</summary>
        public ProjectileTravelMode TravelMode;
        /// <summary>How the projectile reacts to mid-flight entity collisions.</summary>
        public ProjectileImpactPolicy ImpactPolicy;
        /// <summary>Collision query half-width in centimeters.</summary>
        public int CollisionHalfWidthCm;
        /// <summary>Relationship filter applied to collision hits.</summary>
        public RelationshipFilter CollisionRelationFilter;
        /// <summary>Whether to ignore the source entity in collision hits.</summary>
        public bool CollisionExcludeSource;
        /// <summary>Maximum number of distinct collision hits before despawn. Must fit ProjectileState.HitHistoryCapacity.</summary>
        public int MaxHitCount;
    }

    public enum ProjectileTravelMode : byte
    {
        Direction = 1,
        TrackTarget = 2,
    }

    public enum ProjectileImpactPolicy : byte
    {
        DestroyOnFirstHit = 1,
        ContinueOnHit = 2,
    }

    /// <summary>
    /// Parameters for CreateUnit effects.
    /// </summary>
    public struct UnitCreationDescriptor
    {
        public UnitCreationPlacementPattern PlacementPattern;
        public UnitCreationFacingPattern FacingPattern;
        /// <summary>Unit type identifier (resolved from string at load time).</summary>
        public int UnitTypeId;
        /// <summary>Template identifier for template-backed runtime manifestation spawns.</summary>
        public string TemplateId;
        /// <summary>True when CreateUnit should materialize a template instead of a bare UnitType.</summary>
        public bool UseTemplateSpawn;
        /// <summary>Number of units to create.</summary>
        public int Count;
        /// <summary>Spawn offset radius from target position (cm).</summary>
        public int OffsetRadius;
        /// <summary>Effect template ID to apply to each spawned unit.</summary>
        public int OnSpawnEffectTemplateId;
        /// <summary>Whether the spawned unit should inherit PlayerOwner from the source entity.</summary>
        public bool CopySourcePlayerOwner;
        /// <summary>Whether the source entity should become the parent relation of the spawned unit.</summary>
        public bool LinkSourceAsParent;
        /// <summary>Fixed formation radius in centimeters for non-scatter patterns.</summary>
        public int PlacementRadiusCm;
        /// <summary>Starting angle for circular formation patterns in degrees.</summary>
        public int PlacementStartAngleDeg;
    }

    public enum UnitCreationPlacementPattern : byte
    {
        Scatter = 0,
        Circle = 1,
    }

    public enum UnitCreationFacingPattern : byte
    {
        PreserveTemplate = 0,
        RadialOutward = 1,
        TangentClockwise = 2,
        TangentCounterClockwise = 3,
    }

    /// <summary>
    /// Direction mode for displacement effects.
    /// </summary>
    public enum DisplacementDirectionMode : byte
    {
        /// <summary>Move toward target entity.</summary>
        ToTarget = 0,
        /// <summary>Move away from source entity (knockback).</summary>
        AwayFromSource = 1,
        /// <summary>Move toward source entity (pull/hook).</summary>
        TowardSource = 2,
        /// <summary>Fixed direction in radians (blink, no animation).</summary>
        Fixed = 3,
    }

    /// <summary>
    /// Parameters for Displacement effects.
    /// All distances in centimeters, all durations in ticks.
    /// </summary>
    public struct DisplacementDescriptor
    {
        public DisplacementDirectionMode DirectionMode;
        /// <summary>Fixed direction in radians (only used when DirectionMode=Fixed). Stored as int for Fix64 conversion.</summary>
        public int FixedDirectionDeg;
        /// <summary>Total distance to travel in centimeters.</summary>
        public int TotalDistanceCm;
        /// <summary>Total duration in ticks.</summary>
        public int TotalDurationTicks;
        /// <summary>Whether to override navigation input during displacement.</summary>
        public bool OverrideNavigation;
    }

    public enum RelationOperation : byte
    {
        None = 0,
        SetParent = 1,
        RemoveParent = 2,
        EnsureLink = 3,
    }

    public enum RelationEntitySlot : byte
    {
        None = 0,
        Source = 1,
        Target = 2,
        TargetContext = 3,
    }

    public struct RelationDescriptor
    {
        public RelationOperation Operation;
        public RelationEntitySlot Subject;
        public RelationEntitySlot Parent;
        public bool SnapSubjectToParentPosition;
        public int RelationshipTypeId;
    }

    /// <summary>
    /// Parameters for on-spawn order dispatch from configured blackboard stored target keys.
    /// </summary>
    public struct SubmitOrderFromBlackboardDescriptor
    {
        public RelationEntitySlot SourceSlot;
        public RelationEntitySlot TargetSlot;
        public BlackboardStoredTargetKeys StoredTargetKeys;
        public int PointMoveOrderTypeId;
        public int EntityOrderTypeId;
        public int EntityOrderIntArg0;
        public OrderSubmitMode SubmitMode;
    }

    // ── EffectTemplateData ──

    public struct EffectTemplateData
    {
        public int TagId;
        public EffectPresetType PresetType;
        public int PresetAttribute0;
        public int PresetAttribute1;
        public EffectLifetimeKind LifetimeKind;
        public GasClockId ClockId;
        public int DurationTicks;
        public int PeriodTicks;
        public GasConditionHandle ExpireCondition;

        public bool ParticipatesInResponse;
        public EffectModifiers Modifiers;

        // ── Target fan-out (three-layer split) ──
        public TargetQueryDescriptor TargetQuery;
        public TargetFilterDescriptor TargetFilter;
        public TargetDispatchDescriptor TargetDispatch;

        // ── Projectile + UnitCreation + Displacement ──
        public ProjectileDescriptor Projectile;
        public UnitCreationDescriptor UnitCreation;
        public DisplacementDescriptor Displacement;
        public RelationDescriptor Relation;
        public KnowledgeAreaRevealDescriptor RevealArea;
        public SubmitOrderFromBlackboardDescriptor SubmitOrderFromBlackboard;
        public ScopeKey ProgressionScope;
        public Ludots.Core.Gameplay.Progression.ProgressionLevelChange ProgressionChange;
        public int ProgressionId;

        // ── Phase Graph bindings ──
        public EffectPhaseGraphBindings PhaseGraphBindings;
        public EffectConfigParams ConfigParams;

        // ── Phase Listeners (bound to this effect's lifecycle) ──
        // Uses EffectPhaseListenerBuffer directly; OwnerEffectIds are 0 at compile-time,
        // filled with real entity ids at runtime registration.
        public EffectPhaseListenerBuffer ListenerSetup;

        // ── Granted Tags: tags contributed by this effect to the target ──
        public EffectGrantedTags GrantedTags;

        // ── Stack Policy ──
        public bool HasStackPolicy;
        public StackPolicy StackPolicy;
        public StackOverflowPolicy StackOverflowPolicy;
        public int StackLimit;

        /// <summary>
        /// Returns true if this template has a configured TargetResolver (spatial query or graph program).
        /// Templates with target resolvers require entity-based processing for fan-out.
        /// </summary>
        public readonly bool HasTargetResolver => TargetQuery.Kind != TargetResolverKind.None;
    }

    public sealed class EffectTemplateRegistry
    {
        public const int MaxTemplates = 4096;
        public const string DuplicateRegistrationError = "GAS.EFFECT_TEMPLATE.ERR.DuplicateRegistration";

        private readonly EffectTemplateData[] _templates = new EffectTemplateData[MaxTemplates];
        private readonly ulong[] _hasBits = new ulong[MaxTemplates >> 6];
        private readonly System.Collections.Generic.Dictionary<int, string> _registrationSource = new();
        private Ludots.Core.Modding.RegistrationConflictReport _conflictReport;

        public void SetConflictReport(Ludots.Core.Modding.RegistrationConflictReport report)
        {
            _conflictReport = report;
        }

        public void Clear()
        {
            Array.Clear(_templates, 0, _templates.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
            _registrationSource.Clear();
        }

        public void Register(int templateId, in EffectTemplateData data, string modId = null)
        {
            if ((uint)templateId >= MaxTemplates) throw new ArgumentOutOfRangeException(nameof(templateId));
            int word = templateId >> 6;
            int bit = templateId & 63;
            if ((_hasBits[word] & (1UL << bit)) != 0)
            {
                string existingMod = _registrationSource.TryGetValue(templateId, out var em) ? em : "(core)";
                string newMod = modId ?? "(core)";
                _conflictReport?.Add("EffectTemplateRegistry", templateId.ToString(), existingMod, newMod);
                throw new InvalidOperationException(
                    $"{DuplicateRegistrationError}: templateId={templateId}, existingSource='{existingMod}', newSource='{newMod}'.");
            }

            _templates[templateId] = data;
            _hasBits[word] |= 1UL << bit;
            _registrationSource[templateId] = modId ?? "(core)";
        }

        public bool TryGet(int templateId, out EffectTemplateData data)
        {
            if ((uint)templateId >= MaxTemplates)
            {
                data = default;
                return false;
            }

            int word = templateId >> 6;
            int bit = templateId & 63;
            if ((_hasBits[word] & (1UL << bit)) == 0)
            {
                data = default;
                return false;
            }

            data = _templates[templateId];
            return true;
        }

        /// <summary>
        /// Check whether a template exists and return its array index (same as templateId).
        /// Use with <see cref="GetRef"/> to avoid copying the large EffectTemplateData struct.
        /// </summary>
        public bool TryGetRef(int templateId, out int index)
        {
            if ((uint)templateId >= MaxTemplates)
            {
                index = -1;
                return false;
            }

            int word = templateId >> 6;
            int bit = templateId & 63;
            if ((_hasBits[word] & (1UL << bit)) == 0)
            {
                index = -1;
                return false;
            }

            index = templateId;
            return true;
        }

        /// <summary>
        /// Returns a readonly reference to the template data, avoiding struct copy.
        /// Caller must ensure templateId is valid (e.g. via <see cref="TryGetRef"/>).
        /// </summary>
        public ref readonly EffectTemplateData GetRef(int templateId)
        {
            return ref _templates[templateId];
        }
    }
}
