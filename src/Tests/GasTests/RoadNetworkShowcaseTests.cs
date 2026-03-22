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
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Map.Board;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;
using RoadNetworkShowcaseMod;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;
using RoadNetworkShowcaseMod.Systems;

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
            Order order = CreateRouteOrder(Entity.Null, roadMoveFollowOrderTypeId: 171, (0, 0), (250, 120), (500, 240));
            order.Args.Spatial.A0 = 2;

            var strategy = new RoadRouteSelectionStrategy();
            bool selected = strategy.TrySelect(in order, Fix64Vec2.FromInt(330, 170), currentWaypointIndex: 1, stopRadiusCm: 40f, out RoadRouteSelection selection);

            Assert.That(selected, Is.True);
            Assert.That(selection.Completed, Is.False);
            Assert.That(selection.WaypointIndex, Is.EqualTo(1), "Road-follow selection should keep the current waypoint until the actor actually reaches it, instead of cutting the corner toward the next sampled point.");
            Assert.That(order.Args.Spatial.A0, Is.EqualTo(2), "Selection must not read authored-order payload as execution cursor.");
        }

        [Test]
        public void RoadRouteFollowSystem_TracksWaypointProgressInRuntimeState_NotAuthoredOrderPayload()
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            Dictionary<string, object> globals = CreateGlobals(new FailingPathService(), pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);
            OrderTypeRegistry orderTypes = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId);

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
            world.Add(actor, new RoadRouteRuntimeState
            {
                ActiveOrderId = 44,
                ActivePointCount = 3,
                ActiveGoalXcm = 0,
                ActiveGoalYcm = 0,
                CurrentWaypointIndex = 1
            });

            var system = new RoadRouteFollowSystem(world, globals, orderTypes, new OrderQueue(capacity: 8));
            system.Update(0.1f);

            ref readonly var activeOrder = ref world.Get<OrderBuffer>(actor).ActiveOrder.Order;
            ref readonly var runtimeState = ref world.Get<RoadRouteRuntimeState>(actor);
            Assert.That(activeOrder.Args.Spatial.A0, Is.EqualTo(2), "Follow execution must not overwrite authored order payload.");
            Assert.That(runtimeState.ActiveOrderId, Is.EqualTo(44));
            Assert.That(runtimeState.CurrentWaypointIndex, Is.EqualTo(1));
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
        public void AutoPathService_PreferGraphFallback_PreservesGraphFailureWithoutMeshOverride()
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
            Assert.That(result.Status, Is.EqualTo(PathStatus.NoPath));
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

            var system = new RoadRouteFollowSystem(world, globals, orderTypes, new OrderQueue(capacity: 8));
            string status = string.Empty;
            for (int step = 0; step < 12; step++)
            {
                system.Update(0.5f);
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
                "    A[Seed stalled road-follow order] --> B[Run RoadRouteFollowSystem without movement progress]",
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
                            Mode = PathSelectionMode.PreferGraph,
                            Fallback = PathSelectionMode.PreferGraph
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
