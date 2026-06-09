using System;
using Arch.Core;

namespace Ludots.Core.Presentation.Events
{
    /// <summary>
    /// Per-tick projected owner fact change consumed by performer owner bindings.
    /// The record is an index entry only: gameplay values remain owned by their gameplay components.
    /// </summary>
    public readonly struct PresentationOwnerChange
    {
        public readonly Entity Owner;
        public readonly PresentationOwnerChangeKind Kind;
        public readonly int KeyId;
        public readonly byte StateValue;

        public bool TagActive => StateValue != 0;

        public PresentationOwnerChange(Entity owner, PresentationOwnerChangeKind kind, int keyId)
            : this(owner, kind, keyId, stateValue: 0)
        {
        }

        public PresentationOwnerChange(Entity owner, PresentationOwnerChangeKind kind, int keyId, byte stateValue)
        {
            Owner = owner;
            Kind = kind;
            KeyId = keyId;
            StateValue = stateValue;
        }
    }

    public enum PresentationOwnerChangeKind : byte
    {
        Attribute = 1,
        Tag = 2,
    }

    /// <summary>
    /// Bounded per-tick projection index for owner tag/attribute changes.
    /// Producers must project from gameplay SSOT components, consumers must resolve current values
    /// from those SSOT components instead of treating this buffer as cached gameplay state.
    /// </summary>
    public sealed class PresentationOwnerChangeBuffer
    {
        private readonly PresentationOwnerChange[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PresentationOwnerChangeBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PresentationOwnerChange[capacity];
        }

        public bool TryAdd(in PresentationOwnerChange change)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = change;
            return true;
        }

        public ReadOnlySpan<PresentationOwnerChange> GetSpan() => new(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
