using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Physics.Broadphase;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Builds Physics2D broadphase inputs. Dynamic bodies are rebuilt every step;
    /// static bodies are materialized into a cache and rebuilt only from explicit dirty state.
    /// </summary>
    public sealed class BuildPhysicsWorldSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _singleRigidBodyQuery;
        private readonly QueryDescription _compoundRigidBodyQuery;
        private readonly QueryDescription _staticDirtyQuery;
        private readonly QueryDescription _trackedStaticBodyQuery;

        private readonly List<Entity> _dynamicEntities;
        private readonly List<byte> _dynamicShapeSlots;
        private readonly List<Entity> _staticEntities;
        private readonly List<byte> _staticShapeSlots;

        public List<RigidBodyDesc> RigidBodyDescriptors { get; }
        public List<RigidBodyDesc> DynamicRigidBodyDescriptors { get; }
        public List<RigidBodyDesc> StaticRigidBodyDescriptors { get; }
        public List<Entity> Entities { get; }
        public List<byte> ShapeSlots { get; }

        public int StaticBodyVersion { get; private set; }
        public int DirtyStaticBodyCountLastUpdate { get; private set; }

        public BuildPhysicsWorldSystem2D(World world) : base(world)
        {
            _singleRigidBodyQuery = new QueryDescription()
                .WithAll<Position2D, Collider2D, Mass2D>()
                .WithNone<CompoundObstacle2DState, Physics2DStaticBodyState>();
            _compoundRigidBodyQuery = new QueryDescription()
                .WithAll<Position2D, CompoundObstacle2DState, Mass2D>()
                .WithNone<Physics2DStaticBodyState>();
            _staticDirtyQuery = new QueryDescription()
                .WithAll<Physics2DStaticBodyDirty>();
            _trackedStaticBodyQuery = new QueryDescription()
                .WithAll<Position2D, Mass2D, Physics2DStaticBodyState>();

            RigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            DynamicRigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            StaticRigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            Entities = new List<Entity>(1024);
            ShapeSlots = new List<byte>(1024);
            _dynamicEntities = new List<Entity>(1024);
            _dynamicShapeSlots = new List<byte>(1024);
            _staticEntities = new List<Entity>(1024);
            _staticShapeSlots = new List<byte>(1024);
        }

        public override void Update(in float deltaTime)
        {
            DirtyStaticBodyCountLastUpdate = 0;
            DynamicRigidBodyDescriptors.Clear();
            _dynamicEntities.Clear();
            _dynamicShapeSlots.Clear();

            bool staticCacheDirty = ConsumeStaticDirtyMarkers();

            World.Query(in _singleRigidBodyQuery, (Entity entity, ref Position2D position, ref Collider2D collider, ref Mass2D mass) =>
            {
                if (mass.IsStatic)
                {
                    MaterializeNewStaticBody(entity);
                    staticCacheDirty = true;
                    return;
                }

                Fix64 rotation = ResolveRotation(entity);
                Aabb aabb = CalculateAabb(position.Value, rotation, in collider);
                AddBody(
                    DynamicRigidBodyDescriptors,
                    _dynamicEntities,
                    _dynamicShapeSlots,
                    entity,
                    shapeSlot: 0,
                    in aabb,
                    isStatic: false);
            });

            World.Query(in _compoundRigidBodyQuery, (Entity entity, ref Position2D position, ref CompoundObstacle2DState state, ref Mass2D mass) =>
            {
                if (state.SinkPhysicsCollider == 0)
                {
                    return;
                }

                if (mass.IsStatic)
                {
                    MaterializeNewStaticBody(entity);
                    staticCacheDirty = true;
                    return;
                }

                AddCompoundBodies(
                    DynamicRigidBodyDescriptors,
                    _dynamicEntities,
                    _dynamicShapeSlots,
                    entity,
                    in position,
                    in state,
                    isStatic: false);
            });

            if (staticCacheDirty)
            {
                RebuildStaticRigidBodies();
                StaticBodyVersion = unchecked(StaticBodyVersion + 1);
            }

            BuildCombinedBodySnapshot();
        }

        public Entity ResolveBodyEntity(int bodyIndex)
        {
            if ((uint)bodyIndex >= (uint)Entities.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(bodyIndex));
            }

            return Entities[bodyIndex];
        }

        public bool TryResolveBody(int bodyIndex, out Entity entity, out byte shapeSlot)
        {
            if ((uint)bodyIndex < (uint)Entities.Count)
            {
                entity = Entities[bodyIndex];
                shapeSlot = ShapeSlots[bodyIndex];
                return true;
            }

            entity = default;
            shapeSlot = 0;
            return false;
        }

        private bool ConsumeStaticDirtyMarkers()
        {
            bool staticCacheDirty = false;
            World.Query(in _staticDirtyQuery, (Entity entity, ref Physics2DStaticBodyDirty dirty) =>
            {
                DirtyStaticBodyCountLastUpdate++;
                staticCacheDirty = true;

                if (!CanMaterializeStaticBody(entity))
                {
                    RemoveIfPresent<Physics2DStaticBodyState>(entity);
                }

                RemoveIfPresent<Physics2DStaticBodyDirty>(entity);
            });

            return staticCacheDirty;
        }

        private bool CanMaterializeStaticBody(Entity entity)
        {
            if (!World.IsAlive(entity) ||
                !World.TryGet(entity, out Position2D _) ||
                !World.TryGet(entity, out Mass2D mass) ||
                !mass.IsStatic)
            {
                return false;
            }

            if (World.TryGet(entity, out CompoundObstacle2DState state))
            {
                return state.SinkPhysicsCollider != 0 && state.PieceCount > 0;
            }

            return World.Has<Collider2D>(entity);
        }

        private void MaterializeNewStaticBody(Entity entity)
        {
            if (!World.Has<Physics2DStaticBodyState>(entity))
            {
                World.Add(entity, new Physics2DStaticBodyState());
            }

            DirtyStaticBodyCountLastUpdate++;
        }

        private void RebuildStaticRigidBodies()
        {
            StaticRigidBodyDescriptors.Clear();
            _staticEntities.Clear();
            _staticShapeSlots.Clear();

            World.Query(in _trackedStaticBodyQuery, (Entity entity, ref Position2D position, ref Mass2D mass, ref Physics2DStaticBodyState state) =>
            {
                if (!mass.IsStatic)
                {
                    return;
                }

                if (World.TryGet(entity, out CompoundObstacle2DState compoundState))
                {
                    if (compoundState.SinkPhysicsCollider == 0)
                    {
                        return;
                    }

                    AddCompoundBodies(
                        StaticRigidBodyDescriptors,
                        _staticEntities,
                        _staticShapeSlots,
                        entity,
                        in position,
                        in compoundState,
                        isStatic: true);
                    return;
                }

                if (!World.TryGet(entity, out Collider2D collider))
                {
                    return;
                }

                Fix64 rotation = ResolveRotation(entity);
                Aabb aabb = CalculateAabb(position.Value, rotation, in collider);
                AddBody(
                    StaticRigidBodyDescriptors,
                    _staticEntities,
                    _staticShapeSlots,
                    entity,
                    shapeSlot: 0,
                    in aabb,
                    isStatic: true);
            });
        }

        private void AddCompoundBodies(
            List<RigidBodyDesc> descriptors,
            List<Entity> entities,
            List<byte> shapeSlots,
            Entity entity,
            in Position2D position,
            in CompoundObstacle2DState state,
            bool isStatic)
        {
            Fix64 rotation = ResolveRotation(entity);
            for (int i = 0; i < state.PieceCount; i++)
            {
                var collider = new Collider2D
                {
                    Type = ToColliderType(state.GetShape(i)),
                    ShapeDataIndex = state.GetShapeDataIndex(i)
                };
                Aabb aabb = CalculateAabb(position.Value, rotation, in collider);
                AddBody(
                    descriptors,
                    entities,
                    shapeSlots,
                    entity,
                    checked((byte)i),
                    in aabb,
                    isStatic);
            }
        }

        private static void AddBody(
            List<RigidBodyDesc> descriptors,
            List<Entity> entities,
            List<byte> shapeSlots,
            Entity entity,
            byte shapeSlot,
            in Aabb aabb,
            bool isStatic)
        {
            descriptors.Add(new RigidBodyDesc
            {
                Index = descriptors.Count,
                EntityIndex = entity.Id,
                BoundingBox = aabb,
                IsStatic = isStatic
            });
            entities.Add(entity);
            shapeSlots.Add(shapeSlot);
        }

        private void BuildCombinedBodySnapshot()
        {
            RigidBodyDescriptors.Clear();
            Entities.Clear();
            ShapeSlots.Clear();

            AddCombinedBodies(DynamicRigidBodyDescriptors, _dynamicEntities, _dynamicShapeSlots);
            AddCombinedBodies(StaticRigidBodyDescriptors, _staticEntities, _staticShapeSlots);
        }

        private void AddCombinedBodies(
            List<RigidBodyDesc> descriptors,
            List<Entity> entities,
            List<byte> shapeSlots)
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                RigidBodyDesc descriptor = descriptors[i];
                descriptor.Index = RigidBodyDescriptors.Count;
                RigidBodyDescriptors.Add(descriptor);
                Entities.Add(entities[i]);
                ShapeSlots.Add(shapeSlots[i]);
            }
        }

        private Fix64 ResolveRotation(Entity entity)
        {
            return World.TryGet(entity, out Rotation2D rot) ? rot.Value : Fix64.Zero;
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

        private void RemoveIfPresent<T>(Entity entity)
        {
            if (World.Has<T>(entity))
            {
                World.Remove<T>(entity);
            }
        }
    }
}
