using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkAoiFailure : byte
{
    None = 0,
    UnknownSeat = 1,
    ViewerPoseUnavailable = 2,
    InvalidNetworkHandle = 3,
    EntityTableMismatch = 4,
    DuplicateNetworkSlot = 5,
    DestinationCapacityExceeded = 6,
    PerSeatCapacityExceeded = 7,
    KnowledgeCapacityExceeded = 8,
}

public sealed class Physics3DNetworkAoiInterestPort : IAuthoritativeReplicationInterestPort, IDisposable
{
    private static readonly QueryDescription ReplicatedBodyQuery = new QueryDescription()
        .WithAll<Physics3DNetworkReplicatedBody, Physics3DPoseCm>();

    private readonly World _world;
    private readonly NetworkEntityTable _networkEntities;
    private readonly Physics3DNetworkPlayerLifecycle _players;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly float _radiusSquared;
    private readonly int _perSeatCapacity;
    private readonly int[] _selectionStamps;
    private readonly uint[] _selectionGenerations;
    private readonly Entity[] _selectionEntities;
    private readonly int[] _selectedSlots;
    private readonly int[] _trackedCounts;
    private readonly uint[] _trackedSeatGenerations;
    private readonly int[] _trackedPlayerIds;
    private readonly Entity[] _trackedViewers;
    private readonly NetworkEntityHandle[] _trackedHandles;
    private readonly Entity[] _trackedEntities;
    private int _queryStamp;
    private bool _disposed;

    public Physics3DNetworkAoiInterestPort(
        World world,
        NetworkEntityTable networkEntities,
        Physics3DNetworkPlayerLifecycle players,
        Physics3DNetworkAoiConfig config)
        : this(
            world,
            networkEntities,
            players,
            RequirePlayerKnowledge(players),
            ResolveCompatibilityPerSeatCapacity(networkEntities, players),
            config)
    {
    }

    public Physics3DNetworkAoiInterestPort(
        World world,
        NetworkEntityTable networkEntities,
        Physics3DNetworkPlayerLifecycle players,
        KnowledgeProjectionStore knowledge,
        int replicationEntityCapacityPerSeat,
        Physics3DNetworkAoiConfig config)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _networkEntities = networkEntities ?? throw new ArgumentNullException(nameof(networkEntities));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        if (!ReferenceEquals(players.Knowledge, knowledge))
        {
            throw new ArgumentException(
                "Physics3D AOI and player lifecycle must share the authoritative knowledge store.",
                nameof(knowledge));
        }

