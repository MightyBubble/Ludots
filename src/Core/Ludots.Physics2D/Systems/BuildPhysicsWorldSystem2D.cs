using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Physics.Broadphase;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 物理世界构建系统 — 全定点数域 AABB 计算。
    /// </summary>
    public sealed class BuildPhysicsWorldSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _singleRigidBodyQuery;
        private readonly QueryDescription _compoundRigidBodyQuery;

        public List<RigidBodyDesc> RigidBodyDescriptors { get; }
        public List<Entity> Entities { get; }
        public List<byte> ShapeSlots { get; }

        public BuildPhysicsWorldSystem2D(World world) : base(world)
        {
            _singleRigidBodyQuery = new QueryDescription()
                .WithAll<Position2D, Collider2D, Mass2D>()
                .WithNone<CompoundObstacle2DState>();
            _compoundRigidBodyQuery = new QueryDescription()
                .WithAll<Position2D, CompoundObstacle2DState, Mass2D>();
            RigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            Entities = new List<Entity>(1024);
            ShapeSlots = new List<byte>(1024);
        }

        public override void Update(in float deltaTime)
        {
            RigidBodyDescriptors.Clear();
            Entities.Clear();
            ShapeSlots.Clear();

            World.Query(in _singleRigidBodyQuery, (Entity entity, ref Position2D position, ref Collider2D collider, ref Mass2D mass) =>
            {
                Fix64 rotation = Fix64.Zero;
                if (World.TryGet(entity, out Rotation2D rot))
                {
                    rotation = rot.Value;
                }

                var aabb = CalculateAabb(position.Value, rotation, in collider);
                AddRigidBody(entity, shapeSlot: 0, in aabb, mass.IsStatic);
            });

            World.Query(in _compoundRigidBodyQuery, (Entity entity, ref Position2D position, ref CompoundObstacle2DState state, ref Mass2D mass) =>
            {
                if (state.SinkPhysicsCollider == 0)
                {
                    return;
                }

                Fix64 rotation = Fix64.Zero;
                if (World.TryGet(entity, out Rotation2D rot))
                {
                    rotation = rot.Value;
                }

                for (int i = 0; i < state.PieceCount; i++)
                {
                    var collider = new Collider2D
                    {
                        Type = ToColliderType(state.GetShape(i)),
                        ShapeDataIndex = state.GetShapeDataIndex(i)
                    };
                    var aabb = CalculateAabb(position.Value, rotation, in collider);
                    AddRigidBody(entity, checked((byte)i), in aabb, mass.IsStatic);
                }
            });
        }

        private void AddRigidBody(Entity entity, byte shapeSlot, in Aabb aabb, bool isStatic)
        {
            RigidBodyDescriptors.Add(new RigidBodyDesc
            {
                Index = RigidBodyDescriptors.Count,
                EntityIndex = entity.Id,
                BoundingBox = aabb,
                IsStatic = isStatic
            });

            Entities.Add(entity);
            ShapeSlots.Add(shapeSlot);
        }

        private static Aabb CalculateAabb(Fix64Vec2 worldPos, Fix64 rotation, in Collider2D collider)
        {
            return collider.Type switch
            {
                ColliderType2D.Circle => CalculateCircleAabb(worldPos, rotation, collider.ShapeDataIndex),
                ColliderType2D.Box => CalculateBoxAabb(worldPos, rotation, collider.ShapeDataIndex),
                ColliderType2D.Polygon => CalculatePolygonAabb(worldPos, rotation, collider.ShapeDataIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(collider.Type), collider.Type, "Unknown collider type")
            };
        }

        private static Aabb CalculateCircleAabb(Fix64Vec2 worldPos, Fix64 rotation, int shapeIndex)
        {
            if (!ShapeDataStorage2D.TryGetCircle(shapeIndex, out var circleData))
            {
                throw new InvalidOperationException($"Circle shape not found: {shapeIndex}");
            }

            var center = ShapeWorldTransform2D.GetCircleCenter(worldPos, rotation, circleData);
            var radiusVec = new Fix64Vec2(circleData.Radius, circleData.Radius);

            return new Aabb
            {
                Min = center - radiusVec,
                Max = center + radiusVec
            };
        }

        private static Aabb CalculateBoxAabb(Fix64Vec2 worldPos, Fix64 rotation, int shapeIndex)
        {
            if (!ShapeDataStorage2D.TryGetBox(shapeIndex, out var boxData))
            {
                throw new InvalidOperationException($"Box shape not found: {shapeIndex}");
            }

            var center = ShapeWorldTransform2D.GetBoxCenter(worldPos, rotation, boxData);
            var halfSize = new Fix64Vec2(boxData.HalfWidth, boxData.HalfHeight);

            if (rotation != Fix64.Zero)
            {
                Fix64 sin = Fix64Math.Sin(rotation);
                Fix64 cos = Fix64Math.Cos(rotation);

                Fix64 absSin = Fix64.Abs(sin);
                Fix64 absCos = Fix64.Abs(cos);

                halfSize = new Fix64Vec2(
                    absCos * boxData.HalfWidth + absSin * boxData.HalfHeight,
                    absSin * boxData.HalfWidth + absCos * boxData.HalfHeight
                );
            }

            return new Aabb
            {
                Min = center - halfSize,
                Max = center + halfSize
            };
        }

        private static Aabb CalculatePolygonAabb(Fix64Vec2 worldPos, Fix64 rotation, int shapeIndex)
        {
            if (!ShapeDataStorage2D.TryGetPolygon(shapeIndex, out var polygonData) ||
                polygonData.Vertices == null ||
                polygonData.VertexCount == 0)
            {
                throw new InvalidOperationException($"Polygon shape not found/invalid: {shapeIndex}");
            }

            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (rotation != Fix64.Zero)
            {
                sin = Fix64Math.Sin(rotation);
                cos = Fix64Math.Cos(rotation);
            }

            var v0 = ShapeWorldTransform2D.GetPolygonLocalVertex(polygonData, 0);
            if (rotation != Fix64.Zero)
            {
                v0 = Rotate(v0, sin, cos);
            }

            var min = v0;
            var max = v0;

            for (int i = 1; i < polygonData.VertexCount; i++)
            {
                var v = ShapeWorldTransform2D.GetPolygonLocalVertex(polygonData, i);
                if (rotation != Fix64.Zero)
                {
                    v = Rotate(v, sin, cos);
                }

                min = Fix64Vec2.Min(min, v);
                max = Fix64Vec2.Max(max, v);
            }

            return new Aabb
            {
                Min = worldPos + min,
                Max = worldPos + max
            };
        }

        private static Fix64Vec2 Rotate(Fix64Vec2 v, Fix64 sin, Fix64 cos)
        {
            return new Fix64Vec2(
                cos * v.X - sin * v.Y,
                sin * v.X + cos * v.Y
            );
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
