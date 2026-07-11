using System;
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
    internal sealed class RoadMovePlanSelectionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, WorldPositionCm, MovePlanOrderRuntime, MovePlanRuntime>()
            .WithNone<SuspendedTag>();

        private readonly float _defaultSpeedCmPerSec;
        private readonly int _moveSpeedAttributeId;
        private readonly int _roadMoveFollowOrderTypeId;
        private readonly MovePlanStore _plans;
        private readonly MovePlanRuntimeService _runtime;
        private readonly MassNavigationRuntimeBinding _binding;
        private readonly RoadRouteProfileCatalog _profiles;
        private readonly RoadRouteSelectionStrategy _selection = new();

        public RoadMovePlanSelectionSystem(World world, int roadMoveFollowOrderTypeId, MovePlanStore plans, MovePlanRuntimeService runtime, MassNavigationRuntimeBinding binding, float defaultSpeedCmPerSec = 600f) : base(world)
        {
            _roadMoveFollowOrderTypeId = roadMoveFollowOrderTypeId;
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
            _defaultSpeedCmPerSec = Math.Max(0f, defaultSpeedCmPerSec);
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
            _profiles = new RoadRouteProfileCatalog(world);
        }

        public override void Update(in float dt)
        {
            if (!RoadNetworkShowcaseIds.TryResolveSimulation(_binding, out MassNavigationSimulationRuntime simulation))
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                Span<OrderBuffer> buffers = chunk.GetSpan<OrderBuffer>();
                Span<MovePlanOrderRuntime> orderStates = chunk.GetSpan<MovePlanOrderRuntime>();
                Span<MovePlanRuntime> planStates = chunk.GetSpan<MovePlanRuntime>();
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var orderRuntime = ref orderStates[index];
                    ref var planRuntime = ref planStates[index];
                    ref var intent = ref _runtime.EnsureExecutionIntent(entity);
                    intent = default;

                    if (!RoadMoveActiveOrderResolver.TryResolve(World, entity, _roadMoveFollowOrderTypeId, out Order activeOrder) ||
                        !RoadMoveActiveOrderResolver.OwnsRuntime(in activeOrder, in orderRuntime))
                    {
                        continue;
                    }

                    if (orderRuntime.LifecycleState != MovePlanLifecycleState.Active)
                    {
                        continue;
                    }

                    if (!_plans.TryGetPlan(entity, activeOrder.OrderId, out MovePlanView plan))
                    {
                        orderRuntime.LifecycleState = MovePlanLifecycleState.Failed;
                        orderRuntime.FailureReason = MovePlanFailureReason.MissingPlan;
                        continue;
                    }

                    if (!simulation.TryGetAgentWorldPositionCm(World, entity, out Vector2 position))
                    {
                        orderRuntime.LifecycleState = MovePlanLifecycleState.Failed;
                        orderRuntime.FailureReason = MovePlanFailureReason.ExecutionUnavailable;
                        continue;
                    }

                    RoadRouteExecutionProfile execution = _profiles.ResolveExecution(entity);
                    if (!_selection.TrySelect(
                            in plan,
                            position,
                            Math.Clamp(planRuntime.CurrentWaypointIndex, 0, Math.Max(0, plan.Count - 1)),
                            execution.WaypointRadiusCm,
                            out RoadRouteSelection selection))
                    {
                        orderRuntime.LifecycleState = MovePlanLifecycleState.NeedsReplan;
                        orderRuntime.FailureReason = MovePlanFailureReason.RouteEndedEarly;
                        continue;
                    }

                    if (selection.Completed)
                    {
                        var arrival = new RoadRouteArrivalPolicy();
                        if (arrival.HasReachedFinalTarget(in activeOrder, position, execution.FinalArrivalRadiusCm))
                        {
                            orderRuntime.LifecycleState = MovePlanLifecycleState.Arrived;
                            orderRuntime.FailureReason = MovePlanFailureReason.None;
                        }
                        else
                        {
                            orderRuntime.LifecycleState = MovePlanLifecycleState.NeedsReplan;
                            orderRuntime.FailureReason = MovePlanFailureReason.RouteEndedEarly;
                        }

                        continue;
                    }

                    planRuntime.PointCount = plan.Count;
                    planRuntime.PlanGeneration = plan.PlanGeneration;
                    planRuntime.CurrentWaypointIndex = selection.WaypointIndex;
                    planRuntime.FinalGoalXcm = (int)MathF.Round(plan.FinalGoalWorldCm.X, MidpointRounding.AwayFromZero);
                    planRuntime.FinalGoalYcm = (int)MathF.Round(plan.FinalGoalWorldCm.Y, MidpointRounding.AwayFromZero);

                    intent.TargetWorldCm = selection.Target;
                    intent.SpeedCmPerSec = ResolveMoveSpeed(entity) * Math.Max(0.1f, execution.SpeedMultiplier);
                    intent.StopRadiusCm = execution.WaypointRadiusCm;
                    intent.HasTarget = 1;
                }
            }
        }

        private float ResolveMoveSpeed(Entity entity)
        {
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                World.TryGet(entity, out AttributeBuffer attributes))
            {
                float configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    return configured;
                }
            }

            return _defaultSpeedCmPerSec;
        }
    }
}
