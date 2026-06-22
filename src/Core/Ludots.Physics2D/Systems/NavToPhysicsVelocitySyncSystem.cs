using Arch.Core;
using Arch.System;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class NavToPhysicsVelocitySyncSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<Velocity2D, NavDesiredVelocity2D, Mass2D>()
            .WithNone<SleepingTag, MovementSuppressed2D>();

        public NavToPhysicsVelocitySyncSystem(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
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
    }
}
