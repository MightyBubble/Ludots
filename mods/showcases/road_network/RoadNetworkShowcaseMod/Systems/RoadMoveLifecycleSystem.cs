using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadMoveLifecycleSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, Position2D, RoadMoveOrderRuntime, RoadNavPlanRuntime>();

        private readonly Dictionary<string, object> _globals;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly RoadNavPlanStore _plans;
        private readonly RoadMoveRuntimeService _runtime;
        private readonly RoadRouteArrivalPolicy _arrival = new();
        private readonly RoadRouteTimeoutPolicy _timeout = new();
        private readonly RoadRouteWalkStrategy _walk = new();
        private readonly RoadRouteRefreshService _refresh;
        private readonly RoadRouteProfileCatalog _profiles;

        public RoadMoveLifecycleSystem(
            World world,
            Dictionary<string, object> globals,
            OrderTypeRegistry orderTypeRegistry,
            RoadNavPlanStore plans,
            RoadMoveRuntimeService runtime) : base(world)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _refresh = new RoadRouteRefreshService(world, globals, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            _profiles = new RoadRouteProfileCatalog(world);
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                Span<Position2D> positions = chunk.GetSpan<Position2D>();
                Span<RoadMoveOrderRuntime> orderStates = chunk.GetSpan<RoadMoveOrderRuntime>();
                Span<RoadNavPlanRuntime> planStates = chunk.GetSpan<RoadNavPlanRuntime>();
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var orderRuntime = ref orderStates[index];
                    ref var planRuntime = ref planStates[index];
                    ref var buffer = ref buffers[index];
                    Order activeOrder = buffer.ActiveOrder.Order;
                    Fix64Vec2 position = positions[index].Value;
                    RoadRouteExecutionProfile execution = _profiles.ResolveExecution(entity);

                    if (orderRuntime.LifecycleState == RoadMoveLifecycleState.Active &&
                        _plans.TryGetPlan(entity, activeOrder.OrderId, out _))
                    {
                        if (_arrival.HasReachedFinalTarget(in activeOrder, position, execution.FinalArrivalRadiusCm))
                        {
                            orderRuntime.LifecycleState = RoadMoveLifecycleState.Arrived;
                        }
                        else if (_timeout.Update(ref planRuntime, position, planRuntime.CurrentWaypointIndex, dt, execution.MinProgressCm, execution.StallTimeoutSeconds))
                        {
                            orderRuntime.TimeoutCount++;
                            orderRuntime.LifecycleState = RoadMoveLifecycleState.NeedsReplan;
                            orderRuntime.FailureReason = RoadMoveFailureReason.TimeoutAbandoned;
                        }
                    }

                    if (orderRuntime.LifecycleState == RoadMoveLifecycleState.NeedsReplan)
                    {
                        if (_arrival.HasReachedFinalTarget(in activeOrder, position, execution.FinalArrivalRadiusCm))
                        {
                            orderRuntime.LifecycleState = RoadMoveLifecycleState.Arrived;
                        }
                        else if (!TryRebuildActivePlan(entity, in activeOrder, ref orderRuntime, ref planRuntime, execution))
                        {
                            orderRuntime.LifecycleState = RoadMoveLifecycleState.Failed;
                        }
                    }

                    if (orderRuntime.LifecycleState == RoadMoveLifecycleState.Arrived ||
                        orderRuntime.LifecycleState == RoadMoveLifecycleState.Failed)
                    {
                        CompleteRoadOrder(entity);
                    }
                }
            }
        }

        private bool TryRebuildActivePlan(Entity entity, in Order activeOrder, ref RoadMoveOrderRuntime orderRuntime, ref RoadNavPlanRuntime planRuntime, in RoadRouteExecutionProfile execution)
        {
            if (orderRuntime.TimeoutCount > execution.MaxTimeoutRecoveries)
            {
                WriteStatus($"Road route abandoned after {orderRuntime.TimeoutCount} timeout recovery attempt(s).");
                orderRuntime.FailureReason = RoadMoveFailureReason.TimeoutAbandoned;
                return false;
            }

            if (!RoadRouteFinalTargetResolver.TryResolve(in activeOrder, out Vector3 destinationWorldCm))
            {
                WriteStatus($"Road route abandoned after {orderRuntime.TimeoutCount} timeout recovery attempt(s): final target metadata is missing.");
                orderRuntime.FailureReason = RoadMoveFailureReason.FinalTargetMissing;
                return false;
            }

            if (!_refresh.TryRefresh(entity, activeOrder.PlayerId, destinationWorldCm, out Order rebuiltPlanOrder, out _))
            {
                WriteStatus($"Road route abandoned after {orderRuntime.TimeoutCount} timeout recovery attempt(s): refresh plan build was rejected.");
                orderRuntime.FailureReason = RoadMoveFailureReason.RefreshRejected;
                return false;
            }

            rebuiltPlanOrder.OrderId = activeOrder.OrderId;
            if (!_runtime.TryBindActiveOrder(entity, in rebuiltPlanOrder, preserveTimeoutCount: true, out orderRuntime, out planRuntime))
            {
                WriteStatus($"Road route abandoned after {orderRuntime.TimeoutCount} timeout recovery attempt(s): refresh plan build was rejected.");
                orderRuntime.FailureReason = RoadMoveFailureReason.RefreshRejected;
                return false;
            }

            orderRuntime.ActiveOrderId = activeOrder.OrderId;
            planRuntime.BoundOrderId = activeOrder.OrderId;
            planRuntime.LastProgressPosition = default;
            planRuntime.LastResolvedWaypointIndex = 0;
            planRuntime.StallSeconds = 0f;
            planRuntime.Initialized = 0;
            WriteStatus($"Road route refreshed after timeout {orderRuntime.TimeoutCount}.");
            return true;
        }

        private void CompleteRoadOrder(Entity entity)
        {
            _walk.Clear(World, entity);
            _runtime.Clear(entity);
            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
        }

        private void WriteStatus(string message)
        {
            _globals[RoadMoveOrderExpander.LastSubmitStatusKey] = message;
        }
    }
}
