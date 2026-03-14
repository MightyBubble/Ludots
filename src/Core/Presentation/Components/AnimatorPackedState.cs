using System;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// 128-bit compact animator payload for skinned adapters.
    /// Word0 stores controller/state/timing/flags; Word1 stores up to 64 bool/trigger bits.
    /// This contract does not contain pose matrices, bone palettes, or GPU skin streams.
    /// </summary>
    public struct AnimatorPackedState
    {
        public const int PackedWordCount = 2;
        public const int PackedByteSize = 16;
        public const int MaxControllerId = 4095;
        public const int MaxStateIndex = 1023;
        public const int MaxNormalizedTimeQuantized = 4095;
        public const int MaxTransitionQuantized = 1023;
        public const int MaxParameterBits = 64;

        private const int ControllerShift = 0;
        private const int ControllerBits = 12;
        private const int PrimaryStateShift = 12;
        private const int StateBits = 10;
        private const int SecondaryStateShift = 22;
        private const int NormalizedTimeShift = 32;
        private const int NormalizedTimeBits = 12;
        private const int TransitionShift = 44;
        private const int TransitionBits = 10;
        private const int FlagsShift = 54;
        private const int FlagsBits = 10;

        private const ulong ControllerMask = (1UL << ControllerBits) - 1UL;
        private const ulong StateMask = (1UL << StateBits) - 1UL;
        private const ulong NormalizedTimeMask = (1UL << NormalizedTimeBits) - 1UL;
        private const ulong TransitionMask = (1UL << TransitionBits) - 1UL;
        private const ulong FlagsMask = (1UL << FlagsBits) - 1UL;

        public ulong Word0;
        public ulong Word1;

        public static AnimatorPackedState Create(int controllerId)
        {
            var state = default(AnimatorPackedState);
            state.SetControllerId(controllerId);
            state.SetFlags(AnimatorPackedStateFlags.Active);
            return state;
        }

        public readonly int GetControllerId() => (int)((Word0 >> ControllerShift) & ControllerMask);
        public readonly int GetPrimaryStateIndex() => (int)((Word0 >> PrimaryStateShift) & StateMask);
        public readonly int GetSecondaryStateIndex() => (int)((Word0 >> SecondaryStateShift) & StateMask);
        public readonly AnimatorPackedStateFlags GetFlags() => (AnimatorPackedStateFlags)((Word0 >> FlagsShift) & FlagsMask);

        public readonly float GetNormalizedTime01()
        {
            int quantized = (int)((Word0 >> NormalizedTimeShift) & NormalizedTimeMask);
            return quantized / (float)MaxNormalizedTimeQuantized;
        }

        public readonly float GetTransitionProgress01()
        {
            int quantized = (int)((Word0 >> TransitionShift) & TransitionMask);
            return quantized / (float)MaxTransitionQuantized;
        }

        public readonly bool GetParameterBit(int index)
        {
            ValidateParameterBit(index);
            return ((Word1 >> index) & 1UL) != 0;
        }

        public void SetControllerId(int controllerId)
        {
            if ((uint)controllerId > MaxControllerId)
                throw new ArgumentOutOfRangeException(nameof(controllerId), $"Animator controller id must be in [0, {MaxControllerId}].");

            Word0 = WriteField(Word0, ControllerShift, ControllerMask, (ulong)controllerId);
        }

        public void SetPrimaryStateIndex(int stateIndex)
        {
            ValidateStateIndex(stateIndex, nameof(stateIndex));
            Word0 = WriteField(Word0, PrimaryStateShift, StateMask, (ulong)stateIndex);
        }

        public void SetSecondaryStateIndex(int stateIndex)
        {
            ValidateStateIndex(stateIndex, nameof(stateIndex));
            Word0 = WriteField(Word0, SecondaryStateShift, StateMask, (ulong)stateIndex);
        }

        public void SetNormalizedTime01(float value)
        {
            int quantized = Quantize(value, MaxNormalizedTimeQuantized);
            Word0 = WriteField(Word0, NormalizedTimeShift, NormalizedTimeMask, (ulong)quantized);
        }

        public void SetTransitionProgress01(float value)
        {
            int quantized = Quantize(value, MaxTransitionQuantized);
            Word0 = WriteField(Word0, TransitionShift, TransitionMask, (ulong)quantized);
        }

        public void SetFlags(AnimatorPackedStateFlags flags)
        {
            Word0 = WriteField(Word0, FlagsShift, FlagsMask, (ulong)flags & FlagsMask);
        }

        public void SetParameterBit(int index, bool enabled)
        {
            ValidateParameterBit(index);
            ulong mask = 1UL << index;
            if (enabled)
                Word1 |= mask;
            else
                Word1 &= ~mask;
        }

        private static void ValidateStateIndex(int stateIndex, string paramName)
        {
            if ((uint)stateIndex > MaxStateIndex)
                throw new ArgumentOutOfRangeException(paramName, $"Animator state index must be in [0, {MaxStateIndex}].");
        }

        private static void ValidateParameterBit(int index)
        {
            if ((uint)index >= MaxParameterBits)
                throw new ArgumentOutOfRangeException(nameof(index), $"Animator parameter bit index must be in [0, {MaxParameterBits - 1}].");
        }

        private static int Quantize(float value, int max)
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            return (int)MathF.Round(clamped * max);
        }

        private static ulong WriteField(ulong word, int shift, ulong mask, ulong value)
        {
            ulong shiftedMask = mask << shift;
            return (word & ~shiftedMask) | ((value & mask) << shift);
        }
    }
}
