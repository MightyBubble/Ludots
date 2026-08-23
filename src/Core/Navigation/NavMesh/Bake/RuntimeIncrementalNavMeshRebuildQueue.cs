using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public readonly struct RuntimeNavMeshRebuildPublishedTile
    {
        public RuntimeNavMeshRebuildPublishedTile(
            NavBakeTileCoord target,
            int layer,
            int profileIndex,
            string profileId,
            uint storeRevision)
        {
            Target = target;
            Layer = layer;
            ProfileIndex = profileIndex;
            ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            StoreRevision = storeRevision;
        }

        public NavBakeTileCoord Target { get; }

        public int Layer { get; }

        public int ProfileIndex { get; }

        public string ProfileId { get; }

        public uint StoreRevision { get; }
    }

    public sealed class RuntimeNavMeshRebuildBatch
    {
        public RuntimeNavMeshRebuildBatch(
            int requestedTileBudget,
            int rebuiltTileCount,
            int failedEntryCount,
            int pendingTileCount,
            IReadOnlyList<RuntimeNavMeshRebuildPublishedTile> publishedTiles,
            IReadOnlyList<NavBakeResultEntry> failedEntries)
        {
            RequestedTileBudget = requestedTileBudget;
            RebuiltTileCount = rebuiltTileCount;
            FailedEntryCount = failedEntryCount;
            PendingTileCount = pendingTileCount;
            PublishedTiles = publishedTiles ?? throw new ArgumentNullException(nameof(publishedTiles));
            FailedEntries = failedEntries ?? throw new ArgumentNullException(nameof(failedEntries));
        }

        public int RequestedTileBudget { get; }

        public int RebuiltTileCount { get; }

        public int FailedEntryCount { get; }

        public int PendingTileCount { get; }

        public IReadOnlyList<RuntimeNavMeshRebuildPublishedTile> PublishedTiles { get; }

        public IReadOnlyList<NavBakeResultEntry> FailedEntries { get; }
    }

    public sealed class RuntimeIncrementalNavMeshRebuildQueue
    {
        private readonly NavBakeService _bakeService;
        private readonly NavBakeContext _baseContext;
        private readonly NavQueryServiceRegistry _queryServices;
        private readonly NavMeshProfileRegistry _profiles;
        private readonly Queue<NavBakeTileCoord> _fifo = new Queue<NavBakeTileCoord>();
        private readonly HashSet<NavBakeTileCoord> _queued = new HashSet<NavBakeTileCoord>();

        public RuntimeIncrementalNavMeshRebuildQueue(
            NavBakeService bakeService,
            NavBakeContext baseContext,
            NavQueryServiceRegistry queryServices,
            NavMeshProfileRegistry profiles)
        {
            _bakeService = bakeService ?? throw new ArgumentNullException(nameof(bakeService));
            _baseContext = baseContext ?? throw new ArgumentNullException(nameof(baseContext));
            _queryServices = queryServices ?? throw new ArgumentNullException(nameof(queryServices));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

            _baseContext.Validate();
            if (_baseContext.Mode != NavBakeMode.RuntimeIncremental)
            {
                throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue requires NavBakeContext.mode 'runtime-incremental'.");
            }

            if (_baseContext.Algorithm != NavBakeAlgorithmKind.Cdt &&
                _baseContext.Algorithm != NavBakeAlgorithmKind.Recast)
            {
                throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue requires NavBakeContext.algorithm 'cdt' or 'recast'.");
            }
        }

        public int PendingTileCount => _fifo.Count;

        public bool EnqueueDirtyTile(NavBakeTileCoord target)
        {
            RequireTargetInRange(target, nameof(target));
            if (!_queued.Add(target))
            {
                return false;
            }

            _fifo.Enqueue(target);
            return true;
        }

        public int EnqueueDirtyAabb(WorldAabbCm dirtyAabb, bool includeNeighbors)
        {
            if (dirtyAabb.Width <= 0 || dirtyAabb.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dirtyAabb), "Dirty AABB width and height must be > 0.");
            }

            LogicTerrainField terrain = _baseContext.Terrain;
            int tileWidthCm = checked(terrain.ChunkSizeCells * terrain.HorizontalStepCm);
            int tileHeightCm = checked(terrain.ChunkSizeCells * terrain.VerticalStepCm);
            if (tileWidthCm <= 0 || tileHeightCm <= 0)
            {
                throw new InvalidOperationException("LogicTerrainField chunk world size must be > 0.");
            }

            int minChunkX = MathUtil.FloorDiv(dirtyAabb.Left, tileWidthCm);
            int minChunkY = MathUtil.FloorDiv(dirtyAabb.Top, tileHeightCm);
            int maxChunkX = MathUtil.FloorDiv(dirtyAabb.Right - 1, tileWidthCm);
            int maxChunkY = MathUtil.FloorDiv(dirtyAabb.Bottom - 1, tileHeightCm);

            if (maxChunkX < 0 ||
                maxChunkY < 0 ||
                minChunkX >= terrain.WidthChunks ||
                minChunkY >= terrain.HeightChunks)
            {
                return 0;
            }

            if (includeNeighbors)
            {
                minChunkX--;
                minChunkY--;
                maxChunkX++;
                maxChunkY++;
            }

            minChunkX = MathUtil.Clamp(minChunkX, 0, terrain.WidthChunks - 1);
            maxChunkX = MathUtil.Clamp(maxChunkX, 0, terrain.WidthChunks - 1);
            minChunkY = MathUtil.Clamp(minChunkY, 0, terrain.HeightChunks - 1);
            maxChunkY = MathUtil.Clamp(maxChunkY, 0, terrain.HeightChunks - 1);

            if (minChunkX > maxChunkX || minChunkY > maxChunkY)
            {
                return 0;
            }

            int added = 0;
            for (int cy = minChunkY; cy <= maxChunkY; cy++)
            {
                for (int cx = minChunkX; cx <= maxChunkX; cx++)
                {
                    if (EnqueueDirtyTile(new NavBakeTileCoord(cx, cy)))
                    {
                        added++;
                    }
                }
            }

            return added;
        }

        public RuntimeNavMeshRebuildBatch ProcessBudget(int maxTiles)
        {
            if (maxTiles <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTiles), "Runtime navmesh rebuild budget must be > 0.");
            }

            var published = new List<RuntimeNavMeshRebuildPublishedTile>();
            var failures = new List<NavBakeResultEntry>();
            int processedTiles = 0;

            while (processedTiles < maxTiles && _fifo.Count > 0)
            {
                NavBakeTileCoord target = _fifo.Dequeue();
                _queued.Remove(target);

                NavBakeContext frameContext = CreateSingleTargetContext(target);
                NavBakeResult result = _bakeService.Bake(frameContext);
                for (int i = 0; i < result.Entries.Count; i++)
                {
                    NavBakeResultEntry entry = result.Entries[i];
                    if (!entry.Success)
                    {
                        failures.Add(entry);
                        continue;
                    }

                    if (!_profiles.TryGetIndex(entry.ProfileId, out int profileIndex))
                    {
                        throw new InvalidOperationException(
                            $"Runtime navmesh rebuild produced profile '{entry.ProfileId}' that is not registered.");
                    }

                    if (!_queryServices.TryGetStore(entry.Layer, profileIndex, out NavTileStore store))
                    {
                        throw new InvalidOperationException(
                            $"Runtime navmesh rebuild cannot publish layer {entry.Layer}, profile '{entry.ProfileId}' because no NavTileStore is registered.");
                    }

                    uint revision = store.Replace(entry.Tile);
                    published.Add(new RuntimeNavMeshRebuildPublishedTile(
                        entry.Target,
                        entry.Layer,
                        profileIndex,
                        entry.ProfileId,
                        revision));
                }

                processedTiles++;
            }

            return new RuntimeNavMeshRebuildBatch(
                maxTiles,
                processedTiles,
                failures.Count,
                _fifo.Count,
                published,
                failures);
        }

        private NavBakeContext CreateSingleTargetContext(NavBakeTileCoord target)
        {
            return new NavBakeContext
            {
                MapId = _baseContext.MapId,
                ModId = _baseContext.ModId,
                SourceUri = _baseContext.SourceUri,
                Terrain = _baseContext.Terrain,
                Obstacles = _baseContext.Obstacles,
                Config = _baseContext.Config,
                AgentProfiles = _baseContext.AgentProfiles,
                Targets = new[] { target },
                BuildConfig = _baseContext.BuildConfig,
                TileVersion = _baseContext.TileVersion + 1u,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = _baseContext.Algorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private void RequireTargetInRange(NavBakeTileCoord target, string argumentName)
        {
            LogicTerrainField terrain = _baseContext.Terrain;
            if (target.ChunkX < 0 ||
                target.ChunkY < 0 ||
                target.ChunkX >= terrain.WidthChunks ||
                target.ChunkY >= terrain.HeightChunks)
            {
                throw new ArgumentOutOfRangeException(argumentName, $"Dirty nav tile is out of terrain range: {target}.");
            }
        }
    }
}
