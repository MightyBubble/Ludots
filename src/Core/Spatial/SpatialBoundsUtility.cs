using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    public readonly record struct ScreenRect(float MinX, float MinY, float MaxX, float MaxY)
    {
        public bool Contains(Vector2 point)
        {
            return point.X >= MinX && point.X <= MaxX &&
                   point.Y >= MinY && point.Y <= MaxY;
        }

        public bool Intersects(in ScreenRect other)
        {
            return MinX <= other.MaxX && MaxX >= other.MinX &&
                   MinY <= other.MaxY && MaxY >= other.MinY;
        }

        public static ScreenRect FromPoints(Vector2 a, Vector2 b)
        {
            return new ScreenRect(
                MathF.Min(a.X, b.X),
                MathF.Min(a.Y, b.Y),
                MathF.Max(a.X, b.X),
                MathF.Max(a.Y, b.Y));
        }
    }

    public static class SpatialBoundsUtility
    {
        public static bool TryProjectScreenBounds(
            World world,
            Entity entity,
            IScreenProjector projector,
            out ScreenRect bounds)
        {
            bounds = default;
            if (!world.IsAlive(entity) ||
                !world.Has<VisualTransform>(entity))
            {
                return false;
            }

            ref readonly var transform = ref world.Get<VisualTransform>(entity);
            SpatialBounds spatialBounds = world.Has<SpatialBounds>(entity)
                ? world.Get<SpatialBounds>(entity)
                : SpatialBounds.Point;

            switch (spatialBounds.Kind)
            {
                case SpatialBoundsKind.Point:
                    {
                        if (!TryProjectPoint(projector, ResolveWorldPoint(transform, in spatialBounds), out Vector2 point))
                        {
                            return false;
                        }

                        bounds = new ScreenRect(point.X, point.Y, point.X, point.Y);
                        return true;
                    }

                case SpatialBoundsKind.Box3D:
                    if (!world.Has<SpatialBox3D>(entity))
                    {
                        throw new InvalidOperationException($"Entity {entity} declares SpatialBounds.Box3D but is missing SpatialBox3D.");
                    }

                    return TryProjectBox(projector, transform, in spatialBounds, world.Get<SpatialBox3D>(entity), out bounds);

                case SpatialBoundsKind.Footprint2D:
                    if (!world.Has<SpatialFootprint2D>(entity))
                    {
                        throw new InvalidOperationException($"Entity {entity} declares SpatialBounds.Footprint2D but is missing SpatialFootprint2D.");
                    }

                    return TryProjectFootprintBounds(projector, transform, in spatialBounds, world.Get<SpatialFootprint2D>(entity), out bounds);

                default:
                    throw new InvalidOperationException($"Unsupported SpatialBoundsKind '{spatialBounds.Kind}'.");
            }
        }

        public static bool TryProjectFootprintScreenPolygon(
            World world,
            Entity entity,
            IScreenProjector projector,
            int polygonIndex,
            Span<Vector2> destination,
            out int count)
        {
            count = 0;
            if (!world.IsAlive(entity) ||
                !world.Has<VisualTransform>(entity) ||
                !world.Has<SpatialBounds>(entity) ||
                !world.Has<SpatialFootprint2D>(entity))
            {
                return false;
            }

            ref readonly var transform = ref world.Get<VisualTransform>(entity);
            SpatialBounds spatialBounds = world.Get<SpatialBounds>(entity);
            if (spatialBounds.Kind != SpatialBoundsKind.Footprint2D)
            {
                return false;
            }

            return TryProjectFootprintPolygon(
                projector,
                transform,
                in spatialBounds,
                world.Get<SpatialFootprint2D>(entity),
                polygonIndex,
                destination,
                out count);
        }

        public static bool PointerHitsEntity(
            World world,
            Entity entity,
            IScreenProjector projector,
            Vector2 pointer,
            float pointPickRadiusPixels)
        {
            if (!world.IsAlive(entity) || !world.Has<VisualTransform>(entity))
            {
                return false;
            }

            ref readonly var transform = ref world.Get<VisualTransform>(entity);
            SpatialBounds spatialBounds = world.Has<SpatialBounds>(entity)
                ? world.Get<SpatialBounds>(entity)
                : SpatialBounds.Point;

            return spatialBounds.Kind switch
            {
                SpatialBoundsKind.Point => PointerHitsPoint(projector, transform, in spatialBounds, pointer, pointPickRadiusPixels),
                SpatialBoundsKind.Box3D => world.Has<SpatialBox3D>(entity) &&
                                           PointerHitsBox(projector, transform, in spatialBounds, world.Get<SpatialBox3D>(entity), pointer),
                SpatialBoundsKind.Footprint2D => world.Has<SpatialFootprint2D>(entity) &&
                                                 PointerHitsFootprint(projector, transform, in spatialBounds, world.Get<SpatialFootprint2D>(entity), pointer),
                _ => false,
            };
        }

        public static bool EntityIntersectsScreenRect(
            World world,
            Entity entity,
            IScreenProjector projector,
            in ScreenRect marquee)
        {
            if (!world.IsAlive(entity) || !world.Has<VisualTransform>(entity))
            {
                return false;
            }

            ref readonly var transform = ref world.Get<VisualTransform>(entity);
            SpatialBounds spatialBounds = world.Has<SpatialBounds>(entity)
                ? world.Get<SpatialBounds>(entity)
                : SpatialBounds.Point;

            return spatialBounds.Kind switch
            {
                SpatialBoundsKind.Point => TryProjectPoint(projector, ResolveWorldPoint(transform, in spatialBounds), out Vector2 point) &&
                                           marquee.Contains(point),
                SpatialBoundsKind.Box3D => world.Has<SpatialBox3D>(entity) &&
                                           TryProjectBox(projector, transform, in spatialBounds, world.Get<SpatialBox3D>(entity), out ScreenRect boxRect) &&
                                           marquee.Intersects(in boxRect),
                SpatialBoundsKind.Footprint2D => world.Has<SpatialFootprint2D>(entity) &&
                                                 FootprintIntersectsRect(projector, transform, in spatialBounds, world.Get<SpatialFootprint2D>(entity), in marquee),
                _ => false,
            };
        }

        private static bool PointerHitsPoint(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            Vector2 pointer,
            float radiusPixels)
        {
            if (!TryProjectPoint(projector, ResolveWorldPoint(transform, in spatialBounds), out Vector2 point))
            {
                return false;
            }

            float dx = point.X - pointer.X;
            float dy = point.Y - pointer.Y;
            return (dx * dx) + (dy * dy) <= radiusPixels * radiusPixels;
        }

        private static bool PointerHitsBox(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            in SpatialBox3D box,
            Vector2 pointer)
        {
            return TryProjectBox(projector, transform, in spatialBounds, in box, out ScreenRect bounds) &&
                   bounds.Contains(pointer);
        }

        private static bool PointerHitsFootprint(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            in SpatialFootprint2D footprint,
            Vector2 pointer)
        {
            Span<Vector2> projected = stackalloc Vector2[SpatialFootprint2D.MaxVerticesPerPolygon];
            for (int polygonIndex = 0; polygonIndex < footprint.PolygonCount; polygonIndex++)
            {
                int count = footprint.GetPolygonVertexCount(polygonIndex);
                if (count < 3)
                {
                    continue;
                }

                if (!TryProjectFootprintPolygon(projector, transform, in spatialBounds, in footprint, polygonIndex, projected, out int projectedCount))
                {
                    continue;
                }

                if (PointInPolygon(pointer, projected.Slice(0, projectedCount)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FootprintIntersectsRect(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            in SpatialFootprint2D footprint,
            in ScreenRect marquee)
        {
            Span<Vector2> projected = stackalloc Vector2[SpatialFootprint2D.MaxVerticesPerPolygon];
            for (int polygonIndex = 0; polygonIndex < footprint.PolygonCount; polygonIndex++)
            {
                int count = footprint.GetPolygonVertexCount(polygonIndex);
                if (count < 3)
                {
                    continue;
                }

                if (!TryProjectFootprintPolygon(projector, transform, in spatialBounds, in footprint, polygonIndex, projected, out int projectedCount))
                {
                    continue;
                }

                if (PolygonIntersectsRect(projected.Slice(0, projectedCount), in marquee))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryProjectFootprintBounds(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            in SpatialFootprint2D footprint,
            out ScreenRect bounds)
        {
            bounds = default;
            bool hasPoint = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            Span<Vector2> projected = stackalloc Vector2[SpatialFootprint2D.MaxVerticesPerPolygon];

            for (int polygonIndex = 0; polygonIndex < footprint.PolygonCount; polygonIndex++)
            {
                if (!TryProjectFootprintPolygon(projector, transform, in spatialBounds, in footprint, polygonIndex, projected, out int projectedCount))
                {
                    continue;
                }

                for (int i = 0; i < projectedCount; i++)
                {
                    Vector2 point = projected[i];
                    if (!hasPoint)
                    {
                        minX = maxX = point.X;
                        minY = maxY = point.Y;
                        hasPoint = true;
                    }
                    else
                    {
                        minX = MathF.Min(minX, point.X);
                        minY = MathF.Min(minY, point.Y);
                        maxX = MathF.Max(maxX, point.X);
                        maxY = MathF.Max(maxY, point.Y);
                    }
                }
            }

            if (!hasPoint)
            {
                return false;
            }

            bounds = new ScreenRect(minX, minY, maxX, maxY);
            return true;
        }

        private static bool TryProjectBox(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            in SpatialBox3D box,
            out ScreenRect bounds)
        {
            bounds = default;

            Vector3 center = ResolveWorldPoint(transform, in spatialBounds);
            Vector3 half = new(
                CmToMeters(box.HalfSizeXCm) * transform.Scale.X,
                CmToMeters(box.HalfSizeYCm) * transform.Scale.Y,
                CmToMeters(box.HalfSizeZCm) * transform.Scale.Z);

            Span<Vector3> corners = stackalloc Vector3[8];
            corners[0] = new Vector3(-half.X, -half.Y, -half.Z);
            corners[1] = new Vector3(half.X, -half.Y, -half.Z);
            corners[2] = new Vector3(-half.X, half.Y, -half.Z);
            corners[3] = new Vector3(half.X, half.Y, -half.Z);
            corners[4] = new Vector3(-half.X, -half.Y, half.Z);
            corners[5] = new Vector3(half.X, -half.Y, half.Z);
            corners[6] = new Vector3(-half.X, half.Y, half.Z);
            corners[7] = new Vector3(half.X, half.Y, half.Z);

            bool hasProjectedPoint = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 worldPoint = center + Vector3.Transform(corners[i], transform.Rotation);
                if (!TryProjectPoint(projector, worldPoint, out Vector2 projected))
                {
                    continue;
                }

                if (!hasProjectedPoint)
                {
                    minX = maxX = projected.X;
                    minY = maxY = projected.Y;
                    hasProjectedPoint = true;
                }
                else
                {
                    minX = MathF.Min(minX, projected.X);
                    minY = MathF.Min(minY, projected.Y);
                    maxX = MathF.Max(maxX, projected.X);
                    maxY = MathF.Max(maxY, projected.Y);
                }
            }

            if (!hasProjectedPoint)
            {
                return false;
            }

            bounds = new ScreenRect(minX, minY, maxX, maxY);
            return true;
        }

        private static bool TryProjectFootprintPolygon(
            IScreenProjector projector,
            in VisualTransform transform,
            in SpatialBounds spatialBounds,
            in SpatialFootprint2D footprint,
            int polygonIndex,
            Span<Vector2> destination,
            out int count)
        {
            count = footprint.GetPolygonVertexCount(polygonIndex);
            if (count < 3 || destination.Length < count)
            {
                count = 0;
                return false;
            }

            Vector3 center = ResolveWorldPoint(transform, in spatialBounds);
            for (int i = 0; i < count; i++)
            {
                var vertex = footprint.GetVertex(polygonIndex, i);
                Vector3 local = new(
                    CmToMeters(vertex.X) * transform.Scale.X,
                    0f,
                    CmToMeters(vertex.Y) * transform.Scale.Z);
                Vector3 worldPoint = center + Vector3.Transform(local, transform.Rotation);
                if (!TryProjectPoint(projector, worldPoint, out destination[i]))
                {
                    count = 0;
                    return false;
                }
            }

            return true;
        }

        private static Vector3 ResolveWorldPoint(in VisualTransform transform, in SpatialBounds spatialBounds)
        {
            Vector3 local = new(
                CmToMeters(spatialBounds.LocalCenterXCm) * transform.Scale.X,
                CmToMeters(spatialBounds.LocalCenterYCm) * transform.Scale.Y,
                CmToMeters(spatialBounds.LocalCenterZCm) * transform.Scale.Z);
            return transform.Position + Vector3.Transform(local, transform.Rotation);
        }

        private static bool TryProjectPoint(IScreenProjector projector, Vector3 worldPoint, out Vector2 projected)
        {
            projected = projector.WorldToScreen(worldPoint);
            return !(float.IsNaN(projected.X) ||
                     float.IsNaN(projected.Y) ||
                     float.IsInfinity(projected.X) ||
                     float.IsInfinity(projected.Y));
        }

        private static float CmToMeters(int valueCm) => valueCm / 100f;

        private static bool PolygonIntersectsRect(ReadOnlySpan<Vector2> polygon, in ScreenRect rect)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                if (rect.Contains(polygon[i]))
                {
                    return true;
                }
            }

            var rectTopLeft = new Vector2(rect.MinX, rect.MinY);
            var rectTopRight = new Vector2(rect.MaxX, rect.MinY);
            var rectBottomRight = new Vector2(rect.MaxX, rect.MaxY);
            var rectBottomLeft = new Vector2(rect.MinX, rect.MaxY);

            if (PointInPolygon(rectTopLeft, polygon) ||
                PointInPolygon(rectTopRight, polygon) ||
                PointInPolygon(rectBottomRight, polygon) ||
                PointInPolygon(rectBottomLeft, polygon))
            {
                return true;
            }

            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                if (SegmentIntersectsRect(a, b, in rect))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, in ScreenRect rect)
        {
            var rectTopLeft = new Vector2(rect.MinX, rect.MinY);
            var rectTopRight = new Vector2(rect.MaxX, rect.MinY);
            var rectBottomRight = new Vector2(rect.MaxX, rect.MaxY);
            var rectBottomLeft = new Vector2(rect.MinX, rect.MaxY);

            return SegmentsIntersect(a, b, rectTopLeft, rectTopRight) ||
                   SegmentsIntersect(a, b, rectTopRight, rectBottomRight) ||
                   SegmentsIntersect(a, b, rectBottomRight, rectBottomLeft) ||
                   SegmentsIntersect(a, b, rectBottomLeft, rectTopLeft);
        }

        private static bool PointInPolygon(Vector2 point, ReadOnlySpan<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 pi = polygon[i];
                Vector2 pj = polygon[j];

                bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                                  (point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + float.Epsilon)) + pi.X);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            float d1 = Cross(a1, a2, b1);
            float d2 = Cross(a1, a2, b2);
            float d3 = Cross(b1, b2, a1);
            float d4 = Cross(b1, b2, a2);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            return (d1 == 0f && OnSegment(a1, a2, b1)) ||
                   (d2 == 0f && OnSegment(a1, a2, b2)) ||
                   (d3 == 0f && OnSegment(b1, b2, a1)) ||
                   (d4 == 0f && OnSegment(b1, b2, a2));
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.X >= MathF.Min(a.X, b.X) && p.X <= MathF.Max(a.X, b.X) &&
                   p.Y >= MathF.Min(a.Y, b.Y) && p.Y <= MathF.Max(a.Y, b.Y);
        }
    }
}
