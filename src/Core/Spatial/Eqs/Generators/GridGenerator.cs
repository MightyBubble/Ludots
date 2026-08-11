using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Generators
{
    /// <summary>
    /// Square grid of candidates. Density = cellSizeCm (spacing), range scale = extentCm (half-width).
    /// Produces a (2*extent/cell + 1)^2 grid centered on origin.
    /// </summary>
    public sealed class GridGenerator : IEqsGenerator
    {
        private readonly int _extentCm;
        private readonly int _cellSizeCm;

        public GridGenerator(int extentCm, int cellSizeCm)
        {
            if (extentCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(extentCm));
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            _extentCm = extentCm;
            _cellSizeCm = cellSizeCm;
        }

        public int Generate(WorldCmInt2 origin, Span<EqsItem> buffer)
        {
            int steps = _extentCm / _cellSizeCm;
            int written = 0;

            for (int gy = -steps; gy <= steps && written < buffer.Length; gy++)
            {
                for (int gx = -steps; gx <= steps && written < buffer.Length; gx++)
                {
                    WorldCmInt2 pos = new WorldCmInt2(
                        origin.X + gx * _cellSizeCm,
                        origin.Y + gy * _cellSizeCm);
                    buffer[written++] = new EqsItem(pos);
                }
            }

            return written;
        }
    }
}
