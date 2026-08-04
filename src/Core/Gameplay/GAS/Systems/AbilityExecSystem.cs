using System;
using System.Collections.Generic;
using Arch.Buffer;
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
        public const string ToggleActiveEffectQueueMissingError = "GAS.ABILITY_EXEC.ERR.ToggleActiveEffectQueueMissing";
        public const string ToggleActiveEffectQueueFullError = "GAS.ABILITY_EXEC.ERR.ToggleActiveEffectQueueFull";

        private readonly IClock _clock;
        private readonly GameplayEventBus? _eventBus;
        private readonly AbilityDefinitionRegistry? _abilityDefinitions;
        private readonly OrderTypeRegistry? _orderTypeRegistry;
        private readonly InputRequestQueue _inputRequests;
        private readonly InputResponseBuffer _inputResponses;
        private readonly EffectRequestQueue _effectRequests;
        private readonly GasPresentationEventBuffer? _presentationEvents;
        private readonly GraphProgramRegistry? _graphPrograms;
        private readonly IGraphRuntimeApi? _graphApi;
        private readonly TagOps _tagOps;
        private readonly ProgressionRequirementEvaluator? _progressionRequirements;
        private readonly CommandBuffer _structuralCommands = new();

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
            AbilityDefinitionRegistry? abilityDefinitions = null,
            GameplayEventBus? eventBus = null,
            int castAbilityOrderTypeId = 0,
            int castAbilityStartOrderTypeId = 0,
            GasPresentationEventBuffer? presentationEvents = null,
            GraphProgramRegistry? graphPrograms = null,
            IGraphRuntimeApi? graphApi = null,
            TagOps? tagOps = null,
            OrderTypeRegistry? orderTypeRegistry = null,
            ProgressionRequirementEvaluator? progressionRequirements = null,
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

            // After Phase 2 finalizes abilities (which promotes next queued orders and
            // activates tags), re-run Phase 1 to pick up
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
                        PlaybackStructuralCommands();
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

                    if (!AbilitySlotResolver.TryResolve(World, actor, slotIndex, out AbilitySlotState slot))
                    {
                        FailAbilityStart(actor, slotIndex, 0, AbilityCastFailReason.InvalidSlot, OrderFailureReason.AbilitySlotOutOfRange);
                        continue;
                    }

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
                    // can always be turned off, even while its reactivation lockout tag is present.
                    if (hasAbilityDef &&
                        abilityDef.HasToggleSpec &&
                        abilityDef.ToggleSpec.ToggleTagId > 0 &&
                        hasActorTags &&
                        actorTags.HasTag(abilityDef.ToggleSpec.ToggleTagId))
                    {
                        DeactivateToggle(
                            actor,
                            in abilityDef.ToggleSpec,
                            orderBuffer.ActiveOrder.Order.OrderId,
                            slotIndex,
                            slot.AbilityId,
                            targetEntity);
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
                        if (!AbilityActivationBlockTagEvaluator.Passes(World, actor, _tagOps, in blockTags))
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

                    EnsurePresentationEventCapacity(2, GasPresentationEventKind.CastStarted);

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

                    var exec = new AbilityExecInstance
                    {
                        OrderId = orderId,
                        AbilitySlot = slotIndex,
                        AbilityId = slot.AbilityId,
                        Target = targetEntity,
                        TargetContext = targetContext,
                        TargetPosCm = targetPosCm,
                        HasTargetPos = (byte)(hasTargetPos ? 1 : 0),
                        TargetOriginPosCm = targetOriginPosCm,
                        HasTargetOriginPos = (byte)(hasTargetOriginPos ? 1 : 0),
                        State = AbilityExecRunState.Running,
                        TerminalFailureReason = OrderFailureReason.None,
                        CurrentTick = 0,
                        StartAbsoluteTick = ClockNow(defaultClockId, actor),
                        NextItemIndex = 0,
                        GateDeadline = 0,
                        WaitTagId = 0,
                        WaitRequestId = 0,
                        ActiveClockId = defaultClockId,
                        IsToggleDeactivating = false,
                        PendingProgressionUseRequirement = (byte)(pendingProgressionUseRequirement ? 1 : 0),
                        PendingProgressionRequirementId = pendingProgressionUseRequirement ? useRequirementId : 0,
                    };
                    PublishCastStartedAndCommitted(actor, targetEntity, slotIndex, slot.AbilityId);
                    AbilityExecCallerParamsPool startCallerPool = default;
                    bool hasStartCallerPool = false;
                    if (hasAbilityDef)
                    {
                        startCallerPool = abilityDef.ExecCallerParamsPool;
                        hasStartCallerPool = abilityDef.HasExecCallerParamsPool;
                    }
                    else if (hasTemplateEntity && World.Has<AbilityExecCallerParamsPool>(templateEntity))
                    {
                        startCallerPool = World.Get<AbilityExecCallerParamsPool>(templateEntity);
                        hasStartCallerPool = true;
                    }

                    if (workUnits >= MaxWorkUnitsPerSlice)
                    {
                        _structuralCommands.Add(actor, exec);
                        PlaybackStructuralCommands();
                        PublishRuntimeState();
                        return false;
                    }

                    bool storedForContinuation = false;
                    try
                    {
                        AdvanceItems(actor, ref startSpec, ref startCallerPool, hasStartCallerPool, ref exec);
                        if (IsTerminalState(exec.State))
                        {
                            FinalizeTerminalExecution(actor, in exec, removeStoredInstance: false);
                        }
                        else
                        {
                            _structuralCommands.Add(actor, exec);
                            storedForContinuation = true;
                        }
                    }
                    catch
                    {
                        if (!storedForContinuation)
                        {
                            _structuralCommands.Add(actor, exec);
                        }
                        PlaybackStructuralCommands();
                        PublishRuntimeState();
                        throw;
                    }
                }
            }

            PlaybackStructuralCommands();

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
                    PlaybackStructuralCommands();
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

                // An interrupting order finalizes the old order before replacing it. The old
                // execution snapshot may still exist until this phase; it must be discarded
                // without touching the replacement order or continuing timeline side effects.
                if (instance.OrderId > 0 && !IsExecutionOrderCurrent(actor, instance.OrderId))
                {
                    AbilityExecInstance cancelledInstance = instance;
                    EnsurePresentationEventCapacity(1, GasPresentationEventKind.CastInterrupted);
                    PublishCastTerminalEvent(actor, in cancelledInstance, GasPresentationEventKind.CastInterrupted);
                    _structuralCommands.Remove<AbilityExecInstance>(in actor);
                    workUnits++;
                    LastSliceProcessed++;
                    continue;
                }

                if (!AbilitySlotResolver.TryResolve(World, actor, instance.AbilitySlot, out AbilitySlotState slot))
                {
                    AbilityExecInstance failedInstance = instance;
                    FailActiveExecution(
                        actor,
                        in failedInstance,
                        AbilityCastFailReason.InvalidSlot,
                        OrderFailureReason.AbilitySlotOutOfRange);
                    workUnits++;
                    LastSliceProcessed++;
                    continue;
                }

                AbilityExecSpec spec;
                AbilityExecCallerParamsPool callerPool = default;
                bool hasCallerPool = false;

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
                    AbilityExecInstance failedInstance = instance;
                    FailActiveExecution(
                        actor,
                        in failedInstance,
                        AbilityCastFailReason.InvalidSlot,
                        OrderFailureReason.AbilityDefinitionMissing);
                    workUnits++;
                    LastSliceProcessed++;
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
                }
                else
                {
                    if (World.Has<AbilityExecCallerParamsPool>(templateEntity))
                    {
                        callerPool = World.Get<AbilityExecCallerParamsPool>(templateEntity);
                        hasCallerPool = true;
                    }
                }

                // Interrupt check
                ref var actorTags = ref World.TryGetRef<GameplayTagContainer>(actor, out bool hasActorTags);
                if (hasActorTags && !spec.InterruptAny.IsEmpty && actorTags.Intersects(in spec.InterruptAny))
                {
                    EnsureTerminalTransitionCapacity(actor, in instance, OrderTerminalState.Cancelled, GasPresentationEventKind.CastInterrupted);
                    instance.State = AbilityExecRunState.Interrupted;
                    instance.TerminalFailureReason = OrderFailureReason.Interrupted;
                }

                // Tick advancement
                if (instance.State == AbilityExecRunState.Running)
                {
                    int now = ClockNow(instance.ActiveClockId, actor);
                    instance.CurrentTick = now - instance.StartAbsoluteTick;
                    AdvanceItems(actor, ref spec, ref callerPool, hasCallerPool, ref instance);
                }
                else if (instance.State == AbilityExecRunState.GateWaiting)
                {
                    ProcessGate(actor, ref spec, ref instance);
                }

                // Cleanup terminal states
                if (IsTerminalState(instance.State))
                {
                    AbilityExecInstance terminalInstance = instance;
                    FinalizeTerminalExecution(actor, in terminalInstance, removeStoredInstance: true);
                }
                else
                {
                    World.Get<AbilityExecInstance>(actor) = instance;
                }

                workUnits++;
                LastSliceProcessed++;
            }

            _sliceActive = false;
            PlaybackStructuralCommands();
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
            EnsureTerminalTransitionCapacity(actor, in inst, OrderTerminalState.Failed, GasPresentationEventKind.CastFailed);
            inst.State = AbilityExecRunState.Failed;
            inst.TerminalFailureReason = OrderFailureReason.PreconditionFailed;
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
            OrderFailureReason orderReason = reason == AbilityCastFailReason.PreconditionFailed
                ? OrderFailureReason.PreconditionFailed
                : OrderFailureReason.ActivationBlocked;
            EnsureOrderTerminalResultCapacity(actor, OrderTerminalState.Failed);
            EnsurePresentationEventCapacity(1, GasPresentationEventKind.CastFailed);
            if (_orderTypeRegistry != null)
            {
                if (!OrderSubmitter.FinalizeCurrent(
                        World,
                        actor,
                        _orderTypeRegistry,
                        OrderTerminalState.Failed,
                        orderReason))
                {
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.StartOrderMissing: actor={actor.Id}, abilityId={abilityId}, reason={orderReason}.");
                }
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
            EnsureOrderTerminalResultCapacity(actor, OrderTerminalState.Failed);
            EnsurePresentationEventCapacity(1, GasPresentationEventKind.CastFailed);
            if (_orderTypeRegistry != null)
            {
                if (!OrderSubmitter.FinalizeCurrent(World, actor, _orderTypeRegistry, OrderTerminalState.Failed, orderReason))
                {
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.StartOrderMissing: actor={actor.Id}, abilityId={abilityId}, reason={orderReason}.");
                }
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

        private void FailActiveExecution(
            Entity actor,
            in AbilityExecInstance instance,
            AbilityCastFailReason presentationReason,
            OrderFailureReason orderReason)
        {
            EnsureTerminalTransitionCapacity(actor, in instance, OrderTerminalState.Failed, GasPresentationEventKind.CastFailed);
            if (_orderTypeRegistry != null && instance.OrderId > 0)
            {
                if (!OrderSubmitter.FinalizeCurrent(
                        World,
                        actor,
                        _orderTypeRegistry,
                        OrderTerminalState.Failed,
                        orderReason))
                {
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.FailedOrderMissing: actor={actor.Id}, orderId={instance.OrderId}, reason={orderReason}.");
                }
            }

            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                Target = instance.Target,
                AbilitySlot = instance.AbilitySlot,
                AbilityId = instance.AbilityId,
                FailReason = presentationReason
            });
            _structuralCommands.Remove<AbilityExecInstance>(in actor);
        }

        private void MarkActiveExecutionFailed(
            Entity actor,
            ref AbilityExecInstance instance,
            AbilityCastFailReason presentationReason,
            OrderFailureReason orderReason)
        {
            EnsureTerminalTransitionCapacity(actor, in instance, OrderTerminalState.Failed, GasPresentationEventKind.CastFailed);
            instance.State = AbilityExecRunState.Failed;
            instance.TerminalFailureReason = orderReason;
            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.CastFailed,
                Actor = actor,
                Target = instance.Target,
                AbilitySlot = instance.AbilitySlot,
                AbilityId = instance.AbilityId,
                FailReason = presentationReason
            });
        }

        // Item processing

        private void AdvanceItems(Entity actor, ref AbilityExecSpec spec,
            ref AbilityExecCallerParamsPool callerPool, bool hasCallerPool,
            ref AbilityExecInstance inst)
        {
            if (!CanPublishDueEffectItems(actor, ref spec, in inst, out OrderFailureReason dispatchFailure))
            {
                MarkActiveExecutionFailed(
                    actor,
                    ref inst,
                    AbilityCastFailReason.PreconditionFailed,
                    dispatchFailure);
                return;
            }

            for (int guard = 0; guard < AbilityExecSpec.MAX_ITEMS; guard++)
            {
                if (inst.NextItemIndex >= spec.ItemCount)
                {
                    EnsureTerminalTransitionCapacity(actor, in inst, OrderTerminalState.Completed, GasPresentationEventKind.CastFinished);
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
                        EnsureTerminalTransitionCapacity(actor, in inst, OrderTerminalState.Completed, GasPresentationEventKind.CastFinished);
                        inst.State = AbilityExecRunState.Finished;
                        return;

                    // Clips
                    case ExecItemKind.EffectClip:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        if (!FireEffectItem(actor, ref spec, idx, ref callerPool, hasCallerPool, ref inst))
                        {
                            return;
                        }
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagClip:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        FireTagClip(actor, ref spec, idx, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagClipTarget:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        if (!World.IsAlive(inst.Target))
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                            return;
                        }
                        FireTagClip(inst.Target, ref spec, idx, ref inst);
                        inst.NextItemIndex++;
                        continue;

                    // Signals
                    case ExecItemKind.EffectSignal:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        if (!FireEffectItem(actor, ref spec, idx, ref callerPool, hasCallerPool, ref inst))
                        {
                            return;
                        }
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.EventSignal:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        if (!FireEventSignal(actor, ref spec, idx, ref inst))
                        {
                            return;
                        }
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagSignal:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        FireTagSignal(actor, ref spec, idx);
                        inst.NextItemIndex++;
                        continue;

                    case ExecItemKind.TagSignalTarget:
                        EnsurePotentialTimelineTerminalCapacity(actor, in inst);
                        if (!World.IsAlive(inst.Target))
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                            return;
                        }
                        FireTagSignal(inst.Target, ref spec, idx);
                        inst.NextItemIndex++;
                        continue;

                    // Gates
                    case ExecItemKind.InputGate:
                    case ExecItemKind.EventGate:
                    case ExecItemKind.TargetCollectionGate:
                        if (!EnterGate(actor, ref spec, idx, ref inst))
                        {
                            return;
                        }
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
            EnsureTerminalTransitionCapacity(actor, in inst, OrderTerminalState.Completed, GasPresentationEventKind.CastFinished);
            inst.State = AbilityExecRunState.Finished;
        }

        private bool CanPublishDueEffectItems(
            Entity actor,
            ref AbilityExecSpec spec,
            in AbilityExecInstance inst,
            out OrderFailureReason failureReason)
        {
            failureReason = OrderFailureReason.None;
            int requiredCapacity = 0;
            for (int index = inst.NextItemIndex; index < spec.ItemCount; index++)
            {
                ExecItemKind kind = spec.GetKind(index);
                if (spec.GetTick(index) > inst.CurrentTick ||
                    kind == ExecItemKind.End ||
                    kind == ExecItemKind.InputGate ||
                    kind == ExecItemKind.EventGate ||
                    kind == ExecItemKind.TargetCollectionGate)
                {
                    break;
                }
                if (inst.PendingProgressionUseRequirement != 0)
                {
                    break;
                }
                if (kind != ExecItemKind.EffectClip && kind != ExecItemKind.EffectSignal)
                {
                    continue;
                }
                if (spec.GetTemplateId(index) <= 0 ||
                    !TryResolveEffectDispatchTarget(
                        actor,
                        (ExecEffectDispatchTarget)spec.GetPayloadA(index),
                        in inst,
                        out _))
                {
                    failureReason = OrderFailureReason.PreconditionFailed;
                    return false;
                }
                requiredCapacity++;
            }

            if (requiredCapacity == 0)
            {
                return true;
            }
            if (_effectRequests == null || requiredCapacity > _effectRequests.AvailableCapacity)
            {
                failureReason = OrderFailureReason.SubmissionQueueFull;
                return false;
            }
            return true;
        }

        // Effect dispatch (shared for EffectClip & EffectSignal)

        private bool FireEffectItem(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecCallerParamsPool callerPool, bool hasCallerPool,
            ref AbilityExecInstance inst)
        {
            if (_effectRequests == null)
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                return false;
            }

            if (_effectRequests.AvailableCapacity <= 0)
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                return false;
            }

            int templateId = spec.GetTemplateId(idx);
            if (templateId <= 0)
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                return false;
            }

            byte cpIdx = spec.GetCallerParamsIdx(idx);
            if (cpIdx != 0xFF && (!hasCallerPool || cpIdx >= callerPool.Count))
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                return false;
            }
            bool hasCp = hasCallerPool && cpIdx != 0xFF && cpIdx < callerPool.Count;
            EffectConfigParams resolvedCallerParams = hasCp ? callerPool.Get(cpIdx) : default;
            bool resolvedHasCallerParams = hasCp;
            bool hasRequestClock = false;
            GasClockId requestClockId = default;

            if (spec.GetKind(idx) == ExecItemKind.EffectClip)
            {
                if (EffectParamKeys.DurationTicks <= 0)
                {
                    throw new InvalidOperationException(
                        "GAS.ABILITY_EXEC.ERR.EffectParamKeysNotInitialized: key=_ep.durationTicks.");
                }

                if (resolvedCallerParams.TryGetRawValue(EffectParamKeys.DurationTicks, out _, out _))
                {
                    MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                    return false;
                }

                int durationTicks = spec.GetDurationTicks(idx);
                if (durationTicks < 0 ||
                    !resolvedCallerParams.TryAddInt(EffectParamKeys.DurationTicks, durationTicks))
                {
                    MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                    return false;
                }

                resolvedHasCallerParams = true;
                requestClockId = spec.GetClockId(idx);
                if ((byte)requestClockId == 0)
                {
                    requestClockId = inst.ActiveClockId;
                }
                hasRequestClock = true;
            }

            var dispatchTarget = (ExecEffectDispatchTarget)spec.GetPayloadA(idx);

            if (!TryResolveEffectDispatchTarget(actor, dispatchTarget, in inst, out Entity target))
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                return false;
            }
            Entity targetContext = ResolveEffectDispatchTargetContext(dispatchTarget, in inst);
            if (!PublishEffectRequest(actor, target, targetContext, templateId,
                    in resolvedCallerParams,
                    resolvedHasCallerParams,
                    requestClockId,
                    hasRequestClock,
                    in inst))
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                return false;
            }
            return true;
        }

        private bool TryResolveEffectDispatchTarget(
            Entity actor,
            ExecEffectDispatchTarget dispatchTarget,
            in AbilityExecInstance inst,
            out Entity target)
        {
            target = Entity.Null;
            switch (dispatchTarget)
            {
                case ExecEffectDispatchTarget.Source:
                    target = actor;
                    return true;

                case ExecEffectDispatchTarget.Target:
                    if (!World.IsAlive(inst.Target))
                    {
                        return false;
                    }
                    target = inst.Target;
                    return true;

                case ExecEffectDispatchTarget.TargetContext:
                    if (!World.IsAlive(inst.TargetContext))
                    {
                        return false;
                    }
                    target = inst.TargetContext;
                    return true;

                case ExecEffectDispatchTarget.Default:
                    if (World.IsAlive(inst.Target))
                    {
                        target = inst.Target;
                        return true;
                    }
                    if (inst.Target != default && inst.Target != Entity.Null)
                    {
                        return false;
                    }
                    target = actor;
                    return true;

                default:
                    return false;
            }
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

        private bool PublishEffectRequest(
            Entity source,
            Entity target,
            Entity targetContext,
            int templateId,
            in EffectConfigParams callerParams,
            bool hasCallerParams,
            GasClockId clockId,
            bool hasClockId,
            in AbilityExecInstance inst)
        {
            var resolvedCallerParams = callerParams;
            bool resolvedHasCallerParams = hasCallerParams;
            if (!TryAppendSpatialCallerParams(ref resolvedCallerParams, in inst, out bool appendedSpatialParams))
            {
                return false;
            }
            resolvedHasCallerParams |= appendedSpatialParams;

            var req = new EffectRequest
            {
                Source = source,
                Target = target,
                TargetContext = targetContext,
                TemplateId = templateId,
                ClockId = clockId,
                HasClockId = hasClockId,
                HasCallerParams = resolvedHasCallerParams,
            };
            if (resolvedHasCallerParams)
            {
                req.CallerParams = resolvedCallerParams;
            }

            _effectRequests.Publish(req);
            return true;
        }

        private static bool TryAppendSpatialCallerParams(
            ref EffectConfigParams callerParams,
            in AbilityExecInstance inst,
            out bool added)
        {
            added = false;
            if (inst.HasTargetPos != 0)
            {
                if (!callerParams.TryAddFloat(EffectParamKeys.TargetPosX, inst.TargetPosCm.X.ToFloat()) ||
                    !callerParams.TryAddFloat(EffectParamKeys.TargetPosY, inst.TargetPosCm.Y.ToFloat()))
                {
                    return false;
                }
                added = true;
            }

            if (inst.HasTargetOriginPos != 0)
            {
                if (!callerParams.TryAddFloat(EffectParamKeys.TargetOriginX, inst.TargetOriginPosCm.X.ToFloat()) ||
                    !callerParams.TryAddFloat(EffectParamKeys.TargetOriginY, inst.TargetOriginPosCm.Y.ToFloat()))
                {
                    return false;
                }
                added = true;
            }

            return true;
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

        private bool FireEventSignal(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            if (_eventBus == null)
            {
                MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.PreconditionFailed);
                return false;
            }
            int tagId = spec.GetTagId(idx);
            _eventBus.Publish(new GameplayEvent
            {
                TagId = tagId,
                Source = actor,
                Target = inst.Target,
                Magnitude = spec.GetPayloadA(idx)
            });
            return true;
        }

        // Gate enter / process

        private bool EnterGate(Entity actor, ref AbilityExecSpec spec, int idx,
            ref AbilityExecInstance inst)
        {
            var kind = spec.GetKind(idx);

            switch (kind)
            {
                case ExecItemKind.InputGate:
                    {
                        int requestId = spec.GetPayloadA(idx) != 0 ? spec.GetPayloadA(idx) : inst.OrderId;
                        var request = new InputRequest
                        {
                            RequestId = requestId,
                            RequestTagId = spec.GetTagId(idx),
                            Source = actor,
                            Target = inst.Target,
                            Context = inst.TargetContext,
                        };
                        if (_inputRequests == null)
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                            return false;
                        }
                        if (_inputRequests.Count >= _inputRequests.Capacity)
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                            return false;
                        }
                        inst.State = AbilityExecRunState.GateWaiting;
                        inst.WaitRequestId = requestId;
                        if (!_inputRequests.TryEnqueue(in request))
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                            return false;
                        }
                        break;
                    }

                case ExecItemKind.TargetCollectionGate:
                    {
                        int requestId = spec.GetPayloadA(idx) != 0 ? spec.GetPayloadA(idx) : inst.OrderId;
                        var request = new InputRequest
                        {
                            RequestId = requestId,
                            RequestTagId = spec.GetTagId(idx),
                            Source = actor,
                            Target = inst.Target,
                            Context = inst.TargetContext,
                        };
                        if (_inputRequests == null)
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                            return false;
                        }
                        if (_inputRequests.Count >= _inputRequests.Capacity)
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                            return false;
                        }
                        inst.State = AbilityExecRunState.GateWaiting;
                        inst.WaitRequestId = requestId;
                        if (!_inputRequests.TryEnqueue(in request))
                        {
                            MarkActiveExecutionFailed(actor, ref inst, AbilityCastFailReason.PreconditionFailed, OrderFailureReason.SubmissionQueueFull);
                            return false;
                        }
                        break;
                    }

                case ExecItemKind.EventGate:
                    {
                        inst.State = AbilityExecRunState.GateWaiting;
                        inst.WaitTagId = spec.GetTagId(idx);
                        int deadlineTicks = spec.GetPayloadA(idx);
                        if (deadlineTicks > 0)
                        {
                            inst.GateDeadline = ClockNow(inst.ActiveClockId, actor) + deadlineTicks;
                        }
                        break;
                    }
            }

            return true;
        }

        private void ProcessGate(Entity actor, ref AbilityExecSpec spec, ref AbilityExecInstance inst)
        {
            if (inst.NextItemIndex >= spec.ItemCount)
            {
                EnsureTerminalTransitionCapacity(actor, in inst, OrderTerminalState.Completed, GasPresentationEventKind.CastFinished);
                inst.State = AbilityExecRunState.Finished;
                return;
            }

            var kind = spec.GetKind(inst.NextItemIndex);

            switch (kind)
            {
                case ExecItemKind.InputGate:
                    {
                        if (_inputResponses == null)
                        {
                            MarkActiveExecutionFailed(
                                actor,
                                ref inst,
                                AbilityCastFailReason.PreconditionFailed,
                                OrderFailureReason.SubmissionQueueFull);
                            return;
                        }
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
                        if (_inputResponses == null)
                        {
                            MarkActiveExecutionFailed(
                                actor,
                                ref inst,
                                AbilityCastFailReason.PreconditionFailed,
                                OrderFailureReason.SubmissionQueueFull);
                            return;
                        }
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
                        if (_eventBus == null)
                        {
                            MarkActiveExecutionFailed(
                                actor,
                                ref inst,
                                AbilityCastFailReason.PreconditionFailed,
                                OrderFailureReason.SubmissionQueueFull);
                            return;
                        }
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
        /// Called when the activate timeline completes successfully, before terminal finalize.
        /// </summary>
        private void ActivateToggle(Entity actor, in AbilityToggleSpec toggleSpec)
        {
            if (!World.IsAlive(actor)) return;
            TagOps.RequireTagState(World, actor);

            ref var tags = ref World.Get<GameplayTagContainer>(actor);
            if (tags.HasTag(toggleSpec.ToggleTagId)) return;

            int requiredEffectSlots = 0;
            unsafe
            {
                for (int i = 0; i < toggleSpec.ActiveEffectCount && i < 4; i++)
                {
                    if (toggleSpec.ActiveEffectTemplateIds[i] > 0)
                    {
                        requiredEffectSlots++;
                    }
                }
            }

            if (requiredEffectSlots > 0)
            {
                if (_effectRequests == null)
                {
                    throw new InvalidOperationException(
                        $"{ToggleActiveEffectQueueMissingError}: actor={actor.Id}, toggleTagId={toggleSpec.ToggleTagId}, required={requiredEffectSlots}.");
                }

                if (_effectRequests.AvailableCapacity < requiredEffectSlots)
                {
                    throw new InvalidOperationException(
                        $"{ToggleActiveEffectQueueFullError}: actor={actor.Id}, toggleTagId={toggleSpec.ToggleTagId}, required={requiredEffectSlots}, available={_effectRequests.AvailableCapacity}.");
                }
            }

            _tagOps.AddTag(World, actor, toggleSpec.ToggleTagId);

            // Apply active effects as infinite-duration effects
            unsafe
            {
                for (int i = 0; i < toggleSpec.ActiveEffectCount && i < 4; i++)
                {
                    int tplId = toggleSpec.ActiveEffectTemplateIds[i];
                    if (tplId > 0)
                    {
                        _effectRequests!.Publish(new EffectRequest
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
        private void DeactivateToggle(
            Entity actor,
            in AbilityToggleSpec toggleSpec,
            int orderId,
            int slotIndex,
            int abilityId,
            Entity targetEntity)
        {
            if (toggleSpec.DeactivateExecSpec.ItemCount > 0)
            {
                EnsurePresentationEventCapacity(2, GasPresentationEventKind.CastStarted);
            }
            else
            {
                EnsurePresentationEventCapacity(1, GasPresentationEventKind.CastFinished);
                if (_orderTypeRegistry != null)
                {
                    int terminalResultCount = OrderSubmitter.CountTerminalResultsRequiredForFinalize(
                        World,
                        actor,
                        _orderTypeRegistry,
                        OrderTerminalState.Completed);
                    if (terminalResultCount > 0)
                    {
                        _orderTypeRegistry.EnsureTerminalResultCapacity(terminalResultCount);
                    }
                }
            }

            _tagOps.RemoveTag(World, actor, toggleSpec.ToggleTagId);

            // Remove active effects by tag (the effects are tagged with the toggle tag,
            // so removing the tag will cause EffectLifetimeSystem to clean them up via ExpireCondition)

            // If there's a deactivate timeline, execute it
            if (toggleSpec.DeactivateExecSpec.ItemCount > 0)
            {
                var exec = new AbilityExecInstance
                {
                    OrderId = orderId,
                    AbilitySlot = slotIndex,
                    AbilityId = abilityId,
                    Target = targetEntity,
                    TargetContext = default,
                    State = AbilityExecRunState.Running,
                    TerminalFailureReason = OrderFailureReason.None,
                    CurrentTick = 0,
                    StartAbsoluteTick = ClockNow(toggleSpec.DeactivateExecSpec.ClockId, actor),
                    NextItemIndex = 0,
                    GateDeadline = 0,
                    WaitTagId = 0,
                    WaitRequestId = 0,
                    ActiveClockId = toggleSpec.DeactivateExecSpec.ClockId,
                    IsToggleDeactivating = true,
                    PendingProgressionUseRequirement = 0,
                    PendingProgressionRequirementId = 0,
                };
                _structuralCommands.Add(actor, exec);

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

        private bool IsExecutionOrderCurrent(Entity actor, int orderId)
        {
            if (!World.Has<OrderBuffer>(actor))
            {
                return false;
            }

            ref var orders = ref World.Get<OrderBuffer>(actor);
            return orders.HasActive && orders.ActiveOrder.Order.OrderId == orderId;
        }

        private static void ResolveOrderTerminalOutcome(
            in AbilityExecInstance instance,
            out OrderTerminalState state,
            out OrderFailureReason failureReason)
        {
            switch (instance.State)
            {
                case AbilityExecRunState.Finished:
                    state = OrderTerminalState.Completed;
                    failureReason = OrderFailureReason.None;
                    return;
                case AbilityExecRunState.Interrupted:
                    state = OrderTerminalState.Cancelled;
                    failureReason = instance.TerminalFailureReason;
                    break;
                case AbilityExecRunState.Failed:
                    state = OrderTerminalState.Failed;
                    failureReason = instance.TerminalFailureReason;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.NonTerminalStateFinalized: state={instance.State}, orderId={instance.OrderId}.");
            }

            if (failureReason == OrderFailureReason.None)
            {
                throw new InvalidOperationException(
                    $"GAS.ABILITY_EXEC.ERR.TerminalReasonMissing: state={instance.State}, orderId={instance.OrderId}.");
            }
        }

        private static bool IsTerminalState(AbilityExecRunState state)
        {
            return state == AbilityExecRunState.Finished ||
                   state == AbilityExecRunState.Interrupted ||
                   state == AbilityExecRunState.Failed;
        }

        private void FinalizeTerminalExecution(
            Entity actor,
            in AbilityExecInstance terminalInstance,
            bool removeStoredInstance)
        {
            ResolveOrderTerminalOutcome(
                in terminalInstance,
                out OrderTerminalState terminalState,
                out OrderFailureReason terminalReason);

            if (_orderTypeRegistry != null && terminalInstance.OrderId > 0)
            {
                int terminalResultCount = OrderSubmitter.CountTerminalResultsRequiredForFinalize(
                    World,
                    actor,
                    _orderTypeRegistry,
                    terminalState);
                if (terminalResultCount > 0)
                {
                    _orderTypeRegistry.EnsureTerminalResultCapacity(terminalResultCount);
                }
            }

            if (terminalInstance.State != AbilityExecRunState.Failed)
            {
                EnsurePresentationEventCapacity(
                    1,
                    terminalInstance.State == AbilityExecRunState.Interrupted
                        ? GasPresentationEventKind.CastInterrupted
                        : GasPresentationEventKind.CastFinished);
            }

            // Toggle activation is part of the Finished activation transaction. Preflight and
            // commit it before publishing Completed / CastFinished so a queue-capacity miss
            // cannot leave the player seeing success while follow-up side effects failed.
            // Finalize / promote failures still leave AbilityExecInstance for retry; ActivateToggle
            // is idempotent once the toggle tag is present.
            if (terminalInstance.State == AbilityExecRunState.Finished &&
                !terminalInstance.IsToggleDeactivating &&
                terminalInstance.AbilityId > 0 &&
                _abilityDefinitions != null &&
                _abilityDefinitions.TryGet(terminalInstance.AbilityId, out var toggleFinishDef) &&
                toggleFinishDef.HasToggleSpec && toggleFinishDef.ToggleSpec.ToggleTagId > 0)
            {
                ActivateToggle(actor, in toggleFinishDef.ToggleSpec);
            }

            if (_orderTypeRegistry != null && terminalInstance.OrderId > 0)
            {
                if (!OrderSubmitter.FinalizeCurrent(
                        World,
                        actor,
                        _orderTypeRegistry,
                        terminalState,
                        terminalReason))
                {
                    throw new InvalidOperationException(
                        $"GAS.ABILITY_EXEC.ERR.TerminalOrderMissing: actor={actor.Id}, orderId={terminalInstance.OrderId}, state={terminalInstance.State}.");
                }
            }

            if (terminalInstance.State != AbilityExecRunState.Failed)
            {
                var finishKind = terminalInstance.State == AbilityExecRunState.Interrupted
                    ? GasPresentationEventKind.CastInterrupted
                    : GasPresentationEventKind.CastFinished;
                PublishCastTerminalEvent(actor, in terminalInstance, finishKind);
            }

            if (removeStoredInstance)
            {
                _structuralCommands.Remove<AbilityExecInstance>(in actor);
            }
        }

        private void EnsurePotentialTimelineTerminalCapacity(Entity actor, in AbilityExecInstance instance)
        {
            EnsureTerminalTransitionCapacity(actor, in instance, OrderTerminalState.Completed, GasPresentationEventKind.CastFinished);
            EnsureTerminalTransitionCapacity(actor, in instance, OrderTerminalState.Failed, GasPresentationEventKind.CastFailed);
        }

        private void EnsureTerminalTransitionCapacity(
            Entity actor,
            in AbilityExecInstance instance,
            OrderTerminalState terminalState,
            GasPresentationEventKind presentationKind)
        {
            if (instance.OrderId > 0)
            {
                EnsureOrderTerminalResultCapacity(actor, terminalState);
            }
            EnsurePresentationEventCapacity(1, presentationKind);
        }

        private void EnsureOrderTerminalResultCapacity(Entity actor, OrderTerminalState terminalState)
        {
            if (_orderTypeRegistry == null)
            {
                return;
            }

            int terminalResultCount = OrderSubmitter.CountTerminalResultsRequiredForFinalize(
                World,
                actor,
                _orderTypeRegistry,
                terminalState);
            if (terminalResultCount > 0)
            {
                _orderTypeRegistry.EnsureTerminalResultCapacity(terminalResultCount);
            }
        }

        private void PublishCastTerminalEvent(
            Entity actor,
            in AbilityExecInstance instance,
            GasPresentationEventKind kind)
        {
            EnsurePresentationEventCapacity(1, kind);
            _presentationEvents?.Publish(new GasPresentationEvent
            {
                Kind = kind,
                Actor = actor,
                Target = instance.Target,
                AbilitySlot = instance.AbilitySlot,
                AbilityId = instance.AbilityId
            });
        }

        private void PlaybackStructuralCommands()
        {
            if (_structuralCommands.Size > 0)
            {
                _structuralCommands.Playback(World);
            }
        }

        private int ClockNow(GasClockId clockId, Entity actor)
        {
            return GasClockRuntime.Now(World, _clock, clockId, actor, "Ability execution clock");
        }

        private void PublishCastStartedAndCommitted(Entity actor, Entity targetEntity, int slotIndex, int abilityId)
        {
            EnsurePresentationEventCapacity(2, GasPresentationEventKind.CastStarted);
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

        private void EnsurePresentationEventCapacity(int count, GasPresentationEventKind kind)
        {
            if (count <= 0 || _presentationEvents == null)
            {
                return;
            }

            if (_presentationEvents.AvailableCapacity < count)
            {
                throw new InvalidOperationException(
                    $"GAS.ABILITY_EXEC.ERR.PresentationEventCapacityExceeded: kind={kind}, required={count}, available={_presentationEvents.AvailableCapacity}.");
            }
        }

        public override void Dispose()
        {
            _structuralCommands.Dispose();
            base.Dispose();
        }
    }
}
