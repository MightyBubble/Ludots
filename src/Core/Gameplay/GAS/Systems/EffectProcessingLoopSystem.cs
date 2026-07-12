using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Main processing loop stage: ProposalAndApply → Lifetime → PostLifetimeProposalAndApply → Done.
    /// </summary>
    public enum EffectLoopStage : byte
    {
        ProposalAndApply = 0,
        Lifetime = 1,
        PostLifetimeProposalAndApply = 2,
        Done = 3,
    }

    /// <summary>
    /// Sub-stage within a ProposalAndApply stage.
    /// </summary>
    public enum EffectLoopSubstage : byte
    {
        Proposal = 0,
        Application = 1,
    }

    public sealed class EffectProcessingLoopSystem : BaseSystem<World, float>, ITimeSlicedSystem
    {
        private readonly EffectRequestQueue _effectRequests;
        private readonly InputRequestQueue _inputRequests;
        private readonly OrderQueue _chainOrders;
        private readonly OrderRequestQueue _orderRequests;

        private readonly EffectProposalProcessingSystem _proposal;
        private readonly EffectApplicationSystem _application;
        private readonly EffectLifetimeSystem _lifetime;

        private EffectLoopStage _stage;
        private EffectLoopSubstage _substage;
        private int _pass;
        private bool _inSlice;
        private bool _hasPendingEffectsCached;

        private Entity _runtimeStateEntity;

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public byte DebugProposalWindowPhase => _proposal.DebugWindowPhase;

        public EffectProcessingLoopSystem(World world, EffectRequestQueue effectRequests, IClock clock, GasConditionRegistry conditions, int lifetimeSnapshotCapacity, GasBudget budget = null, EffectTemplateRegistry templates = null, InputRequestQueue inputRequests = null, OrderQueue chainOrders = null, ResponseChainTelemetryBuffer telemetry = null, OrderRequestQueue orderRequests = null, ResponseChainOrderTypes? responseChainOrderTypes = null, GasPresentationEventBuffer presentationEvents = null, ISpatialQueryService spatialQueries = null, RuntimeEntitySpawnQueue spawnRequests = null, RuntimeEntityLifecycleQueue lifecycleRequests = null, EntityLifecycleRuntimeServices lifecycleServices = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null, TagOps tagOps = null, ExchangeRuntime exchangeRuntime = null, ProgressionRequirementEvaluator progressionEvaluator = null, OrderTypeRegistry orderTypeRegistry = null, OrderRuleRegistry orderRuleRegistry = null, int stepRateHz = 30, RelationshipRuntime relationshipRuntime = null, KnowledgeAreaRevealRuntime knowledgeAreaRevealRuntime = null, int maxWorkUnitsPerSlice = int.MaxValue)
            : base(world)
        {
            _effectRequests = effectRequests;
            _inputRequests = inputRequests;
            _chainOrders = chainOrders;
            _orderRequests = orderRequests;

            var configuredResponseChainOrderTypes = ResponseChainOrderTypes.RequireConfigured(
                responseChainOrderTypes,
                nameof(EffectProcessingLoopSystem));
            _proposal = new EffectProposalProcessingSystem(
                world, effectRequests, budget, templates, inputRequests, chainOrders, telemetry, orderRequests,
                configuredResponseChainOrderTypes, presentationEvents, phaseExecutor, graphApi, tagOps,
                spatialQueries, spawnRequests, lifecycleRequests, lifecycleServices, exchangeRuntime,
                progressionEvaluator, orderTypeRegistry, orderRuleRegistry, clock, stepRateHz,
                relationshipRuntime, knowledgeAreaRevealRuntime);
            _application = new EffectApplicationSystem(world, effectRequests, budget, presentationEvents, templates, spatialQueries, spawnRequests, lifecycleRequests, lifecycleServices, phaseExecutor, graphApi, tagOps, exchangeRuntime, progressionEvaluator, orderTypeRegistry, orderRuleRegistry, clock, stepRateHz, relationshipRuntime, knowledgeAreaRevealRuntime);
            _lifetime = new EffectLifetimeSystem(world, clock, conditions, lifetimeSnapshotCapacity, effectRequests, budget, templates, spatialQueries, spawnRequests, lifecycleRequests, lifecycleServices, phaseExecutor, graphApi, tagOps, exchangeRuntime, progressionEvaluator, orderTypeRegistry, orderRuleRegistry, stepRateHz, relationshipRuntime, presentationEvents, knowledgeAreaRevealRuntime);
            MaxWorkUnitsPerSlice = maxWorkUnitsPerSlice;
            _runtimeStateEntity = world.Create(new GasRuntimeState
            {
                EffectLifetimeSnapshotCapacity = lifetimeSnapshotCapacity,
                EffectProcessingMaxWorkUnitsPerSlice = maxWorkUnitsPerSlice,
            });
        }

        public override void Update(in float dt)
        {
            while (!UpdateSlice(dt, int.MaxValue)) { }
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            if (!_inSlice)
            {
                _inSlice = true;
                _stage = EffectLoopStage.ProposalAndApply;
                _substage = EffectLoopSubstage.Proposal;
                _pass = 0;
                _hasPendingEffectsCached = HasFollowUpEffectRequests();
            }

            UpdateRuntimeState();

            if (timeBudgetMs <= 0) timeBudgetMs = 1;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = timeBudgetMs * (System.Diagnostics.Stopwatch.Frequency / 1000);

            _proposal.MaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice;
            _application.MaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice;
            _lifetime.MaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice;

            while (true)
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                if (elapsed >= budgetTicks)
                {
                    return false;
                }

                int remainingMs = (int)((budgetTicks - elapsed) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                if (remainingMs <= 0) remainingMs = 1;

                if (_stage == EffectLoopStage.ProposalAndApply)
                {
                    if (_substage == EffectLoopSubstage.Proposal)
                    {
                        if (!_proposal.UpdateSlice(dt, remainingMs)) return false;
                        _substage = EffectLoopSubstage.Application;
                        continue;
                    }

                    if (!_application.UpdateSlice(dt, remainingMs)) return false;
                    _substage = EffectLoopSubstage.Proposal;
                    _pass++;
                    _hasPendingEffectsCached = HasFollowUpEffectRequests();
                    if (!_hasPendingEffectsCached || _pass >= GasConstants.MAX_EFFECT_PROCESSING_PASSES_PER_FRAME)
                    {
                        _stage = EffectLoopStage.Lifetime;
                        _substage = EffectLoopSubstage.Proposal;
                        _pass = 0;
                    }
                    continue;
                }

                if (_stage == EffectLoopStage.Lifetime)
                {
                    elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                    if (elapsed >= budgetTicks) return false;
                    if (!_lifetime.UpdateSlice(dt, remainingMs)) return false;
                    _stage = EffectLoopStage.PostLifetimeProposalAndApply;
                    _substage = EffectLoopSubstage.Proposal;
                    _pass = 0;
                    continue;
                }

                if (_stage == EffectLoopStage.PostLifetimeProposalAndApply)
                {
                    if (_substage == EffectLoopSubstage.Proposal)
                    {
                        if (!_proposal.UpdateSlice(dt, remainingMs)) return false;
                        _substage = EffectLoopSubstage.Application;
                        continue;
                    }

                    if (!_application.UpdateSlice(dt, remainingMs)) return false;
                    _substage = EffectLoopSubstage.Proposal;
                    _pass++;
                    _hasPendingEffectsCached = HasFollowUpEffectRequests();
                    if (!_hasPendingEffectsCached || _pass >= GasConstants.MAX_EFFECT_PROCESSING_PASSES_PER_FRAME)
                    {
                        _stage = EffectLoopStage.Done;
                    }
                    continue;
                }

                _inSlice = false;
                _hasPendingEffectsCached = false;
                UpdateRuntimeState();
                return true;
            }
        }

        public void ResetSlice()
        {
            _inSlice = false;
            _stage = EffectLoopStage.ProposalAndApply;
            _substage = EffectLoopSubstage.Proposal;
            _pass = 0;
            _hasPendingEffectsCached = false;
            _proposal.ResetSlice();
            _application.ResetSlice();
            _lifetime.ResetSlice();
            UpdateRuntimeState();
        }

        private bool HasFollowUpEffectRequests()
        {
            return _effectRequests != null && _effectRequests.Count > 0;
        }

        private void UpdateRuntimeState()
        {
            if (!World.IsAlive(_runtimeStateEntity)) return;

            byte phase = _proposal.DebugWindowPhase;
            var state = new GasRuntimeState
            {
                EffectLoopInSlice = _inSlice,
                EffectLoopStage = (byte)_stage,
                EffectLoopSubstage = (byte)_substage,
                EffectLoopPass = _pass,
                HasPendingEffects = _hasPendingEffectsCached,

                ProposalWindowPhase = phase,
                ProposalWaitingInput = phase == 2,

                EffectRequestCount = _effectRequests?.Count ?? 0,
                InputRequestCount = _inputRequests?.Count ?? 0,
                ChainOrderCount = _chainOrders?.Count ?? 0,
                OrderRequestCount = _orderRequests?.Count ?? 0,

                EffectLifetimeProcessedLastSlice = _lifetime.LastSliceProcessed,
                EffectLifetimeDeferredCount = _lifetime.DeferredEntityCount,
                EffectLifetimeSnapshotCapacity = _lifetime.SnapshotCapacity,
                EffectProcessingMaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice,
            };

            World.Set(_runtimeStateEntity, state);
        }

    }
}
