using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// 属性变化触发器（延迟到下一帧执行）
    /// </summary>
    public struct AttributeChangedTrigger
    {
        public Entity Target;
        public int AttributeId;
        public float OldValue;
        public float NewValue;
    }

    /// <summary>
    /// Tag变化触发器（延迟到下一帧执行）
    /// </summary>
    public struct TagChangedTrigger
    {
        public Entity Target;
        public int TagId;
        public bool WasPresent;
        public bool IsPresent;
    }

    /// <summary>
    /// TagCount变化触发器（延迟到下一帧执行）
    /// </summary>
    public struct TagCountChangedTrigger
    {
        public Entity Target;
        public int TagId;
        public ushort OldCount;
        public ushort NewCount;
    }

    /// <summary>
    /// Dirty markers for deferred triggers.
    /// Attribute and tag dirty words are absolute-max fixed; live ids fail-closed against the plan.
    /// </summary>
    public unsafe struct DirtyFlags
    {
        public const int MAX_ATTR_DIRTY_WORDS =
            GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots / GasLoadTimeCapacityPlan.TagBitsPerWord;

        public const int MAX_TAG_DIRTY_WORDS =
            GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace / GasLoadTimeCapacityPlan.TagBitsPerWord;

        /// <summary>Obsolete bridge for the old single-ulong 64-slot mask.</summary>
        public const int MAX_ATTRS = 64;

        /// <summary>Obsolete bridge for the old 256-tag byte dirty map.</summary>
        public const int TAG_DIRTY_BYTES = MAX_TAG_DIRTY_WORDS * (GasLoadTimeCapacityPlan.TagBitsPerWord / 8);

        public fixed ulong AttributeDirtyWords[MAX_ATTR_DIRTY_WORDS];
        public byte DeferredTriggerQueued;
        public fixed ulong TagDirtyWords[MAX_TAG_DIRTY_WORDS];

        /// <summary>Word 0 of the multi-word attribute dirty set (ids 0..63).</summary>
        public ulong AttributeDirtyMask
        {
            get => AttributeDirtyWords[0];
            set => AttributeDirtyWords[0] = value;
        }

        public void MarkAttributeDirty(int attrId)
        {
            ValidateAttributeId(attrId);
            AttributeDirtyWords[attrId >> 6] |= 1UL << (attrId & 63);
        }

        public void MarkTagDirty(int tagId)
        {
            ValidateTagId(tagId);
            TagDirtyWords[tagId >> 6] |= 1UL << (tagId & 63);
        }

        public bool IsAttributeDirty(int attrId)
        {
            ValidateAttributeId(attrId);
            return (AttributeDirtyWords[attrId >> 6] & (1UL << (attrId & 63))) != 0UL;
        }

        public bool IsTagDirty(int tagId)
        {
            ValidateTagId(tagId);
            return (TagDirtyWords[tagId >> 6] & (1UL << (tagId & 63))) != 0UL;
        }

        public void Clear()
        {
            for (int i = 0; i < MAX_ATTR_DIRTY_WORDS; i++)
            {
                AttributeDirtyWords[i] = 0UL;
            }

            for (int i = 0; i < MAX_TAG_DIRTY_WORDS; i++)
            {
                TagDirtyWords[i] = 0UL;
            }
        }

        public bool IsAnyAttributeDirty()
        {
            int words = ActiveAttributeDirtyWordCount();
            for (int i = 0; i < words; i++)
            {
                if (AttributeDirtyWords[i] != 0UL)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsAnyTagDirty()
        {
            int words = ActiveTagDirtyWordCount();
            for (int i = 0; i < words; i++)
            {
                if (TagDirtyWords[i] != 0UL)
                {
                    return true;
                }
            }

            return false;
        }

        public void ClearAttributeDirty(int attrId)
        {
            ValidateAttributeId(attrId);
            AttributeDirtyWords[attrId >> 6] &= ~(1UL << (attrId & 63));
        }

        public void ClearTagDirty(int tagId)
        {
            ValidateTagId(tagId);
            TagDirtyWords[tagId >> 6] &= ~(1UL << (tagId & 63));
        }

        public static int ActiveAttributeSlotCount()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.AttributeSlotCount
                : GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots;
        }

        public static int ActiveAttributeDirtyWordCount()
        {
            return GasWorldColumnStore.AttrDirtyWordCount(ActiveAttributeSlotCount());
        }

        public static int ActiveMaxUsableTagId()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.MaxUsableTagId
                : GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace - 1;
        }

        public static int ActiveTagDirtyWordCount()
        {
            if (!GasLoadTimeCapacitySession.IsFrozen)
            {
                return MAX_TAG_DIRTY_WORDS;
            }

            return GasLoadTimeCapacitySession.Plan.TagUlongWordCount;
        }

        private static void ValidateAttributeId(int attrId)
        {
            int slots = ActiveAttributeSlotCount();
            if ((uint)attrId >= (uint)slots)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attrId),
                    attrId,
                    $"attributeId must be in [0, {slots - 1}] for dirty flags.");
            }
        }

        private static void ValidateTagId(int tagId)
        {
            int max = ActiveMaxUsableTagId();
            if (tagId <= 0 || tagId > max)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tagId),
                    tagId,
                    $"tagId must be in [1, {max}] for dirty flags.");
            }
        }
    }
}
