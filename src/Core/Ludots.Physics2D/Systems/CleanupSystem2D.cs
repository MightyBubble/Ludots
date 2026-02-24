using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class CleanupSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _pairQuery = new QueryDescription().WithAll<CollisionPair>().WithNone<ActiveCollisionPairTag>();

        public CleanupSystem2D(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
            World.Query(in _pairQuery, (ref CollisionPair pair) =>
            {
                pair.IsActive = false;
                pair.ContactCount = 0;
                pair.Penetration = Fix64.Zero;
                pair.AccumulatedNormalImpulse0 = Fix64.Zero;
                pair.AccumulatedTangentImpulse0 = Fix64.Zero;
                pair.AccumulatedNormalImpulse1 = Fix64.Zero;
                pair.AccumulatedTangentImpulse1 = Fix64.Zero;
            });
        }
    }
}
