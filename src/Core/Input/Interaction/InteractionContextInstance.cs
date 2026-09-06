using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Which lifecycle wrote a mounted <see cref="InteractionContextInstance"/>: the ability exec
    /// reconciliation, a cast commit <c>pushFrame</c> op, the entity's spawn template, or a
    /// context-instance graph op (whose instances live on
    /// <see cref="InteractionContextInstances"/>). Each writer manages only its own
    /// mounts — the exec reconciliation never reclaims an op-pushed or spawn-mounted context,
    /// and <c>popFrame</c> never removes an exec-carried one.
    /// </summary>
    public enum InteractionContextInstanceSource : byte
    {
        /// <summary>Mounted by <see cref="AbilityExecInteractionContextSystem"/> for a running exec carrier.</summary>
        ExecLifecycle = 0,

        /// <summary>Mounted by a cast commit profile <c>pushFrame</c> op; released by <c>popFrame</c>.</summary>
        CastCommitOp = 1,

        /// <summary>
        /// Mounted at entity spawn by the template's <c>initialInteractionContext</c> field
        /// (#1398 S2b); lives until the entity dies — no foreign lifecycle reclaims it.
        /// </summary>
        TemplateSpawn = 2,

        /// <summary>
        /// Mounted by the <c>ActivateContext</c> graph op onto the instance set (see
        /// <see cref="InteractionContextInstances"/>); released by
        /// <c>DeactivateContext</c> or parent deactivation.
        /// </summary>
        ContextInstanceOp = 3,
    }

    /// <summary>
    /// One runtime interaction context instance (constitution §3.3.1): the entity-mounted
    /// record of "which context this subject is in right now", present on the interaction
    /// subject (the control-domain representative) only while an interaction context is
    /// active in that entity's control domain, absent otherwise. Absence is the steady
    /// state — the entity-side anchor of the retired reserved default frame, where the
    /// player's
    /// <see cref="InteractionPref"/> default applies, command sources resolve to the subject itself,
    /// and cast commits route through the data-declared default profile's collection key.
    /// Sparse like <see cref="InteractionMode"/> and <see cref="InteractionPref"/>: the vast
    /// majority of entities never carry it, and holders are discoverable by archetype query.
    /// <para>
    /// As the single-slot component it is the base instance mounted by the exec / cast /
    /// spawn chains (ParentContextId stays 0). The <c>ActivateContext</c> graph
    /// op mounts the same shape onto the coexisting set
    /// <see cref="InteractionContextInstances"/> with ParentContextId filled —
    /// a set member with ParentContextId != 0 is a derived instance (a child of that parent
    /// context); members written by the mount chains are base instances. There is no third
    /// kind (constitution §8.2, #1398 S2b).
    /// </para>
    /// <para>
    /// All int fields are registry ids resolved once at
    /// <see cref="InteractionContextProfileRegistry"/> install time: context and input context
    /// ids in the profile registry's own spaces, collection keys in the
    /// <c>EntityCollectionStore</c> key space, filter and command intent ids in their kernel
    /// registries' spaces. Component equality across a save round trip therefore only requires
    /// the same install order.
    /// </para>
    /// </summary>
    public struct InteractionContextInstance
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

        /// <summary>
        /// Profile id of the parent context this instance derives from (instance-set members
        /// only): 0 on base mounts (no parent); non-zero marks this instance as a derived
        /// instance — a child of that parent context. Deactivating a parent removes its
        /// descendants transitively.
        /// </summary>
        public int ParentContextId;

        /// <summary>Lifecycle that mounted this context; see <see cref="InteractionContextInstanceSource"/>.</summary>
        public InteractionContextInstanceSource Source;
    }
}
