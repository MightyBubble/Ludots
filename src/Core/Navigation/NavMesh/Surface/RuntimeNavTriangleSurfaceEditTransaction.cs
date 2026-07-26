using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Formal atomic owner for runtime triangle-surface terrain edits.
    /// Stages an immutable before/after <see cref="NavTriangleSurfaceTileIndex"/> without publishing;
    /// commit (only while the rebuild queue is idle) updates
    /// <see cref="RuntimeNavTriangleSurfaceService"/>,
    /// <c>CoreServiceKeys.NavTriangleSurface</c> via the injected publisher,
    /// <see cref="RuntimeIncrementalNavMeshRebuildQueue.ReplaceTriangleSurface"/>,
    /// and enqueues the dirty AABB tiles in deterministic order.
    /// </summary>
    public sealed class RuntimeNavTriangleSurfaceEditTransaction
    {
        private readonly RuntimeNavTriangleSurfaceService _surfaceService;
        private readonly RuntimeIncrementalNavMeshRebuildQueue _queue;
        private readonly Action<NavTriangleSurfaceTileIndex> _publishOwnedSurfaceKey;
        private readonly bool _includeNeighborTiles;

        private NavTriangleSurfaceTileIndex? _stagedBefore;
        private NavTriangleSurfaceTileIndex? _stagedAfter;
        private WorldAabbCm _stagedDirtyAabb;
        private bool _hasStaged;

        private NavTriangleSurfaceTileIndex? _restorableBeforeImage;
        private WorldAabbCm _restorableDirtyAabb;

        public RuntimeNavTriangleSurfaceEditTransaction(
            RuntimeNavTriangleSurfaceService surfaceService,
            RuntimeIncrementalNavMeshRebuildQueue queue,
            Action<NavTriangleSurfaceTileIndex> publishOwnedSurfaceKey,
            bool includeNeighborTiles)
        {
            _surfaceService = surfaceService ?? throw new ArgumentNullException(nameof(surfaceService));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _publishOwnedSurfaceKey = publishOwnedSurfaceKey
                ?? throw new ArgumentNullException(nameof(publishOwnedSurfaceKey));
            _includeNeighborTiles = includeNeighborTiles;
        }

        public bool HasStaged => _hasStaged;

        public bool HasRestorableBeforeImage => _restorableBeforeImage != null;

        public WorldAabbCm StagedDirtyAabb
        {
            get
            {
                RequireStaged();
                return _stagedDirtyAabb;
            }
        }

        public NavTriangleSurfaceTileIndex StagedAfter
        {
            get
            {
                RequireStaged();
                return _stagedAfter!;
            }
        }

        public NavTriangleSurfaceTileIndex StagedBefore
        {
            get
            {
                RequireStaged();
                return _stagedBefore!;
            }
        }

        /// <summary>
        /// Stages a brush-derived after-image against the currently published surface.
        /// Does not publish, replace queue input, or enqueue dirty work.
        /// </summary>
        public void StageBrush(in NavTriangleSurfaceTerrainBrushSpec spec)
        {
            RequireNoStaged();
            NavTriangleSurfaceTileIndex before = _surfaceService.Published;
            NavTriangleSurfaceTileIndex after = NavTriangleSurfaceTerrainBrush.Apply(before, spec, out WorldAabbCm dirty);
            ValidateDirtyAabbAgainstCommittedResidency(dirty, nameof(StageBrush));
            _stagedBefore = before;
            _stagedAfter = after;
            _stagedDirtyAabb = dirty;
            _hasStaged = true;
        }

        /// <summary>
        /// Stages the captured before-image from the last successful commit as the next after-image.
        /// Reloading a map is forbidden; restore must go through this coordinator.
        /// </summary>
        public void StageExactRestore()
        {
            RequireNoStaged();
            if (_restorableBeforeImage == null)
            {
                throw new InvalidOperationException(
                    "StageExactRestore refused: no restorable before-image has been captured by a prior commit.");
            }

            NavTriangleSurfaceTileIndex before = _surfaceService.Published;
            NavTriangleSurfaceTileIndex after = _restorableBeforeImage;
            if (ReferenceEquals(before, after))
            {
                throw new InvalidOperationException(
                    "StageExactRestore refused: published surface already equals the restorable before-image.");
            }

            WorldAabbCm dirty = _restorableDirtyAabb;
            ValidateDirtyAabbAgainstCommittedResidency(dirty, nameof(StageExactRestore));
            _stagedBefore = before;
            _stagedAfter = after;
            _stagedDirtyAabb = dirty;
            _hasStaged = true;
        }

        public void ClearStaged()
        {
            _stagedBefore = null;
            _stagedAfter = null;
            _stagedDirtyAabb = default;
            _hasStaged = false;
        }

        /// <summary>
        /// Publishes the staged after-image to the surface service, owned service key, and rebuild queue,
        /// then enqueues dirty tiles. Rejected while the queue is not idle (pending or sealed/baking).
        /// Failure before mutation leaves surface, queue source, dirty work, and store generation unchanged.
        /// After warmup this path is allocation-free aside from exception strings on failure.
        /// </summary>
        public void Commit()
        {
            RequireStaged();
            if (_queue.Status != RuntimeNavMeshRebuildStatus.Idle)
            {
                throw new InvalidOperationException(
                    $"RuntimeNavTriangleSurfaceEditTransaction.Commit refused: rebuild queue status is '{_queue.Status}' " +
                    $"(pending={_queue.PendingTileCount}, sealedRemaining={_queue.SealedRemainingCount}). " +
                    "Commit requires an idle queue.");
            }

            if (_queue.SealedRemainingCount > 0)
            {
                throw new InvalidOperationException(
                    "RuntimeNavTriangleSurfaceEditTransaction.Commit refused: a generation is sealed/baking.");
            }

            WorldAabbCm dirty = _stagedDirtyAabb;
            ValidateDirtyAabbAgainstCommittedResidency(dirty, nameof(Commit));
            int expectedAdds = CountDirtyTilesToEnqueue(dirty, _includeNeighborTiles);
            if (expectedAdds <= 0)
            {
                throw new InvalidOperationException(
                    $"RuntimeNavTriangleSurfaceEditTransaction.Commit refused: dirty AABB {dirty} enqueues zero resident tiles.");
            }

            if (expectedAdds > _queue.FreeDirtyTileCapacity)
            {
                throw new InvalidOperationException(
                    $"RuntimeNavTriangleSurfaceEditTransaction.Commit refused: dirty tile capacity " +
                    $"{_queue.DirtyTileCapacity} has {_queue.FreeDirtyTileCapacity} free slots, required {expectedAdds}. " +
                    "Surface publication was not changed.");
            }

            NavTriangleSurfaceTileIndex after = _stagedAfter
                ?? throw new InvalidOperationException("Staged after-image is missing.");
            NavTriangleSurfaceTileIndex before = _stagedBefore
                ?? throw new InvalidOperationException("Staged before-image is missing.");

            // Mutation phase: validate completed; publish all three ownership seams then enqueue.
            _surfaceService.Publish(after);
            _publishOwnedSurfaceKey(after);
            _queue.ReplaceTriangleSurface(after);
            int added = _queue.EnqueueDirtyAabb(dirty, _includeNeighborTiles);
            if (added != expectedAdds)
            {
                throw new InvalidOperationException(
                    $"RuntimeNavTriangleSurfaceEditTransaction.Commit enqueue mismatch: expected {expectedAdds} dirty tiles, enqueued {added}. " +
                    "Surface was already published; this indicates a residency/capacity contract break.");
            }

            _restorableBeforeImage = before;
            _restorableDirtyAabb = dirty;
            ClearStaged();
        }

        private void ValidateDirtyAabbAgainstCommittedResidency(WorldAabbCm dirtyAabb, string api)
        {
            if (dirtyAabb.Width <= 0 || dirtyAabb.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"{api} refused: dirty AABB must have positive width and height ({dirtyAabb}).");
            }

            if (_queue.CommittedResidentWindowCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{api} refused: committed resident window is empty; terrain edits fail closed until residency is committed.");
            }

            ResolveTileRange(dirtyAabb, _includeNeighborTiles, out int minX, out int maxX, out int minZ, out int maxZ);
            bool anyResident = false;
            for (int tz = minZ; tz <= maxZ; tz++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    var tile = new NavBakeTileCoord(tx, tz);
                    if (!_queue.IsCommittedResidentTile(tile))
                    {
                        throw new InvalidOperationException(
                            $"{api} refused: dirty tile ({tx},{tz}) is outside the committed resident window " +
                            $"(dirtyAABB={dirtyAabb}, includeNeighbors={_includeNeighborTiles}).");
                    }

                    anyResident = true;
                }
            }

            if (!anyResident)
            {
                throw new InvalidOperationException(
                    $"{api} refused: dirty AABB {dirtyAabb} does not intersect any grid tile.");
            }
        }

        private int CountDirtyTilesToEnqueue(WorldAabbCm dirtyAabb, bool includeNeighbors)
        {
            ResolveTileRange(dirtyAabb, includeNeighbors, out int minX, out int maxX, out int minZ, out int maxZ);
            int count = 0;
            for (int tz = minZ; tz <= maxZ; tz++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    count = checked(count + 1);
                }
            }

            return count;
        }

        private void ResolveTileRange(
            WorldAabbCm dirtyAabb,
            bool includeNeighbors,
            out int minChunkX,
            out int maxChunkX,
            out int minChunkZ,
            out int maxChunkZ)
        {
            NavTriangleSurfaceTileGrid grid = _surfaceService.Published.Grid;
            minChunkX = MathUtil.FloorDiv(checked(dirtyAabb.Left - grid.OriginXcm), grid.TileWidthCm);
            minChunkZ = MathUtil.FloorDiv(checked(dirtyAabb.Top - grid.OriginZcm), grid.TileHeightCm);
            maxChunkX = MathUtil.FloorDiv(checked(dirtyAabb.Right - 1 - grid.OriginXcm), grid.TileWidthCm);
            maxChunkZ = MathUtil.FloorDiv(checked(dirtyAabb.Bottom - 1 - grid.OriginZcm), grid.TileHeightCm);

            if (maxChunkX < 0 ||
                maxChunkZ < 0 ||
                minChunkX >= grid.TileCountX ||
                minChunkZ >= grid.TileCountZ)
            {
                throw new InvalidOperationException(
                    $"Dirty AABB {dirtyAabb} is outside the triangle-surface tile grid " +
                    $"(origin=({grid.OriginXcm},{grid.OriginZcm}), tileSize=({grid.TileWidthCm},{grid.TileHeightCm}), " +
                    $"tileCount=({grid.TileCountX},{grid.TileCountZ})).");
            }

            if (includeNeighbors)
            {
                minChunkX--;
                minChunkZ--;
                maxChunkX++;
                maxChunkZ++;
            }

            if (minChunkX < 0) minChunkX = 0;
            if (minChunkZ < 0) minChunkZ = 0;
            if (maxChunkX >= grid.TileCountX) maxChunkX = grid.TileCountX - 1;
            if (maxChunkZ >= grid.TileCountZ) maxChunkZ = grid.TileCountZ - 1;

            if (minChunkX > maxChunkX || minChunkZ > maxChunkZ)
            {
                throw new InvalidOperationException(
                    $"Dirty AABB {dirtyAabb} clamped to an empty tile range (includeNeighbors={includeNeighbors}).");
            }
        }

        private void RequireNoStaged()
        {
            if (_hasStaged)
            {
                throw new InvalidOperationException(
                    "An edit is already staged. Commit or ClearStaged before staging another terrain edit.");
            }
        }

        private void RequireStaged()
        {
            if (!_hasStaged)
            {
                throw new InvalidOperationException(
                    "No terrain edit is staged. StageBrush or StageExactRestore before Commit.");
            }
        }
    }
}
