using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkPlayerLifecycleFailure : byte
{
    None = 0,
    InvalidSeat = 1,
    GenerationMismatch = 2,
    GenerationNotNewer = 3,
    KnowledgeCapacityExceeded = 4,
    NetworkEntityCapacityExceeded = 5,
    PhysicsBodyCapacityExceeded = 6,
    EntityTableMismatch = 7,
    SeatConnectionStateMismatch = 8,
    DestinationCapacityExceeded = 9,
    BindingStoreRejected = 10,
}

public sealed class Physics3DNetworkPlayerLifecycle : IAuthoritativeSeatControllerResolver, IDisposable
{
    private readonly World _world;
    private readonly IPhysics3DWorld _physics;
    private readonly NetworkEntityTable _networkEntities;
    private readonly Physics3DNetworkReplicatedBindingStore _bindings;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly int _schemaId;
    private readonly Physics3DNetworkPlayerBodyConfig _bodyConfig;
    private readonly Physics3DNetworkPlayerSpawnConfig _spawnConfig;
    private readonly Physics3DShapeId _shape;
    private readonly bool[] _active;
    private readonly bool[] _connected;
    private readonly bool[] _everConnected;
    private readonly uint[] _seatGenerations;
    private readonly uint[] _retiredSeatGenerations;
    private readonly int[] _playerIds;
    private readonly Entity[] _entities;
    private readonly Physics3DBodyId[] _bodies;
    private readonly NetworkEntityHandle[] _networkHandles;
    private bool _disposed;

