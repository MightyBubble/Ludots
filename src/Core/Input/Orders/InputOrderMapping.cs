using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Interaction mode determines HOW InputActions become Orders.
    /// This is a game-level / player-preference setting, NOT per-ability.
    ///
    /// TargetFirst (WoW): player selects target first, then presses ability key ->order submitted immediately.
    /// SmartCast (LoL): player presses ability key -> order submitted immediately using the
    /// mapping's explicit target source.
    /// AimCast (DotA/WC3): player presses ability key ->enters aiming phase ->confirm action submits, cancel action exits.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InteractionModeType
    {
        /// <summary>WoW style: select target, press key ->instant cast.</summary>
        TargetFirst = 0,

        /// <summary>LoL style: press key ->cast at cursor / hovered entity.</summary>
        SmartCast = 1,

        /// <summary>DotA/WC3 style: press key ->aiming ->click confirm.</summary>
        AimCast = 2,

        /// <summary>
        /// LoL "Quick Cast with Indicator" style: hold key ->show indicator,
        /// release key ->cast at cursor position. The configured cancel action exits.
        /// </summary>
        SmartCastWithIndicator = 3,

        /// <summary>
        /// Action-game style: system scores the current context and chooses the best cast slot + target.
        /// </summary>
        ContextScored = 4,

        /// <summary>
        /// Press and release the skill key first, then enter aiming and wait for mouse confirm.
        /// </summary>
        PressReleaseAimCast = 5,
    }

    /// <summary>
    /// Trigger type for input-to-order mapping.
    /// </summary>
    public enum InputTriggerType
    {
        /// <summary>
        /// Trigger when the action is pressed this frame.
        /// </summary>
        PressedThisFrame = 0,

        /// <summary>
        /// Trigger when the action is released this frame.
        /// </summary>
        ReleasedThisFrame = 1,

        /// <summary>
        /// Trigger while the action is held down.
        /// </summary>
        Held = 2,

        /// <summary>
        /// Trigger when the action is pressed twice within the configured window.
        /// </summary>
        DoubleTap = 3
    }
    
    /// <summary>
    /// Target data required for the order.
    /// </summary>
    public enum OrderTargetType
    {
        /// <summary>
        /// No target data required.
        /// </summary>
        None = 0,
        
        /// <summary>
        /// World position required (e.g. ground click for move/skillshot).
        /// </summary>
        Position = 1,
        
        /// <summary>
        /// Single entity required.
        /// </summary>
        Entity = 2,
        
        /// <summary>
        /// Multiple entities required.
        /// </summary>
        Entities = 3,
        
        /// <summary>
        /// 2D direction vector required (e.g. cone/line skill direction).
        /// Stored as a normalized direction in OrderSpatial.
        /// </summary>
        Direction = 4,
        
        /// <summary>
        /// Two-point vector input (start + end) for vector-targeted skills
        /// (e.g. Rumble R, Viktor E). Press records start, drag/release records end.
        /// Both points are stored in OrderSpatial.
        /// </summary>
        Vector = 5,

        /// <summary>
        /// Use hovered command target entity when present; otherwise use the resolved ground position.
        /// </summary>
        HoveredEntityOrPosition = 6,
        
        /// <summary>
        /// Obsolete alias for Position. Use Position instead.
        /// </summary>
        [Obsolete("Use Position instead.")]
        Ground = Position
    }
    
    /// <summary>
    /// Template for order arguments.
    /// Nullable fields are not applied to the order.
    /// </summary>
    public class OrderArgsTemplate
    {
        public int? I0 { get; set; }
        public int? I1 { get; set; }
        public int? I2 { get; set; }
        public int? I3 { get; set; }
        
        public float? F0 { get; set; }
        public float? F1 { get; set; }
        public float? F2 { get; set; }
        public float? F3 { get; set; }

        public OrderArgsTemplate Clone()
        {
            return new OrderArgsTemplate
            {
                I0 = I0,
                I1 = I1,
                I2 = I2,
                I3 = I3,
                F0 = F0,
                F1 = F1,
                F2 = F2,
                F3 = F3
            };
        }
    }
    
    /// <summary>
    /// Policy for how Held trigger type generates orders.
    /// </summary>
    public enum HeldPolicy
    {
        /// <summary>
        /// Fire an order every frame while held (default).
        /// </summary>
        EveryFrame = 0,
        
        /// <summary>
        /// Emit a Start order on press and an End order on release.
        /// The OrderTypeKey is suffixed with ".Start" and ".End" respectively.
        /// No orders are emitted between press and release.
        /// </summary>
        StartEnd = 1
    }
    
    /// <summary>
    /// Policy for explicit automatic target acquisition.
    /// </summary>
    public enum AutoTargetPolicy
    {
        /// <summary>
        /// No automatic target resolution. Entity targets must come from hover or explicit target input.
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Automatically select the nearest valid entity within cast range.
        /// Uses ISpatialQueryService to find the closest target.
        /// </summary>
        NearestInRange = 1,
        
        /// <summary>
        /// Automatically select the nearest enemy entity within cast range.
        /// Filters by Team component (different team from caster).
        /// </summary>
        NearestEnemyInRange = 2,
    }

    /// <summary>
    /// Submit mode behavior when modifier key is pressed.
    /// </summary>
    public enum ModifierSubmitBehavior
    {
        /// <summary>
        /// Ignore modifier key - always use configured default.
        /// </summary>
        IgnoreModifier = 0,
        
        /// <summary>
        /// Use Queued mode when queue modifier is held, Immediate otherwise.
        /// PC: Shift+click, Console: L1+click, etc.
        /// </summary>
        QueueOnModifier = 1,
        
        /// <summary>
        /// Always use Immediate mode regardless of modifiers.
        /// </summary>
        AlwaysImmediate = 2,
        
        /// <summary>
        /// Always use Queued mode regardless of modifiers.
        /// </summary>
        AlwaysQueued = 3,

        /// <summary>
        /// Use PersistentQueued mode when the queue modifier is held, Immediate otherwise.
        /// Intended for explicit player-authored command queues that must outlive input buffering windows.
        /// </summary>
        PersistentQueueOnModifier = 4
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GroupMoveTargetLayoutMode
    {
        None = 0,
        Grid = 1
    }

    public sealed class GroupMoveTargetLayoutSettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GroupMoveTargetLayoutMode Mode { get; set; } = GroupMoveTargetLayoutMode.None;

        public int SpacingCm { get; set; } = 120;

        /// <summary>
        /// Order type keys eligible for a grid target layout when mode is Grid.
        /// Required when mode is Grid.
        /// </summary>
        public List<string> OrderTypeKeys { get; set; } = new();
    }

    public sealed class ActorOrderRoutingMatch
    {
        public List<string> RequiredAllTags { get; set; } = new();
        public List<string> BlockedAnyTags { get; set; } = new();
        public int? AbilitySlotIndex { get; set; }
        public string? AbilityIdKey { get; set; }
        public string? AbilityIdKeySuffix { get; set; }

        public ActorOrderRoutingMatch Clone()
        {
            return new ActorOrderRoutingMatch
            {
                RequiredAllTags = new List<string>(RequiredAllTags),
                BlockedAnyTags = new List<string>(BlockedAnyTags),
                AbilitySlotIndex = AbilitySlotIndex,
                AbilityIdKey = AbilityIdKey,
                AbilityIdKeySuffix = AbilityIdKeySuffix
            };
        }
    }

    public sealed class ActorOrderRoutingCandidate
    {
        public string OrderTypeKey { get; set; } = string.Empty;
        public int Priority { get; set; }
        public ActorOrderRoutingMatch Match { get; set; } = new();

        /// <summary>
        /// Optional per-candidate target resolution. When null, inherits mapping.TargetType.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OrderTargetType? TargetType { get; set; }

        public ActorOrderRoutingCandidate Clone()
        {
            return new ActorOrderRoutingCandidate
            {
                OrderTypeKey = OrderTypeKey,
                Priority = Priority,
                Match = Match?.Clone() ?? new ActorOrderRoutingMatch(),
                TargetType = TargetType
            };
        }
    }

    public sealed class ActorOrderRoutingSettings
    {
        public List<ActorOrderRoutingCandidate> Candidates { get; set; } = new();

        public ActorOrderRoutingSettings Clone()
        {
            var candidates = new List<ActorOrderRoutingCandidate>(Candidates.Count);
            for (int i = 0; i < Candidates.Count; i++)
            {
                candidates.Add(Candidates[i].Clone());
            }

            return new ActorOrderRoutingSettings { Candidates = candidates };
        }
    }
    
    /// <summary>
    /// A single input-to-order mapping.
    /// </summary>
    public class InputOrderMapping
    {
        /// <summary>
        /// The InputAction ID to listen for.
        /// </summary>
        public string ActionId { get; set; } = string.Empty;
        
        /// <summary>
        /// The trigger condition.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public InputTriggerType Trigger { get; set; } = InputTriggerType.PressedThisFrame;

        /// <summary>
        /// Double-tap time window in seconds.
        /// Only meaningful when <see cref="Trigger"/> is <see cref="InputTriggerType.DoubleTap"/>.
        /// </summary>
        public float DoubleTapWindowSeconds { get; set; } = 0.30f;
        
        /// <summary>
        /// The order type key (must match a key in OrderTypeRegistry).
        /// Required when <see cref="ActorOrderRouting"/> is null.
        /// </summary>
        public string OrderTypeKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Per-actor order type routing for shared input actions such as Command.
        /// </summary>
        public ActorOrderRoutingSettings? ActorOrderRouting { get; set; }

        /// <summary>
        /// Template for order arguments.
        /// </summary>
        public OrderArgsTemplate ArgsTemplate { get; set; } = new();
        
        /// <summary>
        /// Whether target data is required.
        /// </summary>
        public bool RequireTarget { get; set; } = false;

        /// <summary>
        /// Optional named entity collection supplying actors for this mapping.
        /// Leave empty when the caller supplies a single actor explicitly.
        /// </summary>
        public string ActorCollectionKey { get; set; } = string.Empty;

        /// <summary>
        /// Optional named entity collection supplying entity targets for this mapping.
        /// Required when targetType is Entity or Entities and the mapping relies on collection target data.
        /// </summary>
        public string TargetCollectionKey { get; set; } = string.Empty;
        
        /// <summary>
        /// The type of target required.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OrderTargetType TargetType { get; set; } = OrderTargetType.None;
        
        /// <summary>
        /// How modifier keys affect the order submit mode.
        /// Default is QueueOnModifier (modifier+action queues the order).
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ModifierSubmitBehavior ModifierBehavior { get; set; } = ModifierSubmitBehavior.QueueOnModifier;

        /// <summary>
        /// Whether this mapping is a "skill-type" mapping that is affected by InteractionMode.
        /// When true, the global InteractionMode (TargetFirst/SmartCast/AimCast) controls
        /// whether the action triggers immediately or enters an aiming phase.
        /// When false (e.g. moveTo, stop), the action always triggers immediately.
        /// </summary>
        public bool IsSkillMapping { get; set; } = false;
        
        /// <summary>
        /// Policy for Held trigger: EveryFrame (fire every frame) or
        /// StartEnd (emit a ".Start" order on press and a ".End" order on release).
        /// Only meaningful when Trigger == Held.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HeldPolicy HeldPolicy { get; set; } = HeldPolicy.EveryFrame;
        
        /// <summary>
        /// Per-ability cast mode override. When set to a value other than <c>null</c>,
        /// overrides the global InteractionMode for this specific mapping.
        /// For example, set to AimCast for a global skillshot while the player uses SmartCast.
        /// Only meaningful when <see cref="IsSkillMapping"/> is true.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public InteractionModeType? CastModeOverride { get; set; }
        
        /// <summary>
        /// Automatic target acquisition policy for SmartCast.
        /// When set, the system uses actor-centered spatial query as the explicit entity target source.
        /// Only meaningful for <see cref="OrderTargetType.Entity"/> and <see cref="OrderTargetType.Position"/> targets.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AutoTargetPolicy AutoTargetPolicy { get; set; } = AutoTargetPolicy.None;
        
        /// <summary>
        /// Range (in world cm) for auto-target spatial query.
        /// Only meaningful when <see cref="AutoTargetPolicy"/> is not None.
        /// </summary>
        public int AutoTargetRangeCm { get; set; } = 0;

        /// <summary>
        /// Cursor-centric entity resolution policy for position / direction casts.
        /// The spatial query is centered on the resolved cursor ground point, not on the actor.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AutoTargetPolicy CursorTargetPolicy { get; set; } = AutoTargetPolicy.None;

        /// <summary>
        /// Range (in world cm) for cursor-centric spatial target resolution.
        /// Only meaningful when <see cref="CursorTargetPolicy"/> is not None.
        /// </summary>
        public int CursorTargetRangeCm { get; set; } = 0;

        public InputOrderMapping Clone()
        {
            return new InputOrderMapping
            {
                ActionId = ActionId,
                Trigger = Trigger,
                DoubleTapWindowSeconds = DoubleTapWindowSeconds,
                OrderTypeKey = OrderTypeKey,
                ActorOrderRouting = ActorOrderRouting?.Clone(),
                ArgsTemplate = ArgsTemplate?.Clone() ?? new OrderArgsTemplate(),
                RequireTarget = RequireTarget,
                ActorCollectionKey = ActorCollectionKey,
                TargetCollectionKey = TargetCollectionKey,
                TargetType = TargetType,
                ModifierBehavior = ModifierBehavior,
                IsSkillMapping = IsSkillMapping,
                HeldPolicy = HeldPolicy,
                CastModeOverride = CastModeOverride,
                AutoTargetPolicy = AutoTargetPolicy,
                AutoTargetRangeCm = AutoTargetRangeCm,
                CursorTargetPolicy = CursorTargetPolicy,
                CursorTargetRangeCm = CursorTargetRangeCm
            };
        }
    }
    
    /// <summary>
    /// User override settings for input-order mappings.
    /// </summary>
    public class UserOverrideSettings
    {
        /// <summary>
        /// Whether user overrides are enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Path to persist user preferences.
        /// </summary>
        public string PersistPath { get; set; } = "user://input_preferences.json";
    }
    
    /// <summary>
    /// Root configuration for input-order mappings.
    /// </summary>
    public class InputOrderMappingConfig
    {
        /// <summary>
        /// Global interaction mode for this configuration.
        /// Determines how skill-type InputActions transition to Orders:
        ///   TargetFirst = instant submit using configured target data
        ///   SmartCast   = instant submit using the mapping's explicit target source
        ///   AimCast     = enter aiming phase, submit on confirm click
        ///
        /// Non-skill mappings (e.g. moveTo, stop) are unaffected by this setting.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public InteractionModeType InteractionMode { get; set; } = InteractionModeType.TargetFirst;

        /// <summary>
        /// List of mappings.
        /// </summary>
        public List<InputOrderMapping> Mappings { get; set; } = new();

        /// <summary>
        /// Global target-layout behavior for multi-actor position commands.
        /// </summary>
        public GroupMoveTargetLayoutSettings GroupMoveTargetLayout { get; set; } = new();
        
        /// <summary>
        /// User override settings.
        /// </summary>
        public UserOverrideSettings UserOverrides { get; set; } = new();
    }
}
