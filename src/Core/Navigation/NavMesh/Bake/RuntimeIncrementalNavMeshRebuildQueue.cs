using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Config;
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

    /// <summary>
    /// 运行时增量重烤队列：脏瓦片在专用后台线程上烘焙，游戏线程的 <see cref="ProcessBudget"/>
    /// 只负责提交（附障碍快照）与发布（写 store、推进 Revision），单瓦片 Recast/CDT 烘焙不再阻塞 fixed tick。
    /// 线程契约：Enqueue*/ProcessBudget/PendingTilesSnapshot/Dispose 仅限游戏线程调用；
    /// 基上下文的 Terrain/Config/AgentProfiles 在队列存活期间必须不可变（运行时增量的脏源只有障碍，
    /// 障碍集合每 tick 由游戏线程重建，提交时快照隔离）；烘焙期异常延迟到发布泵在游戏线程重抛。
    /// </summary>
    public sealed class RuntimeIncrementalNavMeshRebuildQueue : IDisposable
    {
        private const int MaxOutstandingBakeRequests = 64;
        private static readonly TimeSpan DisposeJoinTimeout = TimeSpan.FromSeconds(10);

        private readonly NavBakeService _bakeService;
        private readonly NavBakeContext _baseContext;
        private readonly NavQueryServiceRegistry _queryServices;
        private readonly NavMeshProfileRegistry _profiles;
        private readonly Queue<NavBakeTileCoord> _fifo = new Queue<NavBakeTileCoord>();
        private readonly HashSet<NavBakeTileCoord> _queued = new HashSet<NavBakeTileCoord>();
        private readonly List<NavBakeTileCoord> _inFlightTargets = new List<NavBakeTileCoord>();
        private readonly BlockingCollection<BakeRequest> _bakeRequests;
        private readonly Queue<CompletedBake> _completedBakes = new Queue<CompletedBake>();
        private readonly object _completedGate = new object();
        private readonly Thread _workerThread;
        private bool _disposed;

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

            _bakeRequests = new BlockingCollection<BakeRequest>(MaxOutstandingBakeRequests);
            _workerThread = new Thread(BakeWorkerLoop)
            {
                IsBackground = true,
                Name = "ludots-navmesh-runtime-rebake",
            };
            _workerThread.Start();
        }

        /// <summary>待处理瓦片数：未提交 FIFO + 已提交未发布（在途与已完成待发布）。</summary>
        public int PendingTileCount => _fifo.Count + _inFlightTargets.Count;

        /// <summary>False 时泵送循环既不提交也不发布——供"冻结增量重烤"的消融演示与诊断使用。</summary>
        public bool ProcessingEnabled { get; set; } = true;

        /// <summary>最近一次发布的单瓦片烘焙自挂钟耗时（ms）；从未执行过为 0。</summary>
        public double LastBatchElapsedMs { get; private set; }

        /// <summary>待重烤瓦片坐标快照（按提交顺序，含在途瓦片），供脏瓦片可视化与诊断读取。</summary>
        public NavBakeTileCoord[] PendingTilesSnapshot()
        {
            var tiles = new NavBakeTileCoord[_fifo.Count + _inFlightTargets.Count];
            int index = 0;
            foreach (NavBakeTileCoord target in _fifo)
            {
                tiles[index++] = target;
            }

            for (int i = 0; i < _inFlightTargets.Count; i++)
            {
                tiles[index++] = _inFlightTargets[i];
            }

            return tiles;
        }

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
            int tileWidthCm = terrain.ChunkWidthCm;
            int tileHeightCm = terrain.ChunkHeightCm;
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

        /// <summary>
        /// 游戏线程泵：提交至多 maxTiles 个待处理瓦片到后台烘焙（后台管线满则本轮少提交），
        /// 随后发布全部已完成结果；maxTiles = 0 表示只发布不提交。
        /// </summary>
        public RuntimeNavMeshRebuildBatch ProcessBudget(int maxTiles)
        {
            if (maxTiles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTiles), "Runtime navmesh rebuild budget must be >= 0.");
            }

            if (!ProcessingEnabled)
            {
                return new RuntimeNavMeshRebuildBatch(0, 0, 0, PendingTileCount, Array.Empty<RuntimeNavMeshRebuildPublishedTile>(), Array.Empty<NavBakeResultEntry>());
            }

            if (maxTiles > 0 && _fifo.Count > 0)
            {
                NavObstacleSet obstacleSnapshot = SnapshotObstacles(_baseContext.Obstacles);
                int submitted = 0;
                while (submitted < maxTiles && _fifo.Count > 0)
                {
                    NavBakeTileCoord target = _fifo.Peek();
                    if (!_bakeRequests.TryAdd(new BakeRequest(target, CreateSingleTargetContext(target, obstacleSnapshot))))
                    {
                        break;
                    }

                    _fifo.Dequeue();
                    _queued.Remove(target);
                    _inFlightTargets.Add(target);
                    submitted++;
                }
            }

            return PublishCompletedBakes(maxTiles);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bakeRequests.CompleteAdding();
            _workerThread.Join(DisposeJoinTimeout);
            _bakeRequests.Dispose();
        }

        private RuntimeNavMeshRebuildBatch PublishCompletedBakes(int requestedBudget)
        {
            var published = new List<RuntimeNavMeshRebuildPublishedTile>();
            var failures = new List<NavBakeResultEntry>();
            int rebuiltTiles = 0;

            while (TryDequeueCompleted(out CompletedBake done))
            {
                _inFlightTargets.Remove(done.Target);
                if (done.Error != null)
                {
                    throw done.Error;
                }

                LastBatchElapsedMs = done.ElapsedMs;
                for (int i = 0; i < done.Result.Entries.Count; i++)
                {
                    NavBakeResultEntry entry = done.Result.Entries[i];
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

                rebuiltTiles++;
            }

            return new RuntimeNavMeshRebuildBatch(
                requestedBudget,
                rebuiltTiles,
                failures.Count,
                PendingTileCount,
                published,
                failures);
        }

        private void BakeWorkerLoop()
        {
            while (!_bakeRequests.IsCompleted)
            {
                if (!_bakeRequests.TryTake(out BakeRequest request, Timeout.Infinite))
                {
                    continue;
                }

                long startTimestamp = Stopwatch.GetTimestamp();
                CompletedBake done;
                try
                {
                    NavBakeResult result = _bakeService.Bake(request.Context);
                    done = new CompletedBake(request.Target, result, null, ElapsedMs(startTimestamp));
                }
                catch (Exception ex)
                {
                    done = new CompletedBake(request.Target, null, ex, ElapsedMs(startTimestamp));
                }

                lock (_completedGate)
                {
                    _completedBakes.Enqueue(done);
                }
            }
        }

        private bool TryDequeueCompleted(out CompletedBake done)
        {
            lock (_completedGate)
            {
                if (_completedBakes.Count == 0)
                {
                    done = null;
                    return false;
                }

                done = _completedBakes.Dequeue();
                return true;
            }
        }

        private NavBakeContext CreateSingleTargetContext(NavBakeTileCoord target, NavObstacleSet obstacles)
        {
            return new NavBakeContext
            {
                MapId = _baseContext.MapId,
                ModId = _baseContext.ModId,
                SourceUri = _baseContext.SourceUri,
                Terrain = _baseContext.Terrain,
                Obstacles = obstacles,
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

        private static NavObstacleSet SnapshotObstacles(NavObstacleSet source)
        {
            return new NavObstacleSet
            {
                Version = source.Version,
                Obstacles = new List<NavObstacle>(source.Obstacles),
            };
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
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

        private sealed class BakeRequest
        {
            public BakeRequest(NavBakeTileCoord target, NavBakeContext context)
            {
                Target = target;
                Context = context;
            }

            public NavBakeTileCoord Target { get; }

            public NavBakeContext Context { get; }
        }

        private sealed class CompletedBake
        {
            public CompletedBake(NavBakeTileCoord target, NavBakeResult result, Exception error, double elapsedMs)
            {
                Target = target;
                Result = result;
                Error = error;
                ElapsedMs = elapsedMs;
            }

            public NavBakeTileCoord Target { get; }

            public NavBakeResult Result { get; }

            public Exception Error { get; }

            public double ElapsedMs { get; }
        }
    }
}
