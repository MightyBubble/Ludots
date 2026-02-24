using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Stores GameplayTags using a fixed bitset.
    /// Supports up to 256 unique tags (4 * 64 bits).
    /// tagId must be in range [1, 255]. Out-of-range throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public unsafe struct GameplayTagContainer
    {
        private const int ULONG_COUNT = 4;
        /// <summary>Maximum number of distinct tags this container can hold (256 = 4 * 64 bits).</summary>
        public const int MAX_TAG_ID = ULONG_COUNT * 64 - 1; // 255

        public fixed ulong Bits[ULONG_COUNT];

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                for (int i = 0; i < ULONG_COUNT; i++)
                {
                    if (Bits[i] != 0) return false;
                }
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTag(int tagId)
        {
            ValidateTagId(tagId);
            int index = tagId / 64;
            int bit = tagId % 64;
            Bits[index] |= (1UL << bit);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveTag(int tagId)
        {
            ValidateTagId(tagId);
            int bit = tagId % 64;
            Bits[tagId / 64] &= ~(1UL << bit);
        }

        /// <summary>
        /// Remove all tags in a contiguous range [startTagId, endTagId].
        /// More efficient than calling RemoveTag multiple times.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveTagRange(int startTagId, int endTagId)
        {
            if (startTagId <= 0 || endTagId < startTagId)
                throw new ArgumentOutOfRangeException(nameof(startTagId), $"Invalid tag range [{startTagId}, {endTagId}].");
            if (endTagId > MAX_TAG_ID)
                throw new ArgumentOutOfRangeException(nameof(endTagId), $"endTagId {endTagId} exceeds MAX_TAG_ID ({MAX_TAG_ID}).");

            int startIndex = startTagId / 64;
            int endIndex = endTagId / 64;
            
            int startBit = startTagId % 64;
            int endBit = endTagId % 64;
            
            if (startIndex == endIndex)
            {
                // All bits in the same ulong - create a mask for the range
                ulong mask = ((1UL << (endBit - startBit + 1)) - 1) << startBit;
                Bits[startIndex] &= ~mask;
            }
            else
            {
                // Clear bits in start ulong (from startBit to 63)
                ulong startMask = ~0UL << startBit;
                Bits[startIndex] &= ~startMask;
                
                // Clear all middle ulongs completely
                for (int i = startIndex + 1; i < endIndex; i++)
                {
                    Bits[i] = 0;
                }
                
                // Clear bits in end ulong (from 0 to endBit)
                if (endIndex < ULONG_COUNT)
                {
                    ulong endMask = (1UL << (endBit + 1)) - 1;
                    Bits[endIndex] &= ~endMask;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTag(int tagId)
        {
            ValidateTagId(tagId);
            int bit = tagId % 64;
            return (Bits[tagId / 64] & (1UL << bit)) != 0;
        }

        /// <summary>
        /// Check if any tag in the range [startTagId, endTagId] is set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAnyTagInRange(int startTagId, int endTagId)
        {
            if (startTagId <= 0 || endTagId < startTagId)
                throw new ArgumentOutOfRangeException(nameof(startTagId), $"Invalid tag range [{startTagId}, {endTagId}].");
            if (endTagId > MAX_TAG_ID)
                throw new ArgumentOutOfRangeException(nameof(endTagId), $"endTagId {endTagId} exceeds MAX_TAG_ID ({MAX_TAG_ID}).");

            int startIndex = startTagId / 64;
            int endIndex = endTagId / 64;
            
            int startBit = startTagId % 64;
            int endBit = endTagId % 64;
            
            if (startIndex == endIndex)
            {
                ulong mask = ((1UL << (endBit - startBit + 1)) - 1) << startBit;
                return (Bits[startIndex] & mask) != 0;
            }
            else
            {
                // Check start ulong
                ulong startMask = ~0UL << startBit;
                if ((Bits[startIndex] & startMask) != 0) return true;
                
                // Check middle ulongs
                for (int i = startIndex + 1; i < endIndex; i++)
                {
                    if (Bits[i] != 0) return true;
                }
                
                // Check end ulong
                if (endIndex < ULONG_COUNT)
                {
                    ulong endMask = (1UL << (endBit + 1)) - 1;
                    if ((Bits[endIndex] & endMask) != 0) return true;
                }
                
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(in GameplayTagContainer required)
        {
            for (int i = 0; i < ULONG_COUNT; i++)
            {
                if ((Bits[i] & required.Bits[i]) != required.Bits[i]) return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(in GameplayTagContainer other)
        {
            for (int i = 0; i < ULONG_COUNT; i++)
            {
                if ((Bits[i] & other.Bits[i]) != 0) return true;
            }
            return false;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for(int i=0; i<ULONG_COUNT; i++)
            {
                Bits[i] = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateTagId(int tagId)
        {
            if ((uint)tagId > MAX_TAG_ID || tagId == 0)
                throw new ArgumentOutOfRangeException(nameof(tagId), tagId, $"tagId must be in [1, {MAX_TAG_ID}].");
        }
    }
}
