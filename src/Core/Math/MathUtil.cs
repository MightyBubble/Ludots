using System;

namespace Ludots.Core.Mathematics
{
    public static class MathUtil
    {
        public const int ScalingFactor = 1000; // 1.0 = 1000

        /// <summary>
        /// Integer Square Root.
        /// </summary>
        public static int Sqrt(long value)
        {
            if (value < 0) return 0;
            if (value == 0) return 0;
            
            // Newton's method for integer sqrt
            long x = value;
            long y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + value / x) / 2;
            }
            return (int)x;
        }

        /// <summary>
        /// Returns the sine of the angle (in degrees) scaled by ScalingFactor (1000).
        /// Result range: [-1000, 1000]
        /// </summary>
        public static int Sin(int degrees)
        {
            // Normalize to 0-359
            degrees = degrees % 360;
            if (degrees < 0) degrees += 360;
            return _sinLut[degrees];
        }

        /// <summary>
        /// Returns the cosine of the angle (in degrees) scaled by ScalingFactor (1000).
        /// Result range: [-1000, 1000]
        /// </summary>
        public static int Cos(int degrees)
        {
            // Cos(x) = Sin(x + 90)
            return Sin(degrees + 90);
        }

        private static readonly int[] _sinLut;

        static MathUtil()
        {
            _sinLut = new int[360];
            for (int i = 0; i < 360; i++)
            {
                double angleRad = i * System.Math.PI / 180.0;
                _sinLut[i] = (int)(System.Math.Sin(angleRad) * ScalingFactor);
            }
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        
        public static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }

        /// <summary>
        /// Integer floor division: rounds towards negative infinity.
        /// Standard C# integer division truncates towards zero; this corrects for negative results.
        /// </summary>
        public static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r == 0) return q;
            bool neg = (value < 0) ^ (divisor < 0);
            return neg ? q - 1 : q;
        }

        /// <summary>
        /// Integer ceiling division: rounds towards positive infinity.
        /// </summary>
        public static int CeilDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r == 0) return q;
            bool pos = !((value < 0) ^ (divisor < 0));
            return pos ? q + 1 : q;
        }

        /// <summary>
        /// Converts a world-space AABB (in centimeters) to a cell-space IntRect.
        /// </summary>
        public static IntRect WorldAabbToCellRect(in WorldAabbCm aabb, int cellSizeCm)
        {
            int minX = FloorDiv(aabb.X, cellSizeCm);
            int minY = FloorDiv(aabb.Y, cellSizeCm);
            int maxXEx = CeilDiv(aabb.X + aabb.Width, cellSizeCm);
            int maxYEx = CeilDiv(aabb.Y + aabb.Height, cellSizeCm);
            return new IntRect(minX, minY, maxXEx - minX, maxYEx - minY);
        }
    }
}
