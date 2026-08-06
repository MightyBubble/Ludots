using System;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Gameplay.GAS;

/// <summary>
/// Fixed-capacity staging boundary for side effects produced while a persistent
/// effect executes application or lifetime phases. Nothing reaches the world
/// or externally visible queues until every phase has completed successfully.
/// </summary>
public sealed class EffectPhaseSideEffectTransaction : IDisposable
{
    public const string CapacityExceededError = "GAS.EFFECT_TRANSACTION.ERR.CapacityExceeded";
    public const string ScopeAlreadyActiveError = "GAS.EFFECT_TRANSACTION.ERR.ScopeAlreadyActive";
    public const string ScopeNotActiveError = "GAS.EFFECT_TRANSACTION.ERR.ScopeNotActive";
    public const string UnsupportedSideEffectError = "GAS.EFFECT_TRANSACTION.ERR.UnsupportedSideEffect";
    public const string AttributeTargetInvalidError = "GAS.EFFECT_TRANSACTION.ERR.AttributeTargetInvalid";
    public const string RelationTargetInvalidError = "GAS.EFFECT_TRANSACTION.ERR.RelationTargetInvalid";

    private readonly World _world;
    private readonly TagOps? _tagOps;
    private readonly EffectRequestQueue? _effectRequests;
    private readonly RuntimeEntitySpawnQueue? _spawnRequests;
    private readonly GasPresentationEventBuffer? _presentationEvents;
    private readonly RootBudgetTable? _rootBudget;
    private readonly Entity[] _attributeEntities;
    private readonly AttributeBuffer[] _attributeOriginalValues;
    private readonly AttributeBuffer[] _attributeValues;
    private readonly ulong[] _attributeChangedMasks;
    private readonly GameplayAttributeChangedBits[] _attributeChangedOriginalValues;
    private readonly GameplayAttributeChangedBits[] _attributeChangedValues;
    private readonly bool[] _attributeChangedExisted;
    private readonly Entity[] _dirtyEntities;
    private readonly DirtyFlags[] _dirtyOriginalValues;
    private readonly EffectRequest[] _stagedEffectRequests;
    private readonly RuntimeEntitySpawnRequest[] _stagedSpawnRequests;
    private readonly GasPresentationEvent[] _stagedPresentationEvents;
    private readonly GameplayEvent[] _stagedGameplayEvents;
    private readonly Entity[] _gameplayEffectEntities;
    private readonly GameplayEffect[] _gameplayEffectOriginalValues;
    private readonly GameplayEffect[] _gameplayEffectValues;
    private readonly Entity[] _tagEntities;
    private readonly GameplayTagContainer[] _tagOriginalValues;
    private readonly GameplayTagContainer[] _tagValues;
    private readonly TagCountContainer[] _tagCountOriginalValues;
    private readonly TagCountContainer[] _tagCountValues;
    private readonly DirtyFlags[] _tagDirtyOriginalValues;
    private readonly DirtyFlags[] _tagDirtyValues;
    private readonly Entity[] _activeEffectEntities;
    private readonly ActiveEffectContainer[] _activeEffectOriginalValues;
    private readonly ActiveEffectContainer[] _activeEffectValues;
    private readonly Entity[] _destroyedEffects;
    private readonly Entity[] _blackboardFloatEntities;
    private readonly BlackboardFloatBuffer[] _blackboardFloatOriginalValues;
    private readonly BlackboardFloatBuffer[] _blackboardFloatValues;
    private readonly Entity[] _blackboardIntEntities;
    private readonly BlackboardIntBuffer[] _blackboardIntOriginalValues;
    private readonly BlackboardIntBuffer[] _blackboardIntValues;
    private readonly Entity[] _blackboardEntityEntities;
    private readonly BlackboardEntityBuffer[] _blackboardEntityOriginalValues;
    private readonly BlackboardEntityBuffer[] _blackboardEntityValues;
    private readonly Entity[] _cancelledEffects;
    private readonly bool[] _cancelledEffectOriginalValues;
    private readonly Entity[] _aggregateDirtyEntities;
    private readonly bool[] _aggregateDirtyExisted;
    private readonly ListenerRegistration[] _listenerRegistrations;
    private readonly ListenerRemoval[] _listenerRemovals;
    private readonly Entity[] _listenerEntities;
    private readonly EffectPhaseListenerBuffer[] _listenerOriginalValues;
    private readonly EffectPhaseListenerBuffer[] _listenerValues;
    private readonly bool[] _listenerExisted;
    private readonly Entity[] _relationParentEntities;
    private readonly ChildrenBuffer[] _relationParentOriginalValues;
    private readonly ChildrenBuffer[] _relationParentValues;
    private readonly bool[] _relationParentExisted;
    private readonly bool[] _relationParentShouldExist;
    private readonly Entity[] _relationChildEntities;
    private readonly ChildOf[] _relationChildOriginalValues;
    private readonly ChildOf[] _relationChildValues;
    private readonly bool[] _relationChildExisted;
    private readonly bool[] _relationSnapPositions;
    private readonly WorldPositionCm[] _relationWorldPositionOriginalValues;
    private readonly WorldPositionCm[] _relationWorldPositionValues;
    private readonly bool[] _relationWorldPositionExisted;
    private readonly PreviousWorldPositionCm[] _relationPreviousPositionOriginalValues;
    private readonly PreviousWorldPositionCm[] _relationPreviousPositionValues;
    private readonly bool[] _relationPreviousPositionExisted;
    private readonly Entity[] _relationSnapSourceEntities;
    private readonly WorldPositionCm[] _relationSnapSourceWorldPositionOriginalValues;
    private readonly bool[] _relationSnapSourceWorldPositionExisted;
    private readonly PreviousWorldPositionCm[] _relationSnapSourcePreviousPositionOriginalValues;
    private readonly bool[] _relationSnapSourcePreviousPositionExisted;
    private CommandBuffer _structuralCommands;
    private readonly CommandBuffer _structuralRollbackCommands;
    private readonly int _structuralCommandCapacity;
    private int _attributeCount;
    private int _dirtyEntityCount;
    private int _effectRequestCount;
    private int _spawnRequestCount;
    private int _presentationEventCount;
    private int _gameplayEventCount;
    private int _gameplayEffectCount;
    private int _tagEntityCount;
    private int _activeEffectCount;
    private int _destroyedEffectCount;
    private int _blackboardFloatCount;
    private int _blackboardIntCount;
    private int _blackboardEntityCount;
    private int _cancelledEffectCount;
    private int _aggregateDirtyCount;
    private int _listenerRegistrationCount;
    private int _listenerRemovalCount;
    private int _listenerEntityCount;
    private int _relationParentCount;
    private int _relationChildCount;
    private GameplayEventBus? _gameplayEventBus;
    private bool _worldCommitStarted;
    private bool _externalCommitStarted;
    private DirtyEntityQueue.WriteCheckpoint _dirtyEntityCheckpoint;
    private EffectRequestQueue.WriteCheckpoint _effectRequestCheckpoint;
    private RuntimeEntitySpawnQueue.WriteCheckpoint _spawnRequestCheckpoint;
    private int _presentationEventCheckpoint;
    private GameplayEventBus.WriteCheckpoint _gameplayEventCheckpoint;
    private RootBudgetTable.WriteCheckpoint _rootBudgetCheckpoint;

