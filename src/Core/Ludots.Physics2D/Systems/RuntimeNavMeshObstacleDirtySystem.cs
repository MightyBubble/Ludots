using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class RuntimeNavMeshObstacleDirtySystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _singleQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState, RuntimeNavMeshStructuralObstacle>();

        private static readonly QueryDescription _compoundQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CompoundObstacle2DState, RuntimeNavMeshStructuralObstacle>();

        private readonly GameEngine _engine;
        private readonly ShapeDataStorage2D _shapeStorage;
        private readonly Dictionary<Entity, TrackedObstacle> _tracked = new Dictionary<Entity, TrackedObstacle>();
        private readonly List<Entity> _seen = new List<Entity>(128);

        public RuntimeNavMeshObstacleDirtySystem(GameEngine engine)
            : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
        {
            _engine = engine;
            _shapeStorage = engine.TryGetService(CoreServiceKeys.Physics2DShapeStorage, out object shapeStorage) &&
                shapeStorage is ShapeDataStorage2D typedShapeStorage
                ? typedShapeStorage
                : throw new InvalidOperationException("Runtime navmesh obstacle dirty system requires Physics2D shape storage to be registered.");
        }

        public override void Update(in float dt)
        {
            bool hasBakeConfig = _engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig bakeConfig);
            if (!hasBakeConfig || bakeConfig.ParsedMode != NavBakeMode.RuntimeIncremental)
            {
                _tracked.Clear();
                _seen.Clear();
                return;
            }

            if (!_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue queue) ||
                !_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshObstacles, out NavObstacleSet obstacleSet) ||
                !_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshAuthoredObstacles, out NavObstacleSet authoredObstacles))
            {
                throw new InvalidOperationException("Runtime-incremental navmesh mode requires authored obstacles, runtime obstacles, and a rebuild queue.");
            }

            if (bakeConfig.RuntimeIncremental == null)
            {
                throw new InvalidOperationException("Runtime-incremental navmesh mode requires NavMeshBakeConfig.runtimeIncremental.");
            }

            string layerId = RequireSingleLayerId(bakeConfig);
            _seen.Clear();
            obstacleSet.Obstacles.Clear();
            obstacleSet.Obstacles.AddRange(authoredObstacles.Obstacles);
            CaptureSingles(obstacleSet, queue, layerId, bakeConfig.RuntimeIncremental.IncludeNeighborTiles);
            CaptureCompounds(obstacleSet, queue, layerId, bakeConfig.RuntimeIncremental.IncludeNeighborTiles);
            RemoveMissingTracked(queue, bakeConfig.RuntimeIncremental.IncludeNeighborTiles);
            if (queue.PendingTileCount > 0)
            {
                queue.ProcessBudget(bakeConfig.RuntimeIncremental.TileBudgetPerFixedTick);
            }
        }

        private void CaptureSingles(
            NavObstacleSet obstacleSet,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            string layerId,
            bool includeNeighbors)
        {
            World.Query(in _singleQuery, (Entity entity, ref WorldPositionCm position, ref ManifestationObstacleIntent2D intent, ref ManifestationObstacleBridge2DState state) =>
            {
                if (intent.SinkNavigationObstacle == 0)
                {
                    TrackOrRemove(entity, queue, includeNeighbors);
                    return;
                }

                NavObstacle obstacle = BuildObstacle(
                    entity,
                    $"runtime-obstacle-{entity.Id}",
                    intent.Shape,
                    state.ShapeDataIndex,
                    position.Value,
                    ResolveRotation(entity),
                    layerId);
                obstacleSet.Obstacles.Add(obstacle);
                TrackCurrent(entity, obstacle, state.ShapeSignature, state.PoseSignature, queue, includeNeighbors);
            });
        }

        private void CaptureCompounds(
            NavObstacleSet obstacleSet,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            string layerId,
            bool includeNeighbors)
        {
            World.Query(in _compoundQuery, (Entity entity, ref WorldPositionCm position, ref CompoundObstacle2DState state) =>
            {
                if (state.SinkNavigationObstacle == 0)
                {
                    TrackOrRemove(entity, queue, includeNeighbors);
                    return;
                }

                WorldAabbCm combined = default;
                bool hasBounds = false;
                Fix64 rotation = ResolveRotation(entity);
                for (int i = 0; i < state.PieceCount; i++)
                {
                    NavObstacle obstacle = BuildObstacle(
                        entity,
                        $"runtime-compound-obstacle-{entity.Id}.piece{i}",
                        state.GetShape(i),
                        state.GetShapeDataIndex(i),
                        position.Value,
                        rotation,
                        layerId);
                    obstacleSet.Obstacles.Add(obstacle);
                    WorldAabbCm bounds = ComputeBounds(obstacle);
                    combined = hasBounds ? Union(combined, bounds) : bounds;
                    hasBounds = true;
                }

                if (!hasBounds)
                {
                    TrackOrRemove(entity, queue, includeNeighbors);
                    return;
                }

                TrackCurrent(entity, combined, state.ShapeSignature, state.PoseSignature, queue, includeNeighbors);
            });
        }

        private void TrackCurrent(
            Entity entity,
            NavObstacle obstacle,
            int shapeSignature,
            int poseSignature,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            bool includeNeighbors)
        {
            TrackCurrent(entity, ComputeBounds(obstacle), shapeSignature, poseSignature, queue, includeNeighbors);
        }

        private void TrackCurrent(
            Entity entity,
            WorldAabbCm bounds,
            int shapeSignature,
            int poseSignature,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            bool includeNeighbors)
        {
            _seen.Add(entity);
            var next = new TrackedObstacle(entity, bounds, shapeSignature, poseSignature);
            if (_tracked.TryGetValue(entity, out TrackedObstacle previous) &&
                previous.ShapeSignature == shapeSignature &&
                previous.PoseSignature == poseSignature &&
                previous.Bounds == bounds)
            {
                return;
            }

            if (_tracked.TryGetValue(entity, out previous))
            {
                queue.EnqueueDirtyAabb(Union(previous.Bounds, bounds), includeNeighbors);
            }
            else
            {
                queue.EnqueueDirtyAabb(bounds, includeNeighbors);
            }

            _tracked[entity] = next;
        }

        private void TrackOrRemove(Entity entity, RuntimeIncrementalNavMeshRebuildQueue queue, bool includeNeighbors)
        {
            _seen.Add(entity);
            if (_tracked.Remove(entity, out TrackedObstacle previous))
            {
                queue.EnqueueDirtyAabb(previous.Bounds, includeNeighbors);
            }
        }

        private void RemoveMissingTracked(RuntimeIncrementalNavMeshRebuildQueue queue, bool includeNeighbors)
        {
            if (_tracked.Count == 0)
            {
                return;
            }

            var seen = new HashSet<Entity>(_seen);
            Span<Entity> toRemove = stackalloc Entity[Math.Min(_tracked.Count, 64)];
            var overflow = (List<Entity>?)null;
            int removeCount = 0;
            foreach (KeyValuePair<Entity, TrackedObstacle> kvp in _tracked)
            {
                if (seen.Contains(kvp.Key) &&
                    World.IsAlive(kvp.Value.Entity))
                {
                    continue;
                }

                if (removeCount < toRemove.Length)
                {
                    toRemove[removeCount] = kvp.Key;
                }
                else
                {
                    overflow ??= new List<Entity>();
                    overflow.Add(kvp.Key);
                }

                removeCount++;
            }

            int stackCount = Math.Min(removeCount, toRemove.Length);
            for (int i = 0; i < stackCount; i++)
            {
                RemoveTracked(toRemove[i], queue, includeNeighbors);
            }

            if (overflow != null)
            {
                for (int i = 0; i < overflow.Count; i++)
                {
                    RemoveTracked(overflow[i], queue, includeNeighbors);
                }
            }
        }

        private void RemoveTracked(Entity entity, RuntimeIncrementalNavMeshRebuildQueue queue, bool includeNeighbors)
        {
            if (_tracked.Remove(entity, out TrackedObstacle previous))
            {
                queue.EnqueueDirtyAabb(previous.Bounds, includeNeighbors);
            }
        }

        private NavObstacle BuildObstacle(
            Entity entity,
            string id,
            ManifestationObstacleShape2D shape,
            int shapeDataIndex,
            Fix64Vec2 worldPosition,
            Fix64 rotation,
            string layerId)
        {
            return shape switch
            {
                ManifestationObstacleShape2D.Circle => BuildCircle(id, shapeDataIndex, worldPosition, rotation, layerId),
                ManifestationObstacleShape2D.Box => BuildBox(id, shapeDataIndex, worldPosition, rotation, layerId),
                ManifestationObstacleShape2D.Polygon => BuildPolygon(id, shapeDataIndex, worldPosition, rotation, layerId),
                _ => throw new InvalidOperationException($"Unsupported runtime nav obstacle shape '{shape}' on entity {entity.Id}.")
            };
        }

        private NavObstacle BuildCircle(string id, int shapeDataIndex, Fix64Vec2 worldPosition, Fix64 rotation, string layerId)
        {
            if (!_shapeStorage.TryGetCircle(shapeDataIndex, out CircleShapeData circle))
            {
                throw new InvalidOperationException($"Runtime nav obstacle '{id}' references missing circle shape data index {shapeDataIndex}.");
            }

            Fix64Vec2 center = ShapeWorldTransform2D.GetCircleCenter(worldPosition, rotation, circle);
            return new NavObstacle
            {
                Id = id,
                Enabled = true,
                Kind = NavObstacleKind.Circle,
                LayerId = layerId,
                Center = new NavPointCm(center.X.RoundToInt(), center.Y.RoundToInt()),
                RadiusCm = circle.Radius.RoundToInt()
            };
        }

        private NavObstacle BuildBox(string id, int shapeDataIndex, Fix64Vec2 worldPosition, Fix64 rotation, string layerId)
        {
            if (!_shapeStorage.TryGetBox(shapeDataIndex, out BoxShapeData box))
            {
                throw new InvalidOperationException($"Runtime nav obstacle '{id}' references missing box shape data index {shapeDataIndex}.");
            }

            Fix64Vec2 center = ShapeWorldTransform2D.GetBoxCenter(worldPosition, rotation, box);
            Fix64Vec2[] corners =
            {
                new Fix64Vec2(-box.HalfWidth, -box.HalfHeight),
                new Fix64Vec2(box.HalfWidth, -box.HalfHeight),
                new Fix64Vec2(box.HalfWidth, box.HalfHeight),
                new Fix64Vec2(-box.HalfWidth, box.HalfHeight),
            };
            var obstacle = new NavObstacle
            {
                Id = id,
                Enabled = true,
                Kind = NavObstacleKind.Polygon,
                LayerId = layerId,
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Fix64Vec2 vertex = center + ShapeWorldTransform2D.RotateLocal(corners[i], rotation);
                obstacle.Points.Add(new NavPointCm(vertex.X.RoundToInt(), vertex.Y.RoundToInt()));
            }

            return obstacle;
        }

        private NavObstacle BuildPolygon(string id, int shapeDataIndex, Fix64Vec2 worldPosition, Fix64 rotation, string layerId)
        {
            if (!_shapeStorage.TryGetPolygon(shapeDataIndex, out PolygonShapeData polygon))
            {
                throw new InvalidOperationException($"Runtime nav obstacle '{id}' references missing polygon shape data index {shapeDataIndex}.");
            }

            var obstacle = new NavObstacle
            {
                Id = id,
                Enabled = true,
                Kind = NavObstacleKind.Polygon,
                LayerId = layerId,
            };
            for (int i = 0; i < polygon.VertexCount; i++)
            {
                Fix64Vec2 vertex = ShapeWorldTransform2D.GetPolygonWorldVertex(worldPosition, rotation, polygon, i);
                obstacle.Points.Add(new NavPointCm(vertex.X.RoundToInt(), vertex.Y.RoundToInt()));
            }

            return obstacle;
        }

        private Fix64 ResolveRotation(Entity entity)
        {
            return World.TryGet(entity, out FacingDirection facing)
                ? Fix64.FromFloat(facing.AngleRad)
                : Fix64.Zero;
        }

        private static WorldAabbCm ComputeBounds(NavObstacle obstacle)
        {
            if (obstacle.Kind == NavObstacleKind.Circle)
            {
                return WorldAabbCm.FromCenterRadius(
                    new WorldCmInt2(obstacle.Center.Xcm, obstacle.Center.Zcm),
                    obstacle.RadiusCm);
            }

            if (obstacle.Points == null || obstacle.Points.Count < 3)
            {
                throw new InvalidOperationException($"Runtime nav obstacle '{obstacle.Id}' polygon requires at least 3 points.");
            }

            int minX = obstacle.Points[0].Xcm;
            int maxX = minX;
            int minY = obstacle.Points[0].Zcm;
            int maxY = minY;
            for (int i = 1; i < obstacle.Points.Count; i++)
            {
                NavPointCm point = obstacle.Points[i];
                minX = Math.Min(minX, point.Xcm);
                maxX = Math.Max(maxX, point.Xcm);
                minY = Math.Min(minY, point.Zcm);
                maxY = Math.Max(maxY, point.Zcm);
            }

            return new WorldAabbCm(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        private static WorldAabbCm Union(WorldAabbCm a, WorldAabbCm b)
        {
            int left = Math.Min(a.Left, b.Left);
            int top = Math.Min(a.Top, b.Top);
            int right = Math.Max(a.Right, b.Right);
            int bottom = Math.Max(a.Bottom, b.Bottom);
            return new WorldAabbCm(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private static string RequireSingleLayerId(NavMeshBakeConfig bakeConfig)
        {
            if (bakeConfig?.Layers == null || bakeConfig.Layers.Count != 1)
            {
                throw new InvalidOperationException("Runtime navmesh obstacle dirty system currently requires exactly one nav layer.");
            }

            string layerId = bakeConfig.Layers[0].Id;
            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Runtime navmesh obstacle dirty system requires a non-empty trimmed nav layer id.");
            }

            return layerId;
        }

        private readonly struct TrackedObstacle
        {
            public readonly Entity Entity;
            public readonly WorldAabbCm Bounds;
            public readonly int ShapeSignature;
            public readonly int PoseSignature;

            public TrackedObstacle(Entity entity, WorldAabbCm bounds, int shapeSignature, int poseSignature)
            {
                Entity = entity;
                Bounds = bounds;
                ShapeSignature = shapeSignature;
                PoseSignature = poseSignature;
            }
        }
    }
}
