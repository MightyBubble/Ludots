using System;

namespace Ludots.Core.Navigation.NavMesh.Geometry
{
    /// <summary>
    /// Shared exact Int128 2D predicates for constrained triangulation (CDT / layered-span).
    /// </summary>
    public static class ExactPredicates2D
    {
        public const long DemonstrableLocalAbsDeltaCm = 1L << 30;

        public static Int128 Orient2(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Int128 bxL = (Int128)bx - ax;
            Int128 bzL = (Int128)bz - az;
            Int128 cxL = (Int128)cx - ax;
            Int128 czL = (Int128)cz - az;
            CheckLocalDelta(bxL, "Orient2");
            CheckLocalDelta(bzL, "Orient2");
            CheckLocalDelta(cxL, "Orient2");
            CheckLocalDelta(czL, "Orient2");
            return (bxL * czL) - (bzL * cxL);
        }

        public static int Orient2Sign(int ax, int az, int bx, int bz, int cx, int cz)
        {
            Int128 v = Orient2(ax, az, bx, bz, cx, cz);
            if (v > 0) return 1;
            if (v < 0) return -1;
            return 0;
        }

        public static int InCircleSign(int ax, int az, int bx, int bz, int cx, int cz, int dx, int dz)
        {
            Int128 adx = (Int128)ax - dx;
            Int128 adz = (Int128)az - dz;
            Int128 bdx = (Int128)bx - dx;
            Int128 bdz = (Int128)bz - dz;
            Int128 cdx = (Int128)cx - dx;
            Int128 cdz = (Int128)cz - dz;
            CheckLocalDelta(adx, "InCircle");
            CheckLocalDelta(adz, "InCircle");
            CheckLocalDelta(bdx, "InCircle");
            CheckLocalDelta(bdz, "InCircle");
            CheckLocalDelta(cdx, "InCircle");
            CheckLocalDelta(cdz, "InCircle");

            Int128 abdet = (adx * bdz) - (bdx * adz);
            Int128 bcdet = (bdx * cdz) - (cdx * bdz);
            Int128 cadet = (cdx * adz) - (adx * cdz);
            Int128 alift = (adx * adx) + (adz * adz);
            Int128 blift = (bdx * bdx) + (bdz * bdz);
            Int128 clift = (cdx * cdx) + (cdz * cdz);
            Int128 det = (alift * bcdet) + (blift * cadet) + (clift * abdet);
            if (det > 0) return 1;
            if (det < 0) return -1;
            return 0;
        }

        public static bool PointInTriangleStrict(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            Int128 o1 = Orient2(ax, az, bx, bz, px, pz);
            Int128 o2 = Orient2(bx, bz, cx, cz, px, pz);
            Int128 o3 = Orient2(cx, cz, ax, az, px, pz);
            return o1 > 0 && o2 > 0 && o3 > 0;
        }

        public static bool PointOnSegmentInclusive(int ax, int az, int bx, int bz, int px, int pz)
        {
            if (Orient2Sign(ax, az, bx, bz, px, pz) != 0)
            {
                return false;
            }

            int minX = ax < bx ? ax : bx;
            int maxX = ax < bx ? bx : ax;
            int minZ = az < bz ? az : bz;
            int maxZ = az < bz ? bz : az;
            return px >= minX && px <= maxX && pz >= minZ && pz <= maxZ;
        }

        public static void CheckLocalDelta(Int128 delta, string owner)
        {
            Int128 abs = delta < 0 ? -delta : delta;
            if (abs > DemonstrableLocalAbsDeltaCm)
            {
                throw new InvalidOperationException(
                    $"ExactPredicates2D predicate local delta exceeds DemonstrableLocalAbsDeltaCm; owner {owner}.");
            }
        }
    }
}
