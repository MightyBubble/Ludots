namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// TagRule事务（用于循环阻断和预算控制）
    /// 确保TagRuleSet规则处理在同一事务内完成，防止递归爆炸
    /// </summary>
    public class TagRuleTransaction
    {
        private ProcessedOpTable _processed;
        
        // 事务步数计数器
        private int _stepCount = 0;
        
        /// <summary>
        /// 事务是否激活
        /// </summary>
        public bool IsActive { get; private set; }
        
        /// <summary>
        /// 开始事务
        /// </summary>
        public void Begin()
        {
            IsActive = true;
            _processed.Clear();
            _stepCount = 0;
        }
        
        /// <summary>
        /// 结束事务
        /// </summary>
        public void End()
        {
            IsActive = false;
            _processed.Clear();
            _stepCount = 0;
        }
        
        /// <summary>
        /// 尝试标记Tag操作已处理（用于循环阻断）
        /// </summary>
        /// <param name="tagId">Tag ID</param>
        /// <param name="isAdd">是否为Add操作（false表示Remove）</param>
        /// <returns>true表示可以处理，false表示已处理或预算超限</returns>
        public bool TryMarkProcessed(int tagId, bool isAdd)
        {
            if (!IsActive)
            {
                return false;
            }
            
            // 检查预算限制
            if (_stepCount >= GasConstants.MAX_TAG_RULE_TRANSACTION_STEPS)
            {
                return false; // 预算超限
            }
            
            if (_processed.Count >= GasConstants.MAX_PROCESSED_SET_CAPACITY)
            {
                return false;
            }

            if (!_processed.TryMark(tagId, isAdd))
            {
                return false;
            }

            _stepCount++;
            return true;
        }
        
        /// <summary>
        /// 获取当前事务步数
        /// </summary>
        public int StepCount => _stepCount;
        
        /// <summary>
        /// 获取ProcessedSet大小
        /// </summary>
        public int ProcessedSetSize => _processed.Count;

        private unsafe struct ProcessedOpTable
        {
            private const int BitsLength = 8;
            public fixed ulong Bits[BitsLength];
            public int Count;

            public void Clear()
            {
                for (int i = 0; i < BitsLength; i++)
                {
                    Bits[i] = 0;
                }
                Count = 0;
            }

            public bool TryMark(int tagId, bool isAdd)
            {
                if ((uint)tagId >= 256u)
                {
                    return false;
                }

                int index = (isAdd ? 0 : 256) + tagId;
                int word = index >> 6;
                int bit = index & 63;
                ulong mask = 1UL << bit;

                if ((Bits[word] & mask) != 0)
                {
                    return false;
                }

                Bits[word] |= mask;
                Count++;
                return true;
            }
        }
    }
}
