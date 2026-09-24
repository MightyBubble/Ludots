using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Layers;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public sealed class UtilityAiRuntimeEvaluator
    {
        private readonly World _world;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly AbilityActivationEligibilityQuery _abilityEligibility;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly Entity[] _targets;

        public UtilityAiRuntimeEvaluator(
            World world,
            ISpatialQueryService spatialQueries,
            AbilityActivationEligibilityQuery abilityEligibility,
            IGraphRuntimeApi? graphApi,
            int targetCapacity)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            _abilityEligibility = abilityEligibility ?? throw new ArgumentNullException(nameof(abilityEligibility));
            _graphApi = graphApi;
            if (targetCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetCapacity), targetCapacity, "Target scratch capacity cannot be negative.");
            }

            _targets = targetCapacity == 0 ? Array.Empty<Entity>() : new Entity[targetCapacity];
        }

        public UtilityAiDecisionResult Evaluate(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            int profileId,
            int currentStep,
            in UtilityAiState state,
            in UtilityAiCombatMemory memory)
        {
            if ((uint)profileId >= (uint)runtime.Profiles.Length)
            {
                return new UtilityAiDecisionResult(
                    UtilityAiThinkOutcome.NoCandidate,
                    default,
                    0,
                    0,
                    0,
                    0,
                    UtilityAiFilterRejectReason.None,
                    UtilityAiReadinessBlockReason.None);
            }

            ref readonly var profile = ref runtime.Profiles[profileId];
            var graphInstructionBudget = new GraphInstructionBudget(profile.MaxGraphScoreInstructions);
            UtilityAiCandidate best = default;
            int candidateCount = 0;
            UtilityAiFilterRejectReason rejectReason = UtilityAiFilterRejectReason.None;
            UtilityAiReadinessBlockReason readinessBlockReason = UtilityAiReadinessBlockReason.None;
            bool found = false;
            int bestPriority = int.MinValue;
            int bestPriorityBucket = int.MinValue;
            float bestScore = float.MinValue;
            long bestDistanceSq = long.MaxValue;

            int dmEnd = profile.DecisionMakerOffset + profile.DecisionMakerCount;
            for (int dmIndex = profile.DecisionMakerOffset; dmIndex < dmEnd; dmIndex++)
            {
                ref readonly var maker = ref runtime.DecisionMakers[dmIndex];
                int decisionEnd = maker.DecisionOffset + maker.DecisionCount;
                for (int decisionId = maker.DecisionOffset; decisionId < decisionEnd; decisionId++)
                {
                    ref readonly var decision = ref runtime.Decisions[decisionId];
                    if (!CanSwitchToDecision(in runtime, in decision, decisionId, currentStep, in state, out var switchBlockReason))
                    {
                        if (switchBlockReason != UtilityAiReadinessBlockReason.None)
                        {
                            readinessBlockReason = switchBlockReason;
                        }

                        continue;
                    }

                    int targetCount = AcquireTargets(
                        runtime,
                        actor,
                        decision.TargetFilterId,
                        currentStep,
                        in memory,
                        _targets,
                        out WorldCmInt2 actorPos,
                        out rejectReason,
                        out bool targetScratchCapacityExhausted);
                    if (targetScratchCapacityExhausted)
                    {
                        return BuildResult(
                            UtilityAiThinkOutcome.TargetScratchCapacityExhausted,
                            default,
                            in profile,
                            candidateCount,
                            in graphInstructionBudget,
                            rejectReason,
                            readinessBlockReason);
                    }

                    if (targetCount == 0)
                    {
                        continue;
                    }

                    for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    {
                        Entity target = _targets[targetIndex];
                        if (candidateCount >= profile.MaxCandidates)
                        {
                            return BuildResult(
                                UtilityAiThinkOutcome.CandidateBudgetExhausted,
                                default,
                                in profile,
                                candidateCount,
                                in graphInstructionBudget,
                                rejectReason,
                                readinessBlockReason);
                        }

                        candidateCount++;
                        if (!PassesAllFilterOps(
                                runtime,
                                actor,
                                target,
                                actorPos,
                                decision.TargetFilterId,
                                currentStep,
                                out _,
                                out _,
                                out rejectReason))
                        {
                            continue;
                        }

                        if (!PassesDecisionReadiness(
                                actor,
                                target,
                                in decision,
                                out readinessBlockReason,
                                out int abilitySlotIndex,
                                out int effectiveAbilityId))
                        {
                            continue;
                        }

                        long distanceSq = DistanceSquared(actor, target);
                        if (!TryComputePriorityBucket(
                                runtime,
                                actor,
                                target,
                                currentStep,
                                in decision,
                                ref graphInstructionBudget,
                                out int priorityBucket) ||
                            !TryEvaluateDecision(
                                runtime,
                                actor,
                                target,
                                currentStep,
                                in decision,
                                ref graphInstructionBudget,
                                out float score))
                        {
                            return BuildResult(
                                UtilityAiThinkOutcome.GraphScoreInstructionBudgetExhausted,
                                default,
                                in profile,
                                candidateCount,
                                in graphInstructionBudget,
                                rejectReason,
                                readinessBlockReason);
                        }

                        if (decisionId == state.CurrentDecisionId && target.Equals(state.CurrentTarget))
                        {
                            score += decision.MomentumBonus;
                        }

                        if (!IsBetterCandidate(
                                maker.SelectionMode,
                                maker.SwitchMargin,
                                found,
                                decision.Priority,
                                priorityBucket,
                                score,
                                distanceSq,
                                bestPriority,
                                bestPriorityBucket,
                                bestScore,
                                bestDistanceSq))
                        {
                            continue;
                        }

                        found = true;
                        bestPriority = decision.Priority;
                        bestPriorityBucket = priorityBucket;
                        bestScore = score;
                        bestDistanceSq = distanceSq;
                        best = new UtilityAiCandidate(
                            decisionId,
                            target,
                            score,
                            decision.Priority,
                            priorityBucket,
                            distanceSq,
                            abilitySlotIndex,
                            effectiveAbilityId);
                    }
                }
            }

            return BuildResult(
                found ? UtilityAiThinkOutcome.CandidateSelected : UtilityAiThinkOutcome.NoCandidate,
                in best,
                in profile,
                candidateCount,
                in graphInstructionBudget,
                rejectReason,
                readinessBlockReason);
        }

        private static UtilityAiDecisionResult BuildResult(
            UtilityAiThinkOutcome outcome,
            in UtilityAiCandidate best,
            in UtilityAiProfileDefinition profile,
            int candidateCount,
            in GraphInstructionBudget graphInstructionBudget,
            UtilityAiFilterRejectReason rejectReason,
            UtilityAiReadinessBlockReason readinessBlockReason)
        {
            return new UtilityAiDecisionResult(
                outcome,
                in best,
                candidateCount,
                profile.MaxCandidates,
                graphInstructionBudget.Consumed,
                graphInstructionBudget.Limit,
                rejectReason,
                readinessBlockReason);
        }

        public bool TrySubmitTask(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            in UtilityAiCandidate candidate,
            int currentStep,
            OrderQueue orders,
            out int submittedOrderId,
            out int submittedOrderTypeId,
            out int submittedAbilityId,
            out OrderSubmitResult submissionResult,
            out UtilityAiTaskRunStatus taskStatus)
        {
            submittedOrderId = 0;
            submittedOrderTypeId = 0;
            submittedAbilityId = 0;
            submissionResult = OrderSubmitResult.RejectedValidation;
            taskStatus = UtilityAiTaskRunStatus.None;

            if ((uint)candidate.DecisionId >= (uint)runtime.Decisions.Length)
            {
                taskStatus = UtilityAiTaskRunStatus.Blocked;
                return false;
            }

            ref readonly var decision = ref runtime.Decisions[candidate.DecisionId];
            int taskIndex = decision.TaskIndex;
            if ((uint)taskIndex >= (uint)runtime.Tasks.Length)
            {
                throw new InvalidOperationException(
                    $"UTILITY.TASK.ERR.InvalidTaskIndex: decisionId={candidate.DecisionId}, taskIndex={taskIndex}.");
            }

            ref readonly var task = ref runtime.Tasks[taskIndex];
            switch (task.Kind)
            {
                case UtilityAiTaskKind.SubmitOrder:
                    if (!TrySubmitOrderTask(
                            in task,
                            actor,
                            in candidate,
                            currentStep,
                            orders,
                            out submittedOrderId,
                            out submittedOrderTypeId,
                            out submittedAbilityId,
                            out submissionResult))
                    {
                        taskStatus = UtilityAiTaskRunStatus.Blocked;
                        return false;
                    }

                    taskStatus = OrderSubmitResultSemantics.IsAccepted(submissionResult)
                        ? UtilityAiTaskRunStatus.Submitted
                        : UtilityAiTaskRunStatus.Failed;
                    return true;
                default:
                    throw new InvalidOperationException(
                        $"UTILITY.TASK.ERR.UnsupportedKind: taskIndex={taskIndex}, kind={(int)task.Kind}.");
            }
        }

        private bool TrySubmitOrderTask(
            in UtilityAiTaskDefinition task,
            Entity actor,
            in UtilityAiCandidate candidate,
            int currentStep,
            OrderQueue orders,
            out int submittedOrderId,
            out int submittedOrderTypeId,
            out int submittedAbilityId,
            out OrderSubmitResult submissionResult)
        {
            submittedOrderId = 0;
            submittedOrderTypeId = 0;
            submittedAbilityId = 0;
            submissionResult = OrderSubmitResult.RejectedInvalidOrderType;
            if (task.OrderTypeId <= 0)
            {
                return false;
            }

            int slotIndex = candidate.AbilitySlotIndex;
            int abilityId = candidate.EffectiveAbilityId;

            var order = new Order
            {
                Actor = actor,
                Target = candidate.Target,
                OrderTypeId = task.OrderTypeId,
                PlayerId = task.PlayerId,
                SubmitMode = (OrderSubmitMode)(byte)task.SubmitMode,
                SubmitStep = currentStep
            };

            if (slotIndex >= 0)
            {
                order.Args.I0 = slotIndex;
            }
            else if (task.IntArg0 >= 0)
            {
                order.Args.I0 = task.IntArg0;
            }

            order.Args.I1 = task.IntArg1;

            if (_world.TryGet(candidate.Target, out WorldPositionCm targetPosition))
            {
                var pos = targetPosition.Value.ToVector2();
                order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
                order.Args.Spatial.Mode = OrderCollectionMode.Single;
                order.Args.Spatial.WorldCm = new Vector3(pos.X, 0f, pos.Y);
            }

            submissionResult = orders.SubmitAssigned(ref order);
            submittedOrderId = order.OrderId;
            submittedOrderTypeId = task.OrderTypeId;
            submittedAbilityId = abilityId;
            return true;
        }

        private int AcquireTargets(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            int filterId,
            int currentStep,
            in UtilityAiCombatMemory memory,
            Entity[] scratch,
            out WorldCmInt2 actorPos,
            out UtilityAiFilterRejectReason rejectReason,
            out bool scratchCapacityExhausted)
        {
            actorPos = default;
            rejectReason = UtilityAiFilterRejectReason.None;
            scratchCapacityExhausted = false;
            if ((uint)filterId >= (uint)runtime.TargetFilters.Length)
            {
                return 0;
            }

            ref readonly var filter = ref runtime.TargetFilters[filterId];
            if (filter.MaxResults > scratch.Length)
            {
                rejectReason = UtilityAiFilterRejectReason.ScratchFull;
                scratchCapacityExhausted = true;
                return 0;
            }

            bool sourceSelf = false;
            int count = 0;
            if (!_world.TryGet(actor, out WorldPositionCm actorPosition))
            {
                rejectReason = UtilityAiFilterRejectReason.MissingPosition;
                return 0;
            }

            actorPos = actorPosition.Value.ToWorldCmInt2();
            int opEnd = filter.OpOffset + filter.OpCount;
            for (int opIndex = filter.OpOffset; opIndex < opEnd; opIndex++)
            {
                ref readonly var op = ref runtime.TargetFilterOps[opIndex];
                switch (op.Kind)
                {
                    case UtilityAiTargetFilterOpKind.SourceSelf:
                        sourceSelf = true;
                        break;
                    case UtilityAiTargetFilterOpKind.SpatialRadius:
                        SpatialQueryResult queryResult = _spatialQueries.QueryRadius(
                            actorPos,
                            op.IntA,
                            scratch.AsSpan(0, filter.MaxResults));
                        if (queryResult.Overflowed)
                        {
                            rejectReason = UtilityAiFilterRejectReason.ScratchFull;
                            scratchCapacityExhausted = true;
                            return 0;
                        }

                        count = queryResult.Count;
                        break;
                    case UtilityAiTargetFilterOpKind.RecentAttacker:
                        if (memory.LastAttacker == default ||
                            currentStep - memory.LastAttackerStep > op.IntA ||
                            !_world.IsAlive(memory.LastAttacker))
                        {
                            rejectReason = UtilityAiFilterRejectReason.MissingRecentAttacker;
                            return 0;
                        }

                        scratch[0] = memory.LastAttacker;
                        count = 1;
                        break;
                }
            }

            if (sourceSelf && count == 0)
            {
                scratch[0] = actor;
                count = 1;
            }

            if (count == 0)
            {
                return 0;
            }

            int write = 0;
            for (int i = 0; i < count; i++)
            {
                Entity target = scratch[i];
                if (target.Equals(default) || !_world.IsAlive(target))
                {
                    continue;
                }

                if (!sourceSelf && target.Equals(actor))
                {
                    continue;
                }

                scratch[write++] = target;
            }

            return write;
        }

        private bool PassesAllFilterOps(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            Entity target,
            WorldCmInt2 actorPos,
            int filterId,
            int currentStep,
            out int priorityBucket,
            out long distanceSq,
            out UtilityAiFilterRejectReason rejectReason)
        {
            priorityBucket = 0;
            distanceSq = 0;
            rejectReason = UtilityAiFilterRejectReason.None;
            WorldCmInt2 targetPos = default;
            bool hasTargetPosition = _world.TryGet(target, out WorldPositionCm targetPosition);
            if (hasTargetPosition)
            {
                targetPos = targetPosition.Value.ToWorldCmInt2();
                long dx = targetPos.X - actorPos.X;
                long dy = targetPos.Y - actorPos.Y;
                distanceSq = dx * dx + dy * dy;
            }

            ref readonly var filter = ref runtime.TargetFilters[filterId];
            int opEnd = filter.OpOffset + filter.OpCount;
            for (int opIndex = filter.OpOffset; opIndex < opEnd; opIndex++)
            {
                ref readonly var op = ref runtime.TargetFilterOps[opIndex];
                switch (op.Kind)
                {
                    case UtilityAiTargetFilterOpKind.Relationship:
                        if (!_world.TryGet(actor, out Team actorTeam) ||
                            !_world.TryGet(target, out Team targetTeam) ||
                            !RelationshipFilterUtil.Passes(op.Relationship, actorTeam.Id, targetTeam.Id))
                        {
                            rejectReason = UtilityAiFilterRejectReason.Relationship;
                            return false;
                        }
                        break;
                    case UtilityAiTargetFilterOpKind.HasAllTags:
                        if (!_world.Has<GameplayTagContainer>(target) ||
                            !_world.Get<GameplayTagContainer>(target).ContainsAll(in op.Tags))
                        {
                            rejectReason = UtilityAiFilterRejectReason.RequiredTagMissing;
                            return false;
                        }

                        priorityBucket += op.IntB;
                        break;
                    case UtilityAiTargetFilterOpKind.HasNoneTags:
                        if (_world.Has<GameplayTagContainer>(target) &&
                            _world.Get<GameplayTagContainer>(target).Intersects(in op.Tags))
                        {
                            rejectReason = UtilityAiFilterRejectReason.BlockedTagPresent;
                            return false;
                        }
                        break;
                    case UtilityAiTargetFilterOpKind.LayerAny:
                        if (!_world.TryGet(target, out EntityLayer layer) ||
                            !LayerMask.Test((uint)op.IntA, layer.Value.Category))
                        {
                            rejectReason = UtilityAiFilterRejectReason.Layer;
                            return false;
                        }
                        break;
                    case UtilityAiTargetFilterOpKind.DistanceMax:
                        if (!hasTargetPosition)
                        {
                            rejectReason = UtilityAiFilterRejectReason.MissingPosition;
                            return false;
                        }

                        long maxSq = (long)op.IntA * op.IntA;
                        if (distanceSq > maxSq)
                        {
                            rejectReason = UtilityAiFilterRejectReason.Distance;
                            return false;
                        }
                        break;
                    case UtilityAiTargetFilterOpKind.AbilityEligible:
                        if (!IsAbilityEligible(actor, target, op.IntA, out _))
                        {
                            rejectReason = UtilityAiFilterRejectReason.AbilityNotEligible;
                            return false;
                        }
                        break;
                    case UtilityAiTargetFilterOpKind.SourceSelf:
                    case UtilityAiTargetFilterOpKind.SpatialRadius:
                    case UtilityAiTargetFilterOpKind.RecentAttacker:
                    case UtilityAiTargetFilterOpKind.None:
                    default:
                        break;
                }
            }

            return true;
        }

        private bool TryEvaluateDecision(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            Entity target,
            int currentStep,
            in UtilityAiDecisionDefinition decision,
            ref GraphInstructionBudget graphInstructionBudget,
            out float score)
        {
            float multiply = decision.BaseScore;
            float weighted = 0f;
            int end = decision.ConsiderationOffset + decision.ConsiderationCount;
            for (int i = decision.ConsiderationOffset; i < end; i++)
            {
                ref readonly var consideration = ref runtime.Considerations[i];
                if (!TrySampleInput(
                        runtime,
                        actor,
                        target,
                        currentStep,
                        consideration.InputId,
                        ref graphInstructionBudget,
                        out float raw))
                {
                    score = 0f;
                    return false;
                }

                float normalized = Normalize(runtime.Normalizations[consideration.NormalizationId], raw);
                float curved = Curve(runtime.Curves[consideration.CurveId], normalized);

                switch (consideration.Aggregate)
                {
                    case UtilityAiAggregateMode.Veto:
                        if (curved <= 0f)
                        {
                            score = 0f;
                            return true;
                        }
                        break;
                    case UtilityAiAggregateMode.WeightedSum:
                    case UtilityAiAggregateMode.PriorityBucket:
                        weighted += curved * consideration.Weight;
                        break;
                    case UtilityAiAggregateMode.Multiply:
                    default:
                        multiply *= curved * consideration.Weight;
                        break;
                }
            }

            score = (multiply + weighted) * decision.Weight;
            return true;
        }

        private bool TryComputePriorityBucket(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            Entity target,
            int currentStep,
            in UtilityAiDecisionDefinition decision,
            ref GraphInstructionBudget graphInstructionBudget,
            out int bucket)
        {
            bucket = 0;
            int end = decision.ConsiderationOffset + decision.ConsiderationCount;
            for (int i = decision.ConsiderationOffset; i < end; i++)
            {
                ref readonly var consideration = ref runtime.Considerations[i];
                if (consideration.Aggregate != UtilityAiAggregateMode.PriorityBucket)
                {
                    continue;
                }

                if (!TrySampleInput(
                        runtime,
                        actor,
                        target,
                        currentStep,
                        consideration.InputId,
                        ref graphInstructionBudget,
                        out float raw))
                {
                    return false;
                }

                float normalized = Normalize(runtime.Normalizations[consideration.NormalizationId], raw);
                float curved = Curve(runtime.Curves[consideration.CurveId], normalized);
                bucket += (int)MathF.Round(curved * consideration.Weight);
            }

            return true;
        }

        private bool TrySampleInput(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            Entity target,
            int currentStep,
            int inputId,
            ref GraphInstructionBudget graphInstructionBudget,
            out float value)
        {
            if ((uint)inputId >= (uint)runtime.Inputs.Length)
            {
                value = 0f;
                return true;
            }

            ref readonly var input = ref runtime.Inputs[inputId];
            switch (input.Kind)
            {
                case UtilityAiInputKind.Constant:
                    value = input.Arg0;
                    return true;
                case UtilityAiInputKind.DistanceToTarget:
                    value = Distance(actor, target);
                    return true;
                case UtilityAiInputKind.TargetPriorityBucket:
                    value = ReadTargetPriorityBucket(target, input.Arg0);
                    return true;
                case UtilityAiInputKind.TargetHasTag:
                    value = _world.Has<GameplayTagContainer>(target) &&
                        _world.Get<GameplayTagContainer>(target).HasTag(input.Arg0)
                        ? 1f
                        : 0f;
                    return true;
                case UtilityAiInputKind.SourceHasTag:
                    value = _world.Has<GameplayTagContainer>(actor) &&
                        _world.Get<GameplayTagContainer>(actor).HasTag(input.Arg0)
                        ? 1f
                        : 0f;
                    return true;
                case UtilityAiInputKind.AbilityReady:
                    value = IsAbilityEligible(actor, target, input.Arg0, out _) ? 1f : 0f;
                    return true;
                case UtilityAiInputKind.ActuatorReadiness01:
                    value = TryReadActuatorReadiness(actor, input.Arg0, out float ready) ? ready : 0f;
                    return true;
                case UtilityAiInputKind.GraphScore:
                    return TryExecuteScoreGraph(
                        actor,
                        target,
                        in runtime.GraphScorePrograms[input.Arg0],
                        ref graphInstructionBudget,
                        out value);
                default:
                    value = 0f;
                    return true;
            }
        }

        private bool PassesDecisionReadiness(
            Entity actor,
            Entity target,
            in UtilityAiDecisionDefinition decision,
            out UtilityAiReadinessBlockReason blockReason,
            out int abilitySlotIndex,
            out int effectiveAbilityId)
        {
            blockReason = UtilityAiReadinessBlockReason.None;
            abilitySlotIndex = -1;
            effectiveAbilityId = 0;
            if (decision.AbilitySlotIndex < 0 && decision.AbilityId <= 0)
            {
                return true;
            }

            if (decision.AbilitySlotIndex < 0)
            {
                blockReason = UtilityAiReadinessBlockReason.AbilityMissing;
                return false;
            }

            abilitySlotIndex = decision.AbilitySlotIndex;
            return IsAbilitySlotEligible(
                actor,
                target,
                abilitySlotIndex,
                out blockReason,
                out effectiveAbilityId);
        }

        private bool IsAbilityEligible(
            Entity actor,
            Entity target,
            int abilityId,
            out UtilityAiReadinessBlockReason blockReason)
        {
            if (abilityId <= 0)
            {
                blockReason = UtilityAiReadinessBlockReason.AbilityMissing;
                return false;
            }

            if (!TryFindAbilitySlot(actor, abilityId, out int slotIndex))
            {
                blockReason = UtilityAiReadinessBlockReason.AbilityMissing;
                return false;
            }

            return IsAbilitySlotEligible(actor, target, slotIndex, out blockReason, out _);
        }

        private bool IsAbilitySlotEligible(
            Entity actor,
            Entity target,
            int abilitySlotIndex,
            out UtilityAiReadinessBlockReason blockReason,
            out int effectiveAbilityId)
        {
            IntVector2 targetPoint = default;
            bool hasTargetPoint = false;
            if (_world.TryGet(target, out WorldPositionCm targetPosition))
            {
                WorldCmInt2 point = targetPosition.Value.ToWorldCmInt2();
                targetPoint = new IntVector2(point.X, point.Y);
                hasTargetPoint = true;
            }

            var request = new AbilityActivationEligibilityRequest(
                actor,
                abilitySlotIndex,
                target,
                targetContext: default,
                targetPoint,
                hasTargetPoint);
            AbilityActivationEligibilityResult result = _abilityEligibility.Evaluate(in request);
            effectiveAbilityId = result.EffectiveAbilityId;
            if (result.IsEligible)
            {
                blockReason = UtilityAiReadinessBlockReason.None;
                return true;
            }

            blockReason = MapReadinessBlockReason(result.RefusalReason);
            return false;
        }

        private static UtilityAiReadinessBlockReason MapReadinessBlockReason(
            AbilityActivationRefusalReason refusalReason)
        {
            return refusalReason switch
            {
                AbilityActivationRefusalReason.ActorNotAlive or
                AbilityActivationRefusalReason.AbilityStateMissing or
                AbilityActivationRefusalReason.AbilitySlotInvalid or
                AbilityActivationRefusalReason.AbilityDefinitionMissing =>
                    UtilityAiReadinessBlockReason.AbilityMissing,
                AbilityActivationRefusalReason.RequiredActivationTagMissing or
                AbilityActivationRefusalReason.BlockedActivationTagPresent =>
                    UtilityAiReadinessBlockReason.ActivationBlockTags,
                AbilityActivationRefusalReason.ProgressionRequirementFailed =>
                    UtilityAiReadinessBlockReason.ProgressionRequirement,
                AbilityActivationRefusalReason.ActuatorNotReady =>
                    UtilityAiReadinessBlockReason.ActuatorNotReady,
                AbilityActivationRefusalReason.AimGateNotReady =>
                    UtilityAiReadinessBlockReason.AimGateNotReady,
                AbilityActivationRefusalReason.ActivationPreconditionFailed =>
                    UtilityAiReadinessBlockReason.ActivationPrecondition,
                _ => throw new InvalidOperationException(
                    $"Unsupported GAS ability activation refusal reason {refusalReason}."),
            };
        }

        private bool CanSwitchToDecision(
            in UtilityAiCompiledRuntime runtime,
            in UtilityAiDecisionDefinition decision,
            int decisionId,
            int currentStep,
            in UtilityAiState state,
            out UtilityAiReadinessBlockReason blockReason)
        {
            blockReason = UtilityAiReadinessBlockReason.None;
            if (decision.DecisionRepeatDelaySteps > 0 &&
                state.RepeatDelayDecisionId == decisionId &&
                currentStep < state.DecisionRepeatDelayUntilStep)
            {
                return false;
            }

            if ((uint)state.CurrentDecisionId < (uint)runtime.Decisions.Length)
            {
                ref readonly var current = ref runtime.Decisions[state.CurrentDecisionId];
                if (state.CurrentDecisionId == decisionId)
                {
                    return true;
                }

                if (current.MinDurationSteps > 0 &&
                    currentStep - state.DecisionStartedStep < current.MinDurationSteps)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryFindAbilitySlot(Entity actor, int abilityId, out int slotIndex)
        {
            slotIndex = -1;
            if (!_world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            return AbilitySlotResolver.TryFindAbility(_world, actor, abilityId, out slotIndex);
        }

        private bool TryReadActuatorReadiness(Entity actor, int actuatorId, out float ready)
        {
            ready = 0f;
            if (!_world.Has<ActuatorReadiness>(actor))
            {
                return false;
            }

            var readiness = _world.Get<ActuatorReadiness>(actor);
            if (readiness.ActuatorId != actuatorId)
            {
                return false;
            }

            ready = Math.Clamp(readiness.Ready01, 0f, 1f);
            return true;
        }

        private bool TryExecuteScoreGraph(
            Entity actor,
            Entity target,
            in UtilityAiGraphScoreProgramDefinition graph,
            ref GraphInstructionBudget graphInstructionBudget,
            out float score)
        {
            if (_graphApi == null)
            {
                throw new InvalidOperationException(
                    $"Utility GraphScore graph {graph.GraphId} cannot execute because IGraphRuntimeApi was not assembled.");
            }

            GraphExecutionStatus status = GasGraphExecutor.ExecutePrevalidatedScore(
                _world,
                actor,
                target,
                default,
                graph.Program,
                _graphApi,
                ref graphInstructionBudget,
                out score);
            return status == GraphExecutionStatus.Completed;
        }

        private int ReadTargetPriorityBucket(Entity target, int defaultPriority)
        {
            if (_world.Has<UtilityAiTargetPriority>(target))
            {
                return _world.Get<UtilityAiTargetPriority>(target).Bucket;
            }

            return defaultPriority;
        }

        private float Distance(Entity actor, Entity target)
        {
            long distanceSq = DistanceSquared(actor, target);
            return distanceSq <= 0 ? 0f : MathF.Sqrt(distanceSq);
        }

        private long DistanceSquared(Entity actor, Entity target)
        {
            if (!_world.TryGet(actor, out WorldPositionCm a) ||
                !_world.TryGet(target, out WorldPositionCm b))
            {
                return long.MaxValue;
            }

            WorldCmInt2 acm = a.Value.ToWorldCmInt2();
            WorldCmInt2 bcm = b.Value.ToWorldCmInt2();
            long dx = bcm.X - acm.X;
            long dy = bcm.Y - acm.Y;
            return dx * dx + dy * dy;
        }

        private static bool IsBetterCandidate(
            UtilityAiSelectionMode selectionMode,
            float switchMargin,
            bool found,
            int priority,
            int priorityBucket,
            float score,
            long distanceSq,
            int bestPriority,
            int bestPriorityBucket,
            float bestScore,
            long bestDistanceSq)
        {
            if (!found)
            {
                return true;
            }

            if (selectionMode == UtilityAiSelectionMode.FixedPriority)
            {
                if (priority != bestPriority)
                {
                    return priority > bestPriority;
                }

                if (priorityBucket != bestPriorityBucket)
                {
                    return priorityBucket > bestPriorityBucket;
                }

                if (distanceSq != bestDistanceSq)
                {
                    return distanceSq < bestDistanceSq;
                }

                return score > bestScore;
            }

            if (score > bestScore + switchMargin)
            {
                return true;
            }

            if (Math.Abs(score - bestScore) <= switchMargin)
            {
                if (priorityBucket != bestPriorityBucket)
                {
                    return priorityBucket > bestPriorityBucket;
                }

                if (distanceSq != bestDistanceSq)
                {
                    return distanceSq < bestDistanceSq;
                }
            }

            return false;
        }

        private static float Normalize(in UtilityAiNormalizationDefinition normalization, float raw)
        {
            switch (normalization.Kind)
            {
                case UtilityAiNormalizationKind.Range:
                    return Math.Clamp((raw - normalization.Min) / (normalization.Max - normalization.Min), 0f, 1f);
                case UtilityAiNormalizationKind.RangeInverse:
                    return 1f - Math.Clamp((raw - normalization.Min) / (normalization.Max - normalization.Min), 0f, 1f);
                case UtilityAiNormalizationKind.Identity:
                default:
                    return raw;
            }
        }

        private static float Curve(in UtilityAiCurveDefinition curve, float value)
        {
            switch (curve.Kind)
            {
                case UtilityAiCurveKind.Power:
                    return MathF.Pow(value, curve.Exponent);
                case UtilityAiCurveKind.Inverse:
                    return 1f - value;
                case UtilityAiCurveKind.Linear:
                default:
                    return value;
            }
        }
    }
}
