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
    internal sealed class RoadMoveLifecycleSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, WorldPositionCm, MovePlanOrderRuntime, MovePlanRuntime>();

        private readonly Dictionary<string, object> _globals;
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _roadMoveFollowOrderTypeId;
        private readonly MovePlanStore _plans;
        private readonly MovePlanRuntimeService _runtime;
        private readonly RoadRouteArrivalPolicy _arrival = new();
        private readonly RoadRouteTimeoutPolicy _timeout = new();
        private readonly IMovePlanExecutionSink _executionSink;
        private readonly RoadRouteRefreshService _refresh;
        private readonly RoadRouteProfileCatalog _profiles;
        private readonly MassNavigationSimulationRuntime _simulation;

        public RoadMoveLifecycleSystem(
            World world,
            Dictionary<string, object> globals,
            OrderTypeRegistry orderTypeRegistry,
            int roadMoveFollowOrderTypeId,
            MovePlanStore plans,
            MovePlanRuntimeService runtime,
            MassNavigationSimulationRuntime simulation) : base(world)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _roadMoveFollowOrderTypeId = roadMoveFollowOrderTypeId;
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _executionSink = new MassNavigationMovePlanExecutionSink(_simulation);
            _refresh = new RoadRouteRefreshService(world, globals, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            _profiles = new RoadRouteProfileCatalog(world);
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<MovePlanOrderRuntime> orderStates = chunk.GetSpan<MovePlanOrderRuntime>();
                Span<MovePlanRuntime> planStates = chunk.GetSpan<MovePlanRuntime>();
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var orderRuntime = ref orderStates[index];
                    ref var planRuntime = ref planStates[index];
                    if (!RoadMoveActiveOrderResolver.TryResolve(World, entity, _roadMoveFollowOrderTypeId, out Order activeOrder) ||
                        !RoadMoveActiveOrderResolver.OwnsRuntime(in activeOrder, in orderRuntime))
                    {
                        continue;
                    }

                    if (!_simulation.TryGetAgentWorldPositionCm(World, entity, out Vector2 position))
                    {
                        orderRuntime.LifecycleState = MovePlanLifecycleState.Failed;
                        orderRuntime.FailureReason = MovePlanFailureReason.ExecutionUnavailable;
                        CompleteRoadOrder(entity);
                        continue;
                    }

                    RoadRouteExecutionProfile execution = _profiles.ResolveExecution(entity);

                    if (orderRuntime.LifecycleState == MovePlanLifecycleState.Active &&
                        _plans.TryGetPlan(entity, activeOrder.OrderId, out MovePlanView plan))
                    {
                        if (_arrival.HasReachedFinalTarget(in activeOrder, position, execution.FinalArrivalRadiusCm))
                        {
                            orderRuntime.LifecycleState = MovePlanLifecycleState.Arrived;
                        }
                        else if (!TryResolveTimeoutTarget(in plan, planRuntime.CurrentWaypointIndex, in activeOrder, out Vector2 timeoutTarget))
                        {
                            orderRuntime.LifecycleState = MovePlanLifecycleState.NeedsReplan;
                            orderRuntime.FailureReason = MovePlanFailureReason.RouteEndedEarly;
                        }
                        else if (_timeout.Update(ref planRuntime, position, timeoutTarget, planRuntime.CurrentWaypointIndex, dt, execution.MinProgressCm, execution.StallTimeoutSeconds))
                        {
                            orderRuntime.TimeoutCount++;
                            orderRuntime.LifecycleState = MovePlanLifecycleState.NeedsReplan;
                            orderRuntime.FailureReason = MovePlanFailureReason.TimeoutAbandoned;
                        }
                    }

                    if (orderRuntime.LifecycleState == MovePlanLifecycleState.NeedsReplan)
                    {
                        if (_arrival.HasReachedFinalTarget(in activeOrder, position, execution.FinalArrivalRadiusCm))
                        {
                            orderRuntime.LifecycleState = MovePlanLifecycleState.Arrived;
                        }
                        else if (!TryRebuildActivePlan(entity, in activeOrder, ref orderRuntime, ref planRuntime, execution))
                        {
                            orderRuntime.LifecycleState = MovePlanLifecycleState.Failed;
                        }
                    }

                    if (orderRuntime.LifecycleState == MovePlanLifecycleState.Arrived ||
                        orderRuntime.LifecycleState == MovePlanLifecycleState.Failed)
                    {
                        CompleteRoadOrder(entity);
                    }
                }
            }
        }

        private bool TryRebuildActivePlan(Entity entity, in Order activeOrder, ref MovePlanOrderRuntime orderRuntime, ref MovePlanRuntime planRuntime, in RoadRouteExecutionProfile execution)
        {
            MovePlanFailureReason replanReason = orderRuntime.FailureReason;
            string replanLabel = replanReason == MovePlanFailureReason.RouteEndedEarly
                ? "route-end"
                : "timeout";
            if (orderRuntime.TimeoutCount > execution.MaxTimeoutRecoveries)
            {
                WriteStatus($"Road route abandoned after {orderRuntime.TimeoutCount} timeout recovery attempt(s).");
                orderRuntime.FailureReason = MovePlanFailureReason.TimeoutAbandoned;
                return false;
            }

            if (!RoadRouteFinalTargetResolver.TryResolve(in activeOrder, out Vector3 destinationWorldCm))
            {
                WriteStatus($"Road route abandoned after {replanLabel} replan: final target metadata is missing.");
                orderRuntime.FailureReason = MovePlanFailureReason.FinalTargetMissing;
                return false;
            }

            if (!_refresh.TryRefresh(entity, activeOrder.PlayerId, destinationWorldCm, out Order rebuiltPlanOrder, out _))
            {
                WriteStatus($"Road route abandoned after {replanLabel} replan: refresh plan build was rejected.");
                orderRuntime.FailureReason = MovePlanFailureReason.RefreshRejected;
                return false;
            }

            rebuiltPlanOrder.OrderId = activeOrder.OrderId;
            if (!RoadMoveActiveOrderBufferSync.TryReplaceActive(World, entity, in rebuiltPlanOrder))
            {
                WriteStatus($"Road route abandoned after {replanLabel} replan: active order payload could not be refreshed.");
                orderRuntime.FailureReason = MovePlanFailureReason.RefreshRejected;
                return false;
            }

            if (!_runtime.TryBindActiveOrder(entity, in rebuiltPlanOrder, preserveTimeoutCount: true, out orderRuntime, out planRuntime))
            {
                WriteStatus($"Road route abandoned after {replanLabel} replan: refresh plan build was rejected.");
                orderRuntime.FailureReason = MovePlanFailureReason.RefreshRejected;
                return false;
            }

            orderRuntime.ActiveOrderId = activeOrder.OrderId;
            planRuntime.BoundOrderId = activeOrder.OrderId;
            planRuntime.LastProgressPositionCm = default;
            planRuntime.LastResolvedWaypointIndex = 0;
            planRuntime.StallSeconds = 0f;
            planRuntime.Initialized = 0;
            if (replanReason == MovePlanFailureReason.RouteEndedEarly)
            {
                WriteStatus("Road route refreshed after route-end replan.");
            }
            else
            {
                WriteStatus($"Road route refreshed after timeout {orderRuntime.TimeoutCount}.");
            }

            return true;
        }

        private static bool TryResolveTimeoutTarget(in MovePlanView plan, int waypointIndex, in Order activeOrder, out Vector2 target)
        {
            if (plan.TryGetWaypoint(Math.Clamp(waypointIndex, 0, Math.Max(0, plan.Count - 1)), out target))
            {
                return true;
            }

            if (RoadRouteFinalTargetResolver.TryResolve(in activeOrder, out Vector3 finalTargetWorldCm))
            {
                target = new Vector2(finalTargetWorldCm.X, finalTargetWorldCm.Z);
                return true;
            }

            target = default;
            return false;
        }

        private void CompleteRoadOrder(Entity entity)
        {
            _executionSink.Clear(World, entity);
            _runtime.Clear(entity);
            if (RoadMoveActiveOrderResolver.TryResolve(World, entity, _roadMoveFollowOrderTypeId, out _))
            {
                OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
            }
        }

        private void WriteStatus(string message)
        {
            _globals[RoadMoveOrderExpander.LastSubmitStatusKey] = message;
        }
    }
}
