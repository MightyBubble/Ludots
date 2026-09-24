using Arch.Core;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.AI.Components
{
    public struct UtilityAiAgent
    {
        public int ProfileId;
    }

    public struct UtilityAiState
    {
        public int CurrentDecisionId;
        public Entity CurrentTarget;
        public float CurrentScore;
        public int LastSwitchStep;
        public int NextThinkStep;
        public int LastSubmittedOrderId;
        public UtilityAiTaskRunStatus CurrentTaskStatus;
        public OrderFailureReason CurrentTaskFailureReason;
        public int DecisionStartedStep;
        public int RepeatDelayDecisionId;
        public int DecisionRepeatDelayUntilStep;
    }

    public struct UtilityAiDecisionTrace
    {
        public int ThinkOutcome;
        public int CandidateCount;
        public int CandidateLimit;
        public int GraphScoreInstructionCount;
        public int GraphScoreInstructionLimit;
        public int BestDecisionId;
        public Entity BestTarget;
        public float BestScore;
        public int BestPriorityBucket;
        public long BestDistanceSq;
        public int LastFilterRejectReason;
        public int LastReadinessBlockReason;
        public int LastSubmittedOrderId;
        public int LastSubmittedOrderTypeId;
        public int LastSubmittedAbilityId;
        public int LastTaskStatus;
        public int LastTaskFailureReason;
    }

    public struct UtilityAiCombatMemory
    {
        public Entity LastAttacker;
        public int LastAttackerStep;
        public Entity LastSeenTarget;
        public int LastSeenStep;
    }

    public struct UtilityAiTargetPriority
    {
        public int Bucket;
    }

    public enum UtilityAiTargetPriorityBucket
    {
        None = 0,
        Low = 1,
        Normal = 2,
        High = 3,
        Critical = 4
    }

    public struct UtilityAiStanceState
    {
        public int StanceId;
    }

    public struct ActuatorReadiness
    {
        public int ActuatorId;
        public float Ready01;
        public int BlockReason;
        public int EtaSteps;
        public byte RequiresPreparation;
    }

    public struct AimGate
    {
        public int ActuatorId;
        public float Ready01;
        public int BlockReason;
    }
}
