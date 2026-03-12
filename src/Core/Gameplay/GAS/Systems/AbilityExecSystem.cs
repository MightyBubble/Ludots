using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.GraphRuntime;
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
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly TagOps _tagOps;

        private readonly int _castAbilityOrderTypeId;

        private static readonly QueryDescription _execQuery = new QueryDescription()
            .WithAll<AbilityExecInstance, AbilityStateBuffer>();

        // Query for entities with newly activated CastAbility orders (OrderBuffer + Blackboard driven)
        private static readonly QueryDescription _newOrderQuery = new QueryDescription()
            .WithAll<OrderBuffer, BlackboardIntBuffer, AbilityStateBuffer>()
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
            int castAbilityOrderTypeId = 0,
            GasPresentationEventBuffer presentationEvents = null,
            EffectPhaseExecutor phaseExecutor = null,
            GraphProgramRegistry graphPrograms = null,
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
            _castAbilityOrderTypeId = castAbilityOrderTypeId;
            _presentationEvents = presentationEvents;
            _phaseExecutor = phaseExecutor;
            _graphPrograms = graphPrograms;
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
            
            // After Phase 2 finishes abilities (which calls NotifyOrderComplete 锟?
            // promotes next queued order 锟?activates tags), re-run Phase 1 to pick up
            // newly promoted orders in the same frame. Without this, there would be 
            // a one-frame delay between ability completion and the next queued ability starting.
            for (int rescan = 0; rescan < MaxRescanIterations; rescan++)
            {
                if (_castAbilityOrderTypeId <= 0) break;
                int newCount = World.CountEntities(in _newOrderQuery);
                if (newCount == 0) break;
                while (!UpdateSlice(dt, int.MaxValue)) { }
            }
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            int workUnits = 0;

            // 鈹€鈹€ Phase 1: Query entities with active CastAbility order + Blackboard (no AbilityExecInstance yet) 鈹€鈹€
            if (_castAbilityOrderTypeId > 0)
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
                    
                    ref var orderBuffer = ref World.Get<OrderBuffer>(actor);
                    if (!orderBuffer.HasActive || orderBuffer.ActiveOrder.Order.OrderTypeId != _castAbilityOrderTypeId) continue;

                    ref var actorTags = ref World.TryGetRef<GameplayTagContainer>(actor, out bool hasActorTags);
                    
                    // Read slotIndex from Blackboard (Cast_SlotIndex = 110)
                    ref var bbInts = ref World.Get<BlackboardIntBuffer>(actor);
                    if (!bbInts.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex))
                    {
                        continue;
                    }
                    if (slotIndex < 0)
                    {
                        continue;
                    }
                    
                    ref var abilities = ref World.Get<AbilityStateBuffer>(actor);
                    if ((uint)slotIndex >= (uint)abilities.Count)
                    {
                        continue;
                    }

                    // Resolve effective ability: granted override > base slot
                    bool hasForm = World.Has<AbilityFormSlotBuffer>(actor);
                    AbilityFormSlotBuffer formSlots = hasForm ? World.Get<AbilityFormSlotBuffer>(actor) : default;
                    bool hasGranted = World.Has<GrantedSlotBuffer>(actor);
                    GrantedSlotBuffer grantedSlots = hasGranted ? World.Get<GrantedSlotBuffer>(actor) : default;
                    var slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in grantedSlots, hasGranted, slotIndex);
                    
                    // Read target from Blackboard (Cast_TargetEntity = 111)
                    Entity targetEntity = default;
                    if (World.Has<BlackboardEntityBuffer>(actor))
                    {
                        ref var bbEntities = ref World.Get<BlackboardEntityBuffer>(actor);
                        bbEntities.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out targetEntity);
                    }

                    AbilityDefinition abilityDef = default;
                    bool hasAbilityDef = slot.AbilityId > 0 &&
                        _abilityDefinitions != null &&
                        _abilityDefinitions.TryGet(slot.AbilityId, out abilityDef);
                    Entity templateEntity = default;
                    bool hasTemplateEntity = false;
                    if (slot.TemplateEntityId > 0)
                    {
                        templateEntity = EntityUtil.Reconstruct(slot.TemplateEntityId, slot.TemplateEntityWorldId, slot.TemplateEntityVersion);
                        hasTemplateEntity = World.IsAlive(templateEntity);
                    }

                    // Toggle check comes before activation block tags so a toggled-on ability
                    // can always be turned off, even while its reactivate cooldown is present.
                    if (hasAbilityDef &&
                        abilityDef.HasToggleSpec &&
                        abilityDef.ToggleSpec.ToggleTagId > 0 &&
                        hasActorTags &&
                        actorTags.HasTag(abilityDef.ToggleSpec.ToggleTagId))
                    {
                        DeactivateToggle(actor, in abilityDef.ToggleSpec, slotIndex, slot.AbilityId, targetEntity);
                        continue;
                    }

                    // Block-tag check
                    AbilityActivationBlockTags blockTags = default;
                    bool hasBlockTags = false;
                    if (hasAbilityDef && abilityDef.HasActivationBlockTags)
                    {
                        blockTags = abilityDef.ActivationBlockTags;
                        hasBlockTags = true;
                    }
                    else if (hasTemplateEntity)
                    {
                        if (World.Has<AbilityActivationBlockTags>(templateEntity))
                        {
                            blockTags = World.Get<AbilityActivationBlockTags>(templateEntity);
                            hasBlockTags = true;
                        }
                    }

                    if (hasBlockTags)
                    {
                        if (!blockTags.RequiredAll.IsEmpty && (!hasActorTags || !actorTags.ContainsAll(in blockTags.RequiredAll)))
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
                        if (hasActorTags && !blockTags.BlockedAny.IsEmpty && actorTags.Intersects(in blockTags.BlockedAny))
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

                    AbilityActivationPrecondition activationPrecondition = default;
                    bool hasActivationPrecondition = false;
                    if (hasAbilityDef && abilityDef.HasActivationPrecondition)
                    {
                        activationPrecondition = abilityDef.ActivationPrecondition;
                        hasActivationPrecondition = true;
                    }
                    else if (hasTemplateEntity && World.Has<AbilityActivationPrecondition>(templateEntity))
                    {
                        activationPrecondition = World.Get<AbilityActivationPrecondition>(templateEntity);
                        hasActivationPrecondition = true;
                    }

                    Fix64Vec2 targetOriginPosCm = default;
                    bool hasTargetOriginPos = false;
                    Fix64Vec2 targetPosCm = default;
                    bool hasTargetPos = false;
                    if (World.Has<BlackboardSpatialBuffer>(actor))
                    {
                        ref var bbSpatial = ref World.Get<BlackboardSpatialBuffer>(actor);
                        int pointCount = bbSpatial.GetPointCount(OrderBlackboardKeys.Cast_TargetPosition);
                        if (pointCount > 1 &&
                            bbSpatial.TryGetPointAt(OrderBlackboardKeys.Cast_TargetPosition, 0, out var originPos))
                        {
                            targetOriginPosCm = Fix64Vec2.FromFloat(originPos.X, originPos.Z);
                            hasTargetOriginPos = true;
                        }

                        int targetPointIndex = pointCount > 1 ? pointCount - 1 : 0;
                        if (pointCount > 0 &&
                            bbSpatial.TryGetPointAt(OrderBlackboardKeys.Cast_TargetPosition, targetPointIndex, out var targetPos))
                        {
                            targetPosCm = Fix64Vec2.FromFloat(targetPos.X, targetPos.Z);
                            hasTargetPos = true;
                        }
                    }

                    if (hasActivationPrecondition)
                    {
                        IntVector2 validationTargetPos = default;
                        if (hasTargetPos)
                        {
                            var roundedTargetPos = targetPosCm.RoundToInt();
                            validationTargetPos = new IntVector2(roundedTargetPos.x, roundedTargetPos.y);
                        }

                        if (!AbilityActivationPreconditionEvaluator.Evaluate(
                                World,
                                actor,
                                targetEntity,
                                validationTargetPos,
                                slot.AbilityId,
                                in activationPrecondition,
                                _graphPrograms,
                                _graphApi))
                        {
                            if (_orderTypeRegistry != null)
                            {
                                OrderSubmitter.CancelCurrent(World, actor, _orderTypeRegistry);
                            }
                            _presentationEvents?.Publish(new GasPresentationEvent
                            {
                                Kind = GasPresentationEventKind.CastFailed,
                                Actor = actor,
                                Target = targetEntity,
                                AbilitySlot = slotIndex,
                                AbilityId = slot.AbilityId,
                                FailReason = AbilityCastFailReason.PreconditionFailed
                            });
                            continue;
                        }
                    }

                    if (!World.Has<AbilityExecInstance>(actor))
                    {
                        World.Add(actor, new AbilityExecInstance());
                    }

                    GasClockId defaultClockId = hasAbilityDef ? abilityDef.ExecSpec.ClockId : GasClockId.Step;

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
                    exec.TargetPosCm = targetPosCm;
                    exec.HasTargetPos = (byte)(hasTargetPos ? 1 : 0);
                    exec.TargetOriginPosCm = targetOriginPosCm;
                    exec.HasTargetOriginPos = (byte)(hasTargetOriginPos ? 1 : 0);
                    exec.MultiTargetCount = 0;
                    exec.State = AbilityExecRunState.Running;
                    exec.CurrentTick = 0;
                    exec.StartAbsoluteTick = _clock.Now(defaultClockId.ToDomainId());
                    exec.NextItemIndex = 0;
                    exec.GateDeadline = 0;
                    exec.WaitTagId = 0;
                    exec.WaitRequestId = 0;
                    exec.ActiveClockId = defaultClockId;

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

            // 鈹€鈹€ Phase 2: Advance all active exec instances 鈹€鈹€
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
                bool hasFormP2 = World.Has<AbilityFormSlotBuffer>(actor);
                AbilityFormSlotBuffer formSlotsP2 = hasFormP2 ? World.Get<AbilityFormSlotBuffer>(actor) : default;
                bool hasGrantedP2 = World.Has<GrantedSlotBuffer>(actor);
                GrantedSlotBuffer grantedSlotsP2 = hasGrantedP2 ? World.Get<GrantedSlotBuffer>(actor) : default;
                var slot = AbilitySlotResolver.Resolve(in abilities, in formSlotsP2, hasFormP2, in grantedSlotsP2, hasGrantedP2, instance.AbilitySlot);

                AbilityExecSpec spec;
                AbilityExecCallerParamsPool callerPool = default;
                bool hasCallerPool = false;
                AbilityOnActivateEffects onActivateEffects = default;
                bool hasOnActivate = false;

                if (slot.AbilityId <= 0 || _abilityDefinitions == null || !_abilityDefinitions.TryGet(slot.AbilityId, out var def))
                {
                    // No valid ability definition found 锟?fail-fast, remove exec instance
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

        // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Item processing 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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

                    // 鈹€鈹€ Clips 鈹€鈹€
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

                    // 鈹€鈹€ Signals 鈹€鈹€
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

                    // 鈹€鈹€ Gates 鈹€鈹€
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

        // 鈹€鈹€ Effect dispatch (shared for EffectClip & EffectSignal) 鈹€鈹€

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
                                hasCp ? callerPool.Get(cpIdx) : default, hasCp, in inst);
                        }
                    }
                }
            }
            else
            {
                PublishEffectRequest(actor, target, inst.TargetContext, templateId,
                    hasCp ? callerPool.Get(cpIdx) : default, hasCp, in inst);
            }
        }

        private void PublishEffectRequest(Entity source, Entity target, Entity targetContext,
            int templateId, in EffectConfigParams callerParams, bool hasCallerParams, in AbilityExecInstance inst)
        {
            var resolvedCallerParams = callerParams;
            bool resolvedHasCallerParams = hasCallerParams;
            resolvedHasCallerParams |= TryAppendSpatialCallerParams(ref resolvedCallerParams, in inst);

            var req = new EffectRequest
            {
                Source = source,
                Target = target,
                TargetContext = targetContext,
                TemplateId = templateId,
                HasCallerParams = resolvedHasCallerParams,
            };
            if (resolvedHasCallerParams)
            {
                req.CallerParams = resolvedCallerParams;
            }

            _effectRequests.Publish(req);
        }

        private static bool TryAppendSpatialCallerParams(ref EffectConfigParams callerParams, in AbilityExecInstance inst)
        {
            bool added = false;
            if (inst.HasTargetPos != 0)
            {
                added |= callerParams.TryAddFloat(EffectParamKeys.TargetPosX, inst.TargetPosCm.X.ToFloat());
                added |= callerParams.TryAddFloat(EffectParamKeys.TargetPosY, inst.TargetPosCm.Y.ToFloat());
            }

            if (inst.HasTargetOriginPos != 0)
            {
                added |= callerParams.TryAddFloat(EffectParamKeys.TargetOriginX, inst.TargetOriginPosCm.X.ToFloat());
                added |= callerParams.TryAddFloat(EffectParamKeys.TargetOriginY, inst.TargetOriginPosCm.Y.ToFloat());
            }

            return added;
        }

        // 鈹€鈹€ Tag Clip (add at start, auto-remove via TimedTag) 鈹€鈹€

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

        // 鈹€鈹€ Tag Signal (instant add/remove) 鈹€鈹€

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

        // 鈹€鈹€ Event Signal 鈹€鈹€

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

        // 鈹€鈹€ Gate enter / process 鈹€鈹€

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
                        if (World.IsAlive(resp.TargetContext))
                        {
                            inst.TargetContext = resp.TargetContext;
                        }
                        if (resp.TryGetWorldPoint(out var worldPoint))
                        {
                            inst.TargetPosCm = Fix64Vec2.FromInt(worldPoint.X, worldPoint.Y);
                            inst.HasTargetPos = 1;
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

        // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Toggle Helpers 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        /// <summary>
        /// Activate toggle: add toggle tag and apply infinite active effects.
        /// Called when the activate timeline completes successfully.
        /// </summary>
        private void ActivateToggle(Entity actor, in AbilityToggleSpec toggleSpec)
        {
            if (!World.IsAlive(actor)) return;
            EnsureTagComponents(actor);
            if (!World.Has<DirtyFlags>(actor)) World.Add(actor, new DirtyFlags());

            ref var tags = ref World.Get<GameplayTagContainer>(actor);
            if (tags.HasTag(toggleSpec.ToggleTagId)) return;

            ref var counts = ref World.Get<TagCountContainer>(actor);
            ref var dirty = ref World.Get<DirtyFlags>(actor);
            _tagOps.AddTag(ref tags, ref counts, toggleSpec.ToggleTagId, ref dirty);
            
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
        private void DeactivateToggle(Entity actor,
            in AbilityToggleSpec toggleSpec, int slotIndex, int abilityId, Entity targetEntity)
        {
            EnsureTagComponents(actor);
            if (!World.Has<DirtyFlags>(actor)) World.Add(actor, new DirtyFlags());

            ref var tags = ref World.Get<GameplayTagContainer>(actor);
            ref var counts = ref World.Get<TagCountContainer>(actor);
            ref var dirty = ref World.Get<DirtyFlags>(actor);
            _tagOps.RemoveTag(ref tags, ref counts, toggleSpec.ToggleTagId, ref dirty);
            
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
                // No deactivate timeline 锟?instant deactivation, just complete the order
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

        // 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€ Helpers 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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



