using System;
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
    internal sealed class RoadMovePlanSelectionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, Position2D, RoadMoveOrderRuntime, RoadNavPlanRuntime>();

        private readonly float _defaultSpeedCmPerSec;
        private readonly int _moveSpeedAttributeId;
        private readonly RoadNavPlanStore _plans;
        private readonly RoadMoveRuntimeService _runtime;
        private readonly RoadRouteProfileCatalog _profiles;
        private readonly RoadRouteSelectionStrategy _selection = new();

        public RoadMovePlanSelectionSystem(World world, RoadNavPlanStore plans, RoadMoveRuntimeService runtime, float defaultSpeedCmPerSec = 600f) : base(world)
        {
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _defaultSpeedCmPerSec = Math.Max(0f, defaultSpeedCmPerSec);
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
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
                    Order activeOrder = buffers[index].ActiveOrder.Order;
                    ref var intent = ref _runtime.EnsureExecutionIntent(entity);
                    intent = default;

                    if (orderRuntime.LifecycleState != RoadMoveLifecycleState.Active ||
                        !_plans.TryGetPlan(entity, activeOrder.OrderId, out RoadNavPlanView plan))
                    {
                        orderRuntime.LifecycleState = RoadMoveLifecycleState.Failed;
                        orderRuntime.FailureReason = RoadMoveFailureReason.MissingPlan;
                        continue;
                    }

                    Fix64Vec2 position = positions[index].Value;
                    RoadRouteExecutionProfile execution = _profiles.ResolveExecution(entity);
                    if (!_selection.TrySelect(
                            in plan,
                            position,
                            Math.Clamp(planRuntime.CurrentWaypointIndex, 0, Math.Max(0, plan.Count - 1)),
                            execution.WaypointRadiusCm,
                            out RoadRouteSelection selection))
                    {
                        orderRuntime.LifecycleState = RoadMoveLifecycleState.NeedsReplan;
                        orderRuntime.FailureReason = RoadMoveFailureReason.RouteEndedEarly;
                        continue;
                    }

                    if (selection.Completed)
                    {
                        orderRuntime.LifecycleState = RoadMoveLifecycleState.NeedsReplan;
                        orderRuntime.FailureReason = RoadMoveFailureReason.RouteEndedEarly;
                        continue;
                    }

                    planRuntime.PointCount = plan.Count;
                    planRuntime.PlanGeneration = plan.PlanGeneration;
                    planRuntime.CurrentWaypointIndex = selection.WaypointIndex;
                    planRuntime.FinalGoalXcm = (int)MathF.Round(plan.FinalGoalWorldCm.X, MidpointRounding.AwayFromZero);
                    planRuntime.FinalGoalYcm = (int)MathF.Round(plan.FinalGoalWorldCm.Z, MidpointRounding.AwayFromZero);

                    intent.Target = selection.Target;
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
