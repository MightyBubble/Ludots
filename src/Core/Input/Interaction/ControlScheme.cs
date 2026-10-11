using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Reference frame the declared Axis2D action maps into. <c>None</c> is the unset state and
    /// fails fast at load and install — a movement declaration without a stated frame is authoring
    /// debt, not a default.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ControlSchemeAxisMoveDirectionMode
    {
        None = 0,

        /// <summary>Axis X/Y map directly onto the ground-plane world axes.</summary>
        WorldAbsolute = 1,

        /// <summary>
        /// Axis rotates by the possessed rep's logic-view camera yaw at order time
        /// (<see cref="OrbitCameraDirectionUtil.MoveInputToDirection"/>): W is always screen-up.
        /// </summary>
        CameraRelative = 2,
    }

    /// <summary>Merged root of <c>Input/control_schemes.json</c> (RFC-0065 INT-5, Section 5.11, DEC-15).</summary>
    public sealed class ControlSchemesConfig
    {
        public List<ControlSchemeDefinition> Schemes { get; set; }

        /// <summary>
        /// Mod-declared switchable scheme set. Empty = every declared scheme is allowed; non-empty
        /// entries must reference declared schemes and <see cref="ControlSchemeRuntime.TrySwitch"/>
        /// refuses everything outside the set.
        /// </summary>
        public List<string> AllowedSchemes { get; set; }
    }

    /// <summary>
    /// One control scheme (DEC-15): a named combination of IMC input contexts — a pure device
    /// binding profile. Order routing preferences are player data on the representative
    /// (<see cref="InteractionPref"/>), never scheme data. Scheme ids like <c>scheme.sc2_classic</c>
    /// are mod data, never Core concepts.
    /// </summary>
    public sealed class ControlSchemeDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>IMC context ids pushed onto the <c>PlayerInputHandler</c> while the scheme is active.</summary>
        public List<string> InputContexts { get; set; }

        /// <summary>
        /// Optional WASD-style axis move declaration (RFC-0065 INT-6, DEC-15). Null means the scheme
        /// has no axis movement: a topology fact, not a fallback. When declared, all fields are
        /// mandatory and validated fail-fast.
        /// </summary>
        public ControlSchemeAxisMove AxisMove { get; set; }
    }

    /// <summary>
    /// Per-scheme axis move declaration consumed by <c>AxisMoveOrderSystem</c>: the Axis2D action
    /// sampled from the authoritative input snapshot and the throttled move-order parameters.
    /// <c>orderTypeKey</c> resolves against <c>OrderTypeRegistry</c> at
    /// <see cref="ControlSchemeRuntime.Install"/> (fail fast on unknown keys).
    /// </summary>
    public sealed class ControlSchemeAxisMove
    {
        public string ActionId { get; set; } = string.Empty;

        public string OrderTypeKey { get; set; } = string.Empty;

        /// <summary>Simulation ticks between two submitted orders while the axis is held.</summary>
        public int ThrottleTicks { get; set; }

        /// <summary>Distance in world centimeters from the actor's position to the order target.</summary>
        public int StepDistanceCm { get; set; }

        /// <summary>Whether the axis maps onto world axes directly or rotates with the seat's camera.</summary>
        public ControlSchemeAxisMoveDirectionMode DirectionMode { get; set; } = ControlSchemeAxisMoveDirectionMode.None;
    }
}
