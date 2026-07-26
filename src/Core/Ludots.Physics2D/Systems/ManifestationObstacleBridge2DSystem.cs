using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Bridges runtime manifestation blocker intent into lower-layer physics and navigation components.
    /// Static manifestations are materialized once and revisited only through explicit dirty state.
    /// </summary>
    public sealed class ManifestationObstacleBridge2DSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _newSingleQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D>()
            .WithNone<ManifestationObstacleBridge2DState>()
            .WithNone<CompoundObstacle2D>();

        private static readonly QueryDescription _dirtySingleQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState, ManifestationObstacleBridge2DDirty>()
            .WithNone<CompoundObstacle2D>();

        private static readonly QueryDescription _movingSingleQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState, ManifestationMotion2D>()
            .WithNone<CompoundObstacle2D>();

        private static readonly QueryDescription _newCompoundQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CompoundObstacle2D>()
            .WithNone<CompoundObstacle2DState>();

        private static readonly QueryDescription _dirtyCompoundQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CompoundObstacle2D, CompoundObstacle2DState, ManifestationObstacleBridge2DDirty>();

        private static readonly QueryDescription _movingCompoundQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CompoundObstacle2D, CompoundObstacle2DState, ManifestationMotion2D>();

        private readonly ShapeDataStorage2D _shapeStorage;

        public ManifestationObstacleBridge2DSystem(World world, ShapeDataStorage2D shapeStorage) : base(world)
        {
            _shapeStorage = shapeStorage ?? throw new ArgumentNullException(nameof(shapeStorage));
        }

        public override void Update(in float dt)
        {
            World.Query(in _newSingleQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                MaterializeSingle(entity, in worldPosition, in intent, removeDirty: false);
            });

            World.Query(in _dirtySingleQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                MaterializeSingle(entity, in worldPosition, in intent, removeDirty: true);
            });

            World.Query(in _movingSingleQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                MaterializeSingle(entity, in worldPosition, in intent, removeDirty: false);
            });

            World.Query(in _newCompoundQuery, (Entity entity, ref WorldPositionCm worldPosition, ref CompoundObstacle2D obstacle) =>
            {
                MaterializeCompound(entity, in worldPosition, in obstacle, removeDirty: false);
            });

            World.Query(in _dirtyCompoundQuery, (Entity entity, ref WorldPositionCm worldPosition, ref CompoundObstacle2D obstacle) =>
            {
                MaterializeCompound(entity, in worldPosition, in obstacle, removeDirty: true);
            });

            World.Query(in _movingCompoundQuery, (Entity entity, ref WorldPositionCm worldPosition, ref CompoundObstacle2D obstacle) =>
            {
                MaterializeCompound(entity, in worldPosition, in obstacle, removeDirty: false);
            });
        }

        private void MaterializeSingle(
            Entity entity,
            in WorldPositionCm worldPosition,
            in ManifestationObstacleIntent2D intent,
            bool removeDirty)
        {
            if (intent.SinkNavigationObstacle != 0 && intent.NavMinYcm >= intent.NavMaxYcm)
            {
                throw new InvalidOperationException(
                    $"ManifestationObstacleIntent2D entity {entity.Id} requires navMinYcm < navMaxYcm when sinkNavigationObstacle is enabled.");
            }

            int shapeSignature = ComputeShapeSignature(in intent, entity);
            int poseSignature = ComputePoseSignature(in worldPosition, entity);
            int sinkSignature = ComputeSinkSignature(intent.SinkPhysicsCollider, intent.SinkNavigationObstacle);

            bool hasState = World.TryGet(entity, out ManifestationObstacleBridge2DState previousState);
            bool unchanged = hasState &&
                previousState.ShapeSignature == shapeSignature &&
                previousState.PoseSignature == poseSignature &&
                previousState.SinkSignature == sinkSignature;
            if (unchanged)
            {
                if (removeDirty)
                {
                    RemoveIfPresent<ManifestationObstacleBridge2DDirty>(entity);
                }

                return;
            }

            UpsertPose(entity, in worldPosition);

            bool wantsPhysicsSink = intent.SinkPhysicsCollider != 0;
            bool wantsNavigationSink = intent.SinkNavigationObstacle != 0;
            bool hadPhysicsSink = hasState && HasPhysicsSink(previousState.SinkSignature);
            bool hadNavigationSink = hasState && HasNavigationSink(previousState.SinkSignature);
            int shapeDataIndex = -1;
            if (wantsPhysicsSink || wantsNavigationSink)
            {
                shapeDataIndex = EnsureShapeRegistered(entity, in intent, shapeSignature, in previousState, hasState);
            }
            else if (hasState && previousState.ShapeSignature == shapeSignature)
            {
                shapeDataIndex = previousState.ShapeDataIndex;
            }

            RemoveIfPresent<CompoundObstacle2DState>(entity);

            if (wantsPhysicsSink)
            {
                Upsert(entity, new Collider2D
                {
                    Type = ToColliderType(intent.Shape),
                    ShapeDataIndex = shapeDataIndex
                });
                Upsert(entity, Mass2D.Static);
                Upsert(entity, Velocity2D.Zero);
                MarkStaticBodyActive(entity);
            }
            else
            {
                if (hadPhysicsSink || World.Has<Physics2DStaticBodyState>(entity))
                {
                    RemovePhysicsDerivedState(entity);
                    MarkStaticBodyRemoved(entity);
                }
            }

            if (wantsNavigationSink)
            {
                Upsert(entity, BuildSingleMassNavigationFlowProjection(
                    entity,
                    in intent,
                    shapeDataIndex,
                    ResolveRotation(entity),
                    shapeSignature,
                    poseSignature));
            }
            else
            {
                if (hadNavigationSink)
                {
                    RemoveNavigationDerivedState(entity);
                }

                RemoveIfPresent<MassNavigationFlowObstacleProjection>(entity);
            }

            Upsert(entity, new ManifestationObstacleBridge2DState
            {
                ShapeDataIndex = shapeDataIndex,
                ShapeSignature = shapeSignature,
                PoseSignature = poseSignature,
                SinkSignature = sinkSignature
            });

            if (removeDirty)
            {
                RemoveIfPresent<ManifestationObstacleBridge2DDirty>(entity);
            }
        }

        private void MaterializeCompound(
            Entity entity,
            in WorldPositionCm worldPosition,
            in CompoundObstacle2D obstacle,
            bool removeDirty)
        {
            if (World.Has<ManifestationObstacleIntent2D>(entity))
            {
                throw new InvalidOperationException("Entity must not author both ManifestationObstacleIntent2D and CompoundObstacle2D.");
            }

            int shapeSignature = ComputeCompoundShapeSignature(in obstacle);
            int poseSignature = ComputePoseSignature(in worldPosition, entity);
            int sinkSignature = ComputeSinkSignature(obstacle.SinkPhysicsCollider, obstacle.SinkNavigationObstacle);

            bool hasState = World.TryGet(entity, out CompoundObstacle2DState previousState);
            bool unchanged = hasState &&
                previousState.ShapeSignature == shapeSignature &&
                previousState.PoseSignature == poseSignature &&
                ComputeSinkSignature(previousState.SinkPhysicsCollider, previousState.SinkNavigationObstacle) == sinkSignature;
            if (unchanged)
            {
                if (removeDirty)
                {
                    RemoveIfPresent<ManifestationObstacleBridge2DDirty>(entity);
                }

                return;
            }

            UpsertPose(entity, in worldPosition);

            CompoundObstacle2DState state = EnsureCompoundStateRegistered(entity, in obstacle, shapeSignature);
            bool hadPhysicsSink = hasState && previousState.SinkPhysicsCollider != 0;
            bool hadNavigationSink = hasState && previousState.SinkNavigationObstacle != 0;
            if (state.PoseSignature != poseSignature ||
                state.SinkPhysicsCollider != obstacle.SinkPhysicsCollider ||
                state.SinkNavigationObstacle != obstacle.SinkNavigationObstacle)
            {
                state.PoseSignature = poseSignature;
                state.SinkPhysicsCollider = obstacle.SinkPhysicsCollider;
                state.SinkNavigationObstacle = obstacle.SinkNavigationObstacle;
                Upsert(entity, state);
            }

            RemoveIfPresent<Collider2D>(entity);
            RemoveIfPresent<ManifestationObstacleBridge2DState>(entity);

            if (obstacle.SinkPhysicsCollider != 0)
            {
                Upsert(entity, Mass2D.Static);
                Upsert(entity, Velocity2D.Zero);
                MarkStaticBodyActive(entity);
            }
            else
            {
                if (hadPhysicsSink || World.Has<Physics2DStaticBodyState>(entity))
                {
                    RemovePhysicsDerivedState(entity);
                    MarkStaticBodyRemoved(entity);
                }
            }

            if (obstacle.SinkNavigationObstacle != 0)
            {
                Upsert(entity, BuildCompoundMassNavigationFlowProjection(entity, in state));
            }
            else
            {
                if (hadNavigationSink)
                {
                    RemoveNavigationDerivedState(entity);
                }

                RemoveIfPresent<MassNavigationFlowObstacleProjection>(entity);
            }

            if (removeDirty)
            {
                RemoveIfPresent<ManifestationObstacleBridge2DDirty>(entity);
            }
        }

        private void UpsertPose(Entity entity, in WorldPositionCm worldPosition)
        {
            Upsert(entity, new Position2D { Value = worldPosition.Value });

            if (World.TryGet(entity, out FacingDirection facing))
            {
                Upsert(entity, new Rotation2D { Value = Fix64.FromFloat(facing.AngleRad) });
            }
        }

        private int EnsureShapeRegistered(
            Entity entity,
            in ManifestationObstacleIntent2D intent,
            int signature,
            in ManifestationObstacleBridge2DState previousState,
            bool hasState)
        {
            if (hasState &&
                previousState.ShapeSignature == signature &&
                previousState.ShapeDataIndex >= 0)
            {
                return previousState.ShapeDataIndex;
            }

            return RegisterShape(entity, in intent);
        }

        private int RegisterShape(Entity entity, in ManifestationObstacleIntent2D intent)
        {
            return intent.Shape switch
            {
                ManifestationObstacleShape2D.Circle => _shapeStorage.RegisterCircle(
                    Fix64.FromInt(intent.RadiusCm),
                    Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm)),
                ManifestationObstacleShape2D.Box => _shapeStorage.RegisterBox(
                    Fix64.FromInt(intent.HalfWidthCm),
                    Fix64.FromInt(intent.HalfHeightCm),
                    Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm)),
                ManifestationObstacleShape2D.Polygon => RegisterPolygon(entity, in intent),
                _ => throw new InvalidOperationException($"Unsupported manifestation obstacle shape '{intent.Shape}'.")
            };
        }

        private int RegisterPolygon(Entity entity, in ManifestationObstacleIntent2D intent)
        {
            if (!World.TryGet(entity, out ManifestationObstaclePolygon2D polygon))
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D with Polygon shape requires ManifestationObstaclePolygon2D.");
            }

            int count = polygon.VertexCount;
            if (count < 3 || count > ManifestationObstaclePolygon2D.MaxVertices)
            {
                throw new InvalidOperationException($"ManifestationObstaclePolygon2D vertex count must be between 3 and {ManifestationObstaclePolygon2D.MaxVertices}.");
            }

            var vertices = new Fix64Vec2[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = ToFix64Vec2(polygon.GetVertex(i));
            }

            return _shapeStorage.RegisterPolygon(
                vertices,
                Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm));
        }

        private int ComputeShapeSignature(in ManifestationObstacleIntent2D intent, Entity entity)
        {
            int hash = BeginSignature();
            hash = MixSignature(hash, (byte)intent.Shape);
            hash = MixSignature(hash, intent.RadiusCm);
            hash = MixSignature(hash, intent.HalfWidthCm);
            hash = MixSignature(hash, intent.HalfHeightCm);
            hash = MixSignature(hash, intent.LocalOffsetXCm);
            hash = MixSignature(hash, intent.LocalOffsetYCm);
            hash = MixSignature(hash, intent.NavRadiusCm);
            hash = MixSignature(hash, intent.NavMinYcm);
            hash = MixSignature(hash, intent.NavMaxYcm);

            if (intent.Shape == ManifestationObstacleShape2D.Polygon &&
                World.TryGet(entity, out ManifestationObstaclePolygon2D polygon))
            {
                hash = MixSignature(hash, polygon.VertexCount);
                for (int i = 0; i < polygon.VertexCount; i++)
                {
                    var vertex = polygon.GetVertex(i);
                    hash = MixSignature(hash, vertex.X);
                    hash = MixSignature(hash, vertex.Y);
                }
            }

            return hash;
        }

        private CompoundObstacle2DState EnsureCompoundStateRegistered(
            Entity entity,
            in CompoundObstacle2D obstacle,
            int signature)
        {
            if (obstacle.PieceCount == 0)
            {
                throw new InvalidOperationException("CompoundObstacle2D requires at least one obstacle piece.");
            }

            if (World.TryGet(entity, out CompoundObstacle2DState state) &&
                state.ShapeSignature == signature &&
                state.PieceCount == obstacle.PieceCount)
            {
                return state;
            }

            state = RegisterCompoundState(in obstacle, signature);
            Upsert(entity, state);
            return state;
        }

        private CompoundObstacle2DState RegisterCompoundState(in CompoundObstacle2D obstacle, int signature)
        {
            var state = new CompoundObstacle2DState
            {
                ShapeSignature = signature,
                SinkPhysicsCollider = obstacle.SinkPhysicsCollider,
                SinkNavigationObstacle = obstacle.SinkNavigationObstacle
            };

            for (int i = 0; i < obstacle.PieceCount; i++)
            {
                ManifestationObstacleShape2D shape = obstacle.GetShape(i);
                int shapeDataIndex = RegisterCompoundShape(in obstacle, i, shape);
                int navRadiusCm = ResolveCompoundPieceNavRadiusCm(in obstacle, i, shape, shapeDataIndex);
                int navMinYcm = obstacle.GetNavMinYcm(i);
                int navMaxYcm = obstacle.GetNavMaxYcm(i);
                if (obstacle.SinkNavigationObstacle != 0 && navMinYcm >= navMaxYcm)
                {
                    throw new InvalidOperationException(
                        $"CompoundObstacle2D piece {i} requires navMinYcm < navMaxYcm when sinkNavigationObstacle is enabled.");
                }

                state.SetPiece(i, shape, shapeDataIndex, navRadiusCm, navMinYcm, navMaxYcm);
            }

            return state;
        }

        private int RegisterCompoundShape(
            in CompoundObstacle2D obstacle,
            int pieceIndex,
            ManifestationObstacleShape2D shape)
        {
            return shape switch
            {
                ManifestationObstacleShape2D.Circle => _shapeStorage.RegisterCircle(
                    Fix64.FromInt(obstacle.GetRadiusCm(pieceIndex)),
                    Fix64Vec2.FromInt(obstacle.GetLocalOffsetXCm(pieceIndex), obstacle.GetLocalOffsetYCm(pieceIndex))),
                ManifestationObstacleShape2D.Box => _shapeStorage.RegisterBox(
                    Fix64.FromInt(obstacle.GetHalfWidthCm(pieceIndex)),
                    Fix64.FromInt(obstacle.GetHalfHeightCm(pieceIndex)),
                    Fix64Vec2.FromInt(obstacle.GetLocalOffsetXCm(pieceIndex), obstacle.GetLocalOffsetYCm(pieceIndex))),
                ManifestationObstacleShape2D.Polygon => RegisterCompoundPolygon(in obstacle, pieceIndex),
                _ => throw new InvalidOperationException($"Unsupported compound obstacle shape '{shape}'.")
            };
        }

        private int RegisterCompoundPolygon(in CompoundObstacle2D obstacle, int pieceIndex)
        {
            int count = obstacle.GetPolygonVertexCount(pieceIndex);
            if (count < 3 || count > CompoundObstacle2D.MaxVerticesPerPolygon)
            {
                throw new InvalidOperationException(
                    $"CompoundObstacle2D polygon vertex count must be between 3 and {CompoundObstacle2D.MaxVerticesPerPolygon}.");
            }

            var vertices = new Fix64Vec2[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = ToFix64Vec2(obstacle.GetVertex(pieceIndex, i));
            }

            return _shapeStorage.RegisterPolygon(
                vertices,
                Fix64Vec2.FromInt(obstacle.GetLocalOffsetXCm(pieceIndex), obstacle.GetLocalOffsetYCm(pieceIndex)));
        }

        private static int ComputeCompoundShapeSignature(in CompoundObstacle2D obstacle)
        {
            int hash = BeginSignature();
            hash = MixSignature(hash, obstacle.PieceCount);
            for (int i = 0; i < obstacle.PieceCount; i++)
            {
                ManifestationObstacleShape2D shape = obstacle.GetShape(i);
                hash = MixSignature(hash, (byte)shape);
                hash = MixSignature(hash, obstacle.GetRadiusCm(i));
                hash = MixSignature(hash, obstacle.GetHalfWidthCm(i));
                hash = MixSignature(hash, obstacle.GetHalfHeightCm(i));
                hash = MixSignature(hash, obstacle.GetLocalOffsetXCm(i));
                hash = MixSignature(hash, obstacle.GetLocalOffsetYCm(i));
                hash = MixSignature(hash, obstacle.GetNavRadiusCm(i));
                hash = MixSignature(hash, obstacle.GetNavMinYcm(i));
                hash = MixSignature(hash, obstacle.GetNavMaxYcm(i));

                if (shape == ManifestationObstacleShape2D.Polygon)
                {
                    int vertexCount = obstacle.GetPolygonVertexCount(i);
                    hash = MixSignature(hash, vertexCount);
                    for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        var vertex = obstacle.GetVertex(i, vertexIndex);
                        hash = MixSignature(hash, vertex.X);
                        hash = MixSignature(hash, vertex.Y);
                    }
                }
            }

            return hash;
        }

        private int ComputePoseSignature(in WorldPositionCm worldPosition, Entity entity)
        {
            int hash = BeginSignature();
            hash = MixSignature(hash, worldPosition.Value.X.RawValue);
            hash = MixSignature(hash, worldPosition.Value.Y.RawValue);
            hash = MixSignature(hash, World.TryGet(entity, out FacingDirection facing)
                ? BitConverter.SingleToInt32Bits(facing.AngleRad)
                : 0);
            return hash;
        }

        private static int ComputeSinkSignature(byte physics, byte navigation)
        {
            return (physics & 0x1) |
                   ((navigation & 0x1) << 1);
        }

        private static bool HasPhysicsSink(int sinkSignature)
        {
            return (sinkSignature & 0x1) != 0;
        }

        private static bool HasNavigationSink(int sinkSignature)
        {
            return (sinkSignature & 0x2) != 0;
        }

        private static int BeginSignature()
        {
            return unchecked((int)2166136261);
        }

        private static int MixSignature(int hash, int value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 16777619;
            }
        }

        private static int MixSignature(int hash, long value)
        {
            hash = MixSignature(hash, (int)value);
            return MixSignature(hash, (int)(value >> 32));
        }

        private Fix64 ResolveNavRadiusCm(Entity entity, in ManifestationObstacleIntent2D intent, int shapeDataIndex)
        {
            if (intent.NavRadiusCm > 0)
            {
                return Fix64.FromInt(intent.NavRadiusCm);
            }

            return intent.Shape switch
            {
                ManifestationObstacleShape2D.Circle when _shapeStorage.TryGetCircle(shapeDataIndex, out var circle) => circle.Radius,
                ManifestationObstacleShape2D.Box when _shapeStorage.TryGetBox(shapeDataIndex, out var box) =>
                    Fix64Math.Sqrt(box.HalfWidth * box.HalfWidth + box.HalfHeight * box.HalfHeight),
                ManifestationObstacleShape2D.Polygon when _shapeStorage.TryGetPolygon(shapeDataIndex, out var polygon) => ResolvePolygonRadius(polygon),
                _ => Fix64.Zero
            };
        }

        private static Fix64 ResolvePolygonRadius(in PolygonShapeData polygon)
        {
            Fix64 maxDistanceSq = Fix64.Zero;
            for (int i = 0; i < polygon.VertexCount; i++)
            {
                Fix64Vec2 delta = ShapeWorldTransform2D.GetPolygonLocalVertex(polygon, i);
                Fix64 distanceSq = delta.LengthSquared();
                if (distanceSq > maxDistanceSq)
                {
                    maxDistanceSq = distanceSq;
                }
            }

            return maxDistanceSq > Fix64.Zero ? Fix64Math.Sqrt(maxDistanceSq) : Fix64.Zero;
        }

        private Fix64 ResolveRotation(Entity entity)
        {
            return World.TryGet(entity, out FacingDirection facing)
                ? Fix64.FromFloat(facing.AngleRad)
                : Fix64.Zero;
        }

        private MassNavigationFlowObstacleProjection BuildSingleMassNavigationFlowProjection(
            Entity entity,
            in ManifestationObstacleIntent2D intent,
            int shapeDataIndex,
            Fix64 rotation,
            int shapeSignature,
            int poseSignature)
        {
            int navRadiusCm = ResolveNavRadiusCm(entity, in intent, shapeDataIndex).RoundToInt();
            if (navRadiusCm <= 0)
            {
                throw new InvalidOperationException($"ManifestationObstacleIntent2D entity {entity.Id} requires navRadiusCm > 0 for MassNavigationFlow projection.");
            }

            Fix64Vec2 offset = ShapeWorldTransform2D.RotateLocal(
                Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm),
                rotation);
            var projection = new MassNavigationFlowObstacleProjection
            {
                ShapeSignature = shapeSignature,
                PoseSignature = poseSignature,
            };
            projection.SetPiece(
                0,
                intent.Shape,
                offset.X.RoundToInt(),
                offset.Y.RoundToInt(),
                navRadiusCm);
            return projection;
        }

        private MassNavigationFlowObstacleProjection BuildCompoundMassNavigationFlowProjection(Entity entity, in CompoundObstacle2DState state)
        {
            Fix64 rotation = ResolveRotation(entity);
            var projection = new MassNavigationFlowObstacleProjection
            {
                ShapeSignature = state.ShapeSignature,
                PoseSignature = state.PoseSignature,
            };

            for (int i = 0; i < state.PieceCount; i++)
            {
                int radiusCm = state.GetNavRadiusCm(i);
                if (radiusCm <= 0)
                {
                    throw new InvalidOperationException($"CompoundObstacle2D piece {i} requires navRadiusCm > 0 for MassNavigationFlow projection.");
                }

                Fix64Vec2 offset = ShapeWorldTransform2D.RotateLocal(ResolveCompoundPieceOffsetCm(in state, i), rotation);
                projection.SetPiece(i, state.GetShape(i), offset.X.RoundToInt(), offset.Y.RoundToInt(), radiusCm);
            }

            return projection;
        }

        private Fix64Vec2 ResolveCompoundPieceOffsetCm(in CompoundObstacle2DState state, int pieceIndex)
        {
            int shapeDataIndex = state.GetShapeDataIndex(pieceIndex);
            return state.GetShape(pieceIndex) switch
            {
                ManifestationObstacleShape2D.Circle when _shapeStorage.TryGetCircle(shapeDataIndex, out var circle) => circle.LocalCenter,
                ManifestationObstacleShape2D.Box when _shapeStorage.TryGetBox(shapeDataIndex, out var box) => box.LocalCenter,
                ManifestationObstacleShape2D.Polygon when _shapeStorage.TryGetPolygon(shapeDataIndex, out var polygon) => polygon.LocalOffset,
                _ => throw new InvalidOperationException($"CompoundObstacle2D piece {pieceIndex} references missing shape data index {shapeDataIndex}.")
            };
        }

        private int ResolveCompoundPieceNavRadiusCm(
            in CompoundObstacle2D obstacle,
            int pieceIndex,
            ManifestationObstacleShape2D shape,
            int shapeDataIndex)
        {
            int authoredRadius = obstacle.GetNavRadiusCm(pieceIndex);
            if (authoredRadius > 0)
            {
                return authoredRadius;
            }

            return shape switch
            {
                ManifestationObstacleShape2D.Circle when _shapeStorage.TryGetCircle(shapeDataIndex, out var circle) => circle.Radius.ToInt(),
                ManifestationObstacleShape2D.Box when _shapeStorage.TryGetBox(shapeDataIndex, out var box) =>
                    Fix64Math.Sqrt(box.HalfWidth * box.HalfWidth + box.HalfHeight * box.HalfHeight).ToInt(),
                ManifestationObstacleShape2D.Polygon when _shapeStorage.TryGetPolygon(shapeDataIndex, out var polygon) =>
                    ResolvePolygonRadius(polygon).ToInt(),
                _ => 0
            };
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

        private static Fix64Vec2 ToFix64Vec2(in WorldCmInt2 point)
        {
            return Fix64Vec2.FromInt(point.X, point.Y);
        }

        private void Upsert<T>(Entity entity, in T component)
        {
            if (World.Has<T>(entity))
            {
                World.Set(entity, component);
            }
            else
            {
                World.Add(entity, component);
            }
        }

        private void RemoveIfPresent<T>(Entity entity)
        {
            if (World.Has<T>(entity))
            {
                World.Remove<T>(entity);
            }
        }

        private void RemovePhysicsDerivedState(Entity entity)
        {
            RemoveIfPresent<Collider2D>(entity);
            RemoveIfPresent<Mass2D>(entity);
            RemoveIfPresent<Velocity2D>(entity);
        }

        private void RemoveNavigationDerivedState(Entity entity)
        {
            RemoveIfPresent<MassNavigationFlowObstacleProjection>(entity);
        }

        private void MarkStaticBodyActive(Entity entity)
        {
            if (!World.Has<Physics2DStaticBodyState>(entity))
            {
                World.Add(entity, new Physics2DStaticBodyState());
            }

            MarkStaticBodyDirty(entity);
        }

        private void MarkStaticBodyRemoved(Entity entity)
        {
            if (World.Has<Physics2DStaticBodyState>(entity))
            {
                World.Remove<Physics2DStaticBodyState>(entity);
                MarkStaticBodyDirty(entity);
            }
        }

        private void MarkStaticBodyDirty(Entity entity)
        {
            if (!World.Has<Physics2DStaticBodyDirty>(entity))
            {
                World.Add(entity, new Physics2DStaticBodyDirty());
            }
        }
    }
}
