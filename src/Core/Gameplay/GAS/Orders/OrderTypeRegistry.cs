using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Registry for order type configurations.
    /// Provides lookup by OrderTagId.
    /// </summary>
    public sealed class OrderTypeRegistry
    {
        public const int MaxOrderTypes = 256;
        
        private readonly OrderTypeConfig?[] _configs = new OrderTypeConfig?[MaxOrderTypes];
        private readonly ulong[] _hasBits = new ulong[MaxOrderTypes >> 6];
        
        /// <summary>
        /// Default configuration for unregistered order types.
        /// </summary>
        public OrderTypeConfig DefaultConfig { get; set; } = new OrderTypeConfig
        {
            MaxQueueSize = 1,
            SameTypePolicy = SameTypePolicy.Replace,
            QueueFullPolicy = QueueFullPolicy.DropOldest,
            Priority = 50,
            BufferWindowMs = 500,
            CanInterruptSelf = true,
            QueuedModeMaxSize = 16,
            AllowQueuedMode = true
        };
        
        /// <summary>
        /// Clear all registered configurations.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_configs, 0, _configs.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
        }
        
        /// <summary>
        /// Register an order type configuration.
        /// </summary>
        /// <param name="config">The configuration to register.</param>
        /// <exception cref="ArgumentNullException">If config is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If OrderTagId is out of range.</exception>
        public void Register(OrderTypeConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if ((uint)config.OrderTagId >= MaxOrderTypes)
                throw new ArgumentOutOfRangeException(nameof(config), $"OrderTagId {config.OrderTagId} exceeds max {MaxOrderTypes}");
            
            _configs[config.OrderTagId] = config;
            int word = config.OrderTagId >> 6;
            int bit = config.OrderTagId & 63;
            _hasBits[word] |= 1UL << bit;
        }
        
        /// <summary>
        /// Register multiple order type configurations.
        /// </summary>
        public void RegisterAll(IEnumerable<OrderTypeConfig> configs)
        {
            foreach (var config in configs)
            {
                Register(config);
            }
        }
        
        /// <summary>
        /// Try to get the configuration for an order type.
        /// </summary>
        /// <param name="orderTagId">The order tag ID.</param>
        /// <param name="config">The configuration if found.</param>
        /// <returns>True if found, false otherwise.</returns>
        public bool TryGet(int orderTagId, out OrderTypeConfig config)
        {
            if ((uint)orderTagId >= MaxOrderTypes)
            {
                config = DefaultConfig;
                return false;
            }
            
            int word = orderTagId >> 6;
            int bit = orderTagId & 63;
            if ((_hasBits[word] & (1UL << bit)) == 0)
            {
                config = DefaultConfig;
                return false;
            }
            
            config = _configs[orderTagId]!;
            return true;
        }
        
        /// <summary>
        /// Get the configuration for an order type, or default if not found.
        /// </summary>
        /// <param name="orderTagId">The order tag ID.</param>
        /// <returns>The configuration or default.</returns>
        public OrderTypeConfig Get(int orderTagId)
        {
            TryGet(orderTagId, out var config);
            return config;
        }
        
        /// <summary>
        /// Check if an order type is registered.
        /// </summary>
        public bool IsRegistered(int orderTagId)
        {
            if ((uint)orderTagId >= MaxOrderTypes) return false;
            int word = orderTagId >> 6;
            int bit = orderTagId & 63;
            return (_hasBits[word] & (1UL << bit)) != 0;
        }
        
        /// <summary>
        /// Get all registered order tag IDs.
        /// </summary>
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
