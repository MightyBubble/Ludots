using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.Relationships
{
    public readonly struct RelationshipChangeRecord
    {
        public RelationshipChangeRecord(
            Entity source,
            Entity target,
            int metricId,
            int reasonId,
            short oldValue,
            short newValue,
            uint oldFlags,
            uint newFlags)
            : this(source, target, typeId: 0, metricId, reasonId, oldValue, newValue, oldFlags, newFlags)
        {
        }

        public RelationshipChangeRecord(
            Entity source,
            Entity target,
            int typeId,
            int metricId,
            int reasonId,
            short oldValue,
            short newValue,
            uint oldFlags,
            uint newFlags)
        {
            Source = source;
            Target = target;
            TypeId = typeId;
            MetricId = metricId;
            ReasonId = reasonId;
            OldValue = oldValue;
            NewValue = newValue;
            OldFlags = oldFlags;
            NewFlags = newFlags;
        }

        public Entity Source { get; }
        public Entity Target { get; }
        public int TypeId { get; }
        public int MetricId { get; }
        public int ReasonId { get; }
        public short OldValue { get; }
        public short NewValue { get; }
        public uint OldFlags { get; }
        public uint NewFlags { get; }
    }

    public sealed class RelationshipChangeBuffer
    {
        private RelationshipChangeRecord[] _buffer;
        private int _count;

        public RelationshipChangeBuffer(int capacity = 2048)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new RelationshipChangeRecord[capacity];
        }

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int ResizeCount { get; private set; }

        public bool TryAdd(in RelationshipChangeRecord record)
        {
            EnsureCapacity(_count + 1);
            _buffer[_count++] = record;
            return true;
        }

        public ReadOnlySpan<RelationshipChangeRecord> GetSpan() => new(_buffer, 0, _count);

        public void Clear()
        {
            _count = 0;
        }

        private void EnsureCapacity(int requiredCount)
        {
            if (requiredCount <= _buffer.Length)
            {
                return;
            }

            int newCapacity = Math.Max(_buffer.Length * 2, requiredCount);
            Array.Resize(ref _buffer, newCapacity);
            ResizeCount++;
        }
    }
}
