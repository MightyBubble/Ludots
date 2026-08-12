using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct GameplayTagEffectiveChangedBits
    {
        public const int MaxWords =
            GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace / GasLoadTimeCapacityPlan.TagBitsPerWord;

        public fixed ulong Bits[MaxWords];

        public static int ActiveWordCount()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.TagUlongWordCount
                : MaxWords;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong WordAt(int index) => Bits[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetWord(int index, ulong value) => Bits[index] = value;

        public void Mark(int tagId)
        {
            ValidateTagId(tagId);
            Bits[tagId >> 6] |= 1UL << (tagId & 63);
        }

        public void Clear()
        {
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                Bits[i] = 0UL;
            }
        }

        public bool IsAnyBitSet()
        {
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                if (Bits[i] != 0UL)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateTagId(int tagId)
        {
            int max = GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.MaxUsableTagId
                : GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace - 1;
            if (tagId <= 0 || tagId > max)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tagId),
                    tagId,
                    $"tagId must be in [1, {max}] for effective changed bits.");
            }
        }
    }
}
