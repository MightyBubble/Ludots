using System.Threading;
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
    ConnectedSeatOrderInvalid = 10,
    DuplicateConnectedSeat = 11,
    PreparedStateMissing = 12,
    BindingMismatch = 13,
    WorkerFailure = 14,
}

public sealed class Physics3DNetworkAoiInterestPort : IAuthoritativeReplicationInterestBatchPort, IDisposable
{
    private readonly IPhysics3DWorld _physics;
    private readonly NetworkEntityTable _networkEntities;
    private readonly Physics3DNetworkPlayerLifecycle _players;
    private readonly Physics3DNetworkReplicatedBindingStore _bindings;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly float _radiusCm;
    private readonly int _perSeatCapacity;
    private readonly int _workerCount;
    private readonly Action<int> _prepareWorker;
    private readonly Physics3DOverlapHit[] _overlapHits;
    private readonly int[] _selectionStamps;
    private readonly uint[] _selectionGenerations;
    private readonly Entity[] _selectionEntities;
    private readonly int[] _selectedSlots;
    private readonly int[] _workerQueryStamps;
    private readonly NetworkEntityHandle[] _preparedHandles;
    private readonly Entity[] _preparedEntities;
    private readonly int[] _preparedCounts;
    private readonly uint[] _preparedSeatGenerations;
    private readonly int[] _preparedPlayerIds;
    private readonly Entity[] _preparedViewers;
    private readonly bool[] _preparedActive;
    private readonly int[] _trackedCounts;
    private readonly uint[] _trackedSeatGenerations;
    private readonly int[] _trackedPlayerIds;
    private readonly Entity[] _trackedViewers;
    private readonly NetworkEntityHandle[] _trackedHandles;
    private readonly Entity[] _trackedEntities;
    private readonly long[] _workerAllocatedBytes;
    private SessionSeatBinding[] _batchSeats;
    private int _batchSeatCount;
    private bool _preparedReady;
    private bool _committed;
    private int _failedSeatSlot;
    private int _workerFailureCode;
    private int _lastOverlapHitCount;
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
        _networkEntities = networkEntities ?? throw new ArgumentNullException(nameof(networkEntities));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        if (!ReferenceEquals(players.Knowledge, knowledge))
        {
            throw new ArgumentException(
                "Physics3D AOI and player lifecycle must share the authoritative knowledge store.",
                nameof(knowledge));
        }

        if (bindings.BodySlotCapacity != physics.BodySlotCapacity)
        {
            throw new ArgumentException(
                "Physics3D AOI binding store capacity must match the Physics3D body slot capacity.",
                nameof(bindings));
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
        _workerCount = physics.WorkerCount;
        if (_workerCount <= 0)
        {
            throw new ArgumentException("Physics3D AOI requires a positive worker count from the Physics3D world.", nameof(physics));
        }

        _prepareWorker = PrepareWorker;
        int workerScratch = checked(_workerCount * config.GlobalEntityCapacity);
        _overlapHits = new Physics3DOverlapHit[workerScratch];
        _selectionStamps = new int[workerScratch];
        _selectionGenerations = new uint[workerScratch];
        _selectionEntities = new Entity[workerScratch];
        _selectedSlots = new int[workerScratch];
        _workerQueryStamps = new int[_workerCount];
        _workerAllocatedBytes = new long[_workerCount];
        int preparedCapacity = checked(players.SeatCapacity * replicationEntityCapacityPerSeat);
        _preparedHandles = new NetworkEntityHandle[preparedCapacity];
        _preparedEntities = new Entity[preparedCapacity];
        _preparedCounts = new int[players.SeatCapacity];
        _preparedSeatGenerations = new uint[players.SeatCapacity];
        _preparedPlayerIds = new int[players.SeatCapacity];
        _preparedViewers = new Entity[players.SeatCapacity];
        _preparedActive = new bool[players.SeatCapacity];
        _trackedCounts = new int[players.SeatCapacity];
        _trackedSeatGenerations = new uint[players.SeatCapacity];
        _trackedPlayerIds = new int[players.SeatCapacity];
        _trackedViewers = new Entity[players.SeatCapacity];
        _trackedHandles = new NetworkEntityHandle[preparedCapacity];
        _trackedEntities = new Entity[preparedCapacity];
        _batchSeats = new SessionSeatBinding[players.SeatCapacity];
    }

    public Physics3DNetworkAoiFailure LastFailure { get; private set; }
    public int PerSeatCapacity => _perSeatCapacity;
    public int OverlapScratchCapacity => _overlapHits.Length / _workerCount;
    public int WorkerCount => _workerCount;
    public int LastOverlapHitCount => _lastOverlapHitCount;
    public long LastWorkerAllocatedBytes { get; private set; }
    public int FailedSeatSlot => _failedSeatSlot;

