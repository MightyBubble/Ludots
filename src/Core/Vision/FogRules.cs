using Ludots.Core.Mathematics;

namespace Ludots.Core.Vision
{
    public interface IFogElevationSource
    {
        int GetHeightTier(FogCell cell);
    }

    public interface IFogOcclusionSource
    {
        bool IsOpaque(FogCell cell);
    }

    public interface IFogConcealmentSource
    {
        bool IsConcealed(FogCell cell);
    }

    public sealed class FogCellMap : IFogElevationSource, IFogOcclusionSource, IFogConcealmentSource
    {
        private FogCellValue[] _values;
        private int _count;

        public FogCellMap(int initialCapacity = 16)
        {
            if (initialCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _values = new FogCellValue[initialCapacity];
        }

        public int Count => _count;

        public void SetHeightTier(FogCell cell, int heightTier)
        {
            ref FogCellValue value = ref GetOrCreate(cell);
            value.HeightTier = heightTier;
        }

        public void SetOpaque(FogCell cell, bool opaque)
        {
            ref FogCellValue value = ref GetOrCreate(cell);
            value.Opaque = opaque;
        }

        public void SetConcealed(FogCell cell, bool concealed)
        {
            ref FogCellValue value = ref GetOrCreate(cell);
            value.Concealed = concealed;
        }

        public int GetHeightTier(FogCell cell)
        {
            int index = FindIndex(cell);
            return index >= 0 ? _values[index].HeightTier : 0;
        }

        public bool IsOpaque(FogCell cell)
        {
            int index = FindIndex(cell);
            return index >= 0 && _values[index].Opaque;
        }

        public bool IsConcealed(FogCell cell)
        {
            int index = FindIndex(cell);
            return index >= 0 && _values[index].Concealed;
        }

        private ref FogCellValue GetOrCreate(FogCell cell)
        {
            int existing = FindIndex(cell);
            if (existing >= 0)
            {
                return ref _values[existing];
            }

            EnsureCapacity(_count + 1);
            int index = _count++;
            _values[index] = new FogCellValue(cell);
            return ref _values[index];
        }

        private int FindIndex(FogCell cell)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_values[i].Cell == cell)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _values.Length)
            {
                return;
            }

            int next = _values.Length;
            while (next < required)
            {
                next *= 2;
            }

            System.Array.Resize(ref _values, next);
        }

        private struct FogCellValue
        {
            public FogCellValue(FogCell cell)
            {
                Cell = cell;
                HeightTier = 0;
                Opaque = false;
                Concealed = false;
            }

            public FogCell Cell;
            public int HeightTier;
            public bool Opaque;
            public bool Concealed;
        }
    }

    public readonly struct VerticalVisionRule
    {
        public VerticalVisionRule(IFogElevationSource? elevation)
        {
            Elevation = elevation;
        }

        public readonly IFogElevationSource? Elevation;

        public bool Allows(FogCell targetCell, int emitterTier, in FogRulesPolicy policy)
        {
            if (!policy.VerticalEnabled || Elevation == null)
            {
                return true;
            }

            int targetTier = Elevation.GetHeightTier(targetCell);
            return targetTier <= emitterTier + policy.UpTolerance;
        }
    }

    public readonly struct LineOfSightRule
    {
        public LineOfSightRule(IFogOcclusionSource? occlusion)
        {
            Occlusion = occlusion;
        }

        public readonly IFogOcclusionSource? Occlusion;

        public bool Allows(FogCell from, FogCell to, in FogRulesPolicy policy)
        {
            if (!policy.LineOfSightEnabled || Occlusion == null)
            {
                return true;
            }

            int x0 = from.X;
            int y0 = from.Y;
            int x1 = to.X;
            int y1 = to.Y;
            int dx = MathUtil.Abs(x1 - x0);
            int dy = MathUtil.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0;
            int y = y0;

            while (x != x1 || y != y1)
            {
                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }

                if (x == x1 && y == y1)
                {
                    return true;
                }

                if (Occlusion.IsOpaque(new FogCell(x, y)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public readonly struct ConcealmentRule
    {
        public ConcealmentRule(IFogConcealmentSource? concealment)
        {
            Concealment = concealment;
        }

        public readonly IFogConcealmentSource? Concealment;

        public bool Allows(FogCell viewerCell, FogCell targetCell, bool trueSightActive, in FogProjectionPolicy policy)
        {
            if (!policy.ConcealmentEnabled || Concealment == null || !Concealment.IsConcealed(targetCell))
            {
                return true;
            }

            if (trueSightActive && policy.TrueSightRevealsConcealment)
            {
                return true;
            }

            int dx = MathUtil.Abs(viewerCell.X - targetCell.X);
            int dy = MathUtil.Abs(viewerCell.Y - targetCell.Y);
            return dx <= 1 && dy <= 1;
        }
    }
}
