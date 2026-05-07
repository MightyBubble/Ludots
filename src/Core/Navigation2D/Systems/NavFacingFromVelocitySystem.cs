using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    /// <summary>
    /// Keeps Navigation2D agents facing along locomotion.
    /// Actual physics velocity is the primary truth; desired velocity is only used
    /// as a fallback when the body is nearly stationary so visuals can turn into motion.
    /// </summary>
    public sealed class NavFacingFromVelocitySystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<NavAgent2D, Velocity2D>();

        private static readonly Fix64 MinActualSpeedSq = Fix64.FromFloat(25f);
        private static readonly Fix64 MinDesiredSpeedSq = Fix64.FromFloat(9f);

        private readonly CommandBuffer _commandBuffer = new();

        public NavFacingFromVelocitySystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<Velocity2D> velocities = chunk.GetSpan<Velocity2D>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    Fix64Vec2 facingVector = ResolveFacingVector(entity, velocities[index]);
                    if (facingVector.LengthSquared() <= Fix64.Zero)
                    {
                        continue;
                    }

                    float angleRad = Fix64Math.Atan2Fast(facingVector.Y, facingVector.X).ToFloat();
                    if (World.Has<FacingDirection>(entity))
                    {
                        ref var facing = ref World.Get<FacingDirection>(entity);
                        facing.AngleRad = angleRad;
                    }
                    else
                    {
                        _commandBuffer.Add(entity, new FacingDirection { AngleRad = angleRad });
                    }

                    if (World.Has<Rotation2D>(entity))
                    {
                        ref var rotation = ref World.Get<Rotation2D>(entity);
                        rotation.Value = Fix64.FromFloat(angleRad);
                    }
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Fix64Vec2 ResolveFacingVector(Entity entity, in Velocity2D velocity)
        {
            if (velocity.Linear.LengthSquared() >= MinActualSpeedSq)
            {
                return velocity.Linear;
            }

            if (World.TryGet(entity, out NavDesiredVelocity2D desiredVelocity) &&
                desiredVelocity.ValueCmPerSec.LengthSquared() >= MinDesiredSpeedSq)
            {
                return desiredVelocity.ValueCmPerSec;
            }

            return Fix64Vec2.Zero;
        }
    }
}
