using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Presentation
{
    /// <summary>
    /// Typed event kinds emitted by the GAS logic layer for the Presentation layer to consume.
    /// </summary>
    public enum GasPresentationEventKind : byte
    {
        // ── Ability lifecycle ──
        CastStarted     = 1,
        CastFailed      = 2,
        CastCommitted   = 3,
        CastFinished    = 4,
        CastInterrupted = 5,

        // ── Effect lifecycle ──
        EffectApplied   = 10,
        EffectActivated = 11,
        EffectExpired   = 12,
        EffectCancelled = 13,
    }

    /// <summary>
    /// Why an ability cast was rejected.
    /// </summary>
    public enum AbilityCastFailReason : byte
    {
        None        = 0,
        OnCooldown  = 1,
        BlockedByTag= 2,
        NoTarget    = 3,
        InvalidSlot = 4,
        NotAlive    = 5,
    }

    /// <summary>
    /// A single presentation event emitted by the GAS pipeline.
    /// Written during fixed-tick; read by presentation systems each render frame.
    /// </summary>
    public struct GasPresentationEvent
    {
        public GasPresentationEventKind Kind;
        public Entity Actor;
        public Entity Target;
        public int    AbilitySlot;
        public int    AbilityId;
        public int    EffectTemplateId;
        public int    AttributeId;
        public float  Delta;
        public AbilityCastFailReason FailReason;
    }

    /// <summary>
    /// Fixed-capacity ring buffer for GAS → Presentation events.
    /// Written by GAS systems during logic tick, consumed by presentation systems each render frame.
    /// Zero-allocation after construction.
    /// </summary>
    public sealed class GasPresentationEventBuffer
    {
        private readonly GasPresentationEvent[] _events;
        private int _count;

        public int Count => _count;
        public int Capacity => _events.Length;
        public int DroppedSinceClear { get; private set; }

        public GasPresentationEventBuffer(int capacity = 1024)
        {
            if (capacity <= 0) capacity = 1024;
            _events = new GasPresentationEvent[capacity];
        }

        public bool Publish(in GasPresentationEvent evt)
        {
            if (_count >= _events.Length)
            {
                DroppedSinceClear++;
                return false;
            }
            _events[_count++] = evt;
            return true;
        }

        public ReadOnlySpan<GasPresentationEvent> Events => new(_events, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
