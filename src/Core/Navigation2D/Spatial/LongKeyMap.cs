using System;
using System.Runtime.CompilerServices;
using Arch.LowLevel;

namespace Ludots.Core.Navigation2D.Spatial
{
    public sealed unsafe class LongKeyMap<T> : IDisposable where T : unmanaged
    {
        private const float MaxLoadFactor = 0.7f;

        private UnsafeArray<long> _keys;
        private UnsafeArray<byte> _states;
        private UnsafeArray<T> _values;
        private int _count;
        private int _mask;

        public int Count => _count;

        public LongKeyMap(int capacity)
        {
            int size = CeilPow2(Math.Max(8, (int)(capacity / MaxLoadFactor) + 1));
            _keys = new UnsafeArray<long>(size);
            _states = new UnsafeArray<byte>(size);
            _values = new UnsafeArray<T>(size);
            _mask = size - 1;
            _count = 0;
        }

        public bool TryGet(long key, out T value)
        {
            int slot = FindSlot(key, out bool found);
            if (!found)
            {
                value = default;
                return false;
            }

            value = _values[slot];
            return true;
        }

        public bool TryGetSlot(long key, out int slot)
        {
            slot = FindSlot(key, out bool found);
            if (!found)
            {
                slot = -1;
                return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetValueRefBySlot(int slot) => ref _values[slot];

        public ref T GetValueRefOrAddDefault(long key, out bool existed)
        {
            EnsureCapacityForInsert();
            int slot = FindSlotForInsert(key, out existed);
            if (!existed)
            {
                _keys[slot] = key;
                _states[slot] = 1;
                _values[slot] = default;
                _count++;
            }

            return ref _values[slot];
        }

        public bool Remove(long key, out T removedValue)
        {
            int slot = FindSlot(key, out bool found);
            if (!found)
            {
                removedValue = default;
                return false;
            }

            removedValue = _values[slot];
            _states[slot] = 2;
            _values[slot] = default;
            _count--;
            return true;
        }

        public void Clear()
        {
            UnsafeArray.Fill(ref _states, (byte)0);
            _count = 0;
        }

        public void Dispose()
        {
            _keys.Dispose();
            _states.Dispose();
            _values.Dispose();
        }

        public Enumerator GetEnumerator() => new Enumerator(_keys, _states, _values);

        public unsafe struct Enumerator
        {
            private readonly UnsafeArray<long> _keys;
            private readonly UnsafeArray<byte> _states;
            private readonly UnsafeArray<T> _values;
            private int _index;

            public Enumerator(UnsafeArray<long> keys, UnsafeArray<byte> states, UnsafeArray<T> values)
            {
                _keys = keys;
                _states = states;
                _values = values;
                _index = -1;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    _index++;
                    if (_index >= _states.Length) return false;
                    if (_states[_index] == 1) return true;
                }
            }

            public long CurrentKey => _keys[_index];
            public ref T CurrentValue => ref _values[_index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindSlot(long key, out bool found)
        {
            int slot = (int)Hash(key) & _mask;
            while (true)
            {
                byte state = _states[slot];
                if (state == 0)
                {
                    found = false;
                    return slot;
                }

                if (state == 1 && _keys[slot] == key)
                {
                    found = true;
                    return slot;
                }

                slot = (slot + 1) & _mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindSlotForInsert(long key, out bool existed)
        {
            int slot = (int)Hash(key) & _mask;
            int firstTombstone = -1;
            while (true)
            {
                byte state = _states[slot];
                if (state == 0)
                {
                    existed = false;
                    return firstTombstone >= 0 ? firstTombstone : slot;
                }

                if (state == 1)
                {
                    if (_keys[slot] == key)
                    {
                        existed = true;
                        return slot;
                    }
                }
                else if (state == 2 && firstTombstone < 0)
                {
                    firstTombstone = slot;
                }

                slot = (slot + 1) & _mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacityForInsert()
        {
            int size = _states.Length;
            int threshold = (int)(size * MaxLoadFactor);
            if (_count + 1 <= threshold) return;
            Rehash(size * 2);
        }

        private void Rehash(int newSize)
        {
            var oldKeys = _keys;
            var oldStates = _states;
            var oldValues = _values;

            _keys = new UnsafeArray<long>(newSize);
            _states = new UnsafeArray<byte>(newSize);
            _values = new UnsafeArray<T>(newSize);
            _mask = newSize - 1;

            int oldLen = oldStates.Length;
            _count = 0;

            for (int i = 0; i < oldLen; i++)
            {
                if (oldStates[i] != 1) continue;
                long key = oldKeys[i];
                ref T dst = ref GetValueRefOrAddDefault(key, out _);
                dst = oldValues[i];
            }

            oldKeys.Dispose();
            oldStates.Dispose();
            oldValues.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Hash(long key)
        {
            ulong x = (ulong)key;
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb9fe1a85ec53UL;
            x ^= x >> 33;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CeilPow2(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v < 8 ? 8 : v;
        }
    }
}
