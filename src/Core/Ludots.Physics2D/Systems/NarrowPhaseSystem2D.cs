using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Collision;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 窄相碰撞检测系统 — 全定点数域，确保跨平台确定性。
    /// </summary>
    public sealed class NarrowPhaseSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _pairsQuery;

        public NarrowPhaseSystem2D(World world) : base(world)
        {
            _pairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        }

        public override void Update(in float deltaTime)
        {
            World.Query(in _pairsQuery, (ref CollisionPair pair) =>
            {
                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB))
                {
                    return;
                }

                ref var posA = ref pair.EntityA.Get<Position2D>();
                ref var posB = ref pair.EntityB.Get<Position2D>();
                ref var colliderA = ref pair.EntityA.Get<Collider2D>();
                ref var colliderB = ref pair.EntityB.Get<Collider2D>();

                var rotA = World.TryGet(pair.EntityA, out Rotation2D ra) ? ra : Rotation2D.Identity;
                var rotB = World.TryGet(pair.EntityB, out Rotation2D rb) ? rb : Rotation2D.Identity;

                bool hasCollision = CollisionAlgorithms2D.Detect(
                    posA.Value, rotA, colliderA,
                    posB.Value, rotB, colliderB,
                    out Fix64Vec2 normal,
                    out Fix64 penetration,
                    out Fix64Vec2 contactPoint);

                if (hasCollision)
                {
                    pair.Normal = normal;
                    pair.Penetration = penetration;
                    pair.LocalContactPoint0 = contactPoint;
                    pair.ContactCount = 1;
                }
                else
                {
                    pair.ContactCount = 0;
                    pair.Penetration = Fix64.Zero;
                    pair.AccumulatedNormalImpulse0 = Fix64.Zero;
                    pair.AccumulatedTangentImpulse0 = Fix64.Zero;
                    pair.AccumulatedNormalImpulse1 = Fix64.Zero;
                    pair.AccumulatedTangentImpulse1 = Fix64.Zero;
                }
            });
        }
    }
}
