using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class InstantCompleteOrderSystem : BaseSystem<World, float>
    {
        private readonly struct InstantCompleteOrderEntry
        {
            public InstantCompleteOrderEntry(int orderTypeId, in BlackboardStoredTargetKeys storedTargetKeys)
            {
                OrderTypeId = orderTypeId;
                StoredTargetKeys = storedTargetKeys;
            }

            public int OrderTypeId { get; }
            public BlackboardStoredTargetKeys StoredTargetKeys { get; }
        }

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<OrderBuffer>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly InstantCompleteOrderEntry[] _entries;
        private readonly List<Entity> _completedOrders = new(64);

        public InstantCompleteOrderSystem(World world, OrderTypeRegistry orderTypeRegistry) : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            var entries = new List<InstantCompleteOrderEntry>();
            foreach (int orderTypeId in orderTypeRegistry.GetRegisteredIds())
            {
                OrderTypeConfig config = orderTypeRegistry.Get(orderTypeId);
                if (!config.InstantComplete)
                {
                    continue;
                }

                BlackboardStoredTargetKeys storedTargetKeys = config.PersistentStoredTargetKeys;
                if (!storedTargetKeys.IsConfigured)
                {
                    throw new InvalidOperationException(
                        $"Order type '{config.Key}' ({orderTypeId}) has instantComplete=true but persistentStoredTarget is not fully configured.");
                }

                entries.Add(new InstantCompleteOrderEntry(orderTypeId, storedTargetKeys));
            }

            _entries = entries.ToArray();
        }

        public override void Update(in float dt)
        {
            if (_entries.Length == 0)
            {
                return;
            }

            _completedOrders.Clear();
            foreach (ref var chunk in World.Query(in Query))
            {
                var buffers = chunk.GetSpan<OrderBuffer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    ref OrderBuffer buffer = ref buffers[index];
                    if (!buffer.HasActive)
                    {
                        continue;
                    }

                    int activeOrderTypeId = buffer.ActiveOrder.Order.OrderTypeId;
                    for (int i = 0; i < _entries.Length; i++)
                    {
                        InstantCompleteOrderEntry entry = _entries[i];
                        if (entry.OrderTypeId != activeOrderTypeId)
                        {
                            continue;
                        }

                        BlackboardStoredTargetKeys storedTargetKeys = entry.StoredTargetKeys;
                        BlackboardStoredTargetOps.CommitFromOrder(
                            World,
                            entity,
                            in buffer.ActiveOrder.Order,
                            in storedTargetKeys);
                        _completedOrders.Add(entity);
                        break;
                    }
                }
            }

            for (int i = 0; i < _completedOrders.Count; i++)
            {
                OrderSubmitter.NotifyOrderComplete(World, _completedOrders[i], _orderTypeRegistry);
            }
        }
    }
}
