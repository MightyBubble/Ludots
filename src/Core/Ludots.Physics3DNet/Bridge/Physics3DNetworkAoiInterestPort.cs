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
    PerSeatCapacityExceeded = 7,
    KnowledgeCapacityExceeded = 8,
    SeatOrderInvalid = 9,
    PreparedStateMissing = 10,
    BindingMismatch = 11,
}

public sealed class Physics3DNetworkAoiInterestPort :
    IAuthoritativeReplicationInterestBatchPort,
    IPhysics3DParallelQueryBatch,
    IDisposable
{
    private const byte BatchIdle = 0;
    private const byte BatchPrepared = 1;
    private const byte BatchCommitted = 2;

    private readonly IPhysics3DWorld _physics;
    private readonly NetworkEntityReadPublication _networkPublication;
    private readonly Physics3DNetworkPlayerLifecycle _players;
    private readonly Physics3DNetworkReplicatedBindingReadPublication _bindingPublication;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly float _radiusCm;
    private readonly int _perSeatCapacity;
    private readonly SessionSeatBinding[] _batchSeats;
    private readonly bool[] _preparedActive;
    private readonly uint[] _preparedSeatGenerations;
    private readonly int[] _preparedPlayerIds;
    private readonly Entity[] _preparedViewers;
    private readonly System.Numerics.Vector3[] _preparedCenters;
    private readonly int[] _preparedCounts;
    private readonly bool[] _preparedLaneChanged;
    private readonly int[] _overlapCounts;
    private readonly Physics3DNetworkAoiFailure[] _preparedFailures;
    private readonly PreparedInterestEntry[] _preparedEntries;
    private readonly NetworkEntityHandle[] _preparedHandles;
    private readonly Entity[] _preparedEntities;
    private readonly int[] _trackedCounts;
    private readonly uint[] _trackedSeatGenerations;
    private readonly int[] _trackedPlayerIds;
    private readonly Entity[] _trackedViewers;
    private readonly NetworkEntityHandle[] _trackedHandles;
    private readonly Entity[] _trackedEntities;
    private readonly long[] _workerAllocatedBytes;
    private int _batchSeatCount;
    private int _preparedEnterCount;
    private byte _batchState;
    private bool _disposed;

    public Physics3DNetworkAoiInterestPort(
        IPhysics3DWorld physics,
        NetworkEntityTable networkEntities,
        Physics3DNetworkPlayerLifecycle players,
        Physics3DNetworkReplicatedBindingStore bindings,
        KnowledgeProjectionStore knowledge,
        int replicationEntityCapacityPerSeat,
        Physics3DNetworkAoiConfig config)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        ArgumentNullException.ThrowIfNull(networkEntities);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        ArgumentNullException.ThrowIfNull(bindings);
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
        if (config.GlobalEntityCapacity != networkEntities.Capacity ||
            bindings.NetworkSlotCapacity != networkEntities.Capacity ||
            bindings.BodySlotCapacity != physics.BodySlotCapacity)
        {
            throw new ArgumentException(
                "Physics3D AOI, binding, physics-body, and network-entity capacities must agree.",
                nameof(config));
        }

        int requiredKnowledgeCapacity = checked(players.SeatCapacity * replicationEntityCapacityPerSeat);
        if (knowledge.RecordCapacity < requiredKnowledgeCapacity)
        {
            throw new ArgumentException(
                $"Physics3D AOI knowledge capacity {knowledge.RecordCapacity} is below required {requiredKnowledgeCapacity}.",
                nameof(knowledge));
        }

        SeatCapacity = players.SeatCapacity;
        EntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        _perSeatCapacity = replicationEntityCapacityPerSeat;
        _radiusCm = config.RadiusCm;
        _networkPublication = networkEntities.CreateReadPublication();
        _bindingPublication = bindings.CreateReadPublication();
        _batchSeats = new SessionSeatBinding[SeatCapacity];
        _preparedActive = new bool[SeatCapacity];
        _preparedSeatGenerations = new uint[SeatCapacity];
        _preparedPlayerIds = new int[SeatCapacity];
        _preparedViewers = new Entity[SeatCapacity];
        _preparedCenters = new System.Numerics.Vector3[SeatCapacity];
        _preparedCounts = new int[SeatCapacity];
        _preparedLaneChanged = new bool[SeatCapacity];
        _overlapCounts = new int[SeatCapacity];
        _preparedFailures = new Physics3DNetworkAoiFailure[SeatCapacity];
        int laneCapacity = checked(SeatCapacity * replicationEntityCapacityPerSeat);
        _preparedEntries = new PreparedInterestEntry[laneCapacity];
        _preparedHandles = new NetworkEntityHandle[laneCapacity];
        _preparedEntities = new Entity[laneCapacity];
        _trackedCounts = new int[SeatCapacity];
        _trackedSeatGenerations = new uint[SeatCapacity];
        _trackedPlayerIds = new int[SeatCapacity];
        _trackedViewers = new Entity[SeatCapacity];
        _trackedHandles = new NetworkEntityHandle[laneCapacity];
        _trackedEntities = new Entity[laneCapacity];
        _workerAllocatedBytes = new long[physics.WorkerCount];
    }

    public int SeatCapacity { get; }
    public int EntityCapacityPerSeat { get; }
    public int ItemCount => _batchSeatCount;
    public int OverlapScratchCapacity => _physics.BodySlotCapacity;
    public int LastOverlapHitCount { get; private set; }
    public long LastWorkerAllocatedBytes { get; private set; }
    public Physics3DNetworkAoiFailure LastFailure { get; private set; }
    public int FailedSeatSlot { get; private set; } = -1;

    public bool TryPrepareBatch(ReadOnlySpan<SessionSeatBinding> seats)
    {
        EnsureIdle();
        ResetFailure();
        if (seats.IsEmpty || seats.Length > SeatCapacity || !SeatsAreStrictlyOrdered(seats))
        {
            LastFailure = Physics3DNetworkAoiFailure.SeatOrderInvalid;
            return false;
        }

        _batchSeatCount = seats.Length;
        seats.CopyTo(_batchSeats);
        for (int batchIndex = 0; batchIndex < seats.Length; batchIndex++)
        {
            SessionSeatBinding seat = seats[batchIndex];
            if (!_players.TryGetExistingController(in seat, out Entity viewer))
            {
                return FailPrepare(Physics3DNetworkAoiFailure.UnknownSeat, seat.Slot);
            }

            if (!_players.TryGetBody(in seat, out Physics3DBodyId viewerBody) ||
                !_physics.ContainsBody(viewerBody))
            {
                return FailPrepare(Physics3DNetworkAoiFailure.ViewerBodyUnavailable, seat.Slot);
            }

            int seatSlot = seat.Slot;
            _preparedActive[seatSlot] = true;
            _preparedSeatGenerations[seatSlot] = seat.Generation;
            _preparedPlayerIds[seatSlot] = seat.PlayerId.Value;
            _preparedViewers[seatSlot] = viewer;
            _preparedCenters[seatSlot] = _physics.GetBodyState(viewerBody).PositionCm;
            _preparedCounts[seatSlot] = 0;
            _overlapCounts[seatSlot] = 0;
            _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.None;
        }

        bool networkEntered = false;
        bool bindingsEntered = false;
        try
        {
            _networkPublication.Enter();
            networkEntered = true;
            _bindingPublication.Enter();
            bindingsEntered = true;
            _physics.ExecuteParallelQueries(this);
        }
        catch
        {
            ClearPrepared();
            throw;
        }
        finally
        {
            if (bindingsEntered)
            {
                _bindingPublication.Exit();
            }

            if (networkEntered)
            {
                _networkPublication.Exit();
            }
        }

        _physics.CopyLastParallelQueryWorkerAllocatedBytes(_workerAllocatedBytes);
        for (int worker = 0; worker < _workerAllocatedBytes.Length; worker++)
        {
            LastWorkerAllocatedBytes += _workerAllocatedBytes[worker];
        }

        int exitCount = 0;
        int enterCount = 0;
        for (int batchIndex = 0; batchIndex < seats.Length; batchIndex++)
        {
            int seatSlot = seats[batchIndex].Slot;
            LastOverlapHitCount = Math.Max(LastOverlapHitCount, _overlapCounts[seatSlot]);
            if (_preparedFailures[seatSlot] != Physics3DNetworkAoiFailure.None)
            {
                return FailPrepare(_preparedFailures[seatSlot], seatSlot);
            }

            CountKnowledgeChanges(in seats[batchIndex], ref exitCount, ref enterCount);
        }

        int projectedRecordCount = checked(_knowledge.RecordCount - exitCount + enterCount);
        if (projectedRecordCount < 0 || projectedRecordCount > _knowledge.RecordCapacity)
        {
            return FailPrepare(Physics3DNetworkAoiFailure.KnowledgeCapacityExceeded, -1);
        }

        _preparedEnterCount = enterCount;
        _batchState = BatchPrepared;
        return true;
    }

    public bool TryGetPreparedInterest(
        in SessionSeatBinding seat,
        out ReadOnlySpan<NetworkEntityHandle> handles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        handles = default;
        if ((_batchState != BatchPrepared && _batchState != BatchCommitted) ||
            !seat.IsValid ||
            (uint)seat.Slot >= (uint)_preparedActive.Length ||
            !_preparedActive[seat.Slot] ||
            _preparedSeatGenerations[seat.Slot] != seat.Generation ||
            _preparedPlayerIds[seat.Slot] != seat.PlayerId.Value)
        {
            LastFailure = Physics3DNetworkAoiFailure.PreparedStateMissing;
            FailedSeatSlot = seat.Slot;
            return false;
        }

        int laneStart = checked(seat.Slot * _perSeatCapacity);
        handles = _preparedHandles.AsSpan(laneStart, _preparedCounts[seat.Slot]);
        return true;
    }

    public void CommitPreparedKnowledge()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_batchState != BatchPrepared)
        {
            throw new InvalidOperationException("Physics3D AOI has no prepared batch to commit.");
        }

        for (int batchIndex = 0; batchIndex < _batchSeatCount; batchIndex++)
        {
            SessionSeatBinding seat = _batchSeats[batchIndex];
            if (_preparedLaneChanged[seat.Slot])
            {
                RemoveKnowledgeExits(in seat);
            }
        }

        if (_knowledge.PhysicalRecordCount + _preparedEnterCount > _knowledge.RecordCapacity)
        {
            _knowledge.CompactPreservingCapacity();
        }

        if (_knowledge.PhysicalRecordCount + _preparedEnterCount > _knowledge.RecordCapacity)
        {
            throw new InvalidOperationException(
                "Physics3D AOI knowledge capacity changed after successful batch preparation.");
        }

        for (int batchIndex = 0; batchIndex < _batchSeatCount; batchIndex++)
        {
            SessionSeatBinding seat = _batchSeats[batchIndex];
            if (_preparedLaneChanged[seat.Slot])
            {
                AddKnowledgeEnters(in seat);
            }
        }

        for (int batchIndex = 0; batchIndex < _batchSeatCount; batchIndex++)
        {
            SessionSeatBinding seat = _batchSeats[batchIndex];
            if (_preparedLaneChanged[seat.Slot])
            {
                UpdateTrackedLane(in seat);
            }
        }

        _batchState = BatchCommitted;
    }

    public void CompletePreparedBatch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_batchState != BatchPrepared && _batchState != BatchCommitted)
        {
            throw new InvalidOperationException("Physics3D AOI has no prepared batch to complete or cancel.");
        }

        ClearPrepared();
        _batchState = BatchIdle;
    }

    public void Execute(int itemIndex, Physics3DReadQueryContext context)
    {
        SessionSeatBinding seat = _batchSeats[itemIndex];
        int seatSlot = seat.Slot;
        var filter = new Physics3DQueryFilter(
            LayerMask.All,
            ignoredBody: default,
            includeSensors: true);
        ReadOnlySpan<Physics3DOverlapHit> hits;
        try
        {
            hits = context.OverlapSphere(_preparedCenters[seatSlot], _radiusCm, filter);
        }
        catch (Physics3DCapacityExceededException)
        {
            _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.OverlapScratchCapacityExceeded;
            return;
        }

        _overlapCounts[seatSlot] = hits.Length;
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int selectedCount = 0;
        for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
        {
            ref readonly Physics3DOverlapHit hit = ref hits[hitIndex];
            if (!_bindingPublication.TryResolve(hit.Body, out Physics3DNetworkReplicatedBinding binding))
            {
                continue;
            }

            if (binding.Entity == Entity.Null || binding.Entity != hit.Entity)
            {
                _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.BindingMismatch;
                return;
            }

            NetworkEntityHandle handle = binding.NetworkHandle;
            if (!handle.IsValid)
            {
                _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.InvalidNetworkHandle;
                return;
            }

            if (!_networkPublication.TryResolve(handle, out Entity mapped) || mapped != binding.Entity)
            {
                _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.EntityTableMismatch;
                return;
            }

            if (selectedCount >= _perSeatCapacity)
            {
                _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.PerSeatCapacityExceeded;
                return;
            }

            _preparedEntries[laneStart + selectedCount++] = new PreparedInterestEntry(handle, binding.Entity);
        }

        Span<PreparedInterestEntry> entries = _preparedEntries.AsSpan(laneStart, selectedCount);
        entries.Sort();
        for (int index = 0; index < entries.Length; index++)
        {
            PreparedInterestEntry entry = entries[index];
            if (index > 0 && entry.Handle.Slot == entries[index - 1].Handle.Slot)
            {
                _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.DuplicateNetworkSlot;
                return;
            }

            _preparedHandles[laneStart + index] = entry.Handle;
            _preparedEntities[laneStart + index] = entry.Entity;
        }

        _preparedCounts[seatSlot] = selectedCount;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_batchState != BatchIdle)
        {
            throw new InvalidOperationException("Physics3D AOI cannot dispose with a prepared batch.");
        }

        for (int seatSlot = 0; seatSlot < _trackedCounts.Length; seatSlot++)
        {
            ClearTrackedLane(seatSlot);
        }

        _disposed = true;
    }

    private void CountKnowledgeChanges(
        in SessionSeatBinding seat,
        ref int exitCount,
        ref int enterCount)
    {
        int seatSlot = seat.Slot;
        Entity newViewer = _preparedViewers[seatSlot];
        bool sameViewer = TrackedViewerMatches(in seat, newViewer);
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int oldCount = _trackedCounts[seatSlot];
        int newCount = _preparedCounts[seatSlot];
        bool laneChanged = !sameViewer || oldCount != newCount;
        if (!laneChanged)
        {
            for (int index = 0; index < newCount; index++)
            {
                if (_trackedHandles[laneStart + index] != _preparedHandles[laneStart + index] ||
                    _trackedEntities[laneStart + index] != _preparedEntities[laneStart + index])
                {
                    laneChanged = true;
                    break;
                }
            }
        }

        _preparedLaneChanged[seatSlot] = laneChanged;
        if (!laneChanged)
        {
            return;
        }

        if (!sameViewer)
        {
            Entity oldViewer = _trackedViewers[seatSlot];
            for (int index = 0; index < oldCount; index++)
            {
                Entity exiting = _trackedEntities[laneStart + index];
                if (_knowledge.TryGet(oldViewer, exiting, currentTick: 0, out _))
                {
                    exitCount++;
                }
            }

            for (int index = 0; index < newCount; index++)
            {
                Entity entering = _preparedEntities[laneStart + index];
                if (!_knowledge.TryGet(newViewer, entering, currentTick: 0, out _))
                {
                    enterCount++;
                }
            }

            return;
        }

        int oldIndex = 0;
        int newIndex = 0;
        while (oldIndex < oldCount || newIndex < newCount)
        {
            if (oldIndex >= oldCount)
            {
                Entity entering = _preparedEntities[laneStart + newIndex++];
                if (!_knowledge.TryGet(newViewer, entering, currentTick: 0, out _))
                {
                    enterCount++;
                }

                continue;
            }

            if (newIndex >= newCount)
            {
                Entity exiting = _trackedEntities[laneStart + oldIndex++];
                if (_knowledge.TryGet(_trackedViewers[seatSlot], exiting, currentTick: 0, out _))
                {
                    exitCount++;
                }

                continue;
            }

            NetworkEntityHandle oldHandle = _trackedHandles[laneStart + oldIndex];
            NetworkEntityHandle newHandle = _preparedHandles[laneStart + newIndex];
            int comparison = Compare(oldHandle, newHandle);
            if (comparison == 0 &&
                _trackedEntities[laneStart + oldIndex] == _preparedEntities[laneStart + newIndex])
            {
                oldIndex++;
                newIndex++;
            }
            else if (comparison <= 0)
            {
                Entity exiting = _trackedEntities[laneStart + oldIndex++];
                if (_knowledge.TryGet(_trackedViewers[seatSlot], exiting, currentTick: 0, out _))
                {
                    exitCount++;
                }
            }
            else
            {
                Entity entering = _preparedEntities[laneStart + newIndex++];
                if (!_knowledge.TryGet(newViewer, entering, currentTick: 0, out _))
                {
                    enterCount++;
                }
            }
        }

    }

    private void RemoveKnowledgeExits(in SessionSeatBinding seat)
    {
        int seatSlot = seat.Slot;
        Entity oldViewer = _trackedViewers[seatSlot];
        bool sameViewer = TrackedViewerMatches(in seat, _preparedViewers[seatSlot]);
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int oldCount = _trackedCounts[seatSlot];
        int newCount = _preparedCounts[seatSlot];
        int newIndex = 0;
        for (int oldIndex = 0; oldIndex < oldCount; oldIndex++)
        {
            NetworkEntityHandle oldHandle = _trackedHandles[laneStart + oldIndex];
            while (sameViewer &&
                   newIndex < newCount &&
                   Compare(_preparedHandles[laneStart + newIndex], oldHandle) < 0)
            {
                newIndex++;
            }

            bool retained = sameViewer &&
                newIndex < newCount &&
                _preparedHandles[laneStart + newIndex] == oldHandle &&
                _preparedEntities[laneStart + newIndex] == _trackedEntities[laneStart + oldIndex];
            if (!retained && oldViewer != Entity.Null)
            {
                _knowledge.Remove(oldViewer, _trackedEntities[laneStart + oldIndex]);
            }
        }
    }

    private void AddKnowledgeEnters(in SessionSeatBinding seat)
    {
        int seatSlot = seat.Slot;
        Entity viewer = _preparedViewers[seatSlot];
        bool sameViewer = TrackedViewerMatches(in seat, viewer);
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int oldCount = _trackedCounts[seatSlot];
        int oldIndex = 0;
        KnowledgeDisclosureRecord disclosure = LiveDisclosure(viewer);
        for (int newIndex = 0; newIndex < _preparedCounts[seatSlot]; newIndex++)
        {
            NetworkEntityHandle newHandle = _preparedHandles[laneStart + newIndex];
            while (sameViewer &&
                   oldIndex < oldCount &&
                   Compare(_trackedHandles[laneStart + oldIndex], newHandle) < 0)
            {
                oldIndex++;
            }

            Entity target = _preparedEntities[laneStart + newIndex];
            bool retained = sameViewer &&
                oldIndex < oldCount &&
                _trackedHandles[laneStart + oldIndex] == newHandle &&
                _trackedEntities[laneStart + oldIndex] == target;
            if (!retained && !_knowledge.TryGet(viewer, target, currentTick: 0, out _))
            {
                _knowledge.Upsert(viewer, target, in disclosure);
            }
        }
    }

    private void UpdateTrackedLane(in SessionSeatBinding seat)
    {
        int seatSlot = seat.Slot;
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int oldCount = _trackedCounts[seatSlot];
        int newCount = _preparedCounts[seatSlot];
        _preparedHandles.AsSpan(laneStart, newCount).CopyTo(_trackedHandles.AsSpan(laneStart, newCount));
        _preparedEntities.AsSpan(laneStart, newCount).CopyTo(_trackedEntities.AsSpan(laneStart, newCount));
        if (oldCount > newCount)
        {
            _trackedHandles.AsSpan(laneStart + newCount, oldCount - newCount).Clear();
            _trackedEntities.AsSpan(laneStart + newCount, oldCount - newCount).Clear();
        }

        _trackedCounts[seatSlot] = newCount;
        _trackedSeatGenerations[seatSlot] = seat.Generation;
        _trackedPlayerIds[seatSlot] = seat.PlayerId.Value;
        _trackedViewers[seatSlot] = _preparedViewers[seatSlot];
    }

    private void ClearTrackedLane(int seatSlot)
    {
        int laneStart = checked(seatSlot * _perSeatCapacity);
        Entity viewer = _trackedViewers[seatSlot];
        int count = _trackedCounts[seatSlot];
        for (int index = 0; index < count; index++)
        {
            if (viewer != Entity.Null)
            {
                _knowledge.Remove(viewer, _trackedEntities[laneStart + index]);
            }
        }

        _trackedHandles.AsSpan(laneStart, count).Clear();
        _trackedEntities.AsSpan(laneStart, count).Clear();
        _trackedCounts[seatSlot] = 0;
        _trackedSeatGenerations[seatSlot] = 0;
        _trackedPlayerIds[seatSlot] = 0;
        _trackedViewers[seatSlot] = Entity.Null;
    }

    private bool TrackedViewerMatches(in SessionSeatBinding seat, Entity viewer) =>
        _trackedSeatGenerations[seat.Slot] != 0 &&
        _trackedSeatGenerations[seat.Slot] == seat.Generation &&
        _trackedPlayerIds[seat.Slot] == seat.PlayerId.Value &&
        _trackedViewers[seat.Slot] == viewer;

    private bool FailPrepare(Physics3DNetworkAoiFailure failure, int seatSlot)
    {
        LastFailure = failure;
        FailedSeatSlot = seatSlot;
        ClearPrepared();
        return false;
    }

    private void ClearPrepared()
    {
        for (int batchIndex = 0; batchIndex < _batchSeatCount; batchIndex++)
        {
            int seatSlot = _batchSeats[batchIndex].Slot;
            _preparedActive[seatSlot] = false;
            _preparedSeatGenerations[seatSlot] = 0;
            _preparedPlayerIds[seatSlot] = 0;
            _preparedViewers[seatSlot] = Entity.Null;
            _preparedCenters[seatSlot] = default;
            _preparedCounts[seatSlot] = 0;
            _preparedLaneChanged[seatSlot] = false;
            _overlapCounts[seatSlot] = 0;
            _preparedFailures[seatSlot] = Physics3DNetworkAoiFailure.None;
            _batchSeats[batchIndex] = default;
        }

        _batchSeatCount = 0;
        _preparedEnterCount = 0;
    }

    private void ResetFailure()
    {
        LastFailure = Physics3DNetworkAoiFailure.None;
        FailedSeatSlot = -1;
        LastOverlapHitCount = 0;
        LastWorkerAllocatedBytes = 0;
    }

    private void EnsureIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_batchState != BatchIdle)
        {
            throw new InvalidOperationException("Physics3D AOI already owns a prepared batch.");
        }
    }

    private bool SeatsAreStrictlyOrdered(ReadOnlySpan<SessionSeatBinding> seats)
    {
        int previousSlot = -1;
        for (int index = 0; index < seats.Length; index++)
        {
            SessionSeatBinding seat = seats[index];
            if (!seat.IsValid ||
                (uint)seat.Slot >= (uint)SeatCapacity ||
                seat.Slot <= previousSlot)
            {
                return false;
            }

            previousSlot = seat.Slot;
        }

        return true;
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

    private readonly struct PreparedInterestEntry : IComparable<PreparedInterestEntry>
    {
        public PreparedInterestEntry(NetworkEntityHandle handle, Entity entity)
        {
            Handle = handle;
            Entity = entity;
        }

        public NetworkEntityHandle Handle { get; }
        public Entity Entity { get; }

        public int CompareTo(PreparedInterestEntry other) => Compare(Handle, other.Handle);
    }
}
