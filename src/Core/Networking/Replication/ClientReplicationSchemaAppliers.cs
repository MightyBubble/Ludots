using Arch.Core;

namespace Ludots.Core.Networking.Replication
{
    public interface IClientReplicationSchemaApplier
    {
        bool CanCreate(World world, in ReplicatedEntityState state);

        bool CanApply(World world, Entity entity, in ReplicatedEntityState state);

        bool CanConceal(World world, Entity entity);

        Entity Create(
            World world,
            in ReplicationMirrorIdentity identity,
            in ReplicationMirrorState state);

        void Apply(World world, Entity entity, in ReplicatedEntityState state);

        void Conceal(World world, Entity entity);
    }

    public sealed class ClientReplicationSchemaApplierRegistry
    {
        private readonly FrozenReplicationSchemaRegistry<IClientReplicationSchemaApplier> _registry;

        public ClientReplicationSchemaApplierRegistry(int schemaCapacity)
        {
            _registry = new FrozenReplicationSchemaRegistry<IClientReplicationSchemaApplier>(schemaCapacity);
        }

        public int SchemaCapacity => _registry.SchemaCapacity;
        public int Count => _registry.Count;
        public bool IsFrozen => _registry.IsFrozen;

        public ReplicationSchemaRegistrationResult Register(int schemaId, IClientReplicationSchemaApplier applier)
        {
            return _registry.Register(schemaId, applier);
        }

        public void Freeze()
        {
            _registry.Freeze();
        }

        public bool TryGet(int schemaId, out IClientReplicationSchemaApplier applier)
        {
            return _registry.TryGet(schemaId, out applier);
        }
    }
}
