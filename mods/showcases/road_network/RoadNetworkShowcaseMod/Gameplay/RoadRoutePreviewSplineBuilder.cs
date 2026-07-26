using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.MovePlanning;
using Ludots.Core.Presentation.Events;

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
            PresentationWorldFactPublisher facts,
            IList<RoadRoutePreviewFactScope> activeScopes)
        {
            if (!OrderWorldSpatialResolver.TryGetEntityWorldCm(world, entity, out Vector3 originWorldCm))
            {
                return;
            }

            int segmentIndex = 0;
            if (buffer.HasActive)
            {
                ref Order active = ref buffer.ActiveOrder.Order;
                EmitActivePlanRoute(world, entity, in active, originWorldCm, palette, facts, activeScopes, ref segmentIndex);
                if (OrderWorldSpatialResolver.TryResolveMoveDestination(world, in active, out Vector3 activeDestination))
                {
                    originWorldCm = activeDestination;
                }
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                EmitOrderRoute(world, entity, in queued, originWorldCm, palette, facts, activeScopes, ref segmentIndex);
                if (OrderWorldSpatialResolver.TryResolveMoveDestination(world, in queued, out Vector3 queuedDestination))
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
            PresentationWorldFactPublisher facts,
            IList<RoadRoutePreviewFactScope> activeScopes,
            ref int segmentIndex)
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

            EmitSplineSegments(entity, points[..writeCount], palette, facts, activeScopes, ref segmentIndex);
            EmitEndpoint(entity, points[writeCount - 1], palette, facts, activeScopes, ref segmentIndex);
        }

        private static void EmitOrderRoute(
            World world,
            Entity owner,
            in Order order,
            in Vector3 originWorldCm,
            in RoadRoutePreviewPalette palette,
            PresentationWorldFactPublisher facts,
            IList<RoadRoutePreviewFactScope> activeScopes,
            ref int segmentIndex)
        {
            if (order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                return;
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(world, in order);
            if (pointCount <= 0)
            {
                return;
            }

            Span<Vector3> points = stackalloc Vector3[Math.Min(OrderSpatial.MaxPoints + 1, 65)];
            int writeCount = 0;
            points[writeCount++] = ToVisualMeters(originWorldCm);
            for (int pointIndex = 0; pointIndex < pointCount && writeCount < points.Length; pointIndex++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(world, in order, pointIndex, out Vector3 pointWorldCm))
                {
                    continue;
                }

                points[writeCount++] = ToVisualMeters(pointWorldCm);
            }

            if (writeCount < 2)
            {
                return;
            }

            EmitSplineSegments(owner, points[..writeCount], palette, facts, activeScopes, ref segmentIndex);
            EmitEndpoint(owner, points[writeCount - 1], palette, facts, activeScopes, ref segmentIndex);
        }

        private static void EmitSplineSegments(
            Entity owner,
            ReadOnlySpan<Vector3> points,
            in RoadRoutePreviewPalette palette,
            PresentationWorldFactPublisher facts,
            IList<RoadRoutePreviewFactScope> activeScopes,
            ref int segmentIndex)
        {
            string key = ResolveSplineKey(in palette);
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 start = points[i];
                Vector3 end = points[i + 1];
                int scope = PresentationWorldFactPublisher.ComposeScope(key, owner, segmentIndex++);
                facts.PublishWorldSplineUpdated(
                    key,
                    owner,
                    scope,
                    start,
                    end,
                    palette.WidthMeters,
                    palette.BorderWidthMeters);
                activeScopes.Add(new RoadRoutePreviewFactScope(key, scope));
            }
        }

        private static void EmitEndpoint(
            Entity owner,
            in Vector3 position,
            in RoadRoutePreviewPalette palette,
            PresentationWorldFactPublisher facts,
            IList<RoadRoutePreviewFactScope> activeScopes,
            ref int segmentIndex)
        {
            string key = ResolveEndpointKey(in palette);
            int scope = PresentationWorldFactPublisher.ComposeScope(key, owner, segmentIndex++);
            facts.PublishWorldOverlayUpdated(
                key,
                owner,
                scope,
                position,
                radiusOrLength: 0.30f,
                borderWidth: 0.04f);
            activeScopes.Add(new RoadRoutePreviewFactScope(key, scope));
        }

        private static string ResolveSplineKey(in RoadRoutePreviewPalette palette)
        {
            return palette.PaletteId switch
            {
                RoadRouteProfileCatalog.PreviewCyan => RoadRoutePreviewPresentationKeys.RouteCyan,
                RoadRouteProfileCatalog.PreviewCrimson => RoadRoutePreviewPresentationKeys.RouteCrimson,
                _ => RoadRoutePreviewPresentationKeys.RouteAmber,
            };
        }

        private static string ResolveEndpointKey(in RoadRoutePreviewPalette palette)
        {
            return palette.PaletteId switch
            {
                RoadRouteProfileCatalog.PreviewCyan => RoadRoutePreviewPresentationKeys.EndpointCyan,
                RoadRouteProfileCatalog.PreviewCrimson => RoadRoutePreviewPresentationKeys.EndpointCrimson,
                _ => RoadRoutePreviewPresentationKeys.EndpointAmber,
            };
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

    internal readonly struct RoadRoutePreviewFactScope : IEquatable<RoadRoutePreviewFactScope>
    {
        public readonly string Key;
        public readonly int Scope;

        public RoadRoutePreviewFactScope(string key, int scope)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Scope = scope;
        }

        public bool Equals(RoadRoutePreviewFactScope other)
        {
            return Scope == other.Scope && string.Equals(Key, other.Key, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is RoadRoutePreviewFactScope other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Key, Scope);
        }
    }

    internal static class RoadRoutePreviewPresentationKeys
    {
        public const string RouteAmber = "road_network.route_preview.amber";
        public const string RouteCyan = "road_network.route_preview.cyan";
        public const string RouteCrimson = "road_network.route_preview.crimson";
        public const string EndpointAmber = "road_network.route_preview.endpoint.amber";
        public const string EndpointCyan = "road_network.route_preview.endpoint.cyan";
        public const string EndpointCrimson = "road_network.route_preview.endpoint.crimson";
    }
}
