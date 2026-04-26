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
                if (WorldHudItemEquals(in _buffer[existingIndex], in item))
                {
                    return true;
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

        private static bool WorldHudItemEquals(in WorldHudItem left, in WorldHudItem right)
        {
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
            ContentRevision++;
        }
    }
}
