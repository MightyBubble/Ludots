using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Which lifecycle wrote a mounted <see cref="ActiveInteractionContext"/>: the ability exec
    /// reconciliation or a cast commit <c>pushFrame</c> op. Each writer manages only its own
    /// mounts — the exec reconciliation never reclaims an op-pushed context, and <c>popFrame</c>
    /// never removes an exec-carried one.
    /// </summary>
    public enum ActiveInteractionContextSource : byte
    {
        /// <summary>Mounted by <see cref="AbilityExecInteractionContextSystem"/> for a running exec carrier.</summary>
        ExecLifecycle = 0,

        /// <summary>Mounted by a cast commit profile <c>pushFrame</c> op; released by <c>popFrame</c>.</summary>
        CastCommitOp = 1,
    }

    /// <summary>
    /// Entity-mounted active interaction context state: present on the interaction subject (the
    /// control-domain representative) only while an interaction context is active in that
    /// entity's control domain, absent otherwise. Absence is the steady state — the entity-side
    /// anchor of the retired reserved default frame, where the player's
    /// <see cref="CommandPref"/> default applies, command sources resolve to the subject itself,
    /// and cast commits route through the data-declared default profile's collection key.
    /// Sparse like <see cref="InteractionMode"/> and <see cref="CommandPref"/>: the vast
    /// majority of entities never carry it, and holders are discoverable by archetype query.
    /// <para>
    /// All int fields are registry ids resolved once at
    /// <see cref="InteractionContextProfileRegistry"/> install time: context and input context
    /// ids in the profile registry's own spaces, collection keys in the
    /// <c>EntityCollectionStore</c> key space, filter and command intent ids in their kernel
    /// registries' spaces. Component equality across a save round trip therefore only requires
    /// the same install order.
    /// </para>
    /// </summary>

    public struct ActiveInteractionContext
    {
        /// <summary>
        /// Installed interaction context profile id in the
        /// <see cref="InteractionContextProfileRegistry.ProfileIdRegistry"/> id space — the
        /// active context's identity, read by showcase frame-identity checks.
        /// </summary>
        public int ContextId;

        /// <summary>
        /// Carrier entity of the active context (e.g. the ability exec instance entity).
        /// Command source owner resolution reads this; the reclaim window keeps a dead carrier
        /// mounted so the owner resolves to nothing rather than silently falling back to the
        /// subject. Also the cast dispatch cycle group key source.
        /// </summary>
        public Entity ContextEntity;

        /// <summary>
        /// Declared pointer-command intent profile id in the
        /// <see cref="CommandIntentProfileRegistry.ProfileIdRegistry"/> id space. Positive wins
        /// over the player default (DEC-14); zero means the active context declares no intent
        /// and pointer commands do not route — never bubble (no fallback).
        /// </summary>
        public int CommandIntentProfileId;

        /// <summary>
        /// Collection key id in the <c>EntityCollectionStore</c> key space that context-bound
        /// cast commits write and command intent routing reads while this context is active.
        /// </summary>
        public int ActiveCollectionKeyId;

        /// <summary>
        /// Filter profile id in the <see cref="FilterProfileRegistry.ProfileIdRegistry"/> id
        /// space applied to cast commits; 0 = explicit pass-through.
        /// </summary>
        public int FilterProfileId;

        /// <summary>
        /// IMC input context id in the
        /// <see cref="InteractionContextProfileRegistry.InputContextIdRegistry"/> id space that
        /// <c>InputContextProjectionSystem</c> demands on the subject's seat; 0 = none.
        /// </summary>
        public int InputContextId;

        /// <summary>Lifecycle that mounted this context; see <see cref="ActiveInteractionContextSource"/>.</summary>
        public ActiveInteractionContextSource Source;
    }
}
