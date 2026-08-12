using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// TagRule transaction (cycle break + budget). Ensures TagRuleSet processing finishes in one transaction.
    /// </summary>
    public class TagRuleTransaction
    {
        private ProcessedOpTable _processed;
        private int _stepCount;

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
            private const int BitsLength =
                (GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace * 2) / GasLoadTimeCapacityPlan.TagBitsPerWord;

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
                if ((uint)tagId >= GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace)
                {
                    return false;
                }

                int index = (isAdd ? 0 : GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace) + tagId;
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
