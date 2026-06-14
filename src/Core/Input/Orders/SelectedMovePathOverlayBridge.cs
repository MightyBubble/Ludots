using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Emits path overlays for the currently viewed selection by solving each active/queued
    /// move leg through the shared pathing runtime.
    /// </summary>
    public sealed class SelectedMovePathOverlayBridge
    {
        private const int MaxSelectedEntities = 4;
        private const int DefaultMaxPathPoints = 64;
        private const float OverlayY = 0.035f;
        private const float PrimaryLineWidthCm = 28f;
        private const float SecondaryLineWidthCm = 18f;
        private const float WaypointRadiusCm = 26f;

        private static readonly Vector4 PrimaryFill = new(0.22f, 0.86f, 0.98f, 0.18f);
        private static readonly Vector4 PrimaryBorder = new(0.46f, 0.95f, 1.0f, 0.96f);
        private static readonly Vector4 SecondaryFill = new(0.38f, 0.78f, 0.92f, 0.10f);
        private static readonly Vector4 SecondaryBorder = new(0.50f, 0.88f, 0.98f, 0.68f);

        private readonly World _world;
        private readonly IPathService _paths;
        private readonly PathStore _pathStore;
        private readonly GroundOverlayBuffer _overlays;
        private readonly int[] _moveOrderTypeIds;
        private readonly int[] _pathXcm = new int[DefaultMaxPathPoints];
        private readonly int[] _pathYcm = new int[DefaultMaxPathPoints];
        private int _nextRequestId = 1;

        public SelectedMovePathOverlayBridge(
            World world,
            IPathService paths,
            PathStore pathStore,
            GroundOverlayBuffer overlays,
            int moveToOrderTypeId)
            : this(world, paths, pathStore, overlays, new[] { moveToOrderTypeId })
        {
        }

        public SelectedMovePathOverlayBridge(
            World world,
            IPathService paths,
            PathStore pathStore,
            GroundOverlayBuffer overlays,
            int[] moveOrderTypeIds)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
            _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
            _moveOrderTypeIds = moveOrderTypeIds ?? throw new ArgumentNullException(nameof(moveOrderTypeIds));
            if (_moveOrderTypeIds.Length == 0)
            {
                throw new ArgumentException("At least one move order type id is required.", nameof(moveOrderTypeIds));
            }
        }

        public void UpdateViewedSelection(ReadOnlySpan<Entity> selected)
        {
            int emittedEntities = 0;
            for (int i = 0; i < selected.Length && emittedEntities < MaxSelectedEntities; i++)
            {
                Entity entity = selected[i];
                if (!_world.IsAlive(entity) || !_world.Has<OrderBuffer>(entity))
                {
                    continue;
                }

                EmitEntityPath(entity, emittedEntities == 0);
                emittedEntities++;
            }
        }

        private void EmitEntityPath(Entity entity, bool isPrimary)
        {
            if (!OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, entity, out var originWorldCm))
            {
                return;
            }

            ref var buffer = ref _world.Get<OrderBuffer>(entity);
            if (buffer.HasActive &&
                IsMoveOrderType(buffer.ActiveOrder.Order.OrderTypeId) &&
                TryEmitAuthoredRoute(in buffer.ActiveOrder.Order, originWorldCm, isPrimary, consumeFromCurrentIndex: true, out var activeDestination))
            {
                originWorldCm = activeDestination;
            }
            else if (buffer.HasActive &&
                     IsMoveOrderType(buffer.ActiveOrder.Order.OrderTypeId) &&
                     OrderWorldSpatialResolver.TryResolveMoveDestination(in buffer.ActiveOrder.Order, out activeDestination))
            {
                EmitSolvedLeg(entity, originWorldCm, activeDestination, isPrimary);
                originWorldCm = activeDestination;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (!IsMoveOrderType(queued.OrderTypeId))
                {
                    continue;
                }

                bool emittedAuthoredRoute = TryEmitAuthoredRoute(
                    in queued,
                    originWorldCm,
                    isPrimary,
                    consumeFromCurrentIndex: false,
                    out var queuedDestination);
                if (!emittedAuthoredRoute &&
                    !OrderWorldSpatialResolver.TryResolveMoveDestination(in queued, out queuedDestination))
                {
                    continue;
                }

                if (!emittedAuthoredRoute)
                {
                    EmitSolvedLeg(entity, originWorldCm, queuedDestination, isPrimary);
                }

                originWorldCm = queuedDestination;
            }
        }

        private bool TryEmitAuthoredRoute(in Order order, Vector3 originWorldCm, bool isPrimary, bool consumeFromCurrentIndex, out Vector3 finalDestination)
        {
            finalDestination = default;
            if (order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                return false;
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount <= 0)
            {
                return false;
            }

            int startIndex = consumeFromCurrentIndex
                ? ResolveActiveRouteStartIndex(entity: order.Actor, in order, pointCount)
                : 0;
            int writeCount = 0;
            for (int pointIndex = startIndex; pointIndex < pointCount; pointIndex++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(in order, pointIndex, out var pointWorldCm))
                {
                    continue;
                }

                _pathXcm[writeCount] = (int)MathF.Round(pointWorldCm.X);
                _pathYcm[writeCount] = (int)MathF.Round(pointWorldCm.Z);
                writeCount++;
                finalDestination = pointWorldCm;
                if (writeCount == _pathXcm.Length)
                {
                    break;
                }
            }

            if (writeCount == 0)
            {
                return false;
            }

            EmitPolylineFromOrigin(originWorldCm, writeCount, isPrimary);
            return true;
        }

        private bool IsMoveOrderType(int orderTypeId)
        {
            for (int i = 0; i < _moveOrderTypeIds.Length; i++)
            {
                if (_moveOrderTypeIds[i] == orderTypeId)
                {
                    return true;
                }
            }

            return false;
        }

        private int ResolveActiveRouteStartIndex(Entity entity, in Order order, int pointCount)
        {
            if (pointCount <= 0)
            {
                return 0;
            }

            if (_world.IsAlive(entity) &&
                _world.Has<OrderBuffer>(entity))
            {
                OrderBuffer buffer = _world.Get<OrderBuffer>(entity);
                if (buffer.HasActive &&
                    buffer.ActiveOrder.Order.OrderId == order.OrderId &&
                    buffer.ActiveOrder.Order.OrderTypeId == order.OrderTypeId)
                {
                    return Math.Clamp(buffer.ActiveOrder.RuntimeInt0, 0, pointCount - 1);
                }
            }

            return 0;
        }

        private void EmitSolvedLeg(Entity actor, Vector3 startWorldCm, Vector3 goalWorldCm, bool isPrimary)
        {
            if (DistanceCm(startWorldCm, goalWorldCm) <= 0.01f)
            {
                return;
            }

            var request = new PathRequest(
                _nextRequestId++,
                actor,
                PathDomain.Auto,
                PathEndpoint.FromWorldCm((int)MathF.Round(startWorldCm.X), (int)MathF.Round(startWorldCm.Z)),
                PathEndpoint.FromWorldCm((int)MathF.Round(goalWorldCm.X), (int)MathF.Round(goalWorldCm.Z)),
                new PathBudget(maxExpanded: 0, maxPoints: DefaultMaxPathPoints));

            if (!_paths.TrySolve(in request, out var result) ||
                result.Status != PathStatus.Found ||
                !result.Handle.IsValid)
            {
                return;
            }

            try
            {
                if (!_paths.TryCopyPath(in result.Handle, _pathXcm, _pathYcm, out int count) ||
                    count < 2)
                {
                    return;
                }

                EmitPolyline(count, isPrimary);
            }
            finally
            {
                if (_pathStore.IsAlive(result.Handle))
                {
                    _pathStore.Release(result.Handle);
                }
            }
        }

        private void EmitPolyline(int count, bool isPrimary)
        {
            Vector4 fill = isPrimary ? PrimaryFill : SecondaryFill;
            Vector4 border = isPrimary ? PrimaryBorder : SecondaryBorder;
            float widthMeters = WorldUnits.CmToM(isPrimary ? PrimaryLineWidthCm : SecondaryLineWidthCm);

            for (int i = 0; i < count - 1; i++)
            {
                Vector3 start = ToVisualMeters(_pathXcm[i], _pathYcm[i]);
                Vector3 end = ToVisualMeters(_pathXcm[i + 1], _pathYcm[i + 1]);
                Vector2 delta = new(end.X - start.X, end.Z - start.Z);
                float length = delta.Length();
                if (length <= 0.0001f)
                {
                    continue;
                }

                _overlays.TryAdd(new GroundOverlayItem
                {
                    Shape = GroundOverlayShape.Line,
                    Center = start,
                    Length = length,
                    Width = widthMeters,
                    Rotation = WorldPlane2D.FacingRadFromDirection(delta.X, delta.Y),
                    FillColor = fill,
                    BorderColor = border,
                    BorderWidth = 0.02f
                });
            }

            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = ToVisualMeters(_pathXcm[count - 1], _pathYcm[count - 1]),
                Radius = WorldUnits.CmToM(WaypointRadiusCm),
                FillColor = fill,
                BorderColor = border,
                BorderWidth = 0.025f
            });
        }

        private void EmitPolylineFromOrigin(Vector3 originWorldCm, int count, bool isPrimary)
        {
            Vector4 fill = isPrimary ? PrimaryFill : SecondaryFill;
            Vector4 border = isPrimary ? PrimaryBorder : SecondaryBorder;
            float widthMeters = WorldUnits.CmToM(isPrimary ? PrimaryLineWidthCm : SecondaryLineWidthCm);

            Vector3 previous = ToVisualMeters((int)MathF.Round(originWorldCm.X), (int)MathF.Round(originWorldCm.Z));
            for (int i = 0; i < count; i++)
            {
                Vector3 current = ToVisualMeters(_pathXcm[i], _pathYcm[i]);
                Vector2 delta = new(current.X - previous.X, current.Z - previous.Z);
                float length = delta.Length();
                if (length > 0.0001f)
                {
                    _overlays.TryAdd(new GroundOverlayItem
                    {
                        Shape = GroundOverlayShape.Line,
                        Center = previous,
                        Length = length,
                        Width = widthMeters,
                        Rotation = WorldPlane2D.FacingRadFromDirection(delta.X, delta.Y),
                        FillColor = fill,
                        BorderColor = border,
                        BorderWidth = 0.02f
                    });
                }

                previous = current;
            }

            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = previous,
                Radius = WorldUnits.CmToM(WaypointRadiusCm),
                FillColor = fill,
                BorderColor = border,
                BorderWidth = 0.025f
            });
        }

        private static Vector3 ToVisualMeters(int xcm, int ycm)
        {
            return new Vector3(WorldUnits.CmToM(xcm), OverlayY, WorldUnits.CmToM(ycm));
        }

        private static float DistanceCm(Vector3 startWorldCm, Vector3 goalWorldCm)
        {
            float dx = goalWorldCm.X - startWorldCm.X;
            float dz = goalWorldCm.Z - startWorldCm.Z;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