        if (replicationEntityCapacityPerSeat <= 0 ||
            replicationEntityCapacityPerSeat > networkEntities.Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(replicationEntityCapacityPerSeat));
        }

        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (config.GlobalEntityCapacity != networkEntities.Capacity)
        {
            throw new ArgumentException(
                "Physics3D AOI global capacity must match the Core network entity table.",
                nameof(config));
        }

        int requiredKnowledgeCapacity = checked(players.SeatCapacity * replicationEntityCapacityPerSeat);
        if (knowledge.RecordCapacity < requiredKnowledgeCapacity)
        {
            throw new ArgumentException(
                $"Physics3D AOI knowledge capacity {knowledge.RecordCapacity} is below required {requiredKnowledgeCapacity}.",
                nameof(knowledge));
        }

        _radiusSquared = config.RadiusCm * config.RadiusCm;
        _perSeatCapacity = replicationEntityCapacityPerSeat;
        _selectionStamps = new int[config.GlobalEntityCapacity];
        _selectionGenerations = new uint[config.GlobalEntityCapacity];
        _selectionEntities = new Entity[config.GlobalEntityCapacity];
        _selectedSlots = new int[config.GlobalEntityCapacity];
        _trackedCounts = new int[players.SeatCapacity];
        _trackedSeatGenerations = new uint[players.SeatCapacity];
        _trackedPlayerIds = new int[players.SeatCapacity];
        _trackedViewers = new Entity[players.SeatCapacity];
        int trackedCapacity = checked(players.SeatCapacity * replicationEntityCapacityPerSeat);
        _trackedHandles = new NetworkEntityHandle[trackedCapacity];
        _trackedEntities = new Entity[trackedCapacity];
    }

    public Physics3DNetworkAoiFailure LastFailure { get; private set; }
    public int PerSeatCapacity => _perSeatCapacity;

    public bool TryCopyInterest(
        in SessionSeatBinding seat,
        Span<NetworkEntityHandle> destination,
        out int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastFailure = Physics3DNetworkAoiFailure.None;
        count = 0;
        if (!_players.TryGetExistingController(in seat, out Entity viewer))
        {
            LastFailure = Physics3DNetworkAoiFailure.UnknownSeat;
            return false;
        }

        if (!_world.TryGet(viewer, out Physics3DPoseCm viewerPose))
        {
            LastFailure = Physics3DNetworkAoiFailure.ViewerPoseUnavailable;
            return false;
        }

        int seatSlot = seat.Slot;
        if (_trackedSeatGenerations[seatSlot] != 0 &&
            (_trackedSeatGenerations[seatSlot] != seat.Generation ||
             _trackedPlayerIds[seatSlot] != seat.PlayerId.Value ||
             _trackedViewers[seatSlot] != viewer))
        {
            ClearTrackedLane(seatSlot);
        }

        int stamp = NextQueryStamp();
        int selectedCount = 0;
        foreach (ref Chunk chunk in _world.Query(in ReplicatedBodyQuery))
        {
            chunk.GetSpan<Physics3DNetworkReplicatedBody, Physics3DPoseCm>(
                out Span<Physics3DNetworkReplicatedBody> replicated,
                out Span<Physics3DPoseCm> poses);
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (Vector3.DistanceSquared(viewerPose.Position, poses[index].Position) > _radiusSquared)
                {
                    continue;
                }

                NetworkEntityHandle handle = replicated[index].Handle;
                if (!handle.IsValid || (uint)handle.Slot >= (uint)_selectionStamps.Length)
                {
                    LastFailure = Physics3DNetworkAoiFailure.InvalidNetworkHandle;
                    return false;
                }

                Entity entity = Unsafe.Add(ref first, index);
                if (!_networkEntities.TryResolve(handle, out Entity mapped) || mapped != entity)
                {
                    LastFailure = Physics3DNetworkAoiFailure.EntityTableMismatch;
                    return false;
                }

                int slot = handle.Slot;
                if (_selectionStamps[slot] == stamp)
                {
                    LastFailure = Physics3DNetworkAoiFailure.DuplicateNetworkSlot;
                    return false;
                }

                _selectionStamps[slot] = stamp;
                _selectionGenerations[slot] = handle.Generation;
                _selectionEntities[slot] = entity;
                _selectedSlots[selectedCount++] = slot;
            }
        }

        count = selectedCount;
        if (selectedCount > _perSeatCapacity)
        {
            LastFailure = Physics3DNetworkAoiFailure.PerSeatCapacityExceeded;
            return false;
        }

        if (destination.Length < selectedCount)
        {
            LastFailure = Physics3DNetworkAoiFailure.DestinationCapacityExceeded;
            return false;
        }

        Span<int> selectedSlots = _selectedSlots.AsSpan(0, selectedCount);
        selectedSlots.Sort();
        for (int index = 0; index < selectedSlots.Length; index++)
        {
            int slot = selectedSlots[index];
            destination[index] = new NetworkEntityHandle(slot, _selectionGenerations[slot]);
        }

        return TryUpdateKnowledgeLane(in seat, viewer, destination[..selectedCount]);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int seatSlot = 0; seatSlot < _trackedCounts.Length; seatSlot++)
        {
            ClearTrackedLane(seatSlot);
        }

        _disposed = true;
    }

    private bool TryUpdateKnowledgeLane(
        in SessionSeatBinding seat,
        Entity viewer,
        ReadOnlySpan<NetworkEntityHandle> currentHandles)
    {
        int seatSlot = seat.Slot;
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int oldCount = _trackedCounts[seatSlot];
        int oldIndex = 0;
        int currentIndex = 0;
        int exitCount = 0;
        int enterCount = 0;
        while (oldIndex < oldCount || currentIndex < currentHandles.Length)
        {
            if (oldIndex >= oldCount)
            {
                Entity entering = EntityFor(currentHandles[currentIndex++]);
                if (!_knowledge.TryGet(viewer, entering, currentTick: 0, out _))
                {
                    enterCount++;
                }

                continue;
            }

            if (currentIndex >= currentHandles.Length)
            {
                Entity exiting = _trackedEntities[laneStart + oldIndex++];
                if (_knowledge.TryGet(viewer, exiting, currentTick: 0, out _))
                {
                    exitCount++;
                }

                continue;
            }

            NetworkEntityHandle oldHandle = _trackedHandles[laneStart + oldIndex];
            NetworkEntityHandle currentHandle = currentHandles[currentIndex];
            int comparison = Compare(oldHandle, currentHandle);
            if (comparison == 0)
            {
                oldIndex++;
                currentIndex++;
            }
            else if (comparison < 0)
            {
                Entity exiting = _trackedEntities[laneStart + oldIndex++];
                if (_knowledge.TryGet(viewer, exiting, currentTick: 0, out _))
                {
                    exitCount++;
                }
            }
            else
            {
                Entity entering = EntityFor(currentHandles[currentIndex++]);
                if (!_knowledge.TryGet(viewer, entering, currentTick: 0, out _))
                {
                    enterCount++;
                }
            }
        }

        int projectedRecordCount = checked(_knowledge.RecordCount - exitCount + enterCount);
        if (projectedRecordCount > _knowledge.RecordCapacity)
        {
            LastFailure = Physics3DNetworkAoiFailure.KnowledgeCapacityExceeded;
            return false;
        }

        oldIndex = 0;
        currentIndex = 0;
        while (oldIndex < oldCount)
        {
            NetworkEntityHandle oldHandle = _trackedHandles[laneStart + oldIndex];
            while (currentIndex < currentHandles.Length &&
                   Compare(currentHandles[currentIndex], oldHandle) < 0)
            {
                currentIndex++;
            }

            if (currentIndex >= currentHandles.Length || currentHandles[currentIndex] != oldHandle)
            {
                _knowledge.Remove(viewer, _trackedEntities[laneStart + oldIndex]);
            }

            oldIndex++;
        }

        if (_knowledge.PhysicalRecordCount + enterCount > _knowledge.RecordCapacity)
        {
            _knowledge.CompactPreservingCapacity();
        }

        if (_knowledge.PhysicalRecordCount + enterCount > _knowledge.RecordCapacity)
        {
            throw new InvalidOperationException(
                "Physics3D AOI knowledge physical capacity remained insufficient after in-place compaction.");
        }

        KnowledgeDisclosureRecord disclosure = LiveDisclosure(viewer);
        for (int index = 0; index < currentHandles.Length; index++)
        {
            NetworkEntityHandle handle = currentHandles[index];
            Entity target = EntityFor(handle);
            _knowledge.Upsert(viewer, target, in disclosure);
            _trackedHandles[laneStart + index] = handle;
            _trackedEntities[laneStart + index] = target;
        }

        for (int index = currentHandles.Length; index < oldCount; index++)
        {
            _trackedHandles[laneStart + index] = default;
            _trackedEntities[laneStart + index] = Entity.Null;
        }

        _trackedCounts[seatSlot] = currentHandles.Length;
        _trackedSeatGenerations[seatSlot] = seat.Generation;
        _trackedPlayerIds[seatSlot] = seat.PlayerId.Value;
        _trackedViewers[seatSlot] = viewer;
        return true;
    }

    private Entity EntityFor(NetworkEntityHandle handle)
    {
        Entity entity = _selectionEntities[handle.Slot];
        if (entity == Entity.Null || _selectionGenerations[handle.Slot] != handle.Generation)
        {
            throw new InvalidOperationException(
                $"Physics3D AOI selection scratch lost network handle '{handle}'.");
        }

        return entity;
    }

    private void ClearTrackedLane(int seatSlot)
    {
        int count = _trackedCounts[seatSlot];
        int laneStart = checked(seatSlot * _perSeatCapacity);
        Entity viewer = _trackedViewers[seatSlot];
        for (int index = 0; index < count; index++)
        {
            Entity target = _trackedEntities[laneStart + index];
            if (viewer != Entity.Null && target != Entity.Null)
            {
                _knowledge.Remove(viewer, target);
            }

            _trackedHandles[laneStart + index] = default;
            _trackedEntities[laneStart + index] = Entity.Null;
        }

        _trackedCounts[seatSlot] = 0;
        _trackedSeatGenerations[seatSlot] = 0;
        _trackedPlayerIds[seatSlot] = 0;
        _trackedViewers[seatSlot] = Entity.Null;
    }

    private int NextQueryStamp()
    {
        if (_queryStamp == int.MaxValue)
        {
            Array.Clear(_selectionStamps);
            _queryStamp = 0;
        }

        return ++_queryStamp;
    }

    private static KnowledgeDisclosureRecord LiveDisclosure(Entity viewer) => new(
        KnowledgePresence.LiveVisible,
        KnowledgePositionAccess.Live,
        default,
        default,
        default,
        viewer,
        observedTick: 0,
        expiryTick: 0,
        confidencePermille: 1000,
        revision: 0);

    private static int Compare(NetworkEntityHandle left, NetworkEntityHandle right)
    {
        int slotComparison = left.Slot.CompareTo(right.Slot);
        return slotComparison != 0
            ? slotComparison
            : left.Generation.CompareTo(right.Generation);
    }

    private static KnowledgeProjectionStore RequirePlayerKnowledge(Physics3DNetworkPlayerLifecycle players)
    {
        ArgumentNullException.ThrowIfNull(players);
        return players.Knowledge;
    }

    private static int ResolveCompatibilityPerSeatCapacity(
        NetworkEntityTable networkEntities,
        Physics3DNetworkPlayerLifecycle players)
    {
        ArgumentNullException.ThrowIfNull(networkEntities);
        ArgumentNullException.ThrowIfNull(players);
        int reservedPerSeat = players.Knowledge.RecordCapacity / players.SeatCapacity;
        return Math.Min(networkEntities.Capacity, Math.Max(1, reservedPerSeat));
    }
}
