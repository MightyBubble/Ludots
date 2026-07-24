using System;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Networking.Replication
{
    public enum ReplicationPacketKind : byte
    {
        Full = 1,
        Delta = 2,
    }

    public enum ReplicationBuildResult : byte
    {
        Success = 0,
        InvalidInput = 1,
        EpochMismatch = 2,
        SnapshotOutOfOrder = 3,
        BaselineUnavailable = 4,
        PacketCapacityExceeded = 5,
        DisclosureLogCapacityExceeded = 6,
    }

    public enum ReplicationApplyResult : byte
    {
        Success = 0,
        InvalidPacket = 1,
        EpochMismatch = 2,
        BaselineMismatch = 3,
        SnapshotOutOfOrder = 4,
        CapacityExceeded = 5,
    }

    public enum ReplicationDisclosureChangeKind : byte
    {
        Reveal = 1,
        Conceal = 2,
    }

    public readonly struct ReplicationStateVector : IEquatable<ReplicationStateVector>
    {
        public ReplicationStateVector(long value0, long value1, long value2, long value3)
        {
            Value0 = value0;
            Value1 = value1;
            Value2 = value2;
            Value3 = value3;
        }

        public long Value0 { get; }
        public long Value1 { get; }
        public long Value2 { get; }
        public long Value3 { get; }

        public bool Equals(ReplicationStateVector other)
            => Value0 == other.Value0 &&
               Value1 == other.Value1 &&
               Value2 == other.Value2 &&
               Value3 == other.Value3;

        public override bool Equals(object? obj)
            => obj is ReplicationStateVector other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Value0, Value1, Value2, Value3);

        public static bool operator ==(ReplicationStateVector left, ReplicationStateVector right) => left.Equals(right);
        public static bool operator !=(ReplicationStateVector left, ReplicationStateVector right) => !left.Equals(right);
    }

    public readonly struct ReplicatedEntityState : IEquatable<ReplicatedEntityState>
    {
        public ReplicatedEntityState(
            NetworkEntityHandle entity,
            int schemaId,
            uint revision,
            in ReplicationStateVector values)
        {
            if (!entity.IsValid)
            {
                throw new ArgumentException("Replicated state requires a valid network entity handle.", nameof(entity));
            }

            if (schemaId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaId));
            }

            Entity = entity;
            SchemaId = schemaId;
            Revision = revision;
            Values = values;
        }

        public NetworkEntityHandle Entity { get; }
        public int SchemaId { get; }
        public uint Revision { get; }
        public ReplicationStateVector Values { get; }

        public bool Equals(ReplicatedEntityState other)
            => Entity == other.Entity &&
               SchemaId == other.SchemaId &&
               Revision == other.Revision &&
               Values == other.Values;

        public override bool Equals(object? obj)
            => obj is ReplicatedEntityState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Entity, SchemaId, Revision, Values);

        public static bool operator ==(ReplicatedEntityState left, ReplicatedEntityState right) => left.Equals(right);
        public static bool operator !=(ReplicatedEntityState left, ReplicatedEntityState right) => !left.Equals(right);
    }

    public readonly struct ReplicationDisclosureInput
    {
        public ReplicationDisclosureInput(NetworkEntityHandle entity, KnowledgePresence presence)
        {
            if (!entity.IsValid)
            {
                throw new ArgumentException("Replication disclosure requires a valid network entity handle.", nameof(entity));
            }

            if ((uint)presence > (uint)KnowledgePresence.HiddenWithSource)
            {
                throw new ArgumentOutOfRangeException(nameof(presence));
            }

            Entity = entity;
            Presence = presence;
        }

        public NetworkEntityHandle Entity { get; }
        public KnowledgePresence Presence { get; }

        public bool CanReplicateLiveState => Presence == KnowledgePresence.LiveVisible;
    }

    public readonly struct ReplicationDisclosureChange
    {
        public ReplicationDisclosureChange(
            ulong sequence,
            ulong snapshotId,
            NetworkEntityHandle entity,
            ReplicationDisclosureChangeKind kind)
        {
            Sequence = sequence;
            SnapshotId = snapshotId;
            Entity = entity;
            Kind = kind;
        }

        public ulong Sequence { get; }
        public ulong SnapshotId { get; }
        public NetworkEntityHandle Entity { get; }
        public ReplicationDisclosureChangeKind Kind { get; }
    }

    public readonly struct ReplicationPacketHeader
    {
        public ReplicationPacketHeader(
            ReplicationPacketKind kind,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong baselineSnapshotId)
        {
            Kind = kind;
            SessionEpoch = sessionEpoch;
            Tick = tick;
            SnapshotId = snapshotId;
            BaselineSnapshotId = baselineSnapshotId;
        }

        public ReplicationPacketKind Kind { get; }
        public ulong SessionEpoch { get; }
        public uint Tick { get; }
        public ulong SnapshotId { get; }
        public ulong BaselineSnapshotId { get; }
    }

    public sealed class ReplicationPacketBuffer
    {
        private readonly ReplicatedEntityState[] _upserts;
        private readonly NetworkEntityHandle[] _removals;
        private readonly ReplicationDisclosureChange[] _disclosureChanges;
        private int _upsertCount;
        private int _removalCount;
        private int _disclosureChangeCount;

        public ReplicationPacketBuffer(int entityCapacity)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            if (entityCapacity > ushort.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entityCapacity),
                    "Entity capacity must leave room for both conceal and reveal changes in one packet.");
            }

            _upserts = new ReplicatedEntityState[entityCapacity];
            _removals = new NetworkEntityHandle[entityCapacity];
            _disclosureChanges = new ReplicationDisclosureChange[checked(entityCapacity * 2)];
        }

        public ReplicationPacketHeader Header { get; private set; }
        public int EntityCapacity => _upserts.Length;
        public int DisclosureCapacity => _disclosureChanges.Length;
        public int UpsertCount => _upsertCount;
        public int RemovalCount => _removalCount;
        public int DisclosureChangeCount => _disclosureChangeCount;
        public ReadOnlySpan<ReplicatedEntityState> Upserts => _upserts.AsSpan(0, _upsertCount);
        public ReadOnlySpan<NetworkEntityHandle> Removals => _removals.AsSpan(0, _removalCount);
        public ReadOnlySpan<ReplicationDisclosureChange> DisclosureChanges => _disclosureChanges.AsSpan(0, _disclosureChangeCount);

        internal void Reset(in ReplicationPacketHeader header)
        {
            Header = header;
            _upsertCount = 0;
            _removalCount = 0;
            _disclosureChangeCount = 0;
        }

        internal void AddUpsert(in ReplicatedEntityState state) => _upserts[_upsertCount++] = state;
        internal void AddRemoval(NetworkEntityHandle entity) => _removals[_removalCount++] = entity;
        internal void AddDisclosureChange(in ReplicationDisclosureChange change) => _disclosureChanges[_disclosureChangeCount++] = change;

        /// <summary>
        /// Capacity-checked write used by wire codecs. Returns false without mutating when full.
        /// </summary>
        internal bool TryAddUpsert(in ReplicatedEntityState state)
        {
            if (_upsertCount >= _upserts.Length)
            {
                return false;
            }

            _upserts[_upsertCount++] = state;
            return true;
        }

        internal bool TryAddRemoval(NetworkEntityHandle entity)
        {
            if (_removalCount >= _removals.Length)
            {
                return false;
            }

            _removals[_removalCount++] = entity;
            return true;
        }

        internal bool TryAddDisclosureChange(in ReplicationDisclosureChange change)
        {
            if (_disclosureChangeCount >= _disclosureChanges.Length)
            {
                return false;
            }

            _disclosureChanges[_disclosureChangeCount++] = change;
            return true;
        }
    }
}
