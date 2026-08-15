using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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
        private readonly AbilityDefinitionRegistry? _abilities;
        private readonly GraphProgramRegistry? _graphs;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly Entity[] _targets;

        public UtilityAiRuntimeEvaluator(
            World world,
            ISpatialQueryService spatialQueries,
            AbilityDefinitionRegistry? abilities,
            GraphProgramRegistry? graphs,
            IGraphRuntimeApi? graphApi,
            int targetCapacity = 256)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            _abilities = abilities;
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
            out UtilityAiFilterRejectReason rejectReason,
            out UtilityAiReadinessBlockReason readinessBlockReason)
        {
            best = default;
            candidateCount = 0;
            rejectReason = UtilityAiFilterRejectReason.None;
            readinessBlockReason = UtilityAiReadinessBlockReason.None;

            if ((uint)profileId >= (uint)runtime.Profiles.Length)
            {
                return false;
            }

            ref readonly var profile = ref runtime.Profiles[profileId];
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
                        out rejectReason);
                    if (targetCount == 0)
                    {
                        continue;
                    }

                    for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    {
                        Entity target = _targets[targetIndex];
                        if (!PassesDecisionReadiness(actor, target, currentStep, in decision, in state, out readinessBlockReason))
                        {
                            continue;
                        }

                        candidateCount++;
                        long distanceSq = DistanceSquared(actor, target);
                        int priorityBucket = ComputePriorityBucket(runtime, actor, target, currentStep, in decision);
                        float score = EvaluateDecision(runtime, actor, target, currentStep, in decision);
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
            out int submittedOrderTypeId,
            out int submittedAbilityId,
            out int submittedSharedCooldownTagId,
            out UtilityAiTaskKind taskKind,
            out UtilityAiTaskRunStatus taskStatus)
        {
            submittedOrderTypeId = 0;
            submittedAbilityId = 0;
            submittedSharedCooldownTagId = 0;
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
                        if (!TrySubmitOrderTask(in task, in decision, actor, in candidate, currentStep, orders, out submittedOrderTypeId, out submittedAbilityId))
                        {
                            taskStatus = submittedAny ? UtilityAiTaskRunStatus.Running : UtilityAiTaskRunStatus.Blocked;
                            return submittedAny;
                        }

                        submittedSharedCooldownTagId = ResolveSharedCooldownTag(in decision, submittedAbilityId);
                        submittedAny = true;
                        if (task.Kind == UtilityAiTaskKind.SubmitOrder)
                        {
                            taskStatus = UtilityAiTaskRunStatus.Complete;
                            return true;
                        }
                        break;
                }
            }

            taskStatus = submittedAny
                ? UtilityAiTaskRunStatus.Complete
                : requiredAny ? UtilityAiTaskRunStatus.Blocked : UtilityAiTaskRunStatus.None;
            return submittedAny;
        }

        private bool TrySubmitOrderTask(
            in UtilityAiTaskDefinition task,
            in UtilityAiDecisionDefinition decision,
            Entity actor,
            in UtilityAiCandidate candidate,
            int currentStep,
            OrderQueue orders,
            out int submittedOrderTypeId,
            out int submittedAbilityId)
        {
            submittedOrderTypeId = 0;
            submittedAbilityId = 0;
            if (task.OrderTypeId <= 0)
            {
                return false;
            }

            int slotIndex = task.AbilitySlotIndex >= 0
                ? task.AbilitySlotIndex
                : decision.AbilitySlotIndex;
            int abilityId = task.AbilityId > 0
                ? task.AbilityId
                : decision.AutocastAbilityId;
            if (slotIndex < 0 && abilityId > 0 && TryFindAbilitySlot(actor, abilityId, out int resolvedSlot))
            {
                slotIndex = resolvedSlot;
            }

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

            if (!orders.TryEnqueue(in order))
            {
                return false;
            }

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
                    case UtilityAiTargetFilterOpKind.AbilityEligible:
                        if (!IsAbilityReady(actor, target, op.IntA, currentStep, sharedCooldownTagId: 0, out _))
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

        private float EvaluateDecision(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            Entity target,
            int currentStep,
            in UtilityAiDecisionDefinition decision)
        {
            float multiply = decision.BaseScore;
            float weighted = 0f;
            int end = decision.ConsiderationOffset + decision.ConsiderationCount;
            for (int i = decision.ConsiderationOffset; i < end; i++)
            {
                ref readonly var consideration = ref runtime.Considerations[i];
                float raw = SampleInput(runtime, actor, target, currentStep, consideration.InputId);
                float normalized = Normalize(runtime.Normalizations[consideration.NormalizationId], raw);
                float curved = Curve(runtime.Curves[consideration.CurveId], normalized);

                switch (consideration.Aggregate)
                {
                    case UtilityAiAggregateMode.Veto:
                        if (curved <= 0f)
                        {
                            return 0f;
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

            return (multiply + weighted) * decision.Weight;
        }

        private int ComputePriorityBucket(
            in UtilityAiCompiledRuntime runtime,
            Entity actor,
            Entity target,
            int currentStep,
            in UtilityAiDecisionDefinition decision)
        {
            int bucket = 0;
            int end = decision.ConsiderationOffset + decision.ConsiderationCount;
            for (int i = decision.ConsiderationOffset; i < end; i++)
            {
                ref readonly var consideration = ref runtime.Considerations[i];
                if (consideration.Aggregate != UtilityAiAggregateMode.PriorityBucket)
                {
                    continue;
                }

                float raw = SampleInput(runtime, actor, target, currentStep, consideration.InputId);
                float normalized = Normalize(runtime.Normalizations[consideration.NormalizationId], raw);
                float curved = Curve(runtime.Curves[consideration.CurveId], normalized);
                bucket += (int)MathF.Round(curved * consideration.Weight);
            }

            return bucket;
        }

        private float SampleInput(in UtilityAiCompiledRuntime runtime, Entity actor, Entity target, int currentStep, int inputId)
        {
            if ((uint)inputId >= (uint)runtime.Inputs.Length)
            {
                return 0f;
            }

            ref readonly var input = ref runtime.Inputs[inputId];
            switch (input.Kind)
            {
                case UtilityAiInputKind.Constant:
                    return input.Arg0;
                case UtilityAiInputKind.DistanceToTarget:
                    return Distance(actor, target);
                case UtilityAiInputKind.TargetPriorityBucket:
                    return ReadTargetPriorityBucket(target, input.Arg0);
                case UtilityAiInputKind.TargetHasTag:
                    return _world.Has<GameplayTagContainer>(target) && _world.Get<GameplayTagContainer>(target).HasTag(input.Arg0) ? 1f : 0f;
                case UtilityAiInputKind.SourceHasTag:
                    return _world.Has<GameplayTagContainer>(actor) && _world.Get<GameplayTagContainer>(actor).HasTag(input.Arg0) ? 1f : 0f;
                case UtilityAiInputKind.AbilityReady:
                    return IsAbilityReady(actor, target, input.Arg0, currentStep, sharedCooldownTagId: 0, out _) ? 1f : 0f;
                case UtilityAiInputKind.ActuatorReadiness01:
                    return TryReadActuatorReadiness(actor, input.Arg0, out float ready) ? ready : 0f;
                case UtilityAiInputKind.GraphScore:
                    return ExecuteScoreGraph(actor, target, input.GraphId);
                default:
                    return 0f;
            }
        }

        private bool PassesDecisionReadiness(
            Entity actor,
            Entity target,
            int currentStep,
            in UtilityAiDecisionDefinition decision,
            in UtilityAiState state,
            out UtilityAiReadinessBlockReason blockReason)
        {
            blockReason = UtilityAiReadinessBlockReason.None;
            if ((decision.Flags & UtilityAiDecisionFlags.Autocast) == 0)
            {
                return true;
            }

            int abilityId = decision.AutocastAbilityId;
            if (abilityId <= 0 && decision.AbilitySlotIndex >= 0)
            {
                if (!TryResolveAbilityAtSlot(actor, decision.AbilitySlotIndex, out abilityId))
                {
                    blockReason = UtilityAiReadinessBlockReason.AbilityMissing;
                    return false;
                }
            }

            int sharedCooldownTagId = decision.SharedCooldownTagId;
            if (sharedCooldownTagId <= 0 && abilityId > 0 && _abilities != null && _abilities.TryGet(abilityId, out var ability) && ability.HasCooldown)
            {
                sharedCooldownTagId = ability.Cooldown.CooldownTagId;
            }

            if (sharedCooldownTagId > 0 &&
                state.SharedCooldownTagId == sharedCooldownTagId &&
                currentStep < state.SharedCooldownUntilStep)
            {
                blockReason = UtilityAiReadinessBlockReason.SharedCooldown;
                return false;
            }

            return abilityId <= 0 || IsAbilityReady(actor, target, abilityId, currentStep, sharedCooldownTagId, out blockReason);
        }

        private int ResolveSharedCooldownTag(in UtilityAiDecisionDefinition decision, int abilityId)
        {
            if (decision.SharedCooldownTagId > 0)
            {
                return decision.SharedCooldownTagId;
            }

            if (abilityId > 0 &&
                _abilities != null &&
                _abilities.TryGet(abilityId, out var ability) &&
                ability.HasCooldown)
            {
                return ability.Cooldown.CooldownTagId;
            }

            return 0;
        }

        private bool IsAbilityReady(
            Entity actor,
            Entity target,
            int abilityId,
            int currentStep,
            int sharedCooldownTagId,
            out UtilityAiReadinessBlockReason blockReason)
        {
            blockReason = UtilityAiReadinessBlockReason.None;
            if (abilityId <= 0)
            {
                blockReason = UtilityAiReadinessBlockReason.AbilityMissing;
                return false;
            }

            if (_abilities == null || !_abilities.TryGet(abilityId, out var ability))
            {
                blockReason = UtilityAiReadinessBlockReason.AbilityMissing;
                return false;
            }

            if (ability.HasCooldown)
            {
                if (AttributeRegistry.IsValidId(ability.Cooldown.CooldownValueAttributeId) &&
                    _world.Has<AttributeBuffer>(actor) &&
                    _world.Get<AttributeBuffer>(actor).GetCurrent(ability.Cooldown.CooldownValueAttributeId) > 0f)
                {
                    blockReason = UtilityAiReadinessBlockReason.AbilityCooldown;
                    return false;
                }

                int cooldownTag = sharedCooldownTagId > 0 ? sharedCooldownTagId : ability.Cooldown.CooldownTagId;
                if (cooldownTag > 0 &&
                    _world.Has<GameplayTagContainer>(actor) &&
                    _world.Get<GameplayTagContainer>(actor).HasTag(cooldownTag))
                {
                    blockReason = UtilityAiReadinessBlockReason.SharedCooldown;
                    return false;
                }
            }

            if (ability.HasActivationBlockTags)
            {
                if (!_world.Has<GameplayTagContainer>(actor))
                {
                    if (!ability.ActivationBlockTags.RequiredAll.IsEmpty)
                    {
                        blockReason = UtilityAiReadinessBlockReason.ActivationBlockTags;
                        return false;
                    }
                }
                else
                {
                    ref var tags = ref _world.Get<GameplayTagContainer>(actor);
                    if (!ability.ActivationBlockTags.RequiredAll.IsEmpty &&
                        !tags.ContainsAll(in ability.ActivationBlockTags.RequiredAll))
                    {
                        blockReason = UtilityAiReadinessBlockReason.ActivationBlockTags;
                        return false;
                    }

                    if (!ability.ActivationBlockTags.BlockedAny.IsEmpty &&
                        tags.Intersects(in ability.ActivationBlockTags.BlockedAny))
                    {
                        blockReason = UtilityAiReadinessBlockReason.ActivationBlockTags;
                        return false;
                    }
                }
            }

            if (!PassesActuatorGates(actor, abilityId, out blockReason))
            {
                return false;
            }

            if (ability.HasActivationPrecondition &&
                !AbilityActivationPreconditionEvaluator.Evaluate(
                    _world,
                    actor,
                    target,
                    default,
                    abilityId,
                    in ability.ActivationPrecondition,
                    _graphs,
                    _graphApi))
            {
                blockReason = UtilityAiReadinessBlockReason.ActivationPrecondition;
                return false;
            }

            return true;
        }

        private bool PassesActuatorGates(Entity actor, int abilityId, out UtilityAiReadinessBlockReason blockReason)
        {
            blockReason = UtilityAiReadinessBlockReason.None;
            if (_world.Has<ActuatorReadiness>(actor))
            {
                var readiness = _world.Get<ActuatorReadiness>(actor);
                if (readiness.ActuatorId == abilityId && readiness.Ready01 < 1f)
                {
                    blockReason = UtilityAiReadinessBlockReason.ActuatorNotReady;
                    return false;
                }
            }

            if (_world.Has<AimGate>(actor))
            {
                var aimGate = _world.Get<AimGate>(actor);
                if (aimGate.ActuatorId == abilityId && aimGate.Ready01 < 1f)
                {
                    blockReason = UtilityAiReadinessBlockReason.AimGateNotReady;
                    return false;
                }
            }

            return true;
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
            int sharedCooldownTagId = ResolveSharedCooldownTag(in decision, decision.AutocastAbilityId);
            if (sharedCooldownTagId > 0 &&
                state.SharedCooldownTagId == sharedCooldownTagId &&
                currentStep < state.SharedCooldownUntilStep)
            {
                blockReason = UtilityAiReadinessBlockReason.SharedCooldown;
                return false;
            }

            if (decision.CooldownSteps > 0 &&
                state.CooldownDecisionId == decisionId &&
                currentStep < state.DecisionCooldownUntilStep)
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

        private bool TryResolveAbilityAtSlot(Entity actor, int slotIndex, out int abilityId)
        {
            abilityId = 0;
            if (!_world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            if (!AbilitySlotResolver.TryResolve(_world, actor, slotIndex, out AbilitySlotState slot))
            {
                return false;
            }

            abilityId = slot.AbilityId;
            return abilityId > 0;
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

        private float ExecuteScoreGraph(Entity actor, Entity target, int graphId)
        {
            if (graphId <= 0 || _graphs == null || _graphApi == null)
            {
                return 0f;
            }

            if (!_graphs.TryGetProgram(graphId, out var program))
            {
                throw new InvalidOperationException($"AI score graph id {graphId} is not registered.");
            }

            GraphKind kind = _graphs.RequireKind(graphId, GraphKind.Score);
            UtilityAiGraphSafety.ValidateScoreProgram(program, "AI runtime", graphId);
            return GasGraphExecutor.ExecuteScore(_world, actor, target, default, program, _graphApi, kind, _graphs);
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
