using System;
using System.Numerics;
using System.Text;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.UI;

namespace RoadNetworkShowcaseMod.Runtime
{
    internal sealed class RoadNetworkShowcasePanelStateBuilder
    {
        private readonly GameEngine _engine;
        private readonly RoadNetworkShowcaseRuntime _runtime;
        private readonly World _world;
        private readonly RoadRouteProfileCatalog _profiles;
        private readonly RoadRouteSelectionStrategy _selection = new();

        public RoadNetworkShowcasePanelStateBuilder(GameEngine engine, RoadNetworkShowcaseRuntime runtime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _world = engine.World;
            _profiles = new RoadRouteProfileCatalog(_world);
        }

        public RoadNetworkShowcasePanelState Build()
        {
            Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(_world, _engine.GlobalContext);
            int selectedCount = selected.Length;
            Entity primary = Entity.Null;
            SelectionContextRuntime.TryGetCurrentPrimary(_world, _engine.GlobalContext, out primary);

            var actors = new RoadNetworkShowcaseActorPanelState[selectedCount];
            for (int i = 0; i < selectedCount; i++)
            {
                actors[i] = BuildActorState(selected[i], selected[i] == primary);
            }

            return new RoadNetworkShowcasePanelState(
                Title: "Road Network Showcase",
                Status: _runtime.LastSubmitStatus,
                Selection: BuildSelectionSummary(selected, primary),
                Input: BuildInputSummary(),
                Chunks: $"Chunks {_runtime.LoadedChunkCount} | Nodes {_runtime.LoadedNodeCount}",
                Hint: "Legend: Query=selection/order, Plan=active order plus nav plan, Pick=selected nav waypoint, Move=intent sink to nav, Check=arrival and timeout state.",
                Actors: actors);
        }

        private RoadNetworkShowcaseActorPanelState BuildActorState(Entity actor, bool isPrimary)
        {
            string name = DescribeActorName(actor);
            string world = _world.Has<WorldPositionCm>(actor)
                ? FormatFixVec(_world.Get<WorldPositionCm>(actor).Value)
                : "<no-world>";
            string position2D = _world.Has<Position2D>(actor)
                ? FormatFixVec(_world.Get<Position2D>(actor).Value)
                : "<no-pos2d>";
            RoadRoutePlannerProfile planner = _profiles.ResolvePlanner(actor);
            RoadRouteExecutionProfile execution = _profiles.ResolveExecution(actor);

            OrderBuffer buffer = _world.Has<OrderBuffer>(actor) ? _world.Get<OrderBuffer>(actor) : default;
            RoadMoveOrderRuntime orderRuntime = _world.Has<RoadMoveOrderRuntime>(actor) ? _world.Get<RoadMoveOrderRuntime>(actor) : default;
            RoadNavPlanRuntime planRuntime = _world.Has<RoadNavPlanRuntime>(actor) ? _world.Get<RoadNavPlanRuntime>(actor) : default;
            RoadMoveExecutionIntent intent = _world.Has<RoadMoveExecutionIntent>(actor) ? _world.Get<RoadMoveExecutionIntent>(actor) : default;

            int roadMoveFollowOrderTypeId = ResolveRoadMoveFollowOrderTypeId();
            bool hasRoadActiveOrder = RoadMoveActiveOrderResolver.TryResolve(_world, actor, roadMoveFollowOrderTypeId, out Order activeOrder) &&
                                      RoadMoveActiveOrderResolver.OwnsRuntime(in activeOrder, in orderRuntime);

            string header = isPrimary
                ? $"{name}  [Primary]"
                : name;
            string queue = BuildQueueLine(in buffer, hasRoadActiveOrder ? activeOrder : default, hasRoadActiveOrder, roadMoveFollowOrderTypeId);
            string query = $"Query  world={world} pos2d={position2D} planner={planner.Label} execution={execution.Label}";
            string plan = BuildPlanLine(hasRoadActiveOrder, in activeOrder, in orderRuntime, in planRuntime);
            string select = BuildSelectionLine(actor, hasRoadActiveOrder, in activeOrder, in planRuntime, execution);
            string execute = BuildExecutionLine(actor, in intent);
            string check = BuildCheckLine(actor, hasRoadActiveOrder, in activeOrder, in orderRuntime, in planRuntime, execution);
            string path = BuildPathLine(actor, hasRoadActiveOrder, in activeOrder, in planRuntime);

            return new RoadNetworkShowcaseActorPanelState(
                Header: header,
                Queue: queue,
                Query: query,
                Plan: plan,
                Select: select,
                Execute: execute,
                Check: check,
                Path: path);
        }

