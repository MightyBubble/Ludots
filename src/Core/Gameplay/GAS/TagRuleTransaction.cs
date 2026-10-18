namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// TagRule事务（用于循环阻断和预算控制）
    /// 确保TagRuleSet规则处理在同一事务内完成，防止递归爆炸
    /// </summary>
    public class TagRuleTransaction
    {
        private ProcessedOpTable _processed;
        
        private int _stepCount = 0;
        
        public bool IsActive { get; private set; }
        
        public void Begin()
        {
            IsActive = true;
            _processed.Clear();
            _stepCount = 0;
        }
        
        public void End()
        {
            IsActive = false;
            _processed.Clear();
            _stepCount = 0;
        }
        
        /// <summary>
        /// 尝试标记Tag操作已处理（用于循环阻断）
        /// </summary>
        /// <returns>true表示可以处理，false表示已处理或预算超限</returns>
        public bool TryMarkProcessed(int tagId, bool isAdd)
        {
            if (!IsActive)
            {
                return false;
            }
            
            if (_stepCount >= GasConstants.MAX_TAG_RULE_TRANSACTION_STEPS)
            {
                return false;
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
        
        public int StepCount => _stepCount;
        
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
