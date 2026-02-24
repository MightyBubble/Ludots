using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
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
        private readonly QueryDescription _rigidBodyQuery;

        public List<RigidBodyDesc> RigidBodyDescriptors { get; }
        public List<Entity> Entities { get; }

        public BuildPhysicsWorldSystem2D(World world) : base(world)
        {
            _rigidBodyQuery = new QueryDescription().WithAll<Position2D, Collider2D, Mass2D>();
            RigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            Entities = new List<Entity>(1024);
        }

        public override void Update(in float deltaTime)
        {
            RigidBodyDescriptors.Clear();
            Entities.Clear();

            World.Query(in _rigidBodyQuery, (Entity entity, ref Position2D position, ref Collider2D collider, ref Mass2D mass) =>
            {
                Fix64 rotation = Fix64.Zero;
                if (World.TryGet(entity, out Rotation2D rot))
                {
                    rotation = rot.Value;
                }

                var aabb = CalculateAabb(in position, rotation, in collider);

                RigidBodyDescriptors.Add(new RigidBodyDesc
                {
                    EntityIndex = entity.Id,
                    BoundingBox = aabb,
                    IsStatic = mass.IsStatic
                });

                Entities.Add(entity);
            });
        }

        private static Aabb CalculateAabb(in Position2D position, Fix64 rotation, in Collider2D collider)
        {
            return collider.Type switch
            {
                ColliderType2D.Circle => CalculateCircleAabb(position.Value, collider.ShapeDataIndex),
                ColliderType2D.Box => CalculateBoxAabb(position.Value, rotation, collider.ShapeDataIndex),
                ColliderType2D.Polygon => CalculatePolygonAabb(position.Value, rotation, collider.ShapeDataIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(collider.Type), collider.Type, "Unknown collider type")
            };
        }

        private static Aabb CalculateCircleAabb(Fix64Vec2 worldPos, int shapeIndex)
        {
            if (!ShapeDataStorage2D.TryGetCircle(shapeIndex, out var circleData))
            {
                throw new InvalidOperationException($"Circle shape not found: {shapeIndex}");
            }

            var center = worldPos + circleData.LocalCenter;
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

            var center = worldPos + boxData.LocalCenter;
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

            var localCenter = polygonData.LocalCenter;
            var v0 = polygonData.Vertices[0] - localCenter;
            if (rotation != Fix64.Zero)
            {
                v0 = Rotate(v0, sin, cos);
            }

            var min = v0;
            var max = v0;

            for (int i = 1; i < polygonData.VertexCount; i++)
            {
                var v = polygonData.Vertices[i] - localCenter;
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
    }
}
