using System;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Gameplay.GAS;

/// <summary>
/// Fixed-capacity staging boundary for side effects produced while a persistent
/// effect executes OnResolve, OnHit, and OnApply. Nothing reaches the world or
/// externally visible queues until every phase has completed successfully.
/// </summary>
public sealed class EffectPhaseSideEffectTransaction : IDisposable
{
    public const string CapacityExceededError = "GAS.EFFECT_TRANSACTION.ERR.CapacityExceeded";
    public const string ScopeAlreadyActiveError = "GAS.EFFECT_TRANSACTION.ERR.ScopeAlreadyActive";
    public const string ScopeNotActiveError = "GAS.EFFECT_TRANSACTION.ERR.ScopeNotActive";
    public const string UnsupportedSideEffectError = "GAS.EFFECT_TRANSACTION.ERR.UnsupportedSideEffect";

    private readonly World _world;
    private readonly TagOps? _tagOps;
    private readonly EffectRequestQueue? _effectRequests;
    private readonly RuntimeEntitySpawnQueue? _spawnRequests;
    private readonly GasPresentationEventBuffer? _presentationEvents;
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
    private readonly Entity[] _listenerEntities;
    private readonly EffectPhaseListenerBuffer[] _listenerOriginalValues;
    private readonly EffectPhaseListenerBuffer[] _listenerValues;
    private readonly bool[] _listenerExisted;
    private CommandBuffer _structuralCommands;
    private readonly CommandBuffer _structuralRollbackCommands;
    private readonly int _structuralCommandCapacity;
    private int _attributeCount;
    private int _dirtyEntityCount;
    private int _effectRequestCount;
    private int _spawnRequestCount;
    private int _presentationEventCount;
    private int _gameplayEventCount;
    private int _blackboardFloatCount;
    private int _blackboardIntCount;
    private int _blackboardEntityCount;
    private int _cancelledEffectCount;
    private int _aggregateDirtyCount;
    private int _listenerRegistrationCount;
    private int _listenerEntityCount;
    private GameplayEventBus? _gameplayEventBus;
    private bool _worldCommitStarted;
    private bool _externalCommitStarted;
    private DirtyEntityQueue.WriteCheckpoint _dirtyEntityCheckpoint;
    private EffectRequestQueue.WriteCheckpoint _effectRequestCheckpoint;
    private RuntimeEntitySpawnQueue.WriteCheckpoint _spawnRequestCheckpoint;
    private int _presentationEventCheckpoint;
    private GameplayEventBus.WriteCheckpoint _gameplayEventCheckpoint;

    public EffectPhaseSideEffectTransaction(
        World world,
        TagOps? tagOps,
        EffectRequestQueue? effectRequests,
        RuntimeEntitySpawnQueue? spawnRequests,
        GasPresentationEventBuffer? presentationEvents,
        int attributeEntityCapacity)
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
        _listenerEntities = new Entity[listenerEntityCapacity];
        _listenerOriginalValues = new EffectPhaseListenerBuffer[listenerEntityCapacity];
        _listenerValues = new EffectPhaseListenerBuffer[listenerEntityCapacity];
        _listenerExisted = new bool[listenerEntityCapacity];
        _structuralCommandCapacity = checked(attributeEntityCapacity * 4);
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
        _blackboardFloatCount = 0;
        _blackboardIntCount = 0;
        _blackboardEntityCount = 0;
        _cancelledEffectCount = 0;
        _aggregateDirtyCount = 0;
        _listenerRegistrationCount = 0;
        _listenerEntityCount = 0;
        _gameplayEventBus = null;
        _worldCommitStarted = false;
        _externalCommitStarted = false;
        IsActive = true;
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
        if (index < 0)
        {
            return;
        }

        float before = _attributeValues[index].GetCurrent(attributeId);
        _attributeValues[index].SetCurrent(attributeId, before + delta);
        RefreshAttributeChanged(index, attributeId);
    }

    public void StageAttributeSet(Entity target, int attributeId, float value)
    {
        int index = GetOrAddAttributeEntity(target);
        if (index < 0)
        {
            return;
        }

        _attributeValues[index].SetCurrent(attributeId, value);
        RefreshAttributeChanged(index, attributeId);
    }

    public void StageModifiers(Entity target, in EffectModifiers modifiers)
    {
        int index = GetOrAddAttributeEntity(target);
        if (index < 0)
        {
            return;
        }

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
        if (!_world.IsAlive(target) || templateId <= 0 || !_world.Has<ActiveEffectContainer>(target))
        {
            return;
        }

        ActiveEffectContainer container = _world.Get<ActiveEffectContainer>(target);
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
            if (_world.Get<GameplayEffect>(effect).AggregatesModifiers)
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

    public void StageListenerRegistration(
        in EffectContext context,
        in EffectPhaseListenerBuffer setup,
        int ownerEffectId)
    {
        RequireActive();
        if (setup.Count <= 0)
        {
            return;
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
            return -1;
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
                    $"GAS.EFFECT_TRANSACTION.ERR.AttributeTargetInvalid: entity={entity.Id}.");
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
        for (int i = 0; i < _aggregateDirtyCount; i++)
        {
            if (!_world.IsAlive(_aggregateDirtyEntities[i]))
            {
                throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.AggregateTargetInvalid");
            }
        }
        ValidateListenerRegistrations();
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
    }

    private unsafe void PrepareListenerValues()
    {
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
        _blackboardFloatCount = 0;
        _blackboardIntCount = 0;
        _blackboardEntityCount = 0;
        _cancelledEffectCount = 0;
        _aggregateDirtyCount = 0;
        _listenerRegistrationCount = 0;
        _listenerEntityCount = 0;
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
}
