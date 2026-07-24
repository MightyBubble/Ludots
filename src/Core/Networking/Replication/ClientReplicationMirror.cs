using System;

namespace Ludots.Core.Networking.Replication
{
    public sealed class ClientReplicationMirror
    {
        private readonly ulong _sessionEpoch;
        private readonly int _globalEntityCapacity;
        private readonly FixedIntSparseMap _globalSlotToLane;
        private readonly FixedIntSparseMap _upsertSeen;
        private readonly int[] _freeLanes;
        private readonly int[] _laneGlobalSlots;
        private readonly bool[] _active;
        private readonly bool[] _releaseSeen;
        private readonly ReplicatedEntityState[] _states;
        private int _freeCount;
        private int _activeCount;
        private ulong _lastSnapshotId;

        public ClientReplicationMirror(int globalEntityCapacity, int activeMirrorCapacity, ulong sessionEpoch)
        {
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

            if (sessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            _sessionEpoch = sessionEpoch;
            _globalEntityCapacity = globalEntityCapacity;
            _globalSlotToLane = new FixedIntSparseMap(activeMirrorCapacity);
            _upsertSeen = new FixedIntSparseMap(activeMirrorCapacity);
            _freeLanes = new int[activeMirrorCapacity];
            _laneGlobalSlots = new int[activeMirrorCapacity];
            _active = new bool[activeMirrorCapacity];
            _releaseSeen = new bool[activeMirrorCapacity];
            _states = new ReplicatedEntityState[activeMirrorCapacity];
            for (int lane = 0; lane < activeMirrorCapacity; lane++)
            {
                _laneGlobalSlots[lane] = -1;
                _freeLanes[activeMirrorCapacity - lane - 1] = lane;
            }

            _freeCount = activeMirrorCapacity;
        }

        public int GlobalEntityCapacity => _globalEntityCapacity;
        public int ActiveMirrorCapacity => _active.Length;
        public int ActiveCount => _activeCount;
        public ulong LastSnapshotId => _lastSnapshotId;
        public ulong SessionEpoch => _sessionEpoch;

        public void Clear()
        {
            for (int lane = 0; lane < _active.Length; lane++)
            {
                if (_active[lane])
                {
                    ReleaseLane(lane);
                }
            }

            _globalSlotToLane.Clear();
            _upsertSeen.Clear();
            Array.Clear(_releaseSeen);
            _lastSnapshotId = 0;
        }

        public ReplicationApplyResult Apply(ReplicationPacketBuffer packet)
        {
            ReplicationApplyResult validation = Validate(packet);
            if (validation != ReplicationApplyResult.Success)
            {
                return validation;
            }

            CommitValidated(packet);
            return ReplicationApplyResult.Success;
        }

        public ReplicationApplyResult Validate(ReplicationPacketBuffer packet)
        {
            if (packet == null)
            {
                return ReplicationApplyResult.InvalidPacket;
            }

            ReplicationPacketHeader header = packet.Header;
            if (header.SessionEpoch != _sessionEpoch)
            {
                return ReplicationApplyResult.EpochMismatch;
            }

            if (header.SnapshotId == 0 ||
                (header.Kind != ReplicationPacketKind.Full && header.Kind != ReplicationPacketKind.Delta))
            {
                return ReplicationApplyResult.InvalidPacket;
            }

            if (_lastSnapshotId != 0 && header.SnapshotId <= _lastSnapshotId)
            {
                return ReplicationApplyResult.SnapshotOutOfOrder;
            }

            if (header.Kind == ReplicationPacketKind.Full)
            {
                if (header.BaselineSnapshotId != 0 || !packet.Removals.IsEmpty)
                {
                    return ReplicationApplyResult.InvalidPacket;
                }
            }
            else if (header.BaselineSnapshotId != _lastSnapshotId)
            {
                return ReplicationApplyResult.BaselineMismatch;
            }

            if (packet.EntityCapacity > ActiveMirrorCapacity)
            {
                return ReplicationApplyResult.CapacityExceeded;
            }

            _upsertSeen.Clear();
            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            for (int i = 0; i < upserts.Length; i++)
            {
                ReplicatedEntityState state = upserts[i];
                int globalSlot = state.Entity.Slot;
                if (!state.Entity.IsValid ||
                    (uint)globalSlot >= (uint)_globalEntityCapacity ||
                    !_upsertSeen.TryAdd(globalSlot, i))
                {
                    return ReplicationApplyResult.InvalidPacket;
                }
            }

            Array.Clear(_releaseSeen);
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            for (int i = 0; i < removals.Length; i++)
            {
                NetworkEntityHandle removal = removals[i];
                if (!IsCurrentEntity(removal) || Contains(removals, removal, i))
                {
                    return ReplicationApplyResult.InvalidPacket;
                }

                if (!_globalSlotToLane.TryGet(removal.Slot, out int lane) || _releaseSeen[lane])
                {
                    return ReplicationApplyResult.InvalidPacket;
                }

                _releaseSeen[lane] = true;

                for (int j = 0; j < upserts.Length; j++)
                {
                    if (upserts[j].Entity == removal)
                    {
                        return ReplicationApplyResult.InvalidPacket;
                    }
                }
            }

            ReadOnlySpan<ReplicationDisclosureChange> disclosureChanges = packet.DisclosureChanges;
            for (int i = 0; i < disclosureChanges.Length; i++)
            {
                ReplicationDisclosureChange change = disclosureChanges[i];
                if (!change.Entity.IsValid ||
                    (uint)change.Entity.Slot >= (uint)_globalEntityCapacity ||
                    (change.Kind != ReplicationDisclosureChangeKind.Reveal &&
                     change.Kind != ReplicationDisclosureChangeKind.Conceal))
                {
                    return ReplicationApplyResult.InvalidPacket;
                }

                if (change.Kind == ReplicationDisclosureChangeKind.Reveal)
                {
                    bool hasUpsert = false;
                    for (int j = 0; j < upserts.Length; j++)
                    {
                        hasUpsert |= upserts[j].Entity == change.Entity;
                    }

                    if (!hasUpsert)
                    {
                        return ReplicationApplyResult.InvalidPacket;
                    }
                }
                else if (!IsCurrentEntity(change.Entity))
                {
                    return ReplicationApplyResult.InvalidPacket;
                }
                else
                {
                    if (!_globalSlotToLane.TryGet(change.Entity.Slot, out int lane) || _releaseSeen[lane])
                    {
                        return ReplicationApplyResult.InvalidPacket;
                    }

                    _releaseSeen[lane] = true;
                }
            }

            if (header.Kind == ReplicationPacketKind.Full)
            {
                if (upserts.Length > ActiveMirrorCapacity)
                {
                    return ReplicationApplyResult.CapacityExceeded;
                }
            }
            else
            {
                int occupancy = _activeCount;
                for (int lane = 0; lane < _active.Length; lane++)
                {
                    if (_releaseSeen[lane])
                    {
                        occupancy--;
                    }
                }

                for (int i = 0; i < upserts.Length; i++)
                {
                    int globalSlot = upserts[i].Entity.Slot;
                    if (_globalSlotToLane.TryGet(globalSlot, out int lane) &&
                        _active[lane] &&
                        !_releaseSeen[lane])
                    {
                        continue;
                    }

                    occupancy++;
                }

                if (occupancy > ActiveMirrorCapacity)
                {
                    return ReplicationApplyResult.CapacityExceeded;
                }
            }

            return ReplicationApplyResult.Success;
        }

        internal void CommitValidated(ReplicationPacketBuffer packet)
        {
            ReplicationPacketHeader header = packet.Header;
            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            ReadOnlySpan<ReplicationDisclosureChange> disclosureChanges = packet.DisclosureChanges;

            if (header.Kind == ReplicationPacketKind.Full)
            {
                for (int lane = 0; lane < _active.Length; lane++)
                {
                    if (_active[lane])
                    {
                        ReleaseLane(lane);
                    }
                }
            }
            else
            {
                for (int i = 0; i < removals.Length; i++)
                {
                    if (_globalSlotToLane.TryGet(removals[i].Slot, out int lane))
                    {
                        ReleaseLane(lane);
                    }
                }

                for (int i = 0; i < disclosureChanges.Length; i++)
                {
                    ReplicationDisclosureChange change = disclosureChanges[i];
                    if (change.Kind == ReplicationDisclosureChangeKind.Conceal &&
                        _globalSlotToLane.TryGet(change.Entity.Slot, out int lane))
                    {
                        ReleaseLane(lane);
                    }
                }
            }

            for (int i = 0; i < upserts.Length; i++)
            {
                ReplicatedEntityState state = upserts[i];
                int globalSlot = state.Entity.Slot;
                if (!_globalSlotToLane.TryGet(globalSlot, out int lane))
                {
                    if (!TryAllocateLane(globalSlot, out lane))
                    {
                        throw new InvalidOperationException(
                            "Validated replication mirror commit exceeded its fixed active capacity.");
                    }
                }

                _states[lane] = state;
                if (!_active[lane])
                {
                    _active[lane] = true;
                    _activeCount++;
                }
            }

            _lastSnapshotId = header.SnapshotId;
        }

        public bool TryGet(NetworkEntityHandle entity, out ReplicatedEntityState state)
        {
            if (!entity.IsValid ||
                (uint)entity.Slot >= (uint)_globalEntityCapacity ||
                !_globalSlotToLane.TryGet(entity.Slot, out int lane) ||
                !_active[lane] ||
                _states[lane].Entity != entity)
            {
                state = default;
                return false;
            }

            state = _states[lane];
            return true;
        }

        private bool IsCurrentEntity(NetworkEntityHandle entity)
            => entity.IsValid &&
               (uint)entity.Slot < (uint)_globalEntityCapacity &&
               _globalSlotToLane.TryGet(entity.Slot, out int lane) &&
               _active[lane] &&
               _states[lane].Entity == entity;

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

            _states[lane] = default;
            _laneGlobalSlots[lane] = -1;
            _freeLanes[_freeCount++] = lane;
        }

        private static bool Contains(ReadOnlySpan<NetworkEntityHandle> removals, NetworkEntityHandle entity, int beforeIndex)
        {
            for (int i = 0; i < beforeIndex; i++)
            {
                if (removals[i] == entity)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
