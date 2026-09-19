using System;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial;

public static class ScreenRegionBroadphase
{
    public static bool TryGetBounds(IScreenRayProvider rays, in ScreenRect rect, in WorldAabbCm worldBounds,
        float radiusCm, out WorldAabbCm bounds, float minHeightMeters = 0f, float maxHeightMeters = 0f)
    {
        if (!float.IsFinite(minHeightMeters) ||
            !float.IsFinite(maxHeightMeters) ||
            maxHeightMeters < minHeightMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeightMeters), "SPATIAL.ERR.InvalidHeightRange");
        }

        // Degenerate screen sides expand symmetrically to one pixel so adjacent corner
        // rays stay numerically separable; the caller's exact hit test keeps its own rect.
        float left = rect.MinX, right = rect.MaxX, top = rect.MinY, bottom = rect.MaxY;
        if (right <= left) { float mid = (left + right) * 0.5f; left = mid - 0.5f; right = mid + 0.5f; }
        if (bottom <= top) { float mid = (top + bottom) * 0.5f; top = mid - 0.5f; bottom = mid + 0.5f; }

        Span<ScreenRay> corners = stackalloc ScreenRay[4];
        corners[0] = rays.GetRay(new Vector2(left, top));
        corners[1] = rays.GetRay(new Vector2(right, top));
        corners[2] = rays.GetRay(new Vector2(right, bottom));
        corners[3] = rays.GetRay(new Vector2(left, bottom));
        ScreenRay center = rays.GetRay(new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f));

        // All clipping runs in double precision over camera-scale coordinates: the side
        // planes pass within meters of the ray origins while the world polygon spans
        // thousands of kilometers, and float cancellation tilts the planes unpredictably.
        Span<Pt2> polygon = stackalloc Pt2[16];
        Span<Pt2> output = stackalloc Pt2[16];
        polygon[0] = new Pt2(worldBounds.Left / 100.0, worldBounds.Top / 100.0);
        polygon[1] = new Pt2(worldBounds.Right / 100.0, worldBounds.Top / 100.0);
        polygon[2] = new Pt2(worldBounds.Right / 100.0, worldBounds.Bottom / 100.0);
        polygon[3] = new Pt2(worldBounds.Left / 100.0, worldBounds.Bottom / 100.0);
        int count = 4;
        for (int side = 0; side < 4; side++)
        {
            ScreenRay a = corners[side], b = corners[(side + 1) % 4];
            // Perspective rays meet at the camera, so the side plane normal is the cross
            // of the directions alone; origin sums would drown the direction signal.
            // Parallel (orthographic) rays instead bound the plane by their origin delta.
            (double nX, double nY, double nZ) = Cross(
                a.Direction.X, a.Direction.Y, a.Direction.Z,
                b.Direction.X, b.Direction.Y, b.Direction.Z);
            if (Length(nX, nY, nZ) <= 1e-12)
            {
                (nX, nY, nZ) = Cross(
                    a.Direction.X, a.Direction.Y, a.Direction.Z,
                    (double)b.Origin.X - a.Origin.X,
                    (double)b.Origin.Y - a.Origin.Y,
                    (double)b.Origin.Z - a.Origin.Z);
            }
            double length = Length(nX, nY, nZ);
            if (!double.IsFinite(length) || length <= 1e-12)
                throw new InvalidOperationException("SPATIAL.ERR.DegenerateScreenRays");
            nX /= length; nY /= length; nZ /= length;
            double offset = -(nX * a.Origin.X + nY * a.Origin.Y + nZ * a.Origin.Z);
            // Orient with a point far along the center ray: the near-plane origins sit
            // microns from the side planes, inside float storage error at large
            // coordinates, so their side-of-plane sign is pure noise for a 1px wedge.
            const double probeDistanceMeters = 10000.0;
            double insideDot =
                nX * ((double)center.Origin.X + probeDistanceMeters * center.Direction.X) +
                nY * ((double)center.Origin.Y + probeDistanceMeters * center.Direction.Y) +
                nZ * ((double)center.Origin.Z + probeDistanceMeters * center.Direction.Z);
            if (insideDot + offset < 0) { nX = -nX; nY = -nY; nZ = -nZ; offset = -offset; }
            // An intersecting projected bound has at least one vertex inside each side plane.
            // Expanding every plane by the bounding radius conservatively retains its origin.
            offset += (radiusCm + 1) / 100.0;
            double heightContribution = Math.Max(nY * minHeightMeters, nY * maxHeightMeters);
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                Pt2 p = polygon[i], q = polygon[(i + 1) % count];
                double dp = nX * p.X + nZ * p.Y + offset + heightContribution;
                double dq = nX * q.X + nZ * q.Y + offset + heightContribution;
                if (dp >= 0) output[written++] = p;
                if ((dp >= 0) != (dq >= 0)) output[written++] = Lerp(p, q, dp / (dp - dq));
            }
            count = written;
            output[..count].CopyTo(polygon);
            if (count == 0) { bounds = default; return false; }
        }
        Pt2 min = polygon[0], max = polygon[0];
        for (int i = 1; i < count; i++) { min = Min(min, polygon[i]); max = Max(max, polygon[i]); }
        int leftCm = checked((int)Math.Floor(min.X * 100)), topCm = checked((int)Math.Floor(min.Y * 100));
        bounds = new WorldAabbCm(leftCm, topCm,
            checked((int)Math.Ceiling(max.X * 100) - leftCm), checked((int)Math.Ceiling(max.Y * 100) - topCm));
        return true;
    }

    private readonly record struct Pt2(double X, double Y);

    private static (double X, double Y, double Z) Cross(
        double ax, double ay, double az, double bx, double by, double bz) =>
        (ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx);

    private static double Length(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);

    private static Pt2 Lerp(Pt2 p, Pt2 q, double t) => new(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);

    private static Pt2 Min(Pt2 a, Pt2 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));

    private static Pt2 Max(Pt2 a, Pt2 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}
