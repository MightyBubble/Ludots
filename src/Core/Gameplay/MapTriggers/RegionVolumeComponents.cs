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
            Fix64Vec2 ab = SegmentB - SegmentA;
            Fix64Vec2 ap = local - SegmentA;
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

            Fix64Vec2 closest = SegmentA + t * ab;
            Fix64Vec2 delta = local - closest;
            return delta.X * delta.X + delta.Y * delta.Y <= HalfThickness * HalfThickness;
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
