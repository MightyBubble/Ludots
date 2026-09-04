using Arch.Core;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Who mounted one entity-domain TriggerGraph mount. The TriggerManager map tables are
    /// the single mount ledger; the owner tag lets any feature query and remove exactly the
    /// mounts it created (by owner) instead of keeping a parallel shadow list. Subjects must
    /// stay entity-scoped: map/mod/event-bus triggers are unowned and never enter the index.
    /// </summary>
    public enum TriggerMountOwnerKind : byte
    {
        None = 0,

        /// <summary>Entity mounts built from the entity's template TriggerGraphs declaration.</summary>
        TemplateEntity = 1,

        /// <summary>Context mounts built from a profile's triggers[] on the context subject.</summary>
        InteractionContext = 2,
    }

    /// <summary>Identity of one mounting feature instance: kind + subject entity + kind-local id
    /// (InteractionContext: profile id; TemplateEntity: 0). Record equality keys the index.</summary>
    public readonly record struct TriggerMountOwner(TriggerMountOwnerKind Kind, Entity Subject, int OwnerId)
    {
        public static readonly TriggerMountOwner None = default;

        public bool IsOwned => Kind != TriggerMountOwnerKind.None;
    }
}
