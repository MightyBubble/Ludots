using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.LowLevel;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Config;

namespace Ludots.Core.Navigation2D.Spatial
{
    public readonly record struct Nav2DCellMapSettings(
        Navigation2DSpatialUpdateMode UpdateMode,
        int RebuildCellMigrationsThreshold,
        int RebuildAccumulatedCellMigrationsThreshold)
    {
        public static Nav2DCellMapSettings Default => new(
            Navigation2DSpatialUpdateMode.Adaptive,
            RebuildCellMigrationsThreshold: 128,
            RebuildAccumulatedCellMigrationsThreshold: 1024);

        public static Nav2DCellMapSettings FromConfig(Navigation2DSpatialPartitionConfig? config)
        {
            if (config == null)
            {
                return Default;
            }

            return new Nav2DCellMapSettings(
                config.UpdateMode,
                Math.Max(0, config.RebuildCellMigrationsThreshold),
                Math.Max(0, config.RebuildAccumulatedCellMigrationsThreshold));
        }
    }

    public sealed class Nav2DCellMap : IDisposable
    {
        private readonly float _invCellSizeCm;
        private readonly Nav2DCellMapSettings _settings;

        private UnsafeArray<long> _agentCellKeys;
        private UnsafeArray<int> _sortedAgentIndices;
        private UnsafeArray<long> _sortedCellKeys;
        private UnsafeArray<long> _uniqueCellKeys;
        private UnsafeArray<int> _cellStarts;
        private UnsafeArray<int> _cellCounts;

        private int _agentCapacity;
        private int _cellCapacity;
        private int _agentCount;
        private int _cellCount;
        private int _migrationsSinceBuild;

        public long InstrumentedUpdateTicks;
        public long InstrumentedUpdateCalls;
        public long InstrumentedDirtyAgents;
        public long InstrumentedCellMigrations;
        public long InstrumentedFullRebuilds;
        public long InstrumentedIncrementalUpdates;

        public Navigation2DSpatialUpdateMode UpdateMode => _settings.UpdateMode;

        public Nav2DCellMap(Fix64 cellSizeCm, int initialAgentCapacity, int initialCellCapacity)
            : this(cellSizeCm, initialAgentCapacity, initialCellCapacity, Nav2DCellMapSettings.Default)
        {
        }

        public Nav2DCellMap(Fix64 cellSizeCm, int initialAgentCapacity, int initialCellCapacity, Nav2DCellMapSettings settings)
        {
            float cellSize = cellSizeCm.ToFloat();
            _invCellSizeCm = cellSize > 1e-6f ? 1f / cellSize : 0f;
            _settings = settings;

            _agentCapacity = Math.Max(8, initialAgentCapacity);
            _cellCapacity = Math.Max(8, initialCellCapacity);

            _agentCellKeys = new UnsafeArray<long>(_agentCapacity);
            _sortedAgentIndices = new UnsafeArray<int>(_agentCapacity);
            _sortedCellKeys = new UnsafeArray<long>(_agentCapacity);
            _uniqueCellKeys = new UnsafeArray<long>(_cellCapacity);
            _cellStarts = new UnsafeArray<int>(_cellCapacity);
            _cellCounts = new UnsafeArray<int>(_cellCapacity);

            _agentCount = 0;
            _cellCount = 0;
            _migrationsSinceBuild = 0;
        }

        public void Build(ReadOnlySpan<Vector2> positions)
        {
            EnsureAgentCapacity(positions.Length);
            _agentCount = positions.Length;
            _migrationsSinceBuild = 0;

            for (int i = 0; i < positions.Length; i++)
            {
                _agentCellKeys[i] = ToCellKey(positions[i]);
            }

            RebuildDenseBuckets();
        }

        public void Build(ReadOnlySpan<Fix64Vec2> positionsCm)
        {
            EnsureAgentCapacity(positionsCm.Length);
            _agentCount = positionsCm.Length;
            _migrationsSinceBuild = 0;

            for (int i = 0; i < positionsCm.Length; i++)
            {
                _agentCellKeys[i] = ToCellKey(positionsCm[i].X.ToFloat(), positionsCm[i].Y.ToFloat());
            }

            RebuildDenseBuckets();
        }

