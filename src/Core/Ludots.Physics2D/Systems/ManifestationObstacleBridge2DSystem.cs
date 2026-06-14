using System;
using Arch.Core;
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
    /// This keeps spell/runtime authoring declarative while collision and nav remain owned by their subsystems.
    /// </summary>
    public sealed class ManifestationObstacleBridge2DSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _singleQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D>()
            .WithNone<CompoundObstacle2D>();

        private static readonly QueryDescription _compoundQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CompoundObstacle2D>();

        public ManifestationObstacleBridge2DSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            World.Query(in _singleQuery, (Entity entity, ref WorldPositionCm worldPosition, ref ManifestationObstacleIntent2D intent) =>
            {
                Upsert(entity, new Position2D { Value = worldPosition.Value });

                if (World.TryGet(entity, out FacingDirection facing))
                {
                    Upsert(entity, new Rotation2D { Value = Fix64.FromFloat(facing.AngleRad) });
                }

                int signature = ComputeShapeSignature(in intent, entity);
                int shapeDataIndex = EnsureShapeRegistered(entity, in intent, signature);

                if (intent.SinkPhysicsCollider != 0)
                {
                    Upsert(entity, new Collider2D
                    {
                        Type = ToColliderType(intent.Shape),
                        ShapeDataIndex = shapeDataIndex
                    });
                    Upsert(entity, Mass2D.Static);
                    Upsert(entity, Velocity2D.Zero);
                }

                if (intent.SinkNavigationObstacle != 0)
                {
                    Upsert(entity, new NavObstacle2D
                    {
                        Shape = ToNavObstacleShape(intent.Shape),
                        ShapeDataIndex = shapeDataIndex
                    });
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
            });

            World.Query(in _compoundQuery, (Entity entity, ref WorldPositionCm worldPosition, ref CompoundObstacle2D obstacle) =>
            {
                if (World.Has<ManifestationObstacleIntent2D>(entity))
                {
                    throw new InvalidOperationException("Entity must not author both ManifestationObstacleIntent2D and CompoundObstacle2D.");
                }

                Upsert(entity, new Position2D { Value = worldPosition.Value });

                if (World.TryGet(entity, out FacingDirection facing))
                {
                    Upsert(entity, new Rotation2D { Value = Fix64.FromFloat(facing.AngleRad) });
                }

                int signature = ComputeCompoundShapeSignature(in obstacle);
                CompoundObstacle2DState state = EnsureCompoundStateRegistered(entity, in obstacle, signature);

                if (obstacle.SinkPhysicsCollider != 0)
                {
                    Upsert(entity, Mass2D.Static);
                    Upsert(entity, Velocity2D.Zero);
                }
                else if (World.Has<Collider2D>(entity))
                {
                    World.Remove<Collider2D>(entity);
                }

                if (obstacle.SinkNavigationObstacle != 0)
                {
                    Upsert(entity, new NavKinematics2D
                    {
                        MaxSpeedCmPerSec = Fix64.Zero,
                        MaxAccelCmPerSec2 = Fix64.Zero,
                        RadiusCm = ResolveCompoundNavRadiusCm(in state),
                        NeighborDistCm = Fix64.Zero,
                        TimeHorizonSec = Fix64.Zero,
                        MaxNeighbors = 0
                    });
                }
                else if (World.Has<NavObstacle2D>(entity))
                {
                    World.Remove<NavObstacle2D>(entity);
                }
            });
        }

        private int EnsureShapeRegistered(Entity entity, in ManifestationObstacleIntent2D intent, int signature)
        {
            if (World.TryGet(entity, out ManifestationObstacleBridge2DState bridgeState) &&
                bridgeState.ShapeSignature == signature &&
                bridgeState.ShapeDataIndex >= 0)
            {
                return bridgeState.ShapeDataIndex;
            }

            int shapeDataIndex = RegisterShape(entity, in intent);
            Upsert(entity, new ManifestationObstacleBridge2DState
            {
                ShapeDataIndex = shapeDataIndex,
                ShapeSignature = signature
            });
            return shapeDataIndex;
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

            return ShapeDataStorage2D.RegisterPolygon(
                vertices,
                Fix64Vec2.FromInt(intent.LocalOffsetXCm, intent.LocalOffsetYCm));
        }

        private int ComputeShapeSignature(in ManifestationObstacleIntent2D intent, Entity entity)
        {
            var hash = new HashCode();
            hash.Add((byte)intent.Shape);
            hash.Add(intent.RadiusCm);
            hash.Add(intent.HalfWidthCm);
            hash.Add(intent.HalfHeightCm);
            hash.Add(intent.LocalOffsetXCm);
            hash.Add(intent.LocalOffsetYCm);
            hash.Add(intent.NavRadiusCm);

            if (intent.Shape == ManifestationObstacleShape2D.Polygon &&
                World.TryGet(entity, out ManifestationObstaclePolygon2D polygon))
            {
                hash.Add(polygon.VertexCount);
                for (int i = 0; i < polygon.VertexCount; i++)
                {
                    var vertex = polygon.GetVertex(i);
                    hash.Add(vertex.X);
                    hash.Add(vertex.Y);
                }
            }

            return hash.ToHashCode();
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
                state.SetPiece(i, shape, shapeDataIndex, navRadiusCm);
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
                ManifestationObstacleShape2D.Circle => ShapeDataStorage2D.RegisterCircle(
                    Fix64.FromInt(obstacle.GetRadiusCm(pieceIndex)),
                    Fix64Vec2.FromInt(obstacle.GetLocalOffsetXCm(pieceIndex), obstacle.GetLocalOffsetYCm(pieceIndex))),
                ManifestationObstacleShape2D.Box => ShapeDataStorage2D.RegisterBox(
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

            return ShapeDataStorage2D.RegisterPolygon(
                vertices,
                Fix64Vec2.FromInt(obstacle.GetLocalOffsetXCm(pieceIndex), obstacle.GetLocalOffsetYCm(pieceIndex)));
        }

        private static int ComputeCompoundShapeSignature(in CompoundObstacle2D obstacle)
        {
            var hash = new HashCode();
            hash.Add(obstacle.SinkPhysicsCollider);
            hash.Add(obstacle.SinkNavigationObstacle);
            hash.Add(obstacle.PieceCount);
            for (int i = 0; i < obstacle.PieceCount; i++)
            {
                ManifestationObstacleShape2D shape = obstacle.GetShape(i);
                hash.Add((byte)shape);
                hash.Add(obstacle.GetRadiusCm(i));
                hash.Add(obstacle.GetHalfWidthCm(i));
                hash.Add(obstacle.GetHalfHeightCm(i));
                hash.Add(obstacle.GetLocalOffsetXCm(i));
                hash.Add(obstacle.GetLocalOffsetYCm(i));
                hash.Add(obstacle.GetNavRadiusCm(i));

                if (shape == ManifestationObstacleShape2D.Polygon)
                {
                    int vertexCount = obstacle.GetPolygonVertexCount(i);
                    hash.Add(vertexCount);
                    for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        var vertex = obstacle.GetVertex(i, vertexIndex);
                        hash.Add(vertex.X);
                        hash.Add(vertex.Y);
                    }
                }
            }

            return hash.ToHashCode();
        }

        private static Fix64 ResolveNavRadiusCm(Entity entity, in ManifestationObstacleIntent2D intent, int shapeDataIndex)
        {
            if (intent.NavRadiusCm > 0)
            {
                return Fix64.FromInt(intent.NavRadiusCm);
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

        private static Fix64 ResolvePolygonRadius(in PolygonShapeData polygon)
        {
            Fix64 maxDistanceSq = Fix64.Zero;
            for (int i = 0; i < polygon.VertexCount; i++)
            {
                Fix64Vec2 delta = polygon.LocalOffset + polygon.Vertices[i] - polygon.LocalCenter;
                Fix64 distanceSq = delta.LengthSquared();
                if (distanceSq > maxDistanceSq)
                {
                    maxDistanceSq = distanceSq;
                }
            }

            return maxDistanceSq > Fix64.Zero ? Fix64Math.Sqrt(maxDistanceSq) : Fix64.Zero;
        }

        private static int ResolveCompoundPieceNavRadiusCm(
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
                ManifestationObstacleShape2D.Circle when ShapeDataStorage2D.TryGetCircle(shapeDataIndex, out var circle) => circle.Radius.ToInt(),
                ManifestationObstacleShape2D.Box when ShapeDataStorage2D.TryGetBox(shapeDataIndex, out var box) =>
                    Fix64Math.Sqrt(box.HalfWidth * box.HalfWidth + box.HalfHeight * box.HalfHeight).ToInt(),
                ManifestationObstacleShape2D.Polygon when ShapeDataStorage2D.TryGetPolygon(shapeDataIndex, out var polygon) =>
                    ResolvePolygonRadius(polygon).ToInt(),
                _ => 0
            };
        }

        private static Fix64 ResolveCompoundNavRadiusCm(in CompoundObstacle2DState state)
        {
            int maxRadiusCm = 0;
            for (int i = 0; i < state.PieceCount; i++)
            {
                maxRadiusCm = Math.Max(maxRadiusCm, state.GetNavRadiusCm(i));
            }

            return Fix64.FromInt(maxRadiusCm);
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
    }
}
