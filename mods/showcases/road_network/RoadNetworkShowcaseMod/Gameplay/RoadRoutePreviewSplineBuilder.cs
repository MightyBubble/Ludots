using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.MovePlanning;
using Ludots.Core.Presentation.Rendering;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRoutePreviewSplineBuilder
    {
        private const float OverlayY = 0.055f;
        private readonly MovePlanStore _plans;

        public RoadRoutePreviewSplineBuilder(MovePlanStore plans)
        {
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        }

        public void EmitSelectionPreview(
            World world,
            Entity entity,
            ref OrderBuffer buffer,
            in RoadRoutePreviewPalette palette,
            RoadSplineBuffer roadSplines,
            GroundOverlayBuffer overlays,
            int primaryStableIdBase)
        {
            if (!OrderWorldSpatialResolver.TryGetEntityWorldCm(world, entity, out Vector3 originWorldCm))
            {
                return;
            }

            int stableCursor = primaryStableIdBase;
            if (buffer.HasActive)
            {
                ref Order active = ref buffer.ActiveOrder.Order;
                EmitActivePlanRoute(world, entity, in active, originWorldCm, palette, roadSplines, overlays, ref stableCursor);
                if (OrderWorldSpatialResolver.TryResolveMoveDestination(in active, out Vector3 activeDestination))
                {
                    originWorldCm = activeDestination;
                }
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                EmitOrderRoute(in queued, originWorldCm, palette, roadSplines, overlays, ref stableCursor);
                if (OrderWorldSpatialResolver.TryResolveMoveDestination(in queued, out Vector3 queuedDestination))
                {
                    originWorldCm = queuedDestination;
                }
            }
        }

        private void EmitActivePlanRoute(
            World world,
            Entity entity,
            in Order order,
            in Vector3 originWorldCm,
            in RoadRoutePreviewPalette palette,
            RoadSplineBuffer roadSplines,
            GroundOverlayBuffer overlays,
            ref int stableCursor)
        {
            if (!_plans.TryGetPlan(entity, order.OrderId, out MovePlanView plan))
            {
                return;
            }

            int pointCount = plan.Count;
            if (pointCount <= 0)
            {
                return;
            }

            int startIndex = ResolveActiveStartIndex(world, entity, in order, pointCount);
            int remainingCount = pointCount - startIndex;
            if (remainingCount <= 0)
            {
                return;
            }

            Span<Vector3> points = stackalloc Vector3[Math.Min(OrderSpatial.MaxPoints + 1, 65)];
            int writeCount = 0;
            points[writeCount++] = ToVisualMeters(originWorldCm);
            for (int pointIndex = startIndex; pointIndex < pointCount && writeCount < points.Length; pointIndex++)
            {
                if (!plan.TryGetWaypoint(pointIndex, out var point))
                {
                    continue;
                }

                points[writeCount++] = ToVisualMeters(new Vector3(point.X, 0f, point.Y));
            }

            if (writeCount < 2)
            {
                return;
            }

            EmitSplineSegments(points[..writeCount], palette, roadSplines, ref stableCursor);
            EmitEndpoint(points[writeCount - 1], palette, overlays);
        }

        private static void EmitOrderRoute(
            in Order order,
            in Vector3 originWorldCm,
            in RoadRoutePreviewPalette palette,
            RoadSplineBuffer roadSplines,
            GroundOverlayBuffer overlays,
            ref int stableCursor)
        {
            if (order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                return;
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount <= 0)
            {
                return;
            }

            Span<Vector3> points = stackalloc Vector3[Math.Min(OrderSpatial.MaxPoints + 1, 65)];
            int writeCount = 0;
            points[writeCount++] = ToVisualMeters(originWorldCm);
            for (int pointIndex = 0; pointIndex < pointCount && writeCount < points.Length; pointIndex++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(in order, pointIndex, out Vector3 pointWorldCm))
                {
                    continue;
                }

                points[writeCount++] = ToVisualMeters(pointWorldCm);
            }

            if (writeCount < 2)
            {
                return;
            }

            EmitSplineSegments(points[..writeCount], palette, roadSplines, ref stableCursor);
            EmitEndpoint(points[writeCount - 1], palette, overlays);
        }

        private static void EmitSplineSegments(
            ReadOnlySpan<Vector3> points,
            in RoadRoutePreviewPalette palette,
            RoadSplineBuffer roadSplines,
            ref int stableCursor)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 previous = i == 0 ? points[i] : points[i - 1];
                Vector3 start = points[i];
                Vector3 end = points[i + 1];
                Vector3 next = i + 2 < points.Length ? points[i + 2] : points[i + 1];
                Vector3 control0 = start + ((end - previous) / 6f);
                Vector3 control1 = end - ((next - start) / 6f);
                roadSplines.TryAdd(
                    stableCursor++,
                    start,
                    control0,
                    control1,
                    end,
                    palette.WidthMeters,
                    palette.FillColor,
                    palette.BorderColor,
                    palette.BorderWidthMeters);
            }
        }

        private static void EmitEndpoint(in Vector3 position, in RoadRoutePreviewPalette palette, GroundOverlayBuffer overlays)
        {
            overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = position,
                Radius = 0.30f,
                FillColor = palette.FillColor,
                BorderColor = palette.BorderColor,
                BorderWidth = 0.04f
            });
        }

        private static Vector3 ToVisualMeters(in Vector3 worldCm)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X), OverlayY, WorldUnits.CmToM(worldCm.Z));
        }

        private static int ResolveActiveStartIndex(World world, Entity entity, in Order order, int pointCount)
        {
            if (pointCount <= 0)
            {
                return 0;
            }

            if (world.Has<MovePlanRuntime>(entity))
            {
                ref readonly var state = ref world.Get<MovePlanRuntime>(entity);
                if (state.BoundOrderId == order.OrderId)
                {
                    return Math.Clamp(state.CurrentWaypointIndex, 0, pointCount - 1);
                }
            }

            return 0;
        }
    }
}
