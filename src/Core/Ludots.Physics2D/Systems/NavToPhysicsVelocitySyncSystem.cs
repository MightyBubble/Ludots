using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Establishes the physics linear velocity from movement authority before integration:
    /// free movers commit nav desired velocity, while suppressed movers clear locomotion velocity.
    /// </summary>
    public sealed class NavToPhysicsVelocitySyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<Velocity2D, NavDesiredVelocity2D, Mass2D>()
            .WithNone<SleepingTag, MovementSuppressed2D>();

        private static readonly QueryDescription _suppressedQuery = new QueryDescription()
            .WithAll<Velocity2D, MovementSuppressed2D, Mass2D>()
            .WithNone<SleepingTag>();

        public NavToPhysicsVelocitySyncSystem(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
            ClearSuppressedLocomotionVelocity();

            foreach (ref var chunk in World.Query(in _query))
            {
                var velocities = chunk.GetSpan<Velocity2D>();
                var desiredVelocities = chunk.GetSpan<NavDesiredVelocity2D>();
                var masses = chunk.GetSpan<Mass2D>();

                foreach (int index in chunk)
                {
                    if (masses[index].IsStatic)
                    {
                        continue;
                    }

                    velocities[index].Linear = desiredVelocities[index].ValueCmPerSec;
                }
            }
        }

        private void ClearSuppressedLocomotionVelocity()
        {
            foreach (ref var chunk in World.Query(in _suppressedQuery))
            {
                var velocities = chunk.GetSpan<Velocity2D>();
                var masses = chunk.GetSpan<Mass2D>();

                foreach (int index in chunk)
                {
                    if (masses[index].IsStatic)
                    {
                        continue;
                    }

                    velocities[index].Linear = Fix64Vec2.Zero;
                }
            }
        }
    }
}
