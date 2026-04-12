using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class Navigation2DKinematicMotionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<NavActor, NavActorRuntimeState, Position2D, Velocity2D, NavDesiredVelocity2D>();

        public Navigation2DKinematicMotionSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            if (!(dt > 0f))
            {
                return;
            }

            Fix64 step = Fix64.FromFloat(dt);
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<NavActorRuntimeState> runtimeStates = chunk.GetSpan<NavActorRuntimeState>();
                Span<Position2D> positions = chunk.GetSpan<Position2D>();
                Span<Velocity2D> velocities = chunk.GetSpan<Velocity2D>();
                Span<NavDesiredVelocity2D> desiredVelocities = chunk.GetSpan<NavDesiredVelocity2D>();

                foreach (int index in chunk)
                {
                    if (runtimeStates[index].EffectivePhysicsMode == NavPhysicsMode.FullPhysics2D)
                    {
                        continue;
                    }

                    Fix64Vec2 linear = desiredVelocities[index].ValueCmPerSec;
                    velocities[index] = new Velocity2D
                    {
                        Linear = linear,
                        Angular = Fix64.Zero,
                    };
                    positions[index] = new Position2D
                    {
                        Value = positions[index].Value + (linear * step),
                    };
                }
            }
        }
    }
}
