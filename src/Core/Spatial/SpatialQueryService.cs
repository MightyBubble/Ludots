using System;
using Arch.Core;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Delegate that resolves an entity's world position in integer centimeters.
    /// Required for fine-grained shape filtering (cone, rectangle, line).
    /// </summary>
    public delegate WorldCmInt2 EntityPositionProvider(Entity entity);

    public sealed class SpatialQueryService : ISpatialQueryService
    {
        private ISpatialQueryBackend _backend;
        private EntityPositionProvider? _positionProvider;
        private ISpatialCoordinateConverter? _coordConverter;
        private Map.Hex.HexMetrics? _hexMetrics;
        private ILoadedChunks? _loadedChunks;

        /// <summary>
        /// Reusable heap buffer for hex queries that exceed the stackalloc threshold.
        /// Avoids per-call GC allocation on hot paths with large radius.
        /// </summary>
        private HexCoordinates[] _hexBuffer = new HexCoordinates[512];

        private Span<HexCoordinates> GetOrGrowHexBuffer(int requiredCapacity)
        {
            if (_hexBuffer.Length < requiredCapacity)
                _hexBuffer = new HexCoordinates[requiredCapacity];
            return _hexBuffer.AsSpan(0, requiredCapacity);
        }

        public SpatialQueryService(ISpatialQueryBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public void SetBackend(ISpatialQueryBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>
        /// Set the position provider used for fine shape filtering.
        /// Must be set before calling QueryCone/QueryRectangle/QueryLine.
        /// </summary>
        public void SetPositionProvider(EntityPositionProvider provider)
        {
            _positionProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Set the coordinate converter used for hex spatial queries.
        /// Must be set before calling QueryHexRange/QueryHexRing.
        /// </summary>
        public void SetCoordinateConverter(ISpatialCoordinateConverter converter)
        {
            _coordConverter = converter ?? throw new ArgumentNullException(nameof(converter));
        }

        /// <summary>
        /// Set an optional ILoadedChunks reference. When set, queries may check
        /// whether entities belong to a loaded chunk (future optimization point).
        /// </summary>
        public void SetLoadedChunks(ILoadedChunks loadedChunks)
        {
            _loadedChunks = loadedChunks;
        }

        /// <summary>
        /// Set the hex metrics for parameterized hex cell bounding box calculations.
        /// If not set, falls back to the legacy HexCoordinates constants.
        /// </summary>
        public void SetHexMetrics(Map.Hex.HexMetrics metrics)
        {
            _hexMetrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
        {
            int count = _backend.QueryAabb(bounds, buffer, out int dropped);
            int unique = SpatialQueryPostProcessor.SortStableDedup(buffer.Slice(0, count));
            return new SpatialQueryResult(unique, dropped);
        }

        public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer)
        {
            // Coarse: AABB query via backend (bounding square)
            int count = _backend.QueryRadius(center, radiusCm, buffer, out int dropped);
            int unique = SpatialQueryPostProcessor.SortStableDedup(buffer.Slice(0, count));

            // Fine: true circular distance filter (requires position provider)
            if (_positionProvider is not null && unique > 0)
            {
                long r2 = (long)radiusCm * radiusCm;
                int write = 0;
                for (int i = 0; i < unique; i++)
                {
                    WorldCmInt2 pos = _positionProvider(buffer[i]);
                    long dx = pos.X - center.X;
                    long dy = pos.Y - center.Y;
                    if (dx * dx + dy * dy <= r2)
                        buffer[write++] = buffer[i];
                }
                return new SpatialQueryResult(write, dropped);
            }

            return new SpatialQueryResult(unique, dropped);
        }

        public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer)
        {
            if (_positionProvider is null)
                throw new InvalidOperationException("EntityPositionProvider not set. Call SetPositionProvider() before shape queries.");
            // Coarse: bounding circle → AABB
            var aabb = WorldAabbCm.FromCenterRadius(origin, rangeCm);
            int count = _backend.QueryAabb(aabb, buffer, out int dropped);
            int unique = SpatialQueryPostProcessor.SortStableDedup(buffer.Slice(0, count));
            // Fine: radius + angle test
            int write = 0;
            long r2 = (long)rangeCm * rangeCm;
            for (int i = 0; i < unique; i++)
            {
                WorldCmInt2 pos = _positionProvider!(buffer[i]);
                long dx = pos.X - origin.X;
                long dy = pos.Y - origin.Y;
                long dist2 = dx * dx + dy * dy;
                if (dist2 > r2) continue;
                if (dist2 > 0) // skip center-on-origin (always inside)
                {
                    int angleDeg = Atan2Deg((int)dy, (int)dx);
                    int diff = AngleDiffAbs(angleDeg, directionDeg);
                    if (diff > halfAngleDeg) continue;
                }
                buffer[write++] = buffer[i];
            }
            return new SpatialQueryResult(write, dropped);
        }

        public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
        {
            if (_positionProvider is null)
                throw new InvalidOperationException("EntityPositionProvider not set. Call SetPositionProvider() before shape queries.");
            // Coarse: bounding circle → AABB
            int boundingRadius = IntSqrt((long)halfWidthCm * halfWidthCm + (long)halfHeightCm * halfHeightCm) + 1;
            var aabb = WorldAabbCm.FromCenterRadius(center, boundingRadius);
            int count = _backend.QueryAabb(aabb, buffer, out int dropped);
            int unique = SpatialQueryPostProcessor.SortStableDedup(buffer.Slice(0, count));
            // Fine: rotate point into OBB local space, test against half-extents
            SinCosDeg(rotationDeg, out int sinR, out int cosR); // sin/cos * 1024
            int write = 0;
            for (int i = 0; i < unique; i++)
            {
                WorldCmInt2 pos = _positionProvider!(buffer[i]);
                long dx = pos.X - center.X;
                long dy = pos.Y - center.Y;
                // Rotate into local space (inverse rotation = negate angle → cos same, sin negated)
                long localX = (dx * cosR + dy * sinR) / 1024;
                long localY = (-dx * sinR + dy * cosR) / 1024;
                if (Math.Abs(localX) > halfWidthCm || Math.Abs(localY) > halfHeightCm) continue;
                buffer[write++] = buffer[i];
            }
            return new SpatialQueryResult(write, dropped);
        }

        public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
        {
            if (_positionProvider is null)
                throw new InvalidOperationException("EntityPositionProvider not set. Call SetPositionProvider() before shape queries.");
            // Build a bounding AABB for the capsule
            SinCosDeg(directionDeg, out int sinD, out int cosD); // * 1024
            int endX = origin.X + (int)((long)cosD * lengthCm / 1024);
            int endY = origin.Y + (int)((long)sinD * lengthCm / 1024);
            int minX = Math.Min(origin.X, endX) - halfWidthCm;
            int minY = Math.Min(origin.Y, endY) - halfWidthCm;
            int maxX = Math.Max(origin.X, endX) + halfWidthCm;
            int maxY = Math.Max(origin.Y, endY) + halfWidthCm;
            var aabb = new WorldAabbCm(minX, minY, maxX - minX, maxY - minY);
            int count = _backend.QueryAabb(aabb, buffer, out int dropped);
            int unique = SpatialQueryPostProcessor.SortStableDedup(buffer.Slice(0, count));

            // Fine: project point onto line segment, check perpendicular distance ≤ halfWidth
            // Line direction unit vector * 1024
            long dirX = cosD;  // already *1024
            long dirY = sinD;
            long lenSq1024 = (long)lengthCm * 1024; // length * 1024 (for projection)
            long hw2 = (long)halfWidthCm * halfWidthCm;
            int write = 0;
            for (int i = 0; i < unique; i++)
            {
                WorldCmInt2 pos = _positionProvider!(buffer[i]);
                long dx = pos.X - origin.X;
                long dy = pos.Y - origin.Y;
                // t = dot(d, dir) / length, with dir already *1024
                long dot = dx * dirX + dy * dirY; // result in cm*1024
                // Clamp projection t to [0, length*1024]
                if (dot < 0) dot = 0;
                else if (dot > lenSq1024) dot = lenSq1024;
                // Closest point on segment (in cm*1024 relative to origin)
                long cpX = dot * dirX / 1024; // cm*1024
                long cpY = dot * dirY / 1024; // cm*1024
                // Distance squared from point to closest point (convert dx/dy to *1024)
                long ex = dx * 1024 - cpX;
                long ey = dy * 1024 - cpY;
                long perpDist2 = (ex * ex + ey * ey) / (1024 * 1024);
                if (perpDist2 > hw2) continue;
                buffer[write++] = buffer[i];
            }
            return new SpatialQueryResult(write, dropped);
        }

        // ── Hex spatial queries ──

        public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer)
        {
            if (_coordConverter is null)
                throw new InvalidOperationException("ISpatialCoordinateConverter not set. Call SetCoordinateConverter() before hex queries.");

            if (hexRadius < 0) return new SpatialQueryResult(0, 0);

            int hexCount = HexCoordinates.RangeCount(hexRadius);
            Span<HexCoordinates> hexes = hexCount <= 512
                ? stackalloc HexCoordinates[hexCount]
                : GetOrGrowHexBuffer(hexCount);

            int written = HexCoordinates.GetRange(center, hexRadius, hexes);
            return QueryHexCells(hexes.Slice(0, written), buffer);
        }

        public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer)
        {
            if (_coordConverter is null)
                throw new InvalidOperationException("ISpatialCoordinateConverter not set. Call SetCoordinateConverter() before hex queries.");

            if (hexRadius < 0) return new SpatialQueryResult(0, 0);

            int hexCount = HexCoordinates.RingCount(hexRadius);
            Span<HexCoordinates> hexes = hexCount <= 512
                ? stackalloc HexCoordinates[hexCount]
                : GetOrGrowHexBuffer(hexCount);

            int written = HexCoordinates.GetRing(center, hexRadius, hexes);
            return QueryHexCells(hexes.Slice(0, written), buffer);
        }

        /// <summary>
        /// Core hex query: for each hex cell, build an AABB around its world-center and query the backend.
        /// Results are merged and deduped.
        /// </summary>
        private SpatialQueryResult QueryHexCells(Span<HexCoordinates> hexes, Span<Entity> buffer)
        {
            // Half-extents of one hex cell's bounding box (pointy-top)
            // Use HexMetrics if available, otherwise fall back to legacy constants
            int halfW, halfH;
            if (_hexMetrics is not null)
            {
                halfW = _hexMetrics.BoundingHalfWidthCm;
                halfH = _hexMetrics.BoundingHalfHeightCm;
            }
            else
            {
                halfW = (int)(HexCoordinates.EdgeLengthCm * 1.7320508f * 0.5f) + 1;
                halfH = HexCoordinates.EdgeLengthCm + 1;
            }

            int totalWritten = 0;
            int totalDropped = 0;

            for (int i = 0; i < hexes.Length; i++)
            {
                var worldCenter = _coordConverter!.HexToWorld(hexes[i]);
                var aabb = new WorldAabbCm(worldCenter.X - halfW, worldCenter.Y - halfH, halfW * 2, halfH * 2);
                var remaining = buffer.Slice(totalWritten);
                if (remaining.Length <= 0) break;

                int count = _backend.QueryAabb(aabb, remaining, out int dropped);
                totalWritten += count;
                totalDropped += dropped;
            }

            // Dedup across all hex cells
            int unique = SpatialQueryPostProcessor.SortStableDedup(buffer.Slice(0, totalWritten));
            return new SpatialQueryResult(unique, totalDropped);
        }

        // ── Deterministic integer trig helpers (fixed-point / lookup table, no float) ──

        private static readonly Fix64 _180 = Fix64.FromInt(180);

        /// <summary>Returns atan2 in degrees [0..360). Deterministic via Fix64Math.</summary>
        private static int Atan2Deg(int y, int x)
        {
            var fy = Fix64.FromInt(y);
            var fx = Fix64.FromInt(x);
            var rad = Fix64Math.Atan2Fast(fy, fx);
            int deg = (rad * _180 / Fix64.Pi).RoundToInt();
            if (deg < 0) deg += 360;
            return deg;
        }

        /// <summary>Absolute angular difference in [0..180].</summary>
        private static int AngleDiffAbs(int a, int b)
        {
            int d = Math.Abs(a - b) % 360;
            return d > 180 ? 360 - d : d;
        }

        /// <summary>sin and cos of angle in degrees, scaled by 1024. Deterministic via MathUtil lookup.</summary>
        private static void SinCosDeg(int deg, out int sin1024, out int cos1024)
        {
            // MathUtil.Sin/Cos return values scaled by 1000 (lookup table, fully deterministic)
            sin1024 = MathUtil.Sin(deg) * 1024 / 1000;
            cos1024 = MathUtil.Cos(deg) * 1024 / 1000;
        }

        /// <summary>Integer square root.</summary>
        private static int IntSqrt(long v)
        {
            if (v <= 0) return 0;
            long x = (long)Math.Sqrt(v);
            // Newton correction
            while (x * x > v) x--;
            while ((x + 1) * (x + 1) <= v) x++;
            return (int)x;
        }
    }
}
