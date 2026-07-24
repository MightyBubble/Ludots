using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadMoveOrderBindingSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, WorldPositionCm>()
            .WithNone<SuspendedTag>();

        private readonly int _roadMoveFollowOrderTypeId;
        private readonly MovePlanStore _plans;
        private readonly MovePlanRuntimeService _runtime;
        private readonly RoadNetworkMassNavigationRuntimeAccessor _navigation;

        public RoadMoveOrderBindingSystem(World world, int roadMoveFollowOrderTypeId, MovePlanStore plans, MovePlanRuntimeService runtime, MassNavigationRuntimeBinding binding) : base(world)
        {
            _roadMoveFollowOrderTypeId = roadMoveFollowOrderTypeId;
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _navigation = new RoadNetworkMassNavigationRuntimeAccessor(binding);
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
                MassNavigationMovePlanExecutionSink? executionSink = null;
                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var buffer = ref buffers[index];
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _roadMoveFollowOrderTypeId)
                    {
                        executionSink ??= _navigation.RequireExecutionSink(nameof(RoadMoveOrderBindingSystem));
                        executionSink.Clear(World, entity);
                        _runtime.Clear(entity);
                        continue;
                    }

                    ref readonly Order activeOrder = ref buffer.ActiveOrder.Order;
                    ref MovePlanOrderRuntime orderRuntime = ref _runtime.EnsureOrderRuntime(entity);
                    ref MovePlanRuntime planRuntime = ref _runtime.EnsurePlanRuntime(entity);
                    bool hasPlan = _plans.TryGetPlan(entity, activeOrder.OrderId, out _);
                    bool needsBind =
                        orderRuntime.ActiveOrderId != activeOrder.OrderId ||
                        orderRuntime.LifecycleState == MovePlanLifecycleState.None ||
                        planRuntime.BoundOrderId != activeOrder.OrderId ||
                        !hasPlan ||
                        !World.Has<MovePlanExecutionIntent>(entity);
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
