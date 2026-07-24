using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DReplicationSeatFactoryFailure : byte
{
    None = 0,
    InvalidSeat = 1,
    ViewerUnavailable = 2,
    SeatAlreadyLeased = 3,
    GenerationNotNewer = 4,
    LeaseMismatch = 5,
    ProjectorRegistryNotFrozen = 6,
}

public sealed class Physics3DAuthoritativeReplicationSeatRuntimeFactory : IAuthoritativeReplicationSeatRuntimeFactory
{
    private readonly World _world;
    private readonly NetworkEntityTable _entities;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly ReplicationSchemaProjectorRegistry _projectors;
    private readonly int _baselineCapacity;
    private readonly int _disclosureChangeLogCapacity;
    private readonly AuthoritativeReplicationSeatRuntime?[] _leased;
    private readonly uint[] _leasedGenerations;
    private readonly int[] _leasedPlayers;
    private readonly Entity[] _leasedViewers;
    private readonly uint[] _retiredGenerations;

    public Physics3DAuthoritativeReplicationSeatRuntimeFactory(
        World world,
        NetworkEntityTable entities,
        KnowledgeProjectionStore knowledge,
        ReplicationSchemaProjectorRegistry projectors,
        int seatCapacity,
        int replicationEntityCapacityPerSeat,
        int baselineCapacity,
        int disclosureChangeLogCapacity)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        _projectors = projectors ?? throw new ArgumentNullException(nameof(projectors));
        if (seatCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seatCapacity));
        }

        if (replicationEntityCapacityPerSeat <= 0 || replicationEntityCapacityPerSeat > entities.Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(replicationEntityCapacityPerSeat));
        }

        if (baselineCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baselineCapacity));
        }

        if (disclosureChangeLogCapacity < checked(replicationEntityCapacityPerSeat * 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(disclosureChangeLogCapacity),
                "Disclosure log must hold one maximum conceal plus reveal transition.");
        }

        SeatCapacity = seatCapacity;
        GlobalEntityCapacity = entities.Capacity;
        ReplicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        _baselineCapacity = baselineCapacity;
        _disclosureChangeLogCapacity = disclosureChangeLogCapacity;
        _leased = new AuthoritativeReplicationSeatRuntime[seatCapacity];
        _leasedGenerations = new uint[seatCapacity];
        _leasedPlayers = new int[seatCapacity];
        _leasedViewers = new Entity[seatCapacity];
        _retiredGenerations = new uint[seatCapacity];
    }

    public int SeatCapacity { get; }
    public int GlobalEntityCapacity { get; }
    public int ReplicationEntityCapacityPerSeat { get; }
    public Physics3DReplicationSeatFactoryFailure LastFailure { get; private set; }

    public bool TryAcquire(
        in SessionSeatBinding seat,
        Entity viewer,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AuthoritativeReplicationSeatRuntime? runtime)
    {
        runtime = null;
        LastFailure = Physics3DReplicationSeatFactoryFailure.None;
        if (!_projectors.IsFrozen)
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.ProjectorRegistryNotFrozen;
            return false;
        }

        if (!seat.IsValid || (uint)seat.Slot >= (uint)SeatCapacity)
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.InvalidSeat;
            return false;
        }

        int slot = seat.Slot;
        if (viewer == Entity.Null || !_world.IsAlive(viewer))
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.ViewerUnavailable;
            return false;
        }

        if (_leased[slot] != null)
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.SeatAlreadyLeased;
            return false;
        }

        if (seat.Generation <= _retiredGenerations[slot])
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.GenerationNotNewer;
            return false;
        }

        var disclosureLog = new ReplicationDisclosureChangeLog(_disclosureChangeLogCapacity);
        runtime = new AuthoritativeReplicationSeatRuntime(
            new AuthoritativeWorldReplicationBridge(
                _world,
                _entities,
                _knowledge,
                viewer,
                _projectors,
                ReplicationEntityCapacityPerSeat),
            new AuthoritativeReplicationChannel(
                _entities,
                ReplicationEntityCapacityPerSeat,
                _baselineCapacity,
                disclosureLog),
            new ReplicationProjectionBuffer(ReplicationEntityCapacityPerSeat),
            new ReplicationPacketBuffer(ReplicationEntityCapacityPerSeat));
        _leased[slot] = runtime;
        _leasedGenerations[slot] = seat.Generation;
        _leasedPlayers[slot] = seat.PlayerId.Value;
        _leasedViewers[slot] = viewer;
        return true;
    }

    public bool TryRelease(in SessionSeatBinding seat, AuthoritativeReplicationSeatRuntime runtime)
    {
        LastFailure = Physics3DReplicationSeatFactoryFailure.None;
        if (!seat.IsValid || (uint)seat.Slot >= (uint)SeatCapacity)
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.InvalidSeat;
            return false;
        }

        ArgumentNullException.ThrowIfNull(runtime);
        int slot = seat.Slot;
        if (!ReferenceEquals(_leased[slot], runtime) ||
            _leasedGenerations[slot] != seat.Generation ||
            _leasedPlayers[slot] != seat.PlayerId.Value)
        {
            LastFailure = Physics3DReplicationSeatFactoryFailure.LeaseMismatch;
            return false;
        }

        _leased[slot] = null;
        _leasedGenerations[slot] = 0;
        _leasedPlayers[slot] = 0;
        _leasedViewers[slot] = Entity.Null;
        _retiredGenerations[slot] = seat.Generation;
        return true;
    }
}
