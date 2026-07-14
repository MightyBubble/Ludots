using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Association;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression;
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
        public const string TimedTagCapacityExceededError = "GAS.ABILITY_EXEC.ERR.TimedTagCapacityExceeded";

        private readonly IClock _clock;
        private readonly GameplayEventBus _eventBus;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly InputRequestQueue _inputRequests;
        private readonly InputResponseBuffer _inputResponses;
        private readonly EffectRequestQueue _effectRequests;
        private readonly GasPresentationEventBuffer _presentationEvents;
        private readonly EffectPhaseExecutor _phaseExecutor;
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly TagOps _tagOps;
        private readonly ProgressionRequirementEvaluator _progressionRequirements;

        private readonly int _castAbilityOrderTypeId;
        private readonly int _castAbilityStartOrderTypeId;

        private static readonly QueryDescription _execQuery = new QueryDescription()
            .WithAll<AbilityExecInstance, AbilityStateBuffer>();

        // Query for entities with newly activated CastAbility orders (OrderBuffer + Blackboard driven)
        private static readonly QueryDescription _newOrderQuery = new QueryDescription()
            .WithAll<OrderBuffer, BlackboardIntBuffer, AbilityStateBuffer>()
            .WithNone<AbilityExecInstance>();

        private readonly Entity[] _execEntities;
        private int _execEntityCount;
        private bool _sliceActive;
        private int _cursor;
        private readonly Entity _runtimeStateEntity;

        public int MaxWorkUnitsPerSlice { get; set; } = int.MaxValue;
        public int SnapshotCapacity => _execEntities.Length;
        public int LastSliceProcessed { get; private set; }
        public int DeferredEntityCount => _sliceActive ? _execEntityCount - _cursor : 0;

        public AbilityExecSystem(
            World world,
            IClock clock,
            InputRequestQueue inputRequests,
            InputResponseBuffer inputResponses,
            EffectRequestQueue effectRequests,
            int snapshotCapacity,
            AbilityDefinitionRegistry abilityDefinitions = null,
            GameplayEventBus eventBus = null,
            int castAbilityOrderTypeId = 0,
            int castAbilityStartOrderTypeId = 0,
            GasPresentationEventBuffer presentationEvents = null,
            EffectPhaseExecutor phaseExecutor = null,
            GraphProgramRegistry graphPrograms = null,
            IGraphRuntimeApi graphApi = null,
            TagOps tagOps = null,
            OrderTypeRegistry orderTypeRegistry = null,
            ProgressionRequirementEvaluator progressionRequirements = null,
            int maxWorkUnitsPerSlice = int.MaxValue)
            : base(world)
        {
            if (snapshotCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshotCapacity));
            }

            _execEntities = new Entity[snapshotCapacity];
            _clock = clock;
            _inputRequests = inputRequests;
            _inputResponses = inputResponses;
            _effectRequests = effectRequests;
            _abilityDefinitions = abilityDefinitions;
            _eventBus = eventBus;
            _castAbilityOrderTypeId = castAbilityOrderTypeId;
            _castAbilityStartOrderTypeId = castAbilityStartOrderTypeId;
            _presentationEvents = presentationEvents;
            _phaseExecutor = phaseExecutor;
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            _orderTypeRegistry = orderTypeRegistry;
            _progressionRequirements = progressionRequirements;
            MaxWorkUnitsPerSlice = maxWorkUnitsPerSlice;
            _runtimeStateEntity = world.Create(new AbilityExecRuntimeState
            {
                SnapshotCapacity = snapshotCapacity,
                MaxWorkUnitsPerSlice = maxWorkUnitsPerSlice,
            });
        }

        /// <summary>
        /// Maximum re-scan iterations to prevent infinite loops when abilities
        /// complete and promote new ones repeatedly in the same frame.
        /// </summary>
        private const int MaxRescanIterations = 4;

        public override void Update(in float dt)
        {
            while (!UpdateSlice(dt, int.MaxValue)) { }
            
            // After Phase 2 finishes abilities (which calls NotifyOrderComplete and
            // promotes next queued order / activates tags), re-run Phase 1 to pick up
            // newly promoted orders in the same frame. Without this, there would be 
            // a one-frame delay between ability completion and the next queued ability starting.
            for (int rescan = 0; rescan < MaxRescanIterations; rescan++)
            {
                if (!HasAbilityActivationOrderType) break;
                int newCount = World.CountEntities(in _newOrderQuery);
                if (newCount == 0) break;
                while (!UpdateSlice(dt, int.MaxValue)) { }
            }
        }

        public bool UpdateSlice(float dt, int timeBudgetMs)
        {
            int workUnits = 0;
            LastSliceProcessed = 0;

            // Phase 1: Query entities with active CastAbility order + Blackboard (no AbilityExecInstance yet)
            if (HasAbilityActivationOrderType)
            {
                int newCount = World.CountEntities(in _newOrderQuery);
                if (newCount > _execEntities.Length)
                {
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.SnapshotCapacityExceeded: phase=Start, required={newCount}, capacity={_execEntities.Length}.");
                }
                World.GetEntities(in _newOrderQuery, _execEntities);
                for (int i = 0; i < newCount; i++)
                {
                    if (workUnits >= MaxWorkUnitsPerSlice)
                    {
                        PublishRuntimeState();
                        return false;
                    }

                    workUnits++;
                    LastSliceProcessed++;
                    var actor = _execEntities[i];
                    if (!World.IsAlive(actor)) continue;
                    
                    ref var orderBuffer = ref World.Get<OrderBuffer>(actor);
                    if (!orderBuffer.HasActive || !IsAbilityActivationOrderType(orderBuffer.ActiveOrder.Order.OrderTypeId)) continue;

                    ref var actorTags = ref World.TryGetRef<GameplayTagContainer>(actor, out bool hasActorTags);
                    
                    // Read slotIndex from Blackboard (Cast_SlotIndex = 110)
                    ref var bbInts = ref World.Get<BlackboardIntBuffer>(actor);
                    if (!bbInts.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex))
                    {
                        FailAbilityStart(actor, -1, 0, AbilityCastFailReason.InvalidSlot, OrderFailureReason.MissingBlackboardSlot);
                        continue;
                    }
                    if (slotIndex < 0)
                    {
                        FailAbilityStart(actor, slotIndex, 0, AbilityCastFailReason.InvalidSlot, OrderFailureReason.NegativeAbilitySlot);
                        continue;
                    }
                    
                    ref var abilities = ref World.Get<AbilityStateBuffer>(actor);
                    if ((uint)slotIndex >= (uint)abilities.Count)
                    {
                        FailAbilityStart(actor, slotIndex, 0, AbilityCastFailReason.InvalidSlot, OrderFailureReason.AbilitySlotOutOfRange);
                        continue;
                    }

                    // Resolve effective ability: granted override > base slot
                    bool hasForm = World.Has<AbilityFormSlotBuffer>(actor);
                    AbilityFormSlotBuffer formSlots = hasForm ? World.Get<AbilityFormSlotBuffer>(actor) : default;
                    bool hasItemGranted = World.Has<ItemGrantedSlotBuffer>(actor);
                    ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? World.Get<ItemGrantedSlotBuffer>(actor) : default;
                    bool hasGranted = World.Has<GrantedSlotBuffer>(actor);
                    GrantedSlotBuffer grantedSlots = hasGranted ? World.Get<GrantedSlotBuffer>(actor) : default;
                    var slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in itemGrantedSlots, hasItemGranted, in grantedSlots, hasGranted, slotIndex);
                    
                    // Read target from Blackboard (Cast_TargetEntity = 111)
                    Entity targetEntity = default;
                    if (World.Has<BlackboardEntityBuffer>(actor))
                    {
                        ref var bbEntities = ref World.Get<BlackboardEntityBuffer>(actor);
                        bbEntities.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out targetEntity);
                    }
                    Entity targetContext = World.IsAlive(orderBuffer.ActiveOrder.Order.TargetContext)
                        ? orderBuffer.ActiveOrder.Order.TargetContext
                        : default;

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
                            CancelAbilityStart(actor, targetEntity, slotIndex, slot.AbilityId, AbilityCastFailReason.BlockedByTag);
                            continue;
                        }
                        if (hasActorTags && !blockTags.BlockedAny.IsEmpty && actorTags.Intersects(in blockTags.BlockedAny))
                        {
                            CancelAbilityStart(actor, targetEntity, slotIndex, slot.AbilityId, AbilityCastFailReason.BlockedByTag);
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

                    AbilityExecSpec startSpec = hasAbilityDef
                        ? abilityDef.ExecSpec
                        : hasTemplateEntity && World.Has<AbilityExecSpec>(templateEntity)
                            ? World.Get<AbilityExecSpec>(templateEntity)
                            : default;
                    if (!hasAbilityDef && !hasTemplateEntity)
                    {
                        FailAbilityStart(actor, slotIndex, slot.AbilityId, AbilityCastFailReason.InvalidSlot, OrderFailureReason.AbilityDefinitionMissing);
                        continue;
                    }
                    int useRequirementId = ResolveUseProgressionRequirementId(hasAbilityDef, in abilityDef, hasTemplateEntity, templateEntity);
                    bool pendingProgressionUseRequirement = false;
                    if (useRequirementId > 0)
                    {
                        bool requiresExplicitScope = RequiresExplicitScope(useRequirementId);
                        if (requiresExplicitScope &&
                            !World.IsAlive(targetContext) &&
                            AbilityCanResolveTargetContextBeforeSideEffects(in startSpec))
                        {
                            pendingProgressionUseRequirement = true;
                        }
                        else if (!EvaluateProgressionRequirement(actor, targetEntity, targetContext, useRequirementId))
                        {
                            CancelAbilityStart(actor, targetEntity, slotIndex, slot.AbilityId, AbilityCastFailReason.PreconditionFailed);
                            continue;
                        }
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
                            CancelAbilityStart(actor, targetEntity, slotIndex, slot.AbilityId, AbilityCastFailReason.PreconditionFailed);
                            continue;
                        }
                    }

                    if (!World.Has<AbilityExecInstance>(actor))
                    {
                        World.Add(actor, new AbilityExecInstance());
                    }

                    GasClockId defaultClockId = startSpec.ClockId != 0 ? startSpec.ClockId : GasClockId.Step;

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
                    exec.TargetContext = targetContext;
                    exec.TargetPosCm = targetPosCm;
                    exec.HasTargetPos = (byte)(hasTargetPos ? 1 : 0);
                    exec.TargetOriginPosCm = targetOriginPosCm;
                    exec.HasTargetOriginPos = (byte)(hasTargetOriginPos ? 1 : 0);
                    exec.State = AbilityExecRunState.Running;
                    exec.CurrentTick = 0;
                    exec.StartAbsoluteTick = ClockNow(defaultClockId, actor);
                    exec.NextItemIndex = 0;
                    exec.GateDeadline = 0;
                    exec.WaitTagId = 0;
                    exec.WaitRequestId = 0;
                    exec.ActiveClockId = defaultClockId;
                    exec.IsToggleDeactivating = false;
                    exec.PendingProgressionUseRequirement = (byte)(pendingProgressionUseRequirement ? 1 : 0);
                    exec.PendingProgressionRequirementId = pendingProgressionUseRequirement ? useRequirementId : 0;

                    PublishCastStartedAndCommitted(actor, targetEntity, slotIndex, slot.AbilityId);
                }
            }

            // Phase 2: Advance all active exec instances
            if (!_sliceActive)
            {
                _sliceActive = true;
                _cursor = 0;
                _execEntityCount = World.CountEntities(in _execQuery);
                if (_execEntityCount > _execEntities.Length)
                {
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.SnapshotCapacityExceeded: phase=Advance, required={_execEntityCount}, capacity={_execEntities.Length}.");
                }
                World.GetEntities(in _execQuery, _execEntities);
            }

            while (_cursor < _execEntityCount)
            {
                if (workUnits >= MaxWorkUnitsPerSlice)
                {
                    PublishRuntimeState();
                    return false;
                }

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
                    if (_orderTypeRegistry != null)
                    {
                        OrderSubmitter.FinalizeCurrent(World, actor, _orderTypeRegistry, OrderTerminalState.Failed, OrderFailureReason.AbilitySlotOutOfRange);
                    }
                    World.Remove<AbilityExecInstance>(actor);
                    workUnits++;
                    continue;
                }

                // Resolve effective ability: granted override > base slot
                bool hasFormP2 = World.Has<AbilityFormSlotBuffer>(actor);
                AbilityFormSlotBuffer formSlotsP2 = hasFormP2 ? World.Get<AbilityFormSlotBuffer>(actor) : default;
                bool hasItemGrantedP2 = World.Has<ItemGrantedSlotBuffer>(actor);
                ItemGrantedSlotBuffer itemGrantedSlotsP2 = hasItemGrantedP2 ? World.Get<ItemGrantedSlotBuffer>(actor) : default;
                bool hasGrantedP2 = World.Has<GrantedSlotBuffer>(actor);
                GrantedSlotBuffer grantedSlotsP2 = hasGrantedP2 ? World.Get<GrantedSlotBuffer>(actor) : default;
                var slot = AbilitySlotResolver.Resolve(in abilities, in formSlotsP2, hasFormP2, in itemGrantedSlotsP2, hasItemGrantedP2, in grantedSlotsP2, hasGrantedP2, instance.AbilitySlot);

                AbilityExecSpec spec;
                AbilityExecCallerParamsPool callerPool = default;
                bool hasCallerPool = false;
                AbilityOnActivateEffects onActivateEffects = default;
                bool hasOnActivate = false;

                AbilityDefinition def = default;
                bool hasDefinition = slot.AbilityId > 0 &&
                    _abilityDefinitions != null &&
                    _abilityDefinitions.TryGet(slot.AbilityId, out def);
                Entity templateEntity = default;
                bool hasTemplateEntity = false;
                if (slot.TemplateEntityId > 0)
                {
                    templateEntity = EntityUtil.Reconstruct(slot.TemplateEntityId, slot.TemplateEntityWorldId, slot.TemplateEntityVersion);
                    hasTemplateEntity = World.IsAlive(templateEntity);
                }

                if (!hasDefinition && (!hasTemplateEntity || !World.Has<AbilityExecSpec>(templateEntity)))
                {
                    if (_orderTypeRegistry != null)
                    {
                        OrderSubmitter.FinalizeCurrent(World, actor, _orderTypeRegistry, OrderTerminalState.Failed, OrderFailureReason.AbilityDefinitionMissing);
                    }
                    World.Remove<AbilityExecInstance>(actor);
                    workUnits++;
                    continue;
                }

                // Toggle deactivate uses the DeactivateExecSpec instead of normal ExecSpec
                if (hasDefinition && instance.IsToggleDeactivating && def.HasToggleSpec)
                {
                    spec = def.ToggleSpec.DeactivateExecSpec;
                }
                else if (hasDefinition)
                {
                    spec = def.ExecSpec;
                }
                else
                {
                    spec = World.Get<AbilityExecSpec>(templateEntity);
                }
                if (hasDefinition)
                {
                    callerPool = def.ExecCallerParamsPool;
                    hasCallerPool = def.HasExecCallerParamsPool;
                    hasOnActivate = def.HasOnActivateEffects;
                    onActivateEffects = def.OnActivateEffects;
                }
                else
                {
                    if (World.Has<AbilityExecCallerParamsPool>(templateEntity))
                    {
                        callerPool = World.Get<AbilityExecCallerParamsPool>(templateEntity);
                        hasCallerPool = true;
                    }

                    if (World.Has<AbilityOnActivateEffects>(templateEntity))
                    {
                        onActivateEffects = World.Get<AbilityOnActivateEffects>(templateEntity);
                        hasOnActivate = true;
                    }
                }

                // Interrupt check
                ref var actorTags = ref World.TryGetRef<GameplayTagContainer>(actor, out bool hasActorTags);
                if (hasActorTags && !spec.InterruptAny.IsEmpty && actorTags.Intersects(in spec.InterruptAny))
                {
                    instance.State = AbilityExecRunState.Interrupted;
                }

                // Tick advancement
                if (instance.State == AbilityExecRunState.Running)
                {
                    int now = ClockNow(instance.ActiveClockId, actor);
                    instance.CurrentTick = now - instance.StartAbsoluteTick;
                    AdvanceItems(actor, ref spec, ref callerPool, hasCallerPool, hasOnActivate, ref onActivateEffects, ref instance);
                }
                else if (instance.State == AbilityExecRunState.GateWaiting)
                {
                    ProcessGate(actor, ref spec, ref instance);
                }

                // Cleanup terminal states
                if (instance.State == AbilityExecRunState.Finished ||
                    instance.State == AbilityExecRunState.Interrupted ||
                    instance.State == AbilityExecRunState.Failed)
                {
                    if (instance.State != AbilityExecRunState.Failed)
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
                    }
                    
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
                LastSliceProcessed++;
            }

            _sliceActive = false;
            PublishRuntimeState();
            return true;
        }

        public void ResetSlice()
        {
            _sliceActive = false;
            _cursor = 0;
            _execEntityCount = 0;
            PublishRuntimeState();
        }

        private void PublishRuntimeState()
        {
            if (!World.IsAlive(_runtimeStateEntity)) return;
            World.Set(_runtimeStateEntity, new AbilityExecRuntimeState
            {
                ProcessedLastSlice = LastSliceProcessed,
                DeferredEntityCount = DeferredEntityCount,
                SnapshotEntityCount = _execEntityCount,
                SnapshotCapacity = _execEntities.Length,
                MaxWorkUnitsPerSlice = MaxWorkUnitsPerSlice,
            });
        }

        private bool HasAbilityActivationOrderType => _castAbilityOrderTypeId > 0 || _castAbilityStartOrderTypeId > 0;

        private bool IsAbilityActivationOrderType(int orderTypeId)
        {
            return orderTypeId == _castAbilityOrderTypeId ||
                   (_castAbilityStartOrderTypeId > 0 && orderTypeId == _castAbilityStartOrderTypeId);
        }

        private int ResolveUseProgressionRequirementId(bool hasAbilityDef, in AbilityDefinition abilityDef, bool hasTemplateEntity, Entity templateEntity)
        {
            if (hasAbilityDef && abilityDef.HasUseProgressionRequirement)
            {
                return abilityDef.UseProgressionRequirementId;
            }

            if (hasTemplateEntity && World.Has<AbilityProgressionRequirements>(templateEntity))
            {
                return World.Get<AbilityProgressionRequirements>(templateEntity).UseRequirementId;
            }

            return 0;
        }

        private bool RequiresExplicitScope(int requirementId)
        {
            if (_progressionRequirements == null)
            {
                throw new InvalidOperationException("Ability progression requirement is configured, but ProgressionRequirementEvaluator is not registered.");
            }

            return _progressionRequirements.RequiresExplicitScope(requirementId);
        }

        private bool EvaluateProgressionRequirement(Entity actor, Entity subject, Entity explicitScopeHost, int requirementId)
        {
            if (requirementId <= 0)
            {
                return true;
            }

            if (_progressionRequirements == null)
            {
                throw new InvalidOperationException("Ability progression requirement is configured, but ProgressionRequirementEvaluator is not registered.");
            }

            Entity resolvedSubject = World.IsAlive(subject) ? subject : actor;
            Entity resolvedExplicitScopeHost = World.IsAlive(explicitScopeHost)
                ? explicitScopeHost
                : default;
            var context = new RoleResolverContext(
                actor: actor,
                subject: resolvedSubject,
                explicitScopeHost: resolvedExplicitScopeHost);
            return _progressionRequirements.Evaluate(requirementId, in context);
        }

        private static bool AbilityCanResolveTargetContextBeforeSideEffects(in AbilityExecSpec spec)
        {
            for (int i = 0; i < spec.ItemCount; i++)
            {
                ExecItemKind kind = spec.GetKind(i);
                if (kind == ExecItemKind.InputGate || kind == ExecItemKind.TargetCollectionGate)
                {
                    return true;
                }

                if (kind == ExecItemKind.None)
                {
                    continue;
                }

                return false;
            }

            return false;
        }

        private bool TrySatisfyPendingProgressionUseRequirement(Entity actor, ref AbilityExecInstance inst)
        {
            if (inst.PendingProgressionUseRequirement == 0)
            {
                return true;
            }

            if (EvaluateProgressionRequirement(actor, inst.Target, inst.TargetContext, inst.PendingProgressionRequirementId))
            {
                inst.PendingProgressionUseRequirement = 0;
                inst.PendingProgressionRequirementId = 0;
                return true;
            }

            FailPendingProgressionUseRequirement(actor, ref inst);
            return false;
        }

        private void FailPendingProgressionUseRequirement(Entity actor, ref AbilityExecInstance inst)
        {
            inst.State = AbilityExecRunState.Failed;
            inst.PendingProgressionUseRequirement = 0;
            inst.PendingProgressionRequirementId = 0;
            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                Target = inst.Target,
                AbilitySlot = inst.AbilitySlot,
                AbilityId = inst.AbilityId,
                FailReason = AbilityCastFailReason.PreconditionFailed
            });
        }

        private void CancelAbilityStart(Entity actor, Entity targetEntity, int slotIndex, int abilityId, AbilityCastFailReason reason)
        {
            if (_orderTypeRegistry != null)
            {
                OrderSubmitter.FinalizeCurrent(
                    World,
                    actor,
                    _orderTypeRegistry,
                    OrderTerminalState.Failed,
                    reason == AbilityCastFailReason.PreconditionFailed
                        ? OrderFailureReason.PreconditionFailed
                        : OrderFailureReason.ActivationBlocked);
            }

            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                Target = targetEntity,
                AbilitySlot = slotIndex,
                AbilityId = abilityId,
                FailReason = reason
            });
        }

        private void FailAbilityStart(
            Entity actor,
            int slotIndex,
            int abilityId,
            AbilityCastFailReason presentationReason,
            OrderFailureReason orderReason)
        {
            if (_orderTypeRegistry != null)
            {
                OrderSubmitter.FinalizeCurrent(World, actor, _orderTypeRegistry, OrderTerminalState.Failed, orderReason);
            }

            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                AbilitySlot = slotIndex,
                AbilityId = abilityId,
                FailReason = presentationReason
            });
        }

        // Item processing

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

                if (inst.PendingProgressionUseRequirement != 0 &&
                    kind != ExecItemKind.InputGate &&
                    kind != ExecItemKind.TargetCollectionGate)
                {
                    FailPendingProgressionUseRequirement(actor, ref inst);
                    return;
                }

                switch (kind)
                {
                    case ExecItemKind.End:
                        inst.State = AbilityExecRunState.Finished;
                        return;

                    // Clips
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

                    // Signals
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

                    // Gates
                    case ExecItemKind.InputGate:
                    case ExecItemKind.EventGate:
                    case ExecItemKind.TargetCollectionGate:
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

        // Effect dispatch (shared for EffectClip & EffectSignal)

        private void FireEffectItem(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecCallerParamsPool callerPool, bool hasCallerPool,
            ref AbilityExecInstance inst)
        {
            if (_effectRequests == null) return;

            int templateId = spec.GetTemplateId(idx);
            if (templateId <= 0) return;

            byte cpIdx = spec.GetCallerParamsIdx(idx);
            bool hasCp = hasCallerPool && cpIdx != 0xFF && cpIdx < callerPool.Count;

            var dispatchTarget = (ExecEffectDispatchTarget)spec.GetPayloadA(idx);

            Entity target = ResolveEffectDispatchTarget(actor, dispatchTarget, in inst);
            Entity targetContext = ResolveEffectDispatchTargetContext(dispatchTarget, in inst);
            PublishEffectRequest(actor, target, targetContext, templateId,
                hasCp ? callerPool.Get(cpIdx) : default, hasCp, in inst);
        }

        private Entity ResolveEffectDispatchTarget(Entity actor, ExecEffectDispatchTarget dispatchTarget, in AbilityExecInstance inst)
        {
            return dispatchTarget switch
            {
                ExecEffectDispatchTarget.Source => actor,
                ExecEffectDispatchTarget.TargetContext when World.IsAlive(inst.TargetContext) => inst.TargetContext,
                ExecEffectDispatchTarget.Target when World.IsAlive(inst.Target) => inst.Target,
                ExecEffectDispatchTarget.Default when World.IsAlive(inst.Target) => inst.Target,
                _ => actor,
            };
        }

        private Entity ResolveEffectDispatchTargetContext(ExecEffectDispatchTarget dispatchTarget, in AbilityExecInstance inst)
        {
            if (World.IsAlive(inst.TargetContext))
            {
                return inst.TargetContext;
            }

            return dispatchTarget == ExecEffectDispatchTarget.Source && World.IsAlive(inst.Target)
                ? inst.Target
                : default;
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

        // Tag Clip (add at start, auto-remove via TimedTag)

        private void FireTagClip(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            int tagId = spec.GetTagId(idx);
            if (tagId <= 0) return;
            int durationTicks = spec.GetDurationTicks(idx);
            GasClockId clockId = spec.GetClockId(idx);
            if ((byte)clockId == 0) clockId = inst.ActiveClockId;

            TagOps.RequireTagState(World, actor);
            if (durationTicks > 0 && !World.Has<TimedTagBuffer>(actor))
            {
                throw new InvalidOperationException("GAS.ABILITY_EXEC.ERR.MissingTimedTagBuffer");
            }
            if (durationTicks <= 0)
            {
                _tagOps.AddTag(World, actor, tagId);
                return;
            }

            int reservationIndex = World.Get<TimedTagBuffer>(actor).Count;
            int expireAt = ClockNow(clockId, actor) + durationTicks;
            if (!World.Get<TimedTagBuffer>(actor).TryAdd(tagId, expireAt, clockId))
            {
                throw new InvalidOperationException(TimedTagCapacityExceededError);
            }

            try
            {
                if (!_tagOps.AddTag(World, actor, tagId))
                {
                    World.Get<TimedTagBuffer>(actor).RemoveAtSwapBack(reservationIndex);
                }
            }
            catch
            {
                World.Get<TimedTagBuffer>(actor).RemoveAtSwapBack(reservationIndex);
                throw;
            }
        }

        // Tag Signal (instant add/remove)

        private void FireTagSignal(Entity actor, ref AbilityExecSpec spec, int idx)
        {
            int tagId = spec.GetTagId(idx);
            if (tagId <= 0) return;
            int payloadA = spec.GetPayloadA(idx);
            bool isRemove = payloadA == 1;

            if (isRemove)
            {
                _tagOps.RemoveTag(World, actor, tagId);
            }
            else
            {
                _tagOps.AddTag(World, actor, tagId);
            }
        }

        // Event Signal

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

        // Gate enter / process

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
                        Target = inst.Target,
                        Context = inst.TargetContext,
                    });
                    break;
                }

                case ExecItemKind.TargetCollectionGate:
                {
                    int requestId = spec.GetPayloadA(idx) != 0 ? spec.GetPayloadA(idx) : inst.OrderId;
                    inst.WaitRequestId = requestId;
                    _inputRequests?.TryEnqueue(new InputRequest
                    {
                        RequestId = requestId,
                        RequestTagId = spec.GetTagId(idx),
                        Source = actor,
                        Target = inst.Target,
                        Context = inst.TargetContext,
                    });
                    break;
                }

                case ExecItemKind.EventGate:
                {
                    inst.WaitTagId = spec.GetTagId(idx);
                    int deadlineTicks = spec.GetPayloadA(idx);
                    if (deadlineTicks > 0)
                    {
                        inst.GateDeadline = ClockNow(inst.ActiveClockId, actor) + deadlineTicks;
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
                        if (!TrySatisfyPendingProgressionUseRequirement(actor, ref inst))
                        {
                            return;
                        }
                        inst.WaitRequestId = 0;
                        inst.NextItemIndex++;
                        inst.State = AbilityExecRunState.Running;
                    }
                    break;
                }

                case ExecItemKind.TargetCollectionGate:
                {
                    if (_inputResponses == null) return;
                    if (_inputResponses.TryConsume(inst.WaitRequestId, out var resp))
                    {
                        if (World.IsAlive(resp.Target))
                        {
                            inst.Target = resp.Target;
                        }
                        if (World.IsAlive(resp.TargetContext))
                        {
                            inst.TargetContext = resp.TargetContext;
                        }
                        if (!TrySatisfyPendingProgressionUseRequirement(actor, ref inst))
                        {
                            return;
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
                        int now = ClockNow(inst.ActiveClockId, actor);
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

        // Toggle helpers

        /// <summary>
        /// Activate toggle: add toggle tag and apply infinite active effects.
        /// Called when the activate timeline completes successfully.
        /// </summary>
        private void ActivateToggle(Entity actor, in AbilityToggleSpec toggleSpec)
        {
            if (!World.IsAlive(actor)) return;
            TagOps.RequireTagState(World, actor);

            ref var tags = ref World.Get<GameplayTagContainer>(actor);
            if (tags.HasTag(toggleSpec.ToggleTagId)) return;

            _tagOps.AddTag(World, actor, toggleSpec.ToggleTagId);
            
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
            _tagOps.RemoveTag(World, actor, toggleSpec.ToggleTagId);
            
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
                exec.State = AbilityExecRunState.Running;
                exec.CurrentTick = 0;
                exec.StartAbsoluteTick = ClockNow(toggleSpec.DeactivateExecSpec.ClockId, actor);
                exec.NextItemIndex = 0;
                exec.GateDeadline = 0;
                exec.WaitTagId = 0;
                exec.WaitRequestId = 0;
                exec.ActiveClockId = toggleSpec.DeactivateExecSpec.ClockId;
                exec.IsToggleDeactivating = true;
                exec.PendingProgressionUseRequirement = 0;
                exec.PendingProgressionRequirementId = 0;
                
                PublishCastStartedAndCommitted(actor, targetEntity, slotIndex, abilityId);
            }
            else
            {
                // No deactivate timeline; instant deactivation, just complete the order.
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

        // Helpers

        private int ClockNow(GasClockId clockId, Entity actor)
        {
            return GasClockRuntime.Now(World, _clock, clockId, actor, "Ability execution clock");
        }

        private void PublishCastStartedAndCommitted(Entity actor, Entity targetEntity, int slotIndex, int abilityId)
        {
            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastStarted,
                Actor = actor,
                Target = targetEntity,
                AbilitySlot = slotIndex,
                AbilityId = abilityId
            });
            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastCommitted,
                Actor = actor,
                Target = targetEntity,
                AbilitySlot = slotIndex,
                AbilityId = abilityId
            });
        }

    }
}
