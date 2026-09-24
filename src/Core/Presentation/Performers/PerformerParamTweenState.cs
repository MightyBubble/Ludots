using System.Runtime.CompilerServices;

namespace Ludots.Core.Presentation.Performers
{
    public unsafe struct PerformerParamTweenState
    {
        private const int MaxSlots = 32;

        internal uint StartedMask;
        internal uint CompletedMask;
        internal uint ValidatedTargetMask;
        private fixed float _elapsedSeconds[MaxSlots];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float GetElapsedSeconds(int slotIndex)
        {
            fixed (float* elapsedSeconds = _elapsedSeconds)
            {
                return elapsedSeconds[slotIndex];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetElapsedSeconds(int slotIndex, float value)
        {
            fixed (float* elapsedSeconds = _elapsedSeconds)
            {
                elapsedSeconds[slotIndex] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ResetSlot(int slotIndex)
        {
            uint bit = 1u << slotIndex;
            StartedMask &= ~bit;
            CompletedMask &= ~bit;
            ValidatedTargetMask &= ~bit;
            SetElapsedSeconds(slotIndex, 0f);
        }
    }
}
