using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Generators
{
    /// <summary>
    /// Ring of candidates at fixed radius. Range scale = radiusCm, density = point count.
    /// Mirrors Unreal's "Points: Ring" generator.
    /// </summary>
    public sealed class RingGenerator : IEqsGenerator
    {
        private readonly int _radiusCm;
        private readonly int _count;

        public RingGenerator(int radiusCm, int count)
        {
            if (radiusCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _radiusCm = radiusCm;
            _count = count;
        }

        public int Generate(WorldCmInt2 origin, Span<EqsItem> buffer)
        {
            int n = Math.Min(_count, buffer.Length);
            for (int i = 0; i < n; i++)
            {
                // Deterministic angle sampling: evenly spaced around full circle.
                double angle = 2.0 * Math.PI * i / _count;
                int dx = (int)Math.Round(Math.Cos(angle) * _radiusCm);
                int dy = (int)Math.Round(Math.Sin(angle) * _radiusCm);
                buffer[i] = new EqsItem(new WorldCmInt2(origin.X + dx, origin.Y + dy));
            }

            return n;
        }
    }
}
