using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public enum RegionVolumeShapeKind : byte
    {
        Circle = 0,
        Rect = 1,
        Polygon = 2,
        Segment = 3,
    }

    /// <summary>
    /// Containment geometry of a region volume in coordinates local to the volume
    /// entity's <c>WorldPositionCm</c> anchor. All shapes are inclusive on the
    /// boundary: a position exactly at radius / half-extent / polygon edge /
    /// capsule wall counts as inside.
    /// </summary>
    public struct RegionVolumeShape
    {
        public RegionVolumeShapeKind Kind;

        public Fix64 Radius;

        public Fix64 HalfWidth;
        public Fix64 HalfHeight;

        /// <summary>Convex polygon vertices local to the anchor; validated convex at parse.</summary>
        public Fix64Vec2[]? PolygonPoints;

        public Fix64Vec2 SegmentA;
        public Fix64Vec2 SegmentB;
        public Fix64 HalfThickness;

        public bool Contains(Fix64Vec2 worldPoint, Fix64Vec2 anchor)
        {
            Fix64Vec2 local = worldPoint - anchor;
            switch (Kind)
            {
                case RegionVolumeShapeKind.Circle:
                {
                    Fix64 dx = local.X;
                    Fix64 dy = local.Y;
                    return dx * dx + dy * dy <= Radius * Radius;
                }

                case RegionVolumeShapeKind.Rect:
                    return Fix64.Abs(local.X) <= HalfWidth &&
                           Fix64.Abs(local.Y) <= HalfHeight;

                case RegionVolumeShapeKind.Polygon:
                    return ContainsConvexPolygon(local);

                case RegionVolumeShapeKind.Segment:
                    return ContainsSegment(local);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Convex containment by edge cross-sign consistency: a point inside or on a
        /// convex polygon leaves every edge cross product with the same sign (or zero
        /// on the edge). Authoring winding may be CW or CCW.
        /// </summary>
        private bool ContainsConvexPolygon(Fix64Vec2 local)
        {
            Fix64Vec2[] points = PolygonPoints!;
            bool sawPositive = false;
            bool sawNegative = false;
            for (int i = 0; i < points.Length; i++)
            {
                Fix64Vec2 a = points[i];
                Fix64Vec2 b = points[(i + 1) % points.Length];
                Fix64 cross = (b.X - a.X) * (local.Y - a.Y) - (b.Y - a.Y) * (local.X - a.X);
                if (cross < Fix64.Zero)
                {
                    sawNegative = true;
                }
                else if (cross > Fix64.Zero)
                {
                    sawPositive = true;
                }

                if (sawPositive && sawNegative)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Capsule containment: squared point-to-segment distance vs half-thickness.</summary>
        private bool ContainsSegment(Fix64Vec2 local)
        {
            return PointSegmentDistanceSq(local, SegmentA, SegmentB) <= HalfThickness * HalfThickness;
        }

        /// <summary>
        /// Swept crossing (#1475): does the travel segment [from, to] (world coords)
        /// pass through the volume? Closes the think-wave sampling gap for fast
        /// movers and teleports; boundary-inclusive like containment.
        /// </summary>
        public bool IntersectsPath(Fix64Vec2 from, Fix64Vec2 to, Fix64Vec2 anchor)
        {
            Fix64Vec2 a = from - anchor;
            Fix64Vec2 b = to - anchor;
            switch (Kind)
            {
                case RegionVolumeShapeKind.Circle:
                    return PointSegmentDistanceSq(Fix64Vec2.Zero, a, b) <= Radius * Radius;

                case RegionVolumeShapeKind.Rect:
                    return SegmentTouchesQuadEdges(a, b, HalfWidth, HalfHeight);

                case RegionVolumeShapeKind.Polygon:
                    return SegmentTouchesConvexEdges(a, b, PolygonPoints!);

                case RegionVolumeShapeKind.Segment:
                    return SegmentSegmentDistanceSq(a, b, SegmentA, SegmentB)
                        <= HalfThickness * HalfThickness;

                default:
                    return false;
            }
        }

        private static bool SegmentTouchesQuadEdges(Fix64Vec2 a, Fix64Vec2 b, Fix64 halfWidth, Fix64 halfHeight)
        {
            Fix64 w = halfWidth;
            Fix64 h = halfHeight;
            return SegmentsTouch(a, b, new Fix64Vec2(-w, -h), new Fix64Vec2(w, -h)) ||
                   SegmentsTouch(a, b, new Fix64Vec2(w, -h), new Fix64Vec2(w, h)) ||
                   SegmentsTouch(a, b, new Fix64Vec2(w, h), new Fix64Vec2(-w, h)) ||
                   SegmentsTouch(a, b, new Fix64Vec2(-w, h), new Fix64Vec2(-w, -h));
        }

        private static bool SegmentTouchesConvexEdges(Fix64Vec2 a, Fix64Vec2 b, Fix64Vec2[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                if (SegmentsTouch(a, b, points[i], points[(i + 1) % points.Length]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Touching counts (boundary-inclusive), matching containment semantics.</summary>
        private static bool SegmentsTouch(Fix64Vec2 a1, Fix64Vec2 a2, Fix64Vec2 b1, Fix64Vec2 b2)
        {
            return SegmentSegmentDistanceSq(a1, a2, b1, b2) == Fix64.Zero;
        }

        private static Fix64 PointSegmentDistanceSq(Fix64Vec2 p, Fix64Vec2 a, Fix64Vec2 b)
        {
            Fix64Vec2 ab = b - a;
            Fix64Vec2 ap = p - a;
            Fix64 denominator = ab.X * ab.X + ab.Y * ab.Y;
            Fix64 t = Fix64.Zero;
            if (denominator > Fix64.Zero)
            {
                t = (ap.X * ab.X + ap.Y * ab.Y) / denominator;
                if (t < Fix64.Zero)
                {
                    t = Fix64.Zero;
                }
                else if (t > Fix64.OneValue)
                {
                    t = Fix64.OneValue;
                }
            }

            Fix64Vec2 closest = a + t * ab;
            Fix64Vec2 delta = p - closest;
            return delta.X * delta.X + delta.Y * delta.Y;
        }

        /// <summary>
        /// Squared distance between two segments, exact and overflow-safe: a
        /// straddle-test decides intersection with bounded cross products (the naive
        /// clamped-parametrization denominator is a product of length-squares and
        /// overflows Q32.32 at ordinary map scale), and for non-intersecting 2D
        /// segments the closest point always lies on an endpoint of one of them.
        /// Touching segments yield exactly zero.
        /// </summary>
        private static Fix64 SegmentSegmentDistanceSq(Fix64Vec2 a1, Fix64Vec2 a2, Fix64Vec2 b1, Fix64Vec2 b2)
        {
            if (SegmentsIntersect(a1, a2, b1, b2))
            {
                return Fix64.Zero;
            }

            Fix64 best = PointSegmentDistanceSq(a1, b1, b2);
            Fix64 tail = PointSegmentDistanceSq(a2, b1, b2);
            if (tail < best)
            {
                best = tail;
            }

            Fix64 fromB1 = PointSegmentDistanceSq(b1, a1, a2);
            if (fromB1 < best)
            {
                best = fromB1;
            }

            Fix64 fromB2 = PointSegmentDistanceSq(b2, a1, a2);
            if (fromB2 < best)
            {
                best = fromB2;
            }

            return best;
        }

        private static bool SegmentsIntersect(Fix64Vec2 a1, Fix64Vec2 a2, Fix64Vec2 b1, Fix64Vec2 b2)
        {
            Fix64 d1 = Cross(a1, a2, b1);
            Fix64 d2 = Cross(a1, a2, b2);
            Fix64 d3 = Cross(b1, b2, a1);
            Fix64 d4 = Cross(b1, b2, a2);

            if (((d1 > Fix64.Zero && d2 < Fix64.Zero) || (d1 < Fix64.Zero && d2 > Fix64.Zero)) &&
                ((d3 > Fix64.Zero && d4 < Fix64.Zero) || (d3 < Fix64.Zero && d4 > Fix64.Zero)))
            {
                return true;
            }

            if (d1 == Fix64.Zero && OnSegment(a1, b1, a2))
            {
                return true;
            }

            if (d2 == Fix64.Zero && OnSegment(a1, b2, a2))
            {
                return true;
            }

            if (d3 == Fix64.Zero && OnSegment(b1, a1, b2))
            {
                return true;
            }

            return d4 == Fix64.Zero && OnSegment(b1, a2, b2);
        }

        private static Fix64 Cross(Fix64Vec2 o, Fix64Vec2 a, Fix64Vec2 b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>Collinear containment: q between inclusive p and r.</summary>
        private static bool OnSegment(Fix64Vec2 p, Fix64Vec2 q, Fix64Vec2 r)
        {
            return q.X >= Fix64.Min(p.X, r.X) && q.X <= Fix64.Max(p.X, r.X) &&
                   q.Y >= Fix64.Min(p.Y, r.Y) && q.Y <= Fix64.Max(p.Y, r.Y);
        }
    }

    /// <summary>
    /// Marks an entity as one materialized or runtime-spawned region volume. The
    /// entity's <c>WorldPositionCm</c> is the volume anchor; geometry is local to it.
    /// </summary>
    public struct RegionVolumeCm
    {
        public string VolumeKey;
        public RegionVolumeShape Shape;
    }

    /// <summary>Any-of GameplayTag mover filter; absence tracks every positioned entity.</summary>
    public struct RegionVolumeTagFilterCm
    {
        public GameplayTagContainer Filter;
    }

    public enum RegionVolumePayloadValueType : byte
    {
        Int = 0,
        Float = 1,
        String = 2,
    }

    /// <summary>One authored static payload entry; keys must be declared params of the emitted event schema.</summary>
    public struct RegionVolumePayloadEntry
    {
        public string Key;
        public RegionVolumePayloadValueType Type;
        public int IntValue;
        public float FloatValue;
        public string? StringValue;
    }

    /// <summary>
    /// Emission contract override; resolved so both events are always set (an
    /// authored side defaults to the engine event). Absence on a volume entity means
    /// the engine defaults: <see cref="GameEvents.RegionEntered"/> /
    /// <see cref="GameEvents.RegionExited"/> with the standard crossing payload.
    /// </summary>
    public struct RegionVolumeEmissionCm
    {
        public EventKey EnterEvent;
        public EventKey ExitEvent;
        public RegionVolumePayloadEntry[]? Payload;
    }
}
