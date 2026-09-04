using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Reserved interaction context profile ids owned by the engine.</summary>
    public static class InteractionContextIds
    {
        /// <summary>
        /// Data-declared steady-state profile: never mounted (absence of
        /// <see cref="InteractionContextInstance"/> is the steady state), but its collection key
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
    /// into an <see cref="InteractionContextInstance"/>. Strings live only in JSON — Core never
    /// interprets ids beyond registry resolution.
    /// </summary>
    public sealed class InteractionContextProfileDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Collection key context-bound cast commits write while the context is active.</summary>
        public string ActiveCollectionKey { get; set; } = string.Empty;

        /// <summary>Optional filter profile applied to cast commits (empty = pass-through).</summary>
        public string FilterProfileId { get; set; }

        /// <summary>Optional IMC input context pushed alongside the context (DEC-7 wiring).</summary>
        public string InputContextId { get; set; }

        /// <summary>Optional pointer command intent profile active while the context is mounted (DEC-14).</summary>
        public string CommandIntentId { get; set; }

        /// <summary>
        /// Semantic action ids (input config action space) that hold while this context is
        /// active. Validated against the installed input action catalog at registry install —
        /// an unknown action id fails fast. Data-declared contract only in this slice: routing
        /// consumers land with the slot work (RFC #1398 S6).
        /// </summary>
        public List<string>? Bindings { get; set; }

        /// <summary>
        /// TriggerGraph mounts the context gates while it is active (#1398 S2b): each entry
        /// activates one graph's dispatch entries on the context subject while the context is
        /// mounted and deactivates them on unmount. Graph id and entry event name resolve at
        /// registry install (fail fast on unknown ids); mount-time event vocabulary checks ride
        /// the existing TriggerGraph mount chain.
        /// </summary>
        public List<InteractionContextTriggerMount>? Triggers { get; set; }

        /// <summary>
        /// Case E §05: graph id to run every tick while this context is active. The graph must
        /// WriteCollection to write its preview collection. Absent = no per-tick graph.
        /// </summary>
        public InteractionContextWhileActive? WhileActive { get; set; }
    }

    /// <summary>
    /// While-active graph mount on an <see cref="InteractionContextProfileDefinition"/>:
    /// <c>Graph</c> names the hit function run each tick (e.g. drag-time box hits).
    /// </summary>
    public sealed class InteractionContextWhileActive
    {
        public string Graph { get; set; } = string.Empty;
    }

    /// <summary>
    /// One context-gated TriggerGraph reference on an
    /// <see cref="InteractionContextProfileDefinition"/>. <c>Trigger</c> names a registered
    /// TriggerGraph; <c>Event</c> optionally narrows the mount to the graph's entries listening
    /// on that event name (all dispatch entries mount when empty); <c>Filters</c> optionally
    /// replaces the selected entries' authored filters for this mount — a reference-time
    /// override, not a merge.
    /// </summary>
    public sealed class InteractionContextTriggerMount
    {
        public string Trigger { get; set; } = string.Empty;

        public string? Event { get; set; }

        public TriggerGraphEntryFiltersConfig? Filters { get; set; }
    }
}
