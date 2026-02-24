using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct BlackboardIntBuffer
    {
        public int Count;
        public fixed int Keys[GasConstants.MAX_BLACKBOARD_ENTRIES];
        public fixed int Values[GasConstants.MAX_BLACKBOARD_ENTRIES];

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

                if (Count >= GasConstants.MAX_BLACKBOARD_ENTRIES) return;
                keys[Count] = key;
                values[Count] = value;
                Count++;
            }
        }

        /// <summary>
        /// Remove an entry by key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(int key)
        {
            fixed (int* keys = Keys)
            fixed (int* values = Values)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;

                    // Shift remaining entries
                    for (int j = i; j < Count - 1; j++)
                    {
                        keys[j] = keys[j + 1];
                        values[j] = values[j + 1];
                    }
                    Count--;
                    return true;
                }
            }
            return false;
        }
    }
}
