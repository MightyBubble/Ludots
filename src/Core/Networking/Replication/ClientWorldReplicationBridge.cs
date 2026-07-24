using System;
using Arch.Core;

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
        public ReplicationMirrorState(int schemaId, uint revision, in ReplicationStateVector values)
        {
            SchemaId = schemaId;
            Revision = revision;
            Values = values;
        }

        public int SchemaId { get; }
        public uint Revision { get; }
        public ReplicationStateVector Values { get; }
    }

    public sealed class ClientWorldReplicationBridge
    {
        private enum BatchOperationKind : byte
        {
            Release = 1,
            Update = 2,
            Create = 3,
        }

        private readonly World _world;
        private readonly ClientReplicationMirror _mirror;
        private readonly ClientReplicationSchemaApplierRegistry _appliers;
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
        private readonly int[] _batchSlots;
        private readonly ReplicatedEntityState[] _batchStates;
        private int _batchCount;

        public ClientWorldReplicationBridge(
            World world,
            int entityCapacity,
            ulong sessionEpoch,
            ClientReplicationSchemaApplierRegistry appliers)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _appliers = appliers ?? throw new ArgumentNullException(nameof(appliers));
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            if (!appliers.IsFrozen)
            {
                throw new InvalidOperationException("Client replication schema applier registry must be frozen before bridge construction.");
            }

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
            _batchSlots = new int[batchCapacity];
            _batchStates = new ReplicatedEntityState[batchCapacity];
        }

        public int EntityCapacity => _active.Length;
        public ulong LastSnapshotId => _mirror.LastSnapshotId;

        public ReplicationBridgeResult BindExisting(NetworkEntityHandle handle, Entity entity)
        {
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
            if (packet == null)
            {
                return ReplicationBridgeResult.InvalidPacket;
            }

            if (packet.EntityCapacity > _active.Length)
            {
                return ReplicationBridgeResult.CapacityContractViolated;
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

            ReplicationApplyResult applied = _mirror.Apply(packet);
            if (applied != ReplicationApplyResult.Success)
            {
                _batchCount = 0;
                return ReplicationBridgeResultMapper.FromApply(applied);
            }

            CommitBatch();
            return ReplicationBridgeResult.Success;
        }

        public bool TryResolve(NetworkEntityHandle handle, out Entity entity)
        {
            int slot = handle.Slot;
            if (!handle.IsValid ||
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

        public ReplicationBridgeResult Clear()
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

                int schemaId = _schemas[slot];
                if (!_owned[slot] && schemaId > 0)
                {
                    if (!_appliers.TryGet(schemaId, out IClientReplicationSchemaApplier applier))
                    {
                        return ReplicationBridgeResult.SchemaNotRegistered;
                    }

                    if (!applier.CanConceal(_world, entity))
                    {
                        return ReplicationBridgeResult.SchemaApplyRejected;
                    }
                }
            }

            for (int slot = 0; slot < _active.Length; slot++)
            {
                if (_active[slot])
                {
                    ReleaseSlot(slot);
                }
            }

            _batchCount = 0;
            return ReplicationBridgeResult.Success;
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
                    !ContainsRelease(packet, new NetworkEntityHandle(slot, _generations[slot])))
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
                if (upsertIndex < 0 || upserts[upsertIndex].Entity.Generation != _plannedGenerations[slot])
                {
                    QueueRelease(slot);
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
                QueueRelease(removals[i].Slot);
            }

            ReadOnlySpan<ReplicationDisclosureChange> changes = packet.DisclosureChanges;
            for (int i = 0; i < changes.Length; i++)
            {
                if (changes[i].Kind == ReplicationDisclosureChangeKind.Conceal)
                {
                    QueueRelease(changes[i].Entity.Slot);
                }
            }

            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            for (int i = 0; i < upserts.Length; i++)
            {
                QueueUpsert(in upserts[i]);
            }
        }

        private void QueueRelease(int slot)
        {
            if (!_plannedActive[slot])
            {
                return;
            }

            AddBatchOperation(BatchOperationKind.Release, slot, default);
            _plannedActive[slot] = false;
            _plannedGenerations[slot] = 0;
            _plannedSchemas[slot] = 0;
        }

        private void QueueUpsert(in ReplicatedEntityState state)
        {
            int slot = state.Entity.Slot;
            if (_plannedActive[slot] && _plannedGenerations[slot] == state.Entity.Generation)
            {
                AddBatchOperation(BatchOperationKind.Update, slot, in state);
                _plannedSchemas[slot] = state.SchemaId;
                return;
            }

            if (_plannedActive[slot])
            {
                QueueRelease(slot);
            }

            AddBatchOperation(BatchOperationKind.Create, slot, in state);
            _plannedActive[slot] = true;
            _plannedGenerations[slot] = state.Entity.Generation;
            _plannedSchemas[slot] = state.SchemaId;
        }

        private void AddBatchOperation(BatchOperationKind kind, int slot, in ReplicatedEntityState state)
        {
            if (_batchCount == _batchKinds.Length)
            {
                throw new InvalidOperationException("Validated replication structural batch exceeded its fixed capacity.");
            }

            _batchKinds[_batchCount] = kind;
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
                    case BatchOperationKind.Release:
                    {
                        ReleaseSlot(slot);
                        break;
                    }
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

                        applier.Apply(_world, entity, in state);
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

                        Entity entity = applier.Create(_world, in identity, in mirrorState);
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

        private void ReleaseSlot(int slot)
        {
            Entity entity = _entities[slot];
            if (_owned[slot])
            {
                _world.Destroy(entity);
            }
            else
            {
                int schemaId = _schemas[slot];
                if (schemaId > 0)
                {
                    if (!_appliers.TryGet(schemaId, out IClientReplicationSchemaApplier applier))
                    {
                        throw new InvalidOperationException("Validated client replication schema applier is unavailable.");
                    }

                    applier.Conceal(_world, entity);
                }

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
                    case BatchOperationKind.Release:
                    {
                        int schemaId = _schemas[slot];
                        if (!_owned[slot] && schemaId > 0)
                        {
                            if (!_appliers.TryGet(schemaId, out IClientReplicationSchemaApplier applier))
                            {
                                return ReplicationBridgeResult.SchemaNotRegistered;
                            }

                            if (!applier.CanConceal(_world, _entities[slot]))
                            {
                                return ReplicationBridgeResult.SchemaApplyRejected;
                            }
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

                        if (!applier.CanApply(_world, _entities[slot], in state))
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

                        if (!applier.CanCreate(_world, in state))
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

        private void CommitExistingBinding(
            Entity entity,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state)
        {
            // Loading-time authored bindings use the same explicit structural boundary as packet application.
            _world.Add(entity, in identity, in state);
        }

        private static bool ContainsRelease(ReplicationPacketBuffer packet, NetworkEntityHandle entity)
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
            => new(state.SchemaId, state.Revision, state.Values);
    }
}
