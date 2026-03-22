using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadMoveOrderBindingSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, WorldPositionCm>();

        private readonly int _roadMoveFollowOrderTypeId;
        private readonly RoadMoveRuntimeService _runtime;
        private readonly RoadRouteWalkStrategy _walk = new();

        public RoadMoveOrderBindingSystem(World world, int roadMoveFollowOrderTypeId, RoadMoveRuntimeService runtime) : base(world)
        {
            _roadMoveFollowOrderTypeId = roadMoveFollowOrderTypeId;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float dt)
        {
            if (_roadMoveFollowOrderTypeId <= 0)
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var buffer = ref buffers[index];
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _roadMoveFollowOrderTypeId)
                    {
                        _walk.Clear(World, entity);
                        _runtime.Clear(entity);
                        continue;
                    }

                    ref readonly Order activeOrder = ref buffer.ActiveOrder.Order;
                    ref RoadMoveOrderRuntime orderRuntime = ref _runtime.EnsureOrderRuntime(entity);
                    bool needsBind =
                        orderRuntime.ActiveOrderId != activeOrder.OrderId ||
                        orderRuntime.LifecycleState == RoadMoveLifecycleState.None ||
                        !World.Has<RoadNavPlanRuntime>(entity) ||
                        !World.Has<RoadMoveExecutionIntent>(entity);
                    if (!needsBind)
                    {
                        continue;
                    }

                    _runtime.TryBindActiveOrder(entity, in activeOrder, preserveTimeoutCount: false, out _, out _);
                    _runtime.ClearExecutionIntent(entity);
                }
            }
        }
    }
}
