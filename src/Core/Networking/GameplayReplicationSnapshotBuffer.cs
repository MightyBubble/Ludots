using System;

namespace Ludots.Core.Networking
{
    public sealed class GameplayReplicationSnapshotBuffer
    {
        private readonly GameplayReplicationSnapshotItem[] _items;
        private int _count;

        public GameplayReplicationSnapshotBuffer(int capacity = 8192)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new GameplayReplicationSnapshotItem[capacity];
        }

        public int SimTick { get; private set; }
        public int Count => _count;
        public int Capacity => _items.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public void BeginRebuild(int simTick)
        {
            SimTick = simTick;
            _count = 0;
            DroppedSinceClear = 0;
        }

        public bool TryAdd(in GameplayReplicationSnapshotItem item)
        {
            if (_count >= _items.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _items[_count++] = item;
            return true;
        }

        public ReadOnlySpan<GameplayReplicationSnapshotItem> GetSpan() => new(_items, 0, _count);
    }
}
