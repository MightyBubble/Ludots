using Arch.Core;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public enum UtilityAiThinkOutcome : byte
    {
        NotEvaluated = 0,
        CandidateSelected = 1,
        NoCandidate = 2,
        CandidateBudgetExhausted = 3,
        GraphScoreInstructionBudgetExhausted = 4,
        TargetScratchCapacityExhausted = 5
    }

    public readonly struct UtilityAiDecisionResult
    {
        public readonly UtilityAiThinkOutcome Outcome;
        public readonly UtilityAiCandidate Best;
        public readonly int CandidateCount;
        public readonly int CandidateLimit;
        public readonly int GraphScoreInstructionCount;
        public readonly int GraphScoreInstructionLimit;
        public readonly UtilityAiFilterRejectReason FilterRejectReason;
        public readonly UtilityAiReadinessBlockReason ReadinessBlockReason;

        public UtilityAiDecisionResult(
            UtilityAiThinkOutcome outcome,
            in UtilityAiCandidate best,
            int candidateCount,
            int candidateLimit,
            int graphScoreInstructionCount,
            int graphScoreInstructionLimit,
            UtilityAiFilterRejectReason filterRejectReason,
            UtilityAiReadinessBlockReason readinessBlockReason)
        {
            Outcome = outcome;
            Best = best;
            CandidateCount = candidateCount;
            CandidateLimit = candidateLimit;
            GraphScoreInstructionCount = graphScoreInstructionCount;
            GraphScoreInstructionLimit = graphScoreInstructionLimit;
            FilterRejectReason = filterRejectReason;
            ReadinessBlockReason = readinessBlockReason;
        }

        public bool HasCompleteCandidate => Outcome == UtilityAiThinkOutcome.CandidateSelected;
        public bool IsExhausted =>
            Outcome == UtilityAiThinkOutcome.CandidateBudgetExhausted ||
            Outcome == UtilityAiThinkOutcome.GraphScoreInstructionBudgetExhausted ||
            Outcome == UtilityAiThinkOutcome.TargetScratchCapacityExhausted;
    }

    public readonly struct UtilityAiCandidate
    {
        public readonly int DecisionId;
        public readonly Entity Target;
        public readonly float Score;
        public readonly int Priority;
        public readonly int PriorityBucket;
        public readonly long DistanceSq;
        public readonly int AbilitySlotIndex;
        public readonly int EffectiveAbilityId;

        public UtilityAiCandidate(
            int decisionId,
            Entity target,
            float score,
            int priority,
            int priorityBucket,
            long distanceSq,
            int abilitySlotIndex,
            int effectiveAbilityId)
        {
            DecisionId = decisionId;
            Target = target;
            Score = score;
            Priority = priority;
            PriorityBucket = priorityBucket;
            DistanceSq = distanceSq;
            AbilitySlotIndex = abilitySlotIndex;
            EffectiveAbilityId = effectiveAbilityId;
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
        AbilityNotEligible = 7,
        MissingRecentAttacker = 8,
        ScratchFull = 9
    }

    public enum UtilityAiReadinessBlockReason : int
    {
        None = 0,
        AbilityMissing = 1,
        ActivationBlockTags = 2,
        ActivationPrecondition = 3,
        ActuatorNotReady = 4,
        AimGateNotReady = 5,
        ProgressionRequirement = 6
    }

    public enum UtilityAiTaskRunStatus : byte
    {
        None = 0,
        Submitted = 1,
        Pending = 2,
        Admitted = 3,
        Completed = 4,
        Failed = 5,
        Cancelled = 6,
        Blocked = 7
    }
}
