using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Generic ability execution system. Replaces AbilityTaskSystem.
    /// Tick-driven with Clip/Signal/Gate processing, interruption, and CallerParams injection.
    /// </summary>
    public sealed class AbilityExecSystem : BaseSystem<World, float>, ITimeSlicedSystem
    {
        private readonly IClock _clock;
        private readonly GameplayEventBus _eventBus;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly InputRequestQueue _inputRequests;
        private readonly InputResponseBuffer _inputResponses;
        private readonly SelectionRequestQueue _selectionRequests;
        private readonly SelectionResponseBuffer _selectionResponses;
        private readonly EffectRequestQueue _effectRequests;
        private readonly GasPresentationEventBuffer _presentationEvents;
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly TagOps _tagOps;

        private readonly int _castAbilityOrderTagId;

        private static readonly QueryDescription _execQuery = new QueryDescription()
            .WithAll<AbilityExecInstance, AbilityStateBuffer>();

        // Query for entities with newly activated CastAbility orders (Tag+BB driven)
        private static readonly QueryDescription _newOrderQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, BlackboardIntBuffer>()
            .WithNone<AbilityExecInstance>();

        private Entity[] _execEntities = new Entity[2048];
        private int _execEntityCount;
        private bool _sliceActive;
        private int _cursor;

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;

        public AbilityExecSystem(
            World world,
            IClock clock,
            InputRequestQueue inputRequests,
            InputResponseBuffer inputResponses,
            SelectionRequestQueue selectionRequests,
            SelectionResponseBuffer selectionResponses,
            EffectRequestQueue effectRequests,
            AbilityDefinitionRegistry abilityDefinitions = null,
            GameplayEventBus eventBus = null,
            int castAbilityOrderTagId = 0,
            GasPresentationEventBuffer presentationEvents = null,
            EffectPhaseExecutor phaseExecutor = null,
            IGraphRuntimeApi graphApi = null,
            TagOps tagOps = null,
            OrderTypeRegistry orderTypeRegistry = null)
            : base(world)
        {
            _clock = clock;
            _inputRequests = inputRequests;
            _inputResponses = inputResponses;
            _selectionRequests = selectionRequests;
            _selectionResponses = selectionResponses;
            _effectRequests = effectRequests;
            _abilityDefinitions = abilityDefinitions;
            _eventBus = eventBus;
            _castAbilityOrderTagId = castAbilityOrderTagId;
            _presentationEvents = presentationEvents;
            _phaseExecutor = phaseExecutor;
            _graphApi = graphApi;
            _tagOps = tagOps ?? new TagOps();
            _orderTypeRegistry = orderTypeRegistry;
        }

        /// <summary>
        /// Maximum re-scan iterations to prevent infinite loops when abilities
        /// complete and promote new ones repeatedly in the same frame.
        /// </summary>
        private const int MaxRescanIterations = 4;

        public override void Update(in float dt)
        {
            while (!UpdateSlice(dt, int.MaxValue)) { }
            
            // After Phase 2 finishes abilities (which calls NotifyOrderComplete → 
            // promotes next queued order → activates tags), re-run Phase 1 to pick up
            // newly promoted orders in the same frame. Without this, there would be 
            // a one-frame delay between ability completion and the next queued ability starting.
            for (int rescan = 0; rescan < MaxRescanIterations; rescan++)
            {
                if (_castAbilityOrderTagId <= 0) break;
                int newCount = World.CountEntities(in _newOrderQuery);
                if (newCount == 0) break;
                while (!UpdateSlice(dt, int.MaxValue)) { }
            }
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            int workUnits = 0;

            // ── Phase 1: Query entities with Active_CastAbility tag + Blackboard (no AbilityExecInstance yet) ──
            if (_castAbilityOrderTagId > 0)
            {
                int newCount = World.CountEntities(in _newOrderQuery);
                if (newCount > _execEntities.Length)
                    _execEntities = new Entity[newCount * 2];
                World.GetEntities(in _newOrderQuery, _execEntities);
                for (int i = 0; i < newCount; i++)
                {
                    if (workUnits >= MaxWorkUnitsPerSlice) return false;
                    
                    var actor = _execEntities[i];
                    if (!World.IsAlive(actor)) continue;
                    
                    ref var actorTags = ref World.Get<GameplayTagContainer>(actor);
                    if (!actorTags.HasTag(_castAbilityOrderTagId)) continue;
                    if (!World.Has<AbilityStateBuffer>(actor)) continue;
                    
                    // Read slotIndex from Blackboard (Cast_SlotIndex = 110)
                    ref var bbInts = ref World.Get<BlackboardIntBuffer>(actor);
                    if (!bbInts.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex)) continue;
                    if (slotIndex < 0) continue;
                    
                    ref var abilities = ref World.Get<AbilityStateBuffer>(actor);
                    if ((uint)slotIndex >= (uint)abilities.Count) continue;

                    // Resolve effective ability: granted override > base slot
                    bool hasGranted = World.Has<GrantedSlotBuffer>(actor);
                    GrantedSlotBuffer grantedSlots = hasGranted ? World.Get<GrantedSlotBuffer>(actor) : default;
                    var slot = AbilitySlotResolver.Resolve(in abilities, in grantedSlots, hasGranted, slotIndex);
                    
                    // Read target from Blackboard (Cast_TargetEntity = 111)
                    Entity targetEntity = default;
                    if (World.Has<BlackboardEntityBuffer>(actor))
                    {
                        ref var bbEntities = ref World.Get<BlackboardEntityBuffer>(actor);
                        bbEntities.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out targetEntity);
                    }

                    // Block-tag check
                    AbilityActivationBlockTags blockTags = default;
                    bool hasBlockTags = false;
                    if (slot.AbilityId > 0 && _abilityDefinitions != null && _abilityDefinitions.TryGet(slot.AbilityId, out var def) && def.HasActivationBlockTags)
                    {
                        blockTags = def.ActivationBlockTags;
                        hasBlockTags = true;
                    }
                    else if (slot.TemplateEntityId > 0)
                    {
                        Entity template = EntityUtil.Reconstruct(slot.TemplateEntityId, slot.TemplateEntityWorldId, slot.TemplateEntityVersion);
                        if (World.IsAlive(template) && World.Has<AbilityActivationBlockTags>(template))
                        {
                            blockTags = World.Get<AbilityActivationBlockTags>(template);
                            hasBlockTags = true;
                        }
                    }

                    if (hasBlockTags)
                    {
                        if (!blockTags.RequiredAll.IsEmpty && !actorTags.ContainsAll(in blockTags.RequiredAll))
                        {
                            // Cancel the order via OrderSubmitter so next order can promote
                            if (_orderTypeRegistry != null)
                            {
                                OrderSubmitter.CancelCurrent(World, actor, _orderTypeRegistry);
                            }
                            _presentationEvents?.Publish(new GasPresentationEvent
                            {
                                Kind = GasPresentationEventKind.CastFailed,
                                Actor = actor,
                                AbilitySlot = slotIndex,
                                AbilityId = slot.AbilityId,
                                FailReason = AbilityCastFailReason.BlockedByTag
                            });
                            continue;
                        }
                        if (!blockTags.BlockedAny.IsEmpty && actorTags.Intersects(in blockTags.BlockedAny))
                        {
                            if (_orderTypeRegistry != null)
                            {
                                OrderSubmitter.CancelCurrent(World, actor, _orderTypeRegistry);
                            }
                            _presentationEvents?.Publish(new GasPresentationEvent
                            {
                                Kind = GasPresentationEventKind.CastFailed,
                                Actor = actor,
                                AbilitySlot = slotIndex,
                                AbilityId = slot.AbilityId,
                                FailReason = AbilityCastFailReason.OnCooldown
                            });
                            continue;
                        }
                    }

                    // ── Toggle check: if ability has toggle spec and toggle tag is ON, deactivate instead ──
                    if (slot.AbilityId > 0 && _abilityDefinitions != null &&
                        _abilityDefinitions.TryGet(slot.AbilityId, out var toggleDef) &&
                        toggleDef.HasToggleSpec && toggleDef.ToggleSpec.ToggleTagId > 0 &&
                        actorTags.HasTag(toggleDef.ToggleSpec.ToggleTagId))
                    {
                        DeactivateToggle(actor, ref actorTags, in toggleDef.ToggleSpec, slotIndex, slot.AbilityId, targetEntity);
                        continue;
                    }

                    if (!World.Has<AbilityExecInstance>(actor))
                    {
                        World.Add(actor, new AbilityExecInstance());
                    }

                    GasClockId defaultClockId = GasClockId.Step;
                    if (slot.AbilityId > 0 && _abilityDefinitions != null && _abilityDefinitions.TryGet(slot.AbilityId, out var aDef))
                    {
                        defaultClockId = aDef.ExecSpec.ClockId;
                    }

                    // Read OrderId from active OrderBuffer entry
                    int orderId = 0;
                    if (World.Has<OrderBuffer>(actor))
                    {
                        ref var orderBuf = ref World.Get<OrderBuffer>(actor);
                        if (orderBuf.HasActive)
                        {
                            orderId = orderBuf.ActiveOrder.Order.OrderId;
                        }
                    }

                    ref var exec = ref World.Get<AbilityExecInstance>(actor);
                    exec.OrderId = orderId;
                    exec.AbilitySlot = slotIndex;
                    exec.AbilityId = slot.AbilityId;
                    exec.Target = targetEntity;
                    exec.TargetContext = default;
                    exec.TargetPosCm = default;
                    exec.HasTargetPos = 0;
                    exec.MultiTargetCount = 0;
                    exec.State = AbilityExecRunState.Running;
                    exec.CurrentTick = 0;
                    exec.StartAbsoluteTick = _clock.Now(defaultClockId.ToDomainId());
                    exec.NextItemIndex = 0;
                    exec.GateDeadline = 0;
                    exec.WaitTagId = 0;
                    exec.WaitRequestId = 0;
                    exec.ActiveClockId = defaultClockId;

                    if (World.Has<BlackboardSpatialBuffer>(actor))
                    {
                        ref var bbSpatial = ref World.Get<BlackboardSpatialBuffer>(actor);
                        if (bbSpatial.TryGetPoint(OrderBlackboardKeys.Cast_TargetPosition, out var targetPos))
                        {
                            exec.TargetPosCm = Fix64Vec2.FromFloat(targetPos.X, targetPos.Z);
                            exec.HasTargetPos = 1;
                        }
                    }

                    _presentationEvents?.Publish(new GasPresentationEvent
                    {
                        Kind = GasPresentationEventKind.CastStarted,
                        Actor = actor,
                        Target = targetEntity,
                        AbilitySlot = slotIndex,
                        AbilityId = slot.AbilityId
                    });

                    workUnits++;
                }
            }

            // ── Phase 2: Advance all active exec instances ──
            if (!_sliceActive)
            {
                _sliceActive = true;
                _cursor = 0;
                _execEntityCount = 0;
                var collect = new CollectExecJob { Entities = _execEntities, Count = 0 };
                World.InlineEntityQuery<CollectExecJob, AbilityExecInstance>(in _execQuery, ref collect);
                _execEntityCount = collect.Count;
            }

            while (_cursor < _execEntityCount)
            {
                if (workUnits >= MaxWorkUnitsPerSlice) return false;

                var actor = _execEntities[_cursor++];
                if (!World.IsAlive(actor) || !World.Has<AbilityExecInstance>(actor) || !World.Has<AbilityStateBuffer>(actor))
                {
                    workUnits++;
                    continue;
                }

                ref var instance = ref World.Get<AbilityExecInstance>(actor);
                ref var abilities = ref World.Get<AbilityStateBuffer>(actor);

                if (instance.AbilitySlot < 0 || instance.AbilitySlot >= abilities.Count)
                {
                    World.Remove<AbilityExecInstance>(actor);
                    workUnits++;
                    continue;
                }

                // Resolve effective ability: granted override > base slot
                bool hasGrantedP2 = World.Has<GrantedSlotBuffer>(actor);
                GrantedSlotBuffer grantedSlotsP2 = hasGrantedP2 ? World.Get<GrantedSlotBuffer>(actor) : default;
                var slot = AbilitySlotResolver.Resolve(in abilities, in grantedSlotsP2, hasGrantedP2, instance.AbilitySlot);

                AbilityExecSpec spec;
                AbilityExecCallerParamsPool callerPool = default;
                bool hasCallerPool = false;
                AbilityOnActivateEffects onActivateEffects = default;
                bool hasOnActivate = false;

                if (slot.AbilityId <= 0 || _abilityDefinitions == null || !_abilityDefinitions.TryGet(slot.AbilityId, out var def))
                {
                    // No valid ability definition found — fail-fast, remove exec instance
                    World.Remove<AbilityExecInstance>(actor);
                    workUnits++;
                    continue;
                }

                // Toggle deactivate uses the DeactivateExecSpec instead of normal ExecSpec
                if (instance.IsToggleDeactivating && def.HasToggleSpec)
                {
                    spec = def.ToggleSpec.DeactivateExecSpec;
                }
                else
                {
                    spec = def.ExecSpec;
                }
                callerPool = def.ExecCallerParamsPool;
                hasCallerPool = def.HasExecCallerParamsPool;
                hasOnActivate = def.HasOnActivateEffects;
                onActivateEffects = def.OnActivateEffects;

                // Interrupt check
                ref var actorTags = ref World.TryGetRef<GameplayTagContainer>(actor, out bool hasActorTags);
                if (hasActorTags && !spec.InterruptAny.IsEmpty && actorTags.Intersects(in spec.InterruptAny))
                {
                    instance.State = AbilityExecRunState.Interrupted;
                }

                // Tick advancement
                if (instance.State == AbilityExecRunState.Running)
                {
                    int now = _clock.Now(instance.ActiveClockId.ToDomainId());
                    instance.CurrentTick = now - instance.StartAbsoluteTick;
                    AdvanceItems(actor, ref spec, ref callerPool, hasCallerPool, hasOnActivate, ref onActivateEffects, ref instance);
                }
                else if (instance.State == AbilityExecRunState.GateWaiting)
                {
                    ProcessGate(actor, ref spec, ref instance);
                }

                // Cleanup finished/interrupted
                if (instance.State == AbilityExecRunState.Finished || instance.State == AbilityExecRunState.Interrupted)
                {
                    var finishKind = instance.State == AbilityExecRunState.Interrupted
                        ? GasPresentationEventKind.CastInterrupted
                        : GasPresentationEventKind.CastFinished;
                    _presentationEvents?.Publish(new GasPresentationEvent
                    {
                        Kind = finishKind,
                        Actor = actor,
                        Target = instance.Target,
                        AbilitySlot = instance.AbilitySlot,
                        AbilityId = instance.AbilityId
                    });
                    
                    // Toggle activation: when the activate timeline (not deactivate) finishes successfully,
                    // add the toggle tag and apply infinite effects.
                    if (instance.State == AbilityExecRunState.Finished &&
                        !instance.IsToggleDeactivating &&
                        instance.AbilityId > 0 &&
                        _abilityDefinitions != null &&
                        _abilityDefinitions.TryGet(instance.AbilityId, out var toggleFinishDef) &&
                        toggleFinishDef.HasToggleSpec && toggleFinishDef.ToggleSpec.ToggleTagId > 0)
                    {
                        ActivateToggle(actor, in toggleFinishDef.ToggleSpec);
                    }
                    
                    World.Remove<AbilityExecInstance>(actor);
                    
                    // Notify OrderBuffer pipeline that this order completed (promotes next queued order)
                    if (_orderTypeRegistry != null)
                    {
                        OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypeRegistry);
                    }
                }
                else
                {
                    World.Get<AbilityExecInstance>(actor) = instance;
                }

                workUnits++;
            }

            _sliceActive = false;
            return true;
        }

        public void ResetSlice()
        {
            _sliceActive = false;
            _cursor = 0;
            _execEntityCount = 0;
        }

        // ──────────────────── Item processing ────────────────────

        private void AdvanceItems(Entity actor, ref AbilityExecSpec spec,
            ref AbilityExecCallerParamsPool callerPool, bool hasCallerPool,
            bool hasOnActivate, ref AbilityOnActivateEffects onActivateEffects,
            ref AbilityExecInstance inst)
        {
            for (int guard = 0; guard < AbilityExecSpec.MAX_ITEMS; guard++)
            {
                if (inst.NextItemIndex >= spec.ItemCount)
                {
                    inst.State = AbilityExecRunState.Finished;
                    return;
                }

                int idx = inst.NextItemIndex;
                var kind = spec.GetKind(idx);
                int itemTick = spec.GetTick(idx);

                // Not yet time for this item
                if (itemTick > inst.CurrentTick) return;

                switch (kind)
                {
                    case ExecItemKind.End:
                        inst.State = AbilityExecRunState.Finished;
                        return;

                    // ── Clips ──
                    case ExecItemKind.EffectClip:
                        FireEffectItem(actor, ref spec, idx, ref callerPool, hasCallerPool, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagClip:
                        FireTagClip(actor, ref spec, idx, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagClipTarget:
                        if (World.IsAlive(inst.Target))
                            FireTagClip(inst.Target, ref spec, idx, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    // ── Signals ──
                    case ExecItemKind.EffectSignal:
                        FireEffectItem(actor, ref spec, idx, ref callerPool, hasCallerPool, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.EventSignal:
                        FireEventSignal(actor, ref spec, idx, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.GraphSignal:
                        ExecuteGraphSignal(actor, ref spec, idx, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagSignal:
                        FireTagSignal(actor, ref spec, idx);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagSignalTarget:
                        if (World.IsAlive(inst.Target))
                            FireTagSignal(inst.Target, ref spec, idx);
                        inst.NextItemIndex++;
                        continue;

                    // ── Gates ──
                    case ExecItemKind.InputGate:
                    case ExecItemKind.EventGate:
                    case ExecItemKind.SelectionGate:
                        EnterGate(actor, ref spec, idx, ref inst);
                        // Attempt immediate resolution if response already available
                        if (inst.State == AbilityExecRunState.GateWaiting)
                            ProcessGate(actor, ref spec, ref inst);
                        if (inst.State == AbilityExecRunState.GateWaiting)
                            return; // Still blocked
                        continue; // Gate resolved, advance to next item

                    default:
                        inst.NextItemIndex++;
                        continue;
                }
            }

            // If we exhaust the guard, treat as finished
            inst.State = AbilityExecRunState.Finished;
        }

        // ── Effect dispatch (shared for EffectClip & EffectSignal) ──

        private void FireEffectItem(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecCallerParamsPool callerPool, bool hasCallerPool,
            ref AbilityExecInstance inst)
        {
            if (_effectRequests == null) return;

            int templateId = spec.GetTemplateId(idx);
            if (templateId <= 0) return;

            byte cpIdx = spec.GetCallerParamsIdx(idx);
            bool hasCp = hasCallerPool && cpIdx != 0xFF && cpIdx < callerPool.Count;

            Entity target = World.IsAlive(inst.Target) ? inst.Target : actor;

            if (inst.MultiTargetCount > 0)
            {
                // Multi-target: dispatch to each
                unsafe
                {
                    fixed (int* ids = inst.MultiTargetIds)
                    fixed (int* wids = inst.MultiTargetWorldIds)
                    fixed (int* vers = inst.MultiTargetVersions)
                    {
                        for (int i = 0; i < inst.MultiTargetCount; i++)
                        {
                            var t = EntityUtil.Reconstruct(ids[i], wids[i], vers[i]);
                            if (!World.IsAlive(t)) continue;
                            PublishEffectRequest(actor, t, inst.TargetContext, templateId,
                                hasCp ? callerPool.Get(cpIdx) : default, hasCp);
                        }
                    }
                }
            }
            else
            {
                PublishEffectRequest(actor, target, inst.TargetContext, templateId,
                    hasCp ? callerPool.Get(cpIdx) : default, hasCp);
            }
        }

        private void PublishEffectRequest(Entity source, Entity target, Entity targetContext,
            int templateId, in EffectConfigParams callerParams, bool hasCallerParams)
        {
            var req = new EffectRequest
            {
                Source = source,
                Target = target,
                TargetContext = targetContext,
                TemplateId = templateId,
                HasCallerParams = hasCallerParams,
            };
            if (hasCallerParams) req.CallerParams = callerParams;
            _effectRequests.Publish(req);
        }

        // ── Tag Clip (add at start, auto-remove via TimedTag) ──

        private void FireTagClip(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            int tagId = spec.GetTagId(idx);
            if (tagId <= 0) return;
            int durationTicks = spec.GetDurationTicks(idx);
            GasClockId clockId = spec.GetClockId(idx);
            if ((byte)clockId == 0) clockId = inst.ActiveClockId;

            EnsureTagComponents(actor);
            if (!World.Has<DirtyFlags>(actor)) World.Add(actor, new DirtyFlags());
            ref var tags = ref World.Get<GameplayTagContainer>(actor);
            ref var counts = ref World.Get<TagCountContainer>(actor);
            ref var dirty = ref World.Get<DirtyFlags>(actor);
            _tagOps.AddTag(ref tags, ref counts, tagId, ref dirty);

            if (durationTicks > 0)
            {
                ref var timed = ref World.Get<TimedTagBuffer>(actor);
                int expireAt = _clock.Now(clockId.ToDomainId()) + durationTicks;
                timed.TryAdd(tagId, expireAt, clockId);
            }
        }

        // ── Tag Signal (instant add/remove) ──

        private void FireTagSignal(Entity actor, ref AbilityExecSpec spec, int idx)
        {
            int tagId = spec.GetTagId(idx);
            if (tagId <= 0) return;
            int payloadA = spec.GetPayloadA(idx);
            bool isRemove = payloadA == 1;

            if (isRemove)
            {
                if (World.Has<GameplayTagContainer>(actor) && World.Has<TagCountContainer>(actor))
                {
                    if (!World.Has<DirtyFlags>(actor)) World.Add(actor, new DirtyFlags());
                    ref var tags = ref World.Get<GameplayTagContainer>(actor);
                    ref var counts = ref World.Get<TagCountContainer>(actor);
                    ref var dirty = ref World.Get<DirtyFlags>(actor);
                    _tagOps.RemoveTag(ref tags, ref counts, tagId, ref dirty);
                }
            }
            else
            {
                EnsureTagComponents(actor);
                if (!World.Has<DirtyFlags>(actor)) World.Add(actor, new DirtyFlags());
                ref var tags = ref World.Get<GameplayTagContainer>(actor);
                ref var counts = ref World.Get<TagCountContainer>(actor);
                ref var dirty = ref World.Get<DirtyFlags>(actor);
                _tagOps.AddTag(ref tags, ref counts, tagId, ref dirty);
            }
        }

        // ── Event Signal ──

        private void FireEventSignal(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            if (_eventBus == null) return;
            int tagId = spec.GetTagId(idx);
            _eventBus.Publish(new GameplayEvent
            {
                TagId = tagId,
                Source = actor,
                Target = inst.Target,
                Magnitude = spec.GetPayloadA(idx)
            });
        }

        private void ExecuteGraphSignal(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            if (_phaseExecutor == null || _graphApi == null) return;
            int graphProgramId = spec.GetPayloadA(idx);
            if (graphProgramId <= 0) return;

            Entity target = inst.Target;
            // TargetPos resolution is deferred to the graph itself via LoadAttribute or spatial queries.
            // AbilityExecSystem does not depend on Physics2D position components.
            _phaseExecutor.ExecuteGraph(World, _graphApi, actor, target, default, default, graphProgramId);
        }

        // ── Gate enter / process ──

        private void EnterGate(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            var kind = spec.GetKind(idx);
            inst.State = AbilityExecRunState.GateWaiting;

            switch (kind)
            {
                case ExecItemKind.InputGate:
                {
                    int requestId = spec.GetPayloadA(idx) != 0 ? spec.GetPayloadA(idx) : inst.OrderId;
                    inst.WaitRequestId = requestId;
                    _inputRequests?.TryEnqueue(new InputRequest
                    {
                        RequestId = requestId,
                        RequestTagId = spec.GetTagId(idx),
                        Source = actor,
                        Context = inst.TargetContext,
                    });
                    break;
                }

                case ExecItemKind.SelectionGate:
                {
                    int requestId = spec.GetPayloadA(idx) != 0 ? spec.GetPayloadA(idx) : inst.OrderId;
                    inst.WaitRequestId = requestId;
                    _selectionRequests?.TryEnqueue(new SelectionRequest
                    {
                        RequestId = requestId,
                        RequestTagId = spec.GetTagId(idx),
                        Origin = actor,
                        TargetContext = inst.TargetContext,
                    });
                    break;
                }

                case ExecItemKind.EventGate:
                {
                    inst.WaitTagId = spec.GetTagId(idx);
                    int deadlineTicks = spec.GetPayloadA(idx);
                    if (deadlineTicks > 0)
                    {
                        inst.GateDeadline = _clock.Now(inst.ActiveClockId.ToDomainId()) + deadlineTicks;
                    }
                    break;
                }
            }
        }

        private void ProcessGate(Entity actor, ref AbilityExecSpec spec, ref AbilityExecInstance inst)
        {
            if (inst.NextItemIndex >= spec.ItemCount)
            {
                inst.State = AbilityExecRunState.Finished;
                return;
            }

            var kind = spec.GetKind(inst.NextItemIndex);

            switch (kind)
            {
                case ExecItemKind.InputGate:
                {
                    if (_inputResponses == null) return;
                    if (_inputResponses.TryConsume(inst.WaitRequestId, out var resp))
                    {
                        if (World.IsAlive(resp.Target)) inst.Target = resp.Target;
                        if (World.IsAlive(resp.TargetContext)) inst.TargetContext = resp.TargetContext;
                        inst.WaitRequestId = 0;
                        inst.NextItemIndex++;
                        inst.State = AbilityExecRunState.Running;
                    }
                    break;
                }

                case ExecItemKind.SelectionGate:
                {
                    if (_selectionResponses == null) return;
                    if (_selectionResponses.TryConsume(inst.WaitRequestId, out var resp))
                    {
                        int copyCount = resp.Count;
                        if (copyCount > 64) copyCount = 64;
                        inst.MultiTargetCount = copyCount;
                        unsafe
                        {
                            fixed (int* idsDst = inst.MultiTargetIds)
                            fixed (int* widsDst = inst.MultiTargetWorldIds)
                            fixed (int* verDst = inst.MultiTargetVersions)
                            {
                                for (int i = 0; i < copyCount; i++)
                                {
                                    var e = resp.GetEntity(i);
                                    idsDst[i] = e.Id;
                                    widsDst[i] = e.WorldId;
                                    verDst[i] = e.Version;
                                }
                            }
                        }
                        if (copyCount > 0)
                        {
                            var chosen = resp.GetEntity(0);
                            if (World.IsAlive(chosen)) inst.Target = chosen;
                        }
                        inst.WaitRequestId = 0;
                        inst.NextItemIndex++;
                        inst.State = AbilityExecRunState.Running;
                    }
                    break;
                }

                case ExecItemKind.EventGate:
                {
                    if (_eventBus == null) return;
                    // Timeout check
                    if (inst.GateDeadline > 0)
                    {
                        int now = _clock.Now(inst.ActiveClockId.ToDomainId());
                        if (now >= inst.GateDeadline)
                        {
                            inst.GateDeadline = 0;
                            inst.WaitTagId = 0;
                            inst.NextItemIndex++;
                            inst.State = AbilityExecRunState.Running;
                            return;
                        }
                    }

                    for (int i = 0; i < _eventBus.Events.Count; i++)
                    {
                        var evt = _eventBus.Events[i];
                        if (evt.TagId != inst.WaitTagId) continue;
                        inst.WaitTagId = 0;
                        inst.GateDeadline = 0;
                        inst.NextItemIndex++;
                        inst.State = AbilityExecRunState.Running;
                        return;
                    }
                    break;
                }
            }
        }

        // ──────────────────── Toggle Helpers ────────────────────

        /// <summary>
        /// Activate toggle: add toggle tag and apply infinite active effects.
        /// Called when the activate timeline completes successfully.
        /// </summary>
        private void ActivateToggle(Entity actor, in AbilityToggleSpec toggleSpec)
        {
            if (!World.IsAlive(actor)) return;
            if (!World.Has<GameplayTagContainer>(actor)) return;
            
            ref var tags = ref World.Get<GameplayTagContainer>(actor);
            tags.AddTag(toggleSpec.ToggleTagId);
            
            // Apply active effects as infinite-duration effects
            unsafe
            {
                for (int i = 0; i < toggleSpec.ActiveEffectCount && i < 4; i++)
                {
                    int tplId = toggleSpec.ActiveEffectTemplateIds[i];
                    if (tplId > 0)
                    {
                        _effectRequests?.Publish(new EffectRequest
                        {
                            RootId = 0,
                            Source = actor,
                            Target = actor,
                            TemplateId = tplId
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Deactivate toggle: remove toggle tag and active effects, then optionally
        /// run the deactivate timeline. If no deactivate timeline, completes instantly.
        /// </summary>
        private void DeactivateToggle(Entity actor, ref GameplayTagContainer actorTags,
            in AbilityToggleSpec toggleSpec, int slotIndex, int abilityId, Entity targetEntity)
        {
            // Remove toggle tag
            actorTags.RemoveTag(toggleSpec.ToggleTagId);
            
            // Remove active effects by tag (the effects are tagged with the toggle tag,
            // so removing the tag will cause EffectLifetimeSystem to clean them up via ExpireCondition)
            
            // If there's a deactivate timeline, execute it
            if (toggleSpec.DeactivateExecSpec.ItemCount > 0)
            {
                if (!World.Has<AbilityExecInstance>(actor))
                {
                    World.Add(actor, new AbilityExecInstance());
                }
                
                ref var exec = ref World.Get<AbilityExecInstance>(actor);
                exec.OrderId = 0;
                exec.AbilitySlot = slotIndex;
                exec.AbilityId = abilityId;
                exec.Target = targetEntity;
                exec.TargetContext = default;
                exec.MultiTargetCount = 0;
                exec.State = AbilityExecRunState.Running;
                exec.CurrentTick = 0;
                exec.StartAbsoluteTick = _clock.Now(toggleSpec.DeactivateExecSpec.ClockId.ToDomainId());
                exec.NextItemIndex = 0;
                exec.GateDeadline = 0;
                exec.WaitTagId = 0;
                exec.WaitRequestId = 0;
                exec.ActiveClockId = toggleSpec.DeactivateExecSpec.ClockId;
                exec.IsToggleDeactivating = true;
                
                _presentationEvents?.Publish(new GasPresentationEvent
                {
                    Kind = GasPresentationEventKind.CastStarted,
                    Actor = actor,
                    Target = targetEntity,
                    AbilitySlot = slotIndex,
                    AbilityId = abilityId
                });
            }
            else
            {
                // No deactivate timeline — instant deactivation, just complete the order
                _presentationEvents?.Publish(new GasPresentationEvent
                {
                    Kind = GasPresentationEventKind.CastFinished,
                    Actor = actor,
                    Target = targetEntity,
                    AbilitySlot = slotIndex,
                    AbilityId = abilityId
                });
                
                if (_orderTypeRegistry != null)
                {
                    OrderSubmitter.NotifyOrderComplete(World, actor, _orderTypeRegistry);
                }
            }
        }

        // ──────────────────── Helpers ────────────────────

        private void EnsureTagComponents(Entity actor)
        {
            if (!World.Has<GameplayTagContainer>(actor)) World.Add(actor, new GameplayTagContainer());
            if (!World.Has<TagCountContainer>(actor)) World.Add(actor, new TagCountContainer());
            if (!World.Has<TimedTagBuffer>(actor)) World.Add(actor, new TimedTagBuffer());
        }

        private struct CollectExecJob : IForEachWithEntity<AbilityExecInstance>
        {
            public Entity[] Entities;
            public int Count;

            public void Update(Entity entity, ref AbilityExecInstance _)
            {
                if (Count < Entities.Length)
                {
                    Entities[Count++] = entity;
                }
            }
        }
    }
}
