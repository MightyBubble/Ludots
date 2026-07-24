using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Networking.Replication
{
    /// <summary>
    /// Fixed-capacity open-addressing map from non-negative int keys to int values.
    /// Matches the NetworkEntityTable probe style: power-of-two buckets, linear probe, tombstones.
    /// </summary>
    internal sealed class FixedIntSparseMap
    {
        private const byte EmptyBucket = 0;
        private const byte OccupiedBucket = 1;
        private const byte RemovedBucket = 2;

        private readonly int _entryCapacity;
        private readonly byte[] _states;
        private readonly int[] _keys;
        private readonly int[] _values;
        private int _count;

        public FixedIntSparseMap(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entryCapacity = capacity;
            int bucketCount = NextPowerOfTwo(checked(capacity * 2));
            _states = new byte[bucketCount];
            _keys = new int[bucketCount];
            _values = new int[bucketCount];
        }

        public int Capacity => _entryCapacity;
        public int Count => _count;
        public int BucketCount => _states.Length;

        public void Clear()
        {
            Array.Clear(_states);
            Array.Clear(_keys);
            Array.Clear(_values);
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out int value)
        {
            if (key < 0 || !TryFindOccupied(key, out int bucket))
            {
                value = 0;
                return false;
            }

            value = _values[bucket];
            return true;
        }

        public bool TryAdd(int key, int value)
        {
            if (key < 0)
            {
                return false;
            }

            if (!TryFindInsertionBucket(key, out int bucket, out bool occupied))
            {
                return false;
            }

            if (occupied || _count >= _entryCapacity)
            {
                return false;
            }

            _keys[bucket] = key;
            _values[bucket] = value;
            _states[bucket] = OccupiedBucket;
            _count++;
            return true;
        }

        public bool TryRemove(int key, out int value)
        {
            if (key < 0 || !TryFindOccupied(key, out int bucket))
            {
                value = 0;
                return false;
            }

            value = _values[bucket];
            _states[bucket] = RemovedBucket;
            _keys[bucket] = 0;
            _values[bucket] = 0;
            _count--;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryFindOccupied(int key, out int bucket)
        {
            int mask = _states.Length - 1;
            int candidate = (int)(HashKey(key) & (uint)mask);
            for (int probe = 0; probe < _states.Length; probe++)
            {
                byte state = _states[candidate];
                if (state == EmptyBucket)
                {
                    bucket = -1;
                    return false;
                }

                if (state == OccupiedBucket && _keys[candidate] == key)
                {
                    bucket = candidate;
                    return true;
                }

                candidate = (candidate + 1) & mask;
            }

            bucket = -1;
            return false;
        }

        private bool TryFindInsertionBucket(int key, out int bucket, out bool occupied)
        {
            int mask = _states.Length - 1;
            int candidate = (int)(HashKey(key) & (uint)mask);
            int firstRemoved = -1;
            for (int probe = 0; probe < _states.Length; probe++)
            {
                byte state = _states[candidate];
                if (state == EmptyBucket)
                {
                    bucket = firstRemoved >= 0 ? firstRemoved : candidate;
                    occupied = false;
                    return true;
                }

                if (state == OccupiedBucket && _keys[candidate] == key)
                {
                    bucket = candidate;
                    occupied = true;
                    return true;
                }

                if (state == RemovedBucket && firstRemoved < 0)
                {
                    firstRemoved = candidate;
                }

                candidate = (candidate + 1) & mask;
            }

            if (firstRemoved >= 0)
            {
                bucket = firstRemoved;
                occupied = false;
                return true;
            }

            bucket = -1;
            occupied = false;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashKey(int key)
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)key) * 16777619u;
            return hash;
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value > 1 << 30)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
