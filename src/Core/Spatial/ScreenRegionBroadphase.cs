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

        Span<ScreenRay> corners = stackalloc ScreenRay[4];
        float right = MathF.Max(rect.MaxX, rect.MinX + 0.001f);
        float bottom = MathF.Max(rect.MaxY, rect.MinY + 0.001f);
        corners[0] = rays.GetRay(new Vector2(rect.MinX, rect.MinY));
        corners[1] = rays.GetRay(new Vector2(right, rect.MinY));
        corners[2] = rays.GetRay(new Vector2(right, bottom));
        corners[3] = rays.GetRay(new Vector2(rect.MinX, bottom));
        ScreenRay center = rays.GetRay(new Vector2((rect.MinX + right) / 2, (rect.MinY + bottom) / 2));
        Vector3 inside = center.Origin + center.Direction;
        Span<Vector2> polygon = stackalloc Vector2[16];
        Span<Vector2> output = stackalloc Vector2[16];
        polygon[0] = new Vector2(worldBounds.Left, worldBounds.Top) / 100;
        polygon[1] = new Vector2(worldBounds.Right, worldBounds.Top) / 100;
        polygon[2] = new Vector2(worldBounds.Right, worldBounds.Bottom) / 100;
        polygon[3] = new Vector2(worldBounds.Left, worldBounds.Bottom) / 100;
        int count = 4;
        for (int side = 0; side < 4; side++)
        {
            ScreenRay a = corners[side], b = corners[(side + 1) % 4];
            Vector3 normal = Vector3.Cross(a.Direction, b.Origin + b.Direction - a.Origin);
            float length = normal.Length();
            if (!float.IsFinite(length) || length <= 1e-12f)
                throw new InvalidOperationException("SPATIAL.ERR.DegenerateScreenRays");
            normal /= length;
            float offset = -Vector3.Dot(normal, a.Origin);
            if (Vector3.Dot(normal, inside) + offset < 0) { normal = -normal; offset = -offset; }
            // An intersecting projected bound has at least one vertex inside each side plane.
            // Expanding every plane by the bounding radius conservatively retains its origin.
            offset += (radiusCm + 1) / 100;
            float heightContribution = MathF.Max(normal.Y * minHeightMeters, normal.Y * maxHeightMeters);
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = polygon[i], q = polygon[(i + 1) % count];
                float dp = normal.X * p.X + normal.Z * p.Y + offset + heightContribution;
                float dq = normal.X * q.X + normal.Z * q.Y + offset + heightContribution;
                if (dp >= 0) output[written++] = p;
                if ((dp >= 0) != (dq >= 0)) output[written++] = Vector2.Lerp(p, q, dp / (dp - dq));
            }
            count = written;
            output[..count].CopyTo(polygon);
            if (count == 0) { bounds = default; return false; }
        }
        Vector2 min = polygon[0], max = polygon[0];
        for (int i = 1; i < count; i++) { min = Vector2.Min(min, polygon[i]); max = Vector2.Max(max, polygon[i]); }
        int left = checked((int)MathF.Floor(min.X * 100)), top = checked((int)MathF.Floor(min.Y * 100));
        bounds = new WorldAabbCm(left, top,
            checked((int)MathF.Ceiling(max.X * 100) - left), checked((int)MathF.Ceiling(max.Y * 100) - top));
        return true;
    }
}
