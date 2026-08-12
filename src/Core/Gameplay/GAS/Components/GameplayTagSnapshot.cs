using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct GameplayTagSnapshot
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

        public void CopyFromContainer(in GameplayTagContainer tags)
        {
            int words = ActiveWordCount();
            Span<ulong> scratch = stackalloc ulong[MaxWords];
            tags.CopyWordsTo(scratch.Slice(0, words));
            for (int i = 0; i < words; i++)
            {
                Bits[i] = scratch[i];
            }
        }

        public bool Has(int tagId)
        {
            ValidateTagId(tagId);
            return (Bits[tagId >> 6] & (1UL << (tagId & 63))) != 0UL;
        }

        public void Set(int tagId, bool value)
        {
            ValidateTagId(tagId);
            int word = tagId >> 6;
            ulong mask = 1UL << (tagId & 63);
            Bits[word] = value ? (Bits[word] | mask) : (Bits[word] & ~mask);
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
                    $"tagId must be in [1, {max}] for tag snapshot.");
            }
        }
    }
}
