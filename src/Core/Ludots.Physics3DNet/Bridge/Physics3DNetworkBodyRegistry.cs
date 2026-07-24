using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Simulation;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkBodyRegistryFailure : byte
{
    None = 0,
    StructuralTickRequired = 1,
    CommandCapacityExceeded = 2,
    InvalidEntity = 3,
    EntityUnavailable = 4,
    MissingBody = 5,
    InvalidBody = 6,
    BodyKindMismatch = 7,
    MissingPose = 8,
    MissingSchema = 9,
    InvalidSchema = 10,
    SchemaMismatch = 11,
    DuplicateReplicatedBody = 12,
    DuplicateNetworkEntity = 13,
    NetworkEntityCapacityExceeded = 14,
    InvalidHandle = 15,
    StaleHandle = 16,
    RegistrySlotMismatch = 17,
    EntityTableMismatch = 18,
    ReplicatedComponentMismatch = 19,
    DuplicateReleaseCommand = 20,
    DuplicateRegistrationCommand = 21,
}

public sealed class Physics3DNetworkBodyRegistry : IDisposable
{
    private static readonly QueryDescription EligibleBodyQuery = new QueryDescription()
        .WithAll<Physics3DBodyCm, Physics3DPoseCm, ReplicationSchemaRef>()
        .WithNone<Physics3DNetworkReplicatedBody>();

    private readonly World _world;
    private readonly IPhysics3DWorld _physics;
    private readonly NetworkEntityTable _networkEntities;
    private readonly AuthoritativeSimulationTickState _simulationTicks;
    private readonly int _schemaId;

    private readonly bool[] _activeSlots;
    private readonly uint[] _slotGenerations;
    private readonly Entity[] _slotEntities;
    private readonly Physics3DBodyKind[] _slotKinds;

    private readonly Entity[] _registerEntities;
    private readonly Physics3DBodyId[] _registerBodies;
    private readonly Physics3DBodyKind[] _registerKinds;
    private readonly int[] _registerSchemaIds;
    private readonly NetworkEntityHandle[] _releaseHandles;
    private readonly bool[] _releaseQueuedSlots;
    private int _registerCount;
    private int _releaseCount;
    private bool _disposed;

