using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteWalkStrategy
    {
        public bool TryApply(World world, Entity entity, Fix64Vec2 target, float speedCmPerSec, float stopRadiusCm)
        {
            if (!world.Has<NavAgent2D>(entity) ||
                !world.Has<Position2D>(entity))
            {
                return false;
            }

            if (!world.Has<NavGoal2D>(entity))
            {
                world.Add(entity, new NavGoal2D());
            }

            if (world.Has<NavKinematics2D>(entity))
            {
                ref var kinematics = ref world.Get<NavKinematics2D>(entity);
                kinematics.MaxSpeedCmPerSec = Fix64.FromFloat(speedCmPerSec);
            }

            ref var goal = ref world.Get<NavGoal2D>(entity);
            goal.Kind = NavGoalKind2D.Point;
            goal.TargetCm = target;
            goal.RadiusCm = Fix64.FromFloat(stopRadiusCm);
            return true;
        }

        public void Clear(World world, Entity entity)
        {
            if (world.Has<NavGoal2D>(entity))
            {
                ref var goal = ref world.Get<NavGoal2D>(entity);
                goal.Kind = NavGoalKind2D.None;
            }

        }
    }
}
