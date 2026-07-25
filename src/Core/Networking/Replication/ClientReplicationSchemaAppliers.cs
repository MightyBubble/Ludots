using Arch.Core;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.Replication
{
    /// <summary>
    /// Why a client mirror is leaving the client replication bridge.
    /// </summary>
    public enum ReplicationMirrorLeaveKind : byte
    {
        Conceal = 1,
        Removal = 2,
        Teardown = 3,
    }

    /// <summary>
    /// Context passed through every validated client replication lifecycle step.
    /// Snapshot fields are zero/default only for explicit bridge teardown outside packet apply.
    /// </summary>
    public readonly struct ReplicationApplyContext
    {
        public ReplicationApplyContext(
            in SessionSeatBinding clientSeat,
            ulong sessionEpoch,
            uint committedTick,
            ulong snapshotId,
            ReplicationPacketKind packetKind)
        {
            if (!clientSeat.IsValid)
            {
                throw new System.ArgumentException("Replication apply requires the accepted client seat.", nameof(clientSeat));
            }

            if (sessionEpoch == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            ClientSeat = clientSeat;
            SessionEpoch = sessionEpoch;
            CommittedTick = committedTick;
            SnapshotId = snapshotId;
            PacketKind = packetKind;
        }

        public SessionSeatBinding ClientSeat { get; }
        public ulong SessionEpoch { get; }
        public uint CommittedTick { get; }
        public ulong SnapshotId { get; }
        public ReplicationPacketKind PacketKind { get; }
    }

    public interface IClientReplicationSchemaApplier
    {
        bool CanCreate(World world, in ReplicatedEntityState state, in ReplicationApplyContext context);

        bool CanApply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context);

        bool CanRelease(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context);

        Entity Create(
            World world,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state,
            in ReplicationApplyContext context);

        void Apply(World world, Entity entity, in ReplicatedEntityState state, in ReplicationApplyContext context);

        /// <summary>
        /// Releases physics and other external resources for both owned and borrowed mirrors.
        /// The reason distinguishes visibility loss, permanent removal, and epoch teardown.
        /// </summary>
        void Release(
            World world,
            Entity entity,
            ReplicationMirrorLeaveKind leaveKind,
            in ReplicationApplyContext context);
    }

    /// <summary>
    /// Optional fixed-capacity batch boundary for schema appliers whose invariants span more
    /// than one replicated entity. Validation must not mutate committed runtime state.
    /// </summary>
    public interface IClientReplicationBatchValidationParticipant
    {
        void OnBatchValidationBeginning(in ReplicationApplyContext context);

        bool CanCommitBatchValidation();

        void OnBatchCommitBeginning();

        void OnBatchEnded(bool committed);
    }

    public sealed class ClientReplicationSchemaApplierRegistry
    {
        private readonly FrozenReplicationSchemaRegistry<IClientReplicationSchemaApplier> _registry;

        public ClientReplicationSchemaApplierRegistry(int schemaCapacity)
        {
            _registry = new FrozenReplicationSchemaRegistry<IClientReplicationSchemaApplier>(schemaCapacity);
        }

        public int SchemaCapacity => _registry.SchemaCapacity;
        public bool IsFrozen => _registry.IsFrozen;

        public ReplicationSchemaRegistrationResult Register(int schemaId, IClientReplicationSchemaApplier applier)
        {
            return _registry.Register(schemaId, applier);
        }

        public void Freeze()
        {
            _registry.Freeze();
        }

        public bool HasAnyRegistered() => _registry.HasAnyHandler();

        public bool TryGet(int schemaId, out IClientReplicationSchemaApplier applier)
        {
            return _registry.TryGet(schemaId, out applier);
        }

        public void NotifyBatchValidationBeginning(in ReplicationApplyContext context)
        {
            for (int schemaId = 1; schemaId <= SchemaCapacity; schemaId++)
            {
                if (TryGet(schemaId, out IClientReplicationSchemaApplier applier) &&
                    applier is IClientReplicationBatchValidationParticipant participant)
                {
                    participant.OnBatchValidationBeginning(in context);
                }
            }
        }

        public bool CanCommitBatchValidation()
        {
            for (int schemaId = 1; schemaId <= SchemaCapacity; schemaId++)
            {
                if (TryGet(schemaId, out IClientReplicationSchemaApplier applier) &&
                    applier is IClientReplicationBatchValidationParticipant participant &&
                    !participant.CanCommitBatchValidation())
                {
                    return false;
                }
            }

            return true;
        }

        public void NotifyBatchCommitBeginning()
        {
            for (int schemaId = 1; schemaId <= SchemaCapacity; schemaId++)
            {
                if (TryGet(schemaId, out IClientReplicationSchemaApplier applier) &&
                    applier is IClientReplicationBatchValidationParticipant participant)
                {
                    participant.OnBatchCommitBeginning();
                }
            }
        }

        public void NotifyBatchEnded(bool committed)
        {
            for (int schemaId = 1; schemaId <= SchemaCapacity; schemaId++)
            {
                if (TryGet(schemaId, out IClientReplicationSchemaApplier applier) &&
                    applier is IClientReplicationBatchValidationParticipant participant)
                {
                    participant.OnBatchEnded(committed);
                }
            }
        }
    }
}
