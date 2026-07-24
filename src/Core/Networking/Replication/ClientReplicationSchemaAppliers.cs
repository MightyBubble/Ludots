using Arch.Core;

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
            ulong sessionEpoch,
            uint committedTick,
            ulong snapshotId,
            ReplicationPacketKind packetKind)
        {
            if (sessionEpoch == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(sessionEpoch));
            }

            SessionEpoch = sessionEpoch;
            CommittedTick = committedTick;
            SnapshotId = snapshotId;
            PacketKind = packetKind;
        }

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
    }
}
