using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Generators
{
    /// <summary>
    /// Solid circle (disc) grid of candidates within radius.
    /// Density = cellSizeCm, range scale = radiusCm.
    /// </summary>
    public sealed class CircleGenerator : IEqsGenerator
    {
        private readonly int _radiusCm;
        private readonly int _cellSizeCm;

        public CircleGenerator(int radiusCm, int cellSizeCm)
        {
            if (radiusCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            _radiusCm = radiusCm;
            _cellSizeCm = cellSizeCm;
        }

        public int Generate(WorldCmInt2 origin, Span<EqsItem> buffer)
        {
            int steps = _radiusCm / _cellSizeCm;
            long radiusSq = (long)_radiusCm * _radiusCm;
            int written = 0;

            for (int gy = -steps; gy <= steps && written < buffer.Length; gy++)
            {
                for (int gx = -steps; gx <= steps && written < buffer.Length; gx++)
                {
                    long dx = (long)gx * _cellSizeCm;
                    long dy = (long)gy * _cellSizeCm;
                    if (dx * dx + dy * dy > radiusSq)
                    {
                        continue;
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
