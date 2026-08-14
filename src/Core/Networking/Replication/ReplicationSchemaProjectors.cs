using System;
using Arch.Core;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Networking.Replication
{
    public readonly struct ReplicationSchemaRef
    {
        public ReplicationSchemaRef(int schemaId)
        {
            if (schemaId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaId));
            }

            SchemaId = schemaId;
        }

        public int SchemaId { get; }
    }

    public readonly struct ReplicationProjectedState
    {
        public ReplicationProjectedState(uint revision, in ReplicationStateVector values)
        {
            Revision = revision;
            Values = values;
        }

        public uint Revision { get; }
        public ReplicationStateVector Values { get; }
    }

    public interface IReplicationSchemaProjector
    {
        bool TryProject(
            World world,
            Entity entity,
            in KnowledgeDisclosureRecord disclosure,
            out ReplicationProjectedState state);
    }

    public enum ReplicationSchemaRegistrationResult : byte
    {
        Success = 0,
        InvalidSchema = 1,
        InvalidHandler = 2,
        DuplicateSchema = 3,
        CapacityExceeded = 4,
        RegistryFrozen = 5,
    }

    public sealed class ReplicationSchemaProjectorRegistry
    {
        private readonly FrozenReplicationSchemaRegistry<IReplicationSchemaProjector> _registry;

        public ReplicationSchemaProjectorRegistry(int schemaCapacity)
        {
            _registry = new FrozenReplicationSchemaRegistry<IReplicationSchemaProjector>(schemaCapacity);
        }

        public int SchemaCapacity => _registry.SchemaCapacity;
        public int Count => _registry.Count;
        public bool IsFrozen => _registry.IsFrozen;

        public ReplicationSchemaRegistrationResult Register(int schemaId, IReplicationSchemaProjector projector)
        {
            return _registry.Register(schemaId, projector);
        }

        public void Freeze()
        {
            _registry.Freeze();
        }

        public bool TryGet(int schemaId, out IReplicationSchemaProjector projector)
        {
            return _registry.TryGet(schemaId, out projector);
        }
    }
}
