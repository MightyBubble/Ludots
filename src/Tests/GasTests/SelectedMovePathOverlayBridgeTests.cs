using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SelectedMovePathOverlayBridgeTests
    {
        private const int MoveToOrderTypeId = 101;
        private const int MassNavigationMoveOrderTypeId = 172;

        [Test]
        public void UpdateViewedSelection_EmitsPathLinesAndWaypointsForActiveAndQueuedMoveOrders()
        {
            using var world = World.Create();
            var overlays = new GroundOverlayBuffer();
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var bridge = new SelectedMovePathOverlayBridge(world, pathService, pathStore, overlays, MoveToOrderTypeId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateMoveOrder(actor, 300, 0, orderId: 1), priority: 60);
            Assert.That(orders.Enqueue(CreateMoveOrder(actor, 300, 200, orderId: 2), priority: 60, expireStep: -1, insertStep: 1), Is.True);

            bridge.UpdateViewedSelection(new[] { actor });

            ReadOnlySpan<GroundOverlayItem> span = overlays.GetSpan();
            Assert.That(Count(span, GroundOverlayShape.Line), Is.EqualTo(2));
            Assert.That(Count(span, GroundOverlayShape.Circle), Is.EqualTo(2));
            Assert.That(pathService.Requests.Count, Is.EqualTo(2));
            Assert.That(pathService.Requests[0].Start.Xcm, Is.EqualTo(0));
            Assert.That(pathService.Requests[0].Goal.Xcm, Is.EqualTo(300));
            Assert.That(pathService.Requests[1].Start.Xcm, Is.EqualTo(300));
            Assert.That(pathService.Requests[1].Goal.Ycm, Is.EqualTo(200));
            Assert.That(pathStore.IsAlive(pathService.LastHandle), Is.False, "Preview bridge must release temporary path handles after copying.");
        }

        [Test]
        public void UpdateViewedSelection_IgnoresEntitiesWithoutMoveOrders()
        {
            using var world = World.Create();
            var overlays = new GroundOverlayBuffer();
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var bridge = new SelectedMovePathOverlayBridge(world, pathService, pathStore, overlays, MoveToOrderTypeId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            bridge.UpdateViewedSelection(new[] { actor });

            Assert.That(overlays.Count, Is.EqualTo(0));
            Assert.That(pathService.Requests.Count, Is.EqualTo(0));
        }

        [Test]
        public void UpdateViewedSelection_EmitsTargetMarkerForActiveMassNavigationMoveOrder()
        {
            using var world = World.Create();
            var overlays = new GroundOverlayBuffer();
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var bridge = new SelectedMovePathOverlayBridge(
                world,
                pathService,
                pathStore,
                overlays,
                new[] { MoveToOrderTypeId, MassNavigationMoveOrderTypeId });

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateMoveOrder(actor, MassNavigationMoveOrderTypeId, 450, 250, orderId: 7), priority: 70);

            bridge.UpdateViewedSelection(new[] { actor });

            ReadOnlySpan<GroundOverlayItem> span = overlays.GetSpan();
            Assert.That(Count(span, GroundOverlayShape.Line), Is.EqualTo(1));
            Assert.That(Count(span, GroundOverlayShape.Circle), Is.EqualTo(1));
            Assert.That(pathService.Requests.Count, Is.EqualTo(1));
            Assert.That(pathService.Requests[0].Goal.Xcm, Is.EqualTo(450));
            Assert.That(pathService.Requests[0].Goal.Ycm, Is.EqualTo(250));
        }

        [Test]
        public void UpdateViewedSelection_PathSolveFailureDoesNotEmitFakeDirectLeg()
        {
            using var world = World.Create();
            var overlays = new GroundOverlayBuffer();
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new FailingPathService();
            var bridge = new SelectedMovePathOverlayBridge(world, pathService, pathStore, overlays, MoveToOrderTypeId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateMoveOrder(actor, 300, 0, orderId: 1), priority: 60);

            bridge.UpdateViewedSelection(new[] { actor });

            Assert.That(overlays.Count, Is.EqualTo(0));
            Assert.That(pathService.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void UpdateViewedSelection_AuthoredRoutePastScratchCapacityDoesNotOverflow()
        {
            using var world = World.Create();
            var overlays = new GroundOverlayBuffer(capacity: 256);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var bridge = new SelectedMovePathOverlayBridge(world, pathService, pathStore, overlays, MoveToOrderTypeId);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            ref var orders = ref world.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateRouteOrder(actor, pointCount: OrderSpatial.MaxPoints + 1), priority: 60);

            Assert.DoesNotThrow(() => bridge.UpdateViewedSelection(new[] { actor }));
            Assert.That(Count(overlays.GetSpan(), GroundOverlayShape.Line), Is.EqualTo(OrderSpatial.MaxPoints));
            Assert.That(Count(overlays.GetSpan(), GroundOverlayShape.Circle), Is.EqualTo(1));
            Assert.That(pathService.Requests.Count, Is.EqualTo(0));
        }

        [Test]
        public void GroundOverlayBuffer_ClearTransientRemovesTryAddItems()
        {
            var overlays = new GroundOverlayBuffer();

            Assert.That(overlays.TryAdd(new GroundOverlayItem { Shape = GroundOverlayShape.Line }), Is.True);
            Assert.That(overlays.Count, Is.EqualTo(1));

            overlays.ClearTransient();

            Assert.That(overlays.Count, Is.EqualTo(0));
        }

        private static int Count(ReadOnlySpan<GroundOverlayItem> items, GroundOverlayShape shape)
        {
            int count = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private static Order CreateMoveOrder(Entity actor, int xcm, int ycm, int orderId)
        {
            return CreateMoveOrder(actor, MoveToOrderTypeId, xcm, ycm, orderId);
        }

        private static Order CreateMoveOrder(Entity actor, int orderTypeId, int xcm, int ycm, int orderId)
        {
            return new Order
            {
                OrderId = orderId,
                OrderTypeId = orderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(xcm, 0f, ycm)
                    }
                }
            };
        }

        private static Order CreateRouteOrder(Entity actor, int pointCount)
        {
            var order = new Order
            {
                OrderId = 99,
                OrderTypeId = MoveToOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.List,
                    }
                }
            };

            for (int i = 0; i < pointCount; i++)
            {
                order.Args.Spatial.AddPointWorldCm((i + 1) * 100, 0, 0);
            }
            order.Args.Spatial.PointCount = pointCount;

            return order;
        }

        private sealed class RecordingPathService : IPathService
        {
            private readonly PathStore _store;

            public RecordingPathService(PathStore store)
            {
                _store = store;
            }

            public System.Collections.Generic.List<PathRequest> Requests { get; } = new();
            public PathHandle LastHandle { get; private set; }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                Requests.Add(request);
                if (!_store.TryAllocate(2, out var handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                    return false;
                }

                Span<int> xs = stackalloc int[2];
                Span<int> ys = stackalloc int[2];
                xs[0] = request.Start.Xcm;
                ys[0] = request.Start.Ycm;
                xs[1] = request.Goal.Xcm;
                ys[1] = request.Goal.Ycm;
                _store.TryWrite(in handle, xs, ys, count: 2);
                LastHandle = handle;
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, 0, 0);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }

        private sealed class FailingPathService : IPathService
        {
            public System.Collections.Generic.List<PathRequest> Requests { get; } = new();

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                Requests.Add(request);
                result = new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, 0, 0);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                count = 0;
                return false;
            }
        }
    }
}
