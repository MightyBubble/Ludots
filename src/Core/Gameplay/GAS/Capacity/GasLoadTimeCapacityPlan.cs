using System;

namespace Ludots.Core.Gameplay.GAS.Capacity
{
    /// <summary>
    /// Session capacity frozen after mod registration. Hot path must not resize.
    /// </summary>
    public sealed class GasLoadTimeCapacityPlan
    {
        public const int AbsoluteMaxAttributeSlots = 1024;
        public const int AbsoluteMaxTagIdSpace = 4096;
        public const int TagBitsPerWord = 64;

        public int AttributeSlotCount { get; }
        public int TagIdSpace { get; }
        public int TagUlongWordCount { get; }
        public int MaxUsableTagId { get; }
        public int RegisteredAttributeCount { get; }
        public int RegisteredTagCount { get; }

        private GasLoadTimeCapacityPlan(
            int attributeSlotCount,
            int tagIdSpace,
            int registeredAttributeCount,
            int registeredTagCount)
        {
            AttributeSlotCount = attributeSlotCount;
            TagIdSpace = tagIdSpace;
            TagUlongWordCount = tagIdSpace / TagBitsPerWord;
            MaxUsableTagId = tagIdSpace - 1;
            RegisteredAttributeCount = registeredAttributeCount;
            RegisteredTagCount = registeredTagCount;
        }

        /// <summary>
        /// Builds a plan from dense registry counts. Tag id 0 stays reserved, so
        /// <paramref name="registeredTagCount"/> usable tags require space >= count+1.
        /// </summary>
        public static GasLoadTimeCapacityPlan FromRegisteredCounts(
            int registeredAttributeCount,
            int registeredTagCount,
            GasLoadTimeCapacityRounding rounding = GasLoadTimeCapacityRounding.WordAlignTags)
        {
            if (registeredAttributeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registeredAttributeCount));
            }

            if (registeredTagCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registeredTagCount));
            }

            int attributeSlots = registeredAttributeCount;
            if (attributeSlots == 0)
            {
                attributeSlots = 0;
            }

            int requiredTagSpace = registeredTagCount == 0 ? 0 : registeredTagCount + 1;
            int tagIdSpace = rounding switch
            {
                GasLoadTimeCapacityRounding.Exact => requiredTagSpace,
                GasLoadTimeCapacityRounding.WordAlignTags => AlignUp(requiredTagSpace, TagBitsPerWord),
                _ => throw new ArgumentOutOfRangeException(nameof(rounding)),
            };

            if (attributeSlots > AbsoluteMaxAttributeSlots)
            {
                throw new InvalidOperationException(
                    $"Attribute kind count {attributeSlots} exceeds absolute ceiling {AbsoluteMaxAttributeSlots}. " +
                    "Raise content budget or AbsoluteMaxAttributeSlots via capacity epic — do not grow mid-session.");
            }

            if (tagIdSpace > AbsoluteMaxTagIdSpace)
            {
                throw new InvalidOperationException(
                    $"Tag id space {tagIdSpace} (from {registeredTagCount} usable tags) exceeds absolute ceiling {AbsoluteMaxTagIdSpace}. " +
                    "Raise content budget or AbsoluteMaxTagIdSpace via capacity epic — do not grow mid-session.");
            }

            if (tagIdSpace > 0 && tagIdSpace % TagBitsPerWord != 0)
            {
                throw new InvalidOperationException(
                    $"Tag id space {tagIdSpace} must be a multiple of {TagBitsPerWord} for ulong bitsets.");
            }

            return new GasLoadTimeCapacityPlan(
                attributeSlots,
                tagIdSpace,
                registeredAttributeCount,
                registeredTagCount);
        }

        /// <summary>
        /// Baseline plan matching today's embedded AttributeBuffer(64) / GameplayTagContainer(256).
        /// </summary>
        public static GasLoadTimeCapacityPlan CreateLegacyEmbeddedBaseline()
        {
            return new GasLoadTimeCapacityPlan(
                attributeSlotCount: 64,
                tagIdSpace: 256,
                registeredAttributeCount: 64,
                registeredTagCount: 255);
        }

        public void ValidateAttributeId(int attributeId)
        {
            if ((uint)attributeId >= (uint)AttributeSlotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attributeId),
                    attributeId,
                    $"attributeId must be in [0, {AttributeSlotCount - 1}] for frozen plan.");
            }
        }

        public void ValidateTagId(int tagId)
        {
            if (tagId <= 0 || tagId > MaxUsableTagId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tagId),
                    tagId,
                    $"tagId must be in [1, {MaxUsableTagId}] for frozen plan.");
            }
        }

        private static int AlignUp(int value, int multiple)
        {
            if (value <= 0)
            {
                return 0;
            }

            int rem = value % multiple;
            return rem == 0 ? value : value + (multiple - rem);
        }
    }

    public enum GasLoadTimeCapacityRounding : byte
    {
        Exact = 0,
        WordAlignTags = 1,
    }
}
