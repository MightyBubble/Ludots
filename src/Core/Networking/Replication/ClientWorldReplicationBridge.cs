using System;
using Arch.Core;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.Replication
{
    public readonly struct ReplicationMirrorIdentity
    {
        public ReplicationMirrorIdentity(NetworkEntityHandle handle)
        {
            Handle = handle;
        }

        public NetworkEntityHandle Handle { get; }
    }

    public readonly struct ReplicationMirrorState
    {
        public ReplicationMirrorState(
            int schemaId,
            uint revision,
            in ReplicationStateVector values,
            in ReplicationControlOwnership ownership)
        {
            SchemaId = schemaId;
            Revision = revision;
            Values = values;
            Ownership = ownership;
        }

        public int SchemaId { get; }
        public uint Revision { get; }
        public ReplicationStateVector Values { get; }
        public ReplicationControlOwnership Ownership { get; }
    }

    public sealed class ClientWorldReplicationBridge
    {
        private enum BatchOperationKind : byte
        {
            Leave = 1,
            Update = 2,
            Create = 3,
        }

        private readonly World _world;
        private readonly ClientReplicationMirror _mirror;
        private readonly ClientReplicationSchemaApplierRegistry _appliers;
        private readonly int _globalEntityCapacity;
        private readonly FixedIntSparseMap _globalSlotToLane;
        private readonly int[] _freeLanes;
        private readonly int[] _laneGlobalSlots;
        private readonly SessionSeatBinding _clientSeat;
        private readonly bool[] _active;
        private readonly bool[] _owned;
        private readonly bool[] _plannedActive;
        private readonly bool[] _releaseSeen;
        private readonly uint[] _generations;
        private readonly uint[] _plannedGenerations;
        private readonly int[] _schemas;
        private readonly int[] _plannedSchemas;
        private readonly Entity[] _entities;
        private readonly BatchOperationKind[] _batchKinds;
        private readonly ReplicationMirrorLeaveKind[] _batchLeaveKinds;
        private readonly int[] _batchLanes;
        private readonly int[] _batchGlobalSlots;
        private readonly ReplicatedEntityState[] _batchStates;
        private int _freeCount;
        private int _activeCount;
        private int _batchCount;
        private ReplicationApplyContext _pendingContext;
        private ReplicationApplyContext _lastAppliedContext;
        private bool _tornDown;

        public ClientWorldReplicationBridge(
            World world,
            int globalEntityCapacity,
            int activeMirrorCapacity,
            in SessionSeatBinding clientSeat,
            ulong sessionEpoch,
            ClientReplicationSchemaApplierRegistry appliers)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            if (globalEntityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(globalEntityCapacity));
            }

            if (activeMirrorCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(activeMirrorCapacity));
            }

            if (activeMirrorCapacity > globalEntityCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeMirrorCapacity),
                    "Active mirror capacity cannot exceed global entity capacity.");
            }

            if (!clientSeat.IsValid)
            {
                throw new ArgumentException("Client replication bridge requires an accepted seat.", nameof(clientSeat));
            }

            if (!appliers.IsFrozen)
            {
                throw new InvalidOperationException("Client replication schema applier registry must be frozen before bridge construction.");
            }

            _clientSeat = clientSeat;
            _globalEntityCapacity = globalEntityCapacity;
            _mirror = new ClientReplicationMirror(globalEntityCapacity, activeMirrorCapacity, sessionEpoch);
            _globalSlotToLane = new FixedIntSparseMap(activeMirrorCapacity);
            _freeLanes = new int[activeMirrorCapacity];
            _laneGlobalSlots = new int[activeMirrorCapacity];
            _active = new bool[activeMirrorCapacity];
            _owned = new bool[activeMirrorCapacity];
            _plannedActive = new bool[activeMirrorCapacity];
            _releaseSeen = new bool[activeMirrorCapacity];
            _generations = new uint[activeMirrorCapacity];
            _plannedGenerations = new uint[activeMirrorCapacity];
            _schemas = new int[activeMirrorCapacity];
            _plannedSchemas = new int[activeMirrorCapacity];
            _entities = new Entity[activeMirrorCapacity];
            for (int lane = 0; lane < activeMirrorCapacity; lane++)
            {
                _laneGlobalSlots[lane] = -1;
                _freeLanes[activeMirrorCapacity - lane - 1] = lane;
            }

            _freeCount = activeMirrorCapacity;
            int batchCapacity = checked(activeMirrorCapacity * 2);
            _batchKinds = new BatchOperationKind[batchCapacity];
            _batchLeaveKinds = new ReplicationMirrorLeaveKind[batchCapacity];
            _batchLanes = new int[batchCapacity];
            _batchGlobalSlots = new int[batchCapacity];
            _batchStates = new ReplicatedEntityState[batchCapacity];
        }

        public int GlobalEntityCapacity => _globalEntityCapacity;
        public int ActiveMirrorCapacity => _active.Length;
        public SessionSeatBinding ClientSeat => _clientSeat;
        public ulong SessionEpoch => _mirror.SessionEpoch;
        public ulong LastSnapshotId => _mirror.LastSnapshotId;
        public bool IsTornDown => _tornDown;

        public ReplicationBridgeResult BindExisting(NetworkEntityHandle handle, Entity entity)
        {
            if (_tornDown)
            {
                return ReplicationBridgeResult.TornDown;
            }

            int globalSlot = handle.Slot;
            if (_mirror.LastSnapshotId != 0 ||
                !handle.IsValid ||
                (uint)globalSlot >= (uint)_globalEntityCapacity ||
                entity == Entity.Null ||
                !_world.IsAlive(entity) ||
                _globalSlotToLane.TryGet(globalSlot, out _) ||
                _world.Has<ReplicationMirrorIdentity>(entity) ||
                _world.Has<ReplicationMirrorState>(entity))
            {
                return ReplicationBridgeResult.InvalidInput;
            }

            for (int lane = 0; lane < _active.Length; lane++)
            {
                if (_active[lane] && _entities[lane] == entity)
                {
                    return ReplicationBridgeResult.InvalidInput;
                }
            }

            if (!TryAllocateLane(globalSlot, out int allocatedLane))
            {
                return ReplicationBridgeResult.CapacityContractViolated;
            }

            var identity = new ReplicationMirrorIdentity(handle);
            var state = default(ReplicationMirrorState);
            CommitExistingBinding(entity, in identity, in state);
            _active[allocatedLane] = true;
            _activeCount++;
            _owned[allocatedLane] = false;
            _generations[allocatedLane] = handle.Generation;
            _schemas[allocatedLane] = 0;
            _entities[allocatedLane] = entity;
            return ReplicationBridgeResult.Success;
        }

        public ReplicationBridgeResult Apply(ReplicationPacketBuffer packet)
        {
            if (_tornDown)
            {
                return ReplicationBridgeResult.TornDown;
            }

            if (packet == null)
            {
                return ReplicationBridgeResult.InvalidPacket;
            }

            if (packet.EntityCapacity > ActiveMirrorCapacity)
            {
                return ReplicationBridgeResult.CapacityContractViolated;
            }

            ReplicationApplyResult mirrorValidation = _mirror.Validate(packet);
            if (mirrorValidation != ReplicationApplyResult.Success)
            {
                return ReplicationBridgeResultMapper.FromApply(mirrorValidation);
            }

            ReplicationBridgeResult localValidation = ValidateLocalStateAndGenerations(packet);
            if (localValidation != ReplicationBridgeResult.Success)
            {
                return localValidation;
            }

            Array.Copy(_active, _plannedActive, _active.Length);
            Array.Copy(_generations, _plannedGenerations, _generations.Length);
            Array.Copy(_schemas, _plannedSchemas, _schemas.Length);
            _batchCount = 0;
            _pendingContext = new ReplicationApplyContext(
                in _clientSeat,
                packet.Header.SessionEpoch,
                packet.Header.Tick,
                packet.Header.SnapshotId,
                packet.Header.Kind);

            if (packet.Header.Kind == ReplicationPacketKind.Full)
            {
                PrepareFull(packet.Upserts);
            }
            else
            {
                PrepareDelta(packet);
            }

            ReplicationBridgeResult schemaValidation = ValidateBatchApplications();
            if (schemaValidation != ReplicationBridgeResult.Success)
            {
                _batchCount = 0;
                return schemaValidation;
            }

            _mirror.CommitValidated(packet);
            try
            {
                _appliers.NotifyBatchCommitBeginning();
                CommitBatch();
                _lastAppliedContext = _pendingContext;
                _appliers.NotifyBatchEnded(committed: true);
                return ReplicationBridgeResult.Success;
            }
            catch
            {
                _appliers.NotifyBatchEnded(committed: false);
                throw;
            }
        }

        /// <summary>
        /// Idempotent epoch teardown: releases every active mirror resource, destroys owned ECS
        /// entities, unbinds borrowed entities, and clears the mirror table.
        /// </summary>
        public ReplicationBridgeResult Teardown()
        {
            if (_tornDown)
            {
                return ReplicationBridgeResult.Success;
            }

            ReplicationApplyContext context = _mirror.LastSnapshotId == 0
                ? new ReplicationApplyContext(
                    in _clientSeat,
                    _mirror.SessionEpoch,
                    committedTick: 0,
                    snapshotId: 0,
                    packetKind: default)
                : _lastAppliedContext;

            for (int lane = 0; lane < _active.Length; lane++)
            {
                if (!_active[lane])
                {
                    continue;
                }

                ReplicationBridgeResult validation = ValidateLeave(
                    lane,
                    ReplicationMirrorLeaveKind.Teardown,
                    in context);
                if (validation != ReplicationBridgeResult.Success)
                {
                    return validation;
                }
            }

            for (int lane = 0; lane < _active.Length; lane++)
            {
                if (_active[lane])
                {
                    CommitLeave(lane, ReplicationMirrorLeaveKind.Teardown, in context);
                }
            }

            _mirror.Clear();
            _batchCount = 0;
            _tornDown = true;
            return ReplicationBridgeResult.Success;
        }

        public bool TryResolve(NetworkEntityHandle handle, out Entity entity)
        {
            if (_tornDown ||
                !handle.IsValid ||
                (uint)handle.Slot >= (uint)_globalEntityCapacity ||
                !_globalSlotToLane.TryGet(handle.Slot, out int lane) ||
                !_active[lane] ||
                _generations[lane] != handle.Generation ||
                !_world.IsAlive(_entities[lane]))
            {
                entity = Entity.Null;
                return false;
            }

            entity = _entities[lane];
            return true;
        }

        private ReplicationBridgeResult ValidateLocalStateAndGenerations(ReplicationPacketBuffer packet)
        {
            for (int lane = 0; lane < _active.Length; lane++)
            {
                if (!_active[lane])
                {
                    continue;
                }

                Entity entity = _entities[lane];
                if (!_world.IsAlive(entity) ||
                    !_world.Has<ReplicationMirrorIdentity>(entity) ||
                    !_world.Has<ReplicationMirrorState>(entity))
                {
                    return ReplicationBridgeResult.EcsStateMismatch;
                }
            }

            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            for (int i = 0; i < upserts.Length; i++)
            {
                ReplicatedEntityState state = upserts[i];
                int globalSlot = state.Entity.Slot;
                if (!state.Entity.IsValid ||
                    state.SchemaId <= 0 ||
                    (uint)globalSlot >= (uint)_globalEntityCapacity)
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }

                if (_globalSlotToLane.TryGet(globalSlot, out int lane) && _active[lane])
                {
                    if (state.Entity.Generation < _generations[lane])
                    {
                        return ReplicationBridgeResult.ResyncRequired;
                    }

                    if (state.Entity.Generation == _generations[lane] &&
                        _schemas[lane] != 0 &&
                        state.SchemaId != _schemas[lane])
                    {
                        return ReplicationBridgeResult.ResyncRequired;
                    }

                    if (packet.Header.Kind == ReplicationPacketKind.Delta &&
                        state.Entity.Generation != _generations[lane] &&
                        !ContainsVisibleLeave(packet, new NetworkEntityHandle(globalSlot, _generations[lane])))
                    {
                        return ReplicationBridgeResult.InvalidPacket;
                    }
                }
            }

            Array.Clear(_releaseSeen);
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            for (int i = 0; i < removals.Length; i++)
            {
                int globalSlot = removals[i].Slot;
                if (!removals[i].IsValid ||
                    (uint)globalSlot >= (uint)_globalEntityCapacity ||
                    !_globalSlotToLane.TryGet(globalSlot, out int lane) ||
                    !_active[lane] ||
                    _releaseSeen[lane])
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }

                _releaseSeen[lane] = true;
            }

            ReadOnlySpan<ReplicationDisclosureChange> changes = packet.DisclosureChanges;
            for (int i = 0; i < changes.Length; i++)
            {
                ReplicationDisclosureChange change = changes[i];
                if (change.Kind != ReplicationDisclosureChangeKind.Conceal)
                {
                    continue;
                }

                int globalSlot = change.Entity.Slot;
                if (!change.Entity.IsValid ||
                    (uint)globalSlot >= (uint)_globalEntityCapacity ||
                    !_globalSlotToLane.TryGet(globalSlot, out int lane) ||
                    !_active[lane] ||
                    _releaseSeen[lane])
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }

                _releaseSeen[lane] = true;
            }

            return ReplicationBridgeResult.Success;
        }

        private void PrepareFull(ReadOnlySpan<ReplicatedEntityState> upserts)
        {
            for (int lane = 0; lane < _active.Length; lane++)
            {
                if (!_plannedActive[lane])
                {
                    continue;
                }

                int upsertIndex = FindUpsert(upserts, _laneGlobalSlots[lane]);
                if (upsertIndex < 0)
                {
                    // Full pages describe the current visible set only.
                    QueueLeave(lane, ReplicationMirrorLeaveKind.Conceal);
                }
                else if (upserts[upsertIndex].Entity.Generation != _plannedGenerations[lane])
                {
                    // A new generation permanently retires the prior network identity.
                    QueueLeave(lane, ReplicationMirrorLeaveKind.Removal);
                }
            }

            for (int i = 0; i < upserts.Length; i++)
            {
                QueueUpsert(in upserts[i]);
            }
        }

        private void PrepareDelta(ReplicationPacketBuffer packet)
        {
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            for (int i = 0; i < removals.Length; i++)
            {
                if (_globalSlotToLane.TryGet(removals[i].Slot, out int lane))
                {
                    QueueLeave(lane, ReplicationMirrorLeaveKind.Removal);
                }
            }

            ReadOnlySpan<ReplicationDisclosureChange> changes = packet.DisclosureChanges;
            for (int i = 0; i < changes.Length; i++)
            {
                if (changes[i].Kind == ReplicationDisclosureChangeKind.Conceal &&
                    _globalSlotToLane.TryGet(changes[i].Entity.Slot, out int lane))
                {
                    QueueLeave(lane, ReplicationMirrorLeaveKind.Conceal);
                }
            }

            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            for (int i = 0; i < upserts.Length; i++)
            {
                QueueUpsert(in upserts[i]);
            }
        }

        private void QueueLeave(int lane, ReplicationMirrorLeaveKind leaveKind)
        {
            if (!_plannedActive[lane])
            {
                return;
            }

            AddBatchOperation(BatchOperationKind.Leave, leaveKind, lane, _laneGlobalSlots[lane], default);
            _plannedActive[lane] = false;
            _plannedGenerations[lane] = 0;
            _plannedSchemas[lane] = 0;
        }

        private void QueueUpsert(in ReplicatedEntityState state)
        {
            int globalSlot = state.Entity.Slot;
            if (_globalSlotToLane.TryGet(globalSlot, out int lane) &&
                _plannedActive[lane] &&
                _plannedGenerations[lane] == state.Entity.Generation)
            {
                AddBatchOperation(BatchOperationKind.Update, default, lane, globalSlot, in state);
                _plannedSchemas[lane] = state.SchemaId;
                return;
            }

            if (_globalSlotToLane.TryGet(globalSlot, out lane) && _plannedActive[lane])
            {
                // Generation replacement permanently retires the prior mirror.
                QueueLeave(lane, ReplicationMirrorLeaveKind.Removal);
            }

            // Create resolves/allocates its lane at commit so validation stays mutation-free.
            AddBatchOperation(BatchOperationKind.Create, default, lane: -1, globalSlot, in state);
        }

        private void AddBatchOperation(
            BatchOperationKind kind,
            ReplicationMirrorLeaveKind leaveKind,
            int lane,
            int globalSlot,
            in ReplicatedEntityState state)
        {
            if (_batchCount == _batchKinds.Length)
            {
                throw new InvalidOperationException("Validated replication structural batch exceeded its fixed capacity.");
            }

            _batchKinds[_batchCount] = kind;
            _batchLeaveKinds[_batchCount] = leaveKind;
            _batchLanes[_batchCount] = lane;
            _batchGlobalSlots[_batchCount] = globalSlot;
            _batchStates[_batchCount] = state;
            _batchCount++;
        }

        private void CommitBatch()
        {
            // All packet-driven structural changes cross this validated, fixed-capacity commit boundary.
            for (int i = 0; i < _batchCount; i++)
            {
                switch (_batchKinds[i])
                {
                    case BatchOperationKind.Leave:
                        CommitLeave(_batchLanes[i], _batchLeaveKinds[i], in _pendingContext);
                        break;
                    case BatchOperationKind.Update:
                    {
                        int lane = _batchLanes[i];
                        ReplicatedEntityState state = _batchStates[i];
                        Entity entity = _entities[lane];
                        var identity = new ReplicationMirrorIdentity(state.Entity);
                        var mirrorState = ToMirrorState(in state);
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            throw new InvalidOperationException("Validated client replication schema applier is unavailable.");
                        }

                        applier.Apply(_world, entity, in state, in _pendingContext);
                        _world.Set(entity, in identity);
                        _world.Set(entity, in mirrorState);
                        _schemas[lane] = state.SchemaId;
                        break;
                    }
                    case BatchOperationKind.Create:
                    {
                        ReplicatedEntityState state = _batchStates[i];
                        int globalSlot = _batchGlobalSlots[i];
                        if (!TryAllocateLane(globalSlot, out int lane))
                        {
                            throw new InvalidOperationException(
                                "Validated replication structural create exceeded its fixed active capacity.");
                        }

                        var identity = new ReplicationMirrorIdentity(state.Entity);
                        var mirrorState = ToMirrorState(in state);
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            throw new InvalidOperationException("Validated client replication schema applier is unavailable.");
                        }

                        Entity entity = applier.Create(_world, in identity, in mirrorState, in _pendingContext);
                        if (entity == Entity.Null ||
                            !_world.IsAlive(entity) ||
                            !_world.Has<ReplicationMirrorIdentity>(entity) ||
                            !_world.Has<ReplicationMirrorState>(entity))
                        {
                            throw new InvalidOperationException("Client replication schema applier violated the create contract.");
                        }

                        _active[lane] = true;
                        _activeCount++;
                        _owned[lane] = true;
                        _generations[lane] = state.Entity.Generation;
                        _schemas[lane] = state.SchemaId;
                        _entities[lane] = entity;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("Unknown replication structural batch operation.");
                }
            }

            _batchCount = 0;
        }

        private void CommitLeave(
            int lane,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
        {
            Entity entity = _entities[lane];
            int schemaId = _schemas[lane];
            bool owned = _owned[lane];

            if (schemaId > 0)
            {
                if (!_appliers.TryGet(schemaId, out IClientReplicationSchemaApplier applier))
                {
                    throw new InvalidOperationException("Validated client replication schema applier is unavailable.");
                }

                // Owned and borrowed mirrors both receive exactly one release callback.
                applier.Release(_world, entity, leaveKind, in context);
            }

            if (owned)
            {
                _world.Destroy(entity);
            }
            else if (_world.IsAlive(entity))
            {
                _world.Remove<ReplicationMirrorIdentity, ReplicationMirrorState>(entity);
            }

            ReleaseLane(lane);
        }

        private ReplicationBridgeResult ValidateBatchApplications()
        {
            _appliers.NotifyBatchValidationBeginning(in _pendingContext);
            for (int i = 0; i < _batchCount; i++)
            {
                switch (_batchKinds[i])
                {
                    case BatchOperationKind.Leave:
                    {
                        ReplicationBridgeResult validation = ValidateLeave(
                            _batchLanes[i],
                            _batchLeaveKinds[i],
                            in _pendingContext);
                        if (validation != ReplicationBridgeResult.Success)
                        {
                            _appliers.NotifyBatchEnded(committed: false);
                            return validation;
                        }

                        break;
                    }
                    case BatchOperationKind.Update:
                    {
                        int lane = _batchLanes[i];
                        ReplicatedEntityState state = _batchStates[i];
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            _appliers.NotifyBatchEnded(committed: false);
                            return ReplicationBridgeResult.SchemaNotRegistered;
                        }

                        if (!applier.CanApply(_world, _entities[lane], in state, in _pendingContext))
                        {
                            _appliers.NotifyBatchEnded(committed: false);
                            return ReplicationBridgeResult.SchemaApplyRejected;
                        }

                        break;
                    }
                    case BatchOperationKind.Create:
                    {
                        ReplicatedEntityState state = _batchStates[i];
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            _appliers.NotifyBatchEnded(committed: false);
                            return ReplicationBridgeResult.SchemaNotRegistered;
                        }

                        if (!applier.CanCreate(_world, in state, in _pendingContext))
                        {
                            _appliers.NotifyBatchEnded(committed: false);
                            return ReplicationBridgeResult.SchemaApplyRejected;
                        }

                        break;
                    }
                    default:
                        _appliers.NotifyBatchEnded(committed: false);
                        return ReplicationBridgeResult.InvalidPacket;
                }
            }

            if (!_appliers.CanCommitBatchValidation())
            {
                _appliers.NotifyBatchEnded(committed: false);
                return ReplicationBridgeResult.SchemaApplyRejected;
            }

            return ReplicationBridgeResult.Success;
        }

        private ReplicationBridgeResult ValidateLeave(
            int lane,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
        {
            int schemaId = _schemas[lane];
            if (schemaId == 0)
            {
                // A borrowed binding can be torn down before its first replicated schema arrives.
                return ReplicationBridgeResult.Success;
            }

            if (!_appliers.TryGet(schemaId, out IClientReplicationSchemaApplier applier))
            {
                return ReplicationBridgeResult.SchemaNotRegistered;
            }

            return applier.CanRelease(_world, _entities[lane], leaveKind, in context)
                ? ReplicationBridgeResult.Success
                : ReplicationBridgeResult.SchemaApplyRejected;
        }

        private void CommitExistingBinding(
            Entity entity,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state)
        {
            // Loading-time authored bindings use the same explicit structural boundary as packet application.
            _world.Add(entity, in identity, in state);
        }

        private bool TryAllocateLane(int globalSlot, out int lane)
        {
            if (_freeCount == 0)
            {
                lane = -1;
                return false;
            }

            lane = _freeLanes[--_freeCount];
            if (!_globalSlotToLane.TryAdd(globalSlot, lane))
            {
                _freeLanes[_freeCount++] = lane;
                lane = -1;
                return false;
            }

            _laneGlobalSlots[lane] = globalSlot;
            return true;
        }

        private void ReleaseLane(int lane)
        {
            int globalSlot = _laneGlobalSlots[lane];
            if (globalSlot >= 0)
            {
                _globalSlotToLane.TryRemove(globalSlot, out _);
            }

            if (_active[lane])
            {
                _active[lane] = false;
                _activeCount--;
            }

            _owned[lane] = false;
            _generations[lane] = 0;
            _schemas[lane] = 0;
            _entities[lane] = Entity.Null;
            _laneGlobalSlots[lane] = -1;
            _freeLanes[_freeCount++] = lane;
        }

        private static bool ContainsVisibleLeave(ReplicationPacketBuffer packet, NetworkEntityHandle entity)
        {
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            for (int i = 0; i < removals.Length; i++)
            {
                if (removals[i] == entity)
                {
                    return true;
                }
            }

            ReadOnlySpan<ReplicationDisclosureChange> changes = packet.DisclosureChanges;
            for (int i = 0; i < changes.Length; i++)
            {
                if (changes[i].Kind == ReplicationDisclosureChangeKind.Conceal &&
                    changes[i].Entity == entity)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindUpsert(ReadOnlySpan<ReplicatedEntityState> upserts, int globalSlot)
        {
            for (int i = 0; i < upserts.Length; i++)
            {
                if (upserts[i].Entity.Slot == globalSlot)
                {
                    return i;
                }
            }

            return -1;
        }

        private static ReplicationMirrorState ToMirrorState(in ReplicatedEntityState state)
            => new(state.SchemaId, state.Revision, state.Values, state.Ownership);
    }
}