        private string BuildSelectionSummary(Entity[] selected, Entity primary)
        {
            int selectedCount = selected.Length;
            Entity owner = ResolveLocalOwner();
            string ownerName = owner != Entity.Null && _world.IsAlive(owner) ? DescribeActorName(owner) : "<none>";
            string primaryName = primary != Entity.Null && _world.IsAlive(primary) ? DescribeActorName(primary) : "<none>";
            if (selectedCount <= 0)
            {
                return $"Selection 0 | Primary {primaryName} | Owner {ownerName}";
            }

            var summary = new StringBuilder();
            summary.Append("Selection ").Append(selectedCount);
            summary.Append(" | Primary ").Append(primaryName);
            summary.Append(" | Selected ");
            for (int i = 0; i < selectedCount; i++)
            {
                if (i > 0)
                {
                    summary.Append(", ");
                }

                summary.Append(DescribeActorName(selected[i]));
            }

            return summary.ToString();
        }

        private string BuildInputSummary()
        {
            string ground = ResolveDebugValue(LocalOrderSourceHelper.LastGroundWorldDebugKey, "<none>");
            string order = ResolveDebugValue(LocalOrderSourceHelper.LastOrderDebugKey, "<none>");
            string snapshot = string.IsNullOrWhiteSpace(_runtime.LatestDebugSnapshotPath)
                ? "<pending>"
                : _runtime.LatestDebugSnapshotPath!;
            return $"Input ground={ground}\nInput order={order}\nSnapshot file={snapshot}";
        }

        private string BuildQueueLine(in OrderBuffer buffer, in Order activeOrder, bool hasRoadActiveOrder, int roadMoveFollowOrderTypeId)
        {
            string active = buffer.HasActive
                ? DescribeOrder(buffer.ActiveOrder.Order, roadMoveFollowOrderTypeId)
                : "<none>";
            string road = hasRoadActiveOrder
                ? DescribeOrder(in activeOrder, roadMoveFollowOrderTypeId)
                : "<none>";
            string queued = DescribeQueuedOrders(in buffer, roadMoveFollowOrderTypeId);
            string pending = buffer.HasPending
                ? DescribeOrder(buffer.PendingOrder.Order, roadMoveFollowOrderTypeId)
                : "<none>";
            return $"Queue  active={active} | roadActive={road} | queued={queued} | pending={pending}";
        }

        private string BuildPlanLine(bool hasRoadActiveOrder, in Order activeOrder, in RoadMoveOrderRuntime orderRuntime, in RoadNavPlanRuntime planRuntime)
        {
            if (!hasRoadActiveOrder)
            {
                return $"Plan  lifecycle={orderRuntime.LifecycleState} failure={orderRuntime.FailureReason} activeOrder=<none>";
            }

            string finalGoal = planRuntime.PointCount > 0
                ? $"({planRuntime.FinalGoalXcm},{planRuntime.FinalGoalYcm})"
                : "<none>";
            return $"Plan  lifecycle={orderRuntime.LifecycleState} failure={orderRuntime.FailureReason} order={DescribeOrder(in activeOrder, activeOrder.OrderTypeId)} planGen={planRuntime.PlanGeneration} points={planRuntime.PointCount} final={finalGoal}";
        }

