using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Physics.Broadphase;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public readonly struct PhysicsBodyColliderDesc2D
    {
        public readonly ColliderType2D Type;
        public readonly int ShapeDataIndex;

        public PhysicsBodyColliderDesc2D(ColliderType2D type, int shapeDataIndex)
        {
            Type = type;
            ShapeDataIndex = shapeDataIndex;
        }
    }

    /// <summary>
    /// Builds dynamic rigid body descriptors every physics step and keeps static descriptors cached.
    /// </summary>
    public sealed class BuildPhysicsWorldSystem2D : BaseSystem<World, float>
    {
        private readonly QueryDescription _untrackedSingleQuery;
        private readonly QueryDescription _untrackedCompoundQuery;
        private readonly QueryDescription _dirtySingleQuery;
        private readonly QueryDescription _dirtyCompoundQuery;
        private readonly QueryDescription _dirtyMissingColliderQuery;

        private readonly CommandBuffer _commandBuffer = new(256);
        private readonly Stopwatch _stopwatch = new();

        public List<RigidBodyDesc> RigidBodyDescriptors { get; }
        public List<Entity> Entities { get; }
        public List<PhysicsBodyColliderDesc2D> BodyColliders { get; }

        public List<RigidBodyDesc> DynamicRigidBodyDescriptors { get; }
        public List<Entity> DynamicEntities { get; }
        public List<PhysicsBodyColliderDesc2D> DynamicBodyColliders { get; }

        public List<RigidBodyDesc> StaticRigidBodyDescriptors { get; }
        public List<Entity> StaticEntities { get; }
        public List<PhysicsBodyColliderDesc2D> StaticBodyColliders { get; }

        public int StaticBodyVersion { get; private set; }
        public int DirtyStaticBodyCountLastUpdate { get; private set; }
        public double StaticMaterializationMsLastUpdate { get; private set; }
        public double DynamicBuildMsLastUpdate { get; private set; }

        public BuildPhysicsWorldSystem2D(World world) : base(world)
        {
            _untrackedSingleQuery = new QueryDescription()
                .WithAll<Position2D, Collider2D, Mass2D>()
                .WithNone<Physics2DStaticBodyState, CompoundCollider2D>();
            _untrackedCompoundQuery = new QueryDescription()
                .WithAll<Position2D, CompoundCollider2D, Mass2D>()
                .WithNone<Physics2DStaticBodyState>();
            _dirtySingleQuery = new QueryDescription()
                .WithAll<Physics2DStaticBodyState, Physics2DStaticBodyDirty, Position2D, Collider2D, Mass2D>()
                .WithNone<CompoundCollider2D>();
            _dirtyCompoundQuery = new QueryDescription()
                .WithAll<Physics2DStaticBodyState, Physics2DStaticBodyDirty, Position2D, CompoundCollider2D, Mass2D>();
            _dirtyMissingColliderQuery = new QueryDescription()
                .WithAll<Physics2DStaticBodyState, Physics2DStaticBodyDirty>()
                .WithNone<Collider2D, CompoundCollider2D>();

            RigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            Entities = new List<Entity>(1024);
            BodyColliders = new List<PhysicsBodyColliderDesc2D>(1024);

            DynamicRigidBodyDescriptors = RigidBodyDescriptors;
            DynamicEntities = Entities;
            DynamicBodyColliders = BodyColliders;

            StaticRigidBodyDescriptors = new List<RigidBodyDesc>(1024);
            StaticEntities = new List<Entity>(1024);
            StaticBodyColliders = new List<PhysicsBodyColliderDesc2D>(1024);
        }

        public override void Update(in float deltaTime)
        {
            DirtyStaticBodyCountLastUpdate = 0;
            StaticMaterializationMsLastUpdate = 0d;
            DynamicBuildMsLastUpdate = 0d;

            DynamicRigidBodyDescriptors.Clear();
            DynamicEntities.Clear();
            DynamicBodyColliders.Clear();

            _stopwatch.Restart();
            ProcessUntrackedSingleBodies();
            ProcessUntrackedCompoundBodies();
            _stopwatch.Stop();
            DynamicBuildMsLastUpdate = _stopwatch.Elapsed.TotalMilliseconds;

            _stopwatch.Restart();
            ProcessDirtySingleBodies();
            ProcessDirtyCompoundBodies();
            ProcessDirtyMissingColliderBodies();
            _stopwatch.Stop();
            StaticMaterializationMsLastUpdate = _stopwatch.Elapsed.TotalMilliseconds;

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }

            if (DirtyStaticBodyCountLastUpdate > 0)
            {
                StaticBodyVersion++;
            }
        }

        public bool TryResolveBody(
            int bodyHandle,
            out Entity entity,
            out PhysicsBodyColliderDesc2D collider,
            out bool isStatic)
        {
            if (bodyHandle >= 0)
            {
                isStatic = false;
                if ((uint)bodyHandle < (uint)DynamicEntities.Count)
                {
                    entity = DynamicEntities[bodyHandle];
                    collider = DynamicBodyColliders[bodyHandle];
                    return true;
                }
            }
            else
            {
                isStatic = true;
                int staticIndex = StaticIndexFromHandle(bodyHandle);
                if ((uint)staticIndex < (uint)StaticEntities.Count)
                {
                    entity = StaticEntities[staticIndex];
                    collider = StaticBodyColliders[staticIndex];
                    return true;
                }
            }

            entity = default;
            collider = default;
            isStatic = false;
            return false;
        }

        public Entity ResolveBodyEntity(int bodyHandle)
        {
            if (!TryResolveBody(bodyHandle, out Entity entity, out _, out _))
            {
                throw new ArgumentOutOfRangeException(nameof(bodyHandle));
            }

            return entity;
        }

        private void ProcessUntrackedSingleBodies()
        {
            World.Query(in _untrackedSingleQuery, (Entity entity, ref Position2D position, ref Collider2D collider, ref Mass2D mass) =>
            {
                if (mass.IsStatic)
                {
                    RemoveStaticBodies(entity);
                    AddStaticBody(entity, CalculateAabb(in position, ResolveRotation(entity), in collider), new PhysicsBodyColliderDesc2D(collider.Type, collider.ShapeDataIndex));
                    MarkStaticState(entity, bodyCount: 1);
                    DirtyStaticBodyCountLastUpdate++;
                    return;
                }

                AddDynamicBody(entity, CalculateAabb(in position, ResolveRotation(entity), in collider), new PhysicsBodyColliderDesc2D(collider.Type, collider.ShapeDataIndex));
            });
        }

        private void ProcessUntrackedCompoundBodies()
        {
            World.Query(in _untrackedCompoundQuery, (Entity entity, ref Position2D position, ref CompoundCollider2D compound, ref Mass2D mass) =>
            {
                if (mass.IsStatic)
                {
                    RemoveStaticBodies(entity);
                    int bodyCount = AddCompoundStaticBodies(entity, in position, ResolveRotation(entity), in compound);
                    MarkStaticState(entity, bodyCount);
                    DirtyStaticBodyCountLastUpdate++;
                    return;
                }

                AddCompoundDynamicBodies(entity, in position, ResolveRotation(entity), in compound);
            });
        }

        private void ProcessDirtySingleBodies()
        {
            World.Query(in _dirtySingleQuery, (Entity entity, ref Position2D position, ref Collider2D collider, ref Mass2D mass) =>
            {
                RemoveStaticBodies(entity);
                DirtyStaticBodyCountLastUpdate++;
                if (mass.IsStatic)
                {
                    AddStaticBody(entity, CalculateAabb(in position, ResolveRotation(entity), in collider), new PhysicsBodyColliderDesc2D(collider.Type, collider.ShapeDataIndex));
                    MarkStaticState(entity, bodyCount: 1);
                }
                else
                {
                    AddDynamicBody(entity, CalculateAabb(in position, ResolveRotation(entity), in collider), new PhysicsBodyColliderDesc2D(collider.Type, collider.ShapeDataIndex));
                    _commandBuffer.Remove<Physics2DStaticBodyState>(entity);
                }

                _commandBuffer.Remove<Physics2DStaticBodyDirty>(entity);
            });
        }

        private void ProcessDirtyCompoundBodies()
        {
            World.Query(in _dirtyCompoundQuery, (Entity entity, ref Position2D position, ref CompoundCollider2D compound, ref Mass2D mass) =>
            {
                RemoveStaticBodies(entity);
                DirtyStaticBodyCountLastUpdate++;
                if (mass.IsStatic)
                {
                    int bodyCount = AddCompoundStaticBodies(entity, in position, ResolveRotation(entity), in compound);
                    MarkStaticState(entity, bodyCount);
                }
                else
                {
                    AddCompoundDynamicBodies(entity, in position, ResolveRotation(entity), in compound);
                    _commandBuffer.Remove<Physics2DStaticBodyState>(entity);
                }

                _commandBuffer.Remove<Physics2DStaticBodyDirty>(entity);
            });
        }

        private void ProcessDirtyMissingColliderBodies()
        {
            World.Query(in _dirtyMissingColliderQuery, (Entity entity) =>
            {
                RemoveStaticBodies(entity);
                DirtyStaticBodyCountLastUpdate++;
                _commandBuffer.Remove<Physics2DStaticBodyState>(entity);
                _commandBuffer.Remove<Physics2DStaticBodyDirty>(entity);
            });
        }

        private int AddCompoundStaticBodies(Entity entity, in Position2D position, Fix64 rotation, in CompoundCollider2D compound)
        {
            int added = 0;
            for (int i = 0; i < compound.PieceCount; i++)
            {
                var (type, shapeDataIndex) = compound.GetPiece(i);
                var collider = new Collider2D { Type = type, ShapeDataIndex = shapeDataIndex };
                AddStaticBody(entity, CalculateAabb(in position, rotation, in collider), new PhysicsBodyColliderDesc2D(type, shapeDataIndex));
                added++;
            }

            return added;
        }

        private void AddCompoundDynamicBodies(Entity entity, in Position2D position, Fix64 rotation, in CompoundCollider2D compound)
        {
            for (int i = 0; i < compound.PieceCount; i++)
            {
                var (type, shapeDataIndex) = compound.GetPiece(i);
                var collider = new Collider2D { Type = type, ShapeDataIndex = shapeDataIndex };
                AddDynamicBody(entity, CalculateAabb(in position, rotation, in collider), new PhysicsBodyColliderDesc2D(type, shapeDataIndex));
            }
        }

        private void AddDynamicBody(Entity entity, in Aabb aabb, in PhysicsBodyColliderDesc2D collider)
        {
            int index = DynamicEntities.Count;
            DynamicRigidBodyDescriptors.Add(new RigidBodyDesc
            {
                Index = index,
                EntityIndex = entity.Id,
                BoundingBox = aabb,
                IsStatic = false
            });
            DynamicEntities.Add(entity);
            DynamicBodyColliders.Add(collider);
        }

        private void AddStaticBody(Entity entity, in Aabb aabb, in PhysicsBodyColliderDesc2D collider)
        {
            int index = StaticEntities.Count;
            StaticRigidBodyDescriptors.Add(new RigidBodyDesc
            {
                Index = StaticHandleFromIndex(index),
                EntityIndex = entity.Id,
                BoundingBox = aabb,
                IsStatic = true
            });
            StaticEntities.Add(entity);
            StaticBodyColliders.Add(collider);
        }

        private bool RemoveStaticBodies(Entity entity)
        {
            bool removed = false;
            for (int i = StaticEntities.Count - 1; i >= 0; i--)
            {
                if (StaticEntities[i].Id != entity.Id)
                {
                    continue;
                }

                RemoveStaticBodyAt(i);
                removed = true;
            }

            return removed;
        }

        private void RemoveStaticBodyAt(int index)
        {
            int last = StaticEntities.Count - 1;
            if (index != last)
            {
                StaticRigidBodyDescriptors[index] = StaticRigidBodyDescriptors[last];
                var descriptor = StaticRigidBodyDescriptors[index];
                descriptor.Index = StaticHandleFromIndex(index);
                StaticRigidBodyDescriptors[index] = descriptor;
                StaticEntities[index] = StaticEntities[last];
                StaticBodyColliders[index] = StaticBodyColliders[last];
            }

            StaticRigidBodyDescriptors.RemoveAt(last);
            StaticEntities.RemoveAt(last);
            StaticBodyColliders.RemoveAt(last);
        }

        private void MarkStaticState(Entity entity, int bodyCount)
        {
            var state = new Physics2DStaticBodyState
            {
                BodyCount = bodyCount
            };

            if (World.Has<Physics2DStaticBodyState>(entity))
            {
                _commandBuffer.Set(entity, state);
            }
            else
            {
                _commandBuffer.Add(entity, state);
            }

            _commandBuffer.Remove<Physics2DStaticBodyDirty>(entity);
        }

        private Fix64 ResolveRotation(Entity entity)
        {
            return World.TryGet(entity, out Rotation2D rot) ? rot.Value : Fix64.Zero;
        }

        private static int StaticHandleFromIndex(int index)
        {
            return -index - 1;
        }

        private static int StaticIndexFromHandle(int bodyHandle)
        {
            return -bodyHandle - 1;
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
