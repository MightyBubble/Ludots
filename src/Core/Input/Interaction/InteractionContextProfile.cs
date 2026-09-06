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
        /// Foreground declaration (#1398 刀4): while this context is active, every active
        /// ancestor's interactive (input-action bound) trigger mounts are parked — removed
        /// from listening — while map/passive (event-bound) mounts stay. Scope and the
        /// parent-child coexistence (non-stack) are unaffected; parking is a mount-regime
        /// demotion, not a window close, so no lifecycle slot fires. Restored when the
        /// foreground context deactivates. Pure data switch: the mount gate owns the diff.
        /// </summary>
        public bool Foreground { get; set; }

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
        /// Graph bodies (plain bodies, no entries) run once when this context's
        /// <c>triggers[]</c> window opens — before the trigger mounts are registered on the
        /// subject (#1398 D15). Instant boundary hooks flanking the mounted window, never a
        /// per-tick clock (the retired <c>whileActive</c> was a period field; these are not).
        /// </summary>
        public List<string>? OnActivated { get; set; }

        /// <summary>
        /// Graph bodies run once when this context's <c>triggers[]</c> window closes — after
        /// the trigger mounts are removed (explicit deactivation or owner death; #1398 D15).
        /// Settlement/cleanup graphs (selection_commit, preview clears) live here and are
        /// shared verbatim across every gesture context, because they only read the
        /// handoff collections/blackboard the exit graph wrote, never the gesture shape.
        /// </summary>
        public List<string>? OnDeactivated { get; set; }
    }

    /// <summary>
    /// Lifecycle slot on a profile: graph bodies flank the context's trigger window.
    /// Activated runs as the window opens (before mount), Deactivated as it closes (after
    /// unmount, including the owner-death path). One profile id per slot is unambiguous —
    /// no owner matching is ever needed because the slot belongs to its own profile.
    /// </summary>
    public enum InteractionContextLifecycleSlot
    {
        Activated = 0,
        Deactivated = 1,
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
