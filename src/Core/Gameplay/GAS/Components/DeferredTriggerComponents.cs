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
    /// 脏标记组件（用于标记需要延迟触发）
    /// Attribute dirty words are absolute-max fixed (1024/64); live ids fail-closed against the plan.
    /// </summary>
    public unsafe struct DirtyFlags
    {
        public const int MAX_ATTR_DIRTY_WORDS =
            GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots / GasLoadTimeCapacityPlan.TagBitsPerWord;

        /// <summary>Obsolete bridge for the old single-ulong 64-slot mask.</summary>
        public const int MAX_ATTRS = 64;
        public const int TAG_DIRTY_BYTES = 32; // 256 tags / 8

        public fixed ulong AttributeDirtyWords[MAX_ATTR_DIRTY_WORDS];
        public byte DeferredTriggerQueued;
        public fixed byte TagDirty[TAG_DIRTY_BYTES];

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
            if (tagId >= 0 && tagId < 256)
            {
                int byteIndex = tagId / 8;
                int bitIndex = tagId % 8;
                TagDirty[byteIndex] |= (byte)(1 << bitIndex);
            }
        }

        public bool IsAttributeDirty(int attrId)
        {
            ValidateAttributeId(attrId);
            return (AttributeDirtyWords[attrId >> 6] & (1UL << (attrId & 63))) != 0UL;
        }

        public bool IsTagDirty(int tagId)
        {
            if (tagId < 0 || tagId >= 256)
            {
                return false;
            }
            int byteIndex = tagId / 8;
            int bitIndex = tagId % 8;
            return (TagDirty[byteIndex] & (1 << bitIndex)) != 0;
        }

        public void Clear()
        {
            for (int i = 0; i < MAX_ATTR_DIRTY_WORDS; i++)
            {
                AttributeDirtyWords[i] = 0UL;
            }

            for (int i = 0; i < TAG_DIRTY_BYTES; i++)
            {
                TagDirty[i] = 0;
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
            for (int i = 0; i < TAG_DIRTY_BYTES; i++)
            {
                if (TagDirty[i] != 0) return true;
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
            if (tagId >= 0 && tagId < 256)
            {
                int byteIndex = tagId / 8;
                int bitIndex = tagId % 8;
                TagDirty[byteIndex] &= (byte)~(1 << bitIndex);
            }
        }

        private static int ActiveAttributeSlotCount()
        {
            return GasLoadTimeCapacitySession.IsFrozen
                ? GasLoadTimeCapacitySession.Plan.AttributeSlotCount
                : GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots;
        }

        private static int ActiveAttributeDirtyWordCount()
        {
            return GasWorldColumnStore.AttrDirtyWordCount(ActiveAttributeSlotCount());
        }

        private static void ValidateAttributeId(int attrId)
        {
            int slots = ActiveAttributeSlotCount();
            if ((uint)attrId >= (uint)slots)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attrId),
                    attrId,
                    $"attributeId must be in [0, {slots - 1}] for dirty flags.");
            }
        }
    }
}
