using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavTileStore
    {
        private const byte IndexEmpty = 0;
        private const byte IndexOccupied = 1;
        private const byte IndexTombstone = 2;

        private static long _nextLockOrder;

        private readonly Func<NavTileId, Stream> _openStream;
        private readonly object _gate = new object();
        private readonly NavTileId[] _indexKeys;
        private readonly byte[] _indexState;
        private readonly int[] _indexSlots;
        private readonly int _indexMask;
        private readonly NavTile[] _residentTiles;
        private readonly byte[] _residentOccupied;
        private readonly NavTileId[] _validateScratch;
        private readonly int _residentTileCapacity;
        private readonly int _outputVertexCapacity;
        private readonly int _outputTriangleCapacity;
        private readonly int _outputPortalCapacity;
        private readonly long _lockOrder;
        private int _residentCount;
        private uint _revision;
        private ulong _generation;

        public NavTileStore(Func<NavTileId, Stream> openStream)
            : this(
                openStream,
                residentTileCapacity: 256,
                outputVertexCapacity: 256,
                outputTriangleCapacity: 512,
                outputPortalCapacity: 64)
        {
        }

        public NavTileStore(Func<NavTileId, Stream> openStream, NavRuntimeIncrementalConfig runtimeIncremental)
            : this(
                openStream,
                runtimeIncremental?.ResidentTileCapacity
                    ?? throw new ArgumentNullException(nameof(runtimeIncremental)),
                runtimeIncremental.OutputVertexCapacity,
                runtimeIncremental.OutputTriangleCapacity,
                runtimeIncremental.OutputPortalCapacity)
        {
        }

        public NavTileStore(
            Func<NavTileId, Stream> openStream,
            int residentTileCapacity,
            int outputVertexCapacity,
            int outputTriangleCapacity,
            int outputPortalCapacity)
        {
            _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
            if (residentTileCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(residentTileCapacity),
                    "NavMeshBakeConfig.runtimeIncremental.residentTileCapacity must be > 0.");
            }

            if (outputVertexCapacity <= 0 || outputTriangleCapacity <= 0 || outputPortalCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "NavMeshBakeConfig.runtimeIncremental output bank capacities must be > 0.");
            }

            _residentTileCapacity = residentTileCapacity;
            _outputVertexCapacity = outputVertexCapacity;
            _outputTriangleCapacity = outputTriangleCapacity;
            _outputPortalCapacity = outputPortalCapacity;

            int indexCapacity = NextPowerOfTwo(checked(residentTileCapacity * 2));
            _indexMask = indexCapacity - 1;
            _indexKeys = new NavTileId[indexCapacity];
            _indexState = new byte[indexCapacity];
            _indexSlots = new int[indexCapacity];
            _residentTiles = new NavTile[residentTileCapacity];
            _residentOccupied = new byte[residentTileCapacity];
            _validateScratch = new NavTileId[residentTileCapacity];
            for (int i = 0; i < residentTileCapacity; i++)
            {
                _residentTiles[i] = NavTile.CreateBanked(
                    outputVertexCapacity,
                    outputTriangleCapacity,
                    outputPortalCapacity);
            }

            _lockOrder = Interlocked.Increment(ref _nextLockOrder);
            if (_lockOrder <= 0)
            {
                throw new InvalidOperationException("NavTileStore lock-order id overflow.");
            }
        }

        public int ResidentTileCapacity => _residentTileCapacity;

        public int OutputVertexCapacity => _outputVertexCapacity;

        public int OutputTriangleCapacity => _outputTriangleCapacity;

        public int OutputPortalCapacity => _outputPortalCapacity;

        /// <summary>
        /// Exact byte size of all preallocated resident tile channel payloads at fixed bank capacity.
        /// This is independent of the current live resident count.
        /// </summary>
        public long PreallocatedResidentChannelPayloadBytes
            => checked((long)_residentTileCapacity * NavTile.ComputeBankedChannelPayloadBytes(
                _outputVertexCapacity,
                _outputTriangleCapacity,
                _outputPortalCapacity));

        /// <summary>
        /// Authenticates the complete committed resident generation: occupied tile checksums plus
        /// index tombstones, in stable index-slot order. Empty slots are skipped.
        /// </summary>
        public ulong ComputeCommittedGenerationChecksum()
        {
            lock (_gate)
            {
                ulong hash = 1469598103934665603UL;
                hash ^= _generation;
                hash *= 1099511628211UL;
                hash ^= (ulong)(uint)_residentCount;
                hash *= 1099511628211UL;
                for (int i = 0; i < _indexState.Length; i++)
                {
                    byte state = _indexState[i];
                    if (state == IndexEmpty)
                    {
                        continue;
                    }

                    NavTileId id = _indexKeys[i];
                    hash ^= (ulong)(uint)id.ChunkY;
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(uint)id.ChunkX;
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(uint)id.Layer;
                    hash *= 1099511628211UL;
                    hash ^= state;
                    hash *= 1099511628211UL;
                    if (state == IndexOccupied)
                    {
                        hash ^= _residentTiles[_indexSlots[i]].Checksum;
                        hash *= 1099511628211UL;
                    }
                }

                return hash;
            }
        }

        /// <summary>
        /// Live resident tile channel bytes at the current or supplied resident count.
        /// Prefer <see cref="PreallocatedResidentChannelPayloadBytes"/> for fixed bank capacity telemetry.
        /// </summary>
        [Obsolete("Use PreallocatedResidentChannelPayloadBytes for fixed bank capacity; live resident count is not preallocated memory.")]
        public long EstimateResidentChannelBytes()
            => EstimateResidentChannelBytes(_residentCount);

        [Obsolete("Use PreallocatedResidentChannelPayloadBytes for fixed bank capacity; live resident count is not preallocated memory.")]
        public long EstimateResidentChannelBytes(int residentTileCount)
        {
            if (residentTileCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(residentTileCount));
            }

            if (residentTileCount > _residentTileCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(residentTileCount),
                    $"Resident tile count {residentTileCount} exceeds residentTileCapacity {_residentTileCapacity}.");
            }

            long perTile = NavTile.ComputeBankedChannelPayloadBytes(
                _outputVertexCapacity,
                _outputTriangleCapacity,
                _outputPortalCapacity);
            return checked(perTile * residentTileCount);
        }

        public uint Revision
        {
            get
            {
                lock (_gate)
                {
                    return _revision;
                }
            }
        }

        public ulong Generation
        {
            get
            {
                lock (_gate)
                {
                    return _generation;
                }
            }
        }

        public bool TryGet(NavTileId id, out NavTile tile)
        {
            lock (_gate)
            {
                if (TryFindIndexSlotUnlocked(id, out int indexSlot, out _))
                {
                    tile = _residentTiles[_indexSlots[indexSlot]];
                    return true;
                }

                tile = null!;
                return false;
            }
        }

        public NavTile[] SnapshotLoadedTiles()
        {
            lock (_gate)
            {
                var tiles = new NavTile[_residentCount];
                int cursor = 0;
                for (int i = 0; i < _residentTileCapacity; i++)
                {
                    if (_residentOccupied[i] == 0)
                    {
                        continue;
                    }

                    tiles[cursor++] = _residentTiles[i];
                }

                return tiles;
            }
        }

        public int ResidentCount
        {
            get
            {
                lock (_gate)
                {
                    return _residentCount;
                }
            }
        }

        /// <summary>
        /// Copy occupied resident tile ids into <paramref name="destination"/> (sorted ascending).
        /// Returns the resident count. Capacity exhaustion fails before writing.
        /// </summary>
        public int CopyResidentTileIds(Span<NavTileId> destination)
        {
            lock (_gate)
            {
                if (destination.Length < _residentCount)
                {
                    throw new ArgumentException(
                        $"CopyResidentTileIds destination length {destination.Length} is below resident count {_residentCount}.",
                        nameof(destination));
                }

                int cursor = 0;
                for (int i = 0; i < _residentTileCapacity; i++)
                {
                    if (_residentOccupied[i] == 0)
                    {
                        continue;
                    }

                    destination[cursor++] = _residentTiles[i].TileId;
                }

                for (int i = 1; i < cursor; i++)
                {
                    NavTileId value = destination[i];
                    int j = i - 1;
                    while (j >= 0 && CompareTileId(destination[j], value) > 0)
                    {
                        destination[j + 1] = destination[j];
                        j--;
                    }

                    destination[j + 1] = value;
                }

                return cursor;
            }
        }

        /// <summary>
        /// Copies occupied resident tile references in deterministic tile order under one store lock.
        /// The returned revision and generation describe the same locked snapshot.
        /// </summary>
        public int CopyResidentTiles(
            Span<NavTile> destination,
            out uint revision,
            out ulong generation)
        {
            lock (_gate)
            {
                if (destination.Length < _residentCount)
                {
                    throw new ArgumentException(
                        $"CopyResidentTiles destination length {destination.Length} is below resident count {_residentCount}.",
                        nameof(destination));
                }

                int cursor = 0;
                for (int i = 0; i < _residentTileCapacity; i++)
                {
                    if (_residentOccupied[i] != 0)
                    {
                        destination[cursor++] = _residentTiles[i];
                    }
                }

                for (int i = 1; i < cursor; i++)
                {
                    NavTile value = destination[i];
                    int j = i - 1;
                    while (j >= 0 && CompareTileId(destination[j].TileId, value.TileId) > 0)
                    {
                        destination[j + 1] = destination[j];
                        j--;
                    }

                    destination[j + 1] = value;
                }

                revision = _revision;
                generation = _generation;
                return cursor;
            }
        }

        public bool TryRunStableRead<T>(Func<T> read, out T result, int maxAttempts = 2)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ulong generationBefore = Generation;
                T candidate = read();
                ulong generationAfter = Generation;
                if (generationBefore == generationAfter)
                {
                    result = candidate;
                    return true;
                }
            }

            result = default;
            return false;
        }

        public NavTile GetOrLoad(NavTileId id)
        {
            lock (_gate)
            {
                if (TryFindIndexSlotUnlocked(id, out int indexSlot, out _))
                {
                    return _residentTiles[_indexSlots[indexSlot]];
                }
            }

            using var s = _openStream(id);
            NavTile tile = NavTileBinary.Read(s);
            lock (_gate)
            {
                if (TryFindIndexSlotUnlocked(id, out int indexSlot, out _))
                {
                    return _residentTiles[_indexSlots[indexSlot]];
                }

                return PublishCopyUnlocked(tile);
            }
        }

        public NavTile Reload(NavTileId id)
        {
            using var s = _openStream(id);
            var tile = NavTileBinary.Read(s);
            lock (_gate)
            {
                EnsureCanAdvanceGenerationUnlocked();
                ValidateIncomingGeometryCapacityUnlocked(tile);
                NavTile published = PublishCopyUnlocked(tile);
                AdvanceGeneration();
                AdvanceRevision();
                return published;
            }
        }

        public uint Replace(NavTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            lock (_gate)
            {
                EnsureCanAdvanceGenerationUnlocked();
                ValidateIncomingGeometryCapacityUnlocked(tile);
                PublishCopyUnlocked(tile);
                AdvanceGeneration();
                return AdvanceRevision();
            }
        }

        public uint ReplaceGenerationBatch(ulong generation, IReadOnlyList<NavTile> tiles)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));

            lock (_gate)
            {
                return ApplyGenerationBatchUnlocked(generation, tiles);
            }
        }

        public void ValidateGenerationBatch(ulong generation, IReadOnlyList<NavTile> tiles)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            lock (_gate)
            {
                ValidateGenerationBatchUnlocked(generation, tiles);
            }
        }

        /// <summary>
        /// Acquire every unique store lock in process-wide store order, derive one shared generation,
        /// validate all batches, then mutate all stores before releasing any lock.
        /// Caller supplies fixed revision output storage; validation does not allocate.
        /// </summary>
        internal static void ReplaceGenerationBatchesAtomically(
            ReadOnlySpan<NavTileStore> stores,
            ReadOnlySpan<NavTile> flatTiles,
            ReadOnlySpan<int> batchOffsets,
            ReadOnlySpan<int> batchCounts,
            Span<uint> revisionsOut,
            out ulong committedGeneration)
        {
            int n = stores.Length;
            if (n == 0)
            {
                throw new InvalidOperationException("Atomic generation commit requires at least one NavTileStore.");
            }

            if (batchOffsets.Length != n || batchCounts.Length != n)
            {
                throw new InvalidOperationException(
                    "Atomic generation commit batchOffsets/batchCounts length must match store count.");
            }

            if (revisionsOut.Length < n)
            {
                throw new ArgumentException(
                    "Atomic generation commit revisionsOut length must be >= store count.",
                    nameof(revisionsOut));
            }

            if (n > 64)
            {
                throw new InvalidOperationException(
                    "Atomic generation commit store count exceeds stack lock-order limit 64; raise requires redesign, check storeGroupCapacity.");
            }

            for (int i = 0; i < n; i++)
            {
                if (stores[i] == null)
                {
                    throw new InvalidOperationException($"Atomic generation commit store[{i}] is null.");
                }

                if (batchCounts[i] < 0)
                {
                    throw new InvalidOperationException($"Atomic generation commit batchCounts[{i}] must be >= 0.");
                }

                for (int j = 0; j < i; j++)
                {
                    if (ReferenceEquals(stores[i], stores[j]))
                    {
                        throw new InvalidOperationException(
                            $"Atomic generation commit store[{i}] duplicates store[{j}]; unique stores are required.");
                    }
                }
            }

            Span<int> lockOrder = stackalloc int[64];
            for (int i = 0; i < n; i++) lockOrder[i] = i;
            for (int i = 1; i < n; i++)
            {
                int value = lockOrder[i];
                long order = stores[value]._lockOrder;
                int j = i - 1;
                while (j >= 0 && stores[lockOrder[j]]._lockOrder > order)
                {
                    lockOrder[j + 1] = lockOrder[j];
                    j--;
                }

                lockOrder[j + 1] = value;
            }

            int acquiredLockCount = 0;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    Monitor.Enter(stores[lockOrder[i]]._gate);
                    acquiredLockCount++;
                }

                ulong generation = 0UL;
                for (int i = 0; i < n; i++)
                {
                    ulong storeGeneration = stores[i]._generation;
                    if (storeGeneration >= generation)
                    {
                        generation = checked(storeGeneration + 1UL);
                    }
                }

                if (generation == 0UL)
                {
                    throw new InvalidOperationException("Atomic generation commit failed to derive a non-zero generation.");
                }

                for (int i = 0; i < n; i++)
                {
                    int offset = batchOffsets[i];
                    int count = batchCounts[i];
                    stores[i].ValidateGenerationBatchUnlocked(generation, flatTiles.Slice(offset, count));
                }

                for (int i = 0; i < n; i++)
                {
                    int offset = batchOffsets[i];
                    int count = batchCounts[i];
                    stores[i].MutateGenerationBatchUnlocked(generation, flatTiles.Slice(offset, count));
                    revisionsOut[i] = stores[i].AdvanceRevision();
                }

                committedGeneration = generation;
            }
            finally
            {
                for (int i = acquiredLockCount - 1; i >= 0; i--)
                {
                    Monitor.Exit(stores[lockOrder[i]]._gate);
                }
            }
        }

        /// <summary>
        /// Atomically publish a full new resident window per store and evict every resident tile
        /// that is not in that window, under one shared generation. Capacity exhaustion fails
        /// before mutation. Partial dirty commits must continue using
        /// <see cref="ReplaceGenerationBatchesAtomically"/>.
        /// </summary>
        internal static void ReplaceResidentWindowsAtomically(
            ReadOnlySpan<NavTileStore> stores,
            ReadOnlySpan<NavTile> flatTiles,
            ReadOnlySpan<int> batchOffsets,
            ReadOnlySpan<int> batchCounts,
            Span<uint> revisionsOut,
            out ulong committedGeneration)
        {
            int n = stores.Length;
            if (n == 0)
            {
                throw new InvalidOperationException("Atomic resident-window commit requires at least one NavTileStore.");
            }

            if (batchOffsets.Length != n || batchCounts.Length != n)
            {
                throw new InvalidOperationException(
                    "Atomic resident-window commit batchOffsets/batchCounts length must match store count.");
            }

            if (revisionsOut.Length < n)
            {
                throw new ArgumentException(
                    "Atomic resident-window commit revisionsOut length must be >= store count.",
                    nameof(revisionsOut));
            }

            if (n > 64)
            {
                throw new InvalidOperationException(
                    "Atomic resident-window commit store count exceeds stack lock-order limit 64; raise requires redesign, check storeGroupCapacity.");
            }

            for (int i = 0; i < n; i++)
            {
                if (stores[i] == null)
                {
                    throw new InvalidOperationException($"Atomic resident-window commit store[{i}] is null.");
                }

                if (batchCounts[i] <= 0)
                {
                    throw new InvalidOperationException(
                        $"Atomic resident-window commit batchCounts[{i}] must be > 0 (empty resident window is rejected).");
                }

                for (int j = 0; j < i; j++)
                {
                    if (ReferenceEquals(stores[i], stores[j]))
                    {
                        throw new InvalidOperationException(
                            $"Atomic resident-window commit store[{i}] duplicates store[{j}]; unique stores are required.");
                    }
                }
            }

            Span<int> lockOrder = stackalloc int[64];
            for (int i = 0; i < n; i++) lockOrder[i] = i;
            for (int i = 1; i < n; i++)
            {
                int value = lockOrder[i];
                long order = stores[value]._lockOrder;
                int j = i - 1;
                while (j >= 0 && stores[lockOrder[j]]._lockOrder > order)
                {
                    lockOrder[j + 1] = lockOrder[j];
                    j--;
                }

                lockOrder[j + 1] = value;
            }

            int acquiredLockCount = 0;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    Monitor.Enter(stores[lockOrder[i]]._gate);
                    acquiredLockCount++;
                }

                ulong generation = 0UL;
                for (int i = 0; i < n; i++)
                {
                    ulong storeGeneration = stores[i]._generation;
                    if (storeGeneration >= generation)
                    {
                        generation = checked(storeGeneration + 1UL);
                    }
                }

                if (generation == 0UL)
                {
                    throw new InvalidOperationException("Atomic resident-window commit failed to derive a non-zero generation.");
                }

                for (int i = 0; i < n; i++)
                {
                    int offset = batchOffsets[i];
                    int count = batchCounts[i];
                    stores[i].ValidateResidentWindowUnlocked(generation, flatTiles.Slice(offset, count));
                }

                for (int i = 0; i < n; i++)
                {
                    int offset = batchOffsets[i];
                    int count = batchCounts[i];
                    stores[i].MutateResidentWindowUnlocked(generation, flatTiles.Slice(offset, count));
                    revisionsOut[i] = stores[i].AdvanceRevision();
                }

                committedGeneration = generation;
            }
            finally
            {
                for (int i = acquiredLockCount - 1; i >= 0; i--)
                {
                    Monitor.Exit(stores[lockOrder[i]]._gate);
                }
            }
        }

        /// <summary>
        /// Legacy allocating wrapper retained for tools/tests. Runtime queue uses the span API.
        /// </summary>
        internal static uint[] ReplaceGenerationBatchesAtomically(
            IReadOnlyList<NavTileStore> stores,
            IReadOnlyList<IReadOnlyList<NavTile>> tileBatches,
            out ulong committedGeneration)
        {
            if (stores == null) throw new ArgumentNullException(nameof(stores));
            if (tileBatches == null) throw new ArgumentNullException(nameof(tileBatches));
            if (stores.Count == 0)
            {
                throw new InvalidOperationException("Atomic generation commit requires at least one NavTileStore.");
            }

            if (stores.Count != tileBatches.Count)
            {
                throw new InvalidOperationException(
                    $"Atomic generation commit store count {stores.Count} does not match tile batch count {tileBatches.Count}.");
            }

            int n = stores.Count;
            var storeArray = new NavTileStore[n];
            int totalTiles = 0;
            var offsets = new int[n];
            var counts = new int[n];
            for (int i = 0; i < n; i++)
            {
                storeArray[i] = stores[i] ?? throw new InvalidOperationException($"Atomic generation commit store[{i}] is null.");
                IReadOnlyList<NavTile> batch = tileBatches[i]
                    ?? throw new InvalidOperationException($"Atomic generation commit tileBatches[{i}] is null.");
                offsets[i] = totalTiles;
                counts[i] = batch.Count;
                totalTiles = checked(totalTiles + batch.Count);
            }

            var flat = new NavTile[totalTiles];
            for (int i = 0; i < n; i++)
            {
                IReadOnlyList<NavTile> batch = tileBatches[i];
                for (int t = 0; t < batch.Count; t++)
                {
                    flat[offsets[i] + t] = batch[t];
                }
            }

            var revisions = new uint[n];
            ReplaceGenerationBatchesAtomically(
                storeArray.AsSpan(),
                flat.AsSpan(),
                offsets.AsSpan(),
                counts.AsSpan(),
                revisions.AsSpan(),
                out committedGeneration);
            return revisions;
        }

        public void Unload(NavTileId id)
        {
            lock (_gate)
            {
                if (!TryFindIndexSlotUnlocked(id, out int indexSlot, out _))
                {
                    return;
                }

                EnsureCanAdvanceGenerationUnlocked();
                int residentSlot = _indexSlots[indexSlot];
                _indexState[indexSlot] = IndexTombstone;
                _residentOccupied[residentSlot] = 0;
                _residentTiles[residentSlot].ClearTopology();
                _residentCount--;
                AdvanceGeneration();
                AdvanceRevision();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                if (_residentCount == 0)
                {
                    return;
                }

                EnsureCanAdvanceGenerationUnlocked();
                for (int i = 0; i < _indexState.Length; i++)
                {
                    _indexState[i] = IndexEmpty;
                    _indexSlots[i] = 0;
                    _indexKeys[i] = default;
                }

                for (int i = 0; i < _residentTileCapacity; i++)
                {
                    _residentOccupied[i] = 0;
                    _residentTiles[i].ClearTopology();
                }

                _residentCount = 0;
                AdvanceGeneration();
                AdvanceRevision();
            }
        }

        private uint ApplyGenerationBatchUnlocked(ulong generation, IReadOnlyList<NavTile> tiles)
        {
            ValidateGenerationBatchUnlocked(generation, tiles);
            MutateGenerationBatchUnlocked(generation, tiles);
            return AdvanceRevision();
        }

        private void MutateGenerationBatchUnlocked(ulong generation, IReadOnlyList<NavTile> tiles)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                PublishCopyUnlocked(tiles[i]);
            }

            _generation = generation;
        }

        private void MutateGenerationBatchUnlocked(ulong generation, ReadOnlySpan<NavTile> tiles)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                PublishCopyUnlocked(tiles[i]);
            }

            _generation = generation;
        }

        private void ValidateResidentWindowUnlocked(ulong generation, ReadOnlySpan<NavTile> tiles)
        {
            if (tiles.Length == 0)
            {
                throw new InvalidOperationException("NavTileStore resident-window commit requires a non-empty tile list.");
            }

            if (tiles.Length > _residentTileCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) is insufficient for resident-window size {tiles.Length}.");
            }

            if (tiles.Length > _validateScratch.Length)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) is insufficient for resident-window size {tiles.Length}.");
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                if (tile == null)
                {
                    throw new InvalidOperationException($"NavTileStore resident-window tile[{i}] is null.");
                }

                ValidateIncomingGeometryCapacityUnlocked(tile);
                _validateScratch[i] = tile.TileId;
            }

            if (generation == 0UL)
            {
                throw new InvalidOperationException("NavTileStore resident-window commit requires a non-zero generation.");
            }

            if (generation <= _generation)
            {
                throw new InvalidOperationException(
                    $"NavTileStore resident-window generation {generation} is not strictly greater than current generation {_generation}.");
            }

            for (int i = 1; i < tiles.Length; i++)
            {
                NavTileId value = _validateScratch[i];
                int j = i - 1;
                while (j >= 0 && CompareTileId(_validateScratch[j], value) > 0)
                {
                    _validateScratch[j + 1] = _validateScratch[j];
                    j--;
                }

                _validateScratch[j + 1] = value;
            }

            for (int i = 1; i < tiles.Length; i++)
            {
                if (_validateScratch[i].Equals(_validateScratch[i - 1]))
                {
                    throw new InvalidOperationException(
                        $"NavTileStore resident-window contains duplicate tile id {_validateScratch[i]}.");
                }
            }
        }

        private void MutateResidentWindowUnlocked(ulong generation, ReadOnlySpan<NavTile> tiles)
        {
            // Evict first so peak residency never exceeds capacity while inserting the new window.
            for (int i = 0; i < _residentTileCapacity; i++)
            {
                if (_residentOccupied[i] == 0)
                {
                    continue;
                }

                NavTileId id = _residentTiles[i].TileId;
                if (!ContainsSortedTileId(_validateScratch.AsSpan(0, tiles.Length), id))
                {
                    EvictResidentSlotUnlocked(i);
                }
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                PublishCopyUnlocked(tiles[i]);
            }

            if (_residentCount != tiles.Length)
            {
                throw new InvalidOperationException(
                    $"NavTileStore resident-window commit expected {_residentCount} residents to equal window size {tiles.Length}.");
            }

            _generation = generation;
        }

        private void EvictResidentSlotUnlocked(int residentSlot)
        {
            NavTileId id = _residentTiles[residentSlot].TileId;
            if (!TryFindIndexSlotUnlocked(id, out int indexSlot, out _))
            {
                throw new InvalidOperationException(
                    $"NavTileStore resident-window eviction failed: tile {id} is occupied but missing from the index.");
            }

            _indexState[indexSlot] = IndexTombstone;
            _residentOccupied[residentSlot] = 0;
            _residentTiles[residentSlot].ClearTopology();
            _residentCount--;
        }

        private static bool ContainsSortedTileId(ReadOnlySpan<NavTileId> sortedAscending, NavTileId id)
        {
            int lo = 0;
            int hi = sortedAscending.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = CompareTileId(sortedAscending[mid], id);
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

        private NavTile PublishCopyUnlocked(NavTile incoming)
        {
            if (incoming == null)
            {
                throw new InvalidOperationException("NavTileStore generation batch tile is null.");
            }

            ValidateIncomingGeometryCapacityUnlocked(incoming);

            if (TryFindIndexSlotUnlocked(incoming.TileId, out int existingIndexSlot, out _))
            {
                NavTile resident = _residentTiles[_indexSlots[existingIndexSlot]];
                resident.CopyGeometryFrom(incoming);
                return resident;
            }

            if (!TryFindInsertIndexSlotUnlocked(incoming.TileId, out int insertIndexSlot))
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) exhausted; required {_residentCount + 1}.");
            }

            int free = FindFreeResidentSlotUnlocked();
            NavTile slot = _residentTiles[free];
            slot.CopyGeometryFrom(incoming);
            _residentOccupied[free] = 1;
            _indexKeys[insertIndexSlot] = incoming.TileId;
            _indexState[insertIndexSlot] = IndexOccupied;
            _indexSlots[insertIndexSlot] = free;
            _residentCount++;
            return slot;
        }

        private int FindFreeResidentSlotUnlocked()
        {
            if (_residentCount >= _residentTileCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) exhausted; required {_residentCount + 1}.");
            }

            for (int i = 0; i < _residentTileCapacity; i++)
            {
                if (_residentOccupied[i] == 0)
                {
                    return i;
                }
            }

            throw new InvalidOperationException(
                $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) exhausted; required {_residentCount + 1}.");
        }

        private void ValidateGenerationBatchUnlocked(ulong generation, IReadOnlyList<NavTile> tiles)
        {
            if (tiles.Count == 0)
            {
                throw new InvalidOperationException("NavTileStore generation batch requires a non-empty tile list.");
            }

            if (tiles.Count > _validateScratch.Length)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) is insufficient for generation batch size {tiles.Count}.");
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                NavTile tile = tiles[i];
                if (tile == null)
                {
                    throw new InvalidOperationException($"NavTileStore generation batch tile[{i}] is null.");
                }

                ValidateIncomingGeometryCapacityUnlocked(tile);
                _validateScratch[i] = tile.TileId;
            }

            ValidateGenerationHeaderAndDuplicates(generation, tiles.Count);
        }

        private void ValidateGenerationBatchUnlocked(ulong generation, ReadOnlySpan<NavTile> tiles)
        {
            if (tiles.Length == 0)
            {
                throw new InvalidOperationException("NavTileStore generation batch requires a non-empty tile list.");
            }

            if (tiles.Length > _validateScratch.Length)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) is insufficient for generation batch size {tiles.Length}.");
            }

            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                if (tile == null)
                {
                    throw new InvalidOperationException($"NavTileStore generation batch tile[{i}] is null.");
                }

                ValidateIncomingGeometryCapacityUnlocked(tile);
                _validateScratch[i] = tile.TileId;
            }

            ValidateGenerationHeaderAndDuplicates(generation, tiles.Length);
        }

        private void ValidateIncomingGeometryCapacityUnlocked(NavTile tile)
        {
            if (tile.VertexCount > _outputVertexCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputVertexCapacity ({_outputVertexCapacity}) exhausted; required {tile.VertexCount}.");
            }

            if (tile.TriangleCount > _outputTriangleCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputTriangleCapacity ({_outputTriangleCapacity}) exhausted; required {tile.TriangleCount}.");
            }

            if (tile.PortalCount > _outputPortalCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputPortalCapacity ({_outputPortalCapacity}) exhausted; required {tile.PortalCount}.");
            }
        }

        private void ValidateGenerationHeaderAndDuplicates(ulong generation, int tileCount)
        {
            if (generation == 0UL)
            {
                throw new InvalidOperationException("NavTileStore generation batch requires a non-zero generation.");
            }

            if (generation <= _generation)
            {
                throw new InvalidOperationException(
                    $"NavTileStore generation batch {generation} is not strictly greater than current generation {_generation}.");
            }

            for (int i = 1; i < tileCount; i++)
            {
                NavTileId value = _validateScratch[i];
                int j = i - 1;
                while (j >= 0 && CompareTileId(_validateScratch[j], value) > 0)
                {
                    _validateScratch[j + 1] = _validateScratch[j];
                    j--;
                }

                _validateScratch[j + 1] = value;
            }

            for (int i = 1; i < tileCount; i++)
            {
                if (_validateScratch[i].Equals(_validateScratch[i - 1]))
                {
                    throw new InvalidOperationException(
                        $"NavTileStore generation batch contains duplicate tile id {_validateScratch[i]}.");
                }
            }

            int insertsNeeded = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (!TryFindIndexSlotUnlocked(_validateScratch[i], out _, out _))
                {
                    insertsNeeded++;
                }
            }

            if (_residentCount + insertsNeeded > _residentTileCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_residentTileCapacity}) exhausted; required {_residentCount + insertsNeeded}.");
            }
        }

        private bool TryFindIndexSlotUnlocked(NavTileId id, out int indexSlot, out int probeCount)
        {
            int start = HashTileId(id) & _indexMask;
            indexSlot = start;
            probeCount = 0;
            for (int i = 0; i < _indexState.Length; i++)
            {
                probeCount++;
                int slot = (start + i) & _indexMask;
                byte state = _indexState[slot];
                if (state == IndexEmpty)
                {
                    return false;
                }

                if (state == IndexOccupied && _indexKeys[slot].Equals(id))
                {
                    indexSlot = slot;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindInsertIndexSlotUnlocked(NavTileId id, out int indexSlot)
        {
            int start = HashTileId(id) & _indexMask;
            indexSlot = -1;
            int firstTombstone = -1;
            for (int i = 0; i < _indexState.Length; i++)
            {
                int slot = (start + i) & _indexMask;
                byte state = _indexState[slot];
                if (state == IndexEmpty)
                {
                    indexSlot = firstTombstone >= 0 ? firstTombstone : slot;
                    return true;
                }

                if (state == IndexTombstone)
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = slot;
                    }

                    continue;
                }

                if (_indexKeys[slot].Equals(id))
                {
                    indexSlot = slot;
                    return true;
                }
            }

            if (firstTombstone >= 0)
            {
                indexSlot = firstTombstone;
                return true;
            }

            return false;
        }

        private static int HashTileId(NavTileId id)
        {
            unchecked
            {
                int hash = id.ChunkX * 73856093;
                hash ^= id.ChunkY * 19349663;
                hash ^= id.Layer * 83492791;
                return hash;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 1)
            {
                return 1;
            }

            uint v = (uint)(value - 1);
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return (int)(v + 1);
        }

        private static int CompareTileId(NavTileId a, NavTileId b)
        {
            int y = a.ChunkY.CompareTo(b.ChunkY);
            if (y != 0) return y;
            int x = a.ChunkX.CompareTo(b.ChunkX);
            if (x != 0) return x;
            return a.Layer.CompareTo(b.Layer);
        }

        private void EnsureCanAdvanceGenerationUnlocked()
        {
            if (_generation == ulong.MaxValue)
            {
                throw new InvalidOperationException("NavTileStore Generation overflow.");
            }
        }

        private void AdvanceGeneration()
        {
            EnsureCanAdvanceGenerationUnlocked();
            _generation++;
        }

        private uint AdvanceRevision()
        {
            _revision = _revision == uint.MaxValue ? 1u : _revision + 1u;
            return _revision;
        }
    }
}
