using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Navigation2D.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class StopOrderSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<OrderBuffer>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _stopOrderTypeId;
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<Entity> _completedOrders = new(64);
        public StopOrderSystem(
            World world,
            OrderTypeRegistry orderTypeRegistry,
            int stopOrderTypeId) : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry;
            _stopOrderTypeId = stopOrderTypeId;
        }

        public override void Update(in float dt)
        {
            _completedOrders.Clear();
            foreach (ref var chunk in World.Query(in _query))
            {
                var buffers = chunk.GetSpan<OrderBuffer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity)) continue;

                    ref var buffer = ref buffers[index];
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _stopOrderTypeId)
                    {
                        continue;
                    }

                    if (World.Has<AbilityExecInstance>(entity))
                    {
                        _commandBuffer.Remove<AbilityExecInstance>(in entity);
                    }

                    if (World.Has<NavGoal2D>(entity))
                    {
                        ref var goal = ref World.Get<NavGoal2D>(entity);
                        goal.Kind = NavGoalKind2D.None;
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
