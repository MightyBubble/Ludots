using System.Collections.Generic;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Merged root of <c>Input/interaction_context_profiles.json</c>.</summary>
    public sealed class InteractionContextProfilesConfig
    {
        public List<InteractionContextProfileDefinition> Profiles { get; set; }
    }

    /// <summary>
    /// One interaction context profile (RFC-0065 §5.3). Abilities reference profiles via
    /// <c>abilities.json interactionContextProfile</c>; a pushed frame copies these fields into an
    /// <see cref="InteractionContextFrameDescriptor"/>. Strings live only in JSON — Core never
    /// interprets ids beyond registry resolution.
    /// </summary>
    public sealed class InteractionContextProfileDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Collection key context-bound cast commits write while the frame is active.</summary>
        public string ActiveCollectionKey { get; set; } = string.Empty;

        /// <summary>Entity view key the frame exposes to read surfaces.</summary>
        public string ActiveEntityViewKey { get; set; } = string.Empty;

        /// <summary>Optional filter profile applied to cast commits (empty = pass-through).</summary>
        public string FilterProfileId { get; set; }

        /// <summary>Optional IMC input context pushed alongside the frame (DEC-7 wiring).</summary>
        public string InputContextId { get; set; }

        /// <summary>Optional pointer command intent profile active while the frame is on top (DEC-14).</summary>
        public string CommandIntentId { get; set; }
    }
}
