using System;
using Ludots.Core.Mathematics;

// FieldCellValue2D<T> and ChunkedField2D<T> live in Ludots.Core.Fields (same assembly, parent namespace).

namespace Ludots.Core.Fields.Influence
{
    /// <summary>
    /// Wraps ChunkedField2D&lt;float&gt; with influence-specific operations:
    /// stamp (radial falloff projection), sample, decay, and multi-field registry key.
    /// </summary>
    public sealed class InfluenceField
    {
        private readonly ChunkedField2D<float> _field;
        private readonly string _key;

        public InfluenceField(string key, FieldGridSpec2D grid, float defaultValue = 0f)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _field = new ChunkedField2D<float>(grid, defaultValue);
        }

        public string Key => _key;
        public FieldGridSpec2D Grid => _field.Grid;
        public int CellCount => _field.NonDefaultCount;
        public int CellSizeCm => _field.Grid.CellSizeCm;

        /// <summary>Sample influence value at world position. Returns default if outside written region.</summary>
        public float Sample(WorldCmInt2 world)
        {
            FieldCell2D cell = _field.WorldToCell(world);
            return _field.Get(cell);
        }

        /// <summary>Copy non-default cells into caller span (0-alloc warm path when capacity is sufficient).</summary>
        public int CopyNonDefaultCells(Span<FieldCellValue2D<float>> destination)
            => _field.CopyNonDefaultCells(destination);

        /// <summary>Project radial influence centered at <paramref name="center"/> with peak value and falloff.</summary>
        public void Stamp(WorldCmInt2 center, int radiusCm, float peak, FalloffKind falloff)
        {
            if (radiusCm <= 0 || peak == 0f)
            {
                return;
            }

            FieldCell2D centerCell = _field.WorldToCell(center);
            int cellRadius = (radiusCm + _field.Grid.CellSizeCm - 1) / _field.Grid.CellSizeCm; // ceil
            long radiusSq = (long)radiusCm * radiusCm;

            for (int dy = -cellRadius; dy <= cellRadius; dy++)
            {
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    FieldCell2D cell = new FieldCell2D(centerCell.X + dx, centerCell.Y + dy);
                    WorldCmInt2 cellWorld = _field.CellCenterToWorld(cell);
                    long deltaX = cellWorld.X - center.X;
                    long deltaY = cellWorld.Y - center.Y;
                    long distSq = deltaX * deltaX + deltaY * deltaY;

                    if (distSq > radiusSq)
                    {
                        continue; // outside stamp radius
                    }

                    float distCm = (float)Math.Sqrt(distSq);
                    float ratio = distCm / radiusCm;
                    float influence = falloff switch
                    {
                        FalloffKind.Constant => peak,
                        FalloffKind.Linear => peak * Math.Max(0f, 1f - ratio),
                        FalloffKind.Quadratic => peak * (float)Math.Pow(Math.Max(0f, 1f - ratio), 2.0),
                        _ => throw new ArgumentOutOfRangeException(nameof(falloff), falloff, "Unknown FalloffKind.")
                    };

                    float current = _field.Get(cell);
                    _field.Set(cell, current + influence); // additive
                }
            }
        }

        /// <summary>
        /// Multiply all non-default cells by <paramref name="factor"/> (time decay).
        /// SoA in-place via ChunkedField2D.ScaleNonDefault; 0-alloc warm path.
        /// </summary>
        public void Decay(float factor)
        {
            _field.ScaleNonDefault(factor);
        }

        /// <summary>Clear all influence values to default.</summary>
        public void Clear()
        {
            _field.Clear();
        }
    }
}
