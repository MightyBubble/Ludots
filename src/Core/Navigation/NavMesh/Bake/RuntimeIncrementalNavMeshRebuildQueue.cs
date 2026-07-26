using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public enum RuntimeNavMeshRebuildStatus : byte
    {
        Idle = 0,
        Pending = 1,
        SealedInProgress = 2
    }

    public readonly struct RuntimeNavMeshRebuildPublishedTile
    {
        public RuntimeNavMeshRebuildPublishedTile(
            NavBakeTileCoord target,
            int layer,
            int profileIndex,
            string profileId,
            uint storeRevision,
            ulong generation)
        {
            Target = target;
            Layer = layer;
            ProfileIndex = profileIndex;
            ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            StoreRevision = storeRevision;
            Generation = generation;
        }

        public NavBakeTileCoord Target { get; }

        public int Layer { get; }

        public int ProfileIndex { get; }

        public string ProfileId { get; }

        public uint StoreRevision { get; }

        public ulong Generation { get; }
    }

    public readonly struct RuntimeNavMeshRebuildBatchStats
    {
        public RuntimeNavMeshRebuildBatchStats(
            int requestedTileBudget,
            int rebuiltTileCount,
            int failedEntryCount,
            int pendingTileCount,
            int sealedRemainingCount,
            bool committed,
            bool aborted,
            ulong generation,
            int publishedCount,
            long bakeTicks = 0L,
            long commitTicks = 0L,
            ulong generationChecksum = 0UL,
            int peakResidentTileCount = 0,
            int workerCount = 1,
            bool mixedGenerationDetected = false)
        {
            RequestedTileBudget = requestedTileBudget;
            RebuiltTileCount = rebuiltTileCount;
            FailedEntryCount = failedEntryCount;
            PendingTileCount = pendingTileCount;
            SealedRemainingCount = sealedRemainingCount;
            Committed = committed;
            Aborted = aborted;
            Generation = generation;
            PublishedCount = publishedCount;
            BakeTicks = bakeTicks;
            CommitTicks = commitTicks;
            GenerationChecksum = generationChecksum;
            PeakResidentTileCount = peakResidentTileCount;
            WorkerCount = workerCount;
            MixedGenerationDetected = mixedGenerationDetected;
        }

        public int RequestedTileBudget { get; }
        public int RebuiltTileCount { get; }
        public int FailedEntryCount { get; }
        public int PendingTileCount { get; }
        public int SealedRemainingCount { get; }
        public bool Committed { get; }
        public bool Aborted { get; }
        public ulong Generation { get; }
        public int PublishedCount { get; }

        /// <summary>
        /// Stopwatch ticks for sealed-generation bake work in this ProcessBudgetInto call,
        /// including sealed obstacle snapshot handling when a generation is sealed here.
        /// </summary>
        public long BakeTicks { get; }

        /// <summary>
        /// Stopwatch ticks spent committing a completed generation in this call
        /// (checksum, publish, bookkeeping/cleanup). 0 when not committing.
        /// </summary>
        public long CommitTicks { get; }

        /// <summary>
        /// Stable mix authenticating the complete committed resident generation
        /// (occupied tiles + index tombstones across stores). 0 when not committed.
        /// </summary>
        public ulong GenerationChecksum { get; }

        /// <summary>Peak resident tile count observed across stores at commit (0 when not committed).</summary>
        public int PeakResidentTileCount { get; }

        /// <summary>Runtime bake worker count for this queue (intentionally 1 unless a harness overrides).</summary>
        public int WorkerCount { get; }

        /// <summary>
        /// True when a production invariant observed divergent store generations at commit.
        /// Callers must record this on telemetry; it must not stay permanently zero by omission.
        /// </summary>
        public bool MixedGenerationDetected { get; }
    }

    public sealed class RuntimeNavMeshRebuildBatch
    {
        public RuntimeNavMeshRebuildBatch(
            int requestedTileBudget,
            int rebuiltTileCount,
            int failedEntryCount,
            int pendingTileCount,
            int sealedRemainingCount,
            bool committed,
            bool aborted,
            ulong generation,
            IReadOnlyList<RuntimeNavMeshRebuildPublishedTile> publishedTiles,
            IReadOnlyList<NavBakeResultEntry> failedEntries)
        {
            RequestedTileBudget = requestedTileBudget;
            RebuiltTileCount = rebuiltTileCount;
            FailedEntryCount = failedEntryCount;
            PendingTileCount = pendingTileCount;
            SealedRemainingCount = sealedRemainingCount;
            Committed = committed;
            Aborted = aborted;
            Generation = generation;
            PublishedTiles = publishedTiles ?? throw new ArgumentNullException(nameof(publishedTiles));
            FailedEntries = failedEntries ?? throw new ArgumentNullException(nameof(failedEntries));
        }

        public int RequestedTileBudget { get; }
        public int RebuiltTileCount { get; }
        public int FailedEntryCount { get; }
        public int PendingTileCount { get; }
        public int SealedRemainingCount { get; }
        public bool Committed { get; }
        public bool Aborted { get; }
        public ulong Generation { get; }
        public IReadOnlyList<RuntimeNavMeshRebuildPublishedTile> PublishedTiles { get; }
        public IReadOnlyList<NavBakeResultEntry> FailedEntries { get; }
    }

    public sealed class RuntimeIncrementalNavMeshRebuildQueue
    {
        private readonly NavBakeService _bakeService;
        private readonly NavBakeContext _frameContext;
        private readonly NavBakeTileCoord[] _frameTargets;
        private readonly NavQueryServiceRegistry _queryServices;
        private readonly NavMeshProfileRegistry _profiles;
        private readonly NavRuntimeIncrementalConfig _runtimeConfig;
        private readonly NavBakeTileCoord[] _dirtyRing;
        private readonly ulong[] _dirtyMembershipBits;
        private readonly NavTileOutputBank _outputBank;
        private readonly StagedEntry[] _stagedEntries;
        private readonly NavBakeResultEntry[] _stagedFailures;
        private readonly NavTile[] _commitFlatTiles;
        private readonly int[] _commitBatchOffsets;
        private readonly int[] _commitBatchCounts;
        private readonly NavTileStore[] _commitStores;
        private readonly StorePublishKey[] _commitKeys;
        private readonly uint[] _commitRevisions;
        private readonly RuntimeNavMeshRebuildPublishedTile[] _publishedScratch;
        private readonly NavBakeTileCoord[] _residentWindowCoords;
        private readonly NavBakeTileCoord[] _committedResidentWindowCoords;
        private readonly RuntimeNavObstacleSnapshot _liveRuntimeObstacles;
        private readonly RuntimeNavObstacleSnapshot _generationObstacles;
        private readonly int _originXcm;
        private readonly int _originZcm;
        private readonly int _tileWidthCm;
        private readonly int _tileHeightCm;
        private readonly int _tileCountX;
        private readonly int _tileCountY;
        private readonly int _membershipWordCount;
        private readonly uint _baseTileVersion;
        private NavBakeAlgorithmKind _committedAlgorithm;
        private NavBakeAlgorithmKind _requestedAlgorithm;
        private bool _hasRequestedAlgorithm;
        private bool _algorithmSwitchGenerationActive;
        private bool _residentWindowTransitionActive;
        private int _residentWindowCount;
        private int _committedResidentWindowCount;
        private int _dirtyHead;
        private int _dirtyTail;
        private int _dirtyCount;
        private int _stagedEntryCount;
        private int _stagedFailureCount;
        private int _lastPublishedCount;
        private ulong _lastPublishedGeneration;
        private int _sealedRemaining;
        private uint _activeBatchTileVersion;
        private uint _completedSealedBatchCount;
        private bool _activeBatchHasFailure;
        private int _lastDirtyVisitedCandidateCount;
        private int _droppedDirtyCommandCount;
        private int _capacityGrowthCount;
        private readonly string[] _profileRequireContexts;

        public RuntimeIncrementalNavMeshRebuildQueue(
            NavBakeService bakeService,
            NavBakeContext baseContext,
            NavQueryServiceRegistry queryServices,
            NavMeshProfileRegistry profiles)
        {
            _bakeService = bakeService ?? throw new ArgumentNullException(nameof(bakeService));
            if (baseContext == null) throw new ArgumentNullException(nameof(baseContext));
            _queryServices = queryServices ?? throw new ArgumentNullException(nameof(queryServices));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

            baseContext.Validate();
            if (baseContext.Mode != NavBakeMode.RuntimeIncremental)
            {
                throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue requires NavBakeContext.mode 'runtime-incremental'.");
            }

            _bakeService.EnsureSupports(baseContext);
            if (baseContext.InputKind != NavBakeInputKind.TriangleSurface)
            {
                throw new InvalidOperationException(
                    "RuntimeIncrementalNavMeshRebuildQueue requires TriangleSurface input " +
                    "(LogicTerrain must be cold-compiled once before queue construction).");
            }

            _runtimeConfig = baseContext.Config.RuntimeIncremental
                ?? throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental is required.");
            ValidateRuntimeCapacities(_runtimeConfig);
            _baseTileVersion = baseContext.TileVersion;

            _liveRuntimeObstacles = baseContext.Obstacles as RuntimeNavObstacleSnapshot
                ?? throw new InvalidOperationException(
                    "RuntimeIncrementalNavMeshRebuildQueue requires RuntimeNavObstacleSnapshot so every generation can pin immutable obstacle input.");
            _generationObstacles = _liveRuntimeObstacles.CreateCompatibleEmpty();

            ResolveActiveTileGrid(
                baseContext,
                out _originXcm,
                out _originZcm,
                out _tileWidthCm,
                out _tileHeightCm,
                out _tileCountX,
                out _tileCountY);

            int tileSlots = checked(_tileCountX * _tileCountY);
            _membershipWordCount = (tileSlots + 63) / 64;
            _dirtyMembershipBits = new ulong[Math.Max(1, _membershipWordCount)];
            _dirtyRing = new NavBakeTileCoord[_runtimeConfig.DirtyTileCapacity];
            _outputBank = new NavTileOutputBank(_runtimeConfig);
            _stagedEntries = new StagedEntry[_runtimeConfig.StagedEntryCapacity];
            _stagedFailures = new NavBakeResultEntry[_runtimeConfig.StagedEntryCapacity];
            _commitFlatTiles = new NavTile[_runtimeConfig.StagedEntryCapacity];
            _commitBatchOffsets = new int[_runtimeConfig.StoreGroupCapacity];
            _commitBatchCounts = new int[_runtimeConfig.StoreGroupCapacity];
            _commitStores = new NavTileStore[_runtimeConfig.StoreGroupCapacity];
            _commitKeys = new StorePublishKey[_runtimeConfig.StoreGroupCapacity];
            _commitRevisions = new uint[_runtimeConfig.StoreGroupCapacity];
            _publishedScratch = new RuntimeNavMeshRebuildPublishedTile[_runtimeConfig.PublishedTileCapacity];
            _residentWindowCoords = new NavBakeTileCoord[_runtimeConfig.ResidentTileCapacity];
            _committedResidentWindowCoords = new NavBakeTileCoord[_runtimeConfig.ResidentTileCapacity];
            _residentWindowCount = 0;
            _committedResidentWindowCount = 0;
            _residentWindowTransitionActive = false;

            IReadOnlyList<NavMeshAgentProfileConfig> bakeProfiles = baseContext.Config.Profiles;
            _profileRequireContexts = new string[bakeProfiles.Count];
            for (int pi = 0; pi < bakeProfiles.Count; pi++)
            {
                _profileRequireContexts[pi] = $"{NavMeshConfigPaths.BakeConfigPath}.profiles[{pi}]";
            }

            _committedAlgorithm = baseContext.Algorithm;
            _requestedAlgorithm = baseContext.Algorithm;
            _hasRequestedAlgorithm = false;
            _algorithmSwitchGenerationActive = false;

            _frameTargets = new NavBakeTileCoord[1];
            _frameContext = new NavBakeContext
            {
                MapId = baseContext.MapId,
                ModId = baseContext.ModId,
                SourceUri = baseContext.SourceUri,
                TriangleSurface = baseContext.RequireTriangleSurface(),
                Obstacles = _generationObstacles,
                Config = baseContext.Config,
                AgentProfiles = baseContext.AgentProfiles,
                Targets = _frameTargets,
                BuildConfig = baseContext.BuildConfig,
                TileVersion = baseContext.TileVersion,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = _committedAlgorithm,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };

            ValidateConfiguredStores();
            _droppedDirtyCommandCount = 0;
            _capacityGrowthCount = 0;
        }

        public int PendingTileCount => _dirtyCount;

        public int DirtyTileCapacity => _dirtyRing.Length;

        public int FreeDirtyTileCapacity => _dirtyRing.Length - _dirtyCount;

        public int SealedRemainingCount => _sealedRemaining;

        public int LastDirtyVisitedCandidateCount => _lastDirtyVisitedCandidateCount;

        /// <summary>
        /// Soft-drop counter. Production enqueue fails fast on capacity exhaustion, so this stays 0.
        /// </summary>
        public int DroppedDirtyCommandCount => _droppedDirtyCommandCount;

        /// <summary>
        /// Hot-path capacity growth counter. Production uses fixed capacities and fails instead of growing.
        /// </summary>
        public int CapacityGrowthCount => _capacityGrowthCount;

        /// <summary>
        /// Fixed staged output bank owned by this queue. Peak resident evidence must include these channels.
        /// </summary>
        public NavTileOutputBank OutputBank => _outputBank;

        /// <summary>
        /// Fixed queue working-set bytes excluding NavTileStore banks and algorithm scratch:
        /// staged output bank + checksum scratch + dirty/membership/staging/publish arrays.
        /// </summary>
        public long PreallocatedFixedWorkingSetBytes
        {
            get
            {
                long total = _outputBank.PreallocatedChannelPayloadBytes;
                total = checked(total + ((long)_dirtyRing.Length * (sizeof(int) * 2L)));
                total = checked(total + ((long)_dirtyMembershipBits.Length * sizeof(ulong)));
                total = checked(total + ((long)_stagedEntries.Length * (sizeof(int) * 4L)));
                total = checked(total + ((long)_commitFlatTiles.Length * IntPtr.Size));
                total = checked(total + ((long)_commitBatchOffsets.Length * sizeof(int)));
                total = checked(total + ((long)_commitBatchCounts.Length * sizeof(int)));
                total = checked(total + ((long)_commitStores.Length * IntPtr.Size));
                total = checked(total + ((long)_commitRevisions.Length * sizeof(uint)));
                total = checked(total + ((long)_publishedScratch.Length * (sizeof(int) * 4L + sizeof(uint) + sizeof(ulong))));
                total = checked(total + ((long)_residentWindowCoords.Length * (sizeof(int) * 2L)));
                total = checked(total + ((long)_committedResidentWindowCoords.Length * (sizeof(int) * 2L)));
                return total;
            }
        }

        /// <summary>Runtime ProcessBudgetInto worker count (intentionally 1).</summary>
        public int WorkerCount => Math.Max(1, _frameContext.Execution.MaxDegreeOfParallelism);

        /// <summary>
        /// Committed/visible algorithm currently exposed by the published store generation.
        /// Does not change until an algorithm-switch resident set commits atomically.
        /// </summary>
        public NavBakeAlgorithmKind CurrentAlgorithm => _committedAlgorithm;

        /// <summary>
        /// True while a validated SwitchAlgorithm request is outstanding (in flight or after abort).
        /// </summary>
        public bool HasRequestedAlgorithm => _hasRequestedAlgorithm;

        /// <summary>
        /// Requested algorithm target. Equals <see cref="CurrentAlgorithm"/> when no request is outstanding.
        /// </summary>
        public NavBakeAlgorithmKind RequestedAlgorithm =>
            _hasRequestedAlgorithm ? _requestedAlgorithm : _committedAlgorithm;

        public RuntimeNavMeshRebuildStatus Status
        {
            get
            {
                if (_sealedRemaining > 0)
                {
                    return RuntimeNavMeshRebuildStatus.SealedInProgress;
                }

                if (_dirtyCount > 0)
                {
                    return RuntimeNavMeshRebuildStatus.Pending;
                }

                return RuntimeNavMeshRebuildStatus.Idle;
            }
        }

        public int ResidentWindowCount => _residentWindowCount;

        /// <summary>
        /// Last committed/visible resident window size. During an in-flight transition this stays
        /// on the previously committed set while <see cref="ResidentWindowCount"/> tracks the request.
        /// </summary>
        public int CommittedResidentWindowCount => _committedResidentWindowCount;

        public bool HasResidentWindowTransition => _residentWindowTransitionActive;

        /// <summary>
        /// Copy the tracked resident window coords (sorted by chunk Y then X). Returns count.
        /// During a transition this is the requested set being baked; use
        /// <see cref="CopyCommittedResidentWindow"/> for the last published set.
        /// </summary>
        public int CopyResidentWindow(Span<NavBakeTileCoord> destination)
        {
            if (destination.Length < _residentWindowCount)
            {
                throw new ArgumentException(
                    $"CopyResidentWindow destination length {destination.Length} is below resident window count {_residentWindowCount}.",
                    nameof(destination));
            }

            for (int i = 0; i < _residentWindowCount; i++)
            {
                destination[i] = _residentWindowCoords[i];
            }

            return _residentWindowCount;
        }

        /// <summary>
        /// Copy the last committed/visible resident window. Empty before the first successful commit.
        /// </summary>
        public int CopyCommittedResidentWindow(Span<NavBakeTileCoord> destination)
        {
            if (destination.Length < _committedResidentWindowCount)
            {
                throw new ArgumentException(
                    $"CopyCommittedResidentWindow destination length {destination.Length} is below committed resident window count {_committedResidentWindowCount}.",
                    nameof(destination));
            }

            for (int i = 0; i < _committedResidentWindowCount; i++)
            {
                destination[i] = _committedResidentWindowCoords[i];
            }

            return _committedResidentWindowCount;
        }

        /// <summary>
        /// Copy pending dirty-ring tiles into <paramref name="destination"/> in deterministic
        /// ChunkY then ChunkX order. Returns count. Allocation-free after construction.
        /// </summary>
        public int CopyPendingTiles(Span<NavBakeTileCoord> destination)
        {
            if (destination.Length < _dirtyCount)
            {
                throw new ArgumentException(
                    $"CopyPendingTiles destination length {destination.Length} is below pending tile count {_dirtyCount}.",
                    nameof(destination));
            }

            for (int i = 0; i < _dirtyCount; i++)
            {
                destination[i] = _dirtyRing[(_dirtyHead + i) % _dirtyRing.Length];
            }

            for (int i = 1; i < _dirtyCount; i++)
            {
                NavBakeTileCoord value = destination[i];
                int j = i - 1;
                while (j >= 0 && ComparePendingTileCoord(destination[j], value) > 0)
                {
                    destination[j + 1] = destination[j];
                    j--;
                }

                destination[j + 1] = value;
            }

            return _dirtyCount;
        }

        /// <summary>
        /// Copies tiles already baked into the sealed generation but not yet atomically committed.
        /// Results are filtered to one layer/profile store and sorted by ChunkY then ChunkX.
        /// </summary>
        public int CopyRebuildingTiles(
            int layer,
            int profileIndex,
            Span<NavBakeTileCoord> destination)
        {
            int count = 0;
            for (int i = 0; i < _stagedEntryCount; i++)
            {
                StagedEntry entry = _stagedEntries[i];
                if (entry.Layer != layer || entry.ProfileIndex != profileIndex)
                {
                    continue;
                }

                if (count >= destination.Length)
                {
                    throw new ArgumentException(
                        $"CopyRebuildingTiles destination length {destination.Length} is below rebuilding tile count {count + 1}.",
                        nameof(destination));
                }

                destination[count++] = entry.Target;
            }

            SortTileCoords(destination.Slice(0, count));
            return count;
        }

        /// <summary>
        /// Copies the tiles from the most recent successful atomic generation commit for one store.
        /// A failed/aborted generation never replaces this evidence.
        /// </summary>
        public int CopyLastCommittedTiles(
            int layer,
            int profileIndex,
            Span<NavBakeTileCoord> destination,
            out ulong generation)
        {
            generation = _lastPublishedGeneration;
            int count = 0;
            for (int i = 0; i < _lastPublishedCount; i++)
            {
                RuntimeNavMeshRebuildPublishedTile published = _publishedScratch[i];
                if (published.Layer != layer || published.ProfileIndex != profileIndex)
                {
                    continue;
                }

                if (count >= destination.Length)
                {
                    throw new ArgumentException(
                        $"CopyLastCommittedTiles destination length {destination.Length} is below committed tile count {count + 1}.",
                        nameof(destination));
                }

                destination[count++] = published.Target;
            }

            SortTileCoords(destination.Slice(0, count));
            return count;
        }

        private static void SortTileCoords(Span<NavBakeTileCoord> coords)
        {
            for (int i = 1; i < coords.Length; i++)
            {
                NavBakeTileCoord value = coords[i];
                int j = i - 1;
                while (j >= 0 && ComparePendingTileCoord(coords[j], value) > 0)
                {
                    coords[j + 1] = coords[j];
                    j--;
                }

                coords[j + 1] = value;
            }
        }

        private static int ComparePendingTileCoord(NavBakeTileCoord left, NavBakeTileCoord right)
        {
            int y = left.ChunkY.CompareTo(right.ChunkY);
            return y != 0 ? y : left.ChunkX.CompareTo(right.ChunkX);
        }

        /// <summary>
        /// Cold/hot path resident-window transition: validate first, discard pending/staged work,
        /// enqueue the exact new window, and on the next successful sealed commit publish+evict
        /// under one shared generation via <see cref="NavTileStore.ReplaceResidentWindowsAtomically"/>.
        /// </summary>
        public void RequestResidentWindowTransition(ReadOnlySpan<NavBakeTileCoord> newWindow)
        {
            if (newWindow.Length == 0)
            {
                throw new ArgumentException("Resident-window transition requires a non-empty tile span.", nameof(newWindow));
            }

            if (newWindow.Length > _runtimeConfig.ResidentTileCapacity)
            {
                throw new InvalidOperationException(
                    $"Resident-window size {newWindow.Length} exceeds NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_runtimeConfig.ResidentTileCapacity}).");
            }

            if (newWindow.Length > _dirtyRing.Length)
            {
                throw new InvalidOperationException(
                    $"Resident-window size {newWindow.Length} exceeds NavMeshBakeConfig.runtimeIncremental.dirtyTileCapacity ({_dirtyRing.Length}).");
            }

            for (int i = 0; i < newWindow.Length; i++)
            {
                RequireTargetInRange(newWindow[i], nameof(newWindow));
            }

            // Deduplicate into scratch then fail on duplicates.
            for (int i = 0; i < newWindow.Length; i++)
            {
                _residentWindowCoords[i] = newWindow[i];
            }

            for (int i = 1; i < newWindow.Length; i++)
            {
                NavBakeTileCoord value = _residentWindowCoords[i];
                int j = i - 1;
                while (j >= 0 && CompareTileCoord(_residentWindowCoords[j], value) > 0)
                {
                    _residentWindowCoords[j + 1] = _residentWindowCoords[j];
                    j--;
                }

                _residentWindowCoords[j + 1] = value;
            }

            for (int i = 1; i < newWindow.Length; i++)
            {
                if (_residentWindowCoords[i].ChunkX == _residentWindowCoords[i - 1].ChunkX &&
                    _residentWindowCoords[i].ChunkY == _residentWindowCoords[i - 1].ChunkY)
                {
                    throw new InvalidOperationException(
                        $"Resident-window transition contains duplicate tile ({_residentWindowCoords[i].ChunkX},{_residentWindowCoords[i].ChunkY}).");
                }
            }

            DiscardPendingAndStagedWork();
            _residentWindowCount = newWindow.Length;
            _residentWindowTransitionActive = true;

            for (int i = 0; i < _residentWindowCount; i++)
            {
                EnqueueDirtyTile(_residentWindowCoords[i]);
            }
        }

        /// <summary>
        /// Cold-path algorithm switch: validate first, discard pending/staged work for the previous algorithm,
        /// keep the last committed generation visible until the supplied resident set commits atomically,
        /// then publish one new generation under the selected registered adapter.
        /// </summary>
        public void SwitchAlgorithm(NavBakeAlgorithmKind algorithm, ReadOnlySpan<NavBakeTileCoord> residentTiles)
        {
            if (residentTiles.Length == 0)
            {
                throw new ArgumentException("Algorithm switch requires a non-empty resident tile span.", nameof(residentTiles));
            }

            if (_residentWindowTransitionActive)
            {
                throw new InvalidOperationException(
                    "Algorithm switch is rejected while a resident-window transition is in flight. " +
                    "Wait for the committed window or cancel the transition before switching algorithms.");
            }

            if (!_bakeService.HasAdapter(algorithm))
            {
                NavBakeAlgorithmKind[] registered = _bakeService.RegisteredKinds;
                var names = new string[registered.Length];
                for (int i = 0; i < registered.Length; i++)
                {
                    names[i] = NavBakeNames.FormatAlgorithm(registered[i]);
                }

                throw new InvalidOperationException(
                    $"NavBake algorithm '{NavBakeNames.FormatAlgorithm(algorithm)}' is not registered. " +
                    $"Registered kinds: [{string.Join(", ", names)}].");
            }

            var probe = new NavBakeContext
            {
                MapId = _frameContext.MapId,
                ModId = _frameContext.ModId,
                SourceUri = _frameContext.SourceUri,
                TriangleSurface = _frameContext.RequireTriangleSurface(),
                Obstacles = _generationObstacles,
                Config = _frameContext.Config,
                AgentProfiles = _frameContext.AgentProfiles,
                Targets = _frameTargets,
                BuildConfig = _frameContext.BuildConfig,
                TileVersion = _frameContext.TileVersion,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = algorithm,
                Execution = _frameContext.Execution
            };
            _bakeService.EnsureSupports(probe);

            DiscardPendingAndStagedWork();
            _requestedAlgorithm = algorithm;
            _hasRequestedAlgorithm = true;
            _algorithmSwitchGenerationActive = true;
            // Bake uses the requested adapter while CurrentAlgorithm remains the committed/visible kind.
            _frameContext.Algorithm = algorithm;

            // Track exact resident set used for the switch (no eviction on algorithm switch).
            if (residentTiles.Length > _residentWindowCoords.Length)
            {
                throw new InvalidOperationException(
                    $"Algorithm switch resident set size {residentTiles.Length} exceeds residentTileCapacity {_residentWindowCoords.Length}.");
            }

            for (int i = 0; i < residentTiles.Length; i++)
            {
                RequireTargetInRange(residentTiles[i], nameof(residentTiles));
                _residentWindowCoords[i] = residentTiles[i];
            }

            _residentWindowCount = residentTiles.Length;
            _residentWindowTransitionActive = false;
            CaptureCommittedResidentWindowFromTracked();

            for (int i = 0; i < residentTiles.Length; i++)
            {
                EnqueueDirtyTile(residentTiles[i]);
            }
        }

        private void DiscardPendingAndStagedWork()
        {
            // Preserve committed store generation; only drop in-flight old-algorithm work.
            Array.Clear(_dirtyMembershipBits, 0, _dirtyMembershipBits.Length);
            _dirtyHead = 0;
            _dirtyTail = 0;
            _dirtyCount = 0;
            ClearActiveBatchState(advanceCompletedCount: false);
        }

        public bool EnqueueDirtyTile(NavBakeTileCoord target)
        {
            RequireTargetInRange(target, nameof(target));

            // Once residency is explicit, only tracked tiles may enter a dirty generation.
            // A later window transition always rebuilds its full target set from the latest
            // obstacle snapshot, so publishing an out-of-window partial tile is both unnecessary
            // and would violate the resident-set contract.
            if (_residentWindowCount > 0 && !IsInTrackedResidentWindow(target))
            {
                return false;
            }

            int bitIndex = checked(target.ChunkY * _tileCountX + target.ChunkX);
            if (IsMembershipSet(bitIndex))
            {
                return false;
            }

            if (_dirtyCount >= _dirtyRing.Length)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.dirtyTileCapacity ({_dirtyRing.Length}) exhausted; required {_dirtyCount + 1}.");
            }

            SetMembership(bitIndex);
            _dirtyRing[_dirtyTail] = target;
            _dirtyTail = (_dirtyTail + 1) % _dirtyRing.Length;
            _dirtyCount++;
            return true;
        }

        /// <summary>
        /// Published TriangleSurface bake SSOT currently owned by this queue's frame context.
        /// </summary>
        public NavTriangleSurfaceTileIndex CurrentTriangleSurface => _frameContext.RequireTriangleSurface();

        /// <summary>
        /// Replaces the TriangleSurface bake SSOT used by subsequent dirty tile rebuilds.
        /// Fails fast while a generation is sealed/baking so callers cannot observe mixed surface generations.
        /// Grid origin/size/counts must match the queue's resolved tile space.
        /// Mutates the existing frame context in place (zero managed allocation after construction).
        /// </summary>
        public void ReplaceTriangleSurface(NavTriangleSurfaceTileIndex surface)
        {
            if (surface == null)
            {
                throw new ArgumentNullException(nameof(surface));
            }

            if (_sealedRemaining > 0)
            {
                throw new InvalidOperationException(
                    "RuntimeIncrementalNavMeshRebuildQueue.ReplaceTriangleSurface refused: a generation is sealed/baking.");
            }

            NavTriangleSurfaceTileGrid grid = surface.Grid;
            if (grid.OriginXcm != _originXcm ||
                grid.OriginZcm != _originZcm ||
                grid.TileWidthCm != _tileWidthCm ||
                grid.TileHeightCm != _tileHeightCm ||
                grid.TileCountX != _tileCountX ||
                grid.TileCountZ != _tileCountY)
            {
                throw new InvalidOperationException(
                    "ReplaceTriangleSurface requires an identical tile grid " +
                    $"(expected origin=({_originXcm},{_originZcm}) tileSize=({_tileWidthCm},{_tileHeightCm}) " +
                    $"tileCount=({_tileCountX},{_tileCountY}); " +
                    $"got origin=({grid.OriginXcm},{grid.OriginZcm}) tileSize=({grid.TileWidthCm},{grid.TileHeightCm}) " +
                    $"tileCount=({grid.TileCountX},{grid.TileCountZ})).");
            }

            _frameContext.TriangleSurface = surface;
        }

        /// <summary>
        /// True when the world XZ point lies inside a committed resident tile (half-open tile coverage).
        /// Open-world construction edits must use this gate; empty committed residency fails closed.
        /// </summary>
        public bool IsWorldPointInCommittedResidentWindow(int worldXcm, int worldZcm)
        {
            if (_committedResidentWindowCount <= 0)
            {
                return false;
            }

            int tileX = MathUtil.FloorDiv(checked(worldXcm - _originXcm), _tileWidthCm);
            int tileZ = MathUtil.FloorDiv(checked(worldZcm - _originZcm), _tileHeightCm);
            if ((uint)tileX >= (uint)_tileCountX || (uint)tileZ >= (uint)_tileCountY)
            {
                return false;
            }

            return IsCommittedResidentTile(new NavBakeTileCoord(tileX, tileZ));
        }

        /// <summary>
        /// True when the tile is present in the last committed/visible resident window.
        /// Empty committed residency fails closed.
        /// </summary>
        public bool IsCommittedResidentTile(NavBakeTileCoord target)
        {
            if (_committedResidentWindowCount <= 0)
            {
                return false;
            }

            if ((uint)target.ChunkX >= (uint)_tileCountX || (uint)target.ChunkY >= (uint)_tileCountY)
            {
                return false;
            }

            int lo = 0;
            int hi = _committedResidentWindowCount - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = CompareTileCoord(_committedResidentWindowCoords[mid], target);
                if (cmp == 0)
                {
                    return true;
                }

                if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }

        public int EnqueueDirtyAabb(WorldAabbCm dirtyAabb, bool includeNeighbors)
        {
            if (dirtyAabb.Width <= 0 || dirtyAabb.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dirtyAabb), "Dirty AABB width and height must be > 0.");
            }

            int minChunkX = MathUtil.FloorDiv(checked(dirtyAabb.Left - _originXcm), _tileWidthCm);
            int minChunkY = MathUtil.FloorDiv(checked(dirtyAabb.Top - _originZcm), _tileHeightCm);
            int maxChunkX = MathUtil.FloorDiv(checked(dirtyAabb.Right - 1 - _originXcm), _tileWidthCm);
            int maxChunkY = MathUtil.FloorDiv(checked(dirtyAabb.Bottom - 1 - _originZcm), _tileHeightCm);

            _lastDirtyVisitedCandidateCount = 0;

            if (maxChunkX < 0 ||
                maxChunkY < 0 ||
                minChunkX >= _tileCountX ||
                minChunkY >= _tileCountY)
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

            minChunkX = MathUtil.Clamp(minChunkX, 0, _tileCountX - 1);
            maxChunkX = MathUtil.Clamp(maxChunkX, 0, _tileCountX - 1);
            minChunkY = MathUtil.Clamp(minChunkY, 0, _tileCountY - 1);
            maxChunkY = MathUtil.Clamp(maxChunkY, 0, _tileCountY - 1);

            if (minChunkX > maxChunkX || minChunkY > maxChunkY)
            {
                return 0;
            }

            int added = 0;
            for (int cy = minChunkY; cy <= maxChunkY; cy++)
            {
                for (int cx = minChunkX; cx <= maxChunkX; cx++)
                {
                    _lastDirtyVisitedCandidateCount++;
                    if (EnqueueDirtyTile(new NavBakeTileCoord(cx, cy)))
                    {
                        added++;
                    }
                }
            }

            return added;
        }

        /// <summary>
        /// Zero-managed-allocation runtime path after construction/warmup.
        /// Caller spans and configured publish capacity are validated before any store mutation.
        /// Bake and commit phases are timed separately; dirty collection is owned by the caller system.
        /// </summary>
        public RuntimeNavMeshRebuildBatchStats ProcessBudgetInto(
            int maxTiles,
            Span<RuntimeNavMeshRebuildPublishedTile> publishedOut,
            Span<NavBakeResultEntry> failuresOut)
        {
            if (maxTiles <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTiles), "Runtime navmesh rebuild budget must be > 0.");
            }

            ValidateCallerOutputSpans(publishedOut, failuresOut);

            int workerCount = WorkerCount;
            long bakeTicks = 0L;
            int processedTiles = 0;
            long bakeBefore = Stopwatch.GetTimestamp();
            if (_sealedRemaining == 0 && _dirtyCount > 0)
            {
                // Seal includes immutable obstacle snapshot pin — part of measured bake phase.
                BeginSealedGeneration();
            }

            while (processedTiles < maxTiles && _sealedRemaining > 0)
            {
                NavBakeTileCoord target = DequeueDirtyTile();
                _sealedRemaining--;
                BakeTargetIntoStaged(target);
                processedTiles++;
            }

            bakeTicks = Stopwatch.GetTimestamp() - bakeBefore;

            if (_sealedRemaining > 0)
            {
                return new RuntimeNavMeshRebuildBatchStats(
                    maxTiles,
                    processedTiles,
                    failedEntryCount: 0,
                    _dirtyCount,
                    _sealedRemaining,
                    committed: false,
                    aborted: false,
                    generation: 0UL,
                    publishedCount: 0,
                    bakeTicks,
                    commitTicks: 0L,
                    generationChecksum: 0UL,
                    peakResidentTileCount: 0,
                    workerCount);
            }

            if (processedTiles == 0 && _stagedEntryCount == 0 && _stagedFailureCount == 0)
            {
                return new RuntimeNavMeshRebuildBatchStats(
                    maxTiles,
                    rebuiltTileCount: 0,
                    failedEntryCount: 0,
                    _dirtyCount,
                    sealedRemainingCount: 0,
                    committed: false,
                    aborted: false,
                    generation: 0UL,
                    publishedCount: 0,
                    bakeTicks,
                    commitTicks: 0L,
                    generationChecksum: 0UL,
                    peakResidentTileCount: 0,
                    workerCount);
            }

            if (_activeBatchHasFailure || _stagedFailureCount > 0)
            {
                if (failuresOut.Length < _stagedFailureCount)
                {
                    throw new InvalidOperationException(
                        $"Caller failuresOut length {failuresOut.Length} is below staged failure count {_stagedFailureCount}; " +
                        $"required {_stagedFailureCount} (NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity={_runtimeConfig.StagedEntryCapacity}).");
                }

                for (int i = 0; i < _stagedFailureCount; i++)
                {
                    failuresOut[i] = _stagedFailures[i];
                }

                int failed = _stagedFailureCount;
                AbortActiveBatchKeepingCommittedAlgorithm();
                return new RuntimeNavMeshRebuildBatchStats(
                    maxTiles,
                    processedTiles,
                    failed,
                    _dirtyCount,
                    sealedRemainingCount: 0,
                    committed: false,
                    aborted: true,
                    generation: 0UL,
                    publishedCount: 0,
                    bakeTicks,
                    commitTicks: 0L,
                    generationChecksum: 0UL,
                    peakResidentTileCount: 0,
                    workerCount);
            }

            long commitBefore = Stopwatch.GetTimestamp();
            int publishedCount = CommitStagedGeneration(publishedOut);
            ulong generation = publishedCount > 0 ? publishedOut[0].Generation : 0UL;
            _lastPublishedCount = publishedCount;
            _lastPublishedGeneration = generation;
            bool mixedGeneration = DetectMixedGeneration(generation);
            ulong generationChecksum = ComputeCommittedResidentGenerationChecksum();
            int peakResident = ComputePeakResidentTileCount();
            CommitAlgorithmSwitchIfActive();
            ClearActiveBatchState(advanceCompletedCount: true);
            long commitTicks = Stopwatch.GetTimestamp() - commitBefore;
            return new RuntimeNavMeshRebuildBatchStats(
                maxTiles,
                processedTiles,
                failedEntryCount: 0,
                _dirtyCount,
                sealedRemainingCount: 0,
                committed: true,
                aborted: false,
                generation,
                publishedCount,
                bakeTicks,
                commitTicks,
                generationChecksum,
                peakResident,
                workerCount,
                mixedGeneration);
        }

        /// <summary>
        /// Allocating convenience wrapper for legacy contract tests. Runtime 0GC callers must use ProcessBudgetInto.
        /// </summary>
        public RuntimeNavMeshRebuildBatch ProcessBudget(int maxTiles)
        {
            var published = new RuntimeNavMeshRebuildPublishedTile[_runtimeConfig.PublishedTileCapacity];
            var failures = new NavBakeResultEntry[_runtimeConfig.StagedEntryCapacity];
            RuntimeNavMeshRebuildBatchStats stats = ProcessBudgetInto(maxTiles, published.AsSpan(), failures.AsSpan());

            var publishedList = new RuntimeNavMeshRebuildPublishedTile[stats.PublishedCount];
            for (int i = 0; i < stats.PublishedCount; i++)
            {
                publishedList[i] = published[i];
            }

            var failureList = new NavBakeResultEntry[stats.FailedEntryCount];
            for (int i = 0; i < stats.FailedEntryCount; i++)
            {
                failureList[i] = failures[i];
            }

            return new RuntimeNavMeshRebuildBatch(
                stats.RequestedTileBudget,
                stats.RebuiltTileCount,
                stats.FailedEntryCount,
                stats.PendingTileCount,
                stats.SealedRemainingCount,
                stats.Committed,
                stats.Aborted,
                stats.Generation,
                publishedList,
                failureList);
        }

        private void BakeTargetIntoStaged(NavBakeTileCoord target)
        {
            _frameTargets[0] = target;
            _frameContext.TileVersion = _activeBatchTileVersion;

            IReadOnlyList<NavLayerConfig> layers = _frameContext.Config.Layers;
            IReadOnlyList<NavMeshAgentProfileConfig> profiles = _frameContext.Config.Profiles;
            for (int li = 0; li < layers.Count; li++)
            {
                NavLayerConfig layer = layers[li];
                for (int pi = 0; pi < profiles.Count; pi++)
                {
                    NavMeshAgentProfileConfig navProfile = profiles[pi];
                    AgentProfileConfig agentProfile = _frameContext.AgentProfiles.Require(
                        navProfile.Id,
                        _profileRequireContexts[pi]);

                    if (_stagedEntryCount >= _stagedEntries.Length)
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity ({_stagedEntries.Length}) exhausted; required {_stagedEntryCount + 1}.");
                    }

                    NavTile destination = _outputBank.RentSlot();
                    bool success = _bakeService.BakeInto(
                        _frameContext,
                        target,
                        layer,
                        navProfile,
                        agentProfile,
                        destination,
                        _outputBank.ChecksumScratch,
                        out NavBakeArtifact artifact);

                    if (!success)
                    {
                        if (_stagedFailureCount >= _stagedFailures.Length)
                        {
                            throw new InvalidOperationException(
                                $"NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity ({_stagedFailures.Length}) exhausted for failures; required {_stagedFailureCount + 1}.");
                        }

                        _stagedFailures[_stagedFailureCount++] = new NavBakeResultEntry(
                            target,
                            navProfile.Id,
                            layer.Layer,
                            success: false,
                            tile: null!,
                            detourTileBytes: Array.Empty<byte>(),
                            artifact);
                        _activeBatchHasFailure = true;
                        continue;
                    }

                    if (!_profiles.TryGetIndex(navProfile.Id, out int profileIndex))
                    {
                        throw new InvalidOperationException(
                            $"Runtime navmesh rebuild produced profile '{navProfile.Id}' that is not registered.");
                    }

                    if (!_queryServices.TryGetStore(layer.Layer, profileIndex, out _))
                    {
                        throw new InvalidOperationException(
                            $"Runtime navmesh rebuild cannot publish layer {layer.Layer}, profile '{navProfile.Id}' because no NavTileStore is registered.");
                    }

                    _stagedEntries[_stagedEntryCount++] = new StagedEntry(
                        target,
                        layer.Layer,
                        profileIndex,
                        navProfile.Id,
                        destination);
                }
            }
        }

        private void BeginSealedGeneration()
        {
            _sealedRemaining = _dirtyCount;
            _stagedEntryCount = 0;
            _stagedFailureCount = 0;
            _activeBatchHasFailure = false;
            _outputBank.Reset();
            uint nextOffset = checked(_completedSealedBatchCount + 1u);
            _activeBatchTileVersion = checked(_baseTileVersion + nextOffset);
            _liveRuntimeObstacles.CopyTo(_generationObstacles);
        }

        private void ClearActiveBatchState(bool advanceCompletedCount)
        {
            _sealedRemaining = 0;
            _stagedEntryCount = 0;
            _stagedFailureCount = 0;
            _activeBatchHasFailure = false;
            _outputBank.Reset();
            if (advanceCompletedCount)
            {
                if (_completedSealedBatchCount == uint.MaxValue)
                {
                    throw new InvalidOperationException("RuntimeIncrementalNavMeshRebuildQueue sealed batch counter overflow.");
                }

                _completedSealedBatchCount++;
            }
        }

        private void CommitAlgorithmSwitchIfActive()
        {
            if (!_algorithmSwitchGenerationActive)
            {
                return;
            }

            if (!_hasRequestedAlgorithm)
            {
                throw new InvalidOperationException(
                    "Algorithm-switch generation committed without an outstanding RequestedAlgorithm.");
            }

            if (_frameContext.Algorithm != _requestedAlgorithm)
            {
                throw new InvalidOperationException(
                    $"Algorithm-switch commit expected bake algorithm '{NavBakeNames.FormatAlgorithm(_requestedAlgorithm)}' " +
                    $"but frame context has '{NavBakeNames.FormatAlgorithm(_frameContext.Algorithm)}'.");
            }

            _committedAlgorithm = _requestedAlgorithm;
            _hasRequestedAlgorithm = false;
            _algorithmSwitchGenerationActive = false;
        }

        private void AbortActiveBatchKeepingCommittedAlgorithm()
        {
            if (_algorithmSwitchGenerationActive)
            {
                // Keep RequestedAlgorithm explicit after failure; restore bake path to committed algorithm.
                _frameContext.Algorithm = _committedAlgorithm;
                _algorithmSwitchGenerationActive = false;
            }

            // Window transition failed before commit: restore the last committed resident set.
            if (_residentWindowTransitionActive)
            {
                _residentWindowTransitionActive = false;
                RestoreTrackedResidentWindowFromCommitted();
            }

            ClearActiveBatchState(advanceCompletedCount: true);
        }

        private void CaptureCommittedResidentWindowFromTracked()
        {
            for (int i = 0; i < _residentWindowCount; i++)
            {
                _committedResidentWindowCoords[i] = _residentWindowCoords[i];
            }

            _committedResidentWindowCount = _residentWindowCount;
        }

        private void RestoreTrackedResidentWindowFromCommitted()
        {
            for (int i = 0; i < _committedResidentWindowCount; i++)
            {
                _residentWindowCoords[i] = _committedResidentWindowCoords[i];
            }

            _residentWindowCount = _committedResidentWindowCount;
        }

        private int CountUniqueStagedTargets()
        {
            int unique = 0;
            for (int i = 0; i < _stagedEntryCount; i++)
            {
                NavBakeTileCoord target = _stagedEntries[i].Target;
                bool seen = false;
                for (int j = 0; j < i; j++)
                {
                    if (_stagedEntries[j].Target.ChunkX == target.ChunkX &&
                        _stagedEntries[j].Target.ChunkY == target.ChunkY)
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    unique++;
                }
            }

            return unique;
        }

        private void AdoptResidentWindowFromStaged()
        {
            int count = 0;
            for (int i = 0; i < _stagedEntryCount; i++)
            {
                NavBakeTileCoord target = _stagedEntries[i].Target;
                bool seen = false;
                for (int j = 0; j < count; j++)
                {
                    if (_residentWindowCoords[j].ChunkX == target.ChunkX &&
                        _residentWindowCoords[j].ChunkY == target.ChunkY)
                    {
                        seen = true;
                        break;
                    }
                }

                if (seen)
                {
                    continue;
                }

                if (count >= _residentWindowCoords.Length)
                {
                    throw new InvalidOperationException(
                        $"Adopted resident window exceeds residentTileCapacity {_residentWindowCoords.Length}.");
                }

                _residentWindowCoords[count++] = target;
            }

            for (int i = 1; i < count; i++)
            {
                NavBakeTileCoord value = _residentWindowCoords[i];
                int j = i - 1;
                while (j >= 0 && CompareTileCoord(_residentWindowCoords[j], value) > 0)
                {
                    _residentWindowCoords[j + 1] = _residentWindowCoords[j];
                    j--;
                }

                _residentWindowCoords[j + 1] = value;
            }

            _residentWindowCount = count;
        }

        private bool IsInTrackedResidentWindow(NavBakeTileCoord target)
        {
            int lo = 0;
            int hi = _residentWindowCount - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = CompareTileCoord(_residentWindowCoords[mid], target);
                if (cmp == 0)
                {
                    return true;
                }

                if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }

        private static int CompareTileCoord(NavBakeTileCoord left, NavBakeTileCoord right)
        {
            int cmp = left.ChunkY.CompareTo(right.ChunkY);
            return cmp != 0 ? cmp : left.ChunkX.CompareTo(right.ChunkX);
        }

        private ulong ComputeStagedGenerationChecksum()
        {
            // FNV-1a 64 over stable (chunkY, chunkX, layer, profileIndex, tile.Checksum) keys.
            // Retained for staged diagnostics; committed evidence uses ComputeCommittedResidentGenerationChecksum.
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < _stagedEntryCount; i++)
            {
                int best = i;
                for (int j = i + 1; j < _stagedEntryCount; j++)
                {
                    if (CompareStagedEntryKey(_stagedEntries[j], _stagedEntries[best]) < 0)
                    {
                        best = j;
                    }
                }

                if (best != i)
                {
                    StagedEntry swap = _stagedEntries[i];
                    _stagedEntries[i] = _stagedEntries[best];
                    _stagedEntries[best] = swap;
                }

                StagedEntry entry = _stagedEntries[i];
                hash ^= (ulong)(uint)entry.Target.ChunkY;
                hash *= 1099511628211UL;
                hash ^= (ulong)(uint)entry.Target.ChunkX;
                hash *= 1099511628211UL;
                hash ^= (ulong)(uint)entry.Layer;
                hash *= 1099511628211UL;
                hash ^= (ulong)(uint)entry.ProfileIndex;
                hash *= 1099511628211UL;
                hash ^= entry.Tile.Checksum;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        /// <summary>
        /// Authenticates the complete committed resident generation across every registered store,
        /// including unchanged occupied tiles and index tombstones.
        /// </summary>
        private ulong ComputeCommittedResidentGenerationChecksum()
        {
            ulong hash = 1469598103934665603UL;
            IReadOnlyList<NavLayerConfig> layers = _frameContext.Config.Layers;
            IReadOnlyList<NavMeshAgentProfileConfig> profiles = _frameContext.Config.Profiles;
            for (int li = 0; li < layers.Count; li++)
            {
                int layer = layers[li].Layer;
                for (int pi = 0; pi < profiles.Count; pi++)
                {
                    if (!_queryServices.TryGetStore(layer, pi, out NavTileStore store))
                    {
                        throw new InvalidOperationException(
                            $"Committed generation checksum requires NavTileStore for layer {layer}, profileIndex {pi}.");
                    }

                    hash ^= (ulong)(uint)layer;
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(uint)pi;
                    hash *= 1099511628211UL;
                    hash ^= store.ComputeCommittedGenerationChecksum();
                    hash *= 1099511628211UL;
                }
            }

            return hash;
        }

        private bool DetectMixedGeneration(ulong committedGeneration)
        {
            if (committedGeneration == 0UL)
            {
                return false;
            }

            bool mixed = false;
            IReadOnlyList<NavLayerConfig> layers = _frameContext.Config.Layers;
            IReadOnlyList<NavMeshAgentProfileConfig> profiles = _frameContext.Config.Profiles;
            for (int li = 0; li < layers.Count; li++)
            {
                int layer = layers[li].Layer;
                for (int pi = 0; pi < profiles.Count; pi++)
                {
                    if (!_queryServices.TryGetStore(layer, pi, out NavTileStore store) || store == null)
                    {
                        throw new InvalidOperationException(
                            $"Mixed-generation detection requires NavTileStore for layer {layer}, profileIndex {pi}.");
                    }

                    if (store.Generation != committedGeneration)
                    {
                        mixed = true;
                    }
                }
            }

            return mixed;
        }

        private static int CompareStagedEntryKey(StagedEntry left, StagedEntry right)
        {
            int cmp = left.Target.ChunkY.CompareTo(right.Target.ChunkY);
            if (cmp != 0) return cmp;
            cmp = left.Target.ChunkX.CompareTo(right.Target.ChunkX);
            if (cmp != 0) return cmp;
            cmp = left.Layer.CompareTo(right.Layer);
            if (cmp != 0) return cmp;
            return left.ProfileIndex.CompareTo(right.ProfileIndex);
        }

        private int ComputePeakResidentTileCount()
        {
            int peak = 0;
            for (int i = 0; i < _commitStores.Length; i++)
            {
                NavTileStore? store = _commitStores[i];
                if (store == null)
                {
                    continue;
                }

                if (store.ResidentCount > peak)
                {
                    peak = store.ResidentCount;
                }
            }

            if (_committedResidentWindowCount > peak)
            {
                peak = _committedResidentWindowCount;
            }

            return peak;
        }

        private int CommitStagedGeneration(Span<RuntimeNavMeshRebuildPublishedTile> publishedOut)
        {
            if (_stagedEntryCount > _runtimeConfig.PublishedTileCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.publishedTileCapacity ({_runtimeConfig.PublishedTileCapacity}) exhausted; required {_stagedEntryCount}.");
            }

            if (publishedOut.Length < _stagedEntryCount)
            {
                throw new InvalidOperationException(
                    $"Caller publishedOut length {publishedOut.Length} is below staged publish count {_stagedEntryCount}; " +
                    $"required {_stagedEntryCount} (NavMeshBakeConfig.runtimeIncremental.publishedTileCapacity={_runtimeConfig.PublishedTileCapacity}).");
            }

            int groupCount = 0;
            for (int i = 0; i < _stagedEntryCount; i++)
            {
                StagedEntry entry = _stagedEntries[i];
                int groupIndex = -1;
                for (int g = 0; g < groupCount; g++)
                {
                    if (_commitKeys[g].Equals(new StorePublishKey(entry.Layer, entry.ProfileIndex, entry.ProfileId)))
                    {
                        groupIndex = g;
                        break;
                    }
                }

                if (groupIndex < 0)
                {
                    if (groupCount >= _commitKeys.Length)
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.runtimeIncremental.storeGroupCapacity ({_commitKeys.Length}) exhausted; required {groupCount + 1}.");
                    }

                    groupIndex = groupCount++;
                    _commitKeys[groupIndex] = new StorePublishKey(entry.Layer, entry.ProfileIndex, entry.ProfileId);
                    _commitBatchCounts[groupIndex] = 0;
                }

                _commitBatchCounts[groupIndex]++;
            }

            // Stable sort groups by key.
            for (int i = 1; i < groupCount; i++)
            {
                StorePublishKey key = _commitKeys[i];
                int count = _commitBatchCounts[i];
                int j = i - 1;
                while (j >= 0 && CompareStorePublishKey(_commitKeys[j], key) > 0)
                {
                    _commitKeys[j + 1] = _commitKeys[j];
                    _commitBatchCounts[j + 1] = _commitBatchCounts[j];
                    j--;
                }

                _commitKeys[j + 1] = key;
                _commitBatchCounts[j + 1] = count;
            }

            int flatCursor = 0;
            for (int g = 0; g < groupCount; g++)
            {
                _commitBatchOffsets[g] = flatCursor;
                StorePublishKey key = _commitKeys[g];
                if (!_queryServices.TryGetStore(key.Layer, key.ProfileIndex, out NavTileStore store))
                {
                    throw new InvalidOperationException(
                        $"Runtime navmesh rebuild cannot commit layer {key.Layer}, profile '{key.ProfileId}' because no NavTileStore is registered.");
                }

                _commitStores[g] = store;

                // Collect tiles for this group then sort by tile id.
                int groupStart = flatCursor;
                for (int i = 0; i < _stagedEntryCount; i++)
                {
                    StagedEntry entry = _stagedEntries[i];
                    if (entry.Layer != key.Layer || entry.ProfileIndex != key.ProfileIndex)
                    {
                        continue;
                    }

                    _commitFlatTiles[flatCursor++] = entry.Tile;
                }

                int groupCountTiles = flatCursor - groupStart;
                for (int i = 1; i < groupCountTiles; i++)
                {
                    NavTile value = _commitFlatTiles[groupStart + i];
                    int j = i - 1;
                    while (j >= 0 && CompareTileId(_commitFlatTiles[groupStart + j], value) > 0)
                    {
                        _commitFlatTiles[groupStart + j + 1] = _commitFlatTiles[groupStart + j];
                        j--;
                    }

                    _commitFlatTiles[groupStart + j + 1] = value;
                }

                _commitBatchCounts[g] = groupCountTiles;
            }

            ulong generation;
            if (_residentWindowTransitionActive)
            {
                // Full resident set must be present in this sealed batch; fail before mutation otherwise.
                int uniqueTargets = CountUniqueStagedTargets();
                if (uniqueTargets != _residentWindowCount)
                {
                    throw new InvalidOperationException(
                        $"Resident-window transition sealed batch unique tile count {uniqueTargets} " +
                        $"does not match tracked window count {_residentWindowCount}.");
                }

                NavTileStore.ReplaceResidentWindowsAtomically(
                    _commitStores.AsSpan(0, groupCount),
                    _commitFlatTiles.AsSpan(0, flatCursor),
                    _commitBatchOffsets.AsSpan(0, groupCount),
                    _commitBatchCounts.AsSpan(0, groupCount),
                    _commitRevisions.AsSpan(0, groupCount),
                    out generation);
                _residentWindowTransitionActive = false;
                CaptureCommittedResidentWindowFromTracked();
            }
            else
            {
                NavTileStore.ReplaceGenerationBatchesAtomically(
                    _commitStores.AsSpan(0, groupCount),
                    _commitFlatTiles.AsSpan(0, flatCursor),
                    _commitBatchOffsets.AsSpan(0, groupCount),
                    _commitBatchCounts.AsSpan(0, groupCount),
                    _commitRevisions.AsSpan(0, groupCount),
                    out generation);

                // Bootstrap / partial dirty: adopt published unique targets as tracked window when empty.
                if (_residentWindowCount == 0)
                {
                    AdoptResidentWindowFromStaged();
                }

                if (_committedResidentWindowCount == 0 && _residentWindowCount > 0)
                {
                    CaptureCommittedResidentWindowFromTracked();
                }
            }

            int published = 0;
            for (int g = 0; g < groupCount; g++)
            {
                StorePublishKey key = _commitKeys[g];
                uint revision = _commitRevisions[g];
                int offset = _commitBatchOffsets[g];
                int count = _commitBatchCounts[g];
                for (int t = 0; t < count; t++)
                {
                    NavTile tile = _commitFlatTiles[offset + t];
                    var receipt = new RuntimeNavMeshRebuildPublishedTile(
                        new NavBakeTileCoord(tile.TileId.ChunkX, tile.TileId.ChunkY),
                        key.Layer,
                        key.ProfileIndex,
                        key.ProfileId,
                        revision,
                        generation);
                    _publishedScratch[published] = receipt;
                    publishedOut[published] = receipt;
                    published++;
                }
            }

            return published;
        }

        private void ValidateCallerOutputSpans(
            Span<RuntimeNavMeshRebuildPublishedTile> publishedOut,
            Span<NavBakeResultEntry> failuresOut)
        {
            if (publishedOut.Length < _runtimeConfig.PublishedTileCapacity)
            {
                throw new InvalidOperationException(
                    $"Caller publishedOut length {publishedOut.Length} is below NavMeshBakeConfig.runtimeIncremental.publishedTileCapacity ({_runtimeConfig.PublishedTileCapacity}); required {_runtimeConfig.PublishedTileCapacity}.");
            }

            if (failuresOut.Length < _runtimeConfig.StagedEntryCapacity)
            {
                throw new InvalidOperationException(
                    $"Caller failuresOut length {failuresOut.Length} is below NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity ({_runtimeConfig.StagedEntryCapacity}); required {_runtimeConfig.StagedEntryCapacity}.");
            }
        }

        private NavBakeTileCoord DequeueDirtyTile()
        {
            if (_dirtyCount == 0)
            {
                throw new InvalidOperationException("Dirty ring underflow.");
            }

            NavBakeTileCoord target = _dirtyRing[_dirtyHead];
            _dirtyHead = (_dirtyHead + 1) % _dirtyRing.Length;
            _dirtyCount--;
            int bitIndex = checked(target.ChunkY * _tileCountX + target.ChunkX);
            ClearMembership(bitIndex);
            return target;
        }

        private bool IsMembershipSet(int bitIndex)
        {
            int word = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 63);
            return (_dirtyMembershipBits[word] & mask) != 0UL;
        }

        private void SetMembership(int bitIndex)
        {
            int word = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 63);
            _dirtyMembershipBits[word] |= mask;
        }

        private void ClearMembership(int bitIndex)
        {
            int word = bitIndex >> 6;
            ulong mask = 1UL << (bitIndex & 63);
            _dirtyMembershipBits[word] &= ~mask;
        }

        private void ValidateConfiguredStores()
        {
            var seen = new NavTileStore[_runtimeConfig.StoreGroupCapacity];
            int storeCount = 0;
            IReadOnlyList<NavLayerConfig> layers = _frameContext.Config.Layers;
            for (int li = 0; li < layers.Count; li++)
            {
                NavLayerConfig layer = layers[li];
                for (int pi = 0; pi < _profiles.Count; pi++)
                {
                    string profileId = _profiles.GetId(pi);
                    if (!_queryServices.TryGetStore(layer.Layer, pi, out NavTileStore store))
                    {
                        throw new InvalidOperationException(
                            $"RuntimeIncrementalNavMeshRebuildQueue requires a NavTileStore for layer {layer.Layer} ('{layer.Id}'), profile '{profileId}'.");
                    }

                    if (store.ResidentTileCapacity < _runtimeConfig.ResidentTileCapacity)
                    {
                        throw new InvalidOperationException(
                            $"NavTileStore residentTileCapacity {store.ResidentTileCapacity} is below configured " +
                            $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity {_runtimeConfig.ResidentTileCapacity}.");
                    }

                    if (store.OutputVertexCapacity != _runtimeConfig.OutputVertexCapacity)
                    {
                        throw new InvalidOperationException(
                            $"NavTileStore outputVertexCapacity {store.OutputVertexCapacity} does not match configured " +
                            $"NavMeshBakeConfig.runtimeIncremental.outputVertexCapacity {_runtimeConfig.OutputVertexCapacity}.");
                    }

                    if (store.OutputTriangleCapacity != _runtimeConfig.OutputTriangleCapacity)
                    {
                        throw new InvalidOperationException(
                            $"NavTileStore outputTriangleCapacity {store.OutputTriangleCapacity} does not match configured " +
                            $"NavMeshBakeConfig.runtimeIncremental.outputTriangleCapacity {_runtimeConfig.OutputTriangleCapacity}.");
                    }

                    if (store.OutputPortalCapacity != _runtimeConfig.OutputPortalCapacity)
                    {
                        throw new InvalidOperationException(
                            $"NavTileStore outputPortalCapacity {store.OutputPortalCapacity} does not match configured " +
                            $"NavMeshBakeConfig.runtimeIncremental.outputPortalCapacity {_runtimeConfig.OutputPortalCapacity}.");
                    }

                    for (int prior = 0; prior < storeCount; prior++)
                    {
                        if (ReferenceEquals(seen[prior], store))
                        {
                            throw new InvalidOperationException(
                                $"RuntimeIncrementalNavMeshRebuildQueue rejects duplicate NavTileStore instance for layer {layer.Layer}, profile '{profileId}'.");
                        }
                    }

                    if (storeCount >= seen.Length)
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.runtimeIncremental.storeGroupCapacity ({seen.Length}) exhausted while validating stores; required {storeCount + 1}.");
                    }

                    seen[storeCount++] = store;
                }
            }
        }

        private void RequireTargetInRange(NavBakeTileCoord target, string argumentName)
        {
            if (target.ChunkX < 0 ||
                target.ChunkY < 0 ||
                target.ChunkX >= _tileCountX ||
                target.ChunkY >= _tileCountY)
            {
                throw new ArgumentOutOfRangeException(argumentName, $"Dirty nav tile is out of range: {target}.");
            }
        }

        private static void ValidateRuntimeCapacities(NavRuntimeIncrementalConfig config)
        {
            RequirePositive(config.DirtyTileCapacity, "NavMeshBakeConfig.runtimeIncremental.dirtyTileCapacity");
            RequirePositive(config.StagedEntryCapacity, "NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity");
            RequirePositive(config.PublishedTileCapacity, "NavMeshBakeConfig.runtimeIncremental.publishedTileCapacity");
            RequirePositive(config.StoreGroupCapacity, "NavMeshBakeConfig.runtimeIncremental.storeGroupCapacity");
            RequirePositive(config.ResidentTileCapacity, "NavMeshBakeConfig.runtimeIncremental.residentTileCapacity");
            RequirePositive(config.OutputVertexCapacity, "NavMeshBakeConfig.runtimeIncremental.outputVertexCapacity");
            RequirePositive(config.OutputTriangleCapacity, "NavMeshBakeConfig.runtimeIncremental.outputTriangleCapacity");
            RequirePositive(config.OutputPortalCapacity, "NavMeshBakeConfig.runtimeIncremental.outputPortalCapacity");
        }

        private static void RequirePositive(int value, string owner)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException($"{owner} must be > 0.");
            }
        }

        private static int CompareStorePublishKey(StorePublishKey a, StorePublishKey b)
        {
            int layer = a.Layer.CompareTo(b.Layer);
            if (layer != 0) return layer;
            int profile = a.ProfileIndex.CompareTo(b.ProfileIndex);
            if (profile != 0) return profile;
            return string.CompareOrdinal(a.ProfileId, b.ProfileId);
        }

        private static int CompareTileId(NavTile a, NavTile b)
        {
            int y = a.TileId.ChunkY.CompareTo(b.TileId.ChunkY);
            if (y != 0) return y;
            int x = a.TileId.ChunkX.CompareTo(b.TileId.ChunkX);
            if (x != 0) return x;
            return a.TileId.Layer.CompareTo(b.TileId.Layer);
        }

        private static void ResolveActiveTileGrid(
            NavBakeContext context,
            out int originXcm,
            out int originZcm,
            out int tileWidthCm,
            out int tileHeightCm,
            out int tileCountX,
            out int tileCountY)
        {
            switch (context.InputKind)
            {
                case NavBakeInputKind.LogicTerrain:
                {
                    LogicTerrainField terrain = context.RequireTerrain();
                    originXcm = 0;
                    originZcm = 0;
                    tileWidthCm = checked(terrain.ChunkSizeCells * terrain.HorizontalStepCm);
                    tileHeightCm = checked(terrain.ChunkSizeCells * terrain.VerticalStepCm);
                    if (tileWidthCm <= 0 || tileHeightCm <= 0)
                    {
                        throw new InvalidOperationException("LogicTerrainField chunk world size must be > 0.");
                    }

                    tileCountX = terrain.WidthChunks;
                    tileCountY = terrain.HeightChunks;
                    return;
                }
                case NavBakeInputKind.TriangleSurface:
                {
                    NavTriangleSurfaceTileGrid grid = context.RequireTriangleSurface().Grid;
                    originXcm = grid.OriginXcm;
                    originZcm = grid.OriginZcm;
                    tileWidthCm = grid.TileWidthCm;
                    tileHeightCm = grid.TileHeightCm;
                    tileCountX = grid.TileCountX;
                    tileCountY = grid.TileCountZ;
                    return;
                }
                default:
                    throw new InvalidOperationException($"Unknown NavBakeInputKind '{context.InputKind}'.");
            }
        }

        private readonly struct StagedEntry
        {
            public StagedEntry(NavBakeTileCoord target, int layer, int profileIndex, string profileId, NavTile tile)
            {
                Target = target;
                Layer = layer;
                ProfileIndex = profileIndex;
                ProfileId = profileId;
                Tile = tile;
            }

            public NavBakeTileCoord Target { get; }
            public int Layer { get; }
            public int ProfileIndex { get; }
            public string ProfileId { get; }
            public NavTile Tile { get; }
        }

        private readonly struct StorePublishKey : IEquatable<StorePublishKey>
        {
            public StorePublishKey(int layer, int profileIndex, string profileId)
            {
                Layer = layer;
                ProfileIndex = profileIndex;
                ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            }

            public int Layer { get; }
            public int ProfileIndex { get; }
            public string ProfileId { get; }

            public bool Equals(StorePublishKey other) =>
                Layer == other.Layer &&
                ProfileIndex == other.ProfileIndex &&
                string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is StorePublishKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Layer, ProfileIndex, ProfileId);
        }
    }
}
