using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class EffectApplicationSystem : BaseSystem<World, float>
        , ITimeSlicedSystem
    {
        public const string ActiveEffectContainerCapacityExceededError = "GAS.ACTIVE_EFFECT_CONTAINER.ERR.CapacityExceeded";
        public const string PhaseListenerRegistrationCapacityExceededError = "GAS.PHASE_LISTENER.ERR.RegistrationCapacityExceeded";

        private static readonly QueryDescription _pendingEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext, EffectModifiers>();

        // Reusable lists for deferred structural changes
        private readonly List<Entity> _effectsToDestroy;
        private readonly List<Entity> _effectsToActivate;
        private readonly List<PendingAttach> _pendingAttach;
        private readonly List<PendingCreateContainer> _pendingCreateContainer;
        private readonly List<Entity> _createdContainers;

        private struct PendingEffectEntry
        {
            public Entity Effect;
            public int ResolveOrder;
        }

        private sealed class PendingEffectEntryComparer : IComparer<PendingEffectEntry>
        {
            public static readonly PendingEffectEntryComparer Instance = new PendingEffectEntryComparer();

            public int Compare(PendingEffectEntry x, PendingEffectEntry y)
            {
                int c = x.ResolveOrder.CompareTo(y.ResolveOrder);
                if (c != 0) return c;

                c = x.Effect.WorldId.CompareTo(y.Effect.WorldId);
                if (c != 0) return c;

                c = x.Effect.Id.CompareTo(y.Effect.Id);
                if (c != 0) return c;

                return x.Effect.Version.CompareTo(y.Effect.Version);
            }
        }

        private readonly List<PendingEffectEntry> _pendingEffects;

        // ── TargetResolver fan-out (shared types from TargetResolverFanOutHelper) ──
        private readonly FanOutCommandBuffer _fanOutCommands;
        private readonly RootBudgetTable _fanOutBudget;
        // An injected budget is advanced by the effect-loop owner once per processing transaction.
        private readonly bool _ownsFanOutBudget;
        private readonly Entity[] _resolverBuffer = new Entity[256];
        private readonly BuiltinHandlerExecutionContext _builtinRuntime = new BuiltinHandlerExecutionContext();
        private readonly EffectPhaseSideEffectTransaction _persistentPhaseTransaction;
        private int _activeEffectAttachDropped;
        private int _listenerRegistrationDropped;

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public int LastSliceProcessed { get; private set; }

        /// <summary>
        /// Time-sliced application stages. ProcessPending through AttachEffects
        /// commit a visible attachment; ActivateEffects is the settlement transaction.
        /// </summary>
        private enum ApplicationStage : byte
        {
            ProcessPending = 0,
            DestroyEffects = 1,
            CreateContainers = 2,
            AttachEffects = 3,
            ActivateEffects = 4,
            FanOutTargets = 5,
            RegisterListeners = 6,
            Done = 7,
        }

        private bool _sliceActive;
        private ApplicationStage _sliceStage;
        private int _cursor;
        private int _playbackCursor;

        private readonly EffectRequestQueue _effectRequests;
        private readonly GasBudget _budget;
        private readonly GasPresentationEventBuffer _presentationEvents;
        private readonly EffectTemplateRegistry _templates;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly TagOps _tagOps;

        // ── Phase Graph execution (optional) ──
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi _graphApiHost;
        private readonly OrderTypeRegistry? _orderTypeRegistry;
        private readonly OrderRuleRegistry? _orderRuleRegistry;
        private readonly IClock _clock;
        private readonly int _stepRateHz;

        public EffectApplicationSystem(World world, int fanOutCommandCapacity, IClock clock, EffectRequestQueue effectRequests = null, GasBudget budget = null, GasPresentationEventBuffer presentationEvents = null, EffectTemplateRegistry templates = null, ISpatialQueryService spatialQueries = null, RuntimeEntitySpawnQueue spawnRequests = null, RuntimeEntityLifecycleQueue lifecycleRequests = null, EntityLifecycleRuntimeServices lifecycleServices = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null, TagOps tagOps = null, ExchangeRuntime exchangeRuntime = null, ProgressionRequirementEvaluator progressionEvaluator = null, OrderTypeRegistry orderTypeRegistry = null, OrderRuleRegistry orderRuleRegistry = null, int stepRateHz = 30, RelationshipRuntime relationshipRuntime = null, KnowledgeAreaRevealRuntime knowledgeAreaRevealRuntime = null, OrderQueue orderIntake = null, RootBudgetTable fanOutBudget = null) : base(world)
        {
            _fanOutCommands = new FanOutCommandBuffer(fanOutCommandCapacity);
            _fanOutBudget = fanOutBudget ?? new RootBudgetTable(fanOutCommandCapacity);
            _ownsFanOutBudget = fanOutBudget == null;
            int fixedScratchCapacity = Math.Max(1, fanOutCommandCapacity);
            _effectsToDestroy = new List<Entity>(fixedScratchCapacity);
            _effectsToActivate = new List<Entity>(fixedScratchCapacity);
            _pendingAttach = new List<PendingAttach>(fixedScratchCapacity);
            _pendingCreateContainer = new List<PendingCreateContainer>(fixedScratchCapacity);
            _createdContainers = new List<Entity>(fixedScratchCapacity);
            _pendingEffects = new List<PendingEffectEntry>(fixedScratchCapacity);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _effectRequests = effectRequests;
            _budget = budget;
            _presentationEvents = presentationEvents;
            _templates = templates;
            _spatialQueries = spatialQueries;
            _tagOps = tagOps;
            _phaseExecutor = phaseExecutor;
            _graphApiHost = graphApi;
            _graphApi = graphApi;
            _builtinRuntime.SpatialQueries = spatialQueries;
            _builtinRuntime.FanOutBudget = _fanOutBudget;
            _builtinRuntime.FanOutCommands = _fanOutCommands;
            _builtinRuntime.ResolverBuffer = _resolverBuffer;
            _builtinRuntime.SpawnRequests = spawnRequests;
            _builtinRuntime.LifecycleRequests = lifecycleRequests;
            _builtinRuntime.LifecycleServices = lifecycleServices;
            _builtinRuntime.Exchange = exchangeRuntime;
            _builtinRuntime.Relationships = relationshipRuntime;
            _builtinRuntime.ProgressionEvaluator = progressionEvaluator;
            _builtinRuntime.KnowledgeAreaReveal = knowledgeAreaRevealRuntime;
            _builtinRuntime.TagOps = _tagOps;
            _builtinRuntime.OrderIntake = orderIntake;
            _orderTypeRegistry = orderTypeRegistry;
            _orderRuleRegistry = orderRuleRegistry;
            _stepRateHz = GasStepRate.RequirePositive(stepRateHz, nameof(EffectApplicationSystem));
            _persistentPhaseTransaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps,
                effectRequests,
                spawnRequests,
                presentationEvents,
                _resolverBuffer.Length,
                _fanOutBudget);
        }

        private void RefreshBuiltinOrderContext()
        {
            _builtinRuntime.OrderTypeRegistry = _orderTypeRegistry;
            _builtinRuntime.OrderRuleRegistry = _orderRuleRegistry;
            _builtinRuntime.StepRateHz = _stepRateHz;
            _builtinRuntime.CurrentStep = _clock.Now(ClockDomainId.Step);
        }

        public override void Update(in float dt)
        {
            int prev = MaxWorkUnitsPerSlice;
            MaxWorkUnitsPerSlice = int.MaxValue;
            while (!UpdateSlice(dt, int.MaxValue)) { }
            MaxWorkUnitsPerSlice = prev;
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            _templates?.RequireFinalized();
            LastSliceProcessed = 0;
            if (!_sliceActive)
            {
                _sliceActive = true;
                _sliceStage = ApplicationStage.ProcessPending;
                _cursor = 0;
                _playbackCursor = 0;

                RefreshBuiltinOrderContext();

                _effectsToDestroy.Clear();
                _effectsToActivate.Clear();
                _pendingAttach.Clear();
                _pendingCreateContainer.Clear();
                _createdContainers.Clear();
                _fanOutCommands.Clear();
                _pendingEffects.Clear();
                _pendingListenerRegistrations.Clear();
                if (_ownsFanOutBudget)
                {
                    _fanOutBudget.NextFrame();
                }
                _activeEffectAttachDropped = 0;
                _listenerRegistrationDropped = 0;

                var collectJob = new CollectPendingEffectsJob { World = World, PendingEffects = _pendingEffects };
                World.InlineEntityQuery<CollectPendingEffectsJob, GameplayEffect>(in _pendingEffectsQuery, ref collectJob);

                if (_pendingEffects.Count > 1)
                {
                    _pendingEffects.Sort(PendingEffectEntryComparer.Instance);
                }
            }

            int workUnits = 0;
            while (true)
            {
                if (workUnits >= MaxWorkUnitsPerSlice)
                {
                    return false;
                }

                if (_sliceStage == ApplicationStage.ProcessPending)
                {
                    while (_cursor < _pendingEffects.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }

                        var effectEntity = _pendingEffects[_cursor].Effect;
                        _cursor++;
                        if (!World.IsAlive(effectEntity)) continue;
                        ref var effect = ref World.Get<GameplayEffect>(effectEntity);
                        if (effect.LifetimeKind == EffectLifetimeKind.Instant)
                        {
                            throw new InvalidOperationException(
                                $"GAS.INSTANT.ERR.EntityRuntimeForbidden: effect={effectEntity.Id}.");
                        }

                        if (World.Has<EffectCancelled>(effectEntity) || effect.CancelRequested)
                        {
                            effect.State = EffectState.Committed;
                            AddFixed(_effectsToDestroy, effectEntity, nameof(_effectsToDestroy));
                            ConsumeWork(ref workUnits);
                            continue;
                        }

                        ref var context = ref World.Get<EffectContext>(effectEntity);

                        if (effect.State == EffectState.Created)
                        {
                            effect.State = EffectState.Pending;
                        }

                        effect.State = EffectState.Calculate;
                        effect.State = EffectState.Apply;

                        if (World.IsAlive(context.Target))
                        {
                            if (World.Has<ActiveEffectContainer>(context.Target))
                            {
                                ref var container = ref World.Get<ActiveEffectContainer>(context.Target);
                                if (!container.Add(effectEntity))
                                {
                                    throw CreateActiveEffectContainerCapacityExceeded(context.Target, effectEntity);
                                }

                                AddFixed(_effectsToActivate, effectEntity, nameof(_effectsToActivate));
                            }
                            else
                            {
                                AddFixed(_pendingCreateContainer, new PendingCreateContainer { Target = context.Target }, nameof(_pendingCreateContainer));
                                AddFixed(_pendingAttach, new PendingAttach { Target = context.Target, Effect = effectEntity }, nameof(_pendingAttach));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"GAS.ACTIVE_EFFECT_CONTAINER.ERR.TargetUnavailable: target={context.Target.Id}, effect={effectEntity.Id}.");
                        }

                        ConsumeWork(ref workUnits);
                    }

                    _sliceStage = ApplicationStage.DestroyEffects;
                    _playbackCursor = 0;
                    continue;
                }

                if (_sliceStage == ApplicationStage.DestroyEffects)
                {
                    while (_playbackCursor < _effectsToDestroy.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }

                        var e = _effectsToDestroy[_playbackCursor++];
                        if (World.IsAlive(e) && World.Has<EffectContext>(e))
                        {
                            ref var context = ref World.Get<EffectContext>(e);
                        }
                        if (World.IsAlive(e)) World.Destroy(e);
                        ConsumeWork(ref workUnits);
                    }
                    _sliceStage = ApplicationStage.CreateContainers;
                    _playbackCursor = 0;
                    continue;
                }

                if (_sliceStage == ApplicationStage.CreateContainers)
                {
                    while (_playbackCursor < _pendingCreateContainer.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }
                        var target = _pendingCreateContainer[_playbackCursor++].Target;
                        if (World.IsAlive(target) && !World.Has<ActiveEffectContainer>(target))
                        {
                            World.Add(target, new ActiveEffectContainer());
                            AddFixed(_createdContainers, target, nameof(_createdContainers));
                        }
                        ConsumeWork(ref workUnits);
                    }
                    _sliceStage = ApplicationStage.AttachEffects;
                    _playbackCursor = 0;
                    continue;
                }

                if (_sliceStage == ApplicationStage.AttachEffects)
                {
                    while (_playbackCursor < _pendingAttach.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }
                        var item = _pendingAttach[_playbackCursor++];
                        if (!World.IsAlive(item.Target))
                        {
                            throw new InvalidOperationException(
                                $"GAS.ACTIVE_EFFECT_CONTAINER.ERR.TargetUnavailable: target={item.Target.Id}, effect={item.Effect.Id}.");
                        }
                        if (!World.IsAlive(item.Effect)) { ConsumeWork(ref workUnits); continue; }
                        if (World.Has<ActiveEffectContainer>(item.Target))
                        {
                            ref var container = ref World.Get<ActiveEffectContainer>(item.Target);
                            if (!container.Add(item.Effect))
                            {
                                throw CreateActiveEffectContainerCapacityExceeded(item.Target, item.Effect);
                            }

                            AddFixed(_effectsToActivate, item.Effect, nameof(_effectsToActivate));
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"GAS.ACTIVE_EFFECT_CONTAINER.ERR.MissingContainerAfterCreate: target={item.Target.Id}, effect={item.Effect.Id}.");
                        }
                        ConsumeWork(ref workUnits);
                    }
                    _sliceStage = ApplicationStage.ActivateEffects;
                    _playbackCursor = 0;
                    continue;
                }

                if (_sliceStage == ApplicationStage.ActivateEffects)
                {
                    while (_playbackCursor < _effectsToActivate.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }
                        var e = _effectsToActivate[_playbackCursor++];
                        if (World.IsAlive(e) && World.Has<GameplayEffect>(e) && World.Has<EffectContext>(e))
                        {
                            EffectContext context = World.Get<EffectContext>(e);
                            if (!IsEffectAttached(context.Target, e))
                            {
                                throw new InvalidOperationException(
                                    $"GAS.ACTIVE_EFFECT_CONTAINER.ERR.MissingAttachment: target={context.Target.Id}, effect={e.Id}.");
                            }

                            int templateId = World.Has<EffectTemplateRef>(e)
                                ? World.Get<EffectTemplateRef>(e).TemplateId
                                : 0;

                            int fanOutCommandCountBefore = _fanOutCommands.Count;
                            int listenerRegistrationCountBefore = _pendingListenerRegistrations.Count;
                            bool graphTransactionBound = false;
                            _persistentPhaseTransaction.Begin();
                            _builtinRuntime.EffectSideEffects = _persistentPhaseTransaction;
                            try
                            {
                                if (_graphApiHost != null)
                                {
                                    _graphApiHost.BeginEffectSideEffectTransaction(_persistentPhaseTransaction);
                                    graphTransactionBound = true;
                                }

                                if (World.Has<EffectGrantedTags>(e))
                                {
                                    EffectGrantedTags grantedTags = World.Get<EffectGrantedTags>(e);
                                    int stackCount = World.Has<EffectStack>(e) ? World.Get<EffectStack>(e).Count : 1;
                                    _persistentPhaseTransaction.StageGrantedTagGrant(context.Target, in grantedTags, stackCount);
                                }

                                ExecutePersistentPhases(e, in context, templateId);

                                for (int commandIndex = fanOutCommandCountBefore; commandIndex < _fanOutCommands.Count; commandIndex++)
                                {
                                    FanOutCommand command = _fanOutCommands[commandIndex];
                                    _persistentPhaseTransaction.StageFanOutCommand(in command);
                                }
                                _fanOutCommands.Truncate(fanOutCommandCountBefore);

                                MarkAggregateDirtyIfNeeded(context.Target, e);

                                if (_presentationEvents != null && templateId > 0)
                                {
                                    _persistentPhaseTransaction.StagePresentationEvent(new GasPresentationEvent
                                    {
                                        Kind = GasPresentationEventKind.EffectActivated,
                                        Actor = context.Source,
                                        Target = context.Target,
                                        EffectTemplateId = templateId
                                    });
                                }

                                _persistentPhaseTransaction.Commit();
                            }
                            catch
                            {
                                _persistentPhaseTransaction.Rollback();
                                _fanOutCommands.Truncate(fanOutCommandCountBefore);
                                TrimTail(_pendingListenerRegistrations, listenerRegistrationCountBefore);
                                throw;
                            }
                            finally
                            {
                                if (graphTransactionBound)
                                {
                                    _graphApiHost!.EndEffectSideEffectTransaction(_persistentPhaseTransaction);
                                }
                                _builtinRuntime.EffectSideEffects = null;
                            }

                            if (World.IsAlive(e) && World.Has<GameplayEffect>(e))
                            {
                                ref GameplayEffect effectForActivate = ref World.Get<GameplayEffect>(e);
                                effectForActivate.State = EffectState.Committed;
                            }

                        }
                        ConsumeWork(ref workUnits);
                    }
                    _sliceStage = ApplicationStage.FanOutTargets;
                    _playbackCursor = 0;
                    continue;
                }

                // Fan-out: publish TargetResolver fan-out EffectRequests (time-sliced)
                if (_sliceStage == ApplicationStage.FanOutTargets)
                {
                    while (_playbackCursor < _fanOutCommands.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }
                        var cmd = _fanOutCommands[_playbackCursor++];
                        if (_effectRequests != null)
                        {
                            TargetResolverFanOutHelper.PublishCommand(in cmd, _effectRequests);
                        }
                        ConsumeWork(ref workUnits);
                    }
                    _sliceStage = ApplicationStage.RegisterListeners;
                    _playbackCursor = 0;
                    continue;
                }

                // Replay deferred phase listener registrations (structural changes)
                if (_sliceStage == ApplicationStage.RegisterListeners)
                {
                    while (_playbackCursor < _pendingListenerRegistrations.Count)
                    {
                        if (workUnits >= MaxWorkUnitsPerSlice)
                        {
                            return false;
                        }
                        var reg = _pendingListenerRegistrations[_playbackCursor++];
                        if (reg.TemplateId > 0 && _templates != null && _templates.TryGetRef(reg.TemplateId, out int tplIdx))
                        {
                            ref readonly var tplData = ref _templates.GetRef(tplIdx);
                            RegisterListenersFromTemplate(in reg.Context, in tplData, reg.OwnerEffectId);
                        }
                        ConsumeWork(ref workUnits);
                    }
                    _sliceStage = ApplicationStage.Done;
                    _playbackCursor = 0;
                    continue;
                }

                if (_budget != null)
                {
                    if (_activeEffectAttachDropped > 0)
                    {
                        _budget.ActiveEffectContainerAttachDropped += _activeEffectAttachDropped;
                    }
                    if (_listenerRegistrationDropped > 0)
                    {
                        _budget.PhaseListenerRegistrationDropped += _listenerRegistrationDropped;
                    }
                }
                _sliceActive = false;
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkAggregateDirtyIfNeeded(Entity target, Entity effect)
        {
            if (!World.IsAlive(target) || !World.IsAlive(effect))
            {
                return;
            }

            if (!World.Has<GameplayEffect>(effect) || !World.Get<GameplayEffect>(effect).AggregatesModifiers)
            {
                return;
            }

            if (_persistentPhaseTransaction.IsActive)
            {
                _persistentPhaseTransaction.StageAggregateDirty(target);
            }
            else if (!World.Has<AttributeAggregateDirty>(target))
            {
                World.Add(target, new AttributeAggregateDirty());
            }
        }

        private void ConsumeWork(ref int workUnits)
        {
            workUnits++;
            LastSliceProcessed++;
        }

        public void ResetSlice()
        {
            if (_sliceActive)
            {
                RollbackUncommittedApplicationWork();
            }

            _sliceActive = false;
            _sliceStage = ApplicationStage.ProcessPending;
            _cursor = 0;
            _playbackCursor = 0;
            _pendingEffects.Clear();
            _effectsToDestroy.Clear();
            _effectsToActivate.Clear();
            _pendingAttach.Clear();
            _pendingCreateContainer.Clear();
            _createdContainers.Clear();
            _fanOutCommands.Clear();
            _pendingListenerRegistrations.Clear();
            _activeEffectAttachDropped = 0;
            _listenerRegistrationDropped = 0;
        }

        public override void Dispose()
        {
            _persistentPhaseTransaction.Dispose();
            base.Dispose();
        }

        private void RollbackUncommittedApplicationWork()
        {
            for (int i = 0; i < _effectsToActivate.Count; i++)
            {
                Entity effect = _effectsToActivate[i];
                if (!World.IsAlive(effect) || !World.Has<GameplayEffect>(effect))
                {
                    continue;
                }
                if (World.Get<GameplayEffect>(effect).State == EffectState.Committed)
                {
                    continue;
                }

                if (World.Has<EffectContext>(effect))
                {
                    EffectContext context = World.Get<EffectContext>(effect);
                    if (World.IsAlive(context.Target) && World.Has<ActiveEffectContainer>(context.Target))
                    {
                        ref ActiveEffectContainer container = ref World.Get<ActiveEffectContainer>(context.Target);
                        container.Remove(effect);
                    }
                }
                World.Destroy(effect);
            }

            for (int i = 0; i < _pendingAttach.Count; i++)
            {
                Entity effect = _pendingAttach[i].Effect;
                if (World.IsAlive(effect) &&
                    (!World.Has<GameplayEffect>(effect) || World.Get<GameplayEffect>(effect).State != EffectState.Committed))
                {
                    World.Destroy(effect);
                }
            }

            for (int i = 0; i < _effectsToDestroy.Count; i++)
            {
                Entity effect = _effectsToDestroy[i];
                if (World.IsAlive(effect))
                {
                    World.Destroy(effect);
                }
            }

            for (int i = 0; i < _createdContainers.Count; i++)
            {
                Entity target = _createdContainers[i];
                if (World.IsAlive(target) &&
                    World.Has<ActiveEffectContainer>(target) &&
                    World.Get<ActiveEffectContainer>(target).Count == 0)
                {
                    World.Remove<ActiveEffectContainer>(target);
                }
            }
        }

        private struct CollectPendingEffectsJob : IForEachWithEntity<GameplayEffect>
        {
            public World World;
            public List<PendingEffectEntry> PendingEffects;

            public void Update(Entity effectEntity, ref GameplayEffect effect)
            {
                if (effect.State != EffectState.Pending) return;
                int order = 0;
                if (World.Has<EffectResolveOrder>(effectEntity))
                {
                    order = World.Get<EffectResolveOrder>(effectEntity).Value;
                }
                AddFixed(PendingEffects, new PendingEffectEntry { Effect = effectEntity, ResolveOrder = order }, nameof(PendingEffects));
            }
        }

        private struct PendingAttach
        {
            public Entity Target;
            public Entity Effect;
        }

        private struct PendingCreateContainer
        {
            public Entity Target;
        }

        /// <summary>
        /// Deferred listener registration command. Structural change (World.Add) is replayed in Stage 6.
        /// </summary>
        private struct PendingListenerRegistration
        {
            public Components.EffectContext Context;
            public int TemplateId;
            public int OwnerEffectId;
        }

        private readonly List<PendingListenerRegistration> _pendingListenerRegistrations = new(32);

        private void ExecutePersistentPhases(Entity effectEntity, in EffectContext context, int templateId)
        {
            if (_templates == null || templateId <= 0 || !_templates.TryGetRef(templateId, out int tplIdx))
            {
                return;
            }

            ref readonly EffectTemplateData tplData = ref _templates.GetRef(tplIdx);
            EffectModifiers modifiers = World.Get<EffectModifiers>(effectEntity);
            _builtinRuntime.ResetPerEffect();
            _builtinRuntime.SetModifierOverride(in modifiers);

            ExecutePhaseForEffect(effectEntity, in context, in tplData, EffectPhaseId.OnResolve, _builtinRuntime);
            ExecutePhaseForEffect(effectEntity, in context, in tplData, EffectPhaseId.OnHit, _builtinRuntime);
            ExecutePhaseForEffect(effectEntity, in context, in tplData, EffectPhaseId.OnApply, _builtinRuntime);

            PublishBuiltinAttributeDelta(in context, templateId, _builtinRuntime);
        }

        private bool IsEffectAttached(Entity target, Entity effect)
        {
            if (!World.IsAlive(target) || !World.Has<ActiveEffectContainer>(target))
            {
                return false;
            }

            ref ActiveEffectContainer container = ref World.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < container.Count; i++)
            {
                if (container.GetEntity(i) == effect)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Execute a phase graph for an effect entity, reading its template for behavior and config.
        /// Passes effectTagId and effectTemplateId for Phase Listener matching.
        /// </summary>
        private void ExecutePhaseForEffect(Entity effectEntity, in EffectContext context, in EffectTemplateData tpl, EffectPhaseId phase, BuiltinHandlerExecutionContext? builtinRuntime = null)
        {
            if (_phaseExecutor == null || _graphApi == null) return;

            // Determine template id for listener matching
            int templateId = 0;
            if (World.IsAlive(effectEntity) && World.Has<EffectTemplateRef>(effectEntity))
                templateId = World.Get<EffectTemplateRef>(effectEntity).TemplateId;

            // Build merged config: template params + caller overrides
            var mergedConfig = ConfigParamsMerger.BuildMergedConfig(World, effectEntity, in tpl.ConfigParams);

            IntVector2 targetPos = PlacementPhaseTargetPosResolver.Resolve(World, in context, in mergedConfig);
            _phaseExecutor.ExecutePhase(
                World, _graphApi,
                context.Source, context.Target, context.TargetContext,
                targetPos,
                phase,
                in tpl.PhaseGraphBindings,
                tpl.PresetType,
                tpl.TagId,
                templateId,
                in mergedConfig,
                builtinRuntime,
                BuildExecutionSeed(effectEntity, phase, templateId, context),
                context.RootId);

            // Defer phase listener registration to Stage 6 (structural change safety)
            if (phase == EffectPhaseId.OnApply && tpl.ListenerSetup.Count > 0)
            {
                if (_persistentPhaseTransaction.IsActive)
                {
                    _persistentPhaseTransaction.StageListenerRegistration(
                        in context,
                        in tpl.ListenerSetup,
                        effectEntity.Id);
                }
                else
                {
                    AddFixed(
                        _pendingListenerRegistrations,
                        new PendingListenerRegistration
                        {
                            Context = context,
                            TemplateId = templateId,
                            OwnerEffectId = effectEntity.Id,
                        },
                        nameof(_pendingListenerRegistrations));
                }
            }
        }

        private static void AddFixed<T>(List<T> list, T item, string name)
        {
            if (list.Count >= list.Capacity)
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_APPLICATION.ERR.FixedListCapacityExceeded: list={name}, capacity={list.Capacity}.");
            }

            list.Add(item);
        }

        private void PublishBuiltinAttributeDelta(
            in EffectContext context,
            int templateId,
            BuiltinHandlerExecutionContext runtime)
        {
            if (_presentationEvents == null || !runtime.HasAttributeDelta)
            {
                return;
            }

            var presentationEvent = new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectApplied,
                Actor = context.Source,
                Target = context.Target,
                EffectTemplateId = templateId,
                AttributeId = runtime.AttributeDeltaId,
                Delta = runtime.AttributeDelta
            };
            if (_persistentPhaseTransaction.IsActive)
            {
                _persistentPhaseTransaction.StagePresentationEvent(in presentationEvent);
            }
            else
            {
                _presentationEvents.Publish(in presentationEvent);
            }
        }

        /// <summary>
        /// Register effect-bound phase listeners from the template's ListenerSetup.
        /// Called during the OnApply phase.
        /// </summary>
        private unsafe void RegisterListenersFromTemplate(in EffectContext context, in EffectTemplateData tpl, int ownerEffectId)
        {
            ref readonly var setup = ref tpl.ListenerSetup;
            if (setup.Count == 0) return;

            ValidateListenerRegistrationCapacity(in context, in setup);

            for (int i = 0; i < setup.Count; i++)
            {
                var scope = (PhaseListenerScope)setup.Scopes[i];
                Entity entity = scope == PhaseListenerScope.Target ? context.Target : context.Source;
                if (!World.IsAlive(entity)) continue;

                if (!World.Has<EffectPhaseListenerBuffer>(entity))
                    World.Add(entity, new EffectPhaseListenerBuffer());

                ref var buf = ref World.Get<EffectPhaseListenerBuffer>(entity);
                if (!buf.TryAdd(
                    setup.ListenTagIds[i],
                    setup.ListenEffectIds[i],
                    (EffectPhaseId)setup.Phases[i],
                    scope,
                    (PhaseListenerActionFlags)setup.ActionFlags[i],
                    setup.GraphProgramIds[i],
                    setup.EventTagIds[i],
                    setup.Priorities[i],
                    ownerEffectId))
                {
                    throw CreatePhaseListenerRegistrationCapacityExceeded(entity, setup.Count, 0);
                }
            }
        }

        private unsafe void ValidateListenerRegistrationCapacity(
            in EffectContext context,
            in EffectPhaseListenerBuffer setup)
        {
            int targetAdds = 0;
            int sourceAdds = 0;
            for (int i = 0; i < setup.Count; i++)
            {
                var scope = (PhaseListenerScope)setup.Scopes[i];
                if (scope == PhaseListenerScope.Target)
                {
                    targetAdds++;
                }
                else
                {
                    sourceAdds++;
                }
            }

            if (context.Target.Equals(context.Source))
            {
                ValidateListenerCapacityForEntity(context.Target, targetAdds + sourceAdds);
                return;
            }

            ValidateListenerCapacityForEntity(context.Target, targetAdds);
            ValidateListenerCapacityForEntity(context.Source, sourceAdds);
        }

        private void ValidateListenerCapacityForEntity(Entity entity, int additionalCount)
        {
            if (additionalCount <= 0 || !World.IsAlive(entity))
            {
                return;
            }

            int existingCount = World.Has<EffectPhaseListenerBuffer>(entity)
                ? World.Get<EffectPhaseListenerBuffer>(entity).Count
                : 0;
            int available = EffectPhaseListenerBuffer.CAPACITY - existingCount;
            if (additionalCount > available)
            {
                throw CreatePhaseListenerRegistrationCapacityExceeded(entity, additionalCount, available);
            }
        }

        private static InvalidOperationException CreateActiveEffectContainerCapacityExceeded(Entity target, Entity effect)
        {
            return new InvalidOperationException(
                $"{ActiveEffectContainerCapacityExceededError}: target={target.Id}, effect={effect.Id}, capacity={ActiveEffectContainer.CAPACITY}.");
        }

        private static InvalidOperationException CreatePhaseListenerRegistrationCapacityExceeded(
            Entity entity,
            int requested,
            int available)
        {
            return new InvalidOperationException(
                $"{PhaseListenerRegistrationCapacityExceededError}: entity={entity.Id}, requested={requested}, available={available}, capacity={EffectPhaseListenerBuffer.CAPACITY}.");
        }

        private static uint BuildExecutionSeed(Entity effectEntity, EffectPhaseId phase, int templateId, in EffectContext context)
        {
            uint hash = 2166136261u;
            hash = Mix(hash, effectEntity.Id);
            hash = Mix(hash, effectEntity.Version);
            hash = Mix(hash, context.Source.Id);
            hash = Mix(hash, context.Target.Id);
            hash = Mix(hash, context.TargetContext.Id);
            hash = Mix(hash, templateId);
            hash = Mix(hash, (int)phase);
            return hash == 0u ? 1u : hash;
        }

        private static uint Mix(uint hash, int value)
        {
            return (hash ^ unchecked((uint)value)) * 16777619u;
        }

        private static void TrimTail<T>(List<T> items, int retainedCount)
        {
            if (items.Count > retainedCount)
            {
                items.RemoveRange(retainedCount, items.Count - retainedCount);
            }
        }

    }
}
