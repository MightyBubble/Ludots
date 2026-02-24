using System;
using System.Runtime.CompilerServices;
using Arch.LowLevel;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Spatial
{
    public sealed unsafe class Nav2DCellMap : IDisposable
    {
        private readonly Fix64 _cellSizeCm;
        private readonly LongKeyMap<int> _heads;

        private UnsafeArray<int> _next;
        private int _agentCapacity;

        public Nav2DCellMap(Fix64 cellSizeCm, int initialAgentCapacity, int initialCellCapacity)
        {
            _cellSizeCm = cellSizeCm;
            _heads = new LongKeyMap<int>(initialCellCapacity);
            _next = new UnsafeArray<int>(Math.Max(8, initialAgentCapacity));
            _agentCapacity = _next.Length;
        }

        public void Build(ReadOnlySpan<Fix64Vec2> positionsCm)
        {
            _heads.Clear();
            EnsureAgentCapacity(positionsCm.Length);

            for (int i = 0; i < positionsCm.Length; i++)
            {
                Fix64Vec2 p = positionsCm[i];
                int cx = (p.X / _cellSizeCm).FloorToInt();
                int cy = (p.Y / _cellSizeCm).FloorToInt();
                long key = Nav2DKeyPacking.PackInt2(cx, cy);

                ref int head = ref _heads.GetValueRefOrAddDefault(key, out bool existed);
                if (!existed) head = -1;

                _next[i] = head;
                head = i;
            }
        }

        public int CollectNeighbors(int selfIndex, Fix64Vec2 selfPosCm, Fix64 radiusCm, ReadOnlySpan<Fix64Vec2> positionsCm, Span<int> neighborsOut)
        {
            float radius = radiusCm.ToFloat();
            float radiusSq = radius * radius;

            int cx = (selfPosCm.X / _cellSizeCm).FloorToInt();
            int cy = (selfPosCm.Y / _cellSizeCm).FloorToInt();
            int r = (radiusCm / _cellSizeCm).CeilToInt();

            float sx = selfPosCm.X.ToFloat();
            float sy = selfPosCm.Y.ToFloat();

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

        public void Dispose()
        {
            _heads.Dispose();
            _next.Dispose();
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