    public bool TryPrepareConnectedSeats(ReadOnlySpan<SessionSeatBinding> connectedSeats)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastFailure = Physics3DNetworkAoiFailure.None;
        _lastOverlapHitCount = 0;
        LastWorkerAllocatedBytes = 0;
        _failedSeatSlot = -1;
        _workerFailureCode = (int)Physics3DNetworkAoiFailure.None;
        _preparedReady = false;
        _committed = false;
        ClearPreparedMarkers();

        if (!TryValidateConnectedSeats(connectedSeats))
        {
            return false;
        }

        _batchSeatCount = connectedSeats.Length;
        if (_batchSeatCount > _batchSeats.Length)
        {
            throw new InvalidOperationException(
                $"Physics3D AOI connected seat count {_batchSeatCount} exceeds seat capacity {_batchSeats.Length}.");
        }

        connectedSeats.CopyTo(_batchSeats);

        for (int index = 0; index < _batchSeatCount; index++)
        {
            SessionSeatBinding seat = _batchSeats[index];
            if (!_players.TryGetExistingController(in seat, out Entity viewer))
            {
                LastFailure = Physics3DNetworkAoiFailure.UnknownSeat;
                _failedSeatSlot = seat.Slot;
                return false;
            }

            if (!_players.TryGetBody(in seat, out Physics3DBodyId viewerBody) ||
                !_physics.ContainsBody(viewerBody))
            {
                LastFailure = Physics3DNetworkAoiFailure.ViewerBodyUnavailable;
                _failedSeatSlot = seat.Slot;
                return false;
            }

            int seatSlot = seat.Slot;
            _preparedActive[seatSlot] = true;
            _preparedSeatGenerations[seatSlot] = seat.Generation;
            _preparedPlayerIds[seatSlot] = seat.PlayerId.Value;
            _preparedViewers[seatSlot] = viewer;
            _preparedCounts[seatSlot] = 0;
        }

        Array.Clear(_workerAllocatedBytes);
        _physics.BeginWorkerDispatchMetrics();
        _physics.DispatchWorkers(_prepareWorker, _workerCount);

        var workerFailure = (Physics3DNetworkAoiFailure)Volatile.Read(ref _workerFailureCode);
        if (workerFailure != Physics3DNetworkAoiFailure.None)
        {
            LastFailure = workerFailure;
            ClearPreparedMarkers();
            return false;
        }

        long workerBytes = 0;
        for (int worker = 0; worker < _workerAllocatedBytes.Length; worker++)
        {
            workerBytes += _workerAllocatedBytes[worker];
        }