        private string BuildSelectionLine(Entity actor, bool hasRoadActiveOrder, in Order activeOrder, in RoadNavPlanRuntime planRuntime, in RoadRouteExecutionProfile execution)
        {
            if (!hasRoadActiveOrder)
            {
                return "Pick  no road-follow active order";
            }

            if (!_world.Has<Position2D>(actor))
            {
                return "Pick  actor has no Position2D";
            }

            if (!TryGetPlanStore(out RoadNavPlanStore? plans) ||
                plans == null ||
                !plans.TryGetPlan(actor, activeOrder.OrderId, out RoadNavPlanView plan))
            {
                return "Pick  active order exists but no bound plan";
            }

            Fix64Vec2 position = _world.Get<Position2D>(actor).Value;
            int currentIndex = Math.Clamp(planRuntime.CurrentWaypointIndex, 0, Math.Max(0, plan.Count - 1));
            if (!_selection.TrySelect(in plan, position, currentIndex, execution.WaypointRadiusCm, out RoadRouteSelection selection))
            {
                return $"Pick  plan present but selection failed from index {currentIndex}";
            }

            if (selection.Completed)
            {
                return $"Pick  completed at index {plan.Count} | stopRadius={execution.WaypointRadiusCm:0}";
            }

            float distanceCm = DistanceCm(position, selection.Target);
            return $"Pick  currentIndex={currentIndex} -> selectedIndex={selection.WaypointIndex} target={FormatFixVec(selection.Target)} dist={distanceCm:0.0} stopRadius={execution.WaypointRadiusCm:0}";
        }

        private string BuildExecutionLine(Entity actor, in RoadMoveExecutionIntent intent)
        {
            string intentText = intent.HasTarget != 0
                ? $"{FormatFixVec(intent.Target)} speed={intent.SpeedCmPerSec:0} stop={intent.StopRadiusCm:0}"
                : "<none>";
            string goal = "<none>";
            if (_world.Has<NavGoal2D>(actor))
            {
                ref readonly var navGoal = ref _world.Get<NavGoal2D>(actor);
                goal = navGoal.Kind == NavGoalKind2D.Point
                    ? $"{FormatFixVec(navGoal.TargetCm)} r={navGoal.RadiusCm.ToFloat():0}"
                    : navGoal.Kind.ToString();
            }

            string desired = _world.Has<NavDesiredVelocity2D>(actor)
                ? FormatFixVec(_world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec)
                : "<none>";
            string velocity = _world.Has<Velocity2D>(actor)
                ? FormatFixVec(_world.Get<Velocity2D>(actor).Linear)
                : "<none>";
            string force = _world.Has<ForceInput2D>(actor)
                ? FormatFixVec(_world.Get<ForceInput2D>(actor).Force)
                : "<none>";
            string maxSpeed = _world.Has<NavKinematics2D>(actor)
                ? _world.Get<NavKinematics2D>(actor).MaxSpeedCmPerSec.ToFloat().ToString("0")
                : "<none>";
            return $"Move  intent={intentText} | navGoal={goal} | desired={desired} | vel={velocity} | force={force} | maxSpeed={maxSpeed}";
        }

        private string BuildCheckLine(Entity actor, bool hasRoadActiveOrder, in Order activeOrder, in RoadMoveOrderRuntime orderRuntime, in RoadNavPlanRuntime planRuntime, in RoadRouteExecutionProfile execution)
        {
            string stall = $"{planRuntime.StallSeconds:0.00}/{execution.StallTimeoutSeconds:0.00}";
            string lastProgress = planRuntime.Initialized != 0
                ? FormatFixVec(planRuntime.LastProgressPosition)
                : "<uninitialized>";
            string distanceToGoal = "<none>";
            if (_world.Has<Position2D>(actor) && planRuntime.PointCount > 0)
            {
                Fix64Vec2 position = _world.Get<Position2D>(actor).Value;
                Fix64Vec2 finalGoal = Fix64Vec2.FromInt(planRuntime.FinalGoalXcm, planRuntime.FinalGoalYcm);
                distanceToGoal = DistanceCm(position, finalGoal).ToString("0.0");
            }

            string activeOrderId = hasRoadActiveOrder ? activeOrder.OrderId.ToString() : "<none>";
            return $"Check  activeOrderId={activeOrderId} timeoutCount={orderRuntime.TimeoutCount} execGen={orderRuntime.ExecutionGeneration} stall={stall} lastProgress={lastProgress} lastWp={planRuntime.LastResolvedWaypointIndex} distToFinal={distanceToGoal} arrivalRadius={execution.FinalArrivalRadiusCm:0}";
        }

