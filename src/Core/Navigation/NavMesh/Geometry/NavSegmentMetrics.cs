using System;

namespace Ludots.Core.Navigation.NavMesh.Geometry
{
    /// <summary>
    /// Overflow-proof segment metrics for nav portal clearance and related bake arithmetic.
    /// </summary>
    public static class NavSegmentMetrics
    {
        public static int RoundEuclideanLengthCm(int ax, int ay, int az, int bx, int by, int bz)
            => LengthCmRounded(ax, ay, az, bx, by, bz);

        public static int LengthCmRounded(int ax, int ay, int az, int bx, int by, int bz)
        {
            long dx = (long)bx - ax;
            long dy = (long)by - ay;
            long dz = (long)bz - az;
            Int128 lenSq = ((Int128)dx * dx) + ((Int128)dy * dy) + ((Int128)dz * dz);
            if (lenSq == 0)
            {
                return 0;
            }

            Int128 root = Int128SqrtFloor(lenSq);
            Int128 remainder = lenSq - (root * root);
            Int128 twiceRootPlusOne = (root * 2) + 1;
            if (remainder * 2 >= twiceRootPlusOne)
            {
                root += 1;
            }

            if (root > int.MaxValue)
            {
                throw new OverflowException(
                    $"Segment length overflows int centimetres for endpoints ({ax},{ay},{az})-({bx},{by},{bz}).");
            }

            return (int)root;
        }

        private static Int128 Int128SqrtFloor(Int128 value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Cannot take square root of a negative value.");
            }

            if (value <= 1)
            {
                return value;
            }

            Int128 low = 1;
            Int128 high = value;
            while (low < high)
            {
                Int128 mid = (low + high + 1) / 2;
                if (mid > value / mid)
                {
                    high = mid - 1;
                }
                else
                {
                    low = mid;
                }
            }

            return low;
        }
    }
}
