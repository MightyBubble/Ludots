using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Absolute-max tag bitset for rule masks, catalog tags, and temporary masks.
    /// Live id space is plan-gated via <see cref="ValidateTagId"/>; entity storage uses
    /// <see cref="GameplayTagContainer"/> row handles into <see cref="GasWorldColumnStore"/>.
    /// </summary>
    public unsafe struct GameplayTagBitSet
    {
        public const int MaxWords =
            GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace / GasLoadTimeCapacityPlan.TagBitsPerWord;

        /// <summary>Obsolete bridge for call sites still using the old 255 usable-id constant.</summary>
        public const int MAX_TAG_ID = GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace - 1;

        public fixed ulong Words[MaxWords];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong WordAt(int index) => Words[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetWord(int index, ulong value) => Words[index] = value;

        public static int ActiveWordCount()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.TagUlongWordCount
                : MaxWords;
        }

        public static int ActiveMaxUsableTagId()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.MaxUsableTagId
                : MAX_TAG_ID;
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int words = ActiveWordCount();
                for (int i = 0; i < words; i++)
                {
                    if (Words[i] != 0UL)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTag(int tagId)
        {
            ValidateTagId(tagId);
            Words[tagId >> 6] |= 1UL << (tagId & 63);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveTag(int tagId)
        {
            ValidateTagId(tagId);
            Words[tagId >> 6] &= ~(1UL << (tagId & 63));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTag(int tagId)
        {
            ValidateTagId(tagId);
            return (Words[tagId >> 6] & (1UL << (tagId & 63))) != 0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                Words[i] = 0UL;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(in GameplayTagBitSet required)
        {
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                if ((Words[i] & required.Words[i]) != required.Words[i])
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(in GameplayTagBitSet other)
        {
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                if ((Words[i] & other.Words[i]) != 0UL)
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FirstCommonTag(in GameplayTagBitSet other)
        {
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                ulong intersection = Words[i] & other.Words[i];
                if (intersection != 0UL)
                {
                    return (i << 6) + System.Numerics.BitOperations.TrailingZeroCount(intersection);
                }
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CountCommonTags(in GameplayTagBitSet other)
        {
            int count = 0;
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                count += System.Numerics.BitOperations.PopCount(Words[i] & other.Words[i]);
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AnyWordBits()
        {
            ulong any = 0UL;
            int words = ActiveWordCount();
            for (int i = 0; i < words; i++)
            {
                any |= Words[i];
            }

            return any;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateTagId(int tagId)
        {
            int max = ActiveMaxUsableTagId();
            if (tagId <= 0 || tagId > max)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tagId),
                    tagId,
                    $"tagId must be in [1, {max}].");
            }
        }
    }
}
