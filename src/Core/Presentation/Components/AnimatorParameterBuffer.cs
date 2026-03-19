using System;

namespace Ludots.Core.Presentation.Components
{
    public struct AnimatorParameterBuffer
    {
        public const int MaxFloatParameters = 8;
        public const int MaxBitParameters = 64;

        public ulong BoolBits;
        public ulong TriggerBits;
        public float Float0;
        public float Float1;
        public float Float2;
        public float Float3;
        public float Float4;
        public float Float5;
        public float Float6;
        public float Float7;

        public readonly float GetFloat(int index)
        {
            return index switch
            {
                0 => Float0,
                1 => Float1,
                2 => Float2,
                3 => Float3,
                4 => Float4,
                5 => Float5,
                6 => Float6,
                7 => Float7,
                _ => throw new ArgumentOutOfRangeException(nameof(index), $"Animator float parameter index must be in [0, {MaxFloatParameters - 1}]."),
            };
        }

        public void SetFloat(int index, float value)
        {
            switch (index)
            {
                case 0: Float0 = value; break;
                case 1: Float1 = value; break;
                case 2: Float2 = value; break;
                case 3: Float3 = value; break;
                case 4: Float4 = value; break;
                case 5: Float5 = value; break;
                case 6: Float6 = value; break;
                case 7: Float7 = value; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), $"Animator float parameter index must be in [0, {MaxFloatParameters - 1}].");
            }
        }

        public readonly bool GetBool(int index)
        {
            ValidateBitIndex(index);
            return ((BoolBits >> index) & 1UL) != 0;
        }

        public void SetBool(int index, bool enabled)
        {
            ValidateBitIndex(index);
            ulong mask = 1UL << index;
            if (enabled)
            {
                BoolBits |= mask;
            }
            else
            {
                BoolBits &= ~mask;
            }
        }

        public readonly bool HasTrigger(int index)
        {
            ValidateBitIndex(index);
            return ((TriggerBits >> index) & 1UL) != 0;
        }

        public void SetTrigger(int index, bool enabled = true)
        {
            ValidateBitIndex(index);
            ulong mask = 1UL << index;
            if (enabled)
            {
                TriggerBits |= mask;
            }
            else
            {
                TriggerBits &= ~mask;
            }
        }

        public bool ConsumeTrigger(int index)
        {
            bool active = HasTrigger(index);
            if (active)
            {
                SetTrigger(index, enabled: false);
            }

            return active;
        }

        public readonly ulong BuildPackedBits()
        {
            return BoolBits | TriggerBits;
        }

        private static void ValidateBitIndex(int index)
        {
            if ((uint)index >= MaxBitParameters)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"Animator bit parameter index must be in [0, {MaxBitParameters - 1}].");
            }
        }
    }
}
