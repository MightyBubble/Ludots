using System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;

namespace Ludots.Core.Networking.Commands
{
    public readonly struct NetworkCommandSchema
    {
        public NetworkCommandSchema(
            int orderTypeId,
            NetworkCommandTargetKind targetKind,
            bool allowArg0,
            bool allowArg1,
            OrderSubmitMode submitMode,
            KnowledgePositionAccess requiredTargetPositionAccess)
        {
            if (orderTypeId <= 0 || orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                throw new ArgumentOutOfRangeException(nameof(orderTypeId));
            }

            if ((uint)targetKind > (uint)NetworkCommandTargetKind.WorldPositionAndEntity)
            {
                throw new ArgumentOutOfRangeException(nameof(targetKind));
            }

            if ((uint)submitMode > (uint)OrderSubmitMode.Queued)
            {
                throw new ArgumentOutOfRangeException(nameof(submitMode));
            }

            if ((uint)requiredTargetPositionAccess > (uint)KnowledgePositionAccess.Live)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredTargetPositionAccess));
            }

            bool hasEntityTarget = targetKind is NetworkCommandTargetKind.NetworkEntity or
                NetworkCommandTargetKind.WorldPositionAndEntity;
            if (!hasEntityTarget && requiredTargetPositionAccess != KnowledgePositionAccess.None)
            {
                throw new ArgumentException(
                    "A command without an entity target cannot require entity position knowledge.",
                    nameof(requiredTargetPositionAccess));
            }

            OrderTypeId = orderTypeId;
            TargetKind = targetKind;
            AllowArg0 = allowArg0;
            AllowArg1 = allowArg1;
            SubmitMode = submitMode;
            RequiredTargetPositionAccess = requiredTargetPositionAccess;
        }

        public int OrderTypeId { get; }
        public NetworkCommandTargetKind TargetKind { get; }
        public bool AllowArg0 { get; }
        public bool AllowArg1 { get; }
        public OrderSubmitMode SubmitMode { get; }
        public KnowledgePositionAccess RequiredTargetPositionAccess { get; }
    }

    /// <summary>
    /// Startup-built, fixed command exposure table. Only explicitly registered order types can cross the network.
    /// </summary>
    public sealed class NetworkCommandSchemaRegistry
    {
        private readonly NetworkCommandSchema[] _schemas = new NetworkCommandSchema[OrderTypeRegistry.MaxOrderTypes];
        private readonly ulong[] _registered = new ulong[OrderTypeRegistry.MaxOrderTypes >> 6];
        private bool _frozen;

        public bool IsFrozen => _frozen;

        public void Register(in NetworkCommandSchema schema)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("Network command schemas are frozen.");
            }

            int orderTypeId = schema.OrderTypeId;
            int word = orderTypeId >> 6;
            ulong mask = 1UL << (orderTypeId & 63);
            if ((_registered[word] & mask) != 0)
            {
                throw new InvalidOperationException(
                    $"Network command schema for order type {orderTypeId} is already registered.");
            }

            _schemas[orderTypeId] = schema;
            _registered[word] |= mask;
        }

        public void Freeze()
        {
            _frozen = true;
        }

        public bool TryGet(int orderTypeId, out NetworkCommandSchema schema)
        {
            if ((uint)orderTypeId >= OrderTypeRegistry.MaxOrderTypes)
            {
                schema = default;
                return false;
            }

            int word = orderTypeId >> 6;
            ulong mask = 1UL << (orderTypeId & 63);
            if ((_registered[word] & mask) == 0)
            {
                schema = default;
                return false;
            }

            schema = _schemas[orderTypeId];
            return true;
        }
    }
}
