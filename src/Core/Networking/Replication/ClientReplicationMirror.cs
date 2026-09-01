using System;

namespace Ludots.Core.Networking.Replication
{
    public sealed class ClientReplicationMirror
    {
        private readonly ulong _sessionEpoch;
        private readonly bool[] _active;
        private readonly bool[] _plannedActive;
        private readonly bool[] _seen;
        private readonly ReplicatedEntityState[] _states;
        private readonly ReplicatedEntityState[] _plannedStates;
        private ulong _lastSnapshotId;
        private ulong _preparedSnapshotId;
        private bool _hasPreparedSnapshot;

        public ClientReplicationMirror(int entityCapacity, ulong sessionEpoch)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            if (sessionEpoch == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            _sessionEpoch = sessionEpoch;
            _active = new bool[entityCapacity];
            _plannedActive = new bool[entityCapacity];
            _seen = new bool[entityCapacity];
            _states = new ReplicatedEntityState[entityCapacity];
            _plannedStates = new ReplicatedEntityState[entityCapacity];
        }

        public int EntityCapacity => _active.Length;
        public ulong LastSnapshotId => _lastSnapshotId;
        public bool HasPreparedSnapshot => _hasPreparedSnapshot;

        public ReplicationApplyResult Apply(ReplicationPacketBuffer packet)
        {
            ReplicationApplyResult prepared = Prepare(packet);
            return prepared == ReplicationApplyResult.Success
                ? CommitPrepared()
                : prepared;
        }

        public ReplicationApplyResult Prepare(ReplicationPacketBuffer packet)
        {
            if (packet == null || _hasPreparedSnapshot)
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

            Array.Clear(_seen);
            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            for (int i = 0; i < upserts.Length; i++)
            {
                ReplicatedEntityState state = upserts[i];
                int slot = state.Entity.Slot;
                if ((uint)slot >= (uint)_active.Length || _seen[slot])
                {
                    return ReplicationApplyResult.InvalidPacket;
                }

                _seen[slot] = true;
            }

            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            for (int i = 0; i < removals.Length; i++)
            {
                NetworkEntityHandle removal = removals[i];
                if (!IsCurrentEntity(removal) || Contains(removals, removal, i))
                {
                    return ReplicationApplyResult.InvalidPacket;
                }

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
                    (uint)change.Entity.Slot >= (uint)_active.Length ||
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
            }

            Array.Copy(_active, _plannedActive, _active.Length);
            Array.Copy(_states, _plannedStates, _states.Length);

            if (header.Kind == ReplicationPacketKind.Full)
            {
                Array.Clear(_plannedActive);
            }

            else
            {
                for (int i = 0; i < removals.Length; i++)
                {
                    _plannedActive[removals[i].Slot] = false;
                }

                for (int i = 0; i < disclosureChanges.Length; i++)
                {
                    ReplicationDisclosureChange change = disclosureChanges[i];
                    if (change.Kind == ReplicationDisclosureChangeKind.Conceal)
                    {
                        _plannedActive[change.Entity.Slot] = false;
                    }
                }
            }

            for (int i = 0; i < upserts.Length; i++)
            {
                ReplicatedEntityState state = upserts[i];
                int slot = state.Entity.Slot;
                _plannedStates[slot] = state;
                _plannedActive[slot] = true;
            }

            _preparedSnapshotId = header.SnapshotId;
            _hasPreparedSnapshot = true;
            return ReplicationApplyResult.Success;
        }

        public ReplicationApplyResult CommitPrepared()
        {
            if (!_hasPreparedSnapshot)
            {
                return ReplicationApplyResult.InvalidPacket;
            }

            Array.Copy(_plannedActive, _active, _active.Length);
            Array.Copy(_plannedStates, _states, _states.Length);
            _lastSnapshotId = _preparedSnapshotId;
            DiscardPrepared();
            return ReplicationApplyResult.Success;
        }

        public void DiscardPrepared()
        {
            _preparedSnapshotId = 0;
            _hasPreparedSnapshot = false;
        }

        public bool TryGet(NetworkEntityHandle entity, out ReplicatedEntityState state)
        {
            if (!entity.IsValid ||
                (uint)entity.Slot >= (uint)_active.Length ||
                !_active[entity.Slot] ||
                _states[entity.Slot].Entity != entity)
            {
                state = default;
                return false;
            }

            state = _states[entity.Slot];
            return true;
        }

        private bool IsCurrentEntity(NetworkEntityHandle entity)
            => entity.IsValid &&
               (uint)entity.Slot < (uint)_active.Length &&
               _active[entity.Slot] &&
               _states[entity.Slot].Entity == entity;

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
