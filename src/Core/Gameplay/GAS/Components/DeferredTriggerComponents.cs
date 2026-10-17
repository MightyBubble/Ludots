using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;

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
    /// </summary>
    public unsafe struct DirtyFlags
    {
        public const int MAX_ATTRS = AttributeRegistry.MaxAttributes;
        public const int TAG_DIRTY_BYTES = 32; // 256 tags / 8
        
        public ulong AttributeDirtyMask;
        public byte DeferredTriggerQueued;
        public fixed byte TagDirty[TAG_DIRTY_BYTES];
        
        public void MarkAttributeDirty(int attrId)
        {
            if (attrId >= 0 && attrId < MAX_ATTRS)
            {
                AttributeDirtyMask |= 1UL << attrId;
            }
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
            if (attrId < 0 || attrId >= MAX_ATTRS)
            {
                return false;
            }
            return ((AttributeDirtyMask >> attrId) & 1UL) != 0;
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
            AttributeDirtyMask = 0UL;
            for (int i = 0; i < TAG_DIRTY_BYTES; i++)
            {
                TagDirty[i] = 0;
            }
        }

        public bool IsAnyAttributeDirty()
        {
            return AttributeDirtyMask != 0UL;
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
            if (attrId >= 0 && attrId < MAX_ATTRS)
            {
                AttributeDirtyMask &= ~(1UL << attrId);
            }
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
    }
}
