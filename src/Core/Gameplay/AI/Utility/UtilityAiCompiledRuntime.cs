using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public sealed class UtilityAiAuthoringCatalog
    {
        private readonly Dictionary<string, int> _profileIds;
        private readonly Dictionary<string, int> _stanceIds;
        private readonly Dictionary<string, int> _actuatorIds;

        public UtilityAiAuthoringCatalog(
            IReadOnlyDictionary<string, int> profileIds,
            IReadOnlyDictionary<string, int> stanceIds,
            IReadOnlyDictionary<string, int> actuatorIds)
        {
            _profileIds = profileIds != null
                ? new Dictionary<string, int>(profileIds, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
            _stanceIds = stanceIds != null
                ? new Dictionary<string, int>(stanceIds, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
            _actuatorIds = actuatorIds != null
                ? new Dictionary<string, int>(actuatorIds, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public static UtilityAiAuthoringCatalog Empty { get; } = new(
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal));

        public bool TryGetProfileId(string key, out int profileId)
        {
            if (!string.IsNullOrWhiteSpace(key) && _profileIds.TryGetValue(key, out profileId))
            {
                return true;
            }

            profileId = -1;
            return false;
        }

        public bool TryGetStanceId(string key, out int stanceId)
        {
            if (!string.IsNullOrWhiteSpace(key) && _stanceIds.TryGetValue(key, out stanceId))
            {
                return true;
            }

            stanceId = -1;
            return false;
        }

        public bool TryGetActuatorId(string key, out int actuatorId)
        {
            if (!string.IsNullOrWhiteSpace(key) && _actuatorIds.TryGetValue(key, out actuatorId))
            {
                return true;
            }

            actuatorId = -1;
            return false;
        }
    }

    public readonly struct UtilityAiCompiledRuntime
    {
        public readonly UtilityAiProfileDefinition[] Profiles;
        public readonly UtilityAiDecisionMakerDefinition[] DecisionMakers;
        public readonly UtilityAiDecisionDefinition[] Decisions;
        public readonly UtilityAiConsiderationDefinition[] Considerations;
        public readonly UtilityAiTargetFilterDefinition[] TargetFilters;
        public readonly UtilityAiTargetFilterOpDefinition[] TargetFilterOps;
        public readonly UtilityAiInputDefinition[] Inputs;
        public readonly UtilityAiNormalizationDefinition[] Normalizations;
        public readonly UtilityAiCurveDefinition[] Curves;
        public readonly UtilityAiTaskDefinition[] Tasks;
        public readonly UtilityAiStanceDefinition[] Stances;
        public readonly UtilityAiActuatorDefinition[] Actuators;
        public readonly UtilityAiAuthoringCatalog Authoring;

        public UtilityAiCompiledRuntime(
            UtilityAiProfileDefinition[] profiles,
            UtilityAiDecisionMakerDefinition[] decisionMakers,
            UtilityAiDecisionDefinition[] decisions,
            UtilityAiConsiderationDefinition[] considerations,
            UtilityAiTargetFilterDefinition[] targetFilters,
            UtilityAiTargetFilterOpDefinition[] targetFilterOps,
            UtilityAiInputDefinition[] inputs,
            UtilityAiNormalizationDefinition[] normalizations,
            UtilityAiCurveDefinition[] curves,
            UtilityAiTaskDefinition[] tasks,
            UtilityAiStanceDefinition[] stances,
            UtilityAiActuatorDefinition[] actuators,
            UtilityAiAuthoringCatalog authoring = null)
        {
            Profiles = profiles;
            DecisionMakers = decisionMakers;
            Decisions = decisions;
            Considerations = considerations;
            TargetFilters = targetFilters;
            TargetFilterOps = targetFilterOps;
            Inputs = inputs;
            Normalizations = normalizations;
            Curves = curves;
            Tasks = tasks;
            Stances = stances;
            Actuators = actuators;
            Authoring = authoring ?? UtilityAiAuthoringCatalog.Empty;
        }

        public static UtilityAiCompiledRuntime Empty => new(
            System.Array.Empty<UtilityAiProfileDefinition>(),
            System.Array.Empty<UtilityAiDecisionMakerDefinition>(),
            System.Array.Empty<UtilityAiDecisionDefinition>(),
            System.Array.Empty<UtilityAiConsiderationDefinition>(),
            System.Array.Empty<UtilityAiTargetFilterDefinition>(),
            System.Array.Empty<UtilityAiTargetFilterOpDefinition>(),
            System.Array.Empty<UtilityAiInputDefinition>(),
            System.Array.Empty<UtilityAiNormalizationDefinition>(),
            System.Array.Empty<UtilityAiCurveDefinition>(),
            System.Array.Empty<UtilityAiTaskDefinition>(),
            System.Array.Empty<UtilityAiStanceDefinition>(),
            System.Array.Empty<UtilityAiActuatorDefinition>());

        public readonly bool IsEnabled => Profiles.Length > 0;
    }

    public readonly struct UtilityAiProfileDefinition
    {
        public readonly int DecisionMakerOffset;
        public readonly int DecisionMakerCount;
        public readonly int DecisionIntervalSteps;
        public readonly int MaxCandidates;
        public readonly int DefaultStanceId;

        public UtilityAiProfileDefinition(
            int decisionMakerOffset,
            int decisionMakerCount,
            int decisionIntervalSteps,
            int maxCandidates,
            int defaultStanceId)
        {
            DecisionMakerOffset = decisionMakerOffset;
            DecisionMakerCount = decisionMakerCount;
            DecisionIntervalSteps = decisionIntervalSteps;
            MaxCandidates = maxCandidates;
            DefaultStanceId = defaultStanceId;
        }
    }

    public readonly struct UtilityAiDecisionMakerDefinition
    {
        public readonly int DecisionOffset;
        public readonly int DecisionCount;
        public readonly UtilityAiSelectionMode SelectionMode;
        public readonly float SwitchMargin;

        public UtilityAiDecisionMakerDefinition(
            int decisionOffset,
            int decisionCount,
            UtilityAiSelectionMode selectionMode,
            float switchMargin)
        {
            DecisionOffset = decisionOffset;
            DecisionCount = decisionCount;
            SelectionMode = selectionMode;
            SwitchMargin = switchMargin;
        }
    }

    public readonly struct UtilityAiDecisionDefinition
    {
        public readonly int TargetFilterId;
        public readonly int ConsiderationOffset;
        public readonly int ConsiderationCount;
        public readonly int TaskOffset;
        public readonly int TaskCount;
        public readonly int Priority;
        public readonly float BaseScore;
        public readonly float Weight;
        public readonly float MomentumBonus;
        public readonly int MinDurationSteps;
        public readonly int CooldownSteps;
        public readonly int AutocastAbilityId;
        public readonly int AbilitySlotIndex;
        public readonly int SharedCooldownTagId;
        public readonly UtilityAiDecisionFlags Flags;

        public UtilityAiDecisionDefinition(
            int targetFilterId,
            int considerationOffset,
            int considerationCount,
            int taskOffset,
            int taskCount,
            int priority,
            float baseScore,
            float weight,
            float momentumBonus,
            int minDurationSteps,
            int cooldownSteps,
            int autocastAbilityId,
            int abilitySlotIndex,
            int sharedCooldownTagId,
            UtilityAiDecisionFlags flags)
        {
            TargetFilterId = targetFilterId;
            ConsiderationOffset = considerationOffset;
            ConsiderationCount = considerationCount;
            TaskOffset = taskOffset;
            TaskCount = taskCount;
            Priority = priority;
            BaseScore = baseScore;
            Weight = weight;
            MomentumBonus = momentumBonus;
            MinDurationSteps = minDurationSteps;
            CooldownSteps = cooldownSteps;
            AutocastAbilityId = autocastAbilityId;
            AbilitySlotIndex = abilitySlotIndex;
            SharedCooldownTagId = sharedCooldownTagId;
            Flags = flags;
        }
    }

    public readonly struct UtilityAiConsiderationDefinition
    {
        public readonly int InputId;
        public readonly int NormalizationId;
        public readonly int CurveId;
        public readonly float Weight;
        public readonly UtilityAiAggregateMode Aggregate;

        public UtilityAiConsiderationDefinition(
            int inputId,
            int normalizationId,
            int curveId,
            float weight,
            UtilityAiAggregateMode aggregate)
        {
            InputId = inputId;
            NormalizationId = normalizationId;
            CurveId = curveId;
            Weight = weight;
            Aggregate = aggregate;
        }
    }

    public readonly struct UtilityAiTargetFilterDefinition
    {
        public readonly int OpOffset;
        public readonly int OpCount;
        public readonly int MaxResults;

        public UtilityAiTargetFilterDefinition(int opOffset, int opCount, int maxResults)
        {
            OpOffset = opOffset;
            OpCount = opCount;
            MaxResults = maxResults;
        }
    }

    public readonly struct UtilityAiTargetFilterOpDefinition
    {
        public readonly UtilityAiTargetFilterOpKind Kind;
        public readonly int IntA;
        public readonly int IntB;
        public readonly RelationshipFilter Relationship;
        public readonly GameplayTagContainer Tags;

        public UtilityAiTargetFilterOpDefinition(
            UtilityAiTargetFilterOpKind kind,
            int intA,
            int intB,
            RelationshipFilter relationship,
            in GameplayTagContainer tags)
        {
            Kind = kind;
            IntA = intA;
            IntB = intB;
            Relationship = relationship;
            Tags = tags;
        }
    }

    public readonly struct UtilityAiInputDefinition
    {
        public readonly UtilityAiInputKind Kind;
        public readonly int Arg0;
        public readonly int GraphId;

        public UtilityAiInputDefinition(UtilityAiInputKind kind, int arg0, int graphId)
        {
            Kind = kind;
            Arg0 = arg0;
            GraphId = graphId;
        }
    }

    public readonly struct UtilityAiNormalizationDefinition
    {
        public readonly UtilityAiNormalizationKind Kind;
        public readonly float Min;
        public readonly float Max;

        public UtilityAiNormalizationDefinition(UtilityAiNormalizationKind kind, float min, float max)
        {
            Kind = kind;
            Min = min;
            Max = max;
        }
    }

    public readonly struct UtilityAiCurveDefinition
    {
        public readonly UtilityAiCurveKind Kind;
        public readonly float Exponent;

        public UtilityAiCurveDefinition(UtilityAiCurveKind kind, float exponent)
        {
            Kind = kind;
            Exponent = exponent;
        }
    }

    public readonly struct UtilityAiTaskDefinition
    {
        public readonly UtilityAiTaskKind Kind;
        public readonly int OrderTypeId;
        public readonly int AbilityId;
        public readonly int AbilitySlotIndex;
        public readonly int SubmitMode;
        public readonly int PlayerId;
        public readonly int IntArg0;
        public readonly int IntArg1;

        public UtilityAiTaskDefinition(
            UtilityAiTaskKind kind,
            int orderTypeId,
            int abilityId,
            int abilitySlotIndex,
            int submitMode,
            int playerId,
            int intArg0,
            int intArg1)
        {
            Kind = kind;
            OrderTypeId = orderTypeId;
            AbilityId = abilityId;
            AbilitySlotIndex = abilitySlotIndex;
            SubmitMode = submitMode;
            PlayerId = playerId;
            IntArg0 = intArg0;
            IntArg1 = intArg1;
        }
    }

    public readonly struct UtilityAiStanceDefinition
    {
        public readonly int Id;
        public readonly bool AutoAcquire;
        public readonly bool Retaliate;
        public readonly bool AllowMoveChase;
        public readonly int TargetFilterId;

        public UtilityAiStanceDefinition(
            int id,
            bool autoAcquire,
            bool retaliate,
            bool allowMoveChase,
            int targetFilterId)
        {
            Id = id;
            AutoAcquire = autoAcquire;
            Retaliate = retaliate;
            AllowMoveChase = allowMoveChase;
            TargetFilterId = targetFilterId;
        }
    }

    public readonly struct UtilityAiActuatorDefinition
    {
        public readonly int Id;
        public readonly int AbilityId;
        public readonly int ReadinessInputId;
        public readonly int AimGateInputId;

        public UtilityAiActuatorDefinition(int id, int abilityId, int readinessInputId, int aimGateInputId)
        {
            Id = id;
            AbilityId = abilityId;
            ReadinessInputId = readinessInputId;
            AimGateInputId = aimGateInputId;
        }
    }

    public enum UtilityAiSelectionMode : byte
    {
        UtilityScore = 0,
        FixedPriority = 1
    }

    public enum UtilityAiAggregateMode : byte
    {
        Multiply = 0,
        WeightedSum = 1,
        Veto = 2,
        PriorityBucket = 3
    }

    public enum UtilityAiTargetFilterOpKind : byte
    {
        None = 0,
        SourceSelf = 1,
        SpatialRadius = 2,
        Relationship = 3,
        HasAllTags = 4,
        HasNoneTags = 5,
        LayerAny = 6,
        DistanceMax = 7,
        AbilityEligible = 8,
        RecentAttacker = 9
    }

    public enum UtilityAiInputKind : byte
    {
        Constant = 0,
        DistanceToTarget = 1,
        TargetPriorityBucket = 2,
        ActuatorReadiness01 = 3,
        GraphScore = 4,
        TargetHasTag = 5,
        SourceHasTag = 6,
        AbilityReady = 7
    }

    public enum UtilityAiNormalizationKind : byte
    {
        Identity = 0,
        Range = 1,
        RangeInverse = 2
    }

    public enum UtilityAiCurveKind : byte
    {
        Linear = 0,
        Power = 1,
        Inverse = 2
    }

    public enum UtilityAiTaskKind : byte
    {
        SubmitOrder = 0,
        Sequence = 1,
        Parallel = 2,
        ParallelComplete = 3
    }

    [System.Flags]
    public enum UtilityAiDecisionFlags : ushort
    {
        None = 0,
        Autocast = 1 << 0,
        OrdinaryAttack = 1 << 1,
        RequiresTarget = 1 << 2,
        KeepRunningUntilFinished = 1 << 3,
        ExplicitOrderOnly = 1 << 4
    }
}