    public Physics3DNetworkBodyRegistry(
        World world,
        IPhysics3DWorld physics,
        NetworkEntityTable networkEntities,
        AuthoritativeSimulationTickState simulationTicks,
        int schemaId,
        int commandCapacity)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _networkEntities = networkEntities ?? throw new ArgumentNullException(nameof(networkEntities));
        _simulationTicks = simulationTicks ?? throw new ArgumentNullException(nameof(simulationTicks));
        if (schemaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        if (commandCapacity <= 0 || commandCapacity > networkEntities.Capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandCapacity),
                commandCapacity,
                $"Physics3D network body command capacity must be in range 1..{networkEntities.Capacity}.");
        }

        _schemaId = schemaId;
        CommandCapacity = commandCapacity;
        _activeSlots = new bool[networkEntities.Capacity];
        _slotGenerations = new uint[networkEntities.Capacity];
        _slotEntities = new Entity[networkEntities.Capacity];
        _slotKinds = new Physics3DBodyKind[networkEntities.Capacity];
        _registerEntities = new Entity[commandCapacity];
        _registerBodies = new Physics3DBodyId[commandCapacity];
        _registerKinds = new Physics3DBodyKind[commandCapacity];
        _registerSchemaIds = new int[commandCapacity];
        _releaseHandles = new NetworkEntityHandle[commandCapacity];
        _releaseQueuedSlots = new bool[networkEntities.Capacity];
    }

    public int Capacity => _activeSlots.Length;
    public int CommandCapacity { get; }
    public int Count { get; private set; }
    public int PendingRegistrationCount => _registerCount;
    public int PendingReleaseCount => _releaseCount;
    public Physics3DNetworkBodyRegistryFailure LastFailure { get; private set; }
    public Entity LastFailureEntity { get; private set; }
    public NetworkEntityHandle LastFailureHandle { get; private set; }

    public bool TryQueueRegister(Entity entity)
    {
        EnsureNotDisposed();
        ResetFailure();
        if (entity == Entity.Null)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.InvalidEntity, entity);
            return false;
        }

        if (!_world.IsAlive(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.EntityUnavailable, entity);
            return false;
        }

        if (!_world.TryGet(entity, out Physics3DBodyCm body))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.MissingBody, entity);
            return false;
        }

        if (!_world.Has<Physics3DPoseCm>(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.MissingPose, entity);
            return false;
        }

        if (!_world.TryGet(entity, out ReplicationSchemaRef schema))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.MissingSchema, entity);
            return false;
        }

        if (!TryValidateRegistration(entity, in body, in schema))
        {
            return false;
        }

        for (int command = 0; command < _registerCount; command++)
        {
            if (_registerEntities[command] == entity)
            {
                SetFailure(Physics3DNetworkBodyRegistryFailure.DuplicateRegistrationCommand, entity);
                return false;
            }
        }

        if (_registerCount >= CommandCapacity)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.CommandCapacityExceeded, entity);
            return false;
        }

        QueueRegistration(entity, in body, in schema);
        return true;
    }

    public bool TryQueueEligibleBodies(out int queuedCount)
    {
        EnsureNotDisposed();
        ResetFailure();
        int initialCount = _registerCount;
        foreach (ref Chunk chunk in _world.Query(in EligibleBodyQuery))
        {
            chunk.GetSpan<Physics3DBodyCm, ReplicationSchemaRef>(
                out Span<Physics3DBodyCm> bodies,
                out Span<ReplicationSchemaRef> schemas);
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                ref Physics3DBodyCm body = ref bodies[index];
                ref ReplicationSchemaRef schema = ref schemas[index];
                bool alreadyQueued = false;
                for (int command = 0; command < initialCount; command++)
                {
                    if (_registerEntities[command] == entity)
                    {
                        alreadyQueued = true;
                        break;
                    }
                }

                if (alreadyQueued)
                {
                    continue;
                }

                if (!TryValidateRegistration(entity, in body, in schema))
                {
                    _registerCount = initialCount;
                    queuedCount = 0;
                    return false;
                }

                if (_registerCount >= CommandCapacity)
                {
                    _registerCount = initialCount;
                    SetFailure(Physics3DNetworkBodyRegistryFailure.CommandCapacityExceeded, entity);
                    queuedCount = 0;
                    return false;
                }

                QueueRegistration(entity, in body, in schema);
            }
        }

        queuedCount = _registerCount - initialCount;
        return true;
    }

    public bool TryQueueRelease(NetworkEntityHandle handle)
    {
        EnsureNotDisposed();
        ResetFailure();
        if (!TryValidateRelease(handle, out _))
        {
            return false;
        }

        int slot = handle.Slot;
        if (_releaseQueuedSlots[slot])
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.DuplicateReleaseCommand, _slotEntities[slot], handle);
            return false;
        }

        if (_releaseCount >= CommandCapacity)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.CommandCapacityExceeded, _slotEntities[slot], handle);
            return false;
        }

        _releaseQueuedSlots[slot] = true;
        _releaseHandles[_releaseCount++] = handle;
        return true;
    }

    public bool TryApplyPendingStructuralChanges()
    {
        EnsureNotDisposed();
        ResetFailure();
        if (!_simulationTicks.IsExecuting)
        {
            LastFailure = Physics3DNetworkBodyRegistryFailure.StructuralTickRequired;
            return false;
        }

        for (int command = 0; command < _releaseCount; command++)
        {
            if (!TryValidateRelease(_releaseHandles[command], out _))
            {
                return false;
            }
        }

        if (_registerCount > checked(_networkEntities.AvailableCapacity + _releaseCount))
        {
            LastFailure = Physics3DNetworkBodyRegistryFailure.NetworkEntityCapacityExceeded;
            return false;
        }

        for (int command = 0; command < _registerCount; command++)
        {
            Entity entity = _registerEntities[command];
            if (!_world.IsAlive(entity))
            {
                SetFailure(Physics3DNetworkBodyRegistryFailure.EntityUnavailable, entity);
                return false;
            }

            if (!_world.TryGet(entity, out Physics3DBodyCm body) ||
                body.Id != _registerBodies[command])
            {
                SetFailure(Physics3DNetworkBodyRegistryFailure.InvalidBody, entity);
                return false;
            }

            if (!_world.TryGet(entity, out ReplicationSchemaRef schema) ||
                schema.SchemaId != _registerSchemaIds[command])
            {
                SetFailure(Physics3DNetworkBodyRegistryFailure.SchemaMismatch, entity);
                return false;
            }

            if (!TryValidateRegistration(entity, in body, in schema))
            {
                return false;
            }
        }

        for (int command = 0; command < _releaseCount; command++)
        {
            ReleaseValidated(_releaseHandles[command]);
        }

        for (int command = 0; command < _registerCount; command++)
        {
            RegisterValidated(
                _registerEntities[command],
                _registerKinds[command]);
        }

        ClearCommands();
        return true;
    }

    public bool TryGetHandle(Entity entity, out NetworkEntityHandle handle)
    {
        EnsureNotDisposed();
        handle = default;
        if (!_networkEntities.TryResolve(entity, out NetworkEntityHandle resolved) ||
            (uint)resolved.Slot >= (uint)_activeSlots.Length ||
            !_activeSlots[resolved.Slot] ||
            _slotGenerations[resolved.Slot] != resolved.Generation ||
            _slotEntities[resolved.Slot] != entity)
        {
            return false;
        }

        handle = resolved;
        return true;
    }

    public bool TryResolve(NetworkEntityHandle handle, out Entity entity)
    {
        EnsureNotDisposed();
        entity = Entity.Null;
        if (!handle.IsValid ||
            (uint)handle.Slot >= (uint)_activeSlots.Length ||
            !_activeSlots[handle.Slot] ||
            _slotGenerations[handle.Slot] != handle.Generation ||
            !_networkEntities.TryResolve(handle, out Entity mapped) ||
            mapped != _slotEntities[handle.Slot])
        {
            return false;
        }

        entity = mapped;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registerCount != 0 || _releaseCount != 0)
        {
            throw new InvalidOperationException(
                $"Physics3D network body registry cannot dispose with {_registerCount} pending registrations and {_releaseCount} pending releases.");
        }

        for (int slot = 0; slot < _activeSlots.Length; slot++)
        {
            if (!_activeSlots[slot])
            {
                continue;
            }

            var handle = new NetworkEntityHandle(slot, _slotGenerations[slot]);
            if (!TryValidateRelease(handle, out _))
            {
                throw new InvalidOperationException(
                    $"Physics3D network body registry disposal failed for {handle}: {LastFailure}.");
            }

            ReleaseValidated(handle);
        }

        _disposed = true;
    }

    private bool TryValidateRegistration(
        Entity entity,
        in Physics3DBodyCm body,
        in ReplicationSchemaRef schema)
    {
        if (entity == Entity.Null)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.InvalidEntity, entity);
            return false;
        }

        if (!_world.IsAlive(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.EntityUnavailable, entity);
            return false;
        }

        if (!_world.Has<Physics3DBodyCm>(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.MissingBody, entity);
            return false;
        }

        if (!body.Id.IsValid || !_physics.ContainsBody(body.Id))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.InvalidBody, entity);
            return false;
        }

        if (_physics.GetBodyKind(body.Id) != body.Kind)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.BodyKindMismatch, entity);
            return false;
        }

        if (!_world.Has<Physics3DPoseCm>(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.MissingPose, entity);
            return false;
        }

        if (!_world.Has<ReplicationSchemaRef>(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.MissingSchema, entity);
            return false;
        }

        if (schema.SchemaId <= 0)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.InvalidSchema, entity);
            return false;
        }

        if (schema.SchemaId != _schemaId)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.SchemaMismatch, entity);
            return false;
        }

        if (_world.Has<Physics3DNetworkReplicatedBody>(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.DuplicateReplicatedBody, entity);
            return false;
        }

        if (_networkEntities.TryResolve(entity, out NetworkEntityHandle duplicate))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.DuplicateNetworkEntity, entity, duplicate);
            return false;
        }

        return true;
    }

    private bool TryValidateRelease(NetworkEntityHandle handle, out Entity entity)
    {
        entity = Entity.Null;
        if (!handle.IsValid || (uint)handle.Slot >= (uint)_activeSlots.Length)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.InvalidHandle, Entity.Null, handle);
            return false;
        }

        int slot = handle.Slot;
        if (!_activeSlots[slot])
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.StaleHandle, Entity.Null, handle);
            return false;
        }

        entity = _slotEntities[slot];
        if (_slotGenerations[slot] != handle.Generation)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.StaleHandle, entity, handle);
            return false;
        }

        if (entity == Entity.Null || !_world.IsAlive(entity))
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.EntityUnavailable, entity, handle);
            return false;
        }

        if (!_networkEntities.TryResolve(handle, out Entity mapped) || mapped != entity)
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.EntityTableMismatch, entity, handle);
            return false;
        }

        if (!_world.TryGet(entity, out Physics3DNetworkReplicatedBody replicated) ||
            replicated.Handle != handle ||
            replicated.AuthoritativeKind != _slotKinds[slot])
        {
            SetFailure(Physics3DNetworkBodyRegistryFailure.ReplicatedComponentMismatch, entity, handle);
            return false;
        }

        return true;
    }

    private void RegisterValidated(Entity entity, Physics3DBodyKind kind)
    {
        if (!_networkEntities.TryAllocate(entity, out NetworkEntityHandle handle))
        {
            throw new InvalidOperationException(
                $"Validated Physics3D network body '{entity}' could not allocate a network entity handle.");
        }

        int slot = handle.Slot;
        if (_activeSlots[slot])
        {
            if (!_networkEntities.TryRelease(handle))
            {
                throw new InvalidOperationException(
                    $"Physics3D network body registry failed to roll back unexpected active slot {slot}.");
            }

            throw new InvalidOperationException(
                $"Physics3D network body registry slot {slot} remained active during validated allocation.");
        }

        var replicated = new Physics3DNetworkReplicatedBody
        {
            Handle = handle,
            AuthoritativeKind = kind,
        };
        _world.Add(entity, replicated);
        _activeSlots[slot] = true;
        _slotGenerations[slot] = handle.Generation;
        _slotEntities[slot] = entity;
        _slotKinds[slot] = kind;
        Count++;
    }

    private void ReleaseValidated(NetworkEntityHandle handle)
    {
        int slot = handle.Slot;
        Entity entity = _slotEntities[slot];
        if (!_networkEntities.TryRelease(handle))
        {
            throw new InvalidOperationException(
                $"Validated Physics3D network body handle '{handle}' could not be released from the network entity table.");
        }

        _world.Remove<Physics3DNetworkReplicatedBody>(entity);
        _activeSlots[slot] = false;
        _slotGenerations[slot] = 0;
        _slotEntities[slot] = Entity.Null;
        _slotKinds[slot] = default;
        Count--;
    }

    private void ClearCommands()
    {
        for (int command = 0; command < _releaseCount; command++)
        {
            _releaseQueuedSlots[_releaseHandles[command].Slot] = false;
            _releaseHandles[command] = default;
        }

        for (int command = 0; command < _registerCount; command++)
        {
            _registerEntities[command] = Entity.Null;
            _registerBodies[command] = default;
            _registerKinds[command] = default;
            _registerSchemaIds[command] = 0;
        }

        _releaseCount = 0;
        _registerCount = 0;
    }

    private void QueueRegistration(
        Entity entity,
        in Physics3DBodyCm body,
        in ReplicationSchemaRef schema)
    {
        int command = _registerCount++;
        _registerEntities[command] = entity;
        _registerBodies[command] = body.Id;
        _registerKinds[command] = body.Kind;
        _registerSchemaIds[command] = schema.SchemaId;
    }

    private void ResetFailure()
    {
        LastFailure = Physics3DNetworkBodyRegistryFailure.None;
        LastFailureEntity = Entity.Null;
        LastFailureHandle = default;
    }

    private void SetFailure(
        Physics3DNetworkBodyRegistryFailure failure,
        Entity entity,
        NetworkEntityHandle handle = default)
    {
        LastFailure = failure;
        LastFailureEntity = entity;
        LastFailureHandle = handle;
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class Physics3DNetworkBodyRegistrySystem : BaseSystem<World, float>
{
    private readonly Physics3DNetworkBodyRegistry _registry;

    public Physics3DNetworkBodyRegistrySystem(World world, Physics3DNetworkBodyRegistry registry)
        : base(world)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public override void Update(in float deltaTime)
    {
        if (!_registry.TryQueueEligibleBodies(out _))
        {
            throw new InvalidOperationException(
                $"Physics3D network body discovery failed: {_registry.LastFailure} for '{_registry.LastFailureEntity}'.");
        }

        if (!_registry.TryApplyPendingStructuralChanges())
        {
            throw new InvalidOperationException(
                $"Physics3D network body structural commit failed: {_registry.LastFailure} for " +
                $"'{_registry.LastFailureEntity}' / '{_registry.LastFailureHandle}'.");
        }
    }
}
