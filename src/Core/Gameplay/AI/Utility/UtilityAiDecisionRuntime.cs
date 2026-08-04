using Arch.Core;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public readonly struct UtilityAiCandidate
    {
        public readonly int DecisionId;
        public readonly Entity Target;
        public readonly float Score;
        public readonly int Priority;
        public readonly int PriorityBucket;
        public readonly long DistanceSq;

        public UtilityAiCandidate(int decisionId, Entity target, float score, int priority, int priorityBucket, long distanceSq)
        {
            DecisionId = decisionId;
            Target = target;
            Score = score;
            Priority = priority;
            PriorityBucket = priorityBucket;
            DistanceSq = distanceSq;
        }
    }

    public enum UtilityAiFilterRejectReason : int
    {
        None = 0,
        MissingPosition = 1,
        Relationship = 2,
        RequiredTagMissing = 3,
        BlockedTagPresent = 4,
        Layer = 5,
        Distance = 6,
        MissingRecentAttacker = 7,
        ScratchFull = 8,
        CandidateBudgetExhausted = 9,
        ScoreGraphBudgetExhausted = 10
    }

    public enum UtilityAiTaskRunStatus : byte
    {
        None = 0,
        Running = 1,
        Complete = 2,
        Blocked = 3
    }
}
