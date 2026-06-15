using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.Spawning;
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
                if (!TryResolveCollider(pair.EntityA, pair.ShapeSlotA, out var colliderA) ||
                    !TryResolveCollider(pair.EntityB, pair.ShapeSlotB, out var colliderB))
                {
                    pair.ContactCount = 0;
                    pair.Penetration = Fix64.Zero;
                    return;
                }

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

        private bool TryResolveCollider(Entity entity, byte shapeSlot, out Collider2D collider)
        {
            if (World.TryGet(entity, out CompoundObstacle2DState compoundState))
            {
                if (shapeSlot >= compoundState.PieceCount)
                {
                    collider = default;
                    return false;
                }

                collider = new Collider2D
                {
                    Type = ToColliderType(compoundState.GetShape(shapeSlot)),
                    ShapeDataIndex = compoundState.GetShapeDataIndex(shapeSlot)
                };
                return true;
            }

            if (shapeSlot == 0 && World.TryGet(entity, out collider))
            {
                return true;
            }

            collider = default;
            return false;
        }

        private static ColliderType2D ToColliderType(ManifestationObstacleShape2D shape)
        {
            return shape switch
            {
                ManifestationObstacleShape2D.Circle => ColliderType2D.Circle,
                ManifestationObstacleShape2D.Box => ColliderType2D.Box,
                ManifestationObstacleShape2D.Polygon => ColliderType2D.Polygon,
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
        }
    }
}
