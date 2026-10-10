using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    /// <summary>
    /// A roadMoveFollow order that arrives with only its clicked destination is planned here, when it
    /// becomes active: the route starts from where the unit actually stands at that moment.
    /// </summary>
    internal sealed class RoadMoveOrderBindingSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, WorldPositionCm>()
            .WithNone<SuspendedTag>();

        private readonly Dictionary<string, object> _globals;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _roadMoveFollowOrderTypeId;
        private readonly MovePlanStore _plans;
        private readonly MovePlanRuntimeService _runtime;
        private readonly RoadNetworkMassNavigationRuntimeAccessor _navigation;
        private readonly RoadRouteRefreshService _routePlanner;

        public RoadMoveOrderBindingSystem(
            World world,
            Dictionary<string, object> globals,
            OrderTypeRegistry orderTypeRegistry,
            int roadMoveFollowOrderTypeId,
            MovePlanStore plans,
            MovePlanRuntimeService runtime,
            MassNavigationRuntimeBinding binding) : base(world)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _roadMoveFollowOrderTypeId = roadMoveFollowOrderTypeId;
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _navigation = new RoadNetworkMassNavigationRuntimeAccessor(binding);
            _routePlanner = new RoadRouteRefreshService(world, globals, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
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

                    if (!OrderWorldSpatialResolver.TryResolveExplicitMoveDestination(in buffer.ActiveOrder.Order, out _) &&
                        !TryPlanRequestedRoute(entity, in buffer.ActiveOrder.Order))
                    {
                        _runtime.Clear(entity);
                        OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
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

        private bool TryPlanRequestedRoute(Entity entity, in Order requested)
        {
            if (!OrderWorldSpatialResolver.TryResolveMoveDestination(World, in requested, out Vector3 destinationWorldCm))
            {
                _globals[RoadMoveOrderExpander.LastSubmitStatusKey] = "Road command rejected: the order carries no destination.";
                return false;
            }

            if (!_routePlanner.TryRefresh(entity, requested.PlayerId, destinationWorldCm, out Order planned, out _))
            {
                return false;
            }

            planned.OrderId = requested.OrderId;
            planned.SubmitMode = requested.SubmitMode;
            return RoadMoveActiveOrderBufferSync.TryReplaceActive(World, entity, in planned);
        }
    }
}
