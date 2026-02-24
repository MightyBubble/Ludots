namespace Ludots.Core.Presentation.Events
{
    public enum PresentationEventKind : byte
    {
        None = 0,
        GameplayEvent = 1,
        TagEffectiveChanged = 2,

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
