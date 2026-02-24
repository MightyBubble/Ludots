using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Interaction mode determines HOW InputActions become Orders.
    /// This is a game-level / player-preference setting, NOT per-ability.
    ///
    /// TargetFirst (WoW): player selects target first, then presses ability key → order submitted immediately.
    /// SmartCast (LoL): player presses ability key → order submitted immediately at cursor/hovered entity.
    /// AimCast (DotA/WC3): player presses ability key → enters aiming phase → click to confirm, right-click/ESC to cancel.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InteractionModeType
    {
        /// <summary>WoW style: select target, press key → instant cast.</summary>
        TargetFirst = 0,

        /// <summary>LoL style: press key → cast at cursor / hovered entity.</summary>
        SmartCast = 1,

        /// <summary>DotA/WC3 style: press key → aiming → click confirm.</summary>
        AimCast = 2,

        /// <summary>
        /// LoL "Quick Cast with Indicator" style: hold key → show indicator,
        /// release key → cast at cursor position. Right-click/ESC cancels.
        /// </summary>
        SmartCastWithIndicator = 3,
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
        /// Double-tap trigger. This does not belong in InputTriggerType — double-click-select-same-type
        /// is a selection system concern (see advanced selection system design).
        /// Retained for enum stability; will be removed when selection system is implemented.
        /// </summary>
        [Obsolete("Double-tap selection belongs to the selection system, not InputTriggerType. Will be removed.")]
        DoubleTap = 3
    }
    
    /// <summary>
    /// Selection type required for the order.
    /// </summary>
    public enum OrderSelectionType
    {
        /// <summary>
        /// No selection required.
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
    }
    
    /// <summary>
    /// Policy for how Held trigger type generates orders.
    /// </summary>
    public enum HeldPolicy
    {
        /// <summary>
        /// Fire an order every frame while held (default, legacy behavior).
        /// </summary>
        EveryFrame = 0,
        
        /// <summary>
        /// Emit a Start order on press and an End order on release.
        /// The OrderTagKey is suffixed with ".Start" and ".End" respectively.
        /// No orders are emitted between press and release.
        /// </summary>
        StartEnd = 1
    }
    
    /// <summary>
    /// Policy for automatic target acquisition when SmartCast has no hovered entity.
    /// </summary>
    public enum AutoTargetPolicy
    {
        /// <summary>
        /// No auto-targeting. If no hovered entity, fall back to selected entity or fail.
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
        AlwaysQueued = 3
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
        /// The order type key (must match a key in OrderTypeRegistry).
        /// </summary>
        public string OrderTagKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Template for order arguments.
        /// </summary>
        public OrderArgsTemplate ArgsTemplate { get; set; } = new();
        
        /// <summary>
        /// Whether selection data is required.
        /// </summary>
        public bool RequireSelection { get; set; } = false;
        
        /// <summary>
        /// The type of selection required.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OrderSelectionType SelectionType { get; set; } = OrderSelectionType.None;
        
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
        /// Automatic target acquisition policy for SmartCast when no entity is hovered.
        /// When set, the system uses a spatial query to find a fallback target.
        /// Only meaningful for <see cref="OrderSelectionType.Entity"/> selections.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AutoTargetPolicy AutoTargetPolicy { get; set; } = AutoTargetPolicy.None;
        
        /// <summary>
        /// Range (in world cm) for auto-target spatial query.
        /// Only meaningful when <see cref="AutoTargetPolicy"/> is not None.
        /// </summary>
        public int AutoTargetRangeCm { get; set; } = 0;
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
        ///   TargetFirst = instant submit using selected entity
        ///   SmartCast   = instant submit using cursor/hovered entity
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
        /// User override settings.
        /// </summary>
        public UserOverrideSettings UserOverrides { get; set; } = new();
    }
}
