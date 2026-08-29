using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Entity-mounted active interaction context state: present on the interaction subject (the
    /// control-domain representative) only while an interaction context frame is active in that
    /// entity's control domain, absent otherwise. Absence is the steady state — the entity-side
    /// anchor of the reserved default frame, where the player's <see cref="CommandPref"/> default
    /// applies and command sources resolve to the subject itself. Sparse like
    /// <see cref="InteractionMode"/> and <see cref="CommandPref"/>: the vast majority of entities
    /// never carry it, and holders are discoverable by archetype query.
    /// <para>
    /// Written by the frame-state reconciliation in
    /// <see cref="AbilityExecInteractionContextSystem"/>: per control domain, the topmost frame
    /// whose carrier resolves to the domain rep wins (LIFO, matching the stack's top-frame
    /// arbitration); a frame whose carrier is gone from the stack releases the component, while a
    /// frame whose carrier is merely dead or domain-less freezes the mounted state for the
    /// one-tick reclaim window so readers fail closed exactly like the retired stack read.
    /// </para>
    /// </summary>

    public struct ActiveInteractionContext
    {
        /// <summary>
        /// Carrier entity of the active context frame (e.g. the ability exec instance entity).
        /// Command source owner resolution and showcase frame identity read this; the reclaim
        /// window keeps a dead carrier mounted so the owner resolves to nothing rather than
        /// silently falling back to the subject.
        /// </summary>
        public Entity ContextEntity;

        /// <summary>
        /// Declared pointer-command intent profile id in the
        /// <see cref="InteractionContextStack.CommandIntentProfileIdRegistry"/> id space.
        /// Positive wins over the player default (DEC-14); zero means the active context
        /// declares no intent and pointer commands do not route — never bubble (no fallback).
        /// </summary>
        public int CommandIntentProfileId;
    }
}
