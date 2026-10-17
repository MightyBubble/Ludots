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
            
            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] == tagId)
                {
                    int newCount = Counts[i] + amount;
                    Counts[i] = (ushort)Math.Min(65535, Math.Max(0, newCount));
                    return true;
                }
            }
            
            if (Count >= CAPACITY) return false;

            TagIds[Count] = tagId;
            Counts[Count] = amount;
            Count++;
            return true;
        }
        
        public void RemoveCount(int tagId, ushort amount = 1)
        {
            if (tagId <= 0) throw new ArgumentOutOfRangeException(nameof(tagId), tagId, "tagId must be > 0.");
            
            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] == tagId)
                {
                    if (Counts[i] <= amount)
                    {
                        TagIds[i] = TagIds[Count - 1];
                        Counts[i] = Counts[Count - 1];
                        Count--;
                    }
                    else
                    {
                        int newCount = Counts[i] - amount;
                        Counts[i] = (ushort)Math.Max(0, newCount);
                    }
                    return;
                }
            }
        }
        
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

        public int CopyTagIds(Span<int> destination)
        {
            int written = 0;
            for (int i = 0; i < Count && written < destination.Length; i++)
            {
                int tagId = TagIds[i];
                if (tagId > 0)
                {
                    destination[written++] = tagId;
                }
            }

            return written;
        }
        
        public bool HasCount(int tagId)
        {
            return GetCount(tagId) > 0;
        }
        
        public void Clear()
        {
            Count = 0;
        }
    }
}
