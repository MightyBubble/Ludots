using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class SetRallyPointOrderSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<OrderBuffer>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _setRallyPointOrderTypeId;
        private readonly List<Entity> _completedOrders = new(64);

        public SetRallyPointOrderSystem(
            World world,
            OrderTypeRegistry orderTypeRegistry,
            int setRallyPointOrderTypeId) : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _setRallyPointOrderTypeId = setRallyPointOrderTypeId;
        }

        public override void Update(in float dt)
        {
            if (_setRallyPointOrderTypeId <= 0)
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
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _setRallyPointOrderTypeId)
                    {
                        continue;
                    }

                    RallyBlackboardOps.CommitFromOrder(World, entity, in buffer.ActiveOrder.Order);
                    _completedOrders.Add(entity);
                }
            }

            for (int i = 0; i < _completedOrders.Count; i++)
            {
                OrderSubmitter.NotifyOrderComplete(World, _completedOrders[i], _orderTypeRegistry);
            }
        }
    }
}
