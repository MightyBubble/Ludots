using System;

namespace Ludots.Core.Networking.Replication
{
    public sealed class ClientReplicationMirror
    {
        private readonly ulong _sessionEpoch;
        private readonly bool[] _active;
        private readonly bool[] _seen;
        private readonly ReplicatedEntityState[] _states;
        private ulong _lastSnapshotId;

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
            _seen = new bool[entityCapacity];
            _states = new ReplicatedEntityState[entityCapacity];
        }

        public int EntityCapacity => _active.Length;
        public ulong LastSnapshotId => _lastSnapshotId;
        public ulong SessionEpoch => _sessionEpoch;

        public void Clear()
        {
            Array.Clear(_active);
            Array.Clear(_seen);
            Array.Clear(_states);
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
                Array.Clear(_active);
            }

            else
            {
                for (int i = 0; i < removals.Length; i++)
                {
                    _active[removals[i].Slot] = false;
                }

                for (int i = 0; i < disclosureChanges.Length; i++)
                {
                    ReplicationDisclosureChange change = disclosureChanges[i];
                    if (change.Kind == ReplicationDisclosureChangeKind.Conceal)
                    {
                        _active[change.Entity.Slot] = false;
                    }
                }
            }

            for (int i = 0; i < upserts.Length; i++)
            {
                ReplicatedEntityState state = upserts[i];
                int slot = state.Entity.Slot;
                _states[slot] = state;
                _active[slot] = true;
            }

            _lastSnapshotId = header.SnapshotId;
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
