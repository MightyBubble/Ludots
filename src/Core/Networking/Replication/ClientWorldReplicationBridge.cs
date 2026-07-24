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
        private readonly int[] _batchSlots;
        private readonly ReplicatedEntityState[] _batchStates;
        private int _batchCount;
        private ReplicationApplyContext _pendingContext;
        private ReplicationApplyContext _lastAppliedContext;
        private bool _tornDown;

        public ClientWorldReplicationBridge(
            World world,
            int entityCapacity,
            in SessionSeatBinding clientSeat,
            ulong sessionEpoch,
            ClientReplicationSchemaApplierRegistry appliers)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
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
            _mirror = new ClientReplicationMirror(entityCapacity, sessionEpoch);
            _active = new bool[entityCapacity];
            _owned = new bool[entityCapacity];
            _plannedActive = new bool[entityCapacity];
            _releaseSeen = new bool[entityCapacity];
            _generations = new uint[entityCapacity];
            _plannedGenerations = new uint[entityCapacity];
            _schemas = new int[entityCapacity];
            _plannedSchemas = new int[entityCapacity];
            _entities = new Entity[entityCapacity];
            int batchCapacity = checked(entityCapacity * 2);
            _batchKinds = new BatchOperationKind[batchCapacity];
            _batchLeaveKinds = new ReplicationMirrorLeaveKind[batchCapacity];
            _batchSlots = new int[batchCapacity];
            _batchStates = new ReplicatedEntityState[batchCapacity];
        }

        public int EntityCapacity => _active.Length;
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

            int slot = handle.Slot;
            if (_mirror.LastSnapshotId != 0 ||
                !handle.IsValid ||
                (uint)slot >= (uint)_active.Length ||
                entity == Entity.Null ||
                !_world.IsAlive(entity) ||
                _active[slot] ||
                _world.Has<ReplicationMirrorIdentity>(entity) ||
                _world.Has<ReplicationMirrorState>(entity))
            {
                return ReplicationBridgeResult.InvalidInput;
            }

            for (int i = 0; i < _active.Length; i++)
            {
                if (_active[i] && _entities[i] == entity)
                {
                    return ReplicationBridgeResult.InvalidInput;
                }
            }

            var identity = new ReplicationMirrorIdentity(handle);
            var state = default(ReplicationMirrorState);
            CommitExistingBinding(entity, in identity, in state);
            _active[slot] = true;
            _owned[slot] = false;
            _generations[slot] = handle.Generation;
            _schemas[slot] = 0;
            _entities[slot] = entity;
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

            if (packet.EntityCapacity > _active.Length)
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
            CommitBatch();
            _lastAppliedContext = _pendingContext;
            return ReplicationBridgeResult.Success;
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

            for (int slot = 0; slot < _active.Length; slot++)
            {
                if (!_active[slot])
                {
                    continue;
                }

                ReplicationBridgeResult validation = ValidateLeave(
                    slot,
                    ReplicationMirrorLeaveKind.Teardown,
                    in context);
                if (validation != ReplicationBridgeResult.Success)
                {
                    return validation;
                }
            }

            for (int slot = 0; slot < _active.Length; slot++)
            {
                if (_active[slot])
                {
                    CommitLeave(slot, ReplicationMirrorLeaveKind.Teardown, in context);
                }
            }

            _mirror.Clear();
            _batchCount = 0;
            _tornDown = true;
            return ReplicationBridgeResult.Success;
        }

        public bool TryResolve(NetworkEntityHandle handle, out Entity entity)
        {
            int slot = handle.Slot;
            if (_tornDown ||
                !handle.IsValid ||
                (uint)slot >= (uint)_active.Length ||
                !_active[slot] ||
                _generations[slot] != handle.Generation ||
                !_world.IsAlive(_entities[slot]))
            {
                entity = Entity.Null;
                return false;
            }

            entity = _entities[slot];
            return true;
        }

        private ReplicationBridgeResult ValidateLocalStateAndGenerations(ReplicationPacketBuffer packet)
        {
            for (int slot = 0; slot < _active.Length; slot++)
            {
                if (!_active[slot])
                {
                    continue;
                }

                Entity entity = _entities[slot];
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
                int slot = state.Entity.Slot;
                if (!state.Entity.IsValid ||
                    state.SchemaId <= 0 ||
                    (uint)slot >= (uint)_active.Length)
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }

                if (_active[slot] && state.Entity.Generation < _generations[slot])
                {
                    return ReplicationBridgeResult.ResyncRequired;
                }

                if (_active[slot] &&
                    state.Entity.Generation == _generations[slot] &&
                    _schemas[slot] != 0 &&
                    state.SchemaId != _schemas[slot])
                {
                    return ReplicationBridgeResult.ResyncRequired;
                }

                if (packet.Header.Kind == ReplicationPacketKind.Delta &&
                    _active[slot] &&
                    state.Entity.Generation != _generations[slot] &&
                    !ContainsVisibleLeave(packet, new NetworkEntityHandle(slot, _generations[slot])))
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }
            }

            Array.Clear(_releaseSeen);
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            for (int i = 0; i < removals.Length; i++)
            {
                int slot = removals[i].Slot;
                if (!removals[i].IsValid ||
                    (uint)slot >= (uint)_active.Length ||
                    _releaseSeen[slot])
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }

                _releaseSeen[slot] = true;
            }

            ReadOnlySpan<ReplicationDisclosureChange> changes = packet.DisclosureChanges;
            for (int i = 0; i < changes.Length; i++)
            {
                ReplicationDisclosureChange change = changes[i];
                if (change.Kind != ReplicationDisclosureChangeKind.Conceal)
                {
                    continue;
                }

                int slot = change.Entity.Slot;
                if (!change.Entity.IsValid ||
                    (uint)slot >= (uint)_active.Length ||
                    _releaseSeen[slot])
                {
                    return ReplicationBridgeResult.InvalidPacket;
                }

                _releaseSeen[slot] = true;
            }

            return ReplicationBridgeResult.Success;
        }

        private void PrepareFull(ReadOnlySpan<ReplicatedEntityState> upserts)
        {
            for (int slot = 0; slot < _active.Length; slot++)
            {
                if (!_plannedActive[slot])
                {
                    continue;
                }

                int upsertIndex = FindUpsert(upserts, slot);
                if (upsertIndex < 0)
                {
                    // Full pages describe the current visible set only.
                    QueueLeave(slot, ReplicationMirrorLeaveKind.Conceal);
                }
                else if (upserts[upsertIndex].Entity.Generation != _plannedGenerations[slot])
                {
                    // A new generation permanently retires the prior network identity.
                    QueueLeave(slot, ReplicationMirrorLeaveKind.Removal);
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
                QueueLeave(removals[i].Slot, ReplicationMirrorLeaveKind.Removal);
            }

            ReadOnlySpan<ReplicationDisclosureChange> changes = packet.DisclosureChanges;
            for (int i = 0; i < changes.Length; i++)
            {
                if (changes[i].Kind == ReplicationDisclosureChangeKind.Conceal)
                {
                    QueueLeave(changes[i].Entity.Slot, ReplicationMirrorLeaveKind.Conceal);
                }
            }

            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            for (int i = 0; i < upserts.Length; i++)
            {
                QueueUpsert(in upserts[i]);
            }
        }

        private void QueueLeave(int slot, ReplicationMirrorLeaveKind leaveKind)
        {
            if (!_plannedActive[slot])
            {
                return;
            }

            AddBatchOperation(BatchOperationKind.Leave, leaveKind, slot, default);
            _plannedActive[slot] = false;
            _plannedGenerations[slot] = 0;
            _plannedSchemas[slot] = 0;
        }

        private void QueueUpsert(in ReplicatedEntityState state)
        {
            int slot = state.Entity.Slot;
            if (_plannedActive[slot] && _plannedGenerations[slot] == state.Entity.Generation)
            {
                AddBatchOperation(BatchOperationKind.Update, default, slot, in state);
                _plannedSchemas[slot] = state.SchemaId;
                return;
            }

            if (_plannedActive[slot])
            {
                // Generation replacement permanently retires the prior mirror.
                QueueLeave(slot, ReplicationMirrorLeaveKind.Removal);
            }

            AddBatchOperation(BatchOperationKind.Create, default, slot, in state);
            _plannedActive[slot] = true;
            _plannedGenerations[slot] = state.Entity.Generation;
            _plannedSchemas[slot] = state.SchemaId;
        }

        private void AddBatchOperation(
            BatchOperationKind kind,
            ReplicationMirrorLeaveKind leaveKind,
            int slot,
            in ReplicatedEntityState state)
        {
            if (_batchCount == _batchKinds.Length)
            {
                throw new InvalidOperationException("Validated replication structural batch exceeded its fixed capacity.");
            }

            _batchKinds[_batchCount] = kind;
            _batchLeaveKinds[_batchCount] = leaveKind;
            _batchSlots[_batchCount] = slot;
            _batchStates[_batchCount] = state;
            _batchCount++;
        }

        private void CommitBatch()
        {
            // All packet-driven structural changes cross this validated, fixed-capacity commit boundary.
            for (int i = 0; i < _batchCount; i++)
            {
                int slot = _batchSlots[i];
                switch (_batchKinds[i])
                {
                    case BatchOperationKind.Leave:
                        CommitLeave(slot, _batchLeaveKinds[i], in _pendingContext);
                        break;
                    case BatchOperationKind.Update:
                    {
                        ReplicatedEntityState state = _batchStates[i];
                        Entity entity = _entities[slot];
                        var identity = new ReplicationMirrorIdentity(state.Entity);
                        var mirrorState = ToMirrorState(in state);
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            throw new InvalidOperationException("Validated client replication schema applier is unavailable.");
                        }

                        applier.Apply(_world, entity, in state, in _pendingContext);
                        _world.Set(entity, in identity);
                        _world.Set(entity, in mirrorState);
                        _schemas[slot] = state.SchemaId;
                        break;
                    }
                    case BatchOperationKind.Create:
                    {
                        ReplicatedEntityState state = _batchStates[i];
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

                        _active[slot] = true;
                        _owned[slot] = true;
                        _generations[slot] = state.Entity.Generation;
                        _schemas[slot] = state.SchemaId;
                        _entities[slot] = entity;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("Unknown replication structural batch operation.");
                }
            }

            _batchCount = 0;
        }

        private void CommitLeave(
            int slot,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
        {
            Entity entity = _entities[slot];
            int schemaId = _schemas[slot];
            bool owned = _owned[slot];

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

            _active[slot] = false;
            _owned[slot] = false;
            _generations[slot] = 0;
            _schemas[slot] = 0;
            _entities[slot] = Entity.Null;
        }

        private ReplicationBridgeResult ValidateBatchApplications()
        {
            for (int i = 0; i < _batchCount; i++)
            {
                int slot = _batchSlots[i];
                switch (_batchKinds[i])
                {
                    case BatchOperationKind.Leave:
                    {
                        ReplicationBridgeResult validation = ValidateLeave(
                            slot,
                            _batchLeaveKinds[i],
                            in _pendingContext);
                        if (validation != ReplicationBridgeResult.Success)
                        {
                            return validation;
                        }

                        break;
                    }
                    case BatchOperationKind.Update:
                    {
                        ReplicatedEntityState state = _batchStates[i];
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            return ReplicationBridgeResult.SchemaNotRegistered;
                        }

                        if (!applier.CanApply(_world, _entities[slot], in state, in _pendingContext))
                        {
                            return ReplicationBridgeResult.SchemaApplyRejected;
                        }

                        break;
                    }
                    case BatchOperationKind.Create:
                    {
                        ReplicatedEntityState state = _batchStates[i];
                        if (!_appliers.TryGet(state.SchemaId, out IClientReplicationSchemaApplier applier))
                        {
                            return ReplicationBridgeResult.SchemaNotRegistered;
                        }

                        if (!applier.CanCreate(_world, in state, in _pendingContext))
                        {
                            return ReplicationBridgeResult.SchemaApplyRejected;
                        }

                        break;
                    }
                    default:
                        return ReplicationBridgeResult.InvalidPacket;
                }
            }

            return ReplicationBridgeResult.Success;
        }

        private ReplicationBridgeResult ValidateLeave(
            int slot,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context)
        {
            int schemaId = _schemas[slot];
            if (schemaId == 0)
            {
                // A borrowed binding can be torn down before its first replicated schema arrives.
                return ReplicationBridgeResult.Success;
            }

            if (!_appliers.TryGet(schemaId, out IClientReplicationSchemaApplier applier))
            {
                return ReplicationBridgeResult.SchemaNotRegistered;
            }

            return applier.CanRelease(_world, _entities[slot], leaveKind, in context)
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

        private static int FindUpsert(ReadOnlySpan<ReplicatedEntityState> upserts, int slot)
        {
            for (int i = 0; i < upserts.Length; i++)
            {
                if (upserts[i].Entity.Slot == slot)
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
