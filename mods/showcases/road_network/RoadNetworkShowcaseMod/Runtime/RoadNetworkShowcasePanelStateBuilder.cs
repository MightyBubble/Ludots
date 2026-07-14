using System;
using System.Numerics;
using System.Text;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
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
            Entity[] commandActors = SnapshotCommandSource();
            int commandActorCount = commandActors.Length;
            Entity primary = Entity.Null;
            TryGetCommandSourcePrimary(out primary);

            var actors = new RoadNetworkShowcaseActorPanelState[commandActorCount];
            for (int i = 0; i < commandActorCount; i++)
            {
                actors[i] = BuildActorState(commandActors[i], commandActors[i] == primary);
            }

            return new RoadNetworkShowcasePanelState(
                Title: "Road Network Showcase",
                Status: _runtime.LastSubmitStatus,
                CommandSource: BuildCommandSourceSummary(commandActors, primary),
                Input: BuildInputSummary(),
                Chunks: $"Chunks {_runtime.LoadedChunkCount} | Nodes {_runtime.LoadedNodeCount}",
                Hint: "Legend: Query=command source/order, Plan=active order plus nav plan, Pick=route waypoint, Move=intent sink to nav, Check=arrival and timeout state.",
                Actors: actors);
        }

        private RoadNetworkShowcaseActorPanelState BuildActorState(Entity actor, bool isPrimary)
        {
            string name = DescribeActorName(actor);
            string world = _world.Has<WorldPositionCm>(actor)
                ? FormatFixVec(_world.Get<WorldPositionCm>(actor).Value)
                : "<no-world>";
            RoadRoutePlannerProfile planner = _profiles.ResolvePlanner(actor);
            RoadRouteExecutionProfile execution = _profiles.ResolveExecution(actor);

            OrderBuffer buffer = _world.Has<OrderBuffer>(actor) ? _world.Get<OrderBuffer>(actor) : default;
            MovePlanOrderRuntime orderRuntime = _world.Has<MovePlanOrderRuntime>(actor) ? _world.Get<MovePlanOrderRuntime>(actor) : default;
            MovePlanRuntime planRuntime = _world.Has<MovePlanRuntime>(actor) ? _world.Get<MovePlanRuntime>(actor) : default;
            MovePlanExecutionIntent intent = _world.Has<MovePlanExecutionIntent>(actor) ? _world.Get<MovePlanExecutionIntent>(actor) : default;

            int roadMoveFollowOrderTypeId = ResolveRoadMoveFollowOrderTypeId();
            bool hasRoadActiveOrder = RoadMoveActiveOrderResolver.TryResolve(_world, actor, roadMoveFollowOrderTypeId, out Order activeOrder) &&
                                      RoadMoveActiveOrderResolver.OwnsRuntime(in activeOrder, in orderRuntime);

            string header = isPrimary
                ? $"{name}  [Primary]"
                : name;
            string queue = BuildQueueLine(in buffer, hasRoadActiveOrder ? activeOrder : default, hasRoadActiveOrder, roadMoveFollowOrderTypeId);
            string query = $"Query  world={world} planner={planner.Label} execution={execution.Label}";
            string plan = BuildPlanLine(hasRoadActiveOrder, in activeOrder, in orderRuntime, in planRuntime);
            string pick = BuildRoutePickLine(actor, hasRoadActiveOrder, in activeOrder, in planRuntime, execution);
            string execute = BuildExecutionLine(actor, in intent);
            string check = BuildCheckLine(actor, hasRoadActiveOrder, in activeOrder, in orderRuntime, in planRuntime, execution);
            string path = BuildPathLine(actor, hasRoadActiveOrder, in activeOrder, in planRuntime);

            return new RoadNetworkShowcaseActorPanelState(
                Header: header,
                Queue: queue,
                Query: query,
                Plan: plan,
                Pick: pick,
                Execute: execute,
                Check: check,
                Path: path);
        }

        private string BuildCommandSourceSummary(Entity[] commandActors, Entity primary)
        {
            int commandActorCount = commandActors.Length;
            Entity owner = ResolveLocalOwner();
            string ownerName = owner != Entity.Null && _world.IsAlive(owner) ? DescribeActorName(owner) : "<none>";
            string primaryName = primary != Entity.Null && _world.IsAlive(primary) ? DescribeActorName(primary) : "<none>";
            if (commandActorCount <= 0)
            {
                return $"Command source 0 | Primary {primaryName} | Owner {ownerName}";
            }

            var summary = new StringBuilder();
            summary.Append("Command source ").Append(commandActorCount);
            summary.Append(" | Primary ").Append(primaryName);
            summary.Append(" | Actors ");
            for (int i = 0; i < commandActorCount; i++)
            {
                if (i > 0)
                {
                    summary.Append(", ");
                }

                summary.Append(DescribeActorName(commandActors[i]));
            }

            return summary.ToString();
        }

        private Entity[] SnapshotCommandSource()
        {
            return TryResolveLocalCommandSourceOwner(out Entity owner)
                ? EntityCollectionContextRuntime.Snapshot(_engine.GlobalContext, owner, EntityCollectionKeys.CommandSource)
                : Array.Empty<Entity>();
        }

        private bool TryGetCommandSourcePrimary(out Entity entity)
        {
            entity = Entity.Null;
            return TryResolveLocalCommandSourceOwner(out Entity owner) &&
                   EntityCollectionContextRuntime.TryGetPrimary(
                       _world,
                       _engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       out entity);
        }

        private bool TryResolveLocalCommandSourceOwner(out Entity owner)
        {
            owner = Entity.Null;
            Entity local = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (local == Entity.Null || !_world.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
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

        private string BuildPlanLine(bool hasRoadActiveOrder, in Order activeOrder, in MovePlanOrderRuntime orderRuntime, in MovePlanRuntime planRuntime)
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

        private string BuildRoutePickLine(Entity actor, bool hasRoadActiveOrder, in Order activeOrder, in MovePlanRuntime planRuntime, in RoadRouteExecutionProfile execution)
        {
            if (!hasRoadActiveOrder)
            {
                return "Pick  no road-follow active order";
            }

            if (!_world.Has<WorldPositionCm>(actor))
            {
                return "Pick  actor has no WorldPositionCm";
            }

            if (!TryGetPlanStore(out MovePlanStore? plans) ||
                plans == null ||
                !plans.TryGetPlan(actor, activeOrder.OrderId, out MovePlanView plan))
            {
                return "Pick  active order exists but no bound plan";
            }

            Vector2 position = ToVector2(_world.Get<WorldPositionCm>(actor));
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
            return $"Pick  currentIndex={currentIndex} -> selectedIndex={selection.WaypointIndex} target={FormatVector2(selection.Target)} dist={distanceCm:0.0} stopRadius={execution.WaypointRadiusCm:0}";
        }

        private string BuildExecutionLine(Entity actor, in MovePlanExecutionIntent intent)
        {
            string intentText = intent.HasTarget != 0
                ? $"{FormatVector2(intent.TargetWorldCm)} speed={intent.SpeedCmPerSec:0} stop={intent.StopRadiusCm:0}"
                : "<none>";
            string massTarget = ResolveMassTargetText(actor);
            string massAgent = _world.Has<MassNavigationAgentIndex>(actor)
                ? _world.Get<MassNavigationAgentIndex>(actor).Value.ToString()
                : "<unbound>";
            return $"Move  intent={intentText} | massAgent={massAgent} | massTarget={massTarget}";
        }

        private string BuildCheckLine(Entity actor, bool hasRoadActiveOrder, in Order activeOrder, in MovePlanOrderRuntime orderRuntime, in MovePlanRuntime planRuntime, in RoadRouteExecutionProfile execution)
        {
            string stall = $"{planRuntime.StallSeconds:0.00}/{execution.StallTimeoutSeconds:0.00}";
            string lastProgress = planRuntime.Initialized != 0
                ? FormatVector2(planRuntime.LastProgressPositionCm)
                : "<uninitialized>";
            string distanceToGoal = "<none>";
            if (_world.Has<WorldPositionCm>(actor) && planRuntime.PointCount > 0)
            {
                Vector2 position = ToVector2(_world.Get<WorldPositionCm>(actor));
                var finalGoal = new Vector2(planRuntime.FinalGoalXcm, planRuntime.FinalGoalYcm);
                distanceToGoal = DistanceCm(position, finalGoal).ToString("0.0");
            }

            string activeOrderId = hasRoadActiveOrder ? activeOrder.OrderId.ToString() : "<none>";
            return $"Check  activeOrderId={activeOrderId} timeoutCount={orderRuntime.TimeoutCount} execGen={orderRuntime.ExecutionGeneration} stall={stall} lastProgress={lastProgress} lastWp={planRuntime.LastResolvedWaypointIndex} distToFinal={distanceToGoal} arrivalRadius={execution.FinalArrivalRadiusCm:0}";
        }

        private string BuildPathLine(Entity actor, bool hasRoadActiveOrder, in Order activeOrder, in MovePlanRuntime planRuntime)
        {
            if (!hasRoadActiveOrder)
            {
                return "Path  <none>";
            }

            if (TryGetPlanStore(out MovePlanStore? plans) &&
                plans != null &&
                plans.TryGetPlan(actor, activeOrder.OrderId, out MovePlanView plan))
            {
                return BuildPlanPointList("Path(plan)", in plan, planRuntime.CurrentWaypointIndex);
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(_world, in activeOrder);
            if (pointCount <= 0)
            {
                return "Path  active order has no spatial waypoints";
            }

            var text = new StringBuilder();
            text.Append("Path(order) ").Append(pointCount).Append(" pts");
            for (int i = 0; i < pointCount; i++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(_world, in activeOrder, i, out Vector3 waypoint))
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

        private static string BuildPlanPointList(string label, in MovePlanView plan, int currentIndex)
        {
            var text = new StringBuilder();
            text.Append(label).Append(' ').Append(plan.Count).Append(" pts");
            for (int i = 0; i < plan.Count; i++)
            {
                if (!plan.TryGetWaypoint(i, out Vector2 waypoint))
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

                text.Append(i).Append(':').Append(FormatVector2(waypoint));
                if (i == currentIndex)
                {
                    text.Append(" <- current");
                }
            }

            return text.ToString();
        }

        private bool TryGetPlanStore(out MovePlanStore? plans)
        {
            plans = _engine.GlobalContext.TryGetValue(typeof(MovePlanStore).FullName!, out object? planObj) &&
                    planObj is MovePlanStore resolved
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
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(_world, in order);
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

        private string ResolveMassTargetText(Entity actor)
        {
            if (!_world.TryGet(actor, out MassNavigationAgentIndex index))
            {
                return "<unbound>";
            }

            MassNavigationSimulationRuntime? simulation = _engine.GetService(MassNavigationKeys.SimulationRuntime);
            if (simulation == null ||
                !simulation.TryGetAgentNavigationTargetWorldCm(index.Value, out float xCm, out float yCm))
            {
                return "<none>";
            }

            return FormatVector2(new Vector2(xCm, yCm));
        }

        private static Vector2 ToVector2(in WorldPositionCm position)
        {
            var worldCm = position.ToWorldCmInt2();
            return new Vector2(worldCm.X, worldCm.Y);
        }

        private static float DistanceCm(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            float dx = delta.X;
            float dy = delta.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static string FormatFixVec(in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 value)
        {
            return $"({value.X.ToFloat():0},{value.Y.ToFloat():0})";
        }

        private static string FormatVector2(in Vector2 value)
        {
            return $"({value.X:0},{value.Y:0})";
        }
    }
}
