using System;

namespace Ludots.Core.Map.Hex
{
    /// <summary>
    /// Runtime-configurable hex grid geometry parameters.
    /// Replaces the hardcoded constants in HexCoordinates for per-map hex configurations.
    /// All values are in centimeters (integer) to maintain determinism.
    /// </summary>
    public sealed class HexMetrics
    {
        /// <summary>Edge length of a hex cell in centimeters.</summary>
        public int EdgeLengthCm { get; }

        /// <summary>Edge length in meters (float, for rendering).</summary>
        public float EdgeLength { get; }

        /// <summary>Width of one hex (pointy-top): sqrt(3) * EdgeLength. In meters.</summary>
        public float HexWidth { get; }

        /// <summary>Height of one hex (pointy-top): 2 * EdgeLength. In meters.</summary>
        public float HexHeight { get; }

        /// <summary>Horizontal distance between adjacent column centers. In meters.</summary>
        public float ColSpacing { get; }

        /// <summary>Vertical distance between adjacent row centers: 1.5 * EdgeLength. In meters.</summary>
        public float RowSpacing { get; }

        /// <summary>Width of one hex in centimeters (integer-approximate).</summary>
        public int HexWidthCm { get; }

        /// <summary>Row spacing in centimeters (integer-approximate).</summary>
        public int RowSpacingCm { get; }

        private const float Sqrt3 = 1.7320508f;

        /// <summary>
        /// Default metrics matching legacy HexCoordinates constants (EdgeLength = 4m = 400cm).
        /// </summary>
        public static HexMetrics Default { get; } = new HexMetrics(400);

        public HexMetrics(int edgeLengthCm)
        {
            if (edgeLengthCm <= 0) throw new ArgumentOutOfRangeException(nameof(edgeLengthCm));
            EdgeLengthCm = edgeLengthCm;
            EdgeLength = edgeLengthCm / 100f;
            HexWidth = Sqrt3 * EdgeLength;
            HexHeight = 2f * EdgeLength;
            ColSpacing = HexWidth;
            RowSpacing = 1.5f * EdgeLength;
            HexWidthCm = (int)(Sqrt3 * edgeLengthCm + 0.5f);
            RowSpacingCm = edgeLengthCm * 3 / 2; // integer exact for multiples of 2
        }

        /// <summary>
        /// Convert hex axial coordinates to world position in centimeters (float Vector3, Y=0).
        /// </summary>
        public System.Numerics.Vector3 HexToWorldCm(int q, int r)
        {
            float x = EdgeLengthCm * Sqrt3 * (q + r / 2.0f);
            float z = EdgeLengthCm * 1.5f * r;
            return new System.Numerics.Vector3(x, 0, z);
        }

        /// <summary>
        /// Convert world position in centimeters to the nearest hex axial coordinate.
        /// </summary>
        public HexCoordinates WorldCmToHex(float xCm, float zCm)
        {
            float q = (Sqrt3 / 3.0f * xCm - 1.0f / 3.0f * zCm) / EdgeLengthCm;
            float r = (2.0f / 3.0f * zCm) / EdgeLengthCm;
            return HexCoordinates.Round(q, r);
        }

        /// <summary>
        /// Half-width of a hex bounding box in centimeters (for spatial queries).
        /// </summary>
        public int BoundingHalfWidthCm => (int)(EdgeLengthCm * Sqrt3 * 0.5f) + 1;

        /// <summary>
        /// Half-height of a hex bounding box in centimeters (for spatial queries).
        /// </summary>
        public int BoundingHalfHeightCm => EdgeLengthCm + 1;
    }
}
