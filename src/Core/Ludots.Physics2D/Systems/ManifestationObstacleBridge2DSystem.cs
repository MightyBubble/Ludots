using System;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Bridges runtime manifestation blocker intent into lower-layer physics and navigation components.
    /// Static manifestations are materialized once and revisited only through explicit dirty state.
    /// </summary>
    public sealed class ManifestationObstacleBridge2DSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _newQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D>()
            .WithNone<ManifestationObstacleBridge2DState>();

        private static readonly QueryDescription _dirtyQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState, ManifestationObstacleBridge2DDirty>();

        private static readonly QueryDescription _movingQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState, ManifestationMotion2D>();

        public ManifestationObstacleBridge2DSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            World.Query(in _newQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                Materialize(entity, in worldPosition, in intent, removeDirty: false);
            });

            World.Query(in _dirtyQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                Materialize(entity, in worldPosition, in intent, removeDirty: true);
            });

            World.Query(in _movingQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                Materialize(entity, in worldPosition, in intent, removeDirty: false);
            });
        }

        private void Materialize(
            Entity entity,
            in WorldPositionCm worldPosition,
            in ManifestationObstacleIntent2D intent,
            bool removeDirty)
        {
            int shapeSignature = ComputeShapeSignature(in intent, entity);
            int poseSignature = ComputePoseSignature(in worldPosition, entity);
            int sinkSignature = ComputeSinkSignature(in intent);

            bool hasState = World.TryGet(entity, out ManifestationObstacleBridge2DState previousState);
            if (hasState &&
                previousState.ShapeSignature == shapeSignature &&
                previousState.PoseSignature == poseSignature &&
                previousState.SinkSignature == sinkSignature &&
                !removeDirty)
            {
                return;
            }

            bool shapeChanged = !hasState || previousState.ShapeSignature != shapeSignature;

            Upsert(entity, new Position2D { Value = worldPosition.Value });

            if (World.TryGet(entity, out FacingDirection facing))
            {
                Upsert(entity, new Rotation2D { Value = Fix64.FromFloat(facing.AngleRad) });
            }

            int shapeDataIndex = intent.Shape == ManifestationObstacleShape2D.GeometryProfile
                ? -1
                : EnsureShapeRegistered(entity, in intent, shapeSignature, in previousState, hasState);

            if (intent.SinkPhysicsCollider != 0)
            {
                if (intent.Shape == ManifestationObstacleShape2D.GeometryProfile)
                {
                    if (shapeChanged || !World.Has<CompoundCollider2D>(entity))
                    {
                        var compound = RegisterCompoundCollider(entity, in intent);
                        Upsert(entity, compound);
                    }

                    RemoveIfPresent<Collider2D>(entity);
                }
                else
                {
                    Upsert(entity, new Collider2D
                    {
                        Type = ToColliderType(intent.Shape),
                        ShapeDataIndex = shapeDataIndex
                    });
                    RemoveIfPresent<CompoundCollider2D>(entity);
                }

                Upsert(entity, Mass2D.Static);
                Upsert(entity, Velocity2D.Zero);
            }
            else
            {
                RemoveIfPresent<Collider2D>(entity);
                RemoveIfPresent<CompoundCollider2D>(entity);
            }

            if (intent.SinkNavigationObstacle != 0)
            {
                if (intent.Shape == ManifestationObstacleShape2D.GeometryProfile)
                {
                    if (shapeChanged || !World.Has<NavCompoundObstacle2D>(entity))
                    {
                        CompoundCollider2D compound = World.TryGet(entity, out CompoundCollider2D existingCompound)
                            ? existingCompound
                            : RegisterCompoundCollider(entity, in intent);
                        var navCompound = ToNavCompoundObstacle(in compound);
                        Upsert(entity, navCompound);
                    }

                    RemoveIfPresent<NavObstacle2D>(entity);
                }
                else
                {
                    Upsert(entity, new NavObstacle2D
                    {
                        Shape = ToNavObstacleShape(intent.Shape),
                        ShapeDataIndex = shapeDataIndex
                    });
                    RemoveIfPresent<NavCompoundObstacle2D>(entity);
                }

                Upsert(entity, new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.Zero,
                    MaxAccelCmPerSec2 = Fix64.Zero,
                    RadiusCm = ResolveNavRadiusCm(entity, in intent, shapeDataIndex),
                    NeighborDistCm = Fix64.Zero,
                    TimeHorizonSec = Fix64.Zero,
                    MaxNeighbors = 0
                });
            }
            else
            {
                RemoveIfPresent<NavObstacle2D>(entity);
                RemoveIfPresent<NavCompoundObstacle2D>(entity);
                RemoveIfPresent<NavKinematics2D>(entity);
            }

            Upsert(entity, new ManifestationObstacleBridge2DState
            {
                ShapeDataIndex = shapeDataIndex,
                ShapeSignature = shapeSignature,
                PoseSignature = poseSignature,
                SinkSignature = sinkSignature
            });

            bool hasStaticPhysicsState = World.Has<Physics2DStaticBodyState>(entity);
            if (intent.SinkPhysicsCollider != 0 || hasStaticPhysicsState)
            {
                MarkStaticBodyDirty(entity);
            }
            else
            {
                RemoveIfPresent<Physics2DStaticBodyDirty>(entity);
            }

            if (removeDirty)
            {
                RemoveIfPresent<ManifestationObstacleBridge2DDirty>(entity);
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
                ManifestationObstacleShape2D.Circle => ShapeDataStorage2D.RegisterCircle(
                    Fix64.FromInt(intent.RadiusCm),
                    Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm)),
                ManifestationObstacleShape2D.Box => ShapeDataStorage2D.RegisterBox(
                    Fix64.FromInt(intent.HalfWidthCm),
                    Fix64.FromInt(intent.HalfHeightCm),
                    Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm)),
                ManifestationObstacleShape2D.Polygon => RegisterPolygon(entity, intent.LocalOffsetXCm, intent.LocalOffsetYCm),
                _ => throw new InvalidOperationException($"Unsupported manifestation obstacle shape '{intent.Shape}'.")
            };
        }

        private int RegisterPolygon(Entity entity, int localOffsetXCm, int localOffsetYCm)
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
                WorldCmInt2 vertex = polygon.GetVertex(i);
                vertices[i] = Fix64Vec2.FromInt(vertex.X + localOffsetXCm, vertex.Y + localOffsetYCm);
            }

            return ShapeDataStorage2D.RegisterPolygon(vertices);
        }

        private CompoundCollider2D RegisterCompoundCollider(Entity entity, in ManifestationObstacleIntent2D intent)
        {
            if (!World.TryGet(entity, out ObstacleGeometryProfile2D profile))
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D with GeometryProfile shape requires ObstacleGeometryProfile2D.");
            }

            if (profile.PieceCount == 0 || profile.PieceCount > ObstacleGeometryProfile2D.MaxPieces)
            {
                throw new InvalidOperationException($"ObstacleGeometryProfile2D pieces count must be between 1 and {ObstacleGeometryProfile2D.MaxPieces}.");
            }

            var compound = new CompoundCollider2D();
            for (int i = 0; i < profile.PieceCount; i++)
            {
                int shapeDataIndex = RegisterGeometryPiece(in profile, i, in intent, out ColliderType2D colliderType);
                compound.SetPiece(i, colliderType, shapeDataIndex);
            }

            return compound;
        }

        private static NavCompoundObstacle2D ToNavCompoundObstacle(in CompoundCollider2D source)
        {
            var compound = new NavCompoundObstacle2D();
            for (int i = 0; i < source.PieceCount; i++)
            {
                var (colliderType, shapeDataIndex) = source.GetPiece(i);
                compound.SetPiece(i, ToNavObstacleShape(colliderType), shapeDataIndex);
            }

            return compound;
        }

        private static int RegisterGeometryPiece(
            in ObstacleGeometryProfile2D profile,
            int pieceIndex,
            in ManifestationObstacleIntent2D intent,
            out ColliderType2D colliderType)
        {
            ObstacleGeometryPiece2D piece = profile.GetPiece(pieceIndex);
            int offsetX = intent.LocalOffsetXCm + piece.LocalOffsetXCm;
            int offsetY = intent.LocalOffsetYCm + piece.LocalOffsetYCm;

            switch (piece.Shape)
            {
                case ObstacleGeometryPieceShape2D.Circle:
                    colliderType = ColliderType2D.Circle;
                    return ShapeDataStorage2D.RegisterCircle(
                        Fix64.FromInt(piece.RadiusCm),
                        Fix64Vec2.FromInt(offsetX, offsetY));

                case ObstacleGeometryPieceShape2D.Box:
                    colliderType = ColliderType2D.Box;
                    return ShapeDataStorage2D.RegisterBox(
                        Fix64.FromInt(piece.HalfWidthCm),
                        Fix64.FromInt(piece.HalfHeightCm),
                        Fix64Vec2.FromInt(offsetX, offsetY));

                case ObstacleGeometryPieceShape2D.Polygon:
                    colliderType = ColliderType2D.Polygon;
                    return RegisterGeometryPolygon(in profile, pieceIndex, piece.VertexCount, offsetX, offsetY);

                default:
                    throw new InvalidOperationException($"Unsupported ObstacleGeometryProfile2D piece shape '{piece.Shape}'.");
            }
        }

        private static int RegisterGeometryPolygon(
            in ObstacleGeometryProfile2D profile,
            int pieceIndex,
            int vertexCount,
            int offsetX,
            int offsetY)
        {
            if (vertexCount < 3 || vertexCount > ObstacleGeometryProfile2D.MaxPolygonVertices)
            {
                throw new InvalidOperationException($"ObstacleGeometryProfile2D polygon vertex count must be between 3 and {ObstacleGeometryProfile2D.MaxPolygonVertices}.");
            }

            var vertices = new Fix64Vec2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                WorldCmInt2 vertex = profile.GetPolygonVertex(pieceIndex, i);
                vertices[i] = Fix64Vec2.FromInt(vertex.X + offsetX, vertex.Y + offsetY);
            }

            return ShapeDataStorage2D.RegisterPolygon(vertices);
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
            else if (intent.Shape == ManifestationObstacleShape2D.GeometryProfile &&
                World.TryGet(entity, out ObstacleGeometryProfile2D profile))
            {
                hash = MixSignature(hash, profile.PieceCount);
                for (int i = 0; i < profile.PieceCount; i++)
                {
                    ObstacleGeometryPiece2D piece = profile.GetPiece(i);
                    hash = MixSignature(hash, (byte)piece.Shape);
                    hash = MixSignature(hash, piece.RadiusCm);
                    hash = MixSignature(hash, piece.HalfWidthCm);
                    hash = MixSignature(hash, piece.HalfHeightCm);
                    hash = MixSignature(hash, piece.LocalOffsetXCm);
                    hash = MixSignature(hash, piece.LocalOffsetYCm);
                    hash = MixSignature(hash, piece.VertexCount);
                    for (int vertexIndex = 0; vertexIndex < piece.VertexCount; vertexIndex++)
                    {
                        WorldCmInt2 vertex = profile.GetPolygonVertex(i, vertexIndex);
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
            if (World.TryGet(entity, out FacingDirection facing))
            {
                hash = MixSignature(hash, BitConverter.SingleToInt32Bits(facing.AngleRad));
            }
            else
            {
                hash = MixSignature(hash, 0);
            }

            return hash;
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

        private static int ComputeSinkSignature(in ManifestationObstacleIntent2D intent)
        {
            return (intent.SinkPhysicsCollider & 0x1) |
                   ((intent.SinkNavigationObstacle & 0x1) << 1);
        }

        private static Fix64 ResolveNavRadiusCm(Entity entity, in ManifestationObstacleIntent2D intent, int shapeDataIndex)
        {
            if (intent.NavRadiusCm > 0)
            {
                return Fix64.FromInt(intent.NavRadiusCm);
            }

            if (intent.Shape == ManifestationObstacleShape2D.GeometryProfile)
            {
                if (entity.TryGet(out CompoundCollider2D compound))
                {
                    return ResolveCompoundRadius(in compound);
                }

                if (entity.TryGet(out NavCompoundObstacle2D navCompound))
                {
                    return ResolveNavCompoundRadius(in navCompound);
                }

                throw new InvalidOperationException("GeometryProfile navigation obstacle requires materialized CompoundCollider2D or NavCompoundObstacle2D.");
            }

            return intent.Shape switch
            {
                ManifestationObstacleShape2D.Circle when ShapeDataStorage2D.TryGetCircle(shapeDataIndex, out var circle) => circle.Radius,
                ManifestationObstacleShape2D.Box when ShapeDataStorage2D.TryGetBox(shapeDataIndex, out var box) =>
                    Fix64Math.Sqrt(box.HalfWidth * box.HalfWidth + box.HalfHeight * box.HalfHeight),
                ManifestationObstacleShape2D.Polygon when ShapeDataStorage2D.TryGetPolygon(shapeDataIndex, out var polygon) => ResolvePolygonRadius(polygon),
                _ => Fix64.Zero
            };
        }

        private static Fix64 ResolveCompoundRadius(in CompoundCollider2D compound)
        {
            Fix64 maxRadius = Fix64.Zero;
            for (int i = 0; i < compound.PieceCount; i++)
            {
                var (shape, shapeDataIndex) = compound.GetPiece(i);
                Fix64 radius = shape switch
                {
                    ColliderType2D.Circle when ShapeDataStorage2D.TryGetCircle(shapeDataIndex, out var circle) =>
                        circle.LocalCenter.Length() + circle.Radius,
                    ColliderType2D.Box when ShapeDataStorage2D.TryGetBox(shapeDataIndex, out var box) =>
                        box.LocalCenter.Length() + Fix64Math.Sqrt(box.HalfWidth * box.HalfWidth + box.HalfHeight * box.HalfHeight),
                    ColliderType2D.Polygon when ShapeDataStorage2D.TryGetPolygon(shapeDataIndex, out var polygon) =>
                        ResolvePolygonWorldRadius(polygon),
                    _ => Fix64.Zero
                };

                if (radius > maxRadius)
                {
                    maxRadius = radius;
                }
            }

            return maxRadius;
        }

        private static Fix64 ResolveNavCompoundRadius(in NavCompoundObstacle2D compound)
        {
            Fix64 maxRadius = Fix64.Zero;
            for (int i = 0; i < compound.PieceCount; i++)
            {
                var (shape, shapeDataIndex) = compound.GetPiece(i);
                Fix64 radius = shape switch
                {
                    NavObstacleShape2D.Circle when ShapeDataStorage2D.TryGetCircle(shapeDataIndex, out var circle) =>
                        circle.LocalCenter.Length() + circle.Radius,
                    NavObstacleShape2D.Box when ShapeDataStorage2D.TryGetBox(shapeDataIndex, out var box) =>
                        box.LocalCenter.Length() + Fix64Math.Sqrt(box.HalfWidth * box.HalfWidth + box.HalfHeight * box.HalfHeight),
                    NavObstacleShape2D.Polygon when ShapeDataStorage2D.TryGetPolygon(shapeDataIndex, out var polygon) =>
                        ResolvePolygonWorldRadius(polygon),
                    _ => Fix64.Zero
                };

                if (radius > maxRadius)
                {
                    maxRadius = radius;
                }
            }

            return maxRadius;
        }

        private static Fix64 ResolvePolygonRadius(in PolygonShapeData polygon)
        {
            Fix64 maxDistanceSq = Fix64.Zero;
            for (int i = 0; i < polygon.VertexCount; i++)
            {
                Fix64Vec2 delta = polygon.Vertices[i] - polygon.LocalCenter;
                Fix64 distanceSq = delta.LengthSquared();
                if (distanceSq > maxDistanceSq)
                {
                    maxDistanceSq = distanceSq;
                }
            }

            return maxDistanceSq > Fix64.Zero ? Fix64Math.Sqrt(maxDistanceSq) : Fix64.Zero;
        }

        private static Fix64 ResolvePolygonWorldRadius(in PolygonShapeData polygon)
        {
            Fix64 maxDistanceSq = Fix64.Zero;
            for (int i = 0; i < polygon.VertexCount; i++)
            {
                Fix64 distanceSq = polygon.Vertices[i].LengthSquared();
                if (distanceSq > maxDistanceSq)
                {
                    maxDistanceSq = distanceSq;
                }
            }

            return maxDistanceSq > Fix64.Zero ? Fix64Math.Sqrt(maxDistanceSq) : Fix64.Zero;
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

        private static NavObstacleShape2D ToNavObstacleShape(ManifestationObstacleShape2D shape)
        {
            return shape switch
            {
                ManifestationObstacleShape2D.Circle => NavObstacleShape2D.Circle,
                ManifestationObstacleShape2D.Box => NavObstacleShape2D.Box,
                ManifestationObstacleShape2D.Polygon => NavObstacleShape2D.Polygon,
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
        }

        private static NavObstacleShape2D ToNavObstacleShape(ColliderType2D shape)
        {
            return shape switch
            {
                ColliderType2D.Circle => NavObstacleShape2D.Circle,
                ColliderType2D.Box => NavObstacleShape2D.Box,
                ColliderType2D.Polygon => NavObstacleShape2D.Polygon,
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
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

        private void MarkStaticBodyDirty(Entity entity)
        {
            if (!World.Has<Physics2DStaticBodyDirty>(entity))
            {
                World.Add(entity, new Physics2DStaticBodyDirty());
            }
        }
    }
}
