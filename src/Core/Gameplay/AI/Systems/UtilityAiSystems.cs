using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Spatial;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class UtilityAiThinkScheduleSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly UtilityAiCompiledRuntime _runtime;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<UtilityAiAgent, UtilityAiState>();

        public UtilityAiThinkScheduleSystem(World world, IClock clock, UtilityAiCompiledRuntime runtime)
            : base(world)
        {
            _clock = clock;
            _runtime = runtime;
        }

        public override void Update(in float dt)
        {
            if (!_runtime.IsEnabled)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.Step);
            var job = new ScheduleJob(_runtime, step);
            World.InlineQuery<ScheduleJob, UtilityAiAgent, UtilityAiState>(in Query, ref job);
        }

        private struct ScheduleJob : IForEach<UtilityAiAgent, UtilityAiState>
        {
            private readonly UtilityAiCompiledRuntime _runtime;
            private readonly int _step;

            public ScheduleJob(UtilityAiCompiledRuntime runtime, int step)
            {
                _runtime = runtime;
                _step = step;
            }

            public void Update(ref UtilityAiAgent agent, ref UtilityAiState state)
            {
                if ((uint)agent.ProfileId >= (uint)_runtime.Profiles.Length)
                {
                    return;
                }

                if (state.CurrentDecisionId < 0)
                {
                    return;
                }

                if (state.NextThinkStep <= 0 && state.CurrentDecisionId == 0 && state.DecisionStartedStep == 0)
                {
                    state.CurrentDecisionId = -1;
                }

                if (state.NextThinkStep <= 0)
                {
                    state.NextThinkStep = _step;
                }
            }
        }
    }

    public sealed class UtilityAiDecisionSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly UtilityAiCompiledRuntime _runtime;
        private readonly UtilityAiRuntimeEvaluator _evaluator;
        private readonly OrderQueue _orders;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<UtilityAiAgent, UtilityAiState, OrderBuffer>();

        public UtilityAiDecisionSystem(
            World world,
            IClock clock,
            UtilityAiCompiledRuntime runtime,
            ISpatialQueryService spatialQueries,
            Ludots.Core.Gameplay.GAS.AbilityDefinitionRegistry? abilities,
            Ludots.Core.GraphRuntime.GraphProgramRegistry? graphs,
            Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi? graphApi,
            Ludots.Core.NodeLibraries.GASGraph.GasGraphOpHandlerTable? graphHandlers,
            OrderQueue orders)
            : base(world)
        {
            _clock = clock;
            _runtime = runtime;
            _orders = orders;
            _evaluator = new UtilityAiRuntimeEvaluator(world, spatialQueries, abilities, graphs, graphApi, graphHandlers, ResolveTargetScratchCapacity(in runtime));
        }

        public override void Update(in float dt)
        {
            if (!_runtime.IsEnabled)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.Step);
            var job = new DecisionJob(World, _runtime, _evaluator, _orders, step);
            World.InlineEntityQuery<DecisionJob, UtilityAiAgent, UtilityAiState, OrderBuffer>(in Query, ref job);
        }

        private struct DecisionJob : IForEachWithEntity<UtilityAiAgent, UtilityAiState, OrderBuffer>
        {
            private readonly World _world;
            private readonly UtilityAiCompiledRuntime _runtime;
            private readonly UtilityAiRuntimeEvaluator _evaluator;
            private readonly OrderQueue _orders;
            private readonly int _step;

            public DecisionJob(
                World world,
                UtilityAiCompiledRuntime runtime,
                UtilityAiRuntimeEvaluator evaluator,
                OrderQueue orders,
                int step)
            {
                _world = world;
                _runtime = runtime;
                _evaluator = evaluator;
                _orders = orders;
                _step = step;
            }

            public void Update(Entity entity, ref UtilityAiAgent agent, ref UtilityAiState state, ref OrderBuffer buffer)
            {
                if (state.NextThinkStep > _step || buffer.HasActive || buffer.HasQueued || buffer.HasPending)
                {
                    return;
                }

                UtilityAiCombatMemory memory = _world.Has<UtilityAiCombatMemory>(entity)
                    ? _world.Get<UtilityAiCombatMemory>(entity)
                    : default;

                bool found = _evaluator.TryEvaluate(
                    in _runtime,
                    entity,
                    agent.ProfileId,
                    _step,
                    in state,
                    in memory,
                    out var best,
                    out int candidateCount,
                    out var rejectReason,
                    out var readinessBlockReason);

                bool hasTrace = _world.Has<UtilityAiDecisionTrace>(entity);
                if (hasTrace)
                {
                    ref var trace = ref _world.Get<UtilityAiDecisionTrace>(entity);
                    trace.CandidateCount = candidateCount;
                    trace.LastFilterRejectReason = (int)rejectReason;
                    trace.LastReadinessBlockReason = (int)readinessBlockReason;
                }

                if (found)
                {
                    if (hasTrace)
                    {
                        ref var trace = ref _world.Get<UtilityAiDecisionTrace>(entity);
                        trace.BestDecisionId = best.DecisionId;
                        trace.BestTarget = best.Target;
                        trace.BestScore = best.Score;
                        trace.BestPriorityBucket = best.PriorityBucket;
                        trace.BestDistanceSq = best.DistanceSq;
                    }

                    if (_evaluator.TrySubmitTasks(
                            in _runtime,
                            entity,
                            in best,
                            _step,
                            _orders,
                            out int orderTypeId,
                            out int abilityId,
                            out int sharedCooldownTagId,
                            out var taskKind,
                            out var taskStatus))
                    {
                        ref readonly var submittedDecision = ref _runtime.Decisions[best.DecisionId];
                        state.CurrentDecisionId = best.DecisionId;
                        state.CurrentTarget = best.Target;
                        state.CurrentScore = best.Score;
                        state.LastSwitchStep = _step;
                        state.DecisionStartedStep = _step;
                        state.CooldownDecisionId = best.DecisionId;
                        state.DecisionCooldownUntilStep = _step + submittedDecision.CooldownSteps;
                        if (sharedCooldownTagId > 0)
                        {
                            state.SharedCooldownTagId = sharedCooldownTagId;
                            state.SharedCooldownUntilStep = _step + Math.Max(1, submittedDecision.CooldownSteps);
                        }

                        if (hasTrace)
                        {
                            ref var trace = ref _world.Get<UtilityAiDecisionTrace>(entity);
                            trace.LastSubmittedOrderTypeId = orderTypeId;
                            trace.LastSubmittedAbilityId = abilityId;
                            trace.LastTaskKind = (int)taskKind;
                            trace.LastTaskStatus = (int)taskStatus;
                        }
                    }
                }

                if ((uint)agent.ProfileId < (uint)_runtime.Profiles.Length)
                {
                    state.NextThinkStep = _step + _runtime.Profiles[agent.ProfileId].DecisionIntervalSteps;
                }
            }
        }

        private static int ResolveTargetScratchCapacity(in UtilityAiCompiledRuntime runtime)
        {
            int capacity = 16;
            for (int i = 0; i < runtime.Profiles.Length; i++)
            {
                if (runtime.Profiles[i].MaxCandidates > capacity)
                {
                    capacity = runtime.Profiles[i].MaxCandidates;
                }
            }

            for (int i = 0; i < runtime.TargetFilters.Length; i++)
            {
                if (runtime.TargetFilters[i].MaxResults > capacity)
                {
                    capacity = runtime.TargetFilters[i].MaxResults;
                }
            }

            return capacity;
        }
    }

    public sealed class UtilityAiCombatMemoryCleanupSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<UtilityAiCombatMemory>();

        public UtilityAiCombatMemoryCleanupSystem(World world, IClock clock)
            : base(world)
        {
            _clock = clock;
        }

        public override void Update(in float dt)
        {
            int step = _clock.Now(ClockDomainId.Step);
            var job = new CleanupJob(World, step);
            World.InlineQuery<CleanupJob, UtilityAiCombatMemory>(in Query, ref job);
        }

        private struct CleanupJob : IForEach<UtilityAiCombatMemory>
        {
            private readonly World _world;
            private readonly int _step;

            public CleanupJob(World world, int step)
            {
                _world = world;
                _step = step;
            }

            public void Update(ref UtilityAiCombatMemory memory)
            {
                if (memory.LastAttacker != default &&
                    (!_world.IsAlive(memory.LastAttacker) || _step - memory.LastAttackerStep > 300))
                {
                    memory.LastAttacker = default;
                    memory.LastAttackerStep = 0;
                }

                if (memory.LastSeenTarget != default &&
                    (!_world.IsAlive(memory.LastSeenTarget) || _step - memory.LastSeenStep > 300))
                {
                    memory.LastSeenTarget = default;
                    memory.LastSeenStep = 0;
                }
            }
        }
    }
}