        public void UpdatePositions(ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> dirtyAgentIndices)
        {
            long t0 = Stopwatch.GetTimestamp();
            CountDirtyAgentsAndCellMigrations(positions, dirtyAgentIndices, out int dirtyAgents, out int cellMigrations);

            if (cellMigrations > 0)
            {
                if (ShouldRebuild(cellMigrations))
                {
                    Build(positions);
                    InstrumentedFullRebuilds++;
                }
                else
                {
                    ApplyIncrementalUpdates(positions, dirtyAgentIndices);
                    _migrationsSinceBuild += cellMigrations;
                    InstrumentedIncrementalUpdates++;
                }
            }

            InstrumentedUpdateTicks += Stopwatch.GetTimestamp() - t0;
            InstrumentedUpdateCalls++;
            InstrumentedDirtyAgents += dirtyAgents;
            InstrumentedCellMigrations += cellMigrations;
        }

        public void ResetInstrumentation()
        {
            InstrumentedUpdateTicks = 0;
            InstrumentedUpdateCalls = 0;
            InstrumentedDirtyAgents = 0;
            InstrumentedCellMigrations = 0;
            InstrumentedFullRebuilds = 0;
            InstrumentedIncrementalUpdates = 0;
        }

        public int CollectNeighbors(int selfIndex, Vector2 selfPos, float radius, ReadOnlySpan<Vector2> positions, Span<int> neighborsOut)
        {
            if (_agentCount <= 0 || neighborsOut.Length == 0 || radius <= 0f)
            {
                return 0;
            }

            float radiusSq = radius * radius;
            float sx = selfPos.X;
            float sy = selfPos.Y;
            int cx = FloorToCell(sx);
            int cy = FloorToCell(sy);
            int ring = CeilToCells(radius);
            int count = 0;

            for (int y = cy - ring; y <= cy + ring; y++)
            {
                for (int x = cx - ring; x <= cx + ring; x++)
                {
                    if (!TryGetCellSpan(Nav2DKeyPacking.PackInt2(x, y), out int start, out int cellCount))
                    {
                        continue;
                    }

                    int end = start + cellCount;
                    for (int i = start; i < end; i++)
                    {
                        int neighborIndex = _sortedAgentIndices[i];
                        if (neighborIndex == selfIndex)
                        {
                            continue;
                        }

                        Vector2 op = positions[neighborIndex];
                        float dx = op.X - sx;
                        float dy = op.Y - sy;
                        float d2 = dx * dx + dy * dy;
                        if (d2 > radiusSq)
                        {
                            continue;
                        }

                        neighborsOut[count++] = neighborIndex;
                        if (count >= neighborsOut.Length)
                        {
                            return count;
                        }
                    }
                }
            }

            return count;
        }

        public int CollectNeighbors(int selfIndex, Fix64Vec2 selfPosCm, Fix64 radiusCm, ReadOnlySpan<Fix64Vec2> positionsCm, Span<int> neighborsOut)
        {
            if (_agentCount <= 0 || neighborsOut.Length == 0 || radiusCm <= Fix64.Zero)
            {
                return 0;
            }

            float radius = radiusCm.ToFloat();
            float radiusSq = radius * radius;
            float sx = selfPosCm.X.ToFloat();
            float sy = selfPosCm.Y.ToFloat();
            int cx = FloorToCell(sx);
            int cy = FloorToCell(sy);
            int ring = CeilToCells(radius);
            int count = 0;

            for (int y = cy - ring; y <= cy + ring; y++)
            {
                for (int x = cx - ring; x <= cx + ring; x++)
                {
                    if (!TryGetCellSpan(Nav2DKeyPacking.PackInt2(x, y), out int start, out int cellCount))
                    {
                        continue;
                    }

                    int end = start + cellCount;
                    for (int i = start; i < end; i++)
                    {
                        int neighborIndex = _sortedAgentIndices[i];
                        if (neighborIndex == selfIndex)
                        {
                            continue;
                        }

                        Fix64Vec2 op = positionsCm[neighborIndex];
                        float dx = op.X.ToFloat() - sx;
                        float dy = op.Y.ToFloat() - sy;
                        float d2 = dx * dx + dy * dy;
                        if (d2 > radiusSq)
                        {
                            continue;
                        }

                        neighborsOut[count++] = neighborIndex;
                        if (count >= neighborsOut.Length)
                        {
                            return count;
                        }
                    }
                }
            }

            return count;
        }