        private string BuildPathLine(Entity actor, bool hasRoadActiveOrder, in Order activeOrder, in RoadNavPlanRuntime planRuntime)
        {
            if (!hasRoadActiveOrder)
            {
                return "Path  <none>";
            }

            if (TryGetPlanStore(out RoadNavPlanStore? plans) &&
                plans != null &&
                plans.TryGetPlan(actor, activeOrder.OrderId, out RoadNavPlanView plan))
            {
                return BuildPlanPointList("Path(plan)", in plan, planRuntime.CurrentWaypointIndex);
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in activeOrder.Args.Spatial);
            if (pointCount <= 0)
            {
                return "Path  active order has no spatial waypoints";
            }

            var text = new StringBuilder();
            text.Append("Path(order) ").Append(pointCount).Append(" pts");
            for (int i = 0; i < pointCount; i++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(in activeOrder, i, out Vector3 waypoint))
                {
                    continue;
                }

                if (i % 4 == 0)
                {
                    text.Append('\n');
                }
                else
                {
                    text.Append("  ");
                }

                text.Append(i).Append(':').Append('(')
                    .Append((int)MathF.Round(waypoint.X, MidpointRounding.AwayFromZero)).Append(',')
                    .Append((int)MathF.Round(waypoint.Z, MidpointRounding.AwayFromZero)).Append(')');
            }

            return text.ToString();
        }

        private static string BuildPlanPointList(string label, in RoadNavPlanView plan, int currentIndex)
        {
            var text = new StringBuilder();
            text.Append(label).Append(' ').Append(plan.Count).Append(" pts");
            for (int i = 0; i < plan.Count; i++)
            {
                if (!plan.TryGetWaypoint(i, out Fix64Vec2 waypoint))
                {
                    continue;
                }

                if (i % 4 == 0)
                {
                    text.Append('\n');
                }
                else
                {
                    text.Append("  ");
                }

                text.Append(i).Append(':').Append(FormatFixVec(waypoint));
                if (i == currentIndex)
                {
                    text.Append(" <- current");
                }
            }

            return text.ToString();
        }

        private bool TryGetPlanStore(out RoadNavPlanStore? plans)
        {
            plans = _engine.GlobalContext.TryGetValue(typeof(RoadNavPlanStore).FullName!, out object? planObj) &&
                    planObj is RoadNavPlanStore resolved
                ? resolved
                : null;
            return plans != null;
        }

        private int ResolveRoadMoveFollowOrderTypeId()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config ||
                !config.Constants.OrderTypeIds.TryGetValue(RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey, out int orderTypeId))
            {
                return 0;
            }

            return orderTypeId;
        }

        private Entity ResolveLocalOwner()
        {
            return _engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? ownerObj) &&
                   ownerObj is Entity owner &&
                   _world.IsAlive(owner)
                ? owner
                : Entity.Null;
        }

        private string ResolveDebugValue(string key, string fallback)
        {
            if (_engine.GlobalContext.TryGetValue(key, out object? value) &&
                value is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return fallback;
        }

        private string DescribeQueuedOrders(in OrderBuffer buffer, int roadMoveFollowOrderTypeId)
        {
            if (!buffer.HasQueued)
            {
                return "0";
            }

            var text = new StringBuilder();
            text.Append(buffer.QueuedCount).Append(" [");
            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(DescribeOrder(buffer.GetQueued(i).Order, roadMoveFollowOrderTypeId));
            }

            text.Append(']');
            return text.ToString();
        }

        private string DescribeOrder(in Order order, int roadMoveFollowOrderTypeId)
        {
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            string orderType = order.OrderTypeId == roadMoveFollowOrderTypeId
                ? "roadMoveFollow"
                : order.OrderTypeId.ToString();
            return $"{orderType}#{order.OrderId} submit={order.SubmitMode} pts={pointCount}";
        }

        private string DescribeActorName(Entity actor)
        {
            if (!_world.IsAlive(actor))
            {
                return "<dead>";
            }

            string name = _world.Has<Name>(actor)
                ? _world.Get<Name>(actor).Value
                : $"#{actor.Id}";
            return $"{name}#{actor.Id}";
        }

        private static float DistanceCm(Fix64Vec2 from, Fix64Vec2 to)
        {
            Fix64Vec2 delta = to - from;
            float dx = delta.X.ToFloat();
            float dy = delta.Y.ToFloat();
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static string FormatFixVec(in Fix64Vec2 value)
        {
            return $"({value.X.ToFloat():0},{value.Y.ToFloat():0})";
        }
    }
}
