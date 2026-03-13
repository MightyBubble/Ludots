using Ludots.Core.Gameplay.GAS.Presentation;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public enum AbilityAttributeComparison : byte
    {
        GreaterOrEqual = 1,
        GreaterThan = 2,
        LessOrEqual = 3,
        LessThan = 4,
    }

    public unsafe struct AbilityAttributePreconditions
    {
        public const int MAX_ENTRIES = 4;

        public int Count;
        public fixed int AttributeIds[MAX_ENTRIES];
        public fixed float Thresholds[MAX_ENTRIES];
        public fixed byte Comparisons[MAX_ENTRIES];
        public fixed byte FailReasons[MAX_ENTRIES];

        public bool TryAdd(int attributeId, float threshold, AbilityAttributeComparison comparison, AbilityCastFailReason failReason)
        {
            if (Count >= MAX_ENTRIES || attributeId < 0)
            {
                return false;
            }

            fixed (int* attributeIds = AttributeIds)
            fixed (float* thresholds = Thresholds)
            fixed (byte* comparisons = Comparisons)
            fixed (byte* failReasons = FailReasons)
            {
                int index = Count++;
                attributeIds[index] = attributeId;
                thresholds[index] = threshold;
                comparisons[index] = (byte)comparison;
                failReasons[index] = (byte)failReason;
            }

            return true;
        }

        public int GetAttributeId(int index)
        {
            fixed (int* attributeIds = AttributeIds)
            {
                return attributeIds[index];
            }
        }

        public float GetThreshold(int index)
        {
            fixed (float* thresholds = Thresholds)
            {
                return thresholds[index];
            }
        }

        public AbilityAttributeComparison GetComparison(int index)
        {
            fixed (byte* comparisons = Comparisons)
            {
                return (AbilityAttributeComparison)comparisons[index];
            }
        }

        public AbilityCastFailReason GetFailReason(int index)
        {
            fixed (byte* failReasons = FailReasons)
            {
                return (AbilityCastFailReason)failReasons[index];
            }
        }
    }
}