    public EffectPhaseSideEffectTransaction(
        World world,
        TagOps? tagOps,
        EffectRequestQueue? effectRequests,
        RuntimeEntitySpawnQueue? spawnRequests,
        GasPresentationEventBuffer? presentationEvents,
        int attributeEntityCapacity,
        RootBudgetTable? rootBudget = null)
    {
        if (attributeEntityCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attributeEntityCapacity));
        }

        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tagOps = tagOps;
        _effectRequests = effectRequests;
        _spawnRequests = spawnRequests;
        _presentationEvents = presentationEvents;
        _rootBudget = rootBudget;
        _attributeEntities = new Entity[attributeEntityCapacity];
        _attributeOriginalValues = new AttributeBuffer[attributeEntityCapacity];
        _attributeValues = new AttributeBuffer[attributeEntityCapacity];
        _attributeChangedMasks = new ulong[attributeEntityCapacity];
        _attributeChangedOriginalValues = new GameplayAttributeChangedBits[attributeEntityCapacity];
        _attributeChangedValues = new GameplayAttributeChangedBits[attributeEntityCapacity];
        _attributeChangedExisted = new bool[attributeEntityCapacity];
        _dirtyEntities = new Entity[attributeEntityCapacity + 1];
        _dirtyOriginalValues = new DirtyFlags[attributeEntityCapacity + 1];
        _stagedEffectRequests = new EffectRequest[effectRequests?.TotalCapacity ?? 1];
        _stagedSpawnRequests = new RuntimeEntitySpawnRequest[spawnRequests?.Capacity ?? 1];
        _stagedPresentationEvents = new GasPresentationEvent[presentationEvents?.Capacity ?? 1];
        _stagedGameplayEvents = new GameplayEvent[GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME];
        _gameplayEffectEntities = new Entity[attributeEntityCapacity];
        _gameplayEffectOriginalValues = new GameplayEffect[attributeEntityCapacity];
        _gameplayEffectValues = new GameplayEffect[attributeEntityCapacity];
        _tagEntities = new Entity[attributeEntityCapacity];
        _tagOriginalValues = new GameplayTagContainer[attributeEntityCapacity];
        _tagValues = new GameplayTagContainer[attributeEntityCapacity];
        _tagCountOriginalValues = new TagCountContainer[attributeEntityCapacity];
        _tagCountValues = new TagCountContainer[attributeEntityCapacity];
        _tagDirtyOriginalValues = new DirtyFlags[attributeEntityCapacity];
        _tagDirtyValues = new DirtyFlags[attributeEntityCapacity];
        _activeEffectEntities = new Entity[attributeEntityCapacity];
        _activeEffectOriginalValues = new ActiveEffectContainer[attributeEntityCapacity];
        _activeEffectValues = new ActiveEffectContainer[attributeEntityCapacity];
        _destroyedEffects = new Entity[attributeEntityCapacity];
        _blackboardFloatEntities = new Entity[attributeEntityCapacity];
        _blackboardFloatOriginalValues = new BlackboardFloatBuffer[attributeEntityCapacity];
        _blackboardFloatValues = new BlackboardFloatBuffer[attributeEntityCapacity];
        _blackboardIntEntities = new Entity[attributeEntityCapacity];
        _blackboardIntOriginalValues = new BlackboardIntBuffer[attributeEntityCapacity];
        _blackboardIntValues = new BlackboardIntBuffer[attributeEntityCapacity];
        _blackboardEntityEntities = new Entity[attributeEntityCapacity];
        _blackboardEntityOriginalValues = new BlackboardEntityBuffer[attributeEntityCapacity];
        _blackboardEntityValues = new BlackboardEntityBuffer[attributeEntityCapacity];
        _cancelledEffects = new Entity[attributeEntityCapacity];
        _cancelledEffectOriginalValues = new bool[attributeEntityCapacity];
        _aggregateDirtyEntities = new Entity[attributeEntityCapacity];
        _aggregateDirtyExisted = new bool[attributeEntityCapacity];
        _listenerRegistrations = new ListenerRegistration[attributeEntityCapacity];
        int listenerEntityCapacity = checked(attributeEntityCapacity * 2);
        _listenerRemovals = new ListenerRemoval[listenerEntityCapacity];
        _listenerEntities = new Entity[listenerEntityCapacity];
        _listenerOriginalValues = new EffectPhaseListenerBuffer[listenerEntityCapacity];
        _listenerValues = new EffectPhaseListenerBuffer[listenerEntityCapacity];
        _listenerExisted = new bool[listenerEntityCapacity];
        int relationParentCapacity = checked(attributeEntityCapacity * 2);
        _relationParentEntities = new Entity[relationParentCapacity];
        _relationParentOriginalValues = new ChildrenBuffer[relationParentCapacity];
        _relationParentValues = new ChildrenBuffer[relationParentCapacity];
        _relationParentExisted = new bool[relationParentCapacity];
        _relationParentShouldExist = new bool[relationParentCapacity];
        _relationChildEntities = new Entity[attributeEntityCapacity];
        _relationChildOriginalValues = new ChildOf[attributeEntityCapacity];
        _relationChildValues = new ChildOf[attributeEntityCapacity];
        _relationChildExisted = new bool[attributeEntityCapacity];
        _relationSnapPositions = new bool[attributeEntityCapacity];
        _relationWorldPositionOriginalValues = new WorldPositionCm[attributeEntityCapacity];
        _relationWorldPositionValues = new WorldPositionCm[attributeEntityCapacity];
        _relationWorldPositionExisted = new bool[attributeEntityCapacity];
        _relationPreviousPositionOriginalValues = new PreviousWorldPositionCm[attributeEntityCapacity];
        _relationPreviousPositionValues = new PreviousWorldPositionCm[attributeEntityCapacity];
        _relationPreviousPositionExisted = new bool[attributeEntityCapacity];
        _relationSnapSourceEntities = new Entity[attributeEntityCapacity];
        _relationSnapSourceWorldPositionOriginalValues = new WorldPositionCm[attributeEntityCapacity];
        _relationSnapSourceWorldPositionExisted = new bool[attributeEntityCapacity];
        _relationSnapSourcePreviousPositionOriginalValues = new PreviousWorldPositionCm[attributeEntityCapacity];
        _relationSnapSourcePreviousPositionExisted = new bool[attributeEntityCapacity];
        _structuralCommandCapacity = checked(attributeEntityCapacity * 8);
        _structuralCommands = new CommandBuffer(_structuralCommandCapacity);
        _structuralRollbackCommands = new CommandBuffer(_structuralCommandCapacity);
    }

    public bool IsActive { get; private set; }

    public void Begin()
    {
        if (IsActive)
        {
            throw new InvalidOperationException(ScopeAlreadyActiveError);
        }

        _attributeCount = 0;
        _dirtyEntityCount = 0;
        _effectRequestCount = 0;
        _spawnRequestCount = 0;
        _presentationEventCount = 0;
        _gameplayEventCount = 0;
        _gameplayEffectCount = 0;
        _tagEntityCount = 0;
        _activeEffectCount = 0;
        _destroyedEffectCount = 0;
        _blackboardFloatCount = 0;
        _blackboardIntCount = 0;
        _blackboardEntityCount = 0;
        _cancelledEffectCount = 0;
        _aggregateDirtyCount = 0;
        _listenerRegistrationCount = 0;
        _listenerRemovalCount = 0;
        _listenerEntityCount = 0;
        _relationParentCount = 0;
        _relationChildCount = 0;
        _gameplayEventBus = null;
        _worldCommitStarted = false;
        _externalCommitStarted = false;
        if (_rootBudget != null)
        {
            _rootBudgetCheckpoint = _rootBudget.CaptureWriteCheckpoint();
        }
        IsActive = true;
    }

    public void StageSetParent(Entity subject, Entity parent, bool snapSubjectToParentPosition)
    {
        RequireActive();
        if (!_world.IsAlive(subject) || !_world.IsAlive(parent) || subject == parent)
        {
            throw new InvalidOperationException(
                $"{RelationTargetInvalidError}: subject={subject.Id}, parent={parent.Id}.");
        }
        WorldPositionCm parentPosition = default;
        if (snapSubjectToParentPosition && !TryReadRelationWorldPosition(parent, out parentPosition))
        {
            throw new InvalidOperationException(
                $"GAS.EFFECT_TRANSACTION.ERR.RelationParentPositionMissing: entity={parent.Id}.");
        }

        int childIndex = GetOrAddRelationChild(subject);
        Entity currentParent = _relationChildValues[childIndex].Parent;
        int newParentIndex = GetOrAddRelationParent(parent);
        ref ChildrenBuffer newParentChildren = ref _relationParentValues[newParentIndex];
        bool alreadyContained = newParentChildren.Contains(in subject);
        if (!alreadyContained && newParentChildren.Count >= GasConstants.MAX_CHILDREN_BUFFER_CAPACITY)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=ChildrenBuffer, entity={parent.Id}, capacity={GasConstants.MAX_CHILDREN_BUFFER_CAPACITY}.");
        }

        if (currentParent != Entity.Null && currentParent != parent && _world.IsAlive(currentParent))
        {
            int oldParentIndex = GetOrAddRelationParent(currentParent);
            _relationParentValues[oldParentIndex].Remove(in subject);
        }
        if (!alreadyContained && !newParentChildren.Add(in subject))
        {
            throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.ValidatedRelationCommitFailed");
        }

        _relationParentShouldExist[newParentIndex] = true;
        _relationChildValues[childIndex] = new ChildOf { Parent = parent };
        if (!snapSubjectToParentPosition)
        {
            return;
        }

        WorldPositionCm stagedParentPosition = parentPosition;
        PreviousWorldPositionCm stagedPreviousPosition =
            TryReadRelationPreviousPosition(parent, out PreviousWorldPositionCm parentPreviousPosition)
                ? parentPreviousPosition
                : new PreviousWorldPositionCm { Value = stagedParentPosition.Value };
        _relationSnapPositions[childIndex] = true;
        _relationWorldPositionValues[childIndex] = stagedParentPosition;
        _relationPreviousPositionValues[childIndex] = stagedPreviousPosition;
        CaptureRelationSnapSource(childIndex, parent);
    }

    public bool TryReadAttributeCurrent(Entity entity, int attributeId, out float value)
    {
        if (TryGetAttributeCurrent(entity, attributeId, out value))
        {
            return true;
        }
        if (_world.IsAlive(entity) && _world.Has<AttributeBuffer>(entity))
        {
            value = _world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
            return true;
        }

        value = 0f;
        return false;
    }

    public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
    {
        if (IsActive)
        {
            int index = FindAttributeEntity(entity);
            if (index >= 0)
            {
                value = _attributeValues[index].GetCurrent(attributeId);
                return true;
            }
        }

        value = 0f;
        return false;
    }

    public void StageAttributeAdd(Entity target, int attributeId, float delta)
    {
        int index = GetOrAddAttributeEntity(target);
        float before = _attributeValues[index].GetCurrent(attributeId);
        _attributeValues[index].SetCurrent(attributeId, before + delta);
        RefreshAttributeChanged(index, attributeId);
    }

    public void StageAttributeSet(Entity target, int attributeId, float value)
    {
        int index = GetOrAddAttributeEntity(target);
        _attributeValues[index].SetCurrent(attributeId, value);
        RefreshAttributeChanged(index, attributeId);
    }

    public void StageModifiers(Entity target, in EffectModifiers modifiers)
    {
        int index = GetOrAddAttributeEntity(target);
        EffectModifierOps.Apply(in modifiers, ref _attributeValues[index]);
        for (int i = 0; i < modifiers.Count; i++)
        {
            RefreshAttributeChanged(index, modifiers.Get(i).AttributeId);
        }
    }

    public void StageEffectRequest(in EffectRequest request)
    {
        RequireActive();
        if (_effectRequests == null)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.MissingEffectRequestQueue");
        }
        if (_effectRequestCount >= _stagedEffectRequests.Length ||
            _effectRequestCount >= _effectRequests.AvailableCapacity)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=EffectRequestQueue, staged={_effectRequestCount + 1}, available={_effectRequests.AvailableCapacity}.");
        }

        _stagedEffectRequests[_effectRequestCount++] = request;
    }

    public void StageFanOutCommand(in FanOutCommand command)
    {
        if (command.PayloadEffectTemplateId <= 0)
        {
            return;
        }

        StageEffectRequest(new EffectRequest
        {
            RootId = command.RootId,
            Source = TargetResolverFanOutHelper.ResolveSlot(command.ContextMapping.PayloadSource, in command),
            Target = TargetResolverFanOutHelper.ResolveSlot(command.ContextMapping.PayloadTarget, in command),
            TargetContext = TargetResolverFanOutHelper.ResolveSlot(command.ContextMapping.PayloadTargetContext, in command),
            TemplateId = command.PayloadEffectTemplateId,
        });
    }

    public void StageSpawnRequest(in RuntimeEntitySpawnRequest request)
    {
        RequireActive();
        if (_spawnRequests == null)
        {
            throw new InvalidOperationException("CreateProjectile requires RuntimeEntitySpawnQueue in BuiltinHandlerExecutionContext.");
        }
        if (_spawnRequestCount >= _stagedSpawnRequests.Length ||
            _spawnRequestCount >= _spawnRequests.FreeCapacity)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=RuntimeEntitySpawnQueue, staged={_spawnRequestCount + 1}, available={_spawnRequests.FreeCapacity}.");
        }

        _stagedSpawnRequests[_spawnRequestCount++] = request;
    }

    public void StagePresentationEvent(in GasPresentationEvent presentationEvent)
    {
        RequireActive();
        if (_presentationEvents == null)
        {
            return;
        }
        if (_presentationEventCount >= _stagedPresentationEvents.Length ||
            _presentationEventCount >= _presentationEvents.AvailableCapacity)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=GasPresentationEventBuffer, staged={_presentationEventCount + 1}, available={_presentationEvents.AvailableCapacity}.");
        }

        _stagedPresentationEvents[_presentationEventCount++] = presentationEvent;
    }

    public void StageGameplayEvent(GameplayEventBus eventBus, in GameplayEvent gameplayEvent)
    {
        RequireActive();
        if (eventBus == null)
        {
            throw new ArgumentNullException(nameof(eventBus));
        }
        if (_gameplayEventBus != null && !ReferenceEquals(_gameplayEventBus, eventBus))
        {
            throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.MultipleGameplayEventBuses");
        }

        _gameplayEventBus = eventBus;
        if (_gameplayEventCount >= _stagedGameplayEvents.Length ||
            _gameplayEventCount >= eventBus.AvailableNextCapacity)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=GameplayEventBus, staged={_gameplayEventCount + 1}, available={eventBus.AvailableNextCapacity}.");
        }

        _stagedGameplayEvents[_gameplayEventCount++] = gameplayEvent;
    }

    public void StageGameplayEffectState(Entity entity, in GameplayEffect effect)
    {
        int index = GetOrAddGameplayEffectEntity(entity);
        _gameplayEffectValues[index] = effect;
    }

    public bool TryGetGameplayEffectState(Entity entity, out GameplayEffect effect)
    {
        int index = FindEntity(_gameplayEffectEntities, _gameplayEffectCount, entity);
        if (index >= 0)
        {
            effect = _gameplayEffectValues[index];
            return true;
        }

        effect = default;
        return false;
    }

    public bool TryHasTag(Entity entity, int tagId, out bool hasTag)
    {
        int index = FindEntity(_tagEntities, _tagEntityCount, entity);
        if (index < 0)
        {
            hasTag = false;
            return false;
        }
        if (_tagOps == null)
        {
            throw new InvalidOperationException(TagOps.MissingTagOpsError);
        }

        hasTag = _tagOps.HasTag(ref _tagValues[index], tagId, TagSense.Effective);
        return true;
    }

    public void StageGrantedTagRevoke(Entity target, in EffectGrantedTags grantedTags, int stackCount)
    {
        RequireActive();
        if (!_world.IsAlive(target) || grantedTags.Count <= 0)
        {
            return;
        }
        if (_tagOps == null)
        {
            throw new InvalidOperationException(TagOps.MissingTagOpsError);
        }

        int index = GetOrAddTagEntity(target);
        bool changed = false;
        for (int grantIndex = 0; grantIndex < grantedTags.Count; grantIndex++)
        {
            TagContribution contribution = grantedTags.Get(grantIndex);
            int amount = contribution.Compute(stackCount);
            for (int repeat = 0; repeat < amount; repeat++)
            {
                changed |= _tagOps.RemoveTag(
                    ref _tagValues[index],
                    ref _tagCountValues[index],
                    contribution.TagId,
                    ref _tagDirtyValues[index]);
            }
        }

        if (changed)
        {
            StageDirtyEntity(target);
        }
    }

    public bool StageActiveEffectRemoval(Entity target, Entity effect)
    {
        RequireActive();
        if (!_world.IsAlive(target) || !_world.Has<ActiveEffectContainer>(target))
        {
            return false;
        }

        int index = GetOrAddActiveEffectEntity(target);
        int countBefore = _activeEffectValues[index].Count;
        _activeEffectValues[index].Remove(effect);
        return _activeEffectValues[index].Count != countBefore;
    }

    public bool TryGetActiveEffectContainer(Entity target, out ActiveEffectContainer container)
    {
        int index = FindEntity(_activeEffectEntities, _activeEffectCount, target);
        if (index >= 0)
        {
            container = _activeEffectValues[index];
            return true;
        }
        if (_world.IsAlive(target) && _world.Has<ActiveEffectContainer>(target))
        {
            container = _world.Get<ActiveEffectContainer>(target);
            return true;
        }

        container = default;
        return false;
    }

    public void StageEffectDestroy(Entity effect)
    {
        RequireActive();
        if (!_world.IsAlive(effect) || Contains(_destroyedEffects, _destroyedEffectCount, effect))
        {
            return;
        }
        if (_destroyedEffectCount >= _destroyedEffects.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=DestroyedEffects, staged={_destroyedEffectCount + 1}, capacity={_destroyedEffects.Length}.");
        }

        _destroyedEffects[_destroyedEffectCount++] = effect;
    }

    public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value)
    {
        int index = FindEntity(_blackboardFloatEntities, _blackboardFloatCount, entity);
        if (index >= 0) return _blackboardFloatValues[index].TryGet(keyId, out value);
        value = default;
        return false;
    }

    public bool TryReadBlackboardInt(Entity entity, int keyId, out int value)
    {
        int index = FindEntity(_blackboardIntEntities, _blackboardIntCount, entity);
        if (index >= 0) return _blackboardIntValues[index].TryGet(keyId, out value);
        value = default;
        return false;
    }

    public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value)
    {
        int index = FindEntity(_blackboardEntityEntities, _blackboardEntityCount, entity);
        if (index >= 0) return _blackboardEntityValues[index].TryGet(keyId, out value);
        value = default;
        return false;
    }

    public void StageBlackboardFloat(Entity entity, int keyId, float value)
    {
        int index = GetOrAddBlackboardFloatEntity(entity);
        bool existed = _blackboardFloatValues[index].TryGet(keyId, out _);
        int countBefore = _blackboardFloatValues[index].Count;
        _blackboardFloatValues[index].Set(keyId, value);
        if (!existed && _blackboardFloatValues[index].Count == countBefore)
        {
            throw BlackboardCapacityExceeded(nameof(BlackboardFloatBuffer), entity, keyId);
        }
    }

    public void StageBlackboardInt(Entity entity, int keyId, int value)
    {
        int index = GetOrAddBlackboardIntEntity(entity);
        bool existed = _blackboardIntValues[index].TryGet(keyId, out _);
        int countBefore = _blackboardIntValues[index].Count;
        _blackboardIntValues[index].Set(keyId, value);
        if (!existed && _blackboardIntValues[index].Count == countBefore)
        {
            throw BlackboardCapacityExceeded(nameof(BlackboardIntBuffer), entity, keyId);
        }
    }

    public void StageBlackboardEntity(Entity entity, int keyId, Entity value)
    {
        int index = GetOrAddBlackboardEntityEntity(entity);
        bool existed = _blackboardEntityValues[index].TryGet(keyId, out _);
        int countBefore = _blackboardEntityValues[index].Count;
        _blackboardEntityValues[index].Set(keyId, value);
        if (!existed && _blackboardEntityValues[index].Count == countBefore)
        {
            throw BlackboardCapacityExceeded(nameof(BlackboardEntityBuffer), entity, keyId);
        }
    }

    public void StageEffectCancellation(Entity target, int templateId)
    {
        RequireActive();
        if (!_world.IsAlive(target) || templateId <= 0 || !TryGetActiveEffectContainer(target, out ActiveEffectContainer container))
        {
            return;
        }

        for (int i = 0; i < container.Count; i++)
        {
            Entity effect = container.GetEntity(i);
            if (!_world.IsAlive(effect) ||
                !_world.Has<EffectTemplateRef>(effect) ||
                !_world.Has<GameplayEffect>(effect) ||
                _world.Get<EffectTemplateRef>(effect).TemplateId != templateId ||
                Contains(_cancelledEffects, _cancelledEffectCount, effect))
            {
                continue;
            }
            if (_cancelledEffectCount >= _cancelledEffects.Length)
            {
                throw new InvalidOperationException(
                    $"{CapacityExceededError}: destination=EffectCancellations, staged={_cancelledEffectCount + 1}, capacity={_cancelledEffects.Length}.");
            }

            _cancelledEffects[_cancelledEffectCount++] = effect;
            GameplayEffect gameplayEffect = TryGetGameplayEffectState(effect, out GameplayEffect stagedEffect)
                ? stagedEffect
                : _world.Get<GameplayEffect>(effect);
            if (gameplayEffect.AggregatesModifiers)
            {
                StageAggregateDirty(target);
            }
        }
    }

    public void StageAggregateDirty(Entity target)
    {
        RequireActive();
        if (!_world.IsAlive(target))
        {
            throw new InvalidOperationException(
                $"GAS.EFFECT_TRANSACTION.ERR.AggregateTargetInvalid: entity={target.Id}.");
        }
        if (Contains(_aggregateDirtyEntities, _aggregateDirtyCount, target))
        {
            return;
        }
        if (_aggregateDirtyCount >= _aggregateDirtyEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=AggregateDirtyTargets, staged={_aggregateDirtyCount + 1}, capacity={_aggregateDirtyEntities.Length}.");
        }
        _aggregateDirtyEntities[_aggregateDirtyCount++] = target;
    }

    public unsafe void StageListenerRegistration(
        in EffectContext context,
        in EffectPhaseListenerBuffer setup,
        int ownerEffectId)
    {
        RequireActive();
        EffectPhaseListenerContract.RequireValidCount(setup.Count, EffectPhaseListenerBuffer.CAPACITY);
        if (setup.Count <= 0)
        {
            return;
        }
        for (int listenerIndex = 0; listenerIndex < setup.Count; listenerIndex++)
        {
            EffectPhaseListenerContract.RequireValidRegistration(
                setup.ListenTagIds[listenerIndex],
                setup.ListenEffectIds[listenerIndex],
                (EffectPhaseId)setup.Phases[listenerIndex],
                (PhaseListenerScope)setup.Scopes[listenerIndex],
                (PhaseListenerActionFlags)setup.ActionFlags[listenerIndex],
                setup.GraphProgramIds[listenerIndex],
                setup.EventTagIds[listenerIndex]);
        }
        if (_listenerRegistrationCount >= _listenerRegistrations.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=ListenerRegistrations, staged={_listenerRegistrationCount + 1}, capacity={_listenerRegistrations.Length}.");
        }

        _listenerRegistrations[_listenerRegistrationCount++] = new ListenerRegistration
        {
            Context = context,
            Setup = setup,
            OwnerEffectId = ownerEffectId,
        };
    }

    public void StageListenerRemoval(in EffectContext context, int ownerEffectId)
    {
        RequireActive();
        StageListenerRemoval(context.Target, ownerEffectId);
        if (context.Source != context.Target)
        {
            StageListenerRemoval(context.Source, ownerEffectId);
        }
    }

    private void StageListenerRemoval(Entity entity, int ownerEffectId)
    {
        if (!_world.IsAlive(entity) || !_world.Has<EffectPhaseListenerBuffer>(entity))
        {
            return;
        }
        for (int i = 0; i < _listenerRemovalCount; i++)
        {
            if (_listenerRemovals[i].Entity == entity &&
                _listenerRemovals[i].OwnerEffectId == ownerEffectId)
            {
                return;
            }
        }
        if (_listenerRemovalCount >= _listenerRemovals.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=ListenerRemovals, staged={_listenerRemovalCount + 1}, capacity={_listenerRemovals.Length}.");
        }

        _listenerRemovals[_listenerRemovalCount++] = new ListenerRemoval
        {
            Entity = entity,
            OwnerEffectId = ownerEffectId,
        };
    }

    public void StageDirtyEntity(Entity entity)
    {
        RequireActive();
        if (!_world.IsAlive(entity) || !_world.Has<DirtyFlags>(entity))
        {
            throw new InvalidOperationException(TagOps.MissingDirtyFlagsError);
        }

        for (int i = 0; i < _dirtyEntityCount; i++)
        {
            if (_dirtyEntities[i] == entity)
            {
                return;
            }
        }

        if (_dirtyEntityCount >= _dirtyEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=DirtyEntityQueue, staged={_dirtyEntityCount + 1}, capacity={_dirtyEntities.Length}.");
        }

        _dirtyEntities[_dirtyEntityCount++] = entity;
    }

    public void Commit()
    {
        RequireActive();
        ValidateCommit();
        PrepareCommitState();

        try
        {
            _worldCommitStarted = true;
            if (_structuralCommands.Size > 0)
            {
                _structuralCommands.Playback(_world);
            }

            for (int i = 0; i < _attributeCount; i++)
            {
                if (_attributeChangedMasks[i] == 0UL)
                {
                    continue;
                }

                Entity entity = _attributeEntities[i];
                _world.Get<AttributeBuffer>(entity) = _attributeValues[i];
                _world.Get<GameplayAttributeChangedBits>(entity) = _attributeChangedValues[i];
                for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
                {
                    if ((_attributeChangedMasks[i] & (1UL << attributeId)) != 0UL)
                    {
                        _world.Get<DirtyFlags>(entity).MarkAttributeDirty(attributeId);
                    }
                }
            }
            for (int i = 0; i < _tagEntityCount; i++)
            {
                Entity entity = _tagEntities[i];
                _world.Get<GameplayTagContainer>(entity) = _tagValues[i];
                _world.Get<TagCountContainer>(entity) = _tagCountValues[i];
                _world.Get<DirtyFlags>(entity) = _tagDirtyValues[i];
            }
            for (int i = 0; i < _activeEffectCount; i++)
            {
                _world.Get<ActiveEffectContainer>(_activeEffectEntities[i]) = _activeEffectValues[i];
            }
            for (int i = 0; i < _gameplayEffectCount; i++)
            {
                _world.Get<GameplayEffect>(_gameplayEffectEntities[i]) = _gameplayEffectValues[i];
            }

            for (int i = 0; i < _blackboardFloatCount; i++)
            {
                _world.Get<BlackboardFloatBuffer>(_blackboardFloatEntities[i]) = _blackboardFloatValues[i];
            }
            for (int i = 0; i < _blackboardIntCount; i++)
            {
                _world.Get<BlackboardIntBuffer>(_blackboardIntEntities[i]) = _blackboardIntValues[i];
            }
            for (int i = 0; i < _blackboardEntityCount; i++)
            {
                _world.Get<BlackboardEntityBuffer>(_blackboardEntityEntities[i]) = _blackboardEntityValues[i];
            }
            for (int i = 0; i < _cancelledEffectCount; i++)
            {
                _world.Get<GameplayEffect>(_cancelledEffects[i]).CancelRequested = true;
            }
            for (int i = 0; i < _listenerEntityCount; i++)
            {
                _world.Get<EffectPhaseListenerBuffer>(_listenerEntities[i]) = _listenerValues[i];
            }
            for (int i = 0; i < _relationParentCount; i++)
            {
                if (_relationParentExisted[i] || _relationParentShouldExist[i])
                {
                    _world.Get<ChildrenBuffer>(_relationParentEntities[i]) = _relationParentValues[i];
                }
            }
            for (int i = 0; i < _relationChildCount; i++)
            {
                Entity subject = _relationChildEntities[i];
                _world.Get<ChildOf>(subject) = _relationChildValues[i];
                if (_relationSnapPositions[i])
                {
                    _world.Get<WorldPositionCm>(subject) = _relationWorldPositionValues[i];
                    _world.Get<PreviousWorldPositionCm>(subject) = _relationPreviousPositionValues[i];
                }
            }

            CaptureExternalWriteCheckpoints();
            _externalCommitStarted = true;
            for (int i = 0; i < _dirtyEntityCount; i++)
            {
                _tagOps!.MarkDirtyEntity(_world, _dirtyEntities[i]);
            }
            for (int i = 0; i < _effectRequestCount; i++)
            {
                _effectRequests!.Publish(_stagedEffectRequests[i]);
            }
            for (int i = 0; i < _spawnRequestCount; i++)
            {
                if (!_spawnRequests!.TryEnqueue(_stagedSpawnRequests[i]))
                {
                    throw new InvalidOperationException(
                        "GAS.EFFECT_TRANSACTION.ERR.ValidatedSpawnCommitFailed");
                }
            }
            for (int i = 0; i < _presentationEventCount; i++)
            {
                _presentationEvents!.Publish(_stagedPresentationEvents[i]);
            }
            for (int i = 0; i < _gameplayEventCount; i++)
            {
                _gameplayEventBus!.Publish(_stagedGameplayEvents[i]);
            }
            for (int i = 0; i < _destroyedEffectCount; i++)
            {
                Entity effect = _destroyedEffects[i];
                if (_world.IsAlive(effect))
                {
                    _world.Destroy(effect);
                }
            }

            if (_rootBudget != null)
            {
                _rootBudget.CommitWrites(in _rootBudgetCheckpoint);
            }

            End();
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public void Rollback()
    {
        if (!IsActive)
        {
            return;
        }

        if (_externalCommitStarted)
        {
            RollbackExternalWrites();
        }
        if (_worldCommitStarted)
        {
            RollbackWorldWrites();
        }
        ResetAbortedStructuralCommands();
        if (_rootBudget != null)
        {
            _rootBudget.RollbackWrites(in _rootBudgetCheckpoint);
        }

        End();
    }

    private int GetOrAddAttributeEntity(Entity entity)
    {
        RequireActive();
        int existing = FindAttributeEntity(entity);
        if (existing >= 0)
        {
            return existing;
        }
        if (!_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
        {
            throw new InvalidOperationException(
                $"{AttributeTargetInvalidError}: entity={entity.Id}.");
        }
        if (_tagOps == null)
        {
            throw new InvalidOperationException(TagOps.MissingTagOpsError);
        }
        if (!_world.Has<DirtyFlags>(entity))
        {
            throw new InvalidOperationException(TagOps.MissingDirtyFlagsError);
        }
        if (_attributeCount >= _attributeEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=AttributeTargets, staged={_attributeCount + 1}, capacity={_attributeEntities.Length}.");
        }

        int index = _attributeCount++;
        _attributeEntities[index] = entity;
        _attributeOriginalValues[index] = _world.Get<AttributeBuffer>(entity);
        _attributeValues[index] = _attributeOriginalValues[index];
        _attributeChangedMasks[index] = 0UL;
        StageDirtyEntity(entity);
        return index;
    }

    private int GetOrAddGameplayEffectEntity(Entity entity)
    {
        RequireActive();
        int existing = FindEntity(_gameplayEffectEntities, _gameplayEffectCount, entity);
        if (existing >= 0)
        {
            return existing;
        }
        if (!_world.IsAlive(entity) || !_world.Has<GameplayEffect>(entity))
        {
            throw new InvalidOperationException(
                $"GAS.EFFECT_TRANSACTION.ERR.GameplayEffectInvalid: entity={entity.Id}.");
        }
        if (_gameplayEffectCount >= _gameplayEffectEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=GameplayEffects, staged={_gameplayEffectCount + 1}, capacity={_gameplayEffectEntities.Length}.");
        }

        int index = _gameplayEffectCount++;
        _gameplayEffectEntities[index] = entity;
        _gameplayEffectOriginalValues[index] = _world.Get<GameplayEffect>(entity);
        _gameplayEffectValues[index] = _gameplayEffectOriginalValues[index];
        return index;
    }

    private int GetOrAddTagEntity(Entity entity)
    {
        RequireActive();
        int existing = FindEntity(_tagEntities, _tagEntityCount, entity);
        if (existing >= 0)
        {
            return existing;
        }
        if (!_world.IsAlive(entity) ||
            !_world.Has<GameplayTagContainer>(entity) ||
            !_world.Has<TagCountContainer>(entity) ||
            !_world.Has<DirtyFlags>(entity))
        {
            throw new InvalidOperationException(TagOps.MissingDirtyFlagsError);
        }
        if (_tagEntityCount >= _tagEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=TagEntities, staged={_tagEntityCount + 1}, capacity={_tagEntities.Length}.");
        }

        int index = _tagEntityCount++;
        _tagEntities[index] = entity;
        _tagOriginalValues[index] = _world.Get<GameplayTagContainer>(entity);
        _tagValues[index] = _tagOriginalValues[index];
        _tagCountOriginalValues[index] = _world.Get<TagCountContainer>(entity);
        _tagCountValues[index] = _tagCountOriginalValues[index];
        _tagDirtyOriginalValues[index] = _world.Get<DirtyFlags>(entity);
        _tagDirtyValues[index] = _tagDirtyOriginalValues[index];
        return index;
    }

    private int GetOrAddActiveEffectEntity(Entity entity)
    {
        RequireActive();
        int existing = FindEntity(_activeEffectEntities, _activeEffectCount, entity);
        if (existing >= 0)
        {
            return existing;
        }
        if (!_world.IsAlive(entity) || !_world.Has<ActiveEffectContainer>(entity))
        {
            throw new InvalidOperationException(
                $"GAS.EFFECT_TRANSACTION.ERR.ActiveEffectContainerInvalid: entity={entity.Id}.");
        }
        if (_activeEffectCount >= _activeEffectEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=ActiveEffectContainers, staged={_activeEffectCount + 1}, capacity={_activeEffectEntities.Length}.");
        }

        int index = _activeEffectCount++;
        _activeEffectEntities[index] = entity;
        _activeEffectOriginalValues[index] = _world.Get<ActiveEffectContainer>(entity);
        _activeEffectValues[index] = _activeEffectOriginalValues[index];
        return index;
    }

    private int GetOrAddBlackboardFloatEntity(Entity entity)
    {
        RequireActive();
        int index = FindEntity(_blackboardFloatEntities, _blackboardFloatCount, entity);
        if (index >= 0) return index;
        ValidateBlackboardEntity<BlackboardFloatBuffer>(entity);
        if (_blackboardFloatCount >= _blackboardFloatEntities.Length) throw StagingCapacityExceeded(nameof(BlackboardFloatBuffer));
        index = _blackboardFloatCount++;
        _blackboardFloatEntities[index] = entity;
        _blackboardFloatOriginalValues[index] = _world.Get<BlackboardFloatBuffer>(entity);
        _blackboardFloatValues[index] = _blackboardFloatOriginalValues[index];
        return index;
    }

    private int GetOrAddBlackboardIntEntity(Entity entity)
    {
        RequireActive();
        int index = FindEntity(_blackboardIntEntities, _blackboardIntCount, entity);
        if (index >= 0) return index;
        ValidateBlackboardEntity<BlackboardIntBuffer>(entity);
        if (_blackboardIntCount >= _blackboardIntEntities.Length) throw StagingCapacityExceeded(nameof(BlackboardIntBuffer));
        index = _blackboardIntCount++;
        _blackboardIntEntities[index] = entity;
        _blackboardIntOriginalValues[index] = _world.Get<BlackboardIntBuffer>(entity);
        _blackboardIntValues[index] = _blackboardIntOriginalValues[index];
        return index;
    }

    private int GetOrAddBlackboardEntityEntity(Entity entity)
    {
        RequireActive();
        int index = FindEntity(_blackboardEntityEntities, _blackboardEntityCount, entity);
        if (index >= 0) return index;
        ValidateBlackboardEntity<BlackboardEntityBuffer>(entity);
        if (_blackboardEntityCount >= _blackboardEntityEntities.Length) throw StagingCapacityExceeded(nameof(BlackboardEntityBuffer));
        index = _blackboardEntityCount++;
        _blackboardEntityEntities[index] = entity;
        _blackboardEntityOriginalValues[index] = _world.Get<BlackboardEntityBuffer>(entity);
        _blackboardEntityValues[index] = _blackboardEntityOriginalValues[index];
        return index;
    }

    private void ValidateBlackboardEntity<T>(Entity entity)
    {
        if (!_world.IsAlive(entity) || !_world.Has<T>(entity))
        {
            throw new InvalidOperationException(
                $"GAS.EFFECT_TRANSACTION.ERR.MissingBlackboard: entity={entity.Id}, component={typeof(T).Name}.");
        }
    }

    private InvalidOperationException StagingCapacityExceeded(string destination)
    {
        return new InvalidOperationException(
            $"{CapacityExceededError}: destination={destination}Targets.");
    }

    private InvalidOperationException BlackboardCapacityExceeded(string destination, Entity entity, int keyId)
    {
        return new InvalidOperationException(
            $"{CapacityExceededError}: destination={destination}, entity={entity.Id}, keyId={keyId}.");
    }

    private int FindAttributeEntity(Entity entity)
    {
        for (int i = 0; i < _attributeCount; i++)
        {
            if (_attributeEntities[i] == entity)
            {
                return i;
            }
        }
        return -1;
    }

    private void RefreshAttributeChanged(int index, int attributeId)
    {
        if ((uint)attributeId >= AttributeBuffer.MAX_ATTRS)
        {
            return;
        }
        ulong bit = 1UL << attributeId;
        if (_attributeValues[index].GetCurrent(attributeId) !=
            _attributeOriginalValues[index].GetCurrent(attributeId))
        {
            _attributeChangedMasks[index] |= bit;
        }
        else
        {
            _attributeChangedMasks[index] &= ~bit;
        }
    }

    private void ValidateCommit()
    {
        for (int i = 0; i < _attributeCount; i++)
        {
            Entity entity = _attributeEntities[i];
            if (!_world.IsAlive(entity) ||
                !_world.Has<AttributeBuffer>(entity) ||
                !_world.Has<DirtyFlags>(entity))
            {
                throw new InvalidOperationException(
                    $"{AttributeTargetInvalidError}: entity={entity.Id}.");
            }
        }

        if (_dirtyEntityCount > 0)
        {
            if (_tagOps == null)
            {
                throw new InvalidOperationException(TagOps.MissingTagOpsError);
            }

            int unqueuedCount = 0;
            for (int i = 0; i < _dirtyEntityCount; i++)
            {
                Entity entity = _dirtyEntities[i];
                if (!_world.IsAlive(entity) || !_world.Has<DirtyFlags>(entity))
                {
                    throw new InvalidOperationException(TagOps.MissingDirtyFlagsError);
                }
                if (_world.Get<DirtyFlags>(entity).DeferredTriggerQueued == 0)
                {
                    unqueuedCount++;
                }
            }

            DirtyEntityQueue dirtyQueue = _tagOps.DirtyEntities;
            if (unqueuedCount > dirtyQueue.Capacity - dirtyQueue.Count)
            {
                throw new InvalidOperationException(
                    $"{CapacityExceededError}: destination=DirtyEntityQueue, staged={unqueuedCount}, available={dirtyQueue.Capacity - dirtyQueue.Count}.");
            }
        }

        if (_effectRequestCount > 0 &&
            (_effectRequests == null || _effectRequestCount > _effectRequests.AvailableCapacity))
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=EffectRequestQueue, staged={_effectRequestCount}, available={_effectRequests?.AvailableCapacity ?? 0}.");
        }
        if (_presentationEventCount > 0 &&
            (_presentationEvents == null || _presentationEventCount > _presentationEvents.AvailableCapacity))
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=GasPresentationEventBuffer, staged={_presentationEventCount}, available={_presentationEvents?.AvailableCapacity ?? 0}.");
        }
        ValidateEntities<GameplayEffect>(_gameplayEffectEntities, _gameplayEffectCount);
        ValidateTagEntities();
        ValidateEntities<ActiveEffectContainer>(_activeEffectEntities, _activeEffectCount);
        if (_spawnRequestCount > 0 &&
            (_spawnRequests == null || _spawnRequestCount > _spawnRequests.FreeCapacity))
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=RuntimeEntitySpawnQueue, staged={_spawnRequestCount}, available={_spawnRequests?.FreeCapacity ?? 0}.");
        }
        if (_gameplayEventCount > 0 &&
            (_gameplayEventBus == null || _gameplayEventCount > _gameplayEventBus.AvailableNextCapacity))
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=GameplayEventBus, staged={_gameplayEventCount}, available={_gameplayEventBus?.AvailableNextCapacity ?? 0}.");
        }

        ValidateEntities<BlackboardFloatBuffer>(_blackboardFloatEntities, _blackboardFloatCount);
        ValidateEntities<BlackboardIntBuffer>(_blackboardIntEntities, _blackboardIntCount);
        ValidateEntities<BlackboardEntityBuffer>(_blackboardEntityEntities, _blackboardEntityCount);
        for (int i = 0; i < _cancelledEffectCount; i++)
        {
            if (!_world.IsAlive(_cancelledEffects[i]) || !_world.Has<GameplayEffect>(_cancelledEffects[i]))
            {
                throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.CancelTargetInvalid");
            }
        }
        for (int i = 0; i < _destroyedEffectCount; i++)
        {
            if (!_world.IsAlive(_destroyedEffects[i]))
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_TRANSACTION.ERR.DestroyedEffectInvalid: entity={_destroyedEffects[i].Id}.");
            }
        }
        for (int i = 0; i < _aggregateDirtyCount; i++)
        {
            if (!_world.IsAlive(_aggregateDirtyEntities[i]))
            {
                throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.AggregateTargetInvalid");
            }
        }
        ValidateRelationState();
        ValidateListenerRegistrations();
        ValidateListenerRemovals();
    }

    private void ValidateTagEntities()
    {
        for (int i = 0; i < _tagEntityCount; i++)
        {
            Entity entity = _tagEntities[i];
            if (!_world.IsAlive(entity) ||
                !_world.Has<GameplayTagContainer>(entity) ||
                !_world.Has<TagCountContainer>(entity) ||
                !_world.Has<DirtyFlags>(entity))
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_TRANSACTION.ERR.TagEntityInvalid: entity={entity.Id}.");
            }
        }
    }

    private void ValidateListenerRemovals()
    {
        for (int i = 0; i < _listenerRemovalCount; i++)
        {
            Entity entity = _listenerRemovals[i].Entity;
            if (!_world.IsAlive(entity) || !_world.Has<EffectPhaseListenerBuffer>(entity))
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_TRANSACTION.ERR.ListenerTargetInvalid: entity={entity.Id}.");
            }
        }
    }

    private void ValidateEntities<T>(Entity[] entities, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!_world.IsAlive(entities[i]) || !_world.Has<T>(entities[i]))
            {
                throw new InvalidOperationException(
                    $"GAS.EFFECT_TRANSACTION.ERR.StagedEntityInvalid: entity={entities[i].Id}, component={typeof(T).Name}.");
            }
        }
    }

    private unsafe void ValidateListenerRegistrations()
    {
        for (int registrationIndex = 0; registrationIndex < _listenerRegistrationCount; registrationIndex++)
        {
            ref ListenerRegistration registration = ref _listenerRegistrations[registrationIndex];
            for (int setupIndex = 0; setupIndex < registration.Setup.Count; setupIndex++)
            {
                Entity entity = registration.Setup.Scopes[setupIndex] == (byte)PhaseListenerScope.Target
                    ? registration.Context.Target
                    : registration.Context.Source;
                if (!_world.IsAlive(entity))
                {
                    throw new InvalidOperationException(
                        $"GAS.EFFECT_TRANSACTION.ERR.ListenerTargetInvalid: entity={entity.Id}.");
                }
                if (HasEarlierListenerEntity(registrationIndex, setupIndex, entity))
                {
                    continue;
                }

                int stagedCount = CountListenerEntriesForEntity(entity);
                int existingCount = _world.Has<EffectPhaseListenerBuffer>(entity)
                    ? _world.Get<EffectPhaseListenerBuffer>(entity).Count
                    : 0;
                if (existingCount + stagedCount > EffectPhaseListenerBuffer.CAPACITY)
                {
                    throw new InvalidOperationException(
                        $"{CapacityExceededError}: destination=EffectPhaseListenerBuffer, entity={entity.Id}, staged={stagedCount}, available={EffectPhaseListenerBuffer.CAPACITY - existingCount}.");
                }
            }
        }
    }

    private void PrepareCommitState()
    {
        for (int i = 0; i < _attributeCount; i++)
        {
            ulong changedMask = _attributeChangedMasks[i];
            if (changedMask == 0UL)
            {
                continue;
            }

            Entity entity = _attributeEntities[i];
            bool existed = _world.Has<GameplayAttributeChangedBits>(entity);
            _attributeChangedExisted[i] = existed;
            _attributeChangedOriginalValues[i] = existed
                ? _world.Get<GameplayAttributeChangedBits>(entity)
                : default;
            _attributeChangedValues[i] = _attributeChangedOriginalValues[i];
            for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
            {
                if ((changedMask & (1UL << attributeId)) != 0UL)
                {
                    _attributeChangedValues[i].Mark(attributeId);
                }
            }

            if (!existed)
            {
                _structuralCommands.Add(entity, _attributeChangedValues[i]);
            }
        }

        for (int i = 0; i < _dirtyEntityCount; i++)
        {
            _dirtyOriginalValues[i] = _world.Get<DirtyFlags>(_dirtyEntities[i]);
        }
        for (int i = 0; i < _cancelledEffectCount; i++)
        {
            _cancelledEffectOriginalValues[i] = _world.Get<GameplayEffect>(_cancelledEffects[i]).CancelRequested;
        }
        for (int i = 0; i < _aggregateDirtyCount; i++)
        {
            Entity entity = _aggregateDirtyEntities[i];
            bool existed = _world.Has<AttributeAggregateDirty>(entity);
            _aggregateDirtyExisted[i] = existed;
            if (!existed)
            {
                _structuralCommands.Add(entity, new AttributeAggregateDirty());
            }
        }

        PrepareListenerValues();
        PrepareRelationValues();
    }

    private void ValidateRelationState()
    {
        for (int i = 0; i < _relationParentCount; i++)
        {
            Entity parent = _relationParentEntities[i];
            bool exists = _world.IsAlive(parent) && _world.Has<ChildrenBuffer>(parent);
            if (!_world.IsAlive(parent) ||
                exists != _relationParentExisted[i] ||
                (exists && !ChildrenBuffersEqual(
                    in _world.Get<ChildrenBuffer>(parent),
                    in _relationParentOriginalValues[i])))
            {
                throw new InvalidOperationException(
                    $"{RelationTargetInvalidError}: parent={parent.Id}.");
            }
        }
        for (int i = 0; i < _relationChildCount; i++)
        {
            Entity subject = _relationChildEntities[i];
            bool childOfExists = _world.IsAlive(subject) && _world.Has<ChildOf>(subject);
            if (!_world.IsAlive(subject) ||
                childOfExists != _relationChildExisted[i] ||
                (childOfExists &&
                 _world.Get<ChildOf>(subject).Parent != _relationChildOriginalValues[i].Parent))
            {
                throw new InvalidOperationException(
                    $"{RelationTargetInvalidError}: subject={subject.Id}.");
            }
            if (_relationSnapPositions[i] && !RelationPositionsMatchOriginal(subject, i))
            {
                throw new InvalidOperationException(
                    $"{RelationTargetInvalidError}: positionSubject={subject.Id}.");
            }
        }
    }

    private bool RelationPositionsMatchOriginal(Entity subject, int index)
    {
        bool worldPositionExists = _world.Has<WorldPositionCm>(subject);
        if (worldPositionExists != _relationWorldPositionExisted[index] ||
            (worldPositionExists &&
             !_world.Get<WorldPositionCm>(subject).Value.Equals(
                 _relationWorldPositionOriginalValues[index].Value)))
        {
            return false;
        }

        bool previousPositionExists = _world.Has<PreviousWorldPositionCm>(subject);
        if (previousPositionExists != _relationPreviousPositionExisted[index] ||
            (previousPositionExists &&
             !_world.Get<PreviousWorldPositionCm>(subject).Value.Equals(
                 _relationPreviousPositionOriginalValues[index].Value)))
        {
            return false;
        }

        Entity source = _relationSnapSourceEntities[index];
        if (!_world.IsAlive(source))
        {
            return false;
        }
        bool sourceWorldPositionExists = _world.Has<WorldPositionCm>(source);
        if (sourceWorldPositionExists != _relationSnapSourceWorldPositionExisted[index] ||
            (sourceWorldPositionExists &&
             !_world.Get<WorldPositionCm>(source).Value.Equals(
                 _relationSnapSourceWorldPositionOriginalValues[index].Value)))
        {
            return false;
        }

        bool sourcePreviousPositionExists = _world.Has<PreviousWorldPositionCm>(source);
        return sourcePreviousPositionExists == _relationSnapSourcePreviousPositionExisted[index] &&
               (!sourcePreviousPositionExists ||
                _world.Get<PreviousWorldPositionCm>(source).Value.Equals(
                    _relationSnapSourcePreviousPositionOriginalValues[index].Value));
    }

    private static bool ChildrenBuffersEqual(in ChildrenBuffer left, in ChildrenBuffer right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }
        for (int i = 0; i < left.Count; i++)
        {
            if (left.Get(i) != right.Get(i))
            {
                return false;
            }
        }

        return true;
    }

    private void PrepareRelationValues()
    {
        for (int i = 0; i < _relationParentCount; i++)
        {
            if (_relationParentShouldExist[i] && !_relationParentExisted[i])
            {
                _structuralCommands.Add(_relationParentEntities[i], _relationParentValues[i]);
            }
        }
        for (int i = 0; i < _relationChildCount; i++)
        {
            Entity subject = _relationChildEntities[i];
            if (!_relationChildExisted[i])
            {
                _structuralCommands.Add(subject, _relationChildValues[i]);
            }
            if (!_relationSnapPositions[i])
            {
                continue;
            }
            if (!_relationWorldPositionExisted[i])
            {
                _structuralCommands.Add(subject, _relationWorldPositionValues[i]);
            }
            if (!_relationPreviousPositionExisted[i])
            {
                _structuralCommands.Add(subject, _relationPreviousPositionValues[i]);
            }
        }
    }

    private unsafe void PrepareListenerValues()
    {
        for (int removalIndex = 0; removalIndex < _listenerRemovalCount; removalIndex++)
        {
            ref ListenerRemoval removal = ref _listenerRemovals[removalIndex];
            int listenerEntityIndex = GetOrAddExistingListenerEntity(removal.Entity);
            _listenerValues[listenerEntityIndex].RemoveByOwner(removal.OwnerEffectId);
        }

        for (int registrationIndex = 0; registrationIndex < _listenerRegistrationCount; registrationIndex++)
        {
            ref ListenerRegistration registration = ref _listenerRegistrations[registrationIndex];
            for (int setupIndex = 0; setupIndex < registration.Setup.Count; setupIndex++)
            {
                PhaseListenerScope scope = (PhaseListenerScope)registration.Setup.Scopes[setupIndex];
                Entity entity = scope == PhaseListenerScope.Target
                    ? registration.Context.Target
                    : registration.Context.Source;
                int listenerEntityIndex = FindEntity(_listenerEntities, _listenerEntityCount, entity);
                if (listenerEntityIndex < 0)
                {
                    if (_listenerEntityCount >= _listenerEntities.Length)
                    {
                        throw new InvalidOperationException(
                            $"{CapacityExceededError}: destination=ListenerEntities, staged={_listenerEntityCount + 1}, capacity={_listenerEntities.Length}.");
                    }

                    listenerEntityIndex = _listenerEntityCount++;
                    _listenerEntities[listenerEntityIndex] = entity;
                    bool existed = _world.Has<EffectPhaseListenerBuffer>(entity);
                    _listenerExisted[listenerEntityIndex] = existed;
                    _listenerOriginalValues[listenerEntityIndex] = existed
                        ? _world.Get<EffectPhaseListenerBuffer>(entity)
                        : default;
                    _listenerValues[listenerEntityIndex] = _listenerOriginalValues[listenerEntityIndex];
                }

                if (!_listenerValues[listenerEntityIndex].TryAdd(
                    registration.Setup.ListenTagIds[setupIndex],
                    registration.Setup.ListenEffectIds[setupIndex],
                    (EffectPhaseId)registration.Setup.Phases[setupIndex],
                    scope,
                    (PhaseListenerActionFlags)registration.Setup.ActionFlags[setupIndex],
                    registration.Setup.GraphProgramIds[setupIndex],
                    registration.Setup.EventTagIds[setupIndex],
                    registration.Setup.Priorities[setupIndex],
                    registration.OwnerEffectId))
                {
                    throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.ValidatedListenerCommitFailed");
                }
            }
        }

        for (int i = 0; i < _listenerEntityCount; i++)
        {
            if (!_listenerExisted[i])
            {
                _structuralCommands.Add(_listenerEntities[i], _listenerValues[i]);
            }
        }
    }

    private int GetOrAddExistingListenerEntity(Entity entity)
    {
        int index = FindEntity(_listenerEntities, _listenerEntityCount, entity);
        if (index >= 0)
        {
            return index;
        }
        if (_listenerEntityCount >= _listenerEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=ListenerEntities, staged={_listenerEntityCount + 1}, capacity={_listenerEntities.Length}.");
        }

        index = _listenerEntityCount++;
        _listenerEntities[index] = entity;
        _listenerExisted[index] = true;
        _listenerOriginalValues[index] = _world.Get<EffectPhaseListenerBuffer>(entity);
        _listenerValues[index] = _listenerOriginalValues[index];
        return index;
    }

    private void CaptureExternalWriteCheckpoints()
    {
        if (_dirtyEntityCount > 0)
        {
            _dirtyEntityCheckpoint = _tagOps!.DirtyEntities.CaptureWriteCheckpoint();
        }
        if (_effectRequestCount > 0)
        {
            _effectRequestCheckpoint = _effectRequests!.CaptureWriteCheckpoint();
        }
        if (_spawnRequestCount > 0)
        {
            _spawnRequestCheckpoint = _spawnRequests!.CaptureWriteCheckpoint();
        }
        if (_presentationEventCount > 0)
        {
            _presentationEventCheckpoint = _presentationEvents!.Count;
        }
        if (_gameplayEventCount > 0)
        {
            _gameplayEventCheckpoint = _gameplayEventBus!.CaptureWriteCheckpoint();
        }
    }

    private void RollbackExternalWrites()
    {
        if (_gameplayEventCount > 0)
        {
            _gameplayEventBus!.RollbackWrites(in _gameplayEventCheckpoint);
        }
        if (_presentationEventCount > 0)
        {
            _presentationEvents!.RollbackWrites(_presentationEventCheckpoint);
        }
        if (_spawnRequestCount > 0)
        {
            _spawnRequests!.RollbackWrites(in _spawnRequestCheckpoint);
        }
        if (_effectRequestCount > 0)
        {
            _effectRequests!.RollbackWrites(in _effectRequestCheckpoint);
        }
        if (_dirtyEntityCount > 0)
        {
            _tagOps!.DirtyEntities.RollbackWrites(in _dirtyEntityCheckpoint);
        }
    }

    private void RollbackWorldWrites()
    {
        for (int i = 0; i < _attributeCount; i++)
        {
            Entity entity = _attributeEntities[i];
            if (!_world.IsAlive(entity))
            {
                continue;
            }
            if (_world.Has<AttributeBuffer>(entity))
            {
                _world.Get<AttributeBuffer>(entity) = _attributeOriginalValues[i];
            }
            if (_attributeChangedMasks[i] != 0UL &&
                _attributeChangedExisted[i] &&
                _world.Has<GameplayAttributeChangedBits>(entity))
            {
                _world.Get<GameplayAttributeChangedBits>(entity) = _attributeChangedOriginalValues[i];
            }
        }
        for (int i = 0; i < _dirtyEntityCount; i++)
        {
            Entity entity = _dirtyEntities[i];
            if (_world.IsAlive(entity) && _world.Has<DirtyFlags>(entity))
            {
                _world.Get<DirtyFlags>(entity) = _dirtyOriginalValues[i];
            }
        }
        for (int i = 0; i < _tagEntityCount; i++)
        {
            Entity entity = _tagEntities[i];
            if (_world.IsAlive(entity) &&
                _world.Has<GameplayTagContainer>(entity) &&
                _world.Has<TagCountContainer>(entity) &&
                _world.Has<DirtyFlags>(entity))
            {
                _world.Get<GameplayTagContainer>(entity) = _tagOriginalValues[i];
                _world.Get<TagCountContainer>(entity) = _tagCountOriginalValues[i];
                _world.Get<DirtyFlags>(entity) = _tagDirtyOriginalValues[i];
            }
        }
        for (int i = 0; i < _activeEffectCount; i++)
        {
            Entity entity = _activeEffectEntities[i];
            if (_world.IsAlive(entity) && _world.Has<ActiveEffectContainer>(entity))
            {
                _world.Get<ActiveEffectContainer>(entity) = _activeEffectOriginalValues[i];
            }
        }
        for (int i = 0; i < _gameplayEffectCount; i++)
        {
            Entity entity = _gameplayEffectEntities[i];
            if (_world.IsAlive(entity) && _world.Has<GameplayEffect>(entity))
            {
                _world.Get<GameplayEffect>(entity) = _gameplayEffectOriginalValues[i];
            }
        }
        for (int i = 0; i < _blackboardFloatCount; i++)
        {
            _world.Get<BlackboardFloatBuffer>(_blackboardFloatEntities[i]) = _blackboardFloatOriginalValues[i];
        }
        for (int i = 0; i < _blackboardIntCount; i++)
        {
            _world.Get<BlackboardIntBuffer>(_blackboardIntEntities[i]) = _blackboardIntOriginalValues[i];
        }
        for (int i = 0; i < _blackboardEntityCount; i++)
        {
            _world.Get<BlackboardEntityBuffer>(_blackboardEntityEntities[i]) = _blackboardEntityOriginalValues[i];
        }
        for (int i = 0; i < _cancelledEffectCount; i++)
        {
            _world.Get<GameplayEffect>(_cancelledEffects[i]).CancelRequested = _cancelledEffectOriginalValues[i];
        }
        for (int i = 0; i < _listenerEntityCount; i++)
        {
            if (_listenerExisted[i] && _world.Has<EffectPhaseListenerBuffer>(_listenerEntities[i]))
            {
                _world.Get<EffectPhaseListenerBuffer>(_listenerEntities[i]) = _listenerOriginalValues[i];
            }
        }
        for (int i = 0; i < _relationParentCount; i++)
        {
            Entity parent = _relationParentEntities[i];
            if (_relationParentExisted[i] &&
                _world.IsAlive(parent) &&
                _world.Has<ChildrenBuffer>(parent))
            {
                _world.Get<ChildrenBuffer>(parent) = _relationParentOriginalValues[i];
            }
        }
        for (int i = 0; i < _relationChildCount; i++)
        {
            Entity subject = _relationChildEntities[i];
            if (!_world.IsAlive(subject))
            {
                continue;
            }
            if (_relationChildExisted[i] && _world.Has<ChildOf>(subject))
            {
                _world.Get<ChildOf>(subject) = _relationChildOriginalValues[i];
            }
            if (_relationSnapPositions[i])
            {
                if (_relationWorldPositionExisted[i] && _world.Has<WorldPositionCm>(subject))
                {
                    _world.Get<WorldPositionCm>(subject) = _relationWorldPositionOriginalValues[i];
                }
                if (_relationPreviousPositionExisted[i] && _world.Has<PreviousWorldPositionCm>(subject))
                {
                    _world.Get<PreviousWorldPositionCm>(subject) = _relationPreviousPositionOriginalValues[i];
                }
            }
        }

        for (int i = 0; i < _attributeCount; i++)
        {
            if (_attributeChangedMasks[i] != 0UL &&
                !_attributeChangedExisted[i] &&
                _world.IsAlive(_attributeEntities[i]) &&
                _world.Has<GameplayAttributeChangedBits>(_attributeEntities[i]))
            {
                _structuralRollbackCommands.Remove<GameplayAttributeChangedBits>(_attributeEntities[i]);
            }
        }
        for (int i = 0; i < _aggregateDirtyCount; i++)
        {
            if (!_aggregateDirtyExisted[i] &&
                _world.IsAlive(_aggregateDirtyEntities[i]) &&
                _world.Has<AttributeAggregateDirty>(_aggregateDirtyEntities[i]))
            {
                _structuralRollbackCommands.Remove<AttributeAggregateDirty>(_aggregateDirtyEntities[i]);
            }
        }
        for (int i = 0; i < _listenerEntityCount; i++)
        {
            if (!_listenerExisted[i] &&
                _world.IsAlive(_listenerEntities[i]) &&
                _world.Has<EffectPhaseListenerBuffer>(_listenerEntities[i]))
            {
                _structuralRollbackCommands.Remove<EffectPhaseListenerBuffer>(_listenerEntities[i]);
            }
        }
        for (int i = 0; i < _relationParentCount; i++)
        {
            if (_relationParentShouldExist[i] &&
                !_relationParentExisted[i] &&
                _world.IsAlive(_relationParentEntities[i]) &&
                _world.Has<ChildrenBuffer>(_relationParentEntities[i]))
            {
                _structuralRollbackCommands.Remove<ChildrenBuffer>(_relationParentEntities[i]);
            }
        }
        for (int i = 0; i < _relationChildCount; i++)
        {
            Entity subject = _relationChildEntities[i];
            if (!_world.IsAlive(subject))
            {
                continue;
            }
            if (!_relationChildExisted[i] && _world.Has<ChildOf>(subject))
            {
                _structuralRollbackCommands.Remove<ChildOf>(subject);
            }
            if (_relationSnapPositions[i] &&
                !_relationWorldPositionExisted[i] &&
                _world.Has<WorldPositionCm>(subject))
            {
                _structuralRollbackCommands.Remove<WorldPositionCm>(subject);
            }
            if (_relationSnapPositions[i] &&
                !_relationPreviousPositionExisted[i] &&
                _world.Has<PreviousWorldPositionCm>(subject))
            {
                _structuralRollbackCommands.Remove<PreviousWorldPositionCm>(subject);
            }
        }
        if (_structuralRollbackCommands.Size > 0)
        {
            _structuralRollbackCommands.Playback(_world);
        }
    }

    private unsafe bool HasEarlierListenerEntity(int registrationIndex, int setupIndex, Entity entity)
    {
        for (int previousRegistration = 0; previousRegistration <= registrationIndex; previousRegistration++)
        {
            ref ListenerRegistration registration = ref _listenerRegistrations[previousRegistration];
            int limit = previousRegistration == registrationIndex ? setupIndex : registration.Setup.Count;
            for (int previousSetup = 0; previousSetup < limit; previousSetup++)
            {
                Entity previousEntity = registration.Setup.Scopes[previousSetup] == (byte)PhaseListenerScope.Target
                    ? registration.Context.Target
                    : registration.Context.Source;
                if (previousEntity == entity) return true;
            }
        }
        return false;
    }

    private unsafe int CountListenerEntriesForEntity(Entity entity)
    {
        int count = 0;
        for (int registrationIndex = 0; registrationIndex < _listenerRegistrationCount; registrationIndex++)
        {
            ref ListenerRegistration registration = ref _listenerRegistrations[registrationIndex];
            for (int setupIndex = 0; setupIndex < registration.Setup.Count; setupIndex++)
            {
                Entity candidate = registration.Setup.Scopes[setupIndex] == (byte)PhaseListenerScope.Target
                    ? registration.Context.Target
                    : registration.Context.Source;
                if (candidate == entity) count++;
            }
        }
        return count;
    }

    private int GetOrAddRelationParent(Entity parent)
    {
        int index = FindEntity(_relationParentEntities, _relationParentCount, parent);
        if (index >= 0)
        {
            return index;
        }
        if (_relationParentCount >= _relationParentEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=RelationParents, staged={_relationParentCount + 1}, capacity={_relationParentEntities.Length}.");
        }

        index = _relationParentCount++;
        _relationParentEntities[index] = parent;
        bool existed = _world.Has<ChildrenBuffer>(parent);
        _relationParentExisted[index] = existed;
        _relationParentShouldExist[index] = false;
        _relationParentOriginalValues[index] = existed
            ? _world.Get<ChildrenBuffer>(parent)
            : default;
        _relationParentValues[index] = _relationParentOriginalValues[index];
        return index;
    }

    private int GetOrAddRelationChild(Entity subject)
    {
        int index = FindEntity(_relationChildEntities, _relationChildCount, subject);
        if (index >= 0)
        {
            return index;
        }
        if (_relationChildCount >= _relationChildEntities.Length)
        {
            throw new InvalidOperationException(
                $"{CapacityExceededError}: destination=RelationChildren, staged={_relationChildCount + 1}, capacity={_relationChildEntities.Length}.");
        }

        index = _relationChildCount++;
        _relationChildEntities[index] = subject;
        bool childOfExisted = _world.Has<ChildOf>(subject);
        _relationChildExisted[index] = childOfExisted;
        _relationChildOriginalValues[index] = childOfExisted
            ? _world.Get<ChildOf>(subject)
            : default;
        _relationChildValues[index] = _relationChildOriginalValues[index];
        bool worldPositionExisted = _world.Has<WorldPositionCm>(subject);
        _relationWorldPositionExisted[index] = worldPositionExisted;
        _relationWorldPositionOriginalValues[index] = worldPositionExisted
            ? _world.Get<WorldPositionCm>(subject)
            : default;
        _relationWorldPositionValues[index] = _relationWorldPositionOriginalValues[index];
        bool previousPositionExisted = _world.Has<PreviousWorldPositionCm>(subject);
        _relationPreviousPositionExisted[index] = previousPositionExisted;
        _relationPreviousPositionOriginalValues[index] = previousPositionExisted
            ? _world.Get<PreviousWorldPositionCm>(subject)
            : default;
        _relationPreviousPositionValues[index] = _relationPreviousPositionOriginalValues[index];
        _relationSnapPositions[index] = false;
        _relationSnapSourceEntities[index] = Entity.Null;
        return index;
    }

    private void CaptureRelationSnapSource(int index, Entity source)
    {
        _relationSnapSourceEntities[index] = source;
        bool worldPositionExisted = _world.Has<WorldPositionCm>(source);
        _relationSnapSourceWorldPositionExisted[index] = worldPositionExisted;
        _relationSnapSourceWorldPositionOriginalValues[index] = worldPositionExisted
            ? _world.Get<WorldPositionCm>(source)
            : default;
        bool previousPositionExisted = _world.Has<PreviousWorldPositionCm>(source);
        _relationSnapSourcePreviousPositionExisted[index] = previousPositionExisted;
        _relationSnapSourcePreviousPositionOriginalValues[index] = previousPositionExisted
            ? _world.Get<PreviousWorldPositionCm>(source)
            : default;
    }

    private bool TryReadRelationWorldPosition(Entity entity, out WorldPositionCm position)
    {
        int index = FindEntity(_relationChildEntities, _relationChildCount, entity);
        if (index >= 0 && _relationSnapPositions[index])
        {
            position = _relationWorldPositionValues[index];
            return true;
        }
        if (_world.Has<WorldPositionCm>(entity))
        {
            position = _world.Get<WorldPositionCm>(entity);
            return true;
        }

        position = default;
        return false;
    }

    private bool TryReadRelationPreviousPosition(Entity entity, out PreviousWorldPositionCm position)
    {
        int index = FindEntity(_relationChildEntities, _relationChildCount, entity);
        if (index >= 0 && _relationSnapPositions[index])
        {
            position = _relationPreviousPositionValues[index];
            return true;
        }
        if (_world.Has<PreviousWorldPositionCm>(entity))
        {
            position = _world.Get<PreviousWorldPositionCm>(entity);
            return true;
        }

        position = default;
        return false;
    }

    private void RequireActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException(ScopeNotActiveError);
        }
    }

    private void End()
    {
        _attributeCount = 0;
        _dirtyEntityCount = 0;
        _effectRequestCount = 0;
        _spawnRequestCount = 0;
        _presentationEventCount = 0;
        _gameplayEventCount = 0;
        _gameplayEffectCount = 0;
        _tagEntityCount = 0;
        _activeEffectCount = 0;
        _destroyedEffectCount = 0;
        _blackboardFloatCount = 0;
        _blackboardIntCount = 0;
        _blackboardEntityCount = 0;
        _cancelledEffectCount = 0;
        _aggregateDirtyCount = 0;
        _listenerRegistrationCount = 0;
        _listenerRemovalCount = 0;
        _listenerEntityCount = 0;
        _relationParentCount = 0;
        _relationChildCount = 0;
        _gameplayEventBus = null;
        _worldCommitStarted = false;
        _externalCommitStarted = false;
        IsActive = false;
    }

    private void ResetAbortedStructuralCommands()
    {
        if (_structuralCommands.Size == 0)
        {
            return;
        }

        _structuralCommands.Dispose();
        _structuralCommands = new CommandBuffer(_structuralCommandCapacity);
    }

    public void Dispose()
    {
        _structuralCommands.Dispose();
        _structuralRollbackCommands.Dispose();
    }

    private static int FindEntity(Entity[] entities, int count, Entity entity)
    {
        for (int i = 0; i < count; i++)
        {
            if (entities[i] == entity) return i;
        }
        return -1;
    }

    private static bool Contains(Entity[] entities, int count, Entity entity)
    {
        return FindEntity(entities, count, entity) >= 0;
    }

    private struct ListenerRegistration
    {
        public EffectContext Context;
        public EffectPhaseListenerBuffer Setup;
        public int OwnerEffectId;
    }

    private struct ListenerRemoval
    {
        public Entity Entity;
        public int OwnerEffectId;
    }
}
