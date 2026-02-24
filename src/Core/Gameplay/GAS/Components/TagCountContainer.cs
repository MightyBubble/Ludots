using System;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Tag层数容器（支持TagCount查询）
    /// 用于管理Tag的层数（堆叠次数）。
    /// tagId &lt;= 0 抛 <see cref="ArgumentOutOfRangeException"/>。
    /// CAPACITY 溢出返回 false（调用方应上报至 GasBudget）。
    /// </summary>
    public unsafe struct TagCountContainer
    {
        public const int CAPACITY = 16;
        
        public fixed int TagIds[CAPACITY];
        public fixed ushort Counts[CAPACITY];
        public int Count;
        
        /// <summary>
        /// 增加Tag层数。返回 false 表示 CAPACITY 溢出（新 tagId 无法存储）。
        /// </summary>
        public bool AddCount(int tagId, ushort amount = 1)
        {
            if (tagId <= 0) throw new ArgumentOutOfRangeException(nameof(tagId), tagId, "tagId must be > 0.");
            
            // 查找现有条目
            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] == tagId)
                {
                    // 累加层数，Clamp到[0, 65535]（见裁决AE）
                    int newCount = Counts[i] + amount;
                    Counts[i] = (ushort)Math.Min(65535, Math.Max(0, newCount));
                    return true;
                }
            }
            
            // 添加新条目
            if (Count >= CAPACITY) return false;

            TagIds[Count] = tagId;
            Counts[Count] = amount;
            Count++;
            return true;
        }
        
        /// <summary>
        /// 减少Tag层数
        /// </summary>
        public void RemoveCount(int tagId, ushort amount = 1)
        {
            if (tagId <= 0) throw new ArgumentOutOfRangeException(nameof(tagId), tagId, "tagId must be > 0.");
            
            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] == tagId)
                {
                    if (Counts[i] <= amount)
                    {
                        // 移除条目（移动最后一个到当前位置）
                        TagIds[i] = TagIds[Count - 1];
                        Counts[i] = Counts[Count - 1];
                        Count--;
                    }
                    else
                    {
                        // 减少层数，Clamp到[0, 65535]
                        int newCount = Counts[i] - amount;
                        Counts[i] = (ushort)Math.Max(0, newCount);
                    }
                    return;
                }
            }
        }
        
        /// <summary>
        /// 获取Tag层数
        /// </summary>
        public ushort GetCount(int tagId)
        {
            if (tagId <= 0) throw new ArgumentOutOfRangeException(nameof(tagId), tagId, "tagId must be > 0.");
            
            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] == tagId)
                {
                    return Counts[i];
                }
            }
            return 0;
        }
        
        /// <summary>
        /// 检查Tag是否有层数记录
        /// </summary>
        public bool HasCount(int tagId)
        {
            return GetCount(tagId) > 0;
        }
        
        /// <summary>
        /// 清空所有层数记录
        /// </summary>
        public void Clear()
        {
            Count = 0;
        }
    }
}
