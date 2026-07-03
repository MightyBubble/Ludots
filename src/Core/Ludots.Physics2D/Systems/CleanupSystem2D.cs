using System.Runtime.CompilerServices;
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
            var job = new CleanupPairJob();
            World.InlineQuery<CleanupPairJob, CollisionPair>(in _pairQuery, ref job);
        }

        private struct CleanupPairJob : IForEach<CollisionPair>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                pair.IsActive = false;
                pair.ContactCount = 0;
                pair.Penetration = Fix64.Zero;
                pair.AccumulatedNormalImpulse0 = Fix64.Zero;
                pair.AccumulatedTangentImpulse0 = Fix64.Zero;
            }
        }
    }
}