        LastWorkerAllocatedBytes = workerBytes;
        _preparedReady = true;
        return true;
    }

    public bool TryCommitPreparedKnowledge()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastFailure = Physics3DNetworkAoiFailure.None;
        if (!_preparedReady || _committed)
        {
            LastFailure = Physics3DNetworkAoiFailure.PreparedStateMissing;
            return false;
        }

        int projectedRecordCount = _knowledge.RecordCount;
        for (int batchIndex = 0; batchIndex < _batchSeatCount; batchIndex++)
        {
            SessionSeatBinding seat = _batchSeats[batchIndex];
            int seatSlot = seat.Slot;
            if (!_preparedActive[seatSlot])
            {
                LastFailure = Physics3DNetworkAoiFailure.PreparedStateMissing;
                _failedSeatSlot = seatSlot;
                return false;
            }

            Entity viewer = _preparedViewers[seatSlot];
            if (_trackedSeatGenerations[seatSlot] != 0 &&
                (_trackedSeatGenerations[seatSlot] != seat.Generation ||
                 _trackedPlayerIds[seatSlot] != seat.PlayerId.Value ||
                 _trackedViewers[seatSlot] != viewer))
            {
                projectedRecordCount -= CountLiveTracked(seatSlot, viewer);
                projectedRecordCount += _preparedCounts[seatSlot];
            }
            else
            {
                projectedRecordCount += ProjectKnowledgeDelta(seatSlot, viewer);
            }
        }

        if (projectedRecordCount > _knowledge.RecordCapacity)
        {
            LastFailure = Physics3DNetworkAoiFailure.KnowledgeCapacityExceeded;
            return false;
        }

        for (int batchIndex = 0; batchIndex < _batchSeatCount; batchIndex++)
        {
            SessionSeatBinding seat = _batchSeats[batchIndex];
            Entity viewer = _preparedViewers[seat.Slot];
            if (!TryApplyKnowledgeLane(in seat, viewer))
            {
                throw new InvalidOperationException(
                    $"Physics3D AOI knowledge commit failed after capacity validation: {LastFailure}.");
            }
        }

        _committed = true;
        return true;
    }

    public bool TryCopyPreparedInterest(
        in SessionSeatBinding seat,
        Span<NetworkEntityHandle> destination,
        out int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastFailure = Physics3DNetworkAoiFailure.None;
        count = 0;
        if (!_preparedReady ||
            (uint)seat.Slot >= (uint)_preparedActive.Length ||
            !_preparedActive[seat.Slot] ||
            _preparedSeatGenerations[seat.Slot] != seat.Generation ||
            _preparedPlayerIds[seat.Slot] != seat.PlayerId.Value)
        {
            LastFailure = Physics3DNetworkAoiFailure.PreparedStateMissing;
            return false;
        }

        int preparedCount = _preparedCounts[seat.Slot];
        count = preparedCount;
        if (destination.Length < preparedCount)
        {
            LastFailure = Physics3DNetworkAoiFailure.DestinationCapacityExceeded;
            return false;
        }

        int laneStart = checked(seat.Slot * _perSeatCapacity);
        _preparedHandles.AsSpan(laneStart, preparedCount).CopyTo(destination);
        return true;
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

    private void PrepareWorker(int workerIndex)
    {
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            int seatStart = (workerIndex * _batchSeatCount) / _workerCount;
            int seatEnd = ((workerIndex + 1) * _batchSeatCount) / _workerCount;
            int scratchBase = checked(workerIndex * OverlapScratchCapacity);
            Span<Physics3DOverlapHit> overlapHits = _overlapHits.AsSpan(scratchBase, OverlapScratchCapacity);
            Span<int> selectionStamps = _selectionStamps.AsSpan(scratchBase, OverlapScratchCapacity);
            Span<uint> selectionGenerations = _selectionGenerations.AsSpan(scratchBase, OverlapScratchCapacity);
            Span<Entity> selectionEntities = _selectionEntities.AsSpan(scratchBase, OverlapScratchCapacity);
            Span<int> selectedSlots = _selectedSlots.AsSpan(scratchBase, OverlapScratchCapacity);

            for (int batchIndex = seatStart; batchIndex < seatEnd; batchIndex++)
            {
                if (Volatile.Read(ref _workerFailureCode) != (int)Physics3DNetworkAoiFailure.None)
                {
                    return;
                }

                SessionSeatBinding seat = _batchSeats[batchIndex];
                if (!TryPrepareSeat(
                        in seat,
                        workerIndex,
                        overlapHits,
                        selectionStamps,
                        selectionGenerations,
                        selectionEntities,
                        selectedSlots))
                {
                    return;
                }
            }
        }
        finally
        {
            _workerAllocatedBytes[workerIndex] =
                GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        }
    }

    private bool TryPrepareSeat(
        in SessionSeatBinding seat,
        int workerIndex,
        Span<Physics3DOverlapHit> overlapHits,
        Span<int> selectionStamps,
        Span<uint> selectionGenerations,
        Span<Entity> selectionEntities,
        Span<int> selectedSlots)
    {
        int seatSlot = seat.Slot;
        if (!_players.TryGetBody(in seat, out Physics3DBodyId viewerBody))
        {
            FailWorker(Physics3DNetworkAoiFailure.ViewerBodyUnavailable, seatSlot);
            return false;
        }

        Physics3DBodyState viewerState = _physics.GetBodyState(viewerBody);
        int stamp = NextWorkerStamp(workerIndex);
        int selectedCount = 0;
        int overlapCount;
        try
        {
            var filter = new Physics3DQueryFilter(
                LayerMask.All,
                ignoredBody: default,
                includeSensors: true);
            overlapCount = _physics.OverlapSphere(
                workerIndex,
                viewerState.PositionCm,
                _radiusCm,
                in filter,
                overlapHits);
        }
        catch (Physics3DCapacityExceededException)
        {
            FailWorker(Physics3DNetworkAoiFailure.OverlapScratchCapacityExceeded, seatSlot);
            return false;
        }

        Interlocked.Exchange(
            ref _lastOverlapHitCount,
            Math.Max(Volatile.Read(ref _lastOverlapHitCount), overlapCount));
        for (int index = 0; index < overlapCount; index++)
        {
            ref readonly Physics3DOverlapHit hit = ref overlapHits[index];
            if (!_bindings.TryGet(
                    hit.Body,
                    out Entity boundEntity,
                    out NetworkEntityHandle handle,
                    out _,
                    out _))
            {
                continue;
            }

            if (boundEntity != hit.Entity)
            {
                FailWorker(Physics3DNetworkAoiFailure.BindingMismatch, seatSlot);
                return false;
            }

            if (!handle.IsValid || (uint)handle.Slot >= (uint)OverlapScratchCapacity)
            {
                FailWorker(Physics3DNetworkAoiFailure.InvalidNetworkHandle, seatSlot);
                return false;
            }

            int networkSlot = handle.Slot;
            if (selectionStamps[networkSlot] == stamp)
            {
                FailWorker(Physics3DNetworkAoiFailure.DuplicateNetworkSlot, seatSlot);
                return false;
            }

            selectionStamps[networkSlot] = stamp;
            selectionGenerations[networkSlot] = handle.Generation;
            selectionEntities[networkSlot] = boundEntity;
            selectedSlots[selectedCount++] = networkSlot;
        }

        if (selectedCount > _perSeatCapacity)
        {
            FailWorker(Physics3DNetworkAoiFailure.PerSeatCapacityExceeded, seatSlot);
            return false;
        }

        Span<int> orderedSlots = selectedSlots[..selectedCount];
        orderedSlots.Sort();
        int laneStart = checked(seatSlot * _perSeatCapacity);
        for (int index = 0; index < orderedSlots.Length; index++)
        {
            int networkSlot = orderedSlots[index];
            _preparedHandles[laneStart + index] =
                new NetworkEntityHandle(networkSlot, selectionGenerations[networkSlot]);
            _preparedEntities[laneStart + index] = selectionEntities[networkSlot];
        }

        _preparedCounts[seatSlot] = selectedCount;
        return true;
    }

    private bool TryValidateConnectedSeats(ReadOnlySpan<SessionSeatBinding> connectedSeats)
    {
        for (int index = 0; index < connectedSeats.Length; index++)
        {
            SessionSeatBinding seat = connectedSeats[index];
            if (!seat.IsValid || (uint)seat.Slot >= (uint)_preparedActive.Length)
            {
                LastFailure = Physics3DNetworkAoiFailure.UnknownSeat;
                _failedSeatSlot = seat.Slot;
                return false;
            }

            if (index > 0 && seat.Slot <= connectedSeats[index - 1].Slot)
            {
                LastFailure = seat.Slot == connectedSeats[index - 1].Slot
                    ? Physics3DNetworkAoiFailure.DuplicateConnectedSeat
                    : Physics3DNetworkAoiFailure.ConnectedSeatOrderInvalid;
                _failedSeatSlot = seat.Slot;
                return false;
            }
        }

        return true;
    }

    private int ProjectKnowledgeDelta(int seatSlot, Entity viewer)
    {
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int oldCount = _trackedCounts[seatSlot];
        ReadOnlySpan<NetworkEntityHandle> currentHandles =
            _preparedHandles.AsSpan(laneStart, _preparedCounts[seatSlot]);
        int oldIndex = 0;
        int currentIndex = 0;
        int exitCount = 0;
        int enterCount = 0;
        while (oldIndex < oldCount || currentIndex < currentHandles.Length)
        {
            if (oldIndex >= oldCount)
            {
                Entity entering = _preparedEntities[laneStart + currentIndex++];
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
                Entity entering = _preparedEntities[laneStart + currentIndex++];
                if (!_knowledge.TryGet(viewer, entering, currentTick: 0, out _))
                {
                    enterCount++;
                }
            }
        }

        return enterCount - exitCount;
    }

    private int CountLiveTracked(int seatSlot, Entity viewer)
    {
        int laneStart = checked(seatSlot * _perSeatCapacity);
        int count = _trackedCounts[seatSlot];
        int live = 0;
        for (int index = 0; index < count; index++)
        {
            Entity target = _trackedEntities[laneStart + index];
            if (viewer != Entity.Null && target != Entity.Null &&
                _knowledge.TryGet(viewer, target, currentTick: 0, out _))
            {
                live++;
            }
        }

        return live;
    }

    private bool TryApplyKnowledgeLane(in SessionSeatBinding seat, Entity viewer)
    {
        int seatSlot = seat.Slot;
        if (_trackedSeatGenerations[seatSlot] != 0 &&
            (_trackedSeatGenerations[seatSlot] != seat.Generation ||
             _trackedPlayerIds[seatSlot] != seat.PlayerId.Value ||
             _trackedViewers[seatSlot] != viewer))
        {
            ClearTrackedLane(seatSlot);
        }

        int laneStart = checked(seatSlot * _perSeatCapacity);
        int preparedCount = _preparedCounts[seatSlot];
        ReadOnlySpan<NetworkEntityHandle> currentHandles =
            _preparedHandles.AsSpan(laneStart, preparedCount);
        ReadOnlySpan<Entity> currentEntities =
            _preparedEntities.AsSpan(laneStart, preparedCount);

        for (int index = 0; index < preparedCount; index++)
        {
            NetworkEntityHandle handle = currentHandles[index];
            Entity entity = currentEntities[index];
            if (!_networkEntities.TryResolve(handle, out Entity mapped) || mapped != entity)
            {
                LastFailure = Physics3DNetworkAoiFailure.EntityTableMismatch;
                _failedSeatSlot = seatSlot;
                return false;
            }
        }

        int oldCount = _trackedCounts[seatSlot];
        int oldIndex = 0;
        int currentIndex = 0;
        int enterCount = 0;
        while (oldIndex < oldCount || currentIndex < preparedCount)
        {
            if (oldIndex >= oldCount)
            {
                if (!_knowledge.TryGet(viewer, currentEntities[currentIndex++], currentTick: 0, out _))
                {
                    enterCount++;
                }

                continue;
            }

            if (currentIndex >= preparedCount)
            {
                oldIndex++;
                continue;
            }

            int comparison = Compare(_trackedHandles[laneStart + oldIndex], currentHandles[currentIndex]);
            if (comparison == 0)
            {
                oldIndex++;
                currentIndex++;
            }
            else if (comparison < 0)
            {
                oldIndex++;
            }
            else
            {
                if (!_knowledge.TryGet(viewer, currentEntities[currentIndex++], currentTick: 0, out _))
                {
                    enterCount++;
                }
            }
        }

        oldIndex = 0;
        currentIndex = 0;
        while (oldIndex < oldCount)
        {
            NetworkEntityHandle oldHandle = _trackedHandles[laneStart + oldIndex];
            while (currentIndex < preparedCount &&
                   Compare(currentHandles[currentIndex], oldHandle) < 0)
            {
                currentIndex++;
            }

            if (currentIndex >= preparedCount || currentHandles[currentIndex] != oldHandle)
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
            LastFailure = Physics3DNetworkAoiFailure.KnowledgeCapacityExceeded;
            _failedSeatSlot = seatSlot;
            return false;
        }

        KnowledgeDisclosureRecord disclosure = LiveDisclosure(viewer);
        oldIndex = 0;
        for (int index = 0; index < preparedCount; index++)
        {
            NetworkEntityHandle handle = currentHandles[index];
            Entity target = currentEntities[index];
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

        for (int index = preparedCount; index < oldCount; index++)
        {
            _trackedHandles[laneStart + index] = default;
            _trackedEntities[laneStart + index] = Entity.Null;
        }

        _trackedCounts[seatSlot] = preparedCount;
        _trackedSeatGenerations[seatSlot] = seat.Generation;
        _trackedPlayerIds[seatSlot] = seat.PlayerId.Value;
        _trackedViewers[seatSlot] = viewer;
        return true;
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

    private void ClearPreparedMarkers()
    {
        for (int seatSlot = 0; seatSlot < _preparedActive.Length; seatSlot++)
        {
            if (!_preparedActive[seatSlot])
            {
                continue;
            }

            int laneStart = checked(seatSlot * _perSeatCapacity);
            int count = _preparedCounts[seatSlot];
            for (int index = 0; index < count; index++)
            {
                _preparedHandles[laneStart + index] = default;
                _preparedEntities[laneStart + index] = Entity.Null;
            }

            _preparedCounts[seatSlot] = 0;
            _preparedSeatGenerations[seatSlot] = 0;
            _preparedPlayerIds[seatSlot] = 0;
            _preparedViewers[seatSlot] = Entity.Null;
            _preparedActive[seatSlot] = false;
        }

        _batchSeatCount = 0;
        _preparedReady = false;
        _committed = false;
    }

    private void FailWorker(Physics3DNetworkAoiFailure failure, int seatSlot)
    {
        if (Interlocked.CompareExchange(ref _failedSeatSlot, seatSlot, -1) == -1)
        {
            Volatile.Write(ref _workerFailureCode, (int)failure);
        }
    }

    private int NextWorkerStamp(int workerIndex)
    {
        int stamp = _workerQueryStamps[workerIndex];
        if (stamp == int.MaxValue)
        {
            int scratchBase = checked(workerIndex * OverlapScratchCapacity);
            _selectionStamps.AsSpan(scratchBase, OverlapScratchCapacity).Clear();
            stamp = 0;
        }

        stamp++;
        _workerQueryStamps[workerIndex] = stamp;
        return stamp;
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
