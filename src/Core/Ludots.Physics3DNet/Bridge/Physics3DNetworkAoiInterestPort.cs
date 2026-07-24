using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Layers;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public enum Physics3DNetworkAoiFailure : byte
{
    None = 0,
    UnknownSeat = 1,
    ViewerBodyUnavailable = 2,
    OverlapScratchCapacityExceeded = 3,
    InvalidNetworkHandle = 4,
    EntityTableMismatch = 5,
    DuplicateNetworkSlot = 6,
    DestinationCapacityExceeded = 7,
    PerSeatCapacityExceeded = 8,
    KnowledgeCapacityExceeded = 9,
}

public sealed class Physics3DNetworkAoiInterestPort : IAuthoritativeReplicationInterestPort, IDisposable
{
    private readonly World _world;
    private readonly IPhysics3DWorld _physics;
    private readonly NetworkEntityTable _networkEntities;
    private readonly Physics3DNetworkPlayerLifecycle _players;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly float _radiusCm;
    private readonly int _perSeatCapacity;
    private readonly Physics3DOverlapHit[] _overlapHits;
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
        IPhysics3DWorld physics,
        NetworkEntityTable networkEntities,
        Physics3DNetworkPlayerLifecycle players,
        KnowledgeProjectionStore knowledge,
        int replicationEntityCapacityPerSeat,
        Physics3DNetworkAoiConfig config)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
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

        _radiusCm = config.RadiusCm;
        _perSeatCapacity = replicationEntityCapacityPerSeat;
        _overlapHits = new Physics3DOverlapHit[config.GlobalEntityCapacity];
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
    public int OverlapScratchCapacity => _overlapHits.Length;
    public int LastOverlapHitCount { get; private set; }

    public bool TryCopyInterest(
        in SessionSeatBinding seat,
        Span<NetworkEntityHandle> destination,
        out int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastFailure = Physics3DNetworkAoiFailure.None;
        LastOverlapHitCount = 0;
        count = 0;
        if (!_players.TryGetExistingController(in seat, out Entity viewer))
        {
            LastFailure = Physics3DNetworkAoiFailure.UnknownSeat;
            return false;
        }

        if (!_players.TryGetBody(in seat, out Physics3DBodyId viewerBody))
        {
            LastFailure = Physics3DNetworkAoiFailure.ViewerBodyUnavailable;
            return false;
        }

        Physics3DBodyState viewerState = _physics.GetBodyState(viewerBody);

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
        int overlapCount;
        try
        {
            var filter = new Physics3DQueryFilter(
                LayerMask.All,
                ignoredBody: default,
                includeSensors: true);
            overlapCount = _physics.OverlapSphere(
                viewerState.PositionCm,
                _radiusCm,
                in filter,
                _overlapHits);
        }
        catch (Physics3DCapacityExceededException)
        {
            LastFailure = Physics3DNetworkAoiFailure.OverlapScratchCapacityExceeded;
            return false;
        }

        LastOverlapHitCount = overlapCount;
        for (int index = 0; index < overlapCount; index++)
        {
            ref readonly Physics3DOverlapHit hit = ref _overlapHits[index];
            Entity entity = hit.Entity;
            if (entity == Entity.Null)
            {
                continue;
            }

            if (!_world.TryGet(entity, out Physics3DNetworkReplicatedBody replicated))
            {
                continue;
            }

            NetworkEntityHandle handle = replicated.Handle;
            if (!handle.IsValid || (uint)handle.Slot >= (uint)_selectionStamps.Length)
            {
                LastFailure = Physics3DNetworkAoiFailure.InvalidNetworkHandle;
                return false;
            }

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
        if (oldCount == currentHandles.Length &&
            _trackedHandles.AsSpan(laneStart, oldCount).SequenceEqual(currentHandles))
        {
            return true;
        }

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
        oldIndex = 0;
        for (int index = 0; index < currentHandles.Length; index++)
        {
            NetworkEntityHandle handle = currentHandles[index];
            Entity target = EntityFor(handle);
            while (oldIndex < oldCount &&
                   Compare(_trackedHandles[laneStart + oldIndex], handle) < 0)
            {
                oldIndex++;
            }

            bool retained = oldIndex < oldCount &&
                _trackedHandles[laneStart + oldIndex] == handle;
            if (!retained)
            {
                _knowledge.Upsert(viewer, target, in disclosure);
            }
            else
            {
                oldIndex++;
            }

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

}
