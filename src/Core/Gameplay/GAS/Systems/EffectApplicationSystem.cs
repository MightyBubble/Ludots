using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Mathematics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class EffectApplicationSystem : BaseSystem<World, float>
        , ITimeSlicedSystem
    {
        private static readonly QueryDescription _pendingEffectsQuery = new QueryDescription()
            .WithAll<GameplayEffect, EffectContext, EffectModifiers>();

        // Reusable lists for deferred structural changes
        private readonly List<Entity> _effectsToDestroy = new(1024);
        private readonly List<Entity> _effectsToActivate = new(1024);
        private readonly List<PendingAttach> _pendingAttach = new(1024);
        private readonly List<PendingCreateContainer> _pendingCreateContainer = new(256);

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

        private readonly List<PendingEffectEntry> _pendingEffects = new(1024);

        // OnApply回调命令结构
        private struct OnApplyCallbackCommand
        {
            public int RootId;
            public Entity Source;
            public Entity Target;
            public Entity TargetContext;
            public int EffectTemplateId;
        }
        
        // 收集OnApply回调命令的列表
        private readonly List<OnApplyCallbackCommand> _onApplyCallbacks = new List<OnApplyCallbackCommand>(64);
        private bool _onApplyBudgetFused;
        private readonly RootBudgetTable _onApplyCreateBudget = new RootBudgetTable(16384);
        private int _onApplyDropped;

        // ── TargetResolver fan-out (shared types from TargetResolverFanOutHelper) ──
        private readonly List<FanOutCommand> _fanOutCommands = new(256);
        private int _fanOutDropped;
        private readonly Entity[] _resolverBuffer = new Entity[256];

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;

        /// <summary>
        /// Time-sliced application stages for EffectApplicationSystem.
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

        // ── Phase Graph execution (optional, null = legacy-only mode) ──
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;
        private readonly Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi _graphApiHost;

        public EffectApplicationSystem(World world, EffectRequestQueue effectRequests = null, GasBudget budget = null, GasPresentationEventBuffer presentationEvents = null, EffectTemplateRegistry templates = null, ISpatialQueryService spatialQueries = null, EffectPhaseExecutor phaseExecutor = null, Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi graphApi = null) : base(world) 
        {
            _effectRequests = effectRequests;
            _budget = budget;
            _presentationEvents = presentationEvents;
            _templates = templates;
            _spatialQueries = spatialQueries;
            _phaseExecutor = phaseExecutor;
            _graphApiHost = graphApi;
            _graphApi = graphApi;
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
            if (!_sliceActive)
            {
                _sliceActive = true;
                _sliceStage = ApplicationStage.ProcessPending;
                _cursor = 0;
                _playbackCursor = 0;

                _effectsToDestroy.Clear();
                _effectsToActivate.Clear();
                _pendingAttach.Clear();
                _pendingCreateContainer.Clear();
                _onApplyCallbacks.Clear();
                _fanOutCommands.Clear();
                _pendingEffects.Clear();
                _onApplyCreateBudget.NextFrame();
                _onApplyDropped = 0;
                _fanOutDropped = 0;

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

                        if (World.Has<EffectCancelled>(effectEntity))
                        {
                            ref var cancelledEffect = ref World.Get<GameplayEffect>(effectEntity);
                            cancelledEffect.State = EffectState.Committed;
                            _effectsToDestroy.Add(effectEntity);
                            workUnits++;
                            continue;
                        }

                        ref var effect = ref World.Get<GameplayEffect>(effectEntity);
                        ref var context = ref World.Get<EffectContext>(effectEntity);
                        ref var modifiers = ref World.Get<EffectModifiers>(effectEntity);

                        if (effect.State == EffectState.Created)
                        {
                            effect.State = EffectState.Pending;
                        }

                        bool isInstant = effect.LifetimeKind == EffectLifetimeKind.Instant;
                        effect.State = EffectState.Calculate;
                        effect.State = EffectState.Apply;

                        // ── TargetResolver fan-out (OnResolve + OnHit phases) ──
                        if (_templates != null && _spatialQueries != null && World.Has<EffectTemplateRef>(effectEntity))
                        {
                            int tplId = World.Get<EffectTemplateRef>(effectEntity).TemplateId;
                            if (tplId > 0 && _templates.TryGetRef(tplId, out int tplIdx))
                            {
                                ref readonly var tplData = ref _templates.GetRef(tplIdx);

                                // Execute OnResolve Phase Graphs
                                ExecutePhaseForEffect(effectEntity, in context, in tplData, EffectPhaseId.OnResolve);

                                if (tplData.TargetQuery.Kind != TargetResolverKind.None)
                                {
                                    TargetResolverFanOutHelper.CollectFanOutTargets(
                                        World, in context, in tplData.TargetQuery,
                                        in tplData.TargetFilter, in tplData.TargetDispatch,
                                        _spatialQueries, _onApplyCreateBudget,
                                        _fanOutCommands, _resolverBuffer, ref _fanOutDropped);
                                }

                                // Execute OnHit Phase Graphs (after fan-out candidates collected)
                                ExecutePhaseForEffect(effectEntity, in context, in tplData, EffectPhaseId.OnHit);
                            }
                        }

                        // Execute OnApply Phase Graphs
                        if (_templates != null && World.Has<EffectTemplateRef>(effectEntity))
                        {
                            int tplId2 = World.Get<EffectTemplateRef>(effectEntity).TemplateId;
                            if (tplId2 > 0 && _templates.TryGetRef(tplId2, out int tplIdx2))
                            {
                                ref readonly var tplData2 = ref _templates.GetRef(tplIdx2);
                                ExecutePhaseForEffect(effectEntity, in context, in tplData2, EffectPhaseId.OnApply);
                            }
                        }

                        // Note: Legacy EffectCallbackComponent has been removed.
                        // OnApply callbacks are now handled via Phase Graph bindings.

                        if (isInstant)
                        {
                            if ((_phaseExecutor == null || _graphApi == null) && World.IsAlive(context.Target) && World.Has<AttributeBuffer>(context.Target))
                            {
                                ref var attrBuffer = ref World.Get<AttributeBuffer>(context.Target);
                                int primaryAttrId = modifiers.Count > 0 ? modifiers.Get(0).AttributeId : -1;
                                float before = primaryAttrId >= 0 ? attrBuffer.GetCurrent(primaryAttrId) : 0f;
                                EffectModifierOps.Apply(in modifiers, ref attrBuffer);
                                float after = primaryAttrId >= 0 ? attrBuffer.GetCurrent(primaryAttrId) : 0f;
                                float delta = after - before;
                                if (_presentationEvents != null && delta != 0f)
                                {
                                    _presentationEvents.Publish(new GasPresentationEvent
                                    {
                                        Kind = GasPresentationEventKind.EffectApplied,
                                        Actor = context.Source,
                                        Target = context.Target,
                                        AttributeId = primaryAttrId,
                                        Delta = delta
                                    });
                                }
                            }

                            effect.State = EffectState.Committed;
                            _effectsToDestroy.Add(effectEntity);
                        }
                        else
                        {
                            bool attachToActiveEffects = true;
                            if (_templates != null && World.Has<EffectTemplateRef>(effectEntity))
                            {
                                int tplId3 = World.Get<EffectTemplateRef>(effectEntity).TemplateId;
                                if (tplId3 > 0 && _templates.TryGetRef(tplId3, out int tplIdx3))
                                {
                                    ref readonly var tplData3 = ref _templates.GetRef(tplIdx3);
                                    attachToActiveEffects = tplData3.PresetType == EffectPresetType.Buff;
                                }
                            }

                            if (attachToActiveEffects && World.IsAlive(context.Target))
                            {
                                if (World.Has<ActiveEffectContainer>(context.Target))
                                {
                                    ref var container = ref World.Get<ActiveEffectContainer>(context.Target);
                                    container.Add(effectEntity);
                                }
                                else
                                {
                                    _pendingCreateContainer.Add(new PendingCreateContainer { Target = context.Target });
                                    _pendingAttach.Add(new PendingAttach { Target = context.Target, Effect = effectEntity });
                                }
                            }

                            effect.State = EffectState.Committed;
                            _effectsToActivate.Add(effectEntity);
                        }

                        workUnits++;
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
                        workUnits++;
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
                        }
                        workUnits++;
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
                        if (!World.IsAlive(item.Target)) { workUnits++; continue; }
                        if (!World.IsAlive(item.Effect)) { workUnits++; continue; }
                        if (World.Has<ActiveEffectContainer>(item.Target))
                        {
                            ref var container = ref World.Get<ActiveEffectContainer>(item.Target);
                            container.Add(item.Effect);
                        }
                        workUnits++;
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
                            ref var context = ref World.Get<EffectContext>(e);
                            ref var effectForActivate = ref World.Get<GameplayEffect>(e);
                            effectForActivate.State = EffectState.Committed;

                            // Grant tags to target entity based on EffectGrantedTags component
                            if (World.Has<EffectGrantedTags>(e) && World.IsAlive(context.Target))
                            {
                                ref readonly var grantedTags = ref World.Get<EffectGrantedTags>(e);
                                if (!World.Has<TagCountContainer>(context.Target))
                                    World.Add(context.Target, new TagCountContainer());
                                ref var tagCounts = ref World.Get<TagCountContainer>(context.Target);
                                int stackCount = World.Has<EffectStack>(e) ? World.Get<EffectStack>(e).Count : 1;
                                EffectTagContributionHelper.Grant(in grantedTags, ref tagCounts, stackCount, _budget);
                            }
                        }
                        workUnits++;
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
                            _effectRequests.Publish(new EffectRequest
                            {
                                RootId = cmd.RootId,
                                Source = TargetResolverFanOutHelper.ResolveSlot(cmd.ContextMapping.PayloadSource, in cmd),
                                Target = TargetResolverFanOutHelper.ResolveSlot(cmd.ContextMapping.PayloadTarget, in cmd),
                                TargetContext = TargetResolverFanOutHelper.ResolveSlot(cmd.ContextMapping.PayloadTargetContext, in cmd),
                                TemplateId = cmd.PayloadEffectTemplateId
                            });
                        }
                        workUnits++;
                    }
                    if (_fanOutDropped > 0)
                    {
                        // Budget fused — telemetry exposed via _fanOutDropped
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
                        workUnits++;
                    }
                    _sliceStage = ApplicationStage.Done;
                    _playbackCursor = 0;
                    continue;
                }

                ApplyOnApplyCallbacks();
                _sliceActive = false;
                return true;
            }
        }

        public void ResetSlice()
        {
            _sliceActive = false;
            _sliceStage = ApplicationStage.ProcessPending;
            _cursor = 0;
            _playbackCursor = 0;
            _pendingEffects.Clear();
            _effectsToDestroy.Clear();
            _effectsToActivate.Clear();
            _pendingAttach.Clear();
            _pendingCreateContainer.Clear();
            _onApplyCallbacks.Clear();
            _fanOutCommands.Clear();
            _pendingListenerRegistrations.Clear();
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
                PendingEffects.Add(new PendingEffectEntry { Effect = effectEntity, ResolveOrder = order });
            }
        }
        
        /// <summary>
        /// 批量应用OnApply回调（Query外执行，避免结构变更）
        /// </summary>
        private void ApplyOnApplyCallbacks()
        {
            if (_effectRequests == null || _onApplyCallbacks.Count == 0) return;

            for (int i = 0; i < _onApplyCallbacks.Count; i++)
            {
                var cmd = _onApplyCallbacks[i];
                _effectRequests.Publish(new EffectRequest
                {
                    RootId = cmd.RootId,
                    Source = cmd.Source,
                    Target = cmd.Target,
                    TargetContext = cmd.TargetContext,
                    TemplateId = cmd.EffectTemplateId
                });
            }

            if (_onApplyDropped > 0 && !_onApplyBudgetFused)
            {
                _onApplyBudgetFused = true;
                // Budget fused — telemetry exposed via _onApplyBudgetFused + _onApplyDropped
            }

            if (_onApplyDropped > 0 && _budget != null)
            {
                _budget.OnApplyCreatesDropped += _onApplyDropped;
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

        /// <summary>
        /// Execute a phase graph for an effect entity, reading its template for behavior and config.
        /// Passes effectTagId and effectTemplateId for Phase Listener matching.
        /// </summary>
        private void ExecutePhaseForEffect(Entity effectEntity, in EffectContext context, in EffectTemplateData tpl, EffectPhaseId phase)
        {
            if (_phaseExecutor == null || _graphApi == null) return;

            // Determine template id for listener matching
            int templateId = 0;
            if (World.IsAlive(effectEntity) && World.Has<EffectTemplateRef>(effectEntity))
                templateId = World.Get<EffectTemplateRef>(effectEntity).TemplateId;

            // Build merged config: template params + caller overrides
            var mergedConfig = ConfigParamsMerger.BuildMergedConfig(World, effectEntity, in tpl.ConfigParams);

            if (_graphApiHost != null && mergedConfig.Count > 0)
                _graphApiHost.SetConfigContext(in mergedConfig);

            _phaseExecutor.ExecutePhase(
                World, _graphApi,
                context.Source, context.Target, context.TargetContext,
                default,
                phase,
                in tpl.PhaseGraphBindings,
                tpl.PresetType,
                tpl.TagId,
                templateId);

            _graphApiHost?.ClearConfigContext();

            // Defer phase listener registration to Stage 6 (structural change safety)
            if (phase == EffectPhaseId.OnApply && tpl.ListenerSetup.Count > 0)
            {
                _pendingListenerRegistrations.Add(new PendingListenerRegistration
                {
                    Context = context,
                    TemplateId = templateId,
                    OwnerEffectId = effectEntity.Id,
                });
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
                    // Listener buffer full — silently drop (budget mode)
                }
            }
        }
    }
}
