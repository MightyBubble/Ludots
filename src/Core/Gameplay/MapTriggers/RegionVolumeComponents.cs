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
                    return DistanceSq(local) <= Square(Radius);

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
                double cross = Cross(a, b, local);
                if (cross < 0d)
                {
                    sawNegative = true;
                }
                else if (cross > 0d)
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
            return PointSegmentDistanceSq(local, SegmentA, SegmentB) <= Square(HalfThickness);
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
                    return PointSegmentDistanceSq(Fix64Vec2.Zero, a, b) <= Square(Radius);

                case RegionVolumeShapeKind.Rect:
                    return SegmentTouchesQuadEdges(a, b, HalfWidth, HalfHeight);

                case RegionVolumeShapeKind.Polygon:
                    return SegmentTouchesConvexEdges(a, b, PolygonPoints!);

                case RegionVolumeShapeKind.Segment:
                    return SegmentSegmentDistanceSq(a, b, SegmentA, SegmentB)
                        <= Square(HalfThickness);

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
            return SegmentSegmentDistanceSq(a1, a2, b1, b2) == 0d;
        }

        /// <summary>
        /// Squared distance from <paramref name="p"/> to segment <paramref name="a"/>–<paramref name="b"/>.
        /// The clamp parameter and the square both stay in double: a Q31.32 product
        /// wraps once a length passes about 46340 centimeters, and the wrapped square
        /// then compares as if the point sat near the origin.
        /// </summary>
        private static double PointSegmentDistanceSq(Fix64Vec2 p, Fix64Vec2 a, Fix64Vec2 b)
        {
            double ax = a.X.ToDouble();
            double ay = a.Y.ToDouble();
            double abx = b.X.ToDouble() - ax;
            double aby = b.Y.ToDouble() - ay;
            double apx = p.X.ToDouble() - ax;
            double apy = p.Y.ToDouble() - ay;
            double denominator = abx * abx + aby * aby;
            double t = 0d;
            if (denominator > 0d)
            {
                t = (apx * abx + apy * aby) / denominator;
                if (t < 0d)
                {
                    t = 0d;
                }
                else if (t > 1d)
                {
                    t = 1d;
                }
            }

            double dx = p.X.ToDouble() - (ax + t * abx);
            double dy = p.Y.ToDouble() - (ay + t * aby);
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Squared distance between two segments. A straddle test decides intersection
        /// because the clamped-parameter denominator is a product of length-squares;
        /// touching segments yield exactly zero. Otherwise the closest point of two
        /// non-crossing 2D segments lies on an endpoint, and that square is computed
        /// in double so a separation past about 46340 centimeters cannot wrap.
        /// </summary>
        private static double SegmentSegmentDistanceSq(Fix64Vec2 a1, Fix64Vec2 a2, Fix64Vec2 b1, Fix64Vec2 b2)
        {
            if (SegmentsIntersect(a1, a2, b1, b2))
            {
                return 0d;
            }

            double best = PointSegmentDistanceSq(a1, b1, b2);
            double tail = PointSegmentDistanceSq(a2, b1, b2);
            if (tail < best)
            {
                best = tail;
            }

            double fromB1 = PointSegmentDistanceSq(b1, a1, a2);
            if (fromB1 < best)
            {
                best = fromB1;
            }

            double fromB2 = PointSegmentDistanceSq(b2, a1, a2);
            if (fromB2 < best)
            {
                best = fromB2;
            }

            return best;
        }

        /// <summary>
        /// Squared length in double. A Q31.32 product wraps once the length passes
        /// about 46340 centimeters, and the wrapped square compares as near the origin.
        /// </summary>
        private static double DistanceSq(Fix64Vec2 value)
        {
            double x = value.X.ToDouble();
            double y = value.Y.ToDouble();
            return x * x + y * y;
        }

        private static double Square(Fix64 value)
        {
            double v = value.ToDouble();
            return v * v;
        }

        private static bool SegmentsIntersect(Fix64Vec2 a1, Fix64Vec2 a2, Fix64Vec2 b1, Fix64Vec2 b2)
        {
            double d1 = Cross(a1, a2, b1);
            double d2 = Cross(a1, a2, b2);
            double d3 = Cross(b1, b2, a1);
            double d4 = Cross(b1, b2, a2);

            if (((d1 > 0d && d2 < 0d) || (d1 < 0d && d2 > 0d)) &&
                ((d3 > 0d && d4 < 0d) || (d3 < 0d && d4 > 0d)))
            {
                return true;
            }

            if (d1 == 0d && OnSegment(a1, b1, a2))
            {
                return true;
            }

            if (d2 == 0d && OnSegment(a1, b2, a2))
            {
                return true;
            }

            if (d3 == 0d && OnSegment(b1, a1, b2))
            {
                return true;
            }

            return d4 == 0d && OnSegment(b1, a2, b2);
        }

        /// <summary>
        /// 2D cross (a-o)×(b-o) in double. A Q31.32 product wraps once the two
        /// lengths multiply past about 2^31, which flips the straddle sign and can
        /// both pull a far point inside a polygon and miss a long sweep across an edge.
        /// </summary>
        private static double Cross(Fix64Vec2 o, Fix64Vec2 a, Fix64Vec2 b)
        {
            double ax = a.X.ToDouble() - o.X.ToDouble();
            double ay = a.Y.ToDouble() - o.Y.ToDouble();
            double bx = b.X.ToDouble() - o.X.ToDouble();
            double by = b.Y.ToDouble() - o.Y.ToDouble();
            return ax * by - ay * bx;
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
