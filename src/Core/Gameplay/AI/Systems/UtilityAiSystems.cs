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
        private readonly UtilityAiRuntimeSource _runtimeSource;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<UtilityAiAgent, UtilityAiState>();

        public UtilityAiThinkScheduleSystem(World world, IClock clock, UtilityAiCompiledRuntime runtime)
            : this(world, clock, new UtilityAiRuntimeSource(runtime))
        {
        }

        public UtilityAiThinkScheduleSystem(World world, IClock clock, UtilityAiRuntimeSource runtimeSource)
            : base(world)
        {
            _clock = clock;
            _runtimeSource = runtimeSource ?? throw new ArgumentNullException(nameof(runtimeSource));
        }

        public override void Update(in float dt)
        {
            UtilityAiCompiledRuntime runtime = _runtimeSource.Current;
            if (!runtime.IsEnabled)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.Step);
            var job = new ScheduleJob(runtime, step);
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
        private readonly UtilityAiRuntimeSource _runtimeSource;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly Ludots.Core.GraphRuntime.GraphProgramRegistry? _graphs;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi? _graphApi;
        private readonly Ludots.Core.Gameplay.GAS.AbilityActivationEligibilityQuery _abilityEligibility;
        private UtilityAiRuntimeEvaluator _evaluator;
        private int _boundRuntimeVersion = -1;
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
            OrderQueue orders,
            Ludots.Core.Gameplay.GAS.AbilityActivationEligibilityQuery abilityEligibility)
            : this(
                world,
                clock,
                new UtilityAiRuntimeSource(runtime),
                spatialQueries,
                abilities,
                graphs,
                graphApi,
                orders,
                abilityEligibility)
        {
        }

        public UtilityAiDecisionSystem(
            World world,
            IClock clock,
            UtilityAiRuntimeSource runtimeSource,
            ISpatialQueryService spatialQueries,
            Ludots.Core.Gameplay.GAS.AbilityDefinitionRegistry? abilities,
            Ludots.Core.GraphRuntime.GraphProgramRegistry? graphs,
            Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi? graphApi,
            OrderQueue orders,
            Ludots.Core.Gameplay.GAS.AbilityActivationEligibilityQuery abilityEligibility)
            : base(world)
        {
            _clock = clock;
            _runtimeSource = runtimeSource ?? throw new ArgumentNullException(nameof(runtimeSource));
            _spatialQueries = spatialQueries;
            _graphs = graphs;
            _graphApi = graphApi;
            _abilityEligibility = abilityEligibility;
            _orders = orders;
            BindCurrentRuntime();
        }

        public override void Update(in float dt)
        {
            BindCurrentRuntime();
            UtilityAiCompiledRuntime runtime = _runtimeSource.Current;
            if (!runtime.IsEnabled)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.Step);
            var job = new DecisionJob(World, runtime, _evaluator, _orders, step);
            World.InlineEntityQuery<DecisionJob, UtilityAiAgent, UtilityAiState, OrderBuffer>(in Query, ref job);
        }

        private void BindCurrentRuntime()
        {
            if (_boundRuntimeVersion == _runtimeSource.Version && _evaluator != null)
            {
                return;
            }

            UtilityAiCompiledRuntime runtime = _runtimeSource.Current;
            if (runtime.RequiresGraphScoreServices)
            {
                if (_graphs == null)
                {
                    throw new InvalidOperationException(
                        "Utility AI config contains GraphScore inputs, but GraphProgramRegistry is missing during UtilityAiDecisionSystem assembly.");
                }

                if (_graphApi == null)
                {
                    throw new InvalidOperationException(
                        "Utility AI config contains GraphScore inputs, but IGraphRuntimeApi is missing during UtilityAiDecisionSystem assembly.");
                }

                for (int i = 0; i < runtime.GraphScorePrograms.Length; i++)
                {
                    int graphId = runtime.GraphScorePrograms[i].GraphId;
                    if (!_graphs.TryGetProgram(graphId, out _))
                    {
                        throw new InvalidOperationException(
                            $"Utility AI GraphScore graph id {graphId} is absent from GraphProgramRegistry during UtilityAiDecisionSystem assembly.");
                    }
                }
            }

            _evaluator = new UtilityAiRuntimeEvaluator(
                World,
                _spatialQueries,
                _abilityEligibility,
                _graphApi,
                ResolveTargetScratchCapacity(in runtime));
            _boundRuntimeVersion = _runtimeSource.Version;
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
                if (state.NextThinkStep > _step ||
                    IsWaitingForOrderResult(in _runtime, in state) ||
                    buffer.HasActive ||
                    buffer.HasQueued ||
                    buffer.HasPending)
                {
                    return;
                }

                UtilityAiCombatMemory memory = _world.Has<UtilityAiCombatMemory>(entity)
                    ? _world.Get<UtilityAiCombatMemory>(entity)
                    : default;

                UtilityAiDecisionResult evaluation = _evaluator.Evaluate(
                    in _runtime,
                    entity,
                    agent.ProfileId,
                    _step,
                    in state,
                    in memory);

                bool hasTrace = _world.Has<UtilityAiDecisionTrace>(entity);
                if (hasTrace)
                {
                    ref var trace = ref _world.Get<UtilityAiDecisionTrace>(entity);
                    UpdateThinkTrace(ref trace, in evaluation);
                }

                if (evaluation.HasCompleteCandidate)
                {
                    UtilityAiCandidate best = evaluation.Best;
                    if (hasTrace)
                    {
                        ref var trace = ref _world.Get<UtilityAiDecisionTrace>(entity);
                        trace.BestDecisionId = best.DecisionId;
                        trace.BestTarget = best.Target;
                        trace.BestScore = best.Score;
                        trace.BestPriorityBucket = best.PriorityBucket;
                        trace.BestDistanceSq = best.DistanceSq;
                    }

                    if (_evaluator.TrySubmitTask(
                            in _runtime,
                            entity,
                            in best,
                            _step,
                            _orders,
                            out int orderId,
                            out int orderTypeId,
                            out int abilityId,
                            out OrderSubmitResult submissionResult,
                            out var taskStatus))
                    {
                        state.LastSubmittedOrderId = orderId;
                        state.CurrentTaskStatus = taskStatus;
                        state.CurrentTaskFailureReason = OrderSubmitResultSemantics.IsAccepted(submissionResult)
                            ? OrderFailureReason.None
                            : OrderSubmitResultSemantics.ToFailureReason(submissionResult);

                        if (OrderSubmitResultSemantics.IsAccepted(submissionResult))
                        {
                            state.CurrentDecisionId = best.DecisionId;
                            state.CurrentTarget = best.Target;
                            state.CurrentScore = best.Score;
                            state.LastSwitchStep = _step;
                            state.DecisionStartedStep = _step;
                        }
                        else
                        {
                            ResetFailedDecision(ref state);
                        }

                        if (hasTrace)
                        {
                            ref var trace = ref _world.Get<UtilityAiDecisionTrace>(entity);
                            trace.LastSubmittedOrderId = orderId;
                            trace.LastSubmittedOrderTypeId = orderTypeId;
                            trace.LastSubmittedAbilityId = abilityId;
                            trace.LastTaskStatus = (int)taskStatus;
                            trace.LastTaskFailureReason = (int)state.CurrentTaskFailureReason;
                        }
                    }
                }

                if ((uint)agent.ProfileId < (uint)_runtime.Profiles.Length)
                {
                    state.NextThinkStep = _step + _runtime.Profiles[agent.ProfileId].DecisionIntervalSteps;
                }
            }

            private static void UpdateThinkTrace(
                ref UtilityAiDecisionTrace trace,
                in UtilityAiDecisionResult evaluation)
            {
                trace.ThinkOutcome = (int)evaluation.Outcome;
                trace.CandidateCount = evaluation.CandidateCount;
                trace.CandidateLimit = evaluation.CandidateLimit;
                trace.GraphScoreInstructionCount = evaluation.GraphScoreInstructionCount;
                trace.GraphScoreInstructionLimit = evaluation.GraphScoreInstructionLimit;
                trace.LastFilterRejectReason = (int)evaluation.FilterRejectReason;
                trace.LastReadinessBlockReason = (int)evaluation.ReadinessBlockReason;
                trace.BestDecisionId = -1;
                trace.BestTarget = Entity.Null;
                trace.BestScore = 0f;
                trace.BestPriorityBucket = 0;
                trace.BestDistanceSq = 0;
            }

            private static bool IsWaitingForOrderResult(
                in UtilityAiCompiledRuntime runtime,
                in UtilityAiState state)
            {
                if (state.CurrentTaskStatus == UtilityAiTaskRunStatus.Submitted ||
                    state.CurrentTaskStatus == UtilityAiTaskRunStatus.Pending)
                {
                    return true;
                }

                if (state.CurrentTaskStatus != UtilityAiTaskRunStatus.Admitted)
                {
                    return false;
                }

                if ((uint)state.CurrentDecisionId >= (uint)runtime.Decisions.Length)
                {
                    throw new InvalidOperationException(
                        $"UTILITY.ORDER.ERR.InvalidCurrentDecision: decisionId={state.CurrentDecisionId}, orderId={state.LastSubmittedOrderId}.");
                }

                return runtime.Decisions[state.CurrentDecisionId].KeepRunningUntilFinished;
            }

            private static void ResetFailedDecision(ref UtilityAiState state)
            {
                state.CurrentDecisionId = -1;
                state.CurrentTarget = Entity.Null;
                state.CurrentScore = 0f;
                state.DecisionStartedStep = 0;
            }
        }

        private static int ResolveTargetScratchCapacity(in UtilityAiCompiledRuntime runtime)
        {
            int capacity = 0;
            for (int i = 0; i < runtime.TargetFilters.Length; i++)
            {
                if (runtime.TargetFilters[i].MaxResults > capacity)
                {
                    capacity = runtime.TargetFilters[i].MaxResults;
                }
            }

            if (runtime.IsEnabled && capacity <= 0)
            {
                throw new InvalidOperationException(
                    "Utility AI runtime has profiles but no positive target-filter scratch capacity.");
            }

            return capacity;
        }
    }

    public sealed class UtilityAiOrderResultSystem : BaseSystem<World, float>
    {
        public const string MissingEntityAdmissionError = "UTILITY.ORDER.ERR.EntityAdmissionResultMissing";
        public const string MissingTerminalResultError = "UTILITY.ORDER.ERR.TerminalResultMissing";

        private readonly IClock _clock;
        private readonly UtilityAiRuntimeSource _runtimeSource;
        private readonly OrderAdmissionResultBuffer _admissionResults;
        private readonly OrderTerminalResultBuffer _terminalResults;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<UtilityAiState, OrderBuffer>();

        public UtilityAiOrderResultSystem(
            World world,
            IClock clock,
            UtilityAiCompiledRuntime runtime,
            OrderAdmissionResultBuffer admissionResults,
            OrderTerminalResultBuffer terminalResults)
            : this(
                world,
                clock,
                new UtilityAiRuntimeSource(runtime),
                admissionResults,
                terminalResults)
        {
        }

        public UtilityAiOrderResultSystem(
            World world,
            IClock clock,
            UtilityAiRuntimeSource runtimeSource,
            OrderAdmissionResultBuffer admissionResults,
            OrderTerminalResultBuffer terminalResults)
            : base(world)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _runtimeSource = runtimeSource ?? throw new ArgumentNullException(nameof(runtimeSource));
            _admissionResults = admissionResults ?? throw new ArgumentNullException(nameof(admissionResults));
            _terminalResults = terminalResults ?? throw new ArgumentNullException(nameof(terminalResults));
        }

        public override void Update(in float dt)
        {
            UtilityAiCompiledRuntime runtime = _runtimeSource.Current;
            int currentStep = _clock.Now(ClockDomainId.Step);
            var admissionJob = new AdmissionJob(
                World,
                runtime,
                _admissionResults,
                currentStep);
            World.InlineEntityQuery<AdmissionJob, UtilityAiState, OrderBuffer>(
                in Query,
                ref admissionJob);

            ProcessTerminalResults(in runtime, currentStep);

            var lossJob = new TerminalLossJob(runtime);
            World.InlineEntityQuery<TerminalLossJob, UtilityAiState, OrderBuffer>(
                in Query,
                ref lossJob);
        }

        private void ProcessTerminalResults(in UtilityAiCompiledRuntime runtime, int currentStep)
        {
            for (int i = 0; i < _terminalResults.Count; i++)
            {
                ref readonly OrderTerminalOutcome outcome = ref _terminalResults[i];
                Entity actor = outcome.Actor;
                if (!World.IsAlive(actor) ||
                    !World.Has<UtilityAiState>(actor))
                {
                    continue;
                }

                ref UtilityAiState state = ref World.Get<UtilityAiState>(actor);
                if (state.LastSubmittedOrderId != outcome.OrderId ||
                    !WaitsForTerminal(in runtime, in state))
                {
                    continue;
                }

                switch (outcome.State)
                {
                    case OrderTerminalState.Completed:
                        state.CurrentTaskStatus = UtilityAiTaskRunStatus.Completed;
                        state.CurrentTaskFailureReason = OrderFailureReason.None;
                        CommitRepeatDelay(in runtime, ref state, currentStep);
                        break;
                    case OrderTerminalState.Failed:
                        state.CurrentTaskStatus = UtilityAiTaskRunStatus.Failed;
                        state.CurrentTaskFailureReason = outcome.FailureReason;
                        ResetFailedDecision(ref state);
                        break;
                    case OrderTerminalState.Cancelled:
                        state.CurrentTaskStatus = UtilityAiTaskRunStatus.Cancelled;
                        state.CurrentTaskFailureReason = outcome.FailureReason;
                        ResetFailedDecision(ref state);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"UTILITY.ORDER.ERR.UnknownTerminalState: orderId={outcome.OrderId}, state={(int)outcome.State}.");
                }

                UpdateTrace(World, actor, in state);
            }
        }

        private struct AdmissionJob : IForEachWithEntity<UtilityAiState, OrderBuffer>
        {
            private readonly World _world;
            private readonly UtilityAiCompiledRuntime _runtime;
            private readonly OrderAdmissionResultBuffer _admissionResults;
            private readonly int _currentStep;

            public AdmissionJob(
                World world,
                UtilityAiCompiledRuntime runtime,
                OrderAdmissionResultBuffer admissionResults,
                int currentStep)
            {
                _world = world;
                _runtime = runtime;
                _admissionResults = admissionResults;
                _currentStep = currentStep;
            }

            public void Update(
                Entity entity,
                ref UtilityAiState state,
                ref OrderBuffer buffer)
            {
                if (state.CurrentTaskStatus != UtilityAiTaskRunStatus.Submitted &&
                    state.CurrentTaskStatus != UtilityAiTaskRunStatus.Pending)
                {
                    return;
                }

                int orderId = state.LastSubmittedOrderId;
                if (orderId <= 0)
                {
                    throw new InvalidOperationException(
                        $"{MissingEntityAdmissionError}: actor={entity.Id}, orderId={orderId}.");
                }

                if (!_admissionResults.TryGet(
                        orderId,
                        OrderAdmissionStage.EntityIntake,
                        out OrderAdmissionOutcome outcome))
                {
                    if (state.CurrentTaskStatus == UtilityAiTaskRunStatus.Submitted)
                    {
                        throw new InvalidOperationException(
                            $"{MissingEntityAdmissionError}: actor={entity.Id}, orderId={orderId}, generation={_admissionResults.Generation}.");
                    }

                    return;
                }

                if (!OrderSubmitResultSemantics.IsAccepted(outcome.Result))
                {
                    state.CurrentTaskStatus = UtilityAiTaskRunStatus.Failed;
                    state.CurrentTaskFailureReason = OrderSubmitResultSemantics.ToFailureReason(outcome.Result);
                    ResetFailedDecision(ref state);
                    UpdateTrace(_world, entity, in state);
                    return;
                }

                if (outcome.Result == OrderSubmitResult.Pending)
                {
                    state.CurrentTaskStatus = UtilityAiTaskRunStatus.Pending;
                    state.CurrentTaskFailureReason = OrderFailureReason.None;
                    UpdateTrace(_world, entity, in state);
                    return;
                }

                state.CurrentTaskStatus = UtilityAiTaskRunStatus.Admitted;
                state.CurrentTaskFailureReason = OrderFailureReason.None;
                if (!KeepsRunning(in _runtime, in state))
                {
                    CommitRepeatDelay(in _runtime, ref state, _currentStep);
                }

                UpdateTrace(_world, entity, in state);
            }
        }

        private readonly struct TerminalLossJob : IForEachWithEntity<UtilityAiState, OrderBuffer>
        {
            private readonly UtilityAiCompiledRuntime _runtime;

            public TerminalLossJob(UtilityAiCompiledRuntime runtime)
            {
                _runtime = runtime;
            }

            public void Update(
                Entity entity,
                ref UtilityAiState state,
                ref OrderBuffer buffer)
            {
                if (!WaitsForTerminal(in _runtime, in state))
                {
                    return;
                }

                int orderId = state.LastSubmittedOrderId;
                if (OrderBufferOwns(in buffer, orderId))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"{MissingTerminalResultError}: actor={entity.Id}, orderId={orderId}.");
            }
        }

        private static bool KeepsRunning(
            in UtilityAiCompiledRuntime runtime,
            in UtilityAiState state)
        {
            if ((uint)state.CurrentDecisionId >= (uint)runtime.Decisions.Length)
            {
                throw new InvalidOperationException(
                    $"UTILITY.ORDER.ERR.InvalidCurrentDecision: decisionId={state.CurrentDecisionId}, orderId={state.LastSubmittedOrderId}.");
            }

            return runtime.Decisions[state.CurrentDecisionId].KeepRunningUntilFinished;
        }

        private static bool WaitsForTerminal(
            in UtilityAiCompiledRuntime runtime,
            in UtilityAiState state)
        {
            if (state.CurrentTaskStatus == UtilityAiTaskRunStatus.Pending)
            {
                return true;
            }

            if (state.CurrentTaskStatus != UtilityAiTaskRunStatus.Admitted)
            {
                return false;
            }

            return KeepsRunning(in runtime, in state);
        }

        private static bool OrderBufferOwns(in OrderBuffer buffer, int orderId)
        {
            if (orderId <= 0)
            {
                return false;
            }

            if (buffer.HasActive && buffer.ActiveOrder.Order.OrderId == orderId)
            {
                return true;
            }

            if (buffer.HasPending && buffer.PendingOrder.Order.OrderId == orderId)
            {
                return true;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                if (buffer.GetQueued(i).Order.OrderId == orderId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CommitRepeatDelay(
            in UtilityAiCompiledRuntime runtime,
            ref UtilityAiState state,
            int currentStep)
        {
            if ((uint)state.CurrentDecisionId >= (uint)runtime.Decisions.Length)
            {
                throw new InvalidOperationException(
                    $"UTILITY.ORDER.ERR.InvalidCurrentDecision: decisionId={state.CurrentDecisionId}, orderId={state.LastSubmittedOrderId}.");
            }

            ref readonly UtilityAiDecisionDefinition decision =
                ref runtime.Decisions[state.CurrentDecisionId];
            state.RepeatDelayDecisionId = state.CurrentDecisionId;
            state.DecisionRepeatDelayUntilStep =
                currentStep + decision.DecisionRepeatDelaySteps;
        }

        private static void ResetFailedDecision(ref UtilityAiState state)
        {
            state.CurrentDecisionId = -1;
            state.CurrentTarget = Entity.Null;
            state.CurrentScore = 0f;
            state.DecisionStartedStep = 0;
        }

        private static void UpdateTrace(
            World world,
            Entity entity,
            in UtilityAiState state)
        {
            if (!world.Has<UtilityAiDecisionTrace>(entity))
            {
                return;
            }

            ref UtilityAiDecisionTrace trace =
                ref world.Get<UtilityAiDecisionTrace>(entity);
            trace.LastSubmittedOrderId = state.LastSubmittedOrderId;
            trace.LastTaskStatus = (int)state.CurrentTaskStatus;
            trace.LastTaskFailureReason = (int)state.CurrentTaskFailureReason;
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
