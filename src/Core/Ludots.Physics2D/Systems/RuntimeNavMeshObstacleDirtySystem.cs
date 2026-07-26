using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class RuntimeNavMeshObstacleDirtySystem : BaseSystem<World, float>
    {
        private const byte IndexEmpty = 0;
        private const byte IndexOccupied = 1;
        private const byte IndexTombstone = 2;

        private static readonly QueryDescription SingleQuery = new QueryDescription()
            .WithAll<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState, RuntimeNavMeshStructuralObstacle>();

        private static readonly QueryDescription CompoundQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CompoundObstacle2DState, RuntimeNavMeshStructuralObstacle>();

        private readonly GameEngine _engine;
        private readonly ShapeDataStorage2D _shapeStorage;

        private Entity[] _indexKeys = Array.Empty<Entity>();
        private byte[] _indexState = Array.Empty<byte>();
        private int[] _indexSlots = Array.Empty<int>();
        private int _indexMask;
        private Entity[] _trackedEntities = Array.Empty<Entity>();
        private WorldAabbCm[] _trackedBounds = Array.Empty<WorldAabbCm>();
        private int[] _trackedShapeSignatures = Array.Empty<int>();
        private int[] _trackedPoseSignatures = Array.Empty<int>();
        private int[] _trackedSeenEpoch = Array.Empty<int>();
        private int[] _removeScratch = Array.Empty<int>();
        private RuntimeNavMeshRebuildPublishedTile[] _publishedScratch = Array.Empty<RuntimeNavMeshRebuildPublishedTile>();
        private NavBakeResultEntry[] _failureScratch = Array.Empty<NavBakeResultEntry>();
        private int _trackedCount;
        private int _trackedCapacity;
        private int _updateEpoch;
        private bool _trackingConfigured;

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
                ClearTracking();
                return;
            }

            if (!_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue queue) ||
                !_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshObstacles, out RuntimeNavObstacleSnapshot obstacleSnapshot))
            {
                throw new InvalidOperationException("Runtime-incremental navmesh mode requires runtime obstacle and rebuild queue services.");
            }

            if (bakeConfig.RuntimeIncremental == null)
            {
                throw new InvalidOperationException("Runtime-incremental navmesh mode requires NavMeshBakeConfig.runtimeIncremental.");
            }

            EnsureTrackingCapacity(bakeConfig.RuntimeIncremental);
            string layerId = RequireSingleLayerId(bakeConfig);
            if (!string.Equals(obstacleSnapshot.BoundLayerId, layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime nav obstacle snapshot bound layer id must match the single authored nav layer.");
            }

            if (_updateEpoch == int.MaxValue)
            {
                throw new InvalidOperationException("RuntimeNavMeshObstacleDirtySystem update epoch overflow.");
            }

            _updateEpoch++;
            long collectAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            long collectTimeBefore = Stopwatch.GetTimestamp();
            obstacleSnapshot.BeginCapture();

            var singleJob = new CaptureSingleJob
            {
                System = this,
                Snapshot = obstacleSnapshot,
                Queue = queue,
                IncludeNeighbors = bakeConfig.RuntimeIncremental.IncludeNeighborTiles
            };
            World.InlineEntityQuery<CaptureSingleJob, WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState>(
                in SingleQuery,
                ref singleJob);

            var compoundJob = new CaptureCompoundJob
            {
                System = this,
                Snapshot = obstacleSnapshot,
                Queue = queue,
                IncludeNeighbors = bakeConfig.RuntimeIncremental.IncludeNeighborTiles
            };
            World.InlineEntityQuery<CaptureCompoundJob, WorldPositionCm, CompoundObstacle2DState>(
                in CompoundQuery,
                ref compoundJob);

            RemoveMissingTracked(queue, bakeConfig.RuntimeIncremental.IncludeNeighborTiles);
            obstacleSnapshot.EndCaptureAndSort();
            long collectTicks = Stopwatch.GetTimestamp() - collectTimeBefore;
            long collectAllocated = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - collectAllocBefore);

            if (queue.PendingTileCount > 0 || queue.SealedRemainingCount > 0)
            {
                long bakeCommitAllocBefore = GC.GetAllocatedBytesForCurrentThread();
                RuntimeNavMeshRebuildBatchStats stats = queue.ProcessBudgetInto(
                    bakeConfig.RuntimeIncremental.TileBudgetPerFixedTick,
                    _publishedScratch.AsSpan(),
                    _failureScratch.AsSpan());
                long bakeCommitAllocated = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - bakeCommitAllocBefore);

                if (_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshTelemetry, out RuntimeNavMeshTelemetryService telemetry))
                {
                    long peakWorkerScratch = ResolveOwnedWorkerScratchBytes(queue.RequestedAlgorithm);
                    long peakResidentBytes = EstimateResidentBytes(bakeConfig.RuntimeIncremental);
                    telemetry.RecordHotUpdate(
                        collectTicks,
                        stats.BakeTicks,
                        stats.CommitTicks,
                        checked(collectAllocated + bakeCommitAllocated),
                        in stats,
                        peakWorkerScratch,
                        peakResidentBytes,
                        queue.DroppedDirtyCommandCount,
                        queue.CapacityGrowthCount,
                        fallbackCount: 0);
                }
            }
        }

        private long ResolveOwnedWorkerScratchBytes(NavBakeAlgorithmKind algorithm)
        {
            if (algorithm != NavBakeAlgorithmKind.LayeredSpan)
            {
                // Recast/CDT do not own a fixed Core scratch pool; never report 0 as proof.
                return RuntimeNavMeshTelemetryService.AdapterScratchNotOwned;
            }

            if (!_engine.TryGetService(CoreServiceKeys.NavBakeService, out NavBakeService bakeService) ||
                bakeService == null ||
                !bakeService.TryGetAlgorithm(NavBakeAlgorithmKind.LayeredSpan, out INavBakeAlgorithm adapter) ||
                adapter is not LayeredSpanNavBakeAlgorithm layered)
            {
                throw new InvalidOperationException(
                    "LayeredSpan telemetry requires the owned LayeredSpanNavBakeAlgorithm scratch pool; " +
                    "cannot fabricate peakWorkerScratchBytes from config alone.");
            }

            return layered.PreallocatedScratchChannelPayloadBytes;
        }

        private long EstimateResidentBytes(NavRuntimeIncrementalConfig runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (!_engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry registry) ||
                registry == null)
            {
                throw new InvalidOperationException(
                    "RuntimeNavMeshObstacleDirtySystem peakResidentBytes requires NavQueryServices; " +
                    "config-only estimate fallback is forbidden.");
            }

            if (!_engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig bakeConfig) ||
                bakeConfig?.Layers == null)
            {
                throw new InvalidOperationException(
                    "RuntimeNavMeshObstacleDirtySystem peakResidentBytes requires NavMeshBakeConfig.Layers; " +
                    "config-only estimate fallback is forbidden.");
            }

            if (!_engine.TryGetService(CoreServiceKeys.NavMeshProfiles, out NavMeshProfileRegistry profiles) ||
                profiles == null)
            {
                throw new InvalidOperationException(
                    "RuntimeNavMeshObstacleDirtySystem peakResidentBytes requires NavMeshProfiles; " +
                    "config-only estimate fallback is forbidden.");
            }

            long total = 0L;
            int stores = 0;
            for (int li = 0; li < bakeConfig.Layers.Count; li++)
            {
                int layer = bakeConfig.Layers[li].Layer;
                for (int pi = 0; pi < profiles.Count; pi++)
                {
                    if (!registry.TryGetStore(layer, pi, out NavTileStore store) || store == null)
                    {
                        throw new InvalidOperationException(
                            $"RuntimeNavMeshObstacleDirtySystem peakResidentBytes requires NavTileStore for layer={layer} profileIndex={pi}; " +
                            "missing store is an explicit failure (no config-capacity substitute).");
                    }

                    total = checked(total + store.PreallocatedResidentChannelPayloadBytes);
                    stores++;
                }
            }

            if (stores <= 0)
            {
                throw new InvalidOperationException(
                    "RuntimeNavMeshObstacleDirtySystem peakResidentBytes found zero NavTileStore instances; " +
                    "cannot fabricate resident bytes from runtimeIncremental capacities.");
            }

            return total;
        }

        private void EnsureTrackingCapacity(NavRuntimeIncrementalConfig runtime)
        {
            int trackedStructuralEntityCapacity = runtime.TrackedStructuralEntityCapacity;
            if (trackedStructuralEntityCapacity <= 0)
            {
                throw new InvalidOperationException(
                    "NavMeshBakeConfig.runtimeIncremental.trackedStructuralEntityCapacity must be > 0.");
            }

            if (_trackingConfigured && _trackedCapacity == trackedStructuralEntityCapacity)
            {
                return;
            }

            if (_trackingConfigured && _trackedCount > 0)
            {
                throw new InvalidOperationException(
                    "RuntimeNavMeshObstacleDirtySystem cannot resize trackedStructuralEntityCapacity while entities are tracked.");
            }

            _trackedCapacity = trackedStructuralEntityCapacity;
            int indexCapacity = NextPowerOfTwo(checked(trackedStructuralEntityCapacity * 2));
            _indexMask = indexCapacity - 1;
            _indexKeys = new Entity[indexCapacity];
            _indexState = new byte[indexCapacity];
            _indexSlots = new int[indexCapacity];
            _trackedEntities = new Entity[trackedStructuralEntityCapacity];
            _trackedBounds = new WorldAabbCm[trackedStructuralEntityCapacity];
            _trackedShapeSignatures = new int[trackedStructuralEntityCapacity];
            _trackedPoseSignatures = new int[trackedStructuralEntityCapacity];
            _trackedSeenEpoch = new int[trackedStructuralEntityCapacity];
            _removeScratch = new int[trackedStructuralEntityCapacity];
            _publishedScratch = new RuntimeNavMeshRebuildPublishedTile[runtime.PublishedTileCapacity];
            _failureScratch = new NavBakeResultEntry[runtime.StagedEntryCapacity];
            _trackedCount = 0;
            _trackingConfigured = true;
        }

        private void ClearTracking()
        {
            if (_trackedCount == 0)
            {
                return;
            }

            Array.Clear(_indexState, 0, _indexState.Length);
            _trackedCount = 0;
        }

        private void CaptureSingle(
            Entity entity,
            ref WorldPositionCm position,
            ref ManifestationObstacleIntent2D intent,
            ref ManifestationObstacleBridge2DState state,
            RuntimeNavObstacleSnapshot snapshot,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            bool includeNeighbors)
        {
            if (intent.SinkNavigationObstacle == 0)
            {
                TrackOrRemove(entity, queue, includeNeighbors);
                return;
            }

            WorldAabbCm bounds = WriteShapePrimitive(
                snapshot,
                entity,
                pieceIndex: 0,
                intent.Shape,
                state.ShapeDataIndex,
                position.Value,
                ResolveRotation(entity),
                intent.NavMinYcm,
                intent.NavMaxYcm);
            TrackCurrent(entity, bounds, state.ShapeSignature, state.PoseSignature, queue, includeNeighbors);
        }

        private void CaptureCompound(
            Entity entity,
            ref WorldPositionCm position,
            ref CompoundObstacle2DState state,
            RuntimeNavObstacleSnapshot snapshot,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            bool includeNeighbors)
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
                WorldAabbCm bounds = WriteShapePrimitive(
                    snapshot,
                    entity,
                    pieceIndex: i,
                    state.GetShape(i),
                    state.GetShapeDataIndex(i),
                    position.Value,
                    rotation,
                    state.GetNavMinYcm(i),
                    state.GetNavMaxYcm(i));
                combined = hasBounds ? Union(combined, bounds) : bounds;
                hasBounds = true;
            }

            if (!hasBounds)
            {
                TrackOrRemove(entity, queue, includeNeighbors);
                return;
            }

            TrackCurrent(entity, combined, state.ShapeSignature, state.PoseSignature, queue, includeNeighbors);
        }

        private WorldAabbCm WriteShapePrimitive(
            RuntimeNavObstacleSnapshot snapshot,
            Entity entity,
            int pieceIndex,
            ManifestationObstacleShape2D shape,
            int shapeDataIndex,
            Fix64Vec2 worldPosition,
            Fix64 rotation,
            int minYcm,
            int maxYcm)
        {
            if (minYcm >= maxYcm)
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle entity {entity.Id} piece {pieceIndex} requires minYcm < maxYcm for half-open [minYcm,maxYcm).");
            }

            return shape switch
            {
                ManifestationObstacleShape2D.Circle => WriteCircle(snapshot, entity, pieceIndex, shapeDataIndex, worldPosition, rotation, minYcm, maxYcm),
                ManifestationObstacleShape2D.Box => WriteBox(snapshot, entity, pieceIndex, shapeDataIndex, worldPosition, rotation, minYcm, maxYcm),
                ManifestationObstacleShape2D.Polygon => WritePolygon(snapshot, entity, pieceIndex, shapeDataIndex, worldPosition, rotation, minYcm, maxYcm),
                _ => throw new InvalidOperationException($"Unsupported runtime nav obstacle shape '{shape}' on entity {entity.Id}.")
            };
        }

        private WorldAabbCm WriteCircle(
            RuntimeNavObstacleSnapshot snapshot,
            Entity entity,
            int pieceIndex,
            int shapeDataIndex,
            Fix64Vec2 worldPosition,
            Fix64 rotation,
            int minYcm,
            int maxYcm)
        {
            if (!_shapeStorage.TryGetCircle(shapeDataIndex, out CircleShapeData circle))
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle entity {entity.Id} piece {pieceIndex} references missing circle shape data index {shapeDataIndex}.");
            }

            Fix64Vec2 center = ShapeWorldTransform2D.GetCircleCenter(worldPosition, rotation, circle);
            int centerX = center.X.RoundToInt();
            int centerZ = center.Y.RoundToInt();
            int radiusCm = circle.Radius.RoundToInt();
            int index = snapshot.BeginPrimitive(entity.Id, pieceIndex, NavObstacleKind.Circle, minYcm, maxYcm);
            snapshot.SetCircle(index, centerX, centerZ, radiusCm);
            return WorldAabbCm.FromCenterRadius(new WorldCmInt2(centerX, centerZ), radiusCm);
        }

        private WorldAabbCm WriteBox(
            RuntimeNavObstacleSnapshot snapshot,
            Entity entity,
            int pieceIndex,
            int shapeDataIndex,
            Fix64Vec2 worldPosition,
            Fix64 rotation,
            int minYcm,
            int maxYcm)
        {
            if (!_shapeStorage.TryGetBox(shapeDataIndex, out BoxShapeData box))
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle entity {entity.Id} piece {pieceIndex} references missing box shape data index {shapeDataIndex}.");
            }

            Fix64Vec2 center = ShapeWorldTransform2D.GetBoxCenter(worldPosition, rotation, box);
            int index = snapshot.BeginPrimitive(entity.Id, pieceIndex, NavObstacleKind.Polygon, minYcm, maxYcm);
            int vertexOffset = snapshot.BeginPolygonVertices(index, 4);

            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            WriteBoxCorner(snapshot, vertexOffset + 0, center, rotation, -box.HalfWidth, -box.HalfHeight, ref minX, ref maxX, ref minZ, ref maxZ);
            WriteBoxCorner(snapshot, vertexOffset + 1, center, rotation, box.HalfWidth, -box.HalfHeight, ref minX, ref maxX, ref minZ, ref maxZ);
            WriteBoxCorner(snapshot, vertexOffset + 2, center, rotation, box.HalfWidth, box.HalfHeight, ref minX, ref maxX, ref minZ, ref maxZ);
            WriteBoxCorner(snapshot, vertexOffset + 3, center, rotation, -box.HalfWidth, box.HalfHeight, ref minX, ref maxX, ref minZ, ref maxZ);
            return new WorldAabbCm(minX, minZ, Math.Max(1, maxX - minX), Math.Max(1, maxZ - minZ));
        }

        private static void WriteBoxCorner(
            RuntimeNavObstacleSnapshot snapshot,
            int absoluteVertexIndex,
            Fix64Vec2 center,
            Fix64 rotation,
            Fix64 localX,
            Fix64 localY,
            ref int minX,
            ref int maxX,
            ref int minY,
            ref int maxY)
        {
            Fix64Vec2 vertex = center + ShapeWorldTransform2D.RotateLocal(new Fix64Vec2(localX, localY), rotation);
            int xcm = vertex.X.RoundToInt();
            int zcm = vertex.Y.RoundToInt();
            snapshot.SetPolygonVertex(absoluteVertexIndex, xcm, zcm);
            if (xcm < minX) minX = xcm;
            if (xcm > maxX) maxX = xcm;
            if (zcm < minY) minY = zcm;
            if (zcm > maxY) maxY = zcm;
        }

        private WorldAabbCm WritePolygon(
            RuntimeNavObstacleSnapshot snapshot,
            Entity entity,
            int pieceIndex,
            int shapeDataIndex,
            Fix64Vec2 worldPosition,
            Fix64 rotation,
            int minYcm,
            int maxYcm)
        {
            if (!_shapeStorage.TryGetPolygon(shapeDataIndex, out PolygonShapeData polygon))
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle entity {entity.Id} piece {pieceIndex} references missing polygon shape data index {shapeDataIndex}.");
            }

            if (polygon.VertexCount < 3)
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle entity {entity.Id} piece {pieceIndex} polygon requires at least 3 points.");
            }

            int index = snapshot.BeginPrimitive(entity.Id, pieceIndex, NavObstacleKind.Polygon, minYcm, maxYcm);
            int vertexOffset = snapshot.BeginPolygonVertices(index, polygon.VertexCount);
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            for (int i = 0; i < polygon.VertexCount; i++)
            {
                Fix64Vec2 vertex = ShapeWorldTransform2D.GetPolygonWorldVertex(worldPosition, rotation, polygon, i);
                int xcm = vertex.X.RoundToInt();
                int zcm = vertex.Y.RoundToInt();
                snapshot.SetPolygonVertex(vertexOffset + i, xcm, zcm);
                if (xcm < minX) minX = xcm;
                if (xcm > maxX) maxX = xcm;
                if (zcm < minZ) minZ = zcm;
                if (zcm > maxZ) maxZ = zcm;
            }

            return new WorldAabbCm(minX, minZ, Math.Max(1, maxX - minX), Math.Max(1, maxZ - minZ));
        }

        private void TrackCurrent(
            Entity entity,
            WorldAabbCm bounds,
            int shapeSignature,
            int poseSignature,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            bool includeNeighbors)
        {
            if (TryFindTrackedIndex(entity, out int trackIndex, out int indexSlot))
            {
                _trackedSeenEpoch[trackIndex] = _updateEpoch;
                if (_trackedShapeSignatures[trackIndex] == shapeSignature &&
                    _trackedPoseSignatures[trackIndex] == poseSignature &&
                    _trackedBounds[trackIndex] == bounds)
                {
                    return;
                }

                WorldAabbCm previousBounds = _trackedBounds[trackIndex];
                if (OverlapsOrTouches(previousBounds, bounds))
                {
                    queue.EnqueueDirtyAabb(Union(previousBounds, bounds), includeNeighbors);
                }
                else
                {
                    // Long-range teleports (for example, parking pool to gate) dirty both endpoints.
                    // Unioning them floods dirtyTileCapacity with empty intermediate tiles.
                    queue.EnqueueDirtyAabb(previousBounds, includeNeighbors);
                    queue.EnqueueDirtyAabb(bounds, includeNeighbors);
                }

                _trackedBounds[trackIndex] = bounds;
                _trackedShapeSignatures[trackIndex] = shapeSignature;
                _trackedPoseSignatures[trackIndex] = poseSignature;
                return;
            }

            if (_trackedCount >= _trackedCapacity)
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle snapshot exceeded trackedStructuralEntityCapacity ({_trackedCapacity}); required {_trackedCount + 1}.");
            }

            trackIndex = _trackedCount;
            InsertTrackedIndex(entity, trackIndex, indexSlot);
            _trackedEntities[trackIndex] = entity;
            _trackedBounds[trackIndex] = bounds;
            _trackedShapeSignatures[trackIndex] = shapeSignature;
            _trackedPoseSignatures[trackIndex] = poseSignature;
            _trackedSeenEpoch[trackIndex] = _updateEpoch;
            _trackedCount++;
            queue.EnqueueDirtyAabb(bounds, includeNeighbors);
        }

        private void TrackOrRemove(Entity entity, RuntimeIncrementalNavMeshRebuildQueue queue, bool includeNeighbors)
        {
            if (!TryFindTrackedIndex(entity, out int trackIndex, out _))
            {
                return;
            }

            _trackedSeenEpoch[trackIndex] = _updateEpoch;
            RemoveTrackedAt(trackIndex, queue, includeNeighbors);
        }

        private void RemoveMissingTracked(RuntimeIncrementalNavMeshRebuildQueue queue, bool includeNeighbors)
        {
            int removeCount = 0;
            for (int i = 0; i < _trackedCount; i++)
            {
                if (_trackedSeenEpoch[i] == _updateEpoch &&
                    World.IsAlive(_trackedEntities[i]))
                {
                    continue;
                }

                _removeScratch[removeCount++] = i;
            }

            for (int i = removeCount - 1; i >= 0; i--)
            {
                RemoveTrackedAt(_removeScratch[i], queue, includeNeighbors);
            }
        }

        private void RemoveTrackedAt(int trackIndex, RuntimeIncrementalNavMeshRebuildQueue queue, bool includeNeighbors)
        {
            Entity entity = _trackedEntities[trackIndex];
            WorldAabbCm bounds = _trackedBounds[trackIndex];
            queue.EnqueueDirtyAabb(bounds, includeNeighbors);

            if (!TryFindTrackedIndex(entity, out _, out int indexSlot) || _indexState[indexSlot] != IndexOccupied)
            {
                throw new InvalidOperationException("Tracked structural entity index desynchronized.");
            }

            _indexState[indexSlot] = IndexTombstone;
            _indexKeys[indexSlot] = default;
            _indexSlots[indexSlot] = -1;

            int last = _trackedCount - 1;
            if (trackIndex != last)
            {
                Entity moved = _trackedEntities[last];
                _trackedEntities[trackIndex] = moved;
                _trackedBounds[trackIndex] = _trackedBounds[last];
                _trackedShapeSignatures[trackIndex] = _trackedShapeSignatures[last];
                _trackedPoseSignatures[trackIndex] = _trackedPoseSignatures[last];
                _trackedSeenEpoch[trackIndex] = _trackedSeenEpoch[last];
                if (!TryFindTrackedIndex(moved, out _, out int movedSlot) || _indexState[movedSlot] != IndexOccupied)
                {
                    throw new InvalidOperationException("Tracked structural entity index desynchronized while compacting.");
                }

                _indexSlots[movedSlot] = trackIndex;
            }

            _trackedCount = last;
        }

        private bool TryFindTrackedIndex(Entity entity, out int trackIndex, out int indexSlot)
        {
            if (_indexKeys.Length == 0)
            {
                trackIndex = -1;
                indexSlot = -1;
                return false;
            }

            int start = HashEntity(entity) & _indexMask;
            int firstTombstone = -1;
            for (int i = 0; i < _indexKeys.Length; i++)
            {
                int slot = (start + i) & _indexMask;
                byte state = _indexState[slot];
                if (state == IndexEmpty)
                {
                    indexSlot = firstTombstone >= 0 ? firstTombstone : slot;
                    trackIndex = -1;
                    return false;
                }

                if (state == IndexTombstone)
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = slot;
                    }

                    continue;
                }

                if (_indexKeys[slot].Equals(entity))
                {
                    indexSlot = slot;
                    trackIndex = _indexSlots[slot];
                    return true;
                }
            }

            indexSlot = firstTombstone >= 0 ? firstTombstone : -1;
            trackIndex = -1;
            return false;
        }

        private void InsertTrackedIndex(Entity entity, int trackIndex, int indexSlot)
        {
            if (indexSlot < 0)
            {
                throw new InvalidOperationException(
                    $"Runtime nav obstacle snapshot exceeded trackedStructuralEntityCapacity ({_trackedCapacity}); required {_trackedCount + 1}.");
            }

            _indexKeys[indexSlot] = entity;
            _indexState[indexSlot] = IndexOccupied;
            _indexSlots[indexSlot] = trackIndex;
        }

        private static int HashEntity(Entity entity)
        {
            unchecked
            {
                int hash = entity.Id;
                hash = (hash * 397) ^ entity.WorldId;
                hash = (hash * 397) ^ entity.Version;
                return hash;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value < 1)
            {
                return 1;
            }

            uint v = (uint)value;
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return (int)v;
        }

        private Fix64 ResolveRotation(Entity entity)
        {
            return World.TryGet(entity, out FacingDirection facing)
                ? Fix64.FromFloat(facing.AngleRad)
                : Fix64.Zero;
        }

        private static WorldAabbCm Union(WorldAabbCm a, WorldAabbCm b)
        {
            int left = Math.Min(a.Left, b.Left);
            int top = Math.Min(a.Top, b.Top);
            int right = Math.Max(a.Right, b.Right);
            int bottom = Math.Max(a.Bottom, b.Bottom);
            return new WorldAabbCm(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private static bool OverlapsOrTouches(WorldAabbCm a, WorldAabbCm b)
        {
            return a.Left <= b.Right &&
                   b.Left <= a.Right &&
                   a.Top <= b.Bottom &&
                   b.Top <= a.Bottom;
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

        private struct CaptureSingleJob : IForEachWithEntity<WorldPositionCm, ManifestationObstacleIntent2D, ManifestationObstacleBridge2DState>
        {
            public RuntimeNavMeshObstacleDirtySystem System;
            public RuntimeNavObstacleSnapshot Snapshot;
            public RuntimeIncrementalNavMeshRebuildQueue Queue;
            public bool IncludeNeighbors;

            public void Update(
                Entity entity,
                ref WorldPositionCm position,
                ref ManifestationObstacleIntent2D intent,
                ref ManifestationObstacleBridge2DState state)
            {
                System.CaptureSingle(entity, ref position, ref intent, ref state, Snapshot, Queue, IncludeNeighbors);
            }
        }

        private struct CaptureCompoundJob : IForEachWithEntity<WorldPositionCm, CompoundObstacle2DState>
        {
            public RuntimeNavMeshObstacleDirtySystem System;
            public RuntimeNavObstacleSnapshot Snapshot;
            public RuntimeIncrementalNavMeshRebuildQueue Queue;
            public bool IncludeNeighbors;

            public void Update(Entity entity, ref WorldPositionCm position, ref CompoundObstacle2DState state)
            {
                System.CaptureCompound(entity, ref position, ref state, Snapshot, Queue, IncludeNeighbors);
            }
        }
    }
}
