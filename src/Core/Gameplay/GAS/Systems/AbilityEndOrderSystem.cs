using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Ends an active exec when a matching castAbility.End order becomes active.
    /// This keeps channel-end semantics separate from global stop semantics.
    /// </summary>
    public sealed class AbilityEndOrderSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<OrderBuffer, BlackboardIntBuffer>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _abilityEndOrderTypeId;
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<Entity> _completedOrders = new(64);

        public AbilityEndOrderSystem(World world, OrderTypeRegistry orderTypeRegistry, int abilityEndOrderTypeId)
            : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry;
            _abilityEndOrderTypeId = abilityEndOrderTypeId;
        }

        public override void Update(in float dt)
        {
            if (_abilityEndOrderTypeId <= 0)
            {
                return;
            }

            _completedOrders.Clear();
            foreach (ref var chunk in World.Query(in _query))
            {
                var buffers = chunk.GetSpan<OrderBuffer>();
                var intBuffers = chunk.GetSpan<BlackboardIntBuffer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    ref var orderBuffer = ref buffers[index];
                    if (!orderBuffer.HasActive || orderBuffer.ActiveOrder.Order.OrderTypeId != _abilityEndOrderTypeId)
                    {
                        continue;
                    }

                    ref var ints = ref intBuffers[index];
                    bool hasSlot = ints.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex);
                    if (World.TryGet(entity, out AbilityExecInstance exec) &&
                        (!hasSlot || exec.AbilitySlot == slotIndex))
                    {
                        _commandBuffer.Remove<AbilityExecInstance>(in entity);
                    }

                    _completedOrders.Add(entity);
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }

            for (int i = 0; i < _completedOrders.Count; i++)
            {
                OrderSubmitter.NotifyOrderComplete(World, _completedOrders[i], _orderTypeRegistry);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
