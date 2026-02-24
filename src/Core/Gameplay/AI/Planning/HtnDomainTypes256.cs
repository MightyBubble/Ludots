using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct HtnCompoundTask
    {
        public readonly int FirstMethod;
        public readonly int MethodCount;

        public HtnCompoundTask(int firstMethod, int methodCount)
        {
            FirstMethod = firstMethod;
            MethodCount = methodCount;
        }
    }

    public readonly struct HtnMethod256
    {
        public readonly WorldStateCondition256 Condition;
        public readonly int SubtaskOffset;
        public readonly int SubtaskCount;
        public readonly int Cost;

        public HtnMethod256(in WorldStateCondition256 condition, int subtaskOffset, int subtaskCount, int cost)
        {
            Condition = condition;
            SubtaskOffset = subtaskOffset;
            SubtaskCount = subtaskCount;
            Cost = cost;
        }
    }
}

