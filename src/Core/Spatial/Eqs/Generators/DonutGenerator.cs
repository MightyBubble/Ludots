using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Generators
{
    /// <summary>
    /// Donut (annulus) grid of candidates between inner and outer radius.
    /// Density = cellSizeCm, range scale = [innerCm, outerCm].
    /// Mirrors Unreal's "Points: Donut" generator.
    /// </summary>
    public sealed class DonutGenerator : IEqsGenerator
    {
        private readonly int _innerCm;
        private readonly int _outerCm;
        private readonly int _cellSizeCm;

        public DonutGenerator(int innerCm, int outerCm, int cellSizeCm)
        {
            if (innerCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(innerCm));
            }

            if (outerCm <= innerCm)
            {
                throw new ArgumentException("outerCm must be greater than innerCm.", nameof(outerCm));
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            _innerCm = innerCm;
            _outerCm = outerCm;
            _cellSizeCm = cellSizeCm;
        }

        public int Generate(WorldCmInt2 origin, Span<EqsItem> buffer)
        {
            int steps = _outerCm / _cellSizeCm;
            long innerSq = (long)_innerCm * _innerCm;
            long outerSq = (long)_outerCm * _outerCm;
            int written = 0;

            for (int gy = -steps; gy <= steps && written < buffer.Length; gy++)
            {
                for (int gx = -steps; gx <= steps && written < buffer.Length; gx++)
                {
                    long dx = (long)gx * _cellSizeCm;
                    long dy = (long)gy * _cellSizeCm;
                    long distSq = dx * dx + dy * dy;

                    if (distSq < innerSq || distSq > outerSq)
                    {
                        continue; // outside annulus band
                    }

                    WorldCmInt2 pos = new WorldCmInt2(
                        origin.X + (int)dx,
                        origin.Y + (int)dy);
                    buffer[written++] = new EqsItem(pos);
                }
            }

            return written;
        }
    }
}
