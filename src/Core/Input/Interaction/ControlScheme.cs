using System.Collections.Generic;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Merged root of <c>Input/control_schemes.json</c> (RFC-0065 INT-5, §5.11, DEC-15).</summary>
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
    /// One control scheme (DEC-15): a named combination of IMC input contexts plus the default
    /// command intent that pointer commands on the default frame route through. Scheme ids like
    /// <c>scheme.sc2_classic</c> are mod data, never Core concepts.
    /// </summary>
    public sealed class ControlSchemeDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>IMC context ids pushed onto the <c>PlayerInputHandler</c> while the scheme is active.</summary>
        public List<string> InputContexts { get; set; }

        public ControlSchemeDefaults Defaults { get; set; }
    }

    /// <summary>Scheme defaults consumed when the top interaction frame declares no explicit override.</summary>
    public sealed class ControlSchemeDefaults
    {
        /// <summary>Command intent profile the default frame routes pointer commands through (DEC-14).</summary>
        public string CommandIntentId { get; set; } = string.Empty;
    }
}
