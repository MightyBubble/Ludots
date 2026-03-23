using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Spawning.Systems
{
    /// <summary>
    /// Keeps runtime manifestations anchored to a parent and updates their facing
    /// from sweep velocity or parent execution target data.
    /// </summary>
    public sealed class ManifestationMotion2DSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<ManifestationMotion2D, ChildOf>();

        public ManifestationMotion2DSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            float deltaSeconds = dt <= 0f ? (1f / 60f) : dt;
            World.Query(in Query, (Entity entity, ref ManifestationMotion2D motion, ref ChildOf child) =>
            {
                Entity parent = child.Parent;
                if (!World.IsAlive(parent))
                {
                    return;
                }

                float? facing = motion.FacingSource switch
                {
                    ManifestationFacingSource2D.SweepVelocity => ResolveSweepFacing(entity, parent, motion, deltaSeconds),
                    ManifestationFacingSource2D.ParentExecutionTarget => ResolveParentExecutionFacing(parent),
                    _ => null,
                };

                if (facing.HasValue)
                {
                    Upsert(entity, new FacingDirection { AngleRad = facing.Value });
                }

                if (motion.FollowParentPosition != 0 && World.TryGet(parent, out WorldPositionCm parentPosition))
                {
                    Fix64Vec2 anchoredPosition = parentPosition.Value;
                    float? offsetFacing = facing ?? ResolveOffsetFacing(entity, parent);
                    if (motion.ForwardOffsetCm != 0 && offsetFacing.HasValue)
                    {
                        float angleRad = offsetFacing.Value;
                        anchoredPosition += Fix64Vec2.FromFloat(
                            MathF.Cos(angleRad) * motion.ForwardOffsetCm,
                            MathF.Sin(angleRad) * motion.ForwardOffsetCm);
                    }

                    Upsert(entity, new WorldPositionCm { Value = anchoredPosition });
                    Upsert(entity, new PreviousWorldPositionCm { Value = anchoredPosition });
                }
            });
        }

        private float? ResolveSweepFacing(Entity entity, Entity parent, in ManifestationMotion2D motion, float deltaSeconds)
        {
            float current = ResolveOffsetFacing(entity, parent) ?? 0f;
            return current + (motion.SweepDegreesPerSecond * (MathF.PI / 180f) * deltaSeconds);
        }

        private float? ResolveParentExecutionFacing(Entity parent)
        {
            if (!World.TryGet(parent, out WorldPositionCm parentPosition) ||
                !World.TryGet(parent, out AbilityExecInstance exec) ||
                exec.HasTargetPos == 0)
            {
                return null;
            }

            Fix64Vec2 delta = exec.TargetPosCm - parentPosition.Value;
            if (delta.X == Fix64.Zero && delta.Y == Fix64.Zero)
            {
                return null;
            }

            return Fix64Math.Atan2Fast(delta.Y, delta.X).ToFloat();
        }

        private float? ResolveOffsetFacing(Entity entity, Entity parent)
        {
            if (World.Has<FacingDirection>(entity))
            {
                return World.Get<FacingDirection>(entity).AngleRad;
            }

            if (World.Has<FacingDirection>(parent))
            {
                return World.Get<FacingDirection>(parent).AngleRad;
            }

            return null;
        }

        private void Upsert<T>(Entity entity, in T component)
        {
            if (World.Has<T>(entity))
            {
                World.Set(entity, component);
            }
            else
            {
                World.Add(entity, component);
            }
        }
    }
}
