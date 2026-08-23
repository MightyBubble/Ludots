using System.Runtime.CompilerServices;

namespace Ludots.Core.Presentation.Presenters
{
    public enum ParamLane : byte
    {
        Float = 0,
        Int = 1,
        Vector = 2,
    }

    public unsafe struct PresenterFloatParams
    {
        public const int MAX_ENTRIES = 16;
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed float Values[MAX_ENTRIES];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out float value)
        {
            fixed (int* keys = Keys)
            fixed (float* values = Values)
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
        public void Set(int key, float value)
        {
            fixed (int* keys = Keys)
            fixed (float* values = Values)
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
            fixed (float* values = Values)
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

    public unsafe struct PresenterFloatDefaults
    {
        public const int MAX_ENTRIES = 16;
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed float Values[MAX_ENTRIES];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out float value)
        {
            fixed (int* keys = Keys)
            fixed (float* values = Values)
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
        public void Set(int key, float value)
        {
            fixed (int* keys = Keys)
            fixed (float* values = Values)
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
