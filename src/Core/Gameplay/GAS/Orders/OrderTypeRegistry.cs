using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public sealed class OrderTypeRegistry
    {
        public const int MaxOrderTypes = 256;

        private readonly OrderTypeConfig?[] _configs = new OrderTypeConfig?[MaxOrderTypes];
        private readonly ulong[] _hasBits = new ulong[MaxOrderTypes >> 6];

        public void Clear()
        {
            Array.Clear(_configs, 0, _configs.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
        }

        public void Register(OrderTypeConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if ((uint)config.OrderTypeId >= MaxOrderTypes)
            {
                throw new ArgumentOutOfRangeException(nameof(config), $"OrderTypeId {config.OrderTypeId} exceeds max {MaxOrderTypes}.");
            }

            _configs[config.OrderTypeId] = config;
            int word = config.OrderTypeId >> 6;
            int bit = config.OrderTypeId & 63;
            _hasBits[word] |= 1UL << bit;
        }

        public void RegisterAll(IEnumerable<OrderTypeConfig> configs)
        {
            foreach (var config in configs)
            {
                Register(config);
            }
        }

        public bool TryGet(int orderTypeId, out OrderTypeConfig config)
        {
            if ((uint)orderTypeId >= MaxOrderTypes)
            {
                config = default!;
                return false;
            }

            int word = orderTypeId >> 6;
            int bit = orderTypeId & 63;
            if ((_hasBits[word] & (1UL << bit)) == 0)
            {
                config = default!;
                return false;
            }

            config = _configs[orderTypeId]!;
            return true;
        }

        public OrderTypeConfig Get(int orderTypeId)
        {
            if (!TryGet(orderTypeId, out var config))
            {
                throw new KeyNotFoundException($"OrderTypeRegistry: order type {orderTypeId} is not registered.");
            }

            return config;
        }

        public bool IsRegistered(int orderTypeId)
        {
            if ((uint)orderTypeId >= MaxOrderTypes) return false;
            int word = orderTypeId >> 6;
            int bit = orderTypeId & 63;
            return (_hasBits[word] & (1UL << bit)) != 0;
        }

        public IEnumerable<int> GetRegisteredIds()
        {
            for (int word = 0; word < _hasBits.Length; word++)
            {
                ulong bits = _hasBits[word];
                if (bits == 0) continue;

                for (int bit = 0; bit < 64; bit++)
                {
                    if ((bits & (1UL << bit)) != 0)
                    {
                        yield return (word << 6) | bit;
                    }
                }
            }
        }
    }
}
