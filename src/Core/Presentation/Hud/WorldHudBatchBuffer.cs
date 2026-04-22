using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class WorldHudBatchBuffer
    {
        private readonly WorldHudItem[] _buffer;
        private readonly Dictionary<int, int> _retainedIndexByStableId = new();
        private int _count;
        private int _transientCount;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }
        public int ContentRevision { get; private set; }

        public WorldHudBatchBuffer(int capacity = 65536)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new WorldHudItem[capacity];
        }

        public bool TryAdd(in WorldHudItem item)
        {
            if (item.StableId > 0 && _retainedIndexByStableId.TryGetValue(item.StableId, out int existingIndex))
            {
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
            ContentRevision++;
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

        public void Clear()
        {
            _count = 0;
            _transientCount = 0;
            DroppedSinceClear = 0;
            _retainedIndexByStableId.Clear();
            ContentRevision++;
        }
    }
}
