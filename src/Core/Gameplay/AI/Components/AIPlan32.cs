using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.AI.Components
{
    public unsafe struct AIPlan32
    {
        public const int MaxActions = 32;

        public int Length;
        public int Cursor;
        public fixed int ActionIds[MaxActions];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Length = 0;
            Cursor = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(int actionId)
        {
            if (Length >= MaxActions) return false;
            fixed (int* ids = ActionIds) ids[Length] = actionId;
            Length++;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetCurrent(out int actionId)
        {
            if ((uint)Cursor >= (uint)Length)
            {
                actionId = -1;
                return false;
            }
            fixed (int* ids = ActionIds) actionId = ids[Cursor];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance()
        {
            Cursor++;
        }

        public readonly bool IsDone => Cursor >= Length;
    }
}

