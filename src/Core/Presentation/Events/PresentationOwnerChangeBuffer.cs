using System;
using Arch.Core;

namespace Ludots.Core.Presentation.Events
{
    public readonly struct PresentationOwnerChange
    {
        public readonly Entity Owner;
        public readonly PresentationOwnerChangeKind Kind;
        public readonly int KeyId;

        public PresentationOwnerChange(Entity owner, PresentationOwnerChangeKind kind, int keyId)
        {
            Owner = owner;
            Kind = kind;
            KeyId = keyId;
        }
    }

    public enum PresentationOwnerChangeKind : byte
    {
        Attribute = 1,
        Tag = 2,
    }

    public sealed class PresentationOwnerChangeBuffer
    {
        private readonly PresentationOwnerChange[] _buffer;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PresentationOwnerChangeBuffer(int capacity = 8192)
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
