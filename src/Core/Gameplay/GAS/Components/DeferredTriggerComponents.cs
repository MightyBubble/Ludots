using Arch.Core;

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
        public const int MAX_ATTRS = 64;
        public const int TAG_DIRTY_BYTES = 32; // 256 tags / 8
        
        public fixed byte AttributeDirty[MAX_ATTRS];
        public fixed byte TagDirty[TAG_DIRTY_BYTES];
        
        /// <summary>
        /// 标记属性为脏（需要延迟触发）
        /// </summary>
        public void MarkAttributeDirty(int attrId)
        {
            if (attrId >= 0 && attrId < MAX_ATTRS)
            {
                AttributeDirty[attrId] = 1;
            }
        }
        
        /// <summary>
        /// 标记Tag为脏（需要延迟触发）
        /// </summary>
        public void MarkTagDirty(int tagId)
        {
            if (tagId >= 0 && tagId < 256)
            {
                int byteIndex = tagId / 8;
                int bitIndex = tagId % 8;
                TagDirty[byteIndex] |= (byte)(1 << bitIndex);
            }
        }
        
        /// <summary>
        /// 检查属性是否为脏
        /// </summary>
        public bool IsAttributeDirty(int attrId)
        {
            if (attrId < 0 || attrId >= MAX_ATTRS)
            {
                return false;
            }
            return AttributeDirty[attrId] != 0;
        }
        
        /// <summary>
        /// 检查Tag是否为脏
        /// </summary>
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
        
        /// <summary>
        /// 清除所有脏标记
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < MAX_ATTRS; i++)
            {
                AttributeDirty[i] = 0;
            }
            for (int i = 0; i < TAG_DIRTY_BYTES; i++)
            {
                TagDirty[i] = 0;
            }
        }
        
        /// <summary>
        /// 清除属性脏标记
        /// </summary>
        public void ClearAttributeDirty(int attrId)
        {
            if (attrId >= 0 && attrId < MAX_ATTRS)
            {
                AttributeDirty[attrId] = 0;
            }
        }
        
        /// <summary>
        /// 清除Tag脏标记
        /// </summary>
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
