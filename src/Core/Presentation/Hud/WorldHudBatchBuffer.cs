using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class WorldHudBatchBuffer
    {
        private readonly WorldHudItem[] _buffer;
        private readonly WorldHudItem[] _dirtyContentBuffer;
        private readonly int[] _removedStableIds;
        private readonly Dictionary<int, int> _retainedIndexByStableId = new();
        private readonly Dictionary<WorldHudOwnerGroupKey, int> _ownerGroupIndexByKey = new();
        private WorldHudOwnerGroup[] _ownerGroups;
        private int[] _groupedItemIndices;
        private int[] _ownerGroupWriteOffsets;
        private int _count;
        private int _transientCount;
        private int _dirtyContentCount;
        private int _removedStableIdCount;
        private int _ownerGroupCount;
        private int _ownerGroupProjectionRevision = -1;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }
        public int ContentRevision { get; private set; }
        public int ProjectionRevision { get; private set; }
        public int ContentOnlyRevision { get; private set; }

        public WorldHudBatchBuffer(int capacity = 65536)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new WorldHudItem[capacity];
            _dirtyContentBuffer = new WorldHudItem[capacity];
            _removedStableIds = new int[capacity];
            _ownerGroups = new WorldHudOwnerGroup[Math.Min(capacity, 1024)];
            _groupedItemIndices = new int[capacity];
            _ownerGroupWriteOffsets = new int[Math.Min(capacity, 1024)];
        }

        public bool TryAdd(in WorldHudItem item)
        {
            if (item.StableId > 0 && _retainedIndexByStableId.TryGetValue(item.StableId, out int existingIndex))
            {
                if (WorldHudItemEquals(in _buffer[existingIndex], in item))
                {
                    return true;
                }

                if (!WorldHudProjectionEquals(in _buffer[existingIndex], in item))
                {
                    ProjectionRevision++;
                }
                else
                {
                    AddDirtyContent(in item);
                    ContentOnlyRevision++;
                }

                _buffer[existingIndex] = item;
                ContentRevision++;
                return true;
            }

            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            int index = _count++;
            _buffer[index] = item;
            if (item.StableId > 0)
            {
                _retainedIndexByStableId[item.StableId] = index;
            }
            else
            {
                _transientCount++;
            }

            ContentRevision++;
            ProjectionRevision++;
            return true;
        }

        public void Remove(int stableId)
        {
            if (stableId <= 0 || !_retainedIndexByStableId.TryGetValue(stableId, out int index))
            {
                return;
            }

            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                WorldHudItem moved = _buffer[lastIndex];
                _buffer[index] = moved;
                if (moved.StableId > 0)
                {
                    _retainedIndexByStableId[moved.StableId] = index;
                }
            }

            _count = lastIndex;
            _retainedIndexByStableId.Remove(stableId);
            AddRemovedStableId(stableId);
            ContentRevision++;
            ProjectionRevision++;
        }

        public void ClearTransient()
        {
            if (_transientCount == 0)
            {
                return;
            }

            for (int index = _count - 1; index >= 0; index--)
            {
                if (_buffer[index].StableId > 0)
                {
                    continue;
                }

                RemoveAt(index);
            }

            _transientCount = 0;
            ContentRevision++;
            ProjectionRevision++;
        }

        private void RemoveAt(int index)
        {
            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                WorldHudItem moved = _buffer[lastIndex];
                _buffer[index] = moved;
                if (moved.StableId > 0)
                {
                    _retainedIndexByStableId[moved.StableId] = index;
                }
            }

            _count = lastIndex;
        }

        public ReadOnlySpan<WorldHudItem> GetSpan() => new ReadOnlySpan<WorldHudItem>(_buffer, 0, _count);

        public ReadOnlySpan<WorldHudOwnerGroup> GetOwnerGroupSpan()
        {
            EnsureOwnerGroups();
            return new ReadOnlySpan<WorldHudOwnerGroup>(_ownerGroups, 0, _ownerGroupCount);
        }

        public ReadOnlySpan<int> GetGroupedItemIndexSpan() => new ReadOnlySpan<int>(_groupedItemIndices, 0, _count);

        public ref readonly WorldHudItem GetItemRef(int index) => ref _buffer[index];

        public bool TryGetByStableId(int stableId, out WorldHudItem item)
        {
            if (stableId > 0 && _retainedIndexByStableId.TryGetValue(stableId, out int index))
            {
                item = _buffer[index];
                return true;
            }

            item = default;
            return false;
        }

        private static bool WorldHudItemEquals(in WorldHudItem left, in WorldHudItem right)
        {
            if (left.StableId > 0 &&
                left.DirtySerial != 0 &&
                left.StableId == right.StableId &&
                left.Owner == right.Owner &&
                left.DirtySerial == right.DirtySerial &&
                WorldHudProjectionEquals(in left, in right))
            {
                return true;
            }

            return left.StableId == right.StableId &&
                   left.Owner == right.Owner &&
                   left.DirtySerial == right.DirtySerial &&
                   left.Kind == right.Kind &&
                   left.WorldPosition == right.WorldPosition &&
                   left.Color0 == right.Color0 &&
                   left.Color1 == right.Color1 &&
                   left.Width == right.Width &&
                   left.Height == right.Height &&
                   left.Value0 == right.Value0 &&
                   left.Value1 == right.Value1 &&
                   left.Id0 == right.Id0 &&
                   left.Id1 == right.Id1 &&
                   left.FontSize == right.FontSize &&
                   TextPacketEquals(in left.Text, in right.Text);
        }

        private static bool WorldHudProjectionEquals(in WorldHudItem left, in WorldHudItem right)
        {
            return left.StableId == right.StableId &&
                   left.Owner == right.Owner &&
                   left.Kind == right.Kind &&
                   left.WorldPosition == right.WorldPosition &&
                   left.Width == right.Width &&
                   left.Height == right.Height &&
                   left.FontSize == right.FontSize;
        }

        private static bool TextPacketEquals(in PresentationTextPacket left, in PresentationTextPacket right)
        {
            return left.TokenId == right.TokenId &&
                   left.ArgCount == right.ArgCount &&
                   left.Reserved0 == right.Reserved0 &&
                   left.Reserved1 == right.Reserved1 &&
                   TextArgEquals(in left.Arg0, in right.Arg0) &&
                   TextArgEquals(in left.Arg1, in right.Arg1) &&
                   TextArgEquals(in left.Arg2, in right.Arg2) &&
                   TextArgEquals(in left.Arg3, in right.Arg3);
        }

        private static bool TextArgEquals(in PresentationTextArg left, in PresentationTextArg right)
        {
            return left.Type == right.Type &&
                   left.Format == right.Format &&
                   left.Raw32 == right.Raw32;
        }

        public void Clear()
        {
            _count = 0;
            _transientCount = 0;
            DroppedSinceClear = 0;
            _retainedIndexByStableId.Clear();
            _ownerGroupIndexByKey.Clear();
            _ownerGroupCount = 0;
            _ownerGroupProjectionRevision = -1;
            _dirtyContentCount = 0;
            _removedStableIdCount = 0;
            ContentRevision++;
            ProjectionRevision++;
        }

        public ReadOnlySpan<WorldHudItem> GetDirtyContentSpan() => new(_dirtyContentBuffer, 0, _dirtyContentCount);

        public ReadOnlySpan<int> GetRemovedStableIdSpan() => new(_removedStableIds, 0, _removedStableIdCount);

        public void ClearContentDeltas()
        {
            _dirtyContentCount = 0;
            _removedStableIdCount = 0;
        }

        private void AddDirtyContent(in WorldHudItem item)
        {
            if (_dirtyContentCount >= _dirtyContentBuffer.Length)
            {
                return;
            }

            _dirtyContentBuffer[_dirtyContentCount++] = item;
        }

        private void AddRemovedStableId(int stableId)
        {
            if (_removedStableIdCount >= _removedStableIds.Length)
            {
                return;
            }

            _removedStableIds[_removedStableIdCount++] = stableId;
        }

        private void EnsureOwnerGroups()
        {
            if (_ownerGroupProjectionRevision == ProjectionRevision)
            {
                return;
            }

            _ownerGroupIndexByKey.Clear();
            _ownerGroupCount = 0;
            for (int i = 0; i < _count; i++)
            {
                ref readonly WorldHudItem item = ref _buffer[i];
                var key = new WorldHudOwnerGroupKey(item.Owner, item.WorldPosition);
                if (_ownerGroupIndexByKey.TryGetValue(key, out int groupIndex))
                {
                    _ownerGroups[groupIndex].Count++;
                    continue;
                }

                EnsureOwnerGroupCapacity(_ownerGroupCount + 1);
                _ownerGroups[_ownerGroupCount] = new WorldHudOwnerGroup(item.Owner, item.WorldPosition, 0, 1);
                _ownerGroupIndexByKey[key] = _ownerGroupCount;
                _ownerGroupCount++;
            }

            int start = 0;
            for (int groupIndex = 0; groupIndex < _ownerGroupCount; groupIndex++)
            {
                ref WorldHudOwnerGroup group = ref _ownerGroups[groupIndex];
                int count = group.Count;
                group.Start = start;
                _ownerGroupWriteOffsets[groupIndex] = start;
                start += count;
            }

            for (int i = 0; i < _count; i++)
            {
                ref readonly WorldHudItem item = ref _buffer[i];
                int groupIndex = _ownerGroupIndexByKey[new WorldHudOwnerGroupKey(item.Owner, item.WorldPosition)];
                int writeIndex = _ownerGroupWriteOffsets[groupIndex]++;
                _groupedItemIndices[writeIndex] = i;
            }

            _ownerGroupProjectionRevision = ProjectionRevision;
        }

        private void EnsureOwnerGroupCapacity(int required)
        {
            if (_ownerGroups.Length >= required)
            {
                return;
            }

            int next = _ownerGroups.Length == 0 ? 4 : _ownerGroups.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _ownerGroups, next);
            Array.Resize(ref _ownerGroupWriteOffsets, next);
        }
    }

    public struct WorldHudOwnerGroup
    {
        public Arch.Core.Entity Owner;
        public System.Numerics.Vector3 WorldPosition;
        public int Start;
        public int Count;

        public WorldHudOwnerGroup(Arch.Core.Entity owner, System.Numerics.Vector3 worldPosition, int start, int count)
        {
            Owner = owner;
            WorldPosition = worldPosition;
            Start = start;
            Count = count;
        }
    }

    internal readonly struct WorldHudOwnerGroupKey : IEquatable<WorldHudOwnerGroupKey>
    {
        private readonly Arch.Core.Entity _owner;
        private readonly System.Numerics.Vector3 _worldPosition;

        public WorldHudOwnerGroupKey(Arch.Core.Entity owner, System.Numerics.Vector3 worldPosition)
        {
            _owner = owner;
            _worldPosition = worldPosition;
        }

        public bool Equals(WorldHudOwnerGroupKey other)
        {
            return _owner == other._owner && _worldPosition == other._worldPosition;
        }

        public override bool Equals(object? obj)
        {
            return obj is WorldHudOwnerGroupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_owner, _worldPosition);
        }
    }
}
