using System;

namespace Ludots.Core.Networking.Replication
{
    internal sealed class FrozenReplicationSchemaRegistry<THandler>
        where THandler : class
    {
        private readonly THandler?[] _handlers;

        public FrozenReplicationSchemaRegistry(int schemaCapacity)
        {
            if (schemaCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaCapacity));
            }

            _handlers = new THandler[checked(schemaCapacity + 1)];
        }

        public int SchemaCapacity => _handlers.Length - 1;
        public bool IsFrozen { get; private set; }

        public ReplicationSchemaRegistrationResult Register(int schemaId, THandler handler)
        {
            if (IsFrozen)
            {
                return ReplicationSchemaRegistrationResult.RegistryFrozen;
            }

            if (schemaId <= 0)
            {
                return ReplicationSchemaRegistrationResult.InvalidSchema;
            }

            if (handler == null)
            {
                return ReplicationSchemaRegistrationResult.InvalidHandler;
            }

            if ((uint)schemaId >= (uint)_handlers.Length)
            {
                return ReplicationSchemaRegistrationResult.CapacityExceeded;
            }

            if (_handlers[schemaId] != null)
            {
                return ReplicationSchemaRegistrationResult.DuplicateSchema;
            }

            _handlers[schemaId] = handler;
            return ReplicationSchemaRegistrationResult.Success;
        }

        public void Freeze()
        {
            IsFrozen = true;
        }

        public bool HasAnyHandler()
        {
            for (int schemaId = 1; schemaId < _handlers.Length; schemaId++)
            {
                if (_handlers[schemaId] != null)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGet(int schemaId, out THandler handler)
        {
            if (!IsFrozen ||
                schemaId <= 0 ||
                (uint)schemaId >= (uint)_handlers.Length ||
                _handlers[schemaId] == null)
            {
                handler = null!;
                return false;
            }

            handler = _handlers[schemaId]!;
            return true;
        }
    }
}