    public Physics3DNetworkPlayerLifecycle(
        World world,
        IPhysics3DWorld physics,
        NetworkEntityTable networkEntities,
        Physics3DNetworkReplicatedBindingStore bindings,
        KnowledgeProjectionStore knowledge,
        int seatCapacity,
        int schemaId,
        Physics3DNetworkPlayerBodyConfig bodyConfig,
        Physics3DNetworkPlayerSpawnConfig spawnConfig)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _networkEntities = networkEntities ?? throw new ArgumentNullException(nameof(networkEntities));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        if (bindings.BodySlotCapacity != physics.BodySlotCapacity ||
            bindings.NetworkSlotCapacity != networkEntities.Capacity)
        {
            throw new ArgumentException(
                "Physics3D player lifecycle, physics world, network table, and replicated binding capacities must agree.",
                nameof(bindings));
        }
        if (seatCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seatCapacity));
        }

        if (schemaId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaId));
        }

        _bodyConfig = bodyConfig ?? throw new ArgumentNullException(nameof(bodyConfig));
        _spawnConfig = spawnConfig ?? throw new ArgumentNullException(nameof(spawnConfig));
        _bodyConfig.Validate();
        _spawnConfig.Validate();
        int requiredKnowledgeCapacity = checked(seatCapacity * seatCapacity);
        if (knowledge.RecordCapacity < requiredKnowledgeCapacity)
        {
            throw new ArgumentException(
                $"Physics3D player knowledge capacity {knowledge.RecordCapacity} is below required {requiredKnowledgeCapacity}.",
                nameof(knowledge));
        }

        SeatCapacity = seatCapacity;
        _schemaId = schemaId;
        _shape = physics.RegisterCapsuleShape(bodyConfig.RadiusCm, bodyConfig.CylinderLengthCm);
        _active = new bool[seatCapacity];
        _connected = new bool[seatCapacity];
        _everConnected = new bool[seatCapacity];
        _seatGenerations = new uint[seatCapacity];
        _retiredSeatGenerations = new uint[seatCapacity];
        _playerIds = new int[seatCapacity];
        _entities = new Entity[seatCapacity];
        _bodies = new Physics3DBodyId[seatCapacity];
        _networkHandles = new NetworkEntityHandle[seatCapacity];
    }

    public int SeatCapacity { get; }
    public int ActivePlayerCount { get; private set; }
    public int ConnectedPlayerCount { get; private set; }
    public Physics3DNetworkPlayerLifecycleFailure LastFailure { get; private set; }
    internal KnowledgeProjectionStore Knowledge => _knowledge;

    public bool TryResolveController(in SessionSeatBinding seat, out Entity controller)
    {
        EnsureNotDisposed();
        controller = Entity.Null;
        LastFailure = Physics3DNetworkPlayerLifecycleFailure.None;
        if (!IsSeatShapeValid(in seat))
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.InvalidSeat;
            return false;
        }

        int slot = seat.Slot;
        if (_active[slot])
        {
            if (!Matches(slot, in seat) || !_world.IsAlive(_entities[slot]))
            {
                LastFailure = Physics3DNetworkPlayerLifecycleFailure.GenerationMismatch;
                return false;
            }

            controller = _entities[slot];
            return true;
        }

        if (seat.Generation <= _retiredSeatGenerations[slot])
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.GenerationNotNewer;
            return false;
        }

        Vector3 spawn = _spawnConfig.Resolve(slot);
        var playerIdentity = new PlayerIdentity { PlayerId = seat.PlayerId.Value };
        var player = new Physics3DNetworkPlayer
        {
            SeatSlot = slot,
            SeatGeneration = seat.Generation,
            PlayerId = seat.PlayerId.Value,
        };
        var schema = new ReplicationSchemaRef(_schemaId);
        var body = new Physics3DBodyCm { Kind = Physics3DBodyKind.Dynamic };
        var pose = new Physics3DPoseCm { Position = spawn, Orientation = Quaternion.Identity };
        var previous = new PreviousPhysics3DPoseCm { Position = spawn, Orientation = Quaternion.Identity };
        var replicated = new Physics3DNetworkReplicatedBody
        {
            AuthoritativeKind = Physics3DBodyKind.Dynamic,
        };
        Entity entity = _world.Create(
            in playerIdentity,
            in player,
            in schema,
            in body,
            in pose,
            in previous,
            in replicated);
        Physics3DBodyId bodyId = default;
        try
        {
            Physics3DBodyDescription description = CreateBodyDescription(entity, spawn);
            bodyId = _physics.CreateBody(in description);
        }
        catch (InvalidOperationException)
        {
            _world.Destroy(entity);
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.PhysicsBodyCapacityExceeded;
            return false;
        }

        if (!_networkEntities.TryAllocate(entity, out NetworkEntityHandle handle))
        {
            _physics.DestroyBody(bodyId);
            _world.Destroy(entity);
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.NetworkEntityCapacityExceeded;
            return false;
        }

        body.Id = bodyId;
        replicated.Handle = handle;
        _world.Set(entity, body);
        _world.Set(entity, replicated);
        if (!_bindings.TryBind(bodyId, entity, handle, _schemaId, Physics3DBodyKind.Dynamic))
        {
            if (!_networkEntities.TryRelease(handle))
            {
                throw new InvalidOperationException(
                    "Physics3D player binding rollback could not release its network entity handle.");
            }

            _physics.DestroyBody(bodyId);
            _world.Destroy(entity);
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.BindingStoreRejected;
            return false;
        }

        _active[slot] = true;
        _seatGenerations[slot] = seat.Generation;
        _playerIds[slot] = seat.PlayerId.Value;
        _entities[slot] = entity;
        _bodies[slot] = bodyId;
        _networkHandles[slot] = handle;
        ActivePlayerCount++;
        controller = entity;
        return true;
    }

    public bool TryGetExistingController(in SessionSeatBinding seat, out Entity controller)
    {
        EnsureNotDisposed();
        if (!IsSeatShapeValid(in seat) || !_active[seat.Slot] || !Matches(seat.Slot, in seat) || !_world.IsAlive(_entities[seat.Slot]))
        {
            controller = Entity.Null;
            return false;
        }

        controller = _entities[seat.Slot];
        return true;
    }

    public bool TryGetBody(in SessionSeatBinding seat, out Physics3DBodyId body)
    {
        if (!TryGetExistingController(in seat, out _) || !_physics.ContainsBody(_bodies[seat.Slot]))
        {
            body = default;
            return false;
        }

        body = _bodies[seat.Slot];
        return true;
    }

    public bool TryGetNetworkHandle(in SessionSeatBinding seat, out NetworkEntityHandle handle)
    {
        if (!TryGetExistingController(in seat, out _))
        {
            handle = default;
            return false;
        }

        handle = _networkHandles[seat.Slot];
        return true;
    }

    public bool TryCopyConnectedSeats(Span<SessionSeatBinding> destination, out int count)
    {
        EnsureNotDisposed();
        count = ConnectedPlayerCount;
        if (destination.Length < count)
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.DestinationCapacityExceeded;
            return false;
        }

        int written = 0;
        for (int slot = 0; slot < SeatCapacity; slot++)
        {
            if (!_connected[slot])
            {
                continue;
            }

            destination[written++] = BindingAt(slot);
        }

        count = written;
        LastFailure = Physics3DNetworkPlayerLifecycleFailure.None;
        return true;
    }

    public void OnSeatConnected(in SessionSeatBinding seat, bool reconnected)
    {
        EnsureNotDisposed();
        int slot = RequireExisting(in seat);
        bool expectedReconnect = _everConnected[slot];
        if (_connected[slot] || reconnected != expectedReconnect)
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.SeatConnectionStateMismatch;
            throw new InvalidOperationException($"Physics3D player seat {slot}:{seat.Generation} connection transition is inconsistent.");
        }

        _connected[slot] = true;
        _everConnected[slot] = true;
        ConnectedPlayerCount++;
    }

    public void OnSeatDisconnected(in SessionSeatBinding seat)
    {
        EnsureNotDisposed();
        int slot = RequireExisting(in seat);
        if (!_connected[slot])
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.SeatConnectionStateMismatch;
            throw new InvalidOperationException($"Physics3D player seat {slot}:{seat.Generation} is not connected.");
        }

        _connected[slot] = false;
        ConnectedPlayerCount--;
    }

    public bool TryRelease(in SessionSeatBinding seat)
    {
        EnsureNotDisposed();
        LastFailure = Physics3DNetworkPlayerLifecycleFailure.None;
        if (!IsSeatShapeValid(in seat) || !_active[seat.Slot] || !Matches(seat.Slot, in seat))
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.GenerationMismatch;
            return false;
        }

        int slot = seat.Slot;
        if (_connected[slot])
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.SeatConnectionStateMismatch;
            return false;
        }

        NetworkEntityHandle handle = _networkHandles[slot];
        Physics3DBodyId body = _bodies[slot];
        Entity entity = _entities[slot];
        if (!_networkEntities.TryResolve(handle, out Entity mapped) ||
            mapped != entity ||
            !_bindings.TryResolve(body, out Physics3DNetworkReplicatedBinding binding) ||
            binding.Entity != entity ||
            binding.NetworkHandle != handle)
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.EntityTableMismatch;
            return false;
        }

        if (!_networkEntities.TryRelease(handle))
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.EntityTableMismatch;
            return false;
        }

        if (!_bindings.TryUnbind(body, entity, handle))
        {
            throw new InvalidOperationException(
                $"Validated Physics3D player binding could not be removed: {_bindings.LastFailure}.");
        }

        _physics.DestroyBody(body);
        _world.Destroy(entity);
        _active[slot] = false;
        _everConnected[slot] = false;
        _retiredSeatGenerations[slot] = seat.Generation;
        _seatGenerations[slot] = 0;
        _playerIds[slot] = 0;
        _entities[slot] = Entity.Null;
        _bodies[slot] = default;
        _networkHandles[slot] = default;
        ActivePlayerCount--;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int slot = 0; slot < SeatCapacity; slot++)
        {
            if (!_active[slot])
            {
                continue;
            }

            _connected[slot] = false;
            SessionSeatBinding binding = BindingAt(slot);
            if (!TryRelease(in binding))
            {
                throw new InvalidOperationException($"Physics3D player seat {slot} failed lifecycle disposal: {LastFailure}.");
            }
        }

        _disposed = true;
    }

    private Physics3DBodyDescription CreateBodyDescription(Entity entity, Vector3 position) => new(
        entity,
        Physics3DBodyKind.Dynamic,
        _shape,
        position,
        Quaternion.Identity,
        Vector3.Zero,
        Vector3.Zero,
        _bodyConfig.Mass,
        _bodyConfig.CollisionLayer,
        _bodyConfig.Material,
        _bodyConfig.ContinuousDetection);

    private int RequireExisting(in SessionSeatBinding seat)
    {
        if (!IsSeatShapeValid(in seat) || !_active[seat.Slot] || !Matches(seat.Slot, in seat))
        {
            LastFailure = Physics3DNetworkPlayerLifecycleFailure.GenerationMismatch;
            throw new InvalidOperationException($"Physics3D player seat {seat.Slot}:{seat.Generation} is unavailable.");
        }

        return seat.Slot;
    }

    private bool IsSeatShapeValid(in SessionSeatBinding seat) =>
        seat.IsValid && (uint)seat.Slot < (uint)SeatCapacity;

    private bool Matches(int slot, in SessionSeatBinding seat) =>
        _seatGenerations[slot] == seat.Generation && _playerIds[slot] == seat.PlayerId.Value;

    private SessionSeatBinding BindingAt(int slot) =>
        new(slot, _seatGenerations[slot], new PlayerId(_playerIds[slot]));

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class Physics3DNetworkPlayerLifecycleObserver : INetworkRuntimeObserver
{
    private readonly Physics3DNetworkPlayerLifecycle _lifecycle;

    public Physics3DNetworkPlayerLifecycleObserver(Physics3DNetworkPlayerLifecycle lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public void OnFault(in NetworkRuntimeFault fault) { }

    public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected)
    {
        _lifecycle.OnSeatConnected(in seat, reconnected);
    }

    public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason)
    {
        _lifecycle.OnSeatDisconnected(in seat);
    }

    public void OnServerSeatReleased(in SessionSeatBinding seat)
    {
        if (!_lifecycle.TryRelease(in seat))
        {
            throw new InvalidOperationException($"Physics3D player seat release failed: {_lifecycle.LastFailure}.");
        }
    }

    public void OnClientHandshake(in SessionHandshakeResponse response) { }

    public void OnClientAdmission(in Ludots.Core.Networking.Commands.NetworkCommandAdmissionOutcome outcome) { }

    public void OnClientResyncRequired(in Ludots.Core.Networking.Protocol.NetworkResyncRequired message) { }
}

public sealed class Physics3DClientNetworkRuntimeObserver : INetworkRuntimeObserver
{
    public void OnFault(in NetworkRuntimeFault fault) { }

    public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected) { }

    public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason) { }

    public void OnServerSeatReleased(in SessionSeatBinding seat) { }

    public void OnClientHandshake(in SessionHandshakeResponse response) { }

    public void OnClientAdmission(in Ludots.Core.Networking.Commands.NetworkCommandAdmissionOutcome outcome) { }

    public void OnClientResyncRequired(in Ludots.Core.Networking.Protocol.NetworkResyncRequired message) { }
}
