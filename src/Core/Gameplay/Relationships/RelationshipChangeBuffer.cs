using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.Relationships
{
    /// <summary>关系变更种类：决定缓冲消费侧（回调规则 / trigger 事件键）如何路由一条记录。</summary>
    public enum RelationshipChangeKind : byte
    {
        LinkAdded = 0,
        LinkRemoved = 1,
        MetricChanged = 2,
        FlagChanged = 3,
    }

    public readonly struct RelationshipChangeRecord
    {
        public RelationshipChangeRecord(
            Entity source,
            Entity target,
            int metricId,
            short oldValue,
            short newValue,
            uint oldFlags,
            uint newFlags)
            : this(source, target, typeId: 0, RelationshipChangeKind.MetricChanged, metricId, oldValue, newValue, oldFlags, newFlags)
        {
        }

        public RelationshipChangeRecord(
            Entity source,
            Entity target,
            int typeId,
            int metricId,
            short oldValue,
            short newValue,
            uint oldFlags,
            uint newFlags)
            : this(source, target, typeId, RelationshipChangeKind.MetricChanged, metricId, oldValue, newValue, oldFlags, newFlags)
        {
        }

        public RelationshipChangeRecord(
            Entity source,
            Entity target,
            int typeId,
            RelationshipChangeKind kind,
            int metricId,
            short oldValue,
            short newValue,
            uint oldFlags,
            uint newFlags)
        {
            Source = source;
            Target = target;
            TypeId = typeId;
            Kind = kind;
            MetricId = metricId;
            OldValue = oldValue;
            NewValue = newValue;
            OldFlags = oldFlags;
            NewFlags = newFlags;
        }

        public Entity Source { get; }
        public Entity Target { get; }
        public int TypeId { get; }
        public RelationshipChangeKind Kind { get; }
        public int MetricId { get; }
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

        public void Truncate(int count)
        {
            if ((uint)count > (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Relationship change truncate count is past the buffered records.");
            }

            _count = count;
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
