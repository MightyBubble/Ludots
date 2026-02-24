using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct BlackboardFloatBuffer
    {
        public int Count;
        public fixed int Keys[GasConstants.MAX_BLACKBOARD_ENTRIES];
        public fixed float Values[GasConstants.MAX_BLACKBOARD_ENTRIES];

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

                if (Count >= GasConstants.MAX_BLACKBOARD_ENTRIES) return;
                keys[Count] = key;
                values[Count] = value;
                Count++;
            }
        }
    }
}
