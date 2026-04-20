using System.Runtime.CompilerServices;

namespace Ludots.Core.Presentation.Performers
{
    public unsafe struct PerformerIntParams
    {
        public const int MAX_ENTRIES = 16;
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed int Values[MAX_ENTRIES];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out int value)
        {
            fixed (int* keys = Keys)
            fixed (int* values = Values)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    value = values[i];
                    return true;
                }
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, int value)
        {
            fixed (int* keys = Keys)
            fixed (int* values = Values)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    values[i] = value;
                    return;
                }
                if (Count >= MAX_ENTRIES) return;
                keys[Count] = key;
                values[Count] = value;
                Count++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Clear(int key)
        {
            fixed (int* keys = Keys)
            fixed (int* values = Values)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    Count--;
                    if (i < Count)
                    {
                        keys[i] = keys[Count];
                        values[i] = values[Count];
                    }
                    return true;
                }
            }
            return false;
        }

        public void ClearAll() { Count = 0; }
    }

    public unsafe struct PerformerIntDefaults
    {
        public const int MAX_ENTRIES = 16;
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed int Values[MAX_ENTRIES];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out int value)
        {
            fixed (int* keys = Keys)
            fixed (int* values = Values)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    value = values[i];
                    return true;
                }
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, int value)
        {
            fixed (int* keys = Keys)
            fixed (int* values = Values)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    values[i] = value;
                    return;
                }
                if (Count >= MAX_ENTRIES) return;
                keys[Count] = key;
                values[Count] = value;
                Count++;
            }
        }
    }
}
