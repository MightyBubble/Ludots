using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Spatial;

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

        private static readonly QueryDescription _pendingQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext>();

        private EffectLoopStage _stage;
        private EffectLoopSubstage _substage;
        private int _pass;
        private bool _inSlice;

        private Entity _runtimeStateEntity;

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public byte DebugProposalWindowPhase => _proposal.DebugWindowPhase;

        public EffectProcessingLoopSystem(World world, EffectRequestQueue effectRequests, IClock clock, GasConditionRegistry conditions, GasBudget budget = null, EffectTemplateRegistry templates = null, InputRequestQueue inputRequests = null, OrderQueue chainOrders = null, ResponseChainTelemetryBuffer telemetry = null, OrderRequestQueue orderRequests = null, GasChainOrderTags? chainTags = null, GasPresentationEventBuffer presentationEvents = null, ISpatialQueryService spatialQueries = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null, TagOps tagOps = null)
            : base(world)
        {
            _effectRequests = effectRequests;
            _inputRequests = inputRequests;
            _chainOrders = chainOrders;
            _orderRequests = orderRequests;

            _proposal = new EffectProposalProcessingSystem(world, effectRequests, budget, templates, inputRequests, chainOrders, telemetry, orderRequests, chainTags, presentationEvents, phaseExecutor, graphApi);
            _application = new EffectApplicationSystem(world, effectRequests, budget, presentationEvents, templates, spatialQueries, phaseExecutor, graphApi);
            _lifetime = new EffectLifetimeSystem(world, clock, conditions, effectRequests, budget, templates, spatialQueries, phaseExecutor, graphApi, tagOps);
            _runtimeStateEntity = world.Create(new GasRuntimeState());
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
            }

            UpdateRuntimeState();

            if (timeBudgetMs <= 0) timeBudgetMs = 1;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = timeBudgetMs * (System.Diagnostics.Stopwatch.Frequency / 1000);

            _proposal.MaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice;
            _application.MaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice;

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
                    if (!HasPendingEffects() || _pass >= GasConstants.MAX_EFFECT_PROCESSING_PASSES_PER_FRAME)
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
                    _lifetime.Update(dt);
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
                    if (!HasPendingEffects() || _pass >= GasConstants.MAX_EFFECT_PROCESSING_PASSES_PER_FRAME)
                    {
                        _stage = EffectLoopStage.Done;
                    }
                    continue;
                }

                _inSlice = false;
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
            _proposal.ResetSlice();
            _application.ResetSlice();
            UpdateRuntimeState();
        }

        private bool HasPendingEffects()
        {
            var job = new AnyPendingEffectJob();
            World.InlineEntityQuery<AnyPendingEffectJob, GameplayEffect>(in _pendingQuery, ref job);
            return job.Found;
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
                HasPendingEffects = HasPendingEffects(),

                ProposalWindowPhase = phase,
                ProposalWaitingInput = phase == 2,

                EffectRequestCount = _effectRequests?.Count ?? 0,
                InputRequestCount = _inputRequests?.Count ?? 0,
                ChainOrderCount = _chainOrders?.Count ?? 0,
                OrderRequestCount = _orderRequests?.Count ?? 0
            };

            World.Set(_runtimeStateEntity, state);
        }

        private struct AnyPendingEffectJob : IForEachWithEntity<GameplayEffect>
        {
            public bool Found;

            public void Update(Entity _, ref GameplayEffect effect)
            {
                if (effect.State == EffectState.Pending)
                    Found = true;
            }
        }
    }
}
