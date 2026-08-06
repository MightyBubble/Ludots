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
    /// Builds Physics2D broadphase inputs. Dynamic bodies are rebuilt every step;
    /// static bodies are materialized into a cache and rebuilt only from explicit dirty state.
    /// </summary>
    public sealed class BuildPhysicsWorldSystem2D : BaseSystem<World, float>
    {
        public struct BodySnapshot
        {
            public Entity Entity;
            public byte ShapeSlot;
            public Position2D Position;
            public Rotation2D Rotation;
            public Collider2D Collider;
            public Velocity2D Velocity;
            public Mass2D Mass;
            public PhysicsMaterial2D Material;
            public byte HasMaterial;
            public byte IsSleeping;
            public int IslandId;
            public byte IsContactEventEmitter;
        }

        private readonly QueryDescription _singleRigidBodyQuery;
        private readonly QueryDescription _compoundRigidBodyQuery;
        private readonly QueryDescription _staticDirtyQuery;
        private readonly QueryDescription _trackedStaticBodyQuery;
        private readonly ShapeDataStorage2D _shapeStorage;

        private readonly List<Entity> _dynamicEntities;
        private readonly List<byte> _dynamicShapeSlots;
        private readonly List<BodySnapshot> _dynamicSnapshots;
        private readonly List<Entity> _staticEntities;
        private readonly List<byte> _staticShapeSlots;
        private readonly List<BodySnapshot> _staticSnapshots;

        public List<RigidBodyDesc> RigidBodyDescriptors { get; }
        public List<RigidBodyDesc> DynamicRigidBodyDescriptors { get; }
        public List<RigidBodyDesc> StaticRigidBodyDescriptors { get; }
        public List<Entity> Entities { get; }
        public List<byte> ShapeSlots { get; }
        public List<BodySnapshot> BodySnapshots { get; }

        public int StaticBodyVersion { get; private set; }
        public int DirtyStaticBodyCountLastUpdate { get; private set; }
        public int DirtyStaticBodyCountLastRebuild { get; private set; }
        private int _observedWorldSize = -1;

        public BuildPhysicsWorldSystem2D(World world, ShapeDataStorage2D shapeStorage) : base(world)
        {
            _shapeStorage = shapeStorage ?? throw new ArgumentNullException(nameof(shapeStorage));
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
            BodySnapshots = new List<BodySnapshot>(1024);
            _dynamicEntities = new List<Entity>(1024);
            _dynamicShapeSlots = new List<byte>(1024);
            _dynamicSnapshots = new List<BodySnapshot>(1024);
            _staticEntities = new List<Entity>(1024);
            _staticShapeSlots = new List<byte>(1024);
            _staticSnapshots = new List<BodySnapshot>(1024);
        }

        public override void Update(in float deltaTime)
        {
            DirtyStaticBodyCountLastUpdate = 0;
            DynamicRigidBodyDescriptors.Clear();
            _dynamicEntities.Clear();
            _dynamicShapeSlots.Clear();
            _dynamicSnapshots.Clear();

            bool staticCacheDirty = ConsumeStaticDirtyMarkers();
            if (!staticCacheDirty &&
                _staticEntities.Count > 0 &&
                _observedWorldSize >= 0 &&
                World.Size < _observedWorldSize)
            {
                staticCacheDirty = HasInvalidCachedStaticBodies();
            }

            var collectSingleJob = new CollectSingleBodiesJob { Owner = this };
            World.InlineEntityQuery<CollectSingleBodiesJob, Position2D, Collider2D, Mass2D>(
                in _singleRigidBodyQuery,
                ref collectSingleJob);
            staticCacheDirty |= collectSingleJob.StaticCacheDirty;

            var collectCompoundJob = new CollectCompoundBodiesJob { Owner = this };
            World.InlineEntityQuery<CollectCompoundBodiesJob, Position2D, CompoundObstacle2DState, Mass2D>(
                in _compoundRigidBodyQuery,
                ref collectCompoundJob);
            staticCacheDirty |= collectCompoundJob.StaticCacheDirty;

            if (staticCacheDirty)
            {
                RebuildStaticRigidBodies();
                DirtyStaticBodyCountLastRebuild = DirtyStaticBodyCountLastUpdate;
                StaticBodyVersion = unchecked(StaticBodyVersion + 1);
            }

            BuildCombinedBodySnapshot();
            _observedWorldSize = World.Size;
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
            var job = new ConsumeStaticDirtyJob { Owner = this };
            World.InlineEntityQuery<ConsumeStaticDirtyJob, Physics2DStaticBodyDirty>(
                in _staticDirtyQuery,
                ref job);
            return job.StaticCacheDirty;
        }

        private bool HasInvalidCachedStaticBodies()
        {
            for (int i = 0; i < _staticEntities.Count; i++)
            {
                if (!CanMaterializeStaticBody(_staticEntities[i]))
                {
                    DirtyStaticBodyCountLastUpdate++;
                    return true;
                }
            }

            return false;
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
            _staticSnapshots.Clear();

            var job = new RebuildStaticBodiesJob { Owner = this };
            World.InlineEntityQuery<RebuildStaticBodiesJob, Position2D, Mass2D, Physics2DStaticBodyState>(
                in _trackedStaticBodyQuery,
                ref job);
        }

        private void AddCompoundBodies(
            List<RigidBodyDesc> descriptors,
            List<Entity> entities,
            List<byte> shapeSlots,
            List<BodySnapshot> snapshots,
            Entity entity,
            in Position2D position,
            in CompoundObstacle2DState state,
            bool isStatic)
        {
            Fix64 rotation = ResolveRotation(entity);
            var mass = World.Get<Mass2D>(entity);
            for (int i = 0; i < state.PieceCount; i++)
            {
                var collider = new Collider2D
                {
                    Type = ToColliderType(state.GetShape(i)),
                    ShapeDataIndex = state.GetShapeDataIndex(i)
                };
                var snapshot = CreateSnapshot(entity, checked((byte)i), in position, in collider, in mass);
                Aabb aabb = CalculateAabb(position.Value, rotation, in collider);
                AddBody(
                    descriptors,
                    entities,
                    shapeSlots,
                    snapshots,
                    in snapshot,
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
            List<BodySnapshot> snapshots,
            in BodySnapshot snapshot,
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
            snapshots.Add(snapshot);
        }

        private void BuildCombinedBodySnapshot()
        {
            RigidBodyDescriptors.Clear();
            Entities.Clear();
            ShapeSlots.Clear();
            BodySnapshots.Clear();

            AddCombinedBodies(DynamicRigidBodyDescriptors, _dynamicEntities, _dynamicShapeSlots, _dynamicSnapshots);
            AddCombinedBodies(StaticRigidBodyDescriptors, _staticEntities, _staticShapeSlots, _staticSnapshots);
        }

        private void AddCombinedBodies(
            List<RigidBodyDesc> descriptors,
            List<Entity> entities,
            List<byte> shapeSlots,
            List<BodySnapshot> snapshots)
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                RigidBodyDesc descriptor = descriptors[i];
                descriptor.Index = RigidBodyDescriptors.Count;
                RigidBodyDescriptors.Add(descriptor);
                Entities.Add(entities[i]);
                ShapeSlots.Add(shapeSlots[i]);
                BodySnapshots.Add(snapshots[i]);
            }
        }

        public bool TryGetSnapshot(int bodyIndex, out BodySnapshot snapshot)
        {
            if ((uint)bodyIndex < (uint)BodySnapshots.Count)
            {
                snapshot = BodySnapshots[bodyIndex];
                return true;
            }

            snapshot = default;
            return false;
        }

        private BodySnapshot CreateSnapshot(
            Entity entity,
            byte shapeSlot,
            in Position2D position,
            in Collider2D collider,
            in Mass2D mass)
        {
            bool hasMaterial = World.TryGet(entity, out PhysicsMaterial2D material);
            return new BodySnapshot
            {
                Entity = entity,
                ShapeSlot = shapeSlot,
                Position = position,
                Rotation = new Rotation2D { Value = ResolveRotation(entity) },
                Collider = collider,
                Velocity = World.TryGet(entity, out Velocity2D velocity) ? velocity : Velocity2D.Zero,
                Mass = mass,
                Material = hasMaterial ? material : default,
                HasMaterial = hasMaterial ? (byte)1 : (byte)0,
                IsSleeping = World.Has<SleepingTag>(entity) ? (byte)1 : (byte)0,
                IslandId = World.TryGet(entity, out Island island) ? island.IslandId : -1,
                IsContactEventEmitter = World.Has<ContactEventEmitter2D>(entity) ? (byte)1 : (byte)0
            };
        }

        private Fix64 ResolveRotation(Entity entity)
        {
            return World.TryGet(entity, out Rotation2D rot) ? rot.Value : Fix64.Zero;
        }

        private Aabb CalculateAabb(Fix64Vec2 worldPos, Fix64 rotation, in Collider2D collider)
        {
            return collider.Type switch
            {
                ColliderType2D.Circle => CalculateCircleAabb(worldPos, rotation, collider.ShapeDataIndex),
                ColliderType2D.Box => CalculateBoxAabb(worldPos, rotation, collider.ShapeDataIndex),
                ColliderType2D.Polygon => CalculatePolygonAabb(worldPos, rotation, collider.ShapeDataIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(collider.Type), collider.Type, "Unknown collider type")
            };
        }

        private Aabb CalculateCircleAabb(Fix64Vec2 worldPos, Fix64 rotation, int shapeIndex)
        {
            if (!_shapeStorage.TryGetCircle(shapeIndex, out var circleData))
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

        private Aabb CalculateBoxAabb(Fix64Vec2 worldPos, Fix64 rotation, int shapeIndex)
        {
            if (!_shapeStorage.TryGetBox(shapeIndex, out var boxData))
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

        private Aabb CalculatePolygonAabb(Fix64Vec2 worldPos, Fix64 rotation, int shapeIndex)
        {
            if (!_shapeStorage.TryGetPolygon(shapeIndex, out var polygonData) ||
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

        private struct CollectSingleBodiesJob : IForEachWithEntity<Position2D, Collider2D, Mass2D>
        {
            public BuildPhysicsWorldSystem2D Owner;
            public bool StaticCacheDirty;

            public void Update(Entity entity, ref Position2D position, ref Collider2D collider, ref Mass2D mass)
            {
                if (mass.IsStatic)
                {
                    Owner.MaterializeNewStaticBody(entity);
                    StaticCacheDirty = true;
                    return;
                }

                var snapshot = Owner.CreateSnapshot(entity, shapeSlot: 0, in position, in collider, in mass);
                Aabb aabb = Owner.CalculateAabb(position.Value, snapshot.Rotation.Value, in collider);
                AddBody(
                    Owner.DynamicRigidBodyDescriptors,
                    Owner._dynamicEntities,
                    Owner._dynamicShapeSlots,
                    Owner._dynamicSnapshots,
                    in snapshot,
                    entity,
                    shapeSlot: 0,
                    in aabb,
                    isStatic: false);
            }
        }

        private struct CollectCompoundBodiesJob : IForEachWithEntity<Position2D, CompoundObstacle2DState, Mass2D>
        {
            public BuildPhysicsWorldSystem2D Owner;
            public bool StaticCacheDirty;

            public void Update(Entity entity, ref Position2D position, ref CompoundObstacle2DState state, ref Mass2D mass)
            {
                if (state.SinkPhysicsCollider == 0)
                {
                    return;
                }

                if (mass.IsStatic)
                {
                    Owner.MaterializeNewStaticBody(entity);
                    StaticCacheDirty = true;
                    return;
                }

                Owner.AddCompoundBodies(
                    Owner.DynamicRigidBodyDescriptors,
                    Owner._dynamicEntities,
                    Owner._dynamicShapeSlots,
                    Owner._dynamicSnapshots,
                    entity,
                    in position,
                    in state,
                    isStatic: false);
            }
        }

        private struct ConsumeStaticDirtyJob : IForEachWithEntity<Physics2DStaticBodyDirty>
        {
            public BuildPhysicsWorldSystem2D Owner;
            public bool StaticCacheDirty;

            public void Update(Entity entity, ref Physics2DStaticBodyDirty dirty)
            {
                Owner.DirtyStaticBodyCountLastUpdate++;
                StaticCacheDirty = true;

                if (!Owner.CanMaterializeStaticBody(entity))
                {
                    Owner.RemoveIfPresent<Physics2DStaticBodyState>(entity);
                }

                Owner.RemoveIfPresent<Physics2DStaticBodyDirty>(entity);
            }
        }

        private struct RebuildStaticBodiesJob : IForEachWithEntity<Position2D, Mass2D, Physics2DStaticBodyState>
        {
            public BuildPhysicsWorldSystem2D Owner;

            public void Update(Entity entity, ref Position2D position, ref Mass2D mass, ref Physics2DStaticBodyState state)
            {
                if (!mass.IsStatic)
                {
                    return;
                }

                if (Owner.World.TryGet(entity, out CompoundObstacle2DState compoundState))
                {
                    if (compoundState.SinkPhysicsCollider == 0)
                    {
                        return;
                    }

                    Owner.AddCompoundBodies(
                        Owner.StaticRigidBodyDescriptors,
                        Owner._staticEntities,
                        Owner._staticShapeSlots,
                        Owner._staticSnapshots,
                        entity,
                        in position,
                        in compoundState,
                        isStatic: true);
                    return;
                }

                if (!Owner.World.TryGet(entity, out Collider2D collider))
                {
                    return;
                }

                var snapshot = Owner.CreateSnapshot(entity, shapeSlot: 0, in position, in collider, in mass);
                Aabb aabb = Owner.CalculateAabb(position.Value, snapshot.Rotation.Value, in collider);
                AddBody(
                    Owner.StaticRigidBodyDescriptors,
                    Owner._staticEntities,
                    Owner._staticShapeSlots,
                    Owner._staticSnapshots,
                    in snapshot,
                    entity,
                    shapeSlot: 0,
                    in aabb,
                    isStatic: true);
            }
        }
    }
}
