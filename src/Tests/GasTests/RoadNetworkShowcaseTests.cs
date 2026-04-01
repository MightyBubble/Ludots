using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Mathematics;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Map.Board;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using RoadNetworkShowcaseMod;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;
using RoadNetworkShowcaseMod.Systems;
using RoadNetworkShowcaseMod.UI;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RoadNetworkShowcaseTests
    {
        [Test]
        public void RoadNetworkScenarioDefinition_Create_BuildsChunkedRoadGraphAndSplineBatches()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);

            long centralChunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(0, 0), chunkSizeCm);
            long westernChunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(-9000, 0), chunkSizeCm);
            long easternChunkKey = GraphChunkKey.FromWorld(new WorldCmInt2(9000, 0), chunkSizeCm);

            Assert.That(scenario.ChunkSizeCm, Is.EqualTo(chunkSizeCm));
            Assert.That(scenario.StreamingRadiusCm, Is.EqualTo(12800));
            Assert.That(scenario.TryGetGraphChunk(centralChunkKey, out var centralChunk), Is.True);
            Assert.That(centralChunk.Graph.NodeCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(scenario.TryGetGraphChunk(westernChunkKey, out var westernChunk), Is.True);
            Assert.That(westernChunk.Graph.NodeCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(scenario.TryGetGraphChunk(easternChunkKey, out var easternChunk), Is.True);
            Assert.That(easternChunk.Graph.NodeCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(scenario.TryGetRoadSplineChunk(centralChunkKey, out var centralSplines), Is.True);
            Assert.That(centralSplines.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(scenario.TryGetRoadSplineChunk(easternChunkKey, out var easternSplines), Is.True);
            Assert.That(easternSplines.Length, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void RoadNetworkScenarioDefinition_CurvedRoadPath_UsesDenseIntermediateSamples()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);
            loadedChunks.ChunkLoaded += chunkKey =>
            {
                if (scenario.TryGetGraphChunk(chunkKey, out var chunk))
                {
                    store.AddOrReplace(chunkKey, chunk);
                }
            };

            loadedChunks.Update(centerXcm: -4500, centerYcm: 4500, radiusCm: scenario.StreamingRadiusCm);

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: chunkSizeCm / 2);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 128);
            var service = new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(), pathStore, CreatePathingConfig());
            var request = new PathRequest(
                requestId: 9,
                actor: default,
                domain: PathDomain.Auto,
                agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                start: PathEndpoint.FromWorldCm(-9000, 0),
                goal: PathEndpoint.FromWorldCm(0, 9000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 128));

            Assert.That(service.TrySolve(in request, out var result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found));

            var xs = new int[128];
            var ys = new int[128];
            Assert.That(service.TryCopyPath(in result.Handle, xs, ys, out int count), Is.True);
            Assert.That(count, Is.GreaterThanOrEqualTo(20), "Curved showcase roads should be sampled densely enough that runtime path following does not cut across fields.");
            pathStore.Release(result.Handle);
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_EnqueuesSingleAuthoredRoute_AndReleasesPathHandle()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 16);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore, new[]
            {
                (0, 0),
                (200, 0),
                (450, 150),
            });
            var globals = CreateGlobals(pathService, pathStore, moveToOrderTypeId: 77);
            Entity actor = world.Create(
                new RoadColumnTag(),
                WorldPositionCm.FromCm(0, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = CreateMoveOrder(actor, orderTypeId: 77, xcm: 450, ycm: 150, submitMode: OrderSubmitMode.Immediate);

            Assert.That(expander.TrySubmit(in order), Is.True);
            Assert.That(pathService.Requests.Count, Is.EqualTo(1));
            Assert.That(pathService.Requests[0].AgentTypeId, Is.EqualTo(RoadNetworkShowcaseIds.PathPlannerAgentTypeId));
            Assert.That(pathService.Requests[0].Start.Xcm, Is.EqualTo(0));
            Assert.That(pathService.Requests[0].Goal.Xcm, Is.EqualTo(450));
            Assert.That(orderQueue.Count, Is.EqualTo(1));

            Assert.That(orderQueue.TryDequeue(out var routeOrder), Is.True);
            Assert.That(routeOrder.SubmitMode, Is.EqualTo(OrderSubmitMode.Immediate));
            Assert.That(routeOrder.OrderTypeId, Is.EqualTo(171));
            Assert.That(routeOrder.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.List));
            Assert.That(routeOrder.Args.Spatial.PointCount, Is.EqualTo(3));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(in routeOrder, 0, out var startPoint), Is.True);
            Assert.That(startPoint.X, Is.EqualTo(0f));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(in routeOrder, 1, out var bendPoint), Is.True);
            Assert.That(bendPoint.X, Is.EqualTo(200f));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(in routeOrder, out var finalPoint), Is.True);
            Assert.That(finalPoint.X, Is.EqualTo(450f));
            Assert.That(finalPoint.Z, Is.EqualTo(150f));
            Assert.That(pathStore.IsAlive(pathService.LastHandle), Is.False, "Expanded road moves must release temporary path handles after copying.");
        }

        [Test]
        public void RoadRouteSelectionStrategy_DoesNotSkipAheadBeforeReachingCurrentWaypoint()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            Order order = CreateRouteOrder(actor, roadMoveFollowOrderTypeId: 171, (0, 0), (250, 120), (500, 240));
            order.Args.Spatial.A0 = 2;
            var plans = new RoadNavPlanStore();
            Assert.That(plans.TryBindFromOrder(actor, in order, out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, order.OrderId, out RoadNavPlanView plan), Is.True);

            var strategy = new RoadRouteSelectionStrategy();
            bool selected = strategy.TrySelect(in plan, Fix64Vec2.FromInt(330, 170), currentWaypointIndex: 1, stopRadiusCm: 40f, out RoadRouteSelection selection);

            Assert.That(selected, Is.True);
            Assert.That(selection.Completed, Is.False);
            Assert.That(selection.WaypointIndex, Is.EqualTo(1), "Road-follow selection should keep the current waypoint until the actor actually reaches it, instead of cutting the corner toward the next sampled point.");
            Assert.That(order.Args.Spatial.A0, Is.EqualTo(2), "Selection must not read authored-order payload as execution cursor.");
        }

        [Test]
        public void RoadMovePlanSelectionSystem_TracksWaypointProgressInRuntimeState_NotAuthoredOrderPayload()
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            Dictionary<string, object> globals = CreateGlobals(new FailingPathService(), pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);

            Entity actor = world.Create(
                new Name { Value = "Runtime Cursor Column" },
                new RoadColumnTag(),
                WorldPositionCm.FromCm(330, 170),
                new Position2D { Value = Fix64Vec2.FromInt(330, 170) },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(600),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1200)
                },
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            int moveSpeedId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("MoveSpeed");
            attributes.SetBase(moveSpeedId, 1200f);

            Order routeOrder = CreateRouteOrder(actor, roadMoveFollowOrderTypeId, (0, 0), (250, 120), (500, 240));
            routeOrder.OrderId = 44;
            routeOrder.Args.Spatial.A0 = 2;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in routeOrder, priority: 100);
            var plans = new RoadNavPlanStore();
            var runtime = new RoadMoveRuntimeService(world, plans);
            Assert.That(runtime.TryBindActiveOrder(actor, in routeOrder, preserveTimeoutCount: false, out _, out _), Is.True);
            ref var planRuntime = ref world.Get<RoadNavPlanRuntime>(actor);
            planRuntime.CurrentWaypointIndex = 1;

            var system = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime);
            system.Update(0.1f);

            ref readonly var activeOrder = ref world.Get<OrderBuffer>(actor).ActiveOrder.Order;
            ref readonly var runtimeState = ref world.Get<RoadNavPlanRuntime>(actor);
            Assert.That(activeOrder.Args.Spatial.A0, Is.EqualTo(2), "Follow execution must not overwrite authored order payload.");
            Assert.That(world.Get<RoadMoveOrderRuntime>(actor).ActiveOrderId, Is.EqualTo(44));
            Assert.That(runtimeState.CurrentWaypointIndex, Is.EqualTo(1));
        }

        [Test]
        public void RoadNavPlanStore_TryBindFromOrder_TrimsCurvedPrefixToProjectedActorPosition()
        {
            using var world = World.Create();
            Entity actor = world.Create(
                new Position2D { Value = Fix64Vec2.FromInt(40, 340) },
                WorldPositionCm.FromCm(40, 340));

            Order routeOrder = CreateRouteOrder(actor, roadMoveFollowOrderTypeId: 171, (-600, -120), (-300, 80), (0, 300), (300, 620), (600, 900));
            var plans = new RoadNavPlanStore();

            Assert.That(plans.TryBindFromOrder(actor, in routeOrder, Fix64Vec2.FromInt(40, 340), out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, routeOrder.OrderId, out RoadNavPlanView plan), Is.True);
            Assert.That(plan.Count, Is.EqualTo(3), "Execution slice should drop the curved path prefix that is already behind the actor and keep only the local projected start plus the remaining suffix.");
            Assert.That(plan.TryGetWaypoint(0, out Fix64Vec2 projectedStart), Is.True);
            Assert.That(projectedStart.X.ToInt(), Is.EqualTo(39).Within(1));
            Assert.That(projectedStart.Y.ToInt(), Is.EqualTo(341).Within(1));
            Assert.That(plan.TryGetWaypoint(1, out Fix64Vec2 firstForwardWaypoint), Is.True);
            Assert.That(firstForwardWaypoint.X.ToInt(), Is.EqualTo(300));
            Assert.That(firstForwardWaypoint.Y.ToInt(), Is.EqualTo(620));
            Assert.That(plan.TryGetWaypoint(2, out Fix64Vec2 secondForwardWaypoint), Is.True);
            Assert.That(secondForwardWaypoint.X.ToInt(), Is.EqualTo(600));
            Assert.That(secondForwardWaypoint.Y.ToInt(), Is.EqualTo(900));
        }

        [Test]
        public void RoadMoveRuntimeService_RebindsCurvedRoute_FromProjectedLocalSliceForEachDirection()
        {
            using var world = World.Create();
            Entity actor = world.Create(
                new RoadColumnTag(),
                WorldPositionCm.FromCm(40, 340),
                new Position2D { Value = Fix64Vec2.FromInt(40, 340) },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(600),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1200)
                },
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer());

            Order eastbound = CreateRouteOrder(actor, roadMoveFollowOrderTypeId: 171, (-600, -120), (-300, 80), (0, 300), (300, 620), (600, 900));
            eastbound.OrderId = 101;
            Order westbound = CreateRouteOrder(actor, roadMoveFollowOrderTypeId: 171, (600, 900), (300, 620), (0, 300), (-300, 80), (-600, -120));
            westbound.OrderId = 102;

            var plans = new RoadNavPlanStore();
            var runtime = new RoadMoveRuntimeService(world, plans);
            var selection = new RoadRouteSelectionStrategy();
            Fix64Vec2 actorPosition = world.Get<Position2D>(actor).Value;

            Assert.That(runtime.TryBindActiveOrder(actor, in eastbound, preserveTimeoutCount: false, out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, eastbound.OrderId, out RoadNavPlanView eastPlan), Is.True);
            Assert.That(selection.TrySelect(in eastPlan, actorPosition, currentWaypointIndex: 0, stopRadiusCm: 40f, out RoadRouteSelection eastSelection), Is.True);
            Assert.That(eastSelection.Target.X.ToFloat(), Is.GreaterThan(actorPosition.X.ToFloat()), "Eastbound rebind should immediately target the local forward suffix, not a waypoint from the curved prefix behind the actor.");

            Assert.That(runtime.TryBindActiveOrder(actor, in westbound, preserveTimeoutCount: false, out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, westbound.OrderId, out RoadNavPlanView westPlan), Is.True);
            Assert.That(selection.TrySelect(in westPlan, actorPosition, currentWaypointIndex: 0, stopRadiusCm: 40f, out RoadRouteSelection westSelection), Is.True);
            Assert.That(westSelection.Target.X.ToFloat(), Is.LessThan(actorPosition.X.ToFloat()), "Westbound rebind should immediately target the local reverse suffix, instead of twitching back to the old curved prefix.");
        }

        [Test]
        public void RoadRouteComputeService_CreateFollowOrder_PreservesOriginalFinalDestinationBeyondSampledPrefix()
        {
            Order sourceOrder = CreateMoveOrder(Entity.Null, orderTypeId: 102, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            var pathXcm = new int[OrderSpatial.MaxPoints];
            var pathYcm = new int[OrderSpatial.MaxPoints];
            for (int i = 0; i < OrderSpatial.MaxPoints; i++)
            {
                pathXcm[i] = -9000 + (i * 300);
                pathYcm[i] = 0;
            }

            var compute = new RoadRouteComputeService(roadMoveFollowOrderTypeId: 171);
            Order followOrder = compute.CreateFollowOrder(
                in sourceOrder,
                pathXcm,
                pathYcm,
                OrderSpatial.MaxPoints,
                new Vector3(18000f, 0f, 0f));

            Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(in followOrder, out var sampledDestination), Is.True);
            Assert.That(sampledDestination.X, Is.Not.EqualTo(18000f), "The sampled prefix intentionally ends before the player's true click target in this regression test.");
            Assert.That(RoadRouteFinalTargetResolver.TryResolve(in followOrder, out var preservedDestination), Is.True);
            Assert.That(preservedDestination.X, Is.EqualTo(18000f));
            Assert.That(preservedDestination.Z, Is.EqualTo(0f));
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_UsesProjectedQueuedOrigin_ForFollowUpRoadMove()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 16);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore, new[]
            {
                (500, 0),
                (700, 100),
            });
            var globals = CreateGlobals(pathService, pathStore, moveToOrderTypeId: 55);
            Entity actor = world.Create(
                new RoadColumnTag(),
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            orderBuffer.SetActiveDirect(CreateMoveOrder(actor, orderTypeId: 55, xcm: 300, ycm: 0, submitMode: OrderSubmitMode.Immediate), priority: 60);
            Assert.That(orderBuffer.Enqueue(CreateMoveOrder(actor, orderTypeId: 55, xcm: 500, ycm: 0, submitMode: OrderSubmitMode.Queued), priority: 60, expireStep: -1, insertStep: 1), Is.True);

            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var followUpOrder = CreateMoveOrder(actor, orderTypeId: 55, xcm: 700, ycm: 100, submitMode: OrderSubmitMode.Queued);

            Assert.That(expander.TrySubmit(in followUpOrder), Is.True);
            Assert.That(pathService.Requests.Count, Is.EqualTo(1));
            Assert.That(pathService.Requests[0].Start.Xcm, Is.EqualTo(500));
            Assert.That(pathService.Requests[0].Start.Ycm, Is.EqualTo(0));
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_PrimesLoadedChunks_ForFarRoadDestination()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);
            loadedChunks.ChunkLoaded += chunkKey =>
            {
                if (scenario.TryGetGraphChunk(chunkKey, out var chunk))
                {
                    store.AddOrReplace(chunkKey, chunk);
                }
            };

            loadedChunks.Update(centerXcm: -9800, centerYcm: 0, radiusCm: scenario.StreamingRadiusCm);
            int initialChunkCount = loadedChunks.ActiveChunkKeys.Count;

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: chunkSizeCm / 2);
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 16);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: OrderSpatial.MaxPoints);
            var globals = CreateGlobals(
                new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(), pathStore, CreatePathingConfig()),
                pathStore,
                moveToOrderTypeId: 77);
            globals[CoreServiceKeys.LoadedChunks.Name] = loadedChunks;

            Entity actor = world.Create(
                new RoadColumnTag(),
                WorldPositionCm.FromCm(-9800, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = CreateMoveOrder(actor, orderTypeId: 77, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);

            Assert.That(expander.TrySubmit(in order), Is.True);
            Assert.That(loadedChunks.ActiveChunkKeys.Count, Is.GreaterThan(initialChunkCount));
            Assert.That(runtime.CurrentGraph.NodeCount, Is.GreaterThan(100));
            Assert.That(orderQueue.TryDequeue(out var routeOrder), Is.True);
            Assert.That(routeOrder.Args.Spatial.PointCount, Is.GreaterThan(20));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(in routeOrder, out var finalPoint), Is.True);
            Assert.That(finalPoint.X, Is.EqualTo(18000f));
            Assert.That(finalPoint.Z, Is.EqualTo(0f));
        }

        [Test]
        public void RoadNetworkShowcaseScenario_LoadedCenterWindow_StreamsChunkedGraphAndFindsRoadPath()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);
            loadedChunks.ChunkLoaded += chunkKey =>
            {
                if (scenario.TryGetGraphChunk(chunkKey, out var chunk))
                {
                    store.AddOrReplace(chunkKey, chunk);
                }
            };

            loadedChunks.Update(centerXcm: 0, centerYcm: 0, radiusCm: scenario.StreamingRadiusCm);

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: chunkSizeCm / 2);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 64);
            var service = new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(), pathStore, CreatePathingConfig());
            var request = new PathRequest(
                requestId: 1,
                actor: default,
                domain: PathDomain.Auto,
                agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                start: PathEndpoint.FromWorldCm(-9000, 0),
                goal: PathEndpoint.FromWorldCm(18000, 0),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 64));

            Assert.That(loadedChunks.ActiveChunkKeys.Count, Is.EqualTo(25));
            Assert.That(runtime.CurrentGraph.NodeCount, Is.GreaterThan(100));

            int loadedSplineCount = 0;
            foreach (long chunkKey in loadedChunks.ActiveChunkKeys)
            {
                if (scenario.TryGetRoadSplineChunk(chunkKey, out var splines))
                {
                    loadedSplineCount += splines.Length;
                }
            }

            Assert.That(loadedSplineCount, Is.EqualTo(11));
            Assert.That(service.TrySolve(in request, out var result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found));

            var xs = new int[64];
            var ys = new int[64];
            Assert.That(service.TryCopyPath(in result.Handle, xs, ys, out int count), Is.True);
            Assert.That(count, Is.GreaterThan(20));
            Assert.That(xs[0], Is.EqualTo(-9000));
            Assert.That(xs[count - 1], Is.EqualTo(18000));
            pathStore.Release(result.Handle);
        }

        [Test]
        public void AutoPathService_PreferGraph_PreservesGraphNotReadyWithoutMeshOverride()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);
            loadedChunks.ChunkLoaded += chunkKey =>
            {
                if (scenario.TryGetGraphChunk(chunkKey, out var chunk))
                {
                    store.AddOrReplace(chunkKey, chunk);
                }
            };

            loadedChunks.Update(centerXcm: 0, centerYcm: 0, radiusCm: scenario.StreamingRadiusCm);

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: chunkSizeCm / 2);
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 64);
            var service = new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(), pathStore, CreatePathingConfig());
            var request = new PathRequest(
                requestId: 2,
                actor: default,
                domain: PathDomain.Auto,
                agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                start: PathEndpoint.FromWorldCm(-9000, 0),
                goal: PathEndpoint.FromWorldCm(45000, 45000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 16));

            Assert.That(service.TrySolve(in request, out var result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.NotReady));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_EngineBootstrapsGraphOnlyAutoPathService_AndFindsRoadPath()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Assert.That(engine.CurrentMapSession!.PrimaryBoard, Is.TypeOf<NodeGraphBoard>());
            Assert.That(engine.GetService(CoreServiceKeys.PathService), Is.TypeOf<AutoPathService>());
            Assert.That(
                engine.MergedConfig.Navigation2D.Steering.SmartStop.Enabled,
                Is.False,
                "Road-network waypoint following must not inherit crowd queue SmartStop, or columns can halt behind shared intermediate road nodes.");

            var board = (NodeGraphBoard)engine.CurrentMapSession.PrimaryBoard;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(board.LoadedChunksSource.ChunkSizeCm);
            board.LoadedChunksSource.ChunkLoaded += chunkKey =>
            {
                if (scenario.TryGetGraphChunk(chunkKey, out var chunk))
                {
                    board.GraphStore.AddOrReplace(chunkKey, chunk);
                }
            };

            board.LoadedChunksSource.Update(centerXcm: 0, centerYcm: 0, radiusCm: scenario.StreamingRadiusCm);

            var pathService = engine.GetService(CoreServiceKeys.PathService)!;
            var pathStore = engine.GetService(CoreServiceKeys.PathStore)!;
            var request = new PathRequest(
                requestId: 3,
                actor: default,
                domain: PathDomain.Auto,
                agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                start: PathEndpoint.FromWorldCm(-9000, 0),
                goal: PathEndpoint.FromWorldCm(18000, 0),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 64));

            Assert.That(pathService.TrySolve(in request, out var result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(pathStore.IsAlive(result.Handle), Is.True);
            pathStore.Release(result.Handle);
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_HandleMapFocused_PrimesInteractiveBootstrapState()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByName(engine.World, "Blue Vanguard");
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.LoadedChunkCount, Is.GreaterThan(0), "Initial showcase focus should prime the first chunk window so the first move command does not depend on a later streaming tick.");
            Assert.That(runtime.LoadedNodeCount, Is.GreaterThan(0), "Chunk priming should populate the graph store before the player issues the first road move.");
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj), Is.True);
            Assert.That(localObj, Is.EqualTo(owner));
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionViewViewerEntity.Name, out object? viewOwnerObj), Is.True);
            Assert.That(viewOwnerObj, Is.EqualTo(owner));
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectionViewKey.Name, out object? viewSetObj), Is.True);
            Assert.That(viewSetObj, Is.EqualTo(SelectionViewKeys.Primary));
            var selection = (SelectionRuntime)engine.GetService(CoreServiceKeys.SelectionRuntime)!;
            Assert.That(selection.TryGetPrimary(owner, SelectionSetKeys.Ambient, out Entity primary), Is.True);
            Assert.That(primary, Is.EqualTo(owner));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_UpdateLoadedChunks_RepairsLocalPlayerAndSeedsAmbientSelectionWithoutReset()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByName(engine.World, "Blue Vanguard");
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.GetService(CoreServiceKeys.SelectionRuntime), Is.TypeOf<SelectionRuntime>());
            var selection = (SelectionRuntime)engine.GetService(CoreServiceKeys.SelectionRuntime)!;

            selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, System.Array.Empty<Entity>());
            engine.GlobalContext.Remove(CoreServiceKeys.LocalPlayerEntity.Name);

            runtime.UpdateLoadedChunks(engine);

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj), Is.True);
            Assert.That(localObj, Is.EqualTo(owner));
            Assert.That(selection.TryGetPrimary(owner, SelectionSetKeys.Ambient, out Entity repairedPrimary), Is.True);
            Assert.That(repairedPrimary, Is.EqualTo(owner));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_UpdateLoadedChunks_DoesNotOverwriteValidAmbientSelection()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByName(engine.World, "Blue Vanguard");
            Entity selected = FindEntityByName(engine.World, "Blue North Column");
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(selected, Is.Not.EqualTo(Entity.Null));
            var selection = (SelectionRuntime)engine.GetService(CoreServiceKeys.SelectionRuntime)!;

            Span<Entity> selectedUnits = stackalloc Entity[1];
            selectedUnits[0] = selected;
            Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, selectedUnits), Is.True);
            engine.GlobalContext.Remove(CoreServiceKeys.LocalPlayerEntity.Name);

            runtime.UpdateLoadedChunks(engine);

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj), Is.True);
            Assert.That(localObj, Is.EqualTo(owner));
            Assert.That(selection.TryGetPrimary(owner, SelectionSetKeys.Ambient, out Entity preservedPrimary), Is.True);
            Assert.That(preservedPrimary, Is.EqualTo(selected));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_BuildPanelState_FollowsAmbientSelectionPrimary()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByName(engine.World, "Blue Vanguard");
            Entity selected = FindEntityByName(engine.World, "Blue North Column");
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(selected, Is.Not.EqualTo(Entity.Null));

            var selection = (SelectionRuntime)engine.GetService(CoreServiceKeys.SelectionRuntime)!;
            Span<Entity> selectedUnits = stackalloc Entity[1];
            selectedUnits[0] = selected;
            Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, selectedUnits), Is.True);

            RoadNetworkShowcasePanelState panel = runtime.BuildPanelState(engine);
            Assert.That(panel.Selection, Does.Contain("Blue North Column"));
            Assert.That(panel.Actors.Length, Is.EqualTo(1));
            Assert.That(panel.Actors[0].Header, Does.Contain("Blue North Column"));
            Assert.That(panel.Actors[0].Query, Does.Contain("planner="));
        }

        [Test]
        public void RoadNetworkShowcase_PlayableInitialMultiSelectionRightClick_StartsRoadMoveWithoutReset()
        {
            using var engine = CreatePlayableRoadShowcaseEngine();
            LoadPlayableMap(engine, engine.MergedConfig.StartupMapId);

            var backend = GetInputBackend(engine);
            TickUntil(
                engine,
                () => GetSelectionCount(engine) >= 1 &&
                      string.Equals(GetSelectedEntityName(engine), "Blue Vanguard", StringComparison.Ordinal),
                maxFrames: 12);

            Entity owner = GetLocalPlayer(engine);
            Entity vanguard = FindEntityByName(engine.World, "Blue Vanguard");
            Entity north = FindEntityByName(engine.World, "Blue North Column");
            Entity south = FindEntityByName(engine.World, "Blue South Column");
            var selection = (SelectionRuntime)engine.GetService(CoreServiceKeys.SelectionRuntime)!;
            Span<Entity> selectedUnits = stackalloc Entity[3];
            selectedUnits[0] = vanguard;
            selectedUnits[1] = north;
            selectedUnits[2] = south;
            Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, selectedUnits), Is.True);
            Tick(engine, 2);
            Assert.That(GetSelectionCount(engine), Is.EqualTo(3), BuildPlayableMoveDiagnostics(engine, "Blue Vanguard", "Blue North Column", "Blue South Column"));

            Vector2 vanguardStart = ReadWorldPosition(engine.World, "Blue Vanguard");
            Vector2 northStart = ReadWorldPosition(engine.World, "Blue North Column");
            Vector2 southStart = ReadWorldPosition(engine.World, "Blue South Column");

            RightClickWorld(engine, backend, FindVisibleGroundScreenPoint(engine));

            TickUntil(
                engine,
                () => HasNonZeroDesiredVelocity(engine, "Blue Vanguard") &&
                      HasNonZeroDesiredVelocity(engine, "Blue North Column") &&
                      HasNonZeroDesiredVelocity(engine, "Blue South Column"),
                maxFrames: 20,
                failureMessage: BuildPlayableMoveDiagnostics(engine, "Blue Vanguard", "Blue North Column", "Blue South Column"));

            Tick(engine, 30);

            Vector2 vanguardEnd = ReadWorldPosition(engine.World, "Blue Vanguard");
            Vector2 northEnd = ReadWorldPosition(engine.World, "Blue North Column");
            Vector2 southEnd = ReadWorldPosition(engine.World, "Blue South Column");
            string diagnostics = BuildPlayableMoveDiagnostics(engine, "Blue Vanguard", "Blue North Column", "Blue South Column");

            Assert.That(Vector2.Distance(vanguardEnd, vanguardStart), Is.GreaterThan(120f), diagnostics);
            Assert.That(Vector2.Distance(northEnd, northStart), Is.GreaterThan(120f), diagnostics);
            Assert.That(Vector2.Distance(southEnd, southStart), Is.GreaterThan(120f), diagnostics);
        }

        [Test]
        public void RoadNetworkShowcase_PlayableInitialDragSelectRightClick_StartsRoadMoveWithoutReset()
        {
            using var engine = CreatePlayableRoadShowcaseEngine();
            LoadPlayableMap(engine, engine.MergedConfig.StartupMapId);

            var backend = GetInputBackend(engine);
            TickUntil(
                engine,
                () => GetSelectionCount(engine) >= 1 &&
                      string.Equals(GetSelectedEntityName(engine), "Blue Vanguard", StringComparison.Ordinal),
                maxFrames: 12);

            DragSelectNamed(engine, backend, "Blue Vanguard", "Blue North Column", "Blue South Column");
            Assert.That(GetSelectionCount(engine), Is.EqualTo(3), BuildPlayableMoveDiagnostics(engine, "Blue Vanguard", "Blue North Column", "Blue South Column"));

            Vector2 vanguardStart = ReadWorldPosition(engine.World, "Blue Vanguard");
            Vector2 northStart = ReadWorldPosition(engine.World, "Blue North Column");
            Vector2 southStart = ReadWorldPosition(engine.World, "Blue South Column");

            RightClickWorld(engine, backend, FindVisibleGroundScreenPoint(engine));

            bool startedMoving = false;
            for (int i = 0; i < 20; i++)
            {
                if (HasNonZeroDesiredVelocity(engine, "Blue Vanguard") &&
                    HasNonZeroDesiredVelocity(engine, "Blue North Column") &&
                    HasNonZeroDesiredVelocity(engine, "Blue South Column"))
                {
                    startedMoving = true;
                    break;
                }

                Tick(engine, 1);
            }

            Assert.That(
                startedMoving,
                Is.True,
                BuildPlayableMoveDiagnostics(engine, "Blue Vanguard", "Blue North Column", "Blue South Column"));

            Tick(engine, 30);

            Vector2 vanguardEnd = ReadWorldPosition(engine.World, "Blue Vanguard");
            Vector2 northEnd = ReadWorldPosition(engine.World, "Blue North Column");
            Vector2 southEnd = ReadWorldPosition(engine.World, "Blue South Column");
            string diagnostics = BuildPlayableMoveDiagnostics(engine, "Blue Vanguard", "Blue North Column", "Blue South Column");

            Assert.That(Vector2.Distance(vanguardEnd, vanguardStart), Is.GreaterThan(120f), diagnostics);
            Assert.That(Vector2.Distance(northEnd, northStart), Is.GreaterThan(120f), diagnostics);
            Assert.That(Vector2.Distance(southEnd, southStart), Is.GreaterThan(120f), diagnostics);
        }

        [Test]
        public void RoadNetworkShowcase_EngineFarRoadMove_ReachesDestinationWithoutStoppingMidRoute()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            Entity actor = FindEntityByName(engine.World, "Blue Vanguard");
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null);
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj), Is.True, "GameConfig service must be present in GlobalContext for road order expansion.");
            Assert.That(configObj, Is.TypeOf<GameConfig>(), $"Unexpected GameConfig service type: {configObj?.GetType().FullName ?? "<null>"}");
            var runtimeConfig = (GameConfig)configObj!;
            Assert.That(runtimeConfig.Constants.OrderTypeIds.TryGetValue("moveTo", out int runtimeMoveToOrderTypeId), Is.True, "moveTo order type must be exposed through GameConfig service.");
            Assert.That(runtimeMoveToOrderTypeId, Is.GreaterThan(0), "moveTo order type id from GameConfig service must be positive.");

            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                orderQueue!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                statusKey: string.Empty);

            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.True);
            Assert.That(orderQueue.Count, Is.EqualTo(1), "Expanded road move should enter the engine order queue before the next fixed-step drain.");

            int furthestXcm = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2().X;
            bool completed = false;
            bool movementStarted = false;
            for (int i = 0; i < 2400; i++)
            {
                engine.Tick(1f / 60f);

                bool hasActiveOrder = engine.World.Get<OrderBuffer>(actor).HasActive;
                if (!movementStarted && (hasActiveOrder || orderQueue.Count == 0))
                {
                    movementStarted = true;
                }

                int currentXcm = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2().X;
                if (currentXcm > furthestXcm)
                {
                    furthestXcm = currentXcm;
                }

                if (movementStarted && !hasActiveOrder && orderQueue.Count == 0)
                {
                    completed = true;
                    break;
                }
            }

            var finalPosition = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            Assert.That(movementStarted, Is.True, "Road move never entered the fixed-step movement pipeline.");
            Assert.That(completed, Is.True, $"Road move should complete instead of stalling mid-route. FurthestX={furthestXcm}, Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}");
            Assert.That(furthestXcm, Is.GreaterThan(17000), $"Column should traverse the full eastward road route, not stop in the currently loaded chunk window. Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}");
            Assert.That(finalPosition.X, Is.EqualTo(18000).Within(80));
            Assert.That(finalPosition.Y, Is.EqualTo(0).Within(80));
        }

        [Test]
        public void RoadNetworkShowcase_EngineCentralRoadMove_DoesNotBacktrackToBehindSampledWaypoint()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            Entity actor = FindEntityByName(engine.World, "Blue Vanguard");
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null);

            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                orderQueue!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                statusKey: string.Empty);

            var startPosition = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 0, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.True);

            int minXcm = startPosition.X;
            int maxXcm = startPosition.X;
            for (int i = 0; i < 240; i++)
            {
                engine.Tick(1f / 60f);
                int currentXcm = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2().X;
                if (currentXcm < minXcm)
                {
                    minXcm = currentXcm;
                }

                if (currentXcm > maxXcm)
                {
                    maxXcm = currentXcm;
                }

                if (maxXcm >= startPosition.X + 600)
                {
                    break;
                }
            }

            Assert.That(maxXcm, Is.GreaterThanOrEqualTo(startPosition.X + 600), $"Road route should visibly advance east along the authored road. StartX={startPosition.X}, MaxX={maxXcm}, MinX={minXcm}");
            Assert.That(minXcm, Is.GreaterThanOrEqualTo(startPosition.X - 20), $"Road route should not force the actor to backtrack to a sampled waypoint behind its current road progress. StartX={startPosition.X}, MinX={minXcm}, MaxX={maxXcm}");
        }

        [Test]
        public void RoadNetworkShowcase_EngineBranchRoadQuery_BuildsFollowRouteForBranchClick()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            Entity actor = FindEntityByName(engine.World, "Blue Vanguard");
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null);

            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                orderQueue!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId);

            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: -2720, ycm: 3810, submitMode: OrderSubmitMode.Immediate);
            bool shouldExpand = expander.ShouldExpandRoadMove(in order, out int resolvedMoveToOrderTypeId);
            string gateStatus = engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? gateStatusObj) && gateStatusObj is string gateStatusText
                ? gateStatusText
                : "<missing>";
            Assert.That(shouldExpand, Is.True, $"Road move expander should recognize Blue Vanguard move orders. ResolvedMoveToOrderTypeId={resolvedMoveToOrderTypeId}; Status={gateStatus}");
            bool built = expander.TryBuildFollowOrder(in order, out Order routeOrder);
            string status = engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? statusObj) && statusObj is string statusText
                ? statusText
                : "<missing>";
            Assert.That(built, Is.True, $"Branch clicks should resolve to a sampled road-follow route instead of failing path copy. Status={status}");
            Assert.That(routeOrder.OrderTypeId, Is.EqualTo(engine.MergedConfig.Constants.OrderTypeIds[RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey]));
            Assert.That(routeOrder.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.List));
            Assert.That(routeOrder.Args.Spatial.PointCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(in routeOrder, out var resolvedDestination), Is.True);
            Assert.That(resolvedDestination.Z, Is.GreaterThan(2500f), "Branch clicks should snap onto the northern branch road instead of collapsing back to the origin road sample.");
            float dx = resolvedDestination.X - (-2720f);
            float dz = resolvedDestination.Z - 3810f;
            float distanceToClickCm = System.MathF.Sqrt((dx * dx) + (dz * dz));
            Assert.That(distanceToClickCm, Is.LessThanOrEqualTo(2000f), $"Resolved branch destination should stay near the clicked road sample after snapping to authored road nodes. Destination=({resolvedDestination.X},{resolvedDestination.Z})");
        }

        [Test]
        public void RoadNetworkShowcase_EngineNorthColumnRoadMove_ReachesDestination()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            Entity actor = FindEntityByName(engine.World, "Blue North Column");
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null);

            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                orderQueue!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                statusKey: string.Empty);

            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 0, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.True);

            var startPosition = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            int furthestXcm = startPosition.X;
            bool movementStarted = false;
            bool completed = false;
            for (int i = 0; i < 1800; i++)
            {
                engine.Tick(1f / 60f);

                int currentXcm = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2().X;
                if (currentXcm > furthestXcm)
                {
                    furthestXcm = currentXcm;
                }

                if (!movementStarted && furthestXcm >= startPosition.X + 200)
                {
                    movementStarted = true;
                }

                if (movementStarted &&
                    !engine.World.Get<OrderBuffer>(actor).HasActive &&
                    orderQueue.Count == 0)
                {
                    completed = true;
                    break;
                }
            }

            var finalPosition = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            Assert.That(movementStarted, Is.True, $"Blue North Column should begin moving after a direct road command. Start=({startPosition.X},{startPosition.Y}) Final=({finalPosition.X},{finalPosition.Y})");
            Assert.That(completed, Is.True, $"Blue North Column should finish the submitted road move instead of timing out in place. Final=({finalPosition.X},{finalPosition.Y})");
            Assert.That(finalPosition.X, Is.EqualTo(0).Within(120));
            Assert.That(finalPosition.Y, Is.EqualTo(0).Within(120));
        }

        [Test]
        public void RoadNetworkShowcase_StrategyMatrix_WritesAcceptanceArtifacts_ForProfilesWeightsAndTraits()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadMap(engine.MergedConfig.StartupMapId);

            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            int roadMoveFollowOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds[RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                engine.GetService(CoreServiceKeys.OrderQueue)!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var profiles = new RoadRouteProfileCatalog(engine.World);

            Entity vanguard = FindEntityByName(engine.World, "Blue Vanguard");
            Entity north = FindEntityByName(engine.World, "Blue North Column");
            Entity south = FindEntityByName(engine.World, "Blue South Column");
            Assert.That(vanguard, Is.Not.EqualTo(Entity.Null));
            Assert.That(north, Is.Not.EqualTo(Entity.Null));
            Assert.That(south, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.Has<RoadMoveProfileRef>(vanguard), Is.True, "Blue Vanguard should carry a showcase road move profile component.");
            Assert.That(engine.World.Has<RoadMoveProfileRef>(north), Is.True, "Blue North Column should carry a showcase road move profile component.");
            Assert.That(engine.World.Has<RoadMoveProfileRef>(south), Is.True, "Blue South Column should carry a showcase road move profile component.");

            RoadMoveProfileRef vanguardProfile = engine.World.Get<RoadMoveProfileRef>(vanguard);
            RoadMoveProfileRef northProfile = engine.World.Get<RoadMoveProfileRef>(north);
            RoadMoveProfileRef southProfile = engine.World.Get<RoadMoveProfileRef>(south);
            Assert.That(vanguardProfile.PlannerPresetId, Is.EqualTo(1), $"Blue Vanguard should stay on preset 1 but loaded {vanguardProfile.PlannerPresetId}.");
            Assert.That(northProfile.PlannerPresetId, Is.EqualTo(2), $"Blue North Column should load planner preset 2 but loaded {northProfile.PlannerPresetId}.");
            Assert.That(southProfile.PlannerPresetId, Is.EqualTo(3), $"Blue South Column should load planner preset 3 but loaded {southProfile.PlannerPresetId}.");

            var planRows = new List<StrategyMatrixRow>(3);
            planRows.Add(CaptureStrategyMatrixRow(engine, expander, profiles, vanguard, "Blue Vanguard", moveToOrderTypeId, targetXcm: 9000, targetYcm: 0));
            planRows.Add(CaptureStrategyMatrixRow(engine, expander, profiles, north, "Blue North Column", moveToOrderTypeId, targetXcm: 9000, targetYcm: 0));
            planRows.Add(CaptureStrategyMatrixRow(engine, expander, profiles, south, "Blue South Column", moveToOrderTypeId, targetXcm: 9000, targetYcm: 0));

            Assert.That(planRows[0].AcceptedStatus, Does.Contain("Direct corridor"));
            Assert.That(planRows[1].AcceptedStatus, Does.Contain("North corridor"));
            Assert.That(planRows[2].AcceptedStatus, Does.Contain("South corridor"));

            Vector2 vanguardStart = engine.World.Get<WorldPositionCm>(vanguard).Value.ToVector2();
            Vector2 northStart = engine.World.Get<WorldPositionCm>(north).Value.ToVector2();
            Vector2 southStart = engine.World.Get<WorldPositionCm>(south).Value.ToVector2();

            ActivateExecutionSliceRoute(engine.World, vanguard, roadMoveFollowOrderTypeId, (-9000, 0), (-6000, 0), (-3000, 0), (0, 0));
            ActivateExecutionSliceRoute(engine.World, north, roadMoveFollowOrderTypeId, (-9000, 0), (-6000, 0), (-3000, 0), (0, 0));
            ActivateExecutionSliceRoute(engine.World, south, roadMoveFollowOrderTypeId, (-9000, 0), (-6000, 0), (-3000, 0), (0, 0));

            for (int tick = 0; tick < 180; tick++)
            {
                engine.Tick(1f / 60f);
            }

            Vector2 vanguardEnd = engine.World.Get<WorldPositionCm>(vanguard).Value.ToVector2();
            Vector2 northEnd = engine.World.Get<WorldPositionCm>(north).Value.ToVector2();
            Vector2 southEnd = engine.World.Get<WorldPositionCm>(south).Value.ToVector2();

            float vanguardAdvanceCm = Vector2.Distance(vanguardStart, vanguardEnd);
            float northAdvanceCm = Vector2.Distance(northStart, northEnd);
            float southAdvanceCm = Vector2.Distance(southStart, southEnd);

            float minAdvanceCm = System.MathF.Min(vanguardAdvanceCm, System.MathF.Min(northAdvanceCm, southAdvanceCm));
            float maxAdvanceCm = System.MathF.Max(vanguardAdvanceCm, System.MathF.Max(northAdvanceCm, southAdvanceCm));
            Assert.That(maxAdvanceCm - minAdvanceCm, Is.GreaterThan(180f), $"Execution presets should produce visibly distinct movement envelopes over the same corridor slice. Vanguard={vanguardAdvanceCm:F0} North={northAdvanceCm:F0} South={southAdvanceCm:F0}");

            string outputDir = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "road_network_showcase_strategy_matrix");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "battle-report.md"), BuildStrategyMatrixBattleReport(planRows, vanguardAdvanceCm, northAdvanceCm, southAdvanceCm));
            File.WriteAllText(Path.Combine(outputDir, "trace.jsonl"), BuildStrategyMatrixTrace(planRows, vanguardAdvanceCm, northAdvanceCm, southAdvanceCm));
            File.WriteAllText(Path.Combine(outputDir, "path.mmd"), BuildStrategyMatrixPathMermaid());
            File.WriteAllText(Path.Combine(outputDir, "summary.json"), BuildStrategyMatrixSummary(planRows, vanguardAdvanceCm, northAdvanceCm, southAdvanceCm));
        }

        [Test]
        public void RoadNetworkShowcase_TimeoutAcceptance_WritesArtifacts_ForRefreshAndAbandonBranches()
        {
            TimeoutAcceptanceResult refreshed = RunTimeoutAcceptanceCase(new RecordingPathService(
                new PathStore(maxPaths: 8, maxPointsPerPath: 8),
                new[]
                {
                    (0, 0),
                    (300, 0),
                    (600, 0),
                }));
            TimeoutAcceptanceResult abandoned = RunTimeoutAcceptanceCase(new FailingPathService());

            Assert.That(refreshed.Status, Does.Contain("refreshed after timeout"), $"Expected timeout refresh branch to fire. Status={refreshed.Status}");
            Assert.That(refreshed.HasActiveOrder, Is.True, "Successful timeout refresh should keep the road-follow order alive.");
            Assert.That(abandoned.Status, Does.Contain("abandoned after"), $"Expected timeout abandon branch to fire. Status={abandoned.Status}");
            Assert.That(abandoned.HasActiveOrder, Is.False, "Failed timeout refresh should abandon and clear the active road-follow order.");

            string outputDir = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "road_network_showcase_timeout");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "battle-report.md"), BuildTimeoutBattleReport(refreshed, abandoned));
            File.WriteAllText(Path.Combine(outputDir, "trace.jsonl"), BuildTimeoutTrace(refreshed, abandoned));
            File.WriteAllText(Path.Combine(outputDir, "path.mmd"), BuildTimeoutPathMermaid());
            File.WriteAllText(Path.Combine(outputDir, "summary.json"), BuildTimeoutSummary(refreshed, abandoned));
        }

        private static Dictionary<string, object> CreateGlobals(IPathService pathService, PathStore pathStore, int moveToOrderTypeId, int roadMoveFollowOrderTypeId = 171)
        {
            return new Dictionary<string, object>
            {
                [Ludots.Core.Scripting.CoreServiceKeys.PathService.Name] = pathService,
                [Ludots.Core.Scripting.CoreServiceKeys.PathStore.Name] = pathStore,
                [Ludots.Core.Scripting.CoreServiceKeys.GameConfig.Name] = new GameConfig
                {
                    Constants = new GameConstants
                    {
                        OrderTypeIds = new Dictionary<string, int>
                        {
                            ["moveTo"] = moveToOrderTypeId,
                            [RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey] = roadMoveFollowOrderTypeId,
                        }
                    }
                }
            };
        }

        private static TimeoutAcceptanceResult RunTimeoutAcceptanceCase(IPathService pathService)
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            PathStore pathStore = pathService is RecordingPathService recording
                ? recording.Store
                : new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            Dictionary<string, object> globals = CreateGlobals(pathService, pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);
            OrderTypeRegistry orderTypes = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId);

            Entity actor = world.Create(
                new Name { Value = "Timeout Column" },
                new RoadColumnTag(),
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(600),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1200)
                },
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            int moveSpeedId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("MoveSpeed");
            attributes.SetBase(moveSpeedId, 1200f);

            int[] pathXcm = { 0, 300, 600 };
            int[] pathYcm = { 0, 0, 0 };
            var compute = new RoadRouteComputeService(roadMoveFollowOrderTypeId);
            Order sourceOrder = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 600, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Order followOrder = compute.CreateFollowOrder(in sourceOrder, pathXcm, pathYcm, pathXcm.Length, new Vector3(600f, 0f, 0f));
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in followOrder, priority: 100);

            var plans = new RoadNavPlanStore();
            var runtime = new RoadMoveRuntimeService(world, plans);
            var bindSystem = new RoadMoveOrderBindingSystem(world, roadMoveFollowOrderTypeId, plans, runtime);
            var selectionSystem = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime);
            var executionSystem = new RoadMoveExecutionSystem(world);
            var lifecycleSystem = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime);
            string status = string.Empty;
            for (int step = 0; step < 12; step++)
            {
                bindSystem.Update(0.5f);
                selectionSystem.Update(0.5f);
                executionSystem.Update(0.5f);
                lifecycleSystem.Update(0.5f);
                status = globals.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? statusObj) && statusObj is string statusText
                    ? statusText
                    : string.Empty;

                if (status.Contains("refreshed after timeout", StringComparison.OrdinalIgnoreCase) ||
                    status.Contains("abandoned after", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            return new TimeoutAcceptanceResult(
                Status: status,
                HasActiveOrder: world.Get<OrderBuffer>(actor).HasActive);
        }

        [Test]
        public void RoadMoveShowcaseSystems_DoNotCompleteForeignActiveOrder_WhenStaleRoadRuntimeComponentsRemain()
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            Dictionary<string, object> globals = CreateGlobals(new FailingPathService(), pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);
            OrderTypeRegistry orderTypes = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId);

            Entity actor = world.Create(
                new Name { Value = "Foreign Order Column" },
                new RoadColumnTag(),
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(600),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1200)
                },
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer(),
                default(RoadMoveOrderRuntime),
                default(RoadNavPlanRuntime),
                default(RoadMoveExecutionIntent));

            Order moveOrder = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 600, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            moveOrder.OrderId = 9001;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in moveOrder, priority: 100);

            var plans = new RoadNavPlanStore();
            var runtime = new RoadMoveRuntimeService(world, plans);
            var selectionSystem = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime);
            var lifecycleSystem = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime);

            selectionSystem.Update(0.1f);
            lifecycleSystem.Update(0.1f);

            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.True, "Showcase road systems must not complete a foreign active order just because stale road runtime components remain on the entity.");
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderTypeId, Is.EqualTo(moveToOrderTypeId));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(9001));
        }

        [Test]
        public void RoadMoveLifecycleSystem_TimeoutRefresh_ReplacesActiveOrderPayloadWithRebuiltRoute()
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore, new[]
            {
                (0, 0),
                (160, 220),
                (420, 520),
                (600, 600),
            });
            Dictionary<string, object> globals = CreateGlobals(pathService, pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);
            OrderTypeRegistry orderTypes = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId);

            Entity actor = world.Create(
                new Name { Value = "Refresh Column" },
                new RoadColumnTag(),
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(600),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1200)
                },
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            int moveSpeedId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("MoveSpeed");
            attributes.SetBase(moveSpeedId, 1200f);

            Order staleRoute = CreateRouteOrder(actor, roadMoveFollowOrderTypeId, (0, 0), (300, 0), (600, 0));
            staleRoute.OrderId = 44;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in staleRoute, priority: 100);

            var plans = new RoadNavPlanStore();
            var runtime = new RoadMoveRuntimeService(world, plans);
            Assert.That(runtime.TryBindActiveOrder(actor, in staleRoute, preserveTimeoutCount: false, out _, out _), Is.True);

            ref var orderRuntime = ref world.Get<RoadMoveOrderRuntime>(actor);
            ref var planRuntime = ref world.Get<RoadNavPlanRuntime>(actor);
            orderRuntime.LifecycleState = RoadMoveLifecycleState.NeedsReplan;
            orderRuntime.TimeoutCount = 1;
            planRuntime.Initialized = 1;
            planRuntime.LastProgressPosition = Fix64Vec2.FromInt(0, 0);
            planRuntime.LastResolvedWaypointIndex = 0;

            var lifecycle = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime);
            lifecycle.Update(0.1f);

            ref readonly Order refreshedActive = ref world.Get<OrderBuffer>(actor).ActiveOrder.Order;
            Assert.That(refreshedActive.OrderId, Is.EqualTo(44));
            Assert.That(OrderWorldSpatialResolver.GetSpatialPointCount(in refreshedActive.Args.Spatial), Is.GreaterThanOrEqualTo(3));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(in refreshedActive, 1, out Vector3 refreshedWaypoint), Is.True);
            Assert.That(refreshedWaypoint.Z, Is.GreaterThan(0f), "Timeout refresh should replace the stale straight-line payload with the replanned curved road route.");
            Assert.That(world.Get<RoadMoveOrderRuntime>(actor).LifecycleState, Is.EqualTo(RoadMoveLifecycleState.Active));
        }

        [Test]
        public void Navigation2DSteeringSystem2D_PointGoal_WakesSleepingAgent()
        {
            using var world = World.Create();
            using var runtime = new Navigation2DRuntime(new Navigation2DConfig
            {
                Enabled = true,
                MaxAgents = 8
            }, gridCellSizeCm: 100, loadedChunks: null);

            Entity actor = world.Create(
                new NavAgent2D(),
                new Position2D { Value = Fix64Vec2.FromInt(0, 0) },
                Velocity2D.Zero,
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(600),
                    MaxAccelCmPerSec2 = Fix64.FromInt(1200),
                    RadiusCm = Fix64.FromInt(40),
                    NeighborDistCm = Fix64.FromInt(400),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 8
                },
                new NavGoal2D
                {
                    Kind = NavGoalKind2D.Point,
                    TargetCm = Fix64Vec2.FromInt(600, 0),
                    RadiusCm = Fix64.FromInt(30)
                },
                new SleepingTag(),
                new Motion
                {
                    SleepTimer = 99
                });

            var steering = new Navigation2DSteeringSystem2D(world, runtime);
            steering.Update(1f / 60f);

            Assert.That(world.Has<SleepingTag>(actor), Is.False, "Point-goal navigation should wake a sleeping body before physics integration, otherwise the first movement command can stall and timeout.");
            Assert.That(world.Get<Motion>(actor).SleepTimer, Is.EqualTo(0));
            Assert.That(world.Has<NavDesiredVelocity2D>(actor), Is.True);
        }

        private static OrderTypeRegistry CreateTimeoutOrderTypeRegistry(int moveToOrderTypeId, int roadMoveFollowOrderTypeId)
        {
            var registry = new OrderTypeRegistry();
            registry.Register(new OrderTypeConfig
            {
                Key = "moveTo",
                OrderTypeId = moveToOrderTypeId,
                Label = "Move To",
                Priority = 100,
                CanInterruptSelf = true,
                ClearQueueOnActivate = true,
            });
            registry.Register(new OrderTypeConfig
            {
                Key = RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey,
                OrderTypeId = roadMoveFollowOrderTypeId,
                Label = "Road Move Follow",
                Priority = 100,
                CanInterruptSelf = true,
                ClearQueueOnActivate = true,
            });
            return registry;
        }

        private static Order CreateMoveOrder(Entity actor, int orderTypeId, int xcm, int ycm, OrderSubmitMode submitMode)
        {
            return new Order
            {
                OrderTypeId = orderTypeId,
                Actor = actor,
                SubmitMode = submitMode,
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

        private static Order CreateRouteOrder(Entity actor, int roadMoveFollowOrderTypeId, params (int xcm, int ycm)[] points)
        {
            var order = new Order
            {
                OrderTypeId = roadMoveFollowOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs()
            };

            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.List;
            for (int i = 0; i < points.Length; i++)
            {
                order.Args.Spatial.AddPointWorldCm(points[i].xcm, 0, points[i].ycm);
            }

            return order;
        }

        private static StrategyMatrixRow CaptureStrategyMatrixRow(
            GameEngine engine,
            RoadMoveOrderExpander expander,
            RoadRouteProfileCatalog profiles,
            Entity actor,
            string actorName,
            int moveToOrderTypeId,
            int targetXcm,
            int targetYcm)
        {
            var order = CreateMoveOrder(actor, moveToOrderTypeId, targetXcm, targetYcm, OrderSubmitMode.Immediate);
            bool built = expander.TryBuildFollowOrder(in order, out Order routeOrder);
            string acceptedStatus = ReadRoadStatus(engine);
            Assert.That(built, Is.True, $"{actorName} should produce a route plan in the strategy matrix slice. Status={acceptedStatus}");

            RoadRoutePlannerProfile planner = profiles.ResolvePlanner(actor);
            RoadRouteExecutionProfile execution = profiles.ResolveExecution(actor);
            int pointCount = routeOrder.Args.Spatial.PointCount;
            float maxAbsYcm = 0f;
            for (int waypointIndex = 0; waypointIndex < pointCount; waypointIndex++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(in routeOrder, waypointIndex, out Vector3 waypointWorldCm))
                {
                    continue;
                }

                maxAbsYcm = System.MathF.Max(maxAbsYcm, System.MathF.Abs(waypointWorldCm.Z));
            }

            return new StrategyMatrixRow(
                actorName,
                planner.Label,
                execution.Label,
                acceptedStatus,
                pointCount,
                planner.DirectBiasCm,
                planner.NorthBiasCm,
                planner.SouthBiasCm,
                execution.SpeedMultiplier,
                execution.WaypointRadiusCm,
                execution.FinalArrivalRadiusCm,
                maxAbsYcm);
        }

        private static void SubmitRoadMove(RoadMoveOrderExpander expander, Entity actor, int moveToOrderTypeId, int xcm, int ycm)
        {
            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm, ycm, OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.True);
        }

        private static void ActivateExecutionSliceRoute(World world, Entity actor, int roadMoveFollowOrderTypeId, params (int xcm, int ycm)[] points)
        {
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.ClearQueued();
            buffer.ClearActive();
            Order routeOrder = CreateRouteOrder(actor, roadMoveFollowOrderTypeId, points);
            buffer.SetActiveDirect(in routeOrder, priority: 100);
        }

        private static string ReadRoadStatus(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? statusObj) && statusObj is string status
                ? status
                : "<missing>";
        }

        private static string BuildStrategyMatrixBattleReport(IReadOnlyList<StrategyMatrixRow> rows, float vanguardAdvanceCm, float northAdvanceCm, float southAdvanceCm)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: road_network_showcase_strategy_matrix");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: validate that one playable road-network showcase exposes distinct planner preferences, route-weight biases, and execution traits without welding policy into Core.");
            sb.AppendLine("- Acceptance focus: planning slice sends all three columns toward East Gate to prove different corridor selection; execution slice sends all three toward Central Crossing to compare movement traits on the same corridor.");
            sb.AppendLine();
            sb.AppendLine("## Matrix");
            foreach (StrategyMatrixRow row in rows)
            {
                sb.AppendLine($"- {row.ActorName}: planner=`{row.PlannerLabel}` execution=`{row.ExecutionLabel}` status=`{row.AcceptedStatus}` points=`{row.PointCount}` max|y|=`{row.MaxAbsPathYcm:F0}`cm biases(d/n/s)=`{row.DirectBiasCm:F0}/{row.NorthBiasCm:F0}/{row.SouthBiasCm:F0}` speed=`{row.SpeedMultiplier:F2}` waypoint=`{row.WaypointRadiusCm:F0}` arrival=`{row.FinalArrivalRadiusCm:F0}`");
            }

            sb.AppendLine();
            sb.AppendLine("## Movement Slice");
            sb.AppendLine("- Execution target: `Central Crossing (0,0)` so courier / vanguard / siege share the same corridor family while only execution traits differ.");
            sb.AppendLine($"- Blue Vanguard advance after 180 ticks: `{vanguardAdvanceCm:F0}cm`");
            sb.AppendLine($"- Blue North Column advance after 180 ticks: `{northAdvanceCm:F0}cm`");
            sb.AppendLine($"- Blue South Column advance after 180 ticks: `{southAdvanceCm:F0}cm`");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: showcase-owned planner weights produce different corridor choices, and execution presets produce visibly distinct movement envelopes on the same authored follow order.");
            return sb.ToString();
        }

        private static string BuildStrategyMatrixTrace(IReadOnlyList<StrategyMatrixRow> rows, float vanguardAdvanceCm, float northAdvanceCm, float southAdvanceCm)
        {
            var lines = new List<string>(rows.Count + 3);
            foreach (StrategyMatrixRow row in rows)
            {
                lines.Add(JsonSerializer.Serialize(new
                {
                    actor = row.ActorName,
                    planner = row.PlannerLabel,
                    execution = row.ExecutionLabel,
                    accepted_status = row.AcceptedStatus,
                    point_count = row.PointCount,
                    max_abs_path_y_cm = row.MaxAbsPathYcm,
                    direct_bias_cm = row.DirectBiasCm,
                    north_bias_cm = row.NorthBiasCm,
                    south_bias_cm = row.SouthBiasCm,
                    speed_multiplier = row.SpeedMultiplier,
                    waypoint_radius_cm = row.WaypointRadiusCm,
                    final_arrival_radius_cm = row.FinalArrivalRadiusCm
                }));
            }

            lines.Add(JsonSerializer.Serialize(new { metric = "advance_cm", actor = "Blue Vanguard", value = vanguardAdvanceCm }));
            lines.Add(JsonSerializer.Serialize(new { metric = "advance_cm", actor = "Blue North Column", value = northAdvanceCm }));
            lines.Add(JsonSerializer.Serialize(new { metric = "advance_cm", actor = "Blue South Column", value = southAdvanceCm }));
            return string.Join(System.Environment.NewLine, lines) + System.Environment.NewLine;
        }

        private static string BuildStrategyMatrixPathMermaid()
        {
            return string.Join(System.Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load road_network_showcase_chunked] --> B[Resolve three player columns with distinct RoadMoveProfileRef presets]",
                "    B --> C[Plan Blue Vanguard -> East Gate]",
                "    B --> D[Plan Blue North Column -> East Gate]",
                "    B --> E[Plan Blue South Column -> East Gate]",
                "    C --> F[Direct corridor selected]",
                "    D --> G[North corridor selected]",
                "    E --> H[South corridor selected]",
                "    F --> I[Retarget all three columns to Central Crossing]",
                "    G --> I",
                "    H --> I",
                "    I --> J[Run 180 fixed ticks on shared corridor]",
                "    J --> K[Compare courier / vanguard / siege movement envelope]"
            }) + System.Environment.NewLine;
        }

        private static string BuildStrategyMatrixSummary(IReadOnlyList<StrategyMatrixRow> rows, float vanguardAdvanceCm, float northAdvanceCm, float southAdvanceCm)
        {
            return JsonSerializer.Serialize(new
            {
                scenario = "road_network_showcase_strategy_matrix",
                rows,
                advances = new
                {
                    blue_vanguard_cm = vanguardAdvanceCm,
                    blue_north_column_cm = northAdvanceCm,
                    blue_south_column_cm = southAdvanceCm
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string BuildTimeoutBattleReport(TimeoutAcceptanceResult refreshed, TimeoutAcceptanceResult abandoned)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: road_network_showcase_timeout");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Validate the showcase-owned timeout layer separately from path query and movement steering.");
            sb.AppendLine("- Acceptance focus: one stalled road-follow order refreshes from its preserved final target; another stalled order abandons cleanly when refresh pathing fails.");
            sb.AppendLine();
            sb.AppendLine("## Branches");
            sb.AppendLine($"- Refresh branch: status=`{refreshed.Status}` activeOrder=`{refreshed.HasActiveOrder}`");
            sb.AppendLine($"- Abandon branch: status=`{abandoned.Status}` activeOrder=`{abandoned.HasActiveOrder}`");
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: timeout handling proves both refresh and abandon branches without welding policy into Core.");
            return sb.ToString();
        }

        private static string BuildTimeoutTrace(TimeoutAcceptanceResult refreshed, TimeoutAcceptanceResult abandoned)
        {
            return string.Join(System.Environment.NewLine, new[]
            {
                JsonSerializer.Serialize(new { branch = "refresh", status = refreshed.Status, has_active_order = refreshed.HasActiveOrder }),
                JsonSerializer.Serialize(new { branch = "abandon", status = abandoned.Status, has_active_order = abandoned.HasActiveOrder }),
            }) + System.Environment.NewLine;
        }

        private static string BuildTimeoutPathMermaid()
        {
            return string.Join(System.Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Seed stalled road-follow order] --> B[Run binding, selection, execution, lifecycle systems without movement progress]",
                "    B --> C{Timeout reached?}",
                "    C -->|yes + refresh succeeds| D[Replan from preserved final target]",
                "    D --> E[Status = refreshed after timeout]",
                "    C -->|yes + refresh fails| F[Abandon active order]",
                "    F --> G[Status = abandoned after timeout recovery]"
            }) + System.Environment.NewLine;
        }

        private static string BuildTimeoutSummary(TimeoutAcceptanceResult refreshed, TimeoutAcceptanceResult abandoned)
        {
            return JsonSerializer.Serialize(new
            {
                scenario = "road_network_showcase_timeout",
                refreshed,
                abandoned,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static NavMeshProfileRegistry CreateNavProfiles()
        {
            return new NavMeshProfileRegistry(new NavMeshBakeConfig
            {
                Profiles = new List<NavAgentProfileConfig>
                {
                    new NavAgentProfileConfig { Id = RoadNetworkShowcaseIds.PathPlannerAgentTypeId }
                }
            });
        }

        private static NavQueryServiceRegistry CreateNavRegistry()
        {
            return new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>());
        }

        private static PathingConfig CreatePathingConfig()
        {
            return new PathingConfig
            {
                AgentTypes = new List<PathingAgentTypeConfig>
                {
                    new PathingAgentTypeConfig
                    {
                        Id = RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                        ProfileId = RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                        Selection = new PathingSelectionConfig
                        {
                            Mode = PathSelectionMode.PreferGraph
                        },
                        NodeGraph = new PathingNodeGraphConfig
                        {
                            ProjectionMaxRadiusCm = 3500
                        }
                    }
                }
            };
        }

        private static GameEngine CreateRoadShowcaseEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(repoRoot, "mods", "showcases", "road_network", "RoadNetworkShowcaseMod"),
            };

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.Start();
            return engine;
        }

        private static GameEngine CreatePlayableRoadShowcaseEngine()
        {
            GameEngine engine = CreateRoadShowcaseEngine();
            InstallPlayableInput(engine);

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new CoreScreenRayProvider(engine.GameSession.Camera, view));
            engine.SetService(CoreServiceKeys.ScreenProjector, new CoreScreenProjector(engine.GameSession.Camera, view));

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            return engine;
        }

        private static void LoadPlayableMap(GameEngine engine, string mapId, int frames = 5)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames);
        }

        private static void InstallPlayableInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[PlayableTestInputBackendKey] = backend;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames = 60, string? failureMessage = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1);
            }

            string message = string.IsNullOrWhiteSpace(failureMessage)
                ? $"Predicate was not satisfied within {maxFrames} frames."
                : $"Predicate was not satisfied within {maxFrames} frames. {failureMessage}";
            Assert.That(predicate(), Is.True, message);
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[PlayableTestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Road showcase playable test input backend is missing.");
        }

        private static void RightClickWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition)
        {
            backend.SetMousePosition(screenPosition);
            Tick(engine, 1);
            backend.SetButton("<Mouse>/RightButton", true);
            Tick(engine, 2);
            backend.SetButton("<Mouse>/RightButton", false);
            Tick(engine, 2);
        }

        private static void DragSelectNamed(GameEngine engine, TestInputBackend backend, params string[] names)
        {
            Assert.That(names, Is.Not.Null.And.Not.Empty);

            Vector2[] points = System.Array.ConvertAll(names, name => GetEntityScreen(engine, name));
            float minX = points[0].X;
            float minY = points[0].Y;
            float maxX = points[0].X;
            float maxY = points[0].Y;
            for (int i = 1; i < points.Length; i++)
            {
                minX = MathF.Min(minX, points[i].X);
                minY = MathF.Min(minY, points[i].Y);
                maxX = MathF.Max(maxX, points[i].X);
                maxY = MathF.Max(maxY, points[i].Y);
            }

            Vector2 dragStart = new(minX - 40f, minY - 40f);
            Vector2 dragEnd = new(maxX + 40f, maxY + 40f);
            var gestureDiagnostics = new StringBuilder();

            backend.SetMousePosition(dragStart);
            Tick(engine, 1);
            gestureDiagnostics.Append("phase0=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, names));
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2);
            gestureDiagnostics.Append(" || phase1=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, names));
            backend.SetMousePosition(dragEnd);
            Tick(engine, 2);
            gestureDiagnostics.Append(" || phase2=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, names));
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2);
            gestureDiagnostics.Append(" || phase3=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, names));

            TickUntil(
                engine,
                () => GetSelectionCount(engine) == names.Length,
                maxFrames: 16,
                failureMessage: $"{BuildSelectionScreenDiagnostics(engine, dragStart, dragEnd, names)} || {gestureDiagnostics}");
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");

            ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(entity);
            return GetScreenPositionForWorld(engine, WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static Vector2 GetScreenPositionForWorld(GameEngine engine, Vector3 worldMeters)
        {
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            return projector.WorldToScreen(worldMeters);
        }

        private static Vector2 FindVisibleGroundScreenPoint(GameEngine engine)
        {
            var view = engine.GetService(CoreServiceKeys.ViewController) as IViewController
                ?? throw new InvalidOperationException("ViewController was not installed.");

            float width = view.Resolution.X;
            float height = view.Resolution.Y;
            Vector2[] candidates =
            {
                new(0f, 0f),
                new(width * 0.25f, height * 0.25f),
                new(width * 0.5f, height * 0.25f),
                new(width * 0.75f, height * 0.25f),
                new(width * 0.5f, height * 0.5f),
                new(width * 0.25f, height * 0.75f),
                new(width * 0.5f, height * 0.75f),
                new(width * 0.75f, height * 0.75f),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (AuthoritativeGroundPointerHelper.TryResolveFromScreen(engine.GlobalContext, candidates[i], out _))
                {
                    return candidates[i];
                }
            }

            throw new InvalidOperationException("No visible ground screen point could be resolved for the playable road showcase test.");
        }

        private static int GetSelectionCount(GameEngine engine)
        {
            return SelectionContextRuntime.GetCurrentCount(engine.World, engine.GlobalContext);
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            if (!SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity primary) ||
                !engine.World.TryGet(primary, out Name name))
            {
                return string.Empty;
            }

            return name.Value;
        }

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
                localObj is not Entity local ||
                !engine.World.IsAlive(local))
            {
                return Entity.Null;
            }

            return local;
        }

        private static Vector2 ReadWorldPosition(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");
            return world.Get<WorldPositionCm>(entity).Value.ToVector2();
        }

        private static bool HasNonZeroDesiredVelocity(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            if (entity == Entity.Null || !engine.World.Has<NavDesiredVelocity2D>(entity))
            {
                return false;
            }

            var velocity = engine.World.Get<NavDesiredVelocity2D>(entity).ValueCmPerSec;
            return MathF.Abs(velocity.X.ToFloat()) > 0.1f || MathF.Abs(velocity.Y.ToFloat()) > 0.1f;
        }

        private static string BuildPlayableMoveDiagnostics(GameEngine engine, params string[] names)
        {
            var sb = new StringBuilder();
            sb.Append("selected=");
            sb.Append(GetSelectionCount(engine));
            sb.Append(':');
            sb.Append(GetSelectedEntityName(engine));
            sb.Append(" | status=");
            sb.Append(engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? statusObj) && statusObj is string status
                ? status
                : "<none>");
            sb.Append(" | order=");
            sb.Append(engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastOrder", out object? orderObj) && orderObj is string order
                ? order
                : "<none>");
            for (int i = 0; i < names.Length; i++)
            {
                Entity entity = FindEntityByName(engine.World, names[i]);
                sb.Append(" | ");
                sb.Append(names[i]);
                sb.Append(':');
                if (entity == Entity.Null)
                {
                    sb.Append("<missing>");
                    continue;
                }

                Vector2 world = engine.World.Get<WorldPositionCm>(entity).Value.ToVector2();
                sb.Append("pos=(");
                sb.Append(world.X.ToString("0.##"));
                sb.Append(',');
                sb.Append(world.Y.ToString("0.##"));
                sb.Append(')');

                if (engine.World.Has<NavDesiredVelocity2D>(entity))
                {
                    var desired = engine.World.Get<NavDesiredVelocity2D>(entity).ValueCmPerSec;
                    sb.Append(" desired=(");
                    sb.Append(desired.X.ToFloat().ToString("0.##"));
                    sb.Append(',');
                    sb.Append(desired.Y.ToFloat().ToString("0.##"));
                    sb.Append(')');
                }
                else
                {
                    sb.Append(" desired=<none>");
                }

                if (engine.World.Has<OrderBuffer>(entity))
                {
                    ref readonly OrderBuffer buffer = ref engine.World.Get<OrderBuffer>(entity);
                    sb.Append(" active=");
                    sb.Append(buffer.HasActive ? buffer.ActiveOrder.Order.OrderTypeId : 0);
                    sb.Append(" queued=");
                    sb.Append(buffer.QueuedCount);
                }

                if (engine.World.Has<RoadMoveOrderRuntime>(entity))
                {
                    ref readonly RoadMoveOrderRuntime runtime = ref engine.World.Get<RoadMoveOrderRuntime>(entity);
                    sb.Append(" lifecycle=");
                    sb.Append(runtime.LifecycleState);
                    sb.Append('/');
                    sb.Append(runtime.FailureReason);
                    sb.Append('#');
                    sb.Append(runtime.TimeoutCount);
                }
            }

            return sb.ToString();
        }

        private static string BuildSelectionScreenDiagnostics(GameEngine engine, Vector2 dragStart, Vector2 dragEnd, params string[] names)
        {
            var sb = new StringBuilder();
            sb.Append("cameraTarget=");
            Vector2 target = engine.GameSession.Camera.State.TargetCm;
            sb.Append('(');
            sb.Append(target.X.ToString("0.##"));
            sb.Append(',');
            sb.Append(target.Y.ToString("0.##"));
            sb.Append(')');
            sb.Append(" drag=(");
            sb.Append(dragStart.X.ToString("0.##"));
            sb.Append(',');
            sb.Append(dragStart.Y.ToString("0.##"));
            sb.Append(")->(");
            sb.Append(dragEnd.X.ToString("0.##"));
            sb.Append(',');
            sb.Append(dragEnd.Y.ToString("0.##"));
            sb.Append(')');
            sb.Append(" selected=");
            sb.Append(GetSelectionCount(engine));
            sb.Append(':');
            sb.Append(GetSelectedEntityName(engine));

            Entity owner = GetLocalPlayer(engine);
            if (owner != Entity.Null && engine.World.Has<SelectionDragState>(owner))
            {
                ref readonly SelectionDragState drag = ref engine.World.Get<SelectionDragState>(owner);
                sb.Append(" dragState=");
                sb.Append(drag.Active);
                sb.Append('@');
                sb.Append('(');
                sb.Append(drag.StartScreen.X.ToString("0.##"));
                sb.Append(',');
                sb.Append(drag.StartScreen.Y.ToString("0.##"));
                sb.Append(")->(");
                sb.Append(drag.CurrentScreen.X.ToString("0.##"));
                sb.Append(',');
                sb.Append(drag.CurrentScreen.Y.ToString("0.##"));
                sb.Append(')');
            }

            for (int i = 0; i < names.Length; i++)
            {
                Entity entity = FindEntityByName(engine.World, names[i]);
                sb.Append(" | ");
                sb.Append(names[i]);
                sb.Append(':');
                if (entity == Entity.Null)
                {
                    sb.Append("<missing>");
                    continue;
                }

                if (engine.World.Has<VisualTransform>(entity))
                {
                    Vector2 worldScreen = GetEntityScreen(engine, names[i]);
                    sb.Append("screenWorld=(");
                    sb.Append(worldScreen.X.ToString("0.##"));
                    sb.Append(',');
                    sb.Append(worldScreen.Y.ToString("0.##"));
                    sb.Append(')');

                    Vector2 visualScreen = GetVisualScreen(engine, entity);
                    sb.Append(" screenVisual=(");
                    sb.Append(visualScreen.X.ToString("0.##"));
                    sb.Append(',');
                    sb.Append(visualScreen.Y.ToString("0.##"));
                    sb.Append(')');
                }

                if (engine.World.Has<CullState>(entity))
                {
                    sb.Append(" visible=");
                    sb.Append(engine.World.Get<CullState>(entity).IsVisible);
                }

                sb.Append(" selectable=");
                sb.Append(engine.World.Has<SelectionSelectableTag>(entity));
            }

            return sb.ToString();
        }

        private static string BuildSelectionInputDiagnostics(GameEngine engine, Vector2 dragStart, Vector2 dragEnd, params string[] names)
        {
            var sb = new StringBuilder();
            sb.Append(BuildSelectionScreenDiagnostics(engine, dragStart, dragEnd, names));

            if (engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler liveInput)
            {
                sb.Append(" liveSelect[down=");
                sb.Append(liveInput.IsDown("Select"));
                sb.Append(",pressed=");
                sb.Append(liveInput.PressedThisFrame("Select"));
                sb.Append(",released=");
                sb.Append(liveInput.ReleasedThisFrame("Select"));
                sb.Append(']');
                Vector2 livePointer = liveInput.ReadAction<Vector2>("PointerPos");
                sb.Append(" livePointer=(");
                sb.Append(livePointer.X.ToString("0.##"));
                sb.Append(',');
                sb.Append(livePointer.Y.ToString("0.##"));
                sb.Append(')');
            }

            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader authoritativeInput)
            {
                sb.Append(" authSelect[down=");
                sb.Append(authoritativeInput.IsDown("Select"));
                sb.Append(",pressed=");
                sb.Append(authoritativeInput.PressedThisFrame("Select"));
                sb.Append(",released=");
                sb.Append(authoritativeInput.ReleasedThisFrame("Select"));
                sb.Append(']');
                Vector2 authPointer = authoritativeInput.ReadAction<Vector2>("PointerPos");
                sb.Append(" authPointer=(");
                sb.Append(authPointer.X.ToString("0.##"));
                sb.Append(',');
                sb.Append(authPointer.Y.ToString("0.##"));
                sb.Append(')');
            }

            return sb.ToString();
        }

        private static Vector2 GetVisualScreen(GameEngine engine, Entity entity)
        {
            ref VisualTransform transform = ref engine.World.Get<VisualTransform>(entity);
            return GetScreenPositionForWorld(engine, transform.Position);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Repository root not found from test directory.");
        }

        private static Entity FindEntityByName(World world, string entityName)
        {
            Entity found = Entity.Null;
            world.Query(new QueryDescription().WithAll<Name>(), (Entity entity, ref Name name) =>
            {
                if (found != Entity.Null ||
                    !string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                found = entity;
            });
            return found;
        }

        private const string PlayableTestInputBackendKey = "Tests.RoadNetworkShowcase.InputBackend";

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class RecordingPathService : IPathService
        {
            private readonly PathStore _store;
            private readonly (int xcm, int ycm)[] _points;

            public RecordingPathService(PathStore store, (int xcm, int ycm)[] points)
            {
                _store = store;
                _points = points;
            }

            public List<PathRequest> Requests { get; } = new();
            public PathHandle LastHandle { get; private set; }
            public PathStore Store => _store;

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                Requests.Add(request);
                if (!_store.TryAllocate(_points.Length, out var handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                    return false;
                }

                Span<int> xs = stackalloc int[_points.Length];
                Span<int> ys = stackalloc int[_points.Length];
                for (int i = 0; i < _points.Length; i++)
                {
                    xs[i] = _points[i].xcm;
                    ys[i] = _points[i].ycm;
                }

                Assert.That(_store.TryWrite(in handle, xs, ys, _points.Length), Is.True);
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
            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, 0, 0);
                return false;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                count = 0;
                return false;
            }
        }

        private readonly record struct StrategyMatrixRow(
            string ActorName,
            string PlannerLabel,
            string ExecutionLabel,
            string AcceptedStatus,
            int PointCount,
            float DirectBiasCm,
            float NorthBiasCm,
            float SouthBiasCm,
            float SpeedMultiplier,
            float WaypointRadiusCm,
            float FinalArrivalRadiusCm,
            float MaxAbsPathYcm);

        private readonly record struct TimeoutAcceptanceResult(
            string Status,
            bool HasActiveOrder);
    }
}
