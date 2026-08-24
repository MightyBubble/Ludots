using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Scoring;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Layers;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.AI.Utility
{
    public sealed class UtilityAiRuntimeEvaluator
    {
        private readonly World _world;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly GraphProgramRegistry? _graphs;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly Entity[] _targets;

        public UtilityAiRuntimeEvaluator(
            World world,
            ISpatialQueryService spatialQueries,
            GraphProgramRegistry? graphs,
            IGraphRuntimeApi? graphApi,
            int targetCapacity = 256)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            _graphs = graphs;
            _graphApi = graphApi;
            _targets = new Entity[targetCapacity < 16 ? 16 : targetCapacity];
        }

        public bool TryEvaluate(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            int profileId,
            int currentStep,
            in UtilityAiState state,
            in UtilityAiCombatMemory memory,
            out UtilityAiCandidate best,
            out int candidateCount,
            out UtilityAiFilterRejectReason rejectReason)
        {
            best = default;
            candidateCount = 0;
            rejectReason = UtilityAiFilterRejectReason.None;

            if ((uint)profileId >= (uint)runtime.Profiles.Length)
            {
                return false;
            }

            ref readonly var profile = ref runtime.Profiles[profileId];
            var scoreBudget = GraphScoreEvaluationBudget.Create(profile.MaxCandidates);
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
                    if (!CanSwitchToDecision(in runtime, in decision, decisionId, currentStep, in state))
                    {
                        continue;
                    }

                    int targetCount = AcquireTargets(
                        runtime,
                        actor,
                        decision.TargetFilterId,
                        currentStep,
                        in memory,
                        _targets,
                        out rejectReason);
                    if (targetCount == 0)
                    {
                        continue;
                    }

                    for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    {
                        Entity target = _targets[targetIndex];
                        if (candidateCount >= profile.MaxCandidates)
                        {
                            rejectReason = UtilityAiFilterRejectReason.CandidateBudgetExhausted;
                            best = default;
                            return false;
                        }

                        candidateCount++;
                        long distanceSq = DistanceSquared(actor, target);
                        if (!TryComputePriorityBucket(
                                runtime,
                                actor,
                                target,
                                currentStep,
                                in decision,
                                ref scoreBudget,
                                out int priorityBucket,
                                out rejectReason))
                        {
                            best = default;
                            return false;
                        }

                        if (!TryEvaluateDecision(
                                runtime,
                                actor,
                                target,
                                currentStep,
                                in decision,
                                ref scoreBudget,
                                out float score,
                                out rejectReason))
                        {
                            best = default;
                            return false;
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
                        best = new UtilityAiCandidate(decisionId, target, score, decision.Priority, priorityBucket, distanceSq);
                    }
                }
            }

            return found;
        }

        public bool TrySubmitTasks(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            in UtilityAiCandidate candidate,
            int currentStep,
            OrderQueue orders,
            OrderTerminalResultBuffer terminalResults,
            out int submittedOrderTypeId,
            out int submittedOrderId,
            out UtilityAiTaskKind taskKind,
            out UtilityAiTaskRunStatus taskStatus)
        {
            submittedOrderTypeId = 0;
            submittedOrderId = 0;
            taskKind = UtilityAiTaskKind.SubmitOrder;
            taskStatus = UtilityAiTaskRunStatus.None;

            if ((uint)candidate.DecisionId >= (uint)runtime.Decisions.Length)
            {
                taskStatus = UtilityAiTaskRunStatus.Blocked;
                return false;
            }

            ref readonly var decision = ref runtime.Decisions[candidate.DecisionId];
            bool submittedAny = false;
            bool requiredAny = false;
            int end = decision.TaskOffset + decision.TaskCount;
            for (int taskIndex = decision.TaskOffset; taskIndex < end; taskIndex++)
            {
                ref readonly var task = ref runtime.Tasks[taskIndex];
                taskKind = task.Kind;
                switch (task.Kind)
                {
                    case UtilityAiTaskKind.Sequence:
                        continue;
                    case UtilityAiTaskKind.Parallel:
                    case UtilityAiTaskKind.ParallelComplete:
                        requiredAny = true;
                        continue;
                    case UtilityAiTaskKind.SubmitOrder:
                    default:
                        requiredAny = true;
                        if (!TrySubmitOrderTask(in task, actor, in candidate, currentStep, orders, terminalResults, out submittedOrderTypeId, out submittedOrderId))
                        {
                            taskStatus = submittedAny ? UtilityAiTaskRunStatus.Running : UtilityAiTaskRunStatus.Blocked;
                            return submittedAny;
                        }

                        submittedAny = true;
                        if (task.Kind == UtilityAiTaskKind.SubmitOrder)
                        {
                            taskStatus = UtilityAiTaskRunStatus.Running;
                            return true;
                        }
                        break;
                }
            }

            taskStatus = submittedAny
                ? UtilityAiTaskRunStatus.Running
                : requiredAny ? UtilityAiTaskRunStatus.Blocked : UtilityAiTaskRunStatus.None;
            return submittedAny;
        }

        private bool TrySubmitOrderTask(
            in UtilityAiTaskDefinition task,
            Entity actor,
            in UtilityAiCandidate candidate,
            int currentStep,
            OrderQueue orders,
            OrderTerminalResultBuffer terminalResults,
            out int submittedOrderTypeId,
            out int submittedOrderId)
        {
            submittedOrderTypeId = 0;
            submittedOrderId = 0;
            if (task.OrderTypeId <= 0)
            {
                return false;
            }

            if (task.PlayerId <= 0)
            {
                throw new InvalidOperationException(
                    $"Utility AI task attempted to submit order type id {task.OrderTypeId} without a positive player id.");
            }

            Order order;
            OrderSubmitMode submitMode = (OrderSubmitMode)(byte)task.SubmitMode;
            switch (task.PayloadKind)
            {
                case AiOrderPayloadKind.CastAbility:
                    if (task.AbilitySlotIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Utility AI SubmitOrder requires AbilitySlotIndex for typed CastAbility orderTypeId={task.OrderTypeId}.");
                    }

                    order = OrderBuilder.CreateCastAbility(
                        task.OrderTypeId,
                        task.PlayerId,
                        actor,
                        candidate.Target,
                        Entity.Null,
                        task.AbilitySlotIndex,
                        submitMode,
                        currentStep);

                    if (_world.TryGet(candidate.Target, out WorldPositionCm castTargetPosition))
                    {
                        var pos = castTargetPosition.Value.ToVector2();
                        OrderBuilder.SetSingleWorldCm(ref order, new Vector3(pos.X, 0f, pos.Y));
                    }
                    break;

                case AiOrderPayloadKind.TargetEntity:
                    order = OrderBuilder.CreateTargetEntity(
                        task.OrderTypeId,
                        task.PlayerId,
                        actor,
                        candidate.Target,
                        submitMode,
                        currentStep);
                    break;

                case AiOrderPayloadKind.MoveToWorldCm:
                    if (!_world.TryGet(candidate.Target, out WorldPositionCm moveTargetPosition))
                    {
                        throw new InvalidOperationException(
                            $"ORDER.BUILDER.ERR.MoveDestinationRequired: Utility AI target has no WorldPositionCm for orderTypeId={task.OrderTypeId}.");
                    }

                    var movePos = moveTargetPosition.Value.ToVector2();
                    order = OrderBuilder.CreateMoveToWorldCm(
                        task.OrderTypeId,
                        task.PlayerId,
                        actor,
                        new Vector3(movePos.X, 0f, movePos.Y),
                        submitMode,
                        currentStep);
                    break;

                case AiOrderPayloadKind.Stop:
                    order = OrderBuilder.CreateStop(
                        task.OrderTypeId,
                        task.PlayerId,
                        actor,
                        submitMode,
                        currentStep);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"ORDER.BUILDER.ERR.UnsupportedAiOrderPayloadKind: kind={task.PayloadKind}, orderTypeId={task.OrderTypeId}.");
            }

            orders.EnsureOrderId(ref order);
            terminalResults.Retain(order.OrderId);
            bool retained = true;
            try
            {
                OrderSubmitResult result = orders.SubmitAssigned(ref order);
                if (!OrderSubmitResultSemantics.IsAccepted(result))
                {
                    terminalResults.Release(order.OrderId);
                    retained = false;
                    return false;
                }

                submittedOrderTypeId = task.OrderTypeId;
                submittedOrderId = order.OrderId;
                return true;
            }
            catch
            {
                if (retained)
                {
                    terminalResults.Release(order.OrderId);
                }

                throw;
            }
        }

        private int AcquireTargets(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            int filterId,
            int currentStep,
            in UtilityAiCombatMemory memory,
            Entity[] scratch,
            out UtilityAiFilterRejectReason rejectReason)
        {
            rejectReason = UtilityAiFilterRejectReason.None;
            if ((uint)filterId >= (uint)runtime.TargetFilters.Length)
            {
                return 0;
            }

            ref readonly var filter = ref runtime.TargetFilters[filterId];
            bool sourceSelf = false;
            int count = 0;
            if (!_world.TryGet(actor, out WorldPositionCm actorPosition))
            {
                rejectReason = UtilityAiFilterRejectReason.MissingPosition;
                return 0;
            }

            WorldCmInt2 actorPos = actorPosition.Value.ToWorldCmInt2();
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
                        count = _spatialQueries.QueryRadius(actorPos, op.IntA, scratch).Count;
                        if (count > filter.MaxResults)
                        {
                            count = filter.MaxResults;
                            rejectReason = UtilityAiFilterRejectReason.ScratchFull;
                        }
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
            for (int i = 0; i < count && write < filter.MaxResults && write < scratch.Length; i++)
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

                if (!PassesAllFilterOps(runtime, actor, target, actorPos, filterId, currentStep, out _, out _, out rejectReason))
                {
                    continue;
                }

                scratch[write++] = target;
            }

            if (write >= scratch.Length)
            {
                rejectReason = UtilityAiFilterRejectReason.ScratchFull;
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
            ref GraphScoreEvaluationBudget scoreBudget,
            out float score,
            out UtilityAiFilterRejectReason rejectReason)
        {
            rejectReason = UtilityAiFilterRejectReason.None;
            float multiply = decision.BaseScore;
            float weighted = 0f;
            int end = decision.ConsiderationOffset + decision.ConsiderationCount;
            for (int i = decision.ConsiderationOffset; i < end; i++)
            {
                ref readonly var consideration = ref runtime.Considerations[i];
                if (!TrySampleInput(runtime, actor, target, currentStep, consideration.InputId, ref scoreBudget, out float raw))
                {
                    rejectReason = UtilityAiFilterRejectReason.ScoreGraphBudgetExhausted;
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
            ref GraphScoreEvaluationBudget scoreBudget,
            out int bucket,
            out UtilityAiFilterRejectReason rejectReason)
        {
            bucket = 0;
            rejectReason = UtilityAiFilterRejectReason.None;
            int end = decision.ConsiderationOffset + decision.ConsiderationCount;
            for (int i = decision.ConsiderationOffset; i < end; i++)
            {
                ref readonly var consideration = ref runtime.Considerations[i];
                if (consideration.Aggregate != UtilityAiAggregateMode.PriorityBucket)
                {
                    continue;
                }

                if (!TrySampleInput(runtime, actor, target, currentStep, consideration.InputId, ref scoreBudget, out float raw))
                {
                    rejectReason = UtilityAiFilterRejectReason.ScoreGraphBudgetExhausted;
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
            ref GraphScoreEvaluationBudget scoreBudget,
            out float value)
        {
            value = 0f;
            if ((uint)inputId >= (uint)runtime.Inputs.Length)
            {
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
                    value = _world.Has<GameplayTagContainer>(target) && _world.Get<GameplayTagContainer>(target).HasTag(input.Arg0) ? 1f : 0f;
                    return true;
                case UtilityAiInputKind.SourceHasTag:
                    value = _world.Has<GameplayTagContainer>(actor) && _world.Get<GameplayTagContainer>(actor).HasTag(input.Arg0) ? 1f : 0f;
                    return true;
                case UtilityAiInputKind.ActuatorReadiness01:
                    value = TryReadActuatorReadiness(actor, input.Arg0, out float ready) ? ready : 0f;
                    return true;
                case UtilityAiInputKind.GraphScore:
                    return TryExecuteScoreGraph(actor, target, input.GraphId, ref scoreBudget, out value);
                default:
                    return true;
            }
        }

        private bool CanSwitchToDecision(
            in UtilityAiCompiledRuntime runtime,
            in UtilityAiDecisionDefinition decision,
            int decisionId,
            int currentStep,
            in UtilityAiState state)
        {
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
            int graphId,
            ref GraphScoreEvaluationBudget scoreBudget,
            out float score)
        {
            if (graphId <= 0)
            {
                throw new InvalidOperationException("Utility AI GraphScore input requires a positive graph id.");
            }

            if (_graphs == null || _graphApi == null)
            {
                throw new InvalidOperationException(
                    "Utility AI GraphScore input requires GraphProgramRegistry and IGraphRuntimeApi from the engine-owned GAS graph runtime.");
            }

            return GraphScoreEvaluator.TryEvaluate(
                _world,
                _graphs,
                _graphApi,
                graphId,
                actor,
                target,
                default,
                ref scoreBudget,
                out score);
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
