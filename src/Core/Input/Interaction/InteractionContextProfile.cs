using System.Collections.Generic;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Reserved interaction context profile ids owned by the engine.</summary>
    public static class InteractionContextIds
    {
        /// <summary>
        /// Data-declared steady-state profile: never mounted (absence of
        /// <see cref="ActiveInteractionContext"/> is the steady state), but its collection key
        /// and filter profile anchor steady-state cast commits and command routing.
        /// </summary>
        public const string Default = "interaction.context.default";
    }

    /// <summary>Merged root of <c>Input/interaction_context_profiles.json</c>.</summary>
    public sealed class InteractionContextProfilesConfig
    {
        public List<InteractionContextProfileDefinition> Profiles { get; set; }
    }

    /// <summary>
    /// One interaction context profile (RFC-0065 §5.3). Abilities reference profiles via
    /// <c>abilities.json interactionContextProfile</c>; a mounted context copies these fields
    /// into an <see cref="ActiveInteractionContext"/>. Strings live only in JSON — Core never
    /// interprets ids beyond registry resolution.
    /// </summary>
    public sealed class InteractionContextProfileDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Collection key context-bound cast commits write while the context is active.</summary>
        public string ActiveCollectionKey { get; set; } = string.Empty;

        /// <summary>Entity view key the context exposes to read surfaces.</summary>
        public string ActiveEntityViewKey { get; set; } = string.Empty;

        /// <summary>Optional filter profile applied to cast commits (empty = pass-through).</summary>
        public string FilterProfileId { get; set; }

        /// <summary>Optional IMC input context pushed alongside the context (DEC-7 wiring).</summary>
        public string InputContextId { get; set; }

        /// <summary>Optional pointer command intent profile active while the context is mounted (DEC-14).</summary>
        public string CommandIntentId { get; set; }
    }
}