        public int CollectNearestNeighborsBudgeted(
            int selfIndex,
            Vector2 selfPos,
            float radius,
            ReadOnlySpan<Vector2> positions,
            Span<int> neighborsOut,
            int maxCandidateChecks)
        {
            return CollectNearestNeighborsBudgeted(selfIndex, selfPos, radius, positions, neighborsOut, Span<float>.Empty, maxCandidateChecks);
        }

        public int CollectNearestNeighborsBudgeted(
            int selfIndex,
            Vector2 selfPos,
            float radius,
            ReadOnlySpan<Vector2> positions,
            Span<int> neighborsOut,
            Span<float> neighborDistanceSqOut,
            int maxCandidateChecks)
        {
            if (_agentCount <= 0 || neighborsOut.Length == 0 || radius <= 0f)
            {
                return 0;
            }

            float radiusSq = radius * radius;
            float sx = selfPos.X;
            float sy = selfPos.Y;
            int cx = FloorToCell(sx);
            int cy = FloorToCell(sy);
            int ringLimit = CeilToCells(radius);
            int effectiveMaxChecks = maxCandidateChecks > 0 ? maxCandidateChecks : int.MaxValue;

            int count = 0;
            int checks = 0;

            if (!VisitCell(cx, cy, selfIndex, sx, sy, radiusSq, positions, neighborsOut, neighborDistanceSqOut, ref count, ref checks, effectiveMaxChecks))
            {
                return count;
            }

            for (int ring = 1; ring <= ringLimit; ring++)
            {
                int minX = cx - ring;
                int maxX = cx + ring;
                int minY = cy - ring;
                int maxY = cy + ring;

                for (int x = minX; x <= maxX; x++)
                {
                    if (!VisitCell(x, minY, selfIndex, sx, sy, radiusSq, positions, neighborsOut, neighborDistanceSqOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }

                    if (!VisitCell(x, maxY, selfIndex, sx, sy, radiusSq, positions, neighborsOut, neighborDistanceSqOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }
                }

                for (int y = minY + 1; y < maxY; y++)
                {
                    if (!VisitCell(minX, y, selfIndex, sx, sy, radiusSq, positions, neighborsOut, neighborDistanceSqOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }

                    if (!VisitCell(maxX, y, selfIndex, sx, sy, radiusSq, positions, neighborsOut, neighborDistanceSqOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }
                }
            }

            return count;
        }

        public void Dispose()
        {
            _agentCellKeys.Dispose();
            _sortedAgentIndices.Dispose();
            _sortedCellKeys.Dispose();
            _uniqueCellKeys.Dispose();
            _cellStarts.Dispose();
            _cellCounts.Dispose();
        }

        private bool VisitCell(
            int cx,
            int cy,
            int selfIndex,
            float sx,
            float sy,
            float radiusSq,
            ReadOnlySpan<Vector2> positions,
            Span<int> neighborsOut,
            Span<float> neighborDistanceSqOut,
            ref int count,
            ref int checks,
            int maxCandidateChecks)
        {
            if (!TryGetCellSpan(Nav2DKeyPacking.PackInt2(cx, cy), out int start, out int cellCount))
            {
                return true;
            }

            int end = start + cellCount;
            for (int i = start; i < end; i++)
            {
                int candidateIndex = _sortedAgentIndices[i];
                if (candidateIndex == selfIndex)
                {
                    continue;
                }

                checks++;

                Vector2 op = positions[candidateIndex];
                float dx = op.X - sx;
                float dy = op.Y - sy;
                float d2 = dx * dx + dy * dy;
                if (d2 <= radiusSq)
                {
                    InsertNearest(neighborsOut, neighborDistanceSqOut, ref count, candidateIndex, d2, sx, sy, positions);
                }

                if (checks >= maxCandidateChecks)
                {
                    return false;
                }
            }

            return true;
        }

        private static void InsertNearest(Span<int> neighborsOut, Span<float> neighborDistanceSqOut, ref int count, int candidateIndex, float candidateDistanceSq, float sx, float sy, ReadOnlySpan<Vector2> positions)
        {
            int capacity = neighborsOut.Length;
            if (capacity == 0)
            {
                return;
            }

            if (neighborDistanceSqOut.Length >= capacity)
            {
                int insertAt = count;
                if (count < capacity)
                {
                    while (insertAt > 0 && neighborDistanceSqOut[insertAt - 1] > candidateDistanceSq)
                    {
                        neighborsOut[insertAt] = neighborsOut[insertAt - 1];
                        neighborDistanceSqOut[insertAt] = neighborDistanceSqOut[insertAt - 1];
                        insertAt--;
                    }

                    neighborsOut[insertAt] = candidateIndex;
                    neighborDistanceSqOut[insertAt] = candidateDistanceSq;
                    count++;
                    return;
                }

                if (neighborDistanceSqOut[capacity - 1] <= candidateDistanceSq)
                {
                    return;
                }

                insertAt = capacity - 1;
                while (insertAt > 0 && neighborDistanceSqOut[insertAt - 1] > candidateDistanceSq)
                {
                    neighborsOut[insertAt] = neighborsOut[insertAt - 1];
                    neighborDistanceSqOut[insertAt] = neighborDistanceSqOut[insertAt - 1];
                    insertAt--;
                }

                neighborsOut[insertAt] = candidateIndex;
                neighborDistanceSqOut[insertAt] = candidateDistanceSq;
                return;
            }

            int fallbackInsertAt = count;
            if (count < capacity)
            {
                while (fallbackInsertAt > 0 && DistanceSq(neighborsOut[fallbackInsertAt - 1], sx, sy, positions) > candidateDistanceSq)
                {
                    neighborsOut[fallbackInsertAt] = neighborsOut[fallbackInsertAt - 1];
                    fallbackInsertAt--;
                }

                neighborsOut[fallbackInsertAt] = candidateIndex;
                count++;
                return;
            }

            if (DistanceSq(neighborsOut[capacity - 1], sx, sy, positions) <= candidateDistanceSq)
            {
                return;
            }

            fallbackInsertAt = capacity - 1;
            while (fallbackInsertAt > 0 && DistanceSq(neighborsOut[fallbackInsertAt - 1], sx, sy, positions) > candidateDistanceSq)
            {
                neighborsOut[fallbackInsertAt] = neighborsOut[fallbackInsertAt - 1];
                fallbackInsertAt--;
            }

            neighborsOut[fallbackInsertAt] = candidateIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DistanceSq(int index, float sx, float sy, ReadOnlySpan<Vector2> positions)
        {
            Vector2 op = positions[index];
            float dx = op.X - sx;
            float dy = op.Y - sy;
            return dx * dx + dy * dy;
        }

        private void RebuildDenseBuckets()
        {
            if (_agentCount <= 0)
            {
                _cellCount = 0;
                return;
            }

            EnsureAgentCapacity(_agentCount);

            var sortedKeys = _sortedCellKeys.AsSpan().Slice(0, _agentCount);
            var sortedIndices = _sortedAgentIndices.AsSpan().Slice(0, _agentCount);
            var agentCellKeys = _agentCellKeys.AsSpan().Slice(0, _agentCount);

            for (int i = 0; i < _agentCount; i++)
            {
                sortedKeys[i] = agentCellKeys[i];
                sortedIndices[i] = i;
            }

            if (_agentCount > 1)
            {
                sortedKeys.Sort(sortedIndices);
            }

            int uniqueCount = 0;
            long currentKey = sortedKeys[0];
            int currentStart = 0;
            for (int i = 1; i < _agentCount; i++)
            {
                if (sortedKeys[i] == currentKey)
                {
                    continue;
                }

                WriteCellBucket(uniqueCount++, currentKey, currentStart, i - currentStart);
                currentKey = sortedKeys[i];
                currentStart = i;
            }

            WriteCellBucket(uniqueCount++, currentKey, currentStart, _agentCount - currentStart);
            _cellCount = uniqueCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteCellBucket(int slot, long key, int start, int count)
        {
            EnsureCellCapacity(slot + 1);
            _uniqueCellKeys[slot] = key;
            _cellStarts[slot] = start;
            _cellCounts[slot] = count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetCellSpan(long key, out int start, out int count)
        {
            int low = 0;
            int high = _cellCount - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                long midKey = _uniqueCellKeys[mid];
                if (midKey < key)
                {
                    low = mid + 1;
                    continue;
                }

                if (midKey > key)
                {
                    high = mid - 1;
                    continue;
                }

                start = _cellStarts[mid];
                count = _cellCounts[mid];
                return true;
            }

            start = 0;
            count = 0;
            return false;
        }

        private void CountDirtyAgentsAndCellMigrations(ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> dirtyAgentIndices, out int dirtyAgents, out int cellMigrations)
        {
            dirtyAgents = 0;
            cellMigrations = 0;

            for (int i = 0; i < dirtyAgentIndices.Length; i++)
            {
                int agentIndex = dirtyAgentIndices[i];
                if ((uint)agentIndex >= (uint)_agentCount || (uint)agentIndex >= (uint)positions.Length)
                {
                    continue;
                }

                dirtyAgents++;
                if (ToCellKey(positions[agentIndex]) != _agentCellKeys[agentIndex])
                {
                    cellMigrations++;
                }
            }
        }

        private void ApplyIncrementalUpdates(ReadOnlySpan<Vector2> positions, ReadOnlySpan<int> dirtyAgentIndices)
        {
            bool anyChanged = false;
            for (int i = 0; i < dirtyAgentIndices.Length; i++)
            {
                int agentIndex = dirtyAgentIndices[i];
                if ((uint)agentIndex >= (uint)_agentCount || (uint)agentIndex >= (uint)positions.Length)
                {
                    continue;
                }

                long newKey = ToCellKey(positions[agentIndex]);
                if (newKey == _agentCellKeys[agentIndex])
                {
                    continue;
                }

                _agentCellKeys[agentIndex] = newKey;
                anyChanged = true;
            }

            if (anyChanged)
            {
                RebuildDenseBuckets();
            }
        }

        private bool ShouldRebuild(int cellMigrations)
        {
            if (cellMigrations <= 0)
            {
                return false;
            }

            return _settings.UpdateMode switch
            {
                Navigation2DSpatialUpdateMode.Incremental => false,
                Navigation2DSpatialUpdateMode.RebuildOnAnyCellMigration => true,
                _ => ShouldAdaptiveRebuild(cellMigrations)
            };
        }

        private bool ShouldAdaptiveRebuild(int cellMigrations)
        {
            if (_settings.RebuildCellMigrationsThreshold > 0 && cellMigrations >= _settings.RebuildCellMigrationsThreshold)
            {
                return true;
            }

            if (_settings.RebuildAccumulatedCellMigrationsThreshold > 0 &&
                _migrationsSinceBuild + cellMigrations >= _settings.RebuildAccumulatedCellMigrationsThreshold)
            {
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureAgentCapacity(int required)
        {
            if (required <= _agentCapacity)
            {
                return;
            }

            int nextCap = _agentCapacity;
            while (nextCap < required)
            {
                nextCap *= 2;
            }

            _agentCellKeys = UnsafeArray.Resize(ref _agentCellKeys, nextCap);
            _sortedAgentIndices = UnsafeArray.Resize(ref _sortedAgentIndices, nextCap);
            _sortedCellKeys = UnsafeArray.Resize(ref _sortedCellKeys, nextCap);
            _agentCapacity = nextCap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCellCapacity(int required)
        {
            if (required <= _cellCapacity)
            {
                return;
            }

            int nextCap = _cellCapacity;
            while (nextCap < required)
            {
                nextCap *= 2;
            }

            _uniqueCellKeys = UnsafeArray.Resize(ref _uniqueCellKeys, nextCap);
            _cellStarts = UnsafeArray.Resize(ref _cellStarts, nextCap);
            _cellCounts = UnsafeArray.Resize(ref _cellCounts, nextCap);
            _cellCapacity = nextCap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FloorToCell(float value)
        {
            return (int)MathF.Floor(value * _invCellSizeCm);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CeilToCells(float value)
        {
            return Math.Max(0, (int)MathF.Ceiling(value * _invCellSizeCm));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ToCellKey(in Vector2 position)
        {
            return ToCellKey(position.X, position.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ToCellKey(float x, float y)
        {
            return Nav2DKeyPacking.PackInt2(FloorToCell(x), FloorToCell(y));
        }
    }
}
