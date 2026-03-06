using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.LowLevel;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Spatial
{
    public sealed unsafe class Nav2DCellMap : IDisposable
    {
        private readonly float _invCellSizeCm;
        private readonly LongKeyMap<int> _heads;

        private UnsafeArray<int> _next;
        private int _agentCapacity;

        public Nav2DCellMap(Fix64 cellSizeCm, int initialAgentCapacity, int initialCellCapacity)
        {
            float cellSize = cellSizeCm.ToFloat();
            _invCellSizeCm = cellSize > 1e-6f ? (1f / cellSize) : 0f;
            _heads = new LongKeyMap<int>(initialCellCapacity);
            _next = new UnsafeArray<int>(Math.Max(8, initialAgentCapacity));
            _agentCapacity = _next.Length;
        }

        public void Build(ReadOnlySpan<Vector2> positions)
        {
            _heads.Clear();
            EnsureAgentCapacity(positions.Length);

            for (int i = 0; i < positions.Length; i++)
            {
                Vector2 p = positions[i];
                int cx = FloorToCell(p.X);
                int cy = FloorToCell(p.Y);
                long key = Nav2DKeyPacking.PackInt2(cx, cy);

                ref int head = ref _heads.GetValueRefOrAddDefault(key, out bool existed);
                if (!existed) head = -1;

                _next[i] = head;
                head = i;
            }
        }

        public void Build(ReadOnlySpan<Fix64Vec2> positionsCm)
        {
            _heads.Clear();
            EnsureAgentCapacity(positionsCm.Length);

            for (int i = 0; i < positionsCm.Length; i++)
            {
                Fix64Vec2 p = positionsCm[i];
                int cx = FloorToCell(p.X.ToFloat());
                int cy = FloorToCell(p.Y.ToFloat());
                long key = Nav2DKeyPacking.PackInt2(cx, cy);

                ref int head = ref _heads.GetValueRefOrAddDefault(key, out bool existed);
                if (!existed) head = -1;

                _next[i] = head;
                head = i;
            }
        }

        public int CollectNeighbors(int selfIndex, Vector2 selfPos, float radius, ReadOnlySpan<Vector2> positions, Span<int> neighborsOut)
        {
            float radiusSq = radius * radius;

            int cx = FloorToCell(selfPos.X);
            int cy = FloorToCell(selfPos.Y);
            int r = CeilToCells(radius);

            float sx = selfPos.X;
            float sy = selfPos.Y;

            int count = 0;
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    long key = Nav2DKeyPacking.PackInt2(x, y);
                    if (!_heads.TryGetSlot(key, out int slot)) continue;
                    int head = _heads.GetValueRefBySlot(slot);
                    int it = head;
                    while (it >= 0)
                    {
                        if (it != selfIndex)
                        {
                            Vector2 op = positions[it];
                            float dx = op.X - sx;
                            float dy = op.Y - sy;
                            float d2 = dx * dx + dy * dy;
                            if (d2 <= radiusSq)
                            {
                                if (count < neighborsOut.Length)
                                {
                                    neighborsOut[count++] = it;
                                }
                                else
                                {
                                    return count;
                                }
                            }
                        }

                        it = _next[it];
                    }
                }
            }

            return count;
        }

        public int CollectNeighbors(int selfIndex, Fix64Vec2 selfPosCm, Fix64 radiusCm, ReadOnlySpan<Fix64Vec2> positionsCm, Span<int> neighborsOut)
        {
            float radius = radiusCm.ToFloat();
            float radiusSq = radius * radius;

            float sx = selfPosCm.X.ToFloat();
            float sy = selfPosCm.Y.ToFloat();
            int cx = FloorToCell(sx);
            int cy = FloorToCell(sy);
            int r = CeilToCells(radius);

            int count = 0;
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    long key = Nav2DKeyPacking.PackInt2(x, y);
                    if (!_heads.TryGetSlot(key, out int slot)) continue;
                    int head = _heads.GetValueRefBySlot(slot);
                    int it = head;
                    while (it >= 0)
                    {
                        if (it != selfIndex)
                        {
                            Fix64Vec2 op = positionsCm[it];
                            float dx = op.X.ToFloat() - sx;
                            float dy = op.Y.ToFloat() - sy;
                            float d2 = dx * dx + dy * dy;
                            if (d2 <= radiusSq)
                            {
                                if (count < neighborsOut.Length)
                                {
                                    neighborsOut[count++] = it;
                                }
                                else
                                {
                                    return count;
                                }
                            }
                        }

                        it = _next[it];
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
            if (neighborsOut.Length == 0 || radius <= 0f)
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

            if (!VisitCell(cx, cy, selfIndex, sx, sy, radiusSq, positions, neighborsOut, ref count, ref checks, effectiveMaxChecks))
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
                    if (!VisitCell(x, minY, selfIndex, sx, sy, radiusSq, positions, neighborsOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }
                    if (!VisitCell(x, maxY, selfIndex, sx, sy, radiusSq, positions, neighborsOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }
                }

                for (int y = minY + 1; y < maxY; y++)
                {
                    if (!VisitCell(minX, y, selfIndex, sx, sy, radiusSq, positions, neighborsOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }
                    if (!VisitCell(maxX, y, selfIndex, sx, sy, radiusSq, positions, neighborsOut, ref count, ref checks, effectiveMaxChecks))
                    {
                        return count;
                    }
                }
            }

            return count;
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
            ref int count,
            ref int checks,
            int maxCandidateChecks)
        {
            long key = Nav2DKeyPacking.PackInt2(cx, cy);
            if (!_heads.TryGetSlot(key, out int slot))
            {
                return true;
            }

            int it = _heads.GetValueRefBySlot(slot);
            while (it >= 0)
            {
                if (it != selfIndex)
                {
                    checks++;

                    Vector2 op = positions[it];
                    float dx = op.X - sx;
                    float dy = op.Y - sy;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= radiusSq)
                    {
                        InsertNearest(neighborsOut, ref count, it, d2, sx, sy, positions);
                    }

                    if (checks >= maxCandidateChecks)
                    {
                        return false;
                    }
                }

                it = _next[it];
            }

            return true;
        }

        private static void InsertNearest(Span<int> neighborsOut, ref int count, int candidateIndex, float candidateDistanceSq, float sx, float sy, ReadOnlySpan<Vector2> positions)
        {
            int capacity = neighborsOut.Length;
            if (capacity == 0)
            {
                return;
            }

            int insertAt = count;
            if (count < capacity)
            {
                while (insertAt > 0 && DistanceSq(neighborsOut[insertAt - 1], sx, sy, positions) > candidateDistanceSq)
                {
                    neighborsOut[insertAt] = neighborsOut[insertAt - 1];
                    insertAt--;
                }

                neighborsOut[insertAt] = candidateIndex;
                count++;
                return;
            }

            if (DistanceSq(neighborsOut[capacity - 1], sx, sy, positions) <= candidateDistanceSq)
            {
                return;
            }

            insertAt = capacity - 1;
            while (insertAt > 0 && DistanceSq(neighborsOut[insertAt - 1], sx, sy, positions) > candidateDistanceSq)
            {
                neighborsOut[insertAt] = neighborsOut[insertAt - 1];
                insertAt--;
            }

            neighborsOut[insertAt] = candidateIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DistanceSq(int index, float sx, float sy, ReadOnlySpan<Vector2> positions)
        {
            Vector2 op = positions[index];
            float dx = op.X - sx;
            float dy = op.Y - sy;
            return dx * dx + dy * dy;
        }

        public void Dispose()
        {
            _heads.Dispose();
            _next.Dispose();
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
        private void EnsureAgentCapacity(int required)
        {
            if (required <= _agentCapacity) return;
            int nextCap = _agentCapacity;
            while (nextCap < required) nextCap *= 2;
            var old = _next;
            _next = new UnsafeArray<int>(nextCap);
            old.Dispose();
            _agentCapacity = nextCap;
        }
    }
}
