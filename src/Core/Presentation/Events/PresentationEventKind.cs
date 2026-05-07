namespace Ludots.Core.Presentation.Events
{
    public enum PresentationEventKind : byte
    {
        None = 0,
        GameplayEvent = 1,
        TagEffectiveChanged = 2,

        /// <summary>An entity was created from a registered entity template. Source=created entity.</summary>
        EntitySpawned = 3,

        /// <summary>An entity created from a registered entity template was destroyed. Source=destroyed entity.</summary>
        EntityDestroyed = 4,

        /// <summary>A projectile/effect template was spawned. Source=projectile owner, Target=projectile entity.</summary>
        ProjectileSpawned = 5,

        // ── Performer domain events ──
        /// <summary>Emitted when a persistent performer instance is created.</summary>
        PerformerCreated = 10,
        /// <summary>Emitted when a persistent performer instance is destroyed.</summary>
        PerformerDestroyed = 11,

        // ── GAS presentation events (bridged from GasPresentationEventBuffer) ──
        /// <summary>An effect was applied (damage/heal). PayloadA=AttributeId, Magnitude=Delta.</summary>
        EffectApplied = 20,
        /// <summary>An ability cast was committed. PayloadA=AbilitySlot, PayloadB=AbilityId.</summary>
        CastCommitted = 21,
        /// <summary>An ability cast failed. PayloadA=AbilitySlot, PayloadB=(int)FailReason.</summary>
        CastFailed = 22,
    }
}
