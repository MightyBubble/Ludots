using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.MovePlanning;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
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
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RoadNetworkShowcaseTests
    {
        private const int RoadTestTeamId = 1;
        private const int RoadTestPlayerId = 1;
        private const string BlueVanguardInstanceId = "road.player.blue";
        private const string BlueNorthColumnInstanceId = "road.player.blue.north";
        private const string BlueSouthColumnInstanceId = "road.player.blue.south";
        private static readonly string[] BlueColumnInstanceIds =
        {
            BlueVanguardInstanceId,
            BlueNorthColumnInstanceId,
            BlueSouthColumnInstanceId,
        };

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
            Assert.That(scenario.TryGetRoadRibbonChunk(centralChunkKey, out var centralSplines), Is.True);
            Assert.That(centralSplines.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(scenario.TryGetRoadRibbonChunk(easternChunkKey, out var easternSplines), Is.True);
            Assert.That(easternSplines.Length, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void RoadNetworkScenarioDefinition_CurvedRoadPath_UsesDenseIntermediateSamples()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm, loadedChunkCapacity: 256);
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
            var agentProfiles = CreateAgentProfiles();
            var service = new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(agentProfiles), agentProfiles, pathStore, CreatePathingConfig());
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
            var orderQueue = new OrderQueue(capacity: 16, new OrderAdmissionResultBuffer(16, 16));
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
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(0, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = CreateMoveOrder(actor, orderTypeId: 77, xcm: 450, ycm: 150, submitMode: OrderSubmitMode.Immediate);

            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued));
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
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(world, in routeOrder, 0, out var startPoint), Is.True);
            Assert.That(startPoint.X, Is.EqualTo(0f));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(world, in routeOrder, 1, out var bendPoint), Is.True);
            Assert.That(bendPoint.X, Is.EqualTo(200f));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(world, in routeOrder, out var finalPoint), Is.True);
            Assert.That(finalPoint.X, Is.EqualTo(450f));
            Assert.That(finalPoint.Z, Is.EqualTo(150f));
            Assert.That(pathStore.IsAlive(pathService.LastHandle), Is.False, "Expanded road moves must release temporary path handles after copying.");
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_PlanningFailure_ReturnsRejectedValidation_NotQueueFull()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 16, new OrderAdmissionResultBuffer(16, 16));
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var globals = CreateGlobals(new FailingPathService(), pathStore, moveToOrderTypeId: 77);
            Entity actor = world.Create(
                new RoadColumnTag(),
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(0, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = CreateMoveOrder(actor, orderTypeId: 77, xcm: 450, ycm: 150, submitMode: OrderSubmitMode.Immediate);

            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.RejectedValidation));
            Assert.That(orderQueue.Count, Is.EqualTo(0));
            Assert.That(globals[RoadMoveOrderExpander.LastSubmitStatusKey], Does.Contain("Road command rejected"));
            Assert.That(globals[RoadMoveOrderExpander.LastSubmitStatusKey], Does.Not.Contain("queue is full"));
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_QueueFull_ReturnsRejectedQueueFull()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 1, new OrderAdmissionResultBuffer(16, 16));
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
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(0, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);

            var firstOrder = CreateMoveOrder(actor, orderTypeId: 77, xcm: 450, ycm: 150, submitMode: OrderSubmitMode.Immediate);
            var secondOrder = CreateMoveOrder(actor, orderTypeId: 77, xcm: 500, ycm: 150, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in firstOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(expander.TrySubmit(in secondOrder), Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(orderQueue.Count, Is.EqualTo(1));
            Assert.That(globals[RoadMoveOrderExpander.LastSubmitStatusKey], Does.Contain("order queue is full"));
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_AdmissionCapacityMiss_ReturnsRejectedAdmissionCapacity_NotQueueFull()
        {
            using var world = World.Create();
            var admissionResults = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 16);
            admissionResults.BeginLogicStep();
            var orderQueue = new OrderQueue(capacity: 16, admissionResults);
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
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(0, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);

            var firstOrder = CreateMoveOrder(actor, orderTypeId: 77, xcm: 450, ycm: 150, submitMode: OrderSubmitMode.Immediate);
            var secondOrder = CreateMoveOrder(actor, orderTypeId: 77, xcm: 500, ycm: 150, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in firstOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(expander.TrySubmit(in secondOrder), Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(globals[RoadMoveOrderExpander.LastSubmitStatusKey], Does.Contain("admission capacity exhausted"));
            Assert.That(globals[RoadMoveOrderExpander.LastSubmitStatusKey], Does.Not.Contain("queue is full"));
            admissionResults.EndEntityIntake();
            admissionResults.EndLogicStep();
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmitSharedBatch_MidPlanningFailure_ReleasesEarlierRoutePayloads()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 16, new OrderAdmissionResultBuffer(16, 16));
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: OrderSpatial.MaxPoints);
            var points = new (int xcm, int ycm)[OrderSpatial.MaxInlinePoints + 1];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = (i * 100, 0);
            }

            var pathService = new FailAfterNSolvesPathService(pathStore, points, succeedCount: 1);
            var globals = CreateGlobals(pathService, pathStore, moveToOrderTypeId: 77);
            Entity first = world.Create(
                new RoadColumnTag(),
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(0, 0));
            Entity second = world.Create(
                new RoadColumnTag(),
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(50, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);

            for (int attempt = 0; attempt < OrderSpatialPayloadBuffer.SlotCapacity + 2; attempt++)
            {
                pathService.ResetSolveBudget();
                Order[] batch =
                {
                    CreateMoveOrder(first, orderTypeId: 77, xcm: 450, ycm: 0, submitMode: OrderSubmitMode.Immediate),
                    CreateMoveOrder(second, orderTypeId: 77, xcm: 500, ycm: 0, submitMode: OrderSubmitMode.Immediate),
                };

                Assert.That(
                    expander.TrySubmitSharedBatch(batch),
                    Is.EqualTo(OrderSubmitResult.RejectedValidation),
                    $"Attempt {attempt} must keep typed validation rejection.");
                Assert.That(orderQueue.Count, Is.EqualTo(0));
            }

            pathService.ResetSolveBudget(succeedCount: int.MaxValue);
            var recoveryOrder = CreateMoveOrder(first, orderTypeId: 77, xcm: 450, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Assert.That(
                expander.TrySubmit(in recoveryOrder),
                Is.EqualTo(OrderSubmitResult.Queued),
                "Earlier rejected batch planning must release OrderSpatial payloads so later admissions do not exhaust capacity.");
            Assert.That(orderQueue.Count, Is.EqualTo(1));
        }

        [Test]
        public void RoadRouteSelectionStrategy_DoesNotSkipAheadBeforeReachingCurrentWaypoint()
        {
            using var world = World.Create();
            Entity actor = world.Create(new OrderSpatialPayloadBuffer());
            Order order = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId: 171, (0, 0), (250, 120), (500, 240));
            order.Args.Spatial.A0 = 2;
            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            Assert.That(plans.TryBindFromOrder(actor, in order, out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, order.OrderId, out MovePlanView plan), Is.True);

            var strategy = new RoadRouteSelectionStrategy();
            bool selected = strategy.TrySelect(in plan, new Vector2(330f, 170f), currentWaypointIndex: 1, stopRadiusCm: 40f, out RoadRouteSelection selection);

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
                new Team { Id = RoadTestTeamId },
                new PlayerOwner { PlayerId = RoadTestPlayerId },
                new EntityLayer(category: 1u, mask: 1u),
                new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register("Small") },
                WorldPositionCm.FromCm(330, 170),
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer(),
                new OrderSpatialPayloadBuffer());
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntime(world, actor);

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            int moveSpeedId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("MoveSpeed");
            attributes.SetBase(moveSpeedId, 1200f);

            Order routeOrder = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId, (0, 0), (250, 120), (500, 240));
            routeOrder.OrderId = 44;
            routeOrder.Args.Spatial.A0 = 2;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in routeOrder, priority: 100);
            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            Assert.That(runtime.TryBindActiveOrder(actor, in routeOrder, preserveTimeoutCount: false, out _, out _), Is.True);
            ref var planRuntime = ref world.Get<MovePlanRuntime>(actor);
            planRuntime.CurrentWaypointIndex = 1;

            MassNavigationRuntimeBinding binding = CreateReadyRoadMassRuntimeBinding(simulation);
            var system = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            system.Update(0.1f);

            ref readonly var activeOrder = ref world.Get<OrderBuffer>(actor).ActiveOrder.Order;
            ref readonly var runtimeState = ref world.Get<MovePlanRuntime>(actor);
            Assert.That(activeOrder.Args.Spatial.A0, Is.EqualTo(2), "Follow execution must not overwrite authored order payload.");
            Assert.That(world.Get<MovePlanOrderRuntime>(actor).ActiveOrderId, Is.EqualTo(44));
            Assert.That(runtimeState.CurrentWaypointIndex, Is.EqualTo(1));
            Assert.That(world.Get<MovePlanExecutionIntent>(actor).HasTarget, Is.EqualTo(1), "Follow execution must publish a MassNavigation move-plan intent.");
        }

        [Test]
        public void RoadMovePlanSelectionSystem_FailsFastWithoutPreparedCurrentRoadRuntime()
        {
            using var world = World.Create();
            const int roadMoveFollowOrderTypeId = 171;
            Entity actor = CreateRoadMassAgent(world, "Runtime Guard Column", xcm: 330, ycm: 170);
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntime(world, actor);
            MassNavigationRuntimeBinding binding = CreateReadyRoadMassRuntimeBinding(simulation);
            var mapId = new MapId(RoadNetworkShowcaseIds.MapId);
            binding.Clear(mapId, simulation);

            Order routeOrder = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId, (0, 0), (250, 120), (500, 240));
            routeOrder.OrderId = 44;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in routeOrder, priority: 100);
            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            Assert.That(runtime.TryBindActiveOrder(actor, in routeOrder, preserveTimeoutCount: false, out _, out _), Is.True);

            var system = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.1f))!;
            Assert.That(ex.Message, Does.Contain("prepared current MassNavigation runtime"));
        }

        [Test]
        public void RoadNetworkShowcaseMovePlanExecution_DoesNotReferenceLegacyExecutionComponents()
        {
            string repoRoot = FindRepoRoot();
            string showcaseRoot = Path.Combine(repoRoot, "mods", "showcases", "road_network", "RoadNetworkShowcaseMod");
            string[] forbiddenTokens =
            {
                "LegacyPointGoalComponent",
                "LegacyAgentComponent",
                "LegacyKinematicsComponent",
                "LegacyDomainRoot",
            };

            List<string> hits = Directory.EnumerateFiles(showcaseRoot, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file)
                    .Select((line, lineIndex) => new { file, line, lineIndex })
                    .Where(item => forbiddenTokens.Any(token => item.line.Contains(token, StringComparison.Ordinal)))
                    .Select(item => $"{Path.GetRelativePath(repoRoot, item.file)}:{item.lineIndex + 1}: {item.line.Trim()}"))
                .ToList();

            Assert.That(hits, Is.Empty, "Road-network move-plan execution must stay on Core MovePlanning + MassNavigationFlow, not the deprecated execution sink:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void CoreMovePlanningSeam_DoesNotReferenceRoadShowcasePolicy()
        {
            string repoRoot = FindRepoRoot();
            string seamRoot = Path.Combine(repoRoot, "src", "Core", "MovePlanning");
            string[] forbiddenTokens =
            {
                "Road",
                "Corridor",
                "Fort",
                "RoadNetwork",
                "RoadRoute",
            };

            List<string> hits = Directory.EnumerateFiles(seamRoot, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => File.ReadLines(file)
                    .Select((line, lineIndex) => new { file, line, lineIndex })
                    .Where(item => forbiddenTokens.Any(token => item.line.Contains(token, StringComparison.Ordinal)))
                    .Select(item => $"{Path.GetRelativePath(repoRoot, item.file)}:{item.lineIndex + 1}: {item.line.Trim()}"))
                .ToList();

            Assert.That(hits, Is.Empty, "Core MovePlanning must remain a generic seam; route policy belongs to RoadNetworkShowcaseMod:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void RoadMovePlanRuntimeCursor_DoesNotUseOrderSpatialA0()
        {
            string repoRoot = FindRepoRoot();
            string showcaseRoot = Path.Combine(repoRoot, "mods", "showcases", "road_network", "RoadNetworkShowcaseMod");
            string[] scannedFiles =
            {
                Path.Combine(showcaseRoot, "Systems", "RoadMoveOrderBindingSystem.cs"),
                Path.Combine(showcaseRoot, "Systems", "RoadMovePlanSelectionSystem.cs"),
                Path.Combine(showcaseRoot, "Systems", "RoadMoveLifecycleSystem.cs"),
                Path.Combine(showcaseRoot, "Systems", "RoadMoveExecutionSystem.cs"),
                Path.Combine(showcaseRoot, "Runtime", "RoadNetworkShowcasePanelStateBuilder.cs"),
                Path.Combine(showcaseRoot, "Gameplay", "RoadRoutePreviewSplineBuilder.cs"),
            };

            List<string> hits = scannedFiles
                .SelectMany(file => File.ReadLines(file)
                    .Select((line, lineIndex) => new { file, line, lineIndex })
                    .Where(item => item.line.Contains(".A0", StringComparison.Ordinal) ||
                                   item.line.Contains("Spatial.A0", StringComparison.Ordinal))
                    .Select(item => $"{Path.GetRelativePath(repoRoot, item.file)}:{item.lineIndex + 1}: {item.line.Trim()}"))
                .ToList();

            Assert.That(hits, Is.Empty, "Road move-plan execution cursor must live in MovePlanRuntime.CurrentWaypointIndex, not OrderArgs.Spatial.A0:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
        }

        [Test]
        public void MassNavigationMovePlanExecutionSink_RequiresMassNavigationAgentIndex()
        {
            using var world = World.Create();
            Entity actor = world.Create(
                new Name { Value = "Unbound Column" },
                new RoadColumnTag(),
                WorldPositionCm.FromCm(0, 0));
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntimeWithoutAgents();
            var sink = new MassNavigationMovePlanExecutionSink(simulation);

            bool applied = sink.TryApply(world, actor, new MovePlanExecutionIntent
            {
                HasTarget = 1,
                Mode = MovePlanExecutionMode.Individual,
                TargetWorldCm = new Vector2(400f, 0f),
                SpeedCmPerSec = 1200f,
                StopRadiusCm = 40f,
            });

            Assert.That(applied, Is.False, "MassNavigationFlow execution sink must fail explicitly when the entity has not been bound to MassNavigationAgentIndex.");
            Assert.That(world.Has<MassNavigationAgentIndex>(actor), Is.False);
            Assert.That(simulation.NavigationAgentCount, Is.EqualTo(0));
        }

        [Test]
        public void MassNavigationMovePlanExecutionSink_UnchangedHeldTarget_ReturnsSuccess()
        {
            using var world = World.Create();
            Entity actor = CreateRoadMassAgent(world, "Held Target Column", xcm: 0, ycm: 0);
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntime(world, actor);
            var sink = new MassNavigationMovePlanExecutionSink(simulation);
            var intent = new MovePlanExecutionIntent
            {
                HasTarget = 1,
                Mode = MovePlanExecutionMode.Individual,
                TargetWorldCm = new Vector2(600f, 150f),
                SpeedCmPerSec = 1200f,
                StopRadiusCm = 45f,
            };

            Assert.That(sink.TryApply(world, actor, in intent), Is.True);
            Assert.That(sink.TryApply(world, actor, in intent), Is.True, "A held MassNavigationFlow target reports unchanged at the lower layer, but the move-plan sink must treat that as successfully maintained.");

            int agentIndex = world.Get<MassNavigationAgentIndex>(actor).Value;
            Assert.That(simulation.TryGetAgentNavigationTargetWorldCm(agentIndex, out float targetX, out float targetY), Is.True);
            Assert.That(targetX, Is.EqualTo(intent.TargetWorldCm.X).Within(0.01f));
            Assert.That(targetY, Is.EqualTo(intent.TargetWorldCm.Y).Within(0.01f));
        }

        [Test]
        public void MovePlanStore_TryBindFromOrder_TrimsCurvedPrefixToProjectedActorPosition()
        {
            using var world = World.Create();
            Entity actor = world.Create(
                WorldPositionCm.FromCm(40, 340),
                new OrderSpatialPayloadBuffer());

            Order routeOrder = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId: 171, (-600, -120), (-300, 80), (0, 300), (300, 620), (600, 900));
            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());

            Assert.That(plans.TryBindFromOrder(actor, in routeOrder, new Vector2(40f, 340f), out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, routeOrder.OrderId, out MovePlanView plan), Is.True);
            Assert.That(plan.Count, Is.EqualTo(3), "Execution slice should drop the curved path prefix that is already behind the actor and keep only the local projected start plus the remaining suffix.");
            Assert.That(plan.TryGetWaypoint(0, out Vector2 projectedStart), Is.True);
            Assert.That(projectedStart.X, Is.EqualTo(39f).Within(1f));
            Assert.That(projectedStart.Y, Is.EqualTo(341f).Within(1f));
            Assert.That(plan.TryGetWaypoint(1, out Vector2 firstForwardWaypoint), Is.True);
            Assert.That(firstForwardWaypoint.X, Is.EqualTo(300f));
            Assert.That(firstForwardWaypoint.Y, Is.EqualTo(620f));
            Assert.That(plan.TryGetWaypoint(2, out Vector2 secondForwardWaypoint), Is.True);
            Assert.That(secondForwardWaypoint.X, Is.EqualTo(600f));
            Assert.That(secondForwardWaypoint.Y, Is.EqualTo(900f));
        }

        [Test]
        public void MovePlanRuntimeService_RebindsCurvedRoute_FromProjectedLocalSliceForEachDirection()
        {
            using var world = World.Create();
            Entity actor = world.Create(
                new RoadColumnTag(),
                WorldPositionCm.FromCm(40, 340),
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer(),
                new OrderSpatialPayloadBuffer());

            Order eastbound = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId: 171, (-600, -120), (-300, 80), (0, 300), (300, 620), (600, 900));
            eastbound.OrderId = 101;
            Order westbound = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId: 171, (600, 900), (300, 620), (0, 300), (-300, 80), (-600, -120));
            westbound.OrderId = 102;

            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            var selection = new RoadRouteSelectionStrategy();
            Vector2 actorPosition = world.Get<WorldPositionCm>(actor).Value.ToVector2();

            Assert.That(runtime.TryBindActiveOrder(actor, in eastbound, preserveTimeoutCount: false, out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, eastbound.OrderId, out MovePlanView eastPlan), Is.True);
            Assert.That(selection.TrySelect(in eastPlan, actorPosition, currentWaypointIndex: 0, stopRadiusCm: 40f, out RoadRouteSelection eastSelection), Is.True);
            Assert.That(eastSelection.Target.X, Is.GreaterThan(actorPosition.X), "Eastbound rebind should immediately target the local forward suffix, not a waypoint from the curved prefix behind the actor.");

            Assert.That(runtime.TryBindActiveOrder(actor, in westbound, preserveTimeoutCount: false, out _, out _), Is.True);
            Assert.That(plans.TryGetPlan(actor, westbound.OrderId, out MovePlanView westPlan), Is.True);
            Assert.That(selection.TrySelect(in westPlan, actorPosition, currentWaypointIndex: 0, stopRadiusCm: 40f, out RoadRouteSelection westSelection), Is.True);
            Assert.That(westSelection.Target.X, Is.LessThan(actorPosition.X), "Westbound rebind should immediately target the local reverse suffix, instead of twitching back to the old curved prefix.");
        }

        [Test]
        public void RoadRouteComputeService_CreateFollowOrder_PreservesOriginalFinalDestinationBeyondSampledPrefix()
        {
            using var world = World.Create();
            Entity actor = world.Create(new OrderSpatialPayloadBuffer());
            Order sourceOrder = CreateMoveOrder(actor, orderTypeId: 102, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            var pathXcm = new int[OrderSpatial.MaxPoints];
            var pathYcm = new int[OrderSpatial.MaxPoints];
            for (int i = 0; i < OrderSpatial.MaxPoints; i++)
            {
                pathXcm[i] = -9000 + (i * 300);
                pathYcm[i] = 0;
            }

            var compute = new RoadRouteComputeService(roadMoveFollowOrderTypeId: 171);
            Order followOrder = compute.CreateFollowOrder(
                world,
                in sourceOrder,
                pathXcm,
                pathYcm,
                OrderSpatial.MaxPoints,
                new Vector3(18000f, 0f, 0f));

            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(world, in followOrder, OrderSpatial.MaxPoints - 1, out var sampledDestination), Is.True);
            Assert.That(sampledDestination.X, Is.Not.EqualTo(18000f), "The sampled prefix intentionally ends before the player's true click target in this regression test.");
            Assert.That(RoadRouteFinalTargetResolver.TryResolve(world, in followOrder, out var preservedDestination), Is.True);
            Assert.That(preservedDestination.X, Is.EqualTo(18000f));
            Assert.That(preservedDestination.Z, Is.EqualTo(0f));
            Assert.That(followOrder.Args.I0, Is.Zero, "Road final targets must not occupy generic integer argument slots.");
            Assert.That(followOrder.Args.I1, Is.Zero, "Road final targets must not occupy generic integer argument slots.");
            Assert.That(followOrder.Args.I2, Is.Zero, "Road final targets must not occupy generic integer argument slots.");
        }

        [Test]
        public void MovePlanStore_RoadRouteWithoutExplicitFinalDestination_IsRejected()
        {
            using var world = World.Create();
            Entity actor = world.Create(new OrderSpatialPayloadBuffer());
            Order routeOrder = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId: 171, (0, 0), (500, 0));
            routeOrder.Args.Spatial.HasDestinationWorldCm = 0;
            routeOrder.Args.Spatial.DestinationWorldCm = default;
            routeOrder.OrderId = 91;
            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());

            Assert.That(plans.TryBindFromOrder(actor, in routeOrder, out _, out _), Is.False,
                "A road route without the player's authored final destination must fail instead of guessing its last sampled waypoint.");
            Assert.That(plans.TryGetPlan(actor, routeOrder.OrderId, out _), Is.False);
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_UsesProjectedQueuedOrigin_ForFollowUpRoadMove()
        {
            using var world = World.Create();
            var orderQueue = new OrderQueue(capacity: 16, new OrderAdmissionResultBuffer(16, 16));
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
                OrderBuffer.CreateEmpty(),
                new OrderSpatialPayloadBuffer());

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            orderBuffer.SetActiveDirect(CreateMoveOrder(actor, orderTypeId: 55, xcm: 300, ycm: 0, submitMode: OrderSubmitMode.Immediate), priority: 60);
            Assert.That(orderBuffer.Enqueue(CreateMoveOrder(actor, orderTypeId: 55, xcm: 500, ycm: 0, submitMode: OrderSubmitMode.Queued), priority: 60, expireStep: -1, insertStep: 1), Is.True);

            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var followUpOrder = CreateMoveOrder(actor, orderTypeId: 55, xcm: 700, ycm: 100, submitMode: OrderSubmitMode.Queued);

            Assert.That(expander.TrySubmit(in followUpOrder), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(pathService.Requests.Count, Is.EqualTo(1));
            Assert.That(pathService.Requests[0].Start.Xcm, Is.EqualTo(500));
            Assert.That(pathService.Requests[0].Start.Ycm, Is.EqualTo(0));
        }

        [Test]
        public void RoadMoveOrderExpander_UsesRegisteredOrderTypes_WhenLegacyConfigDictionaryDisagrees()
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            Entity actor = world.Create(WorldPositionCm.FromCm(0, 0), new OrderSpatialPayloadBuffer());
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore, new[] { (0, 0), (300, 0), (600, 0) });
            Dictionary<string, object> globals = CreateGlobals(pathService, pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);
            ((GameConfig)globals[CoreServiceKeys.GameConfig.Name]).Constants.OrderTypeIds["moveTo"] = 78;
            ((GameConfig)globals[CoreServiceKeys.GameConfig.Name]).Constants.OrderTypeIds[RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey] = 172;
            globals[CoreServiceKeys.OrderTypeRegistry.Name] = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId);
            var expander = new RoadMoveOrderExpander(
                world,
                globals,
                new OrderQueue(capacity: 8, new OrderAdmissionResultBuffer(8, 8)),
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            Order order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 600, ycm: 0, OrderSubmitMode.Immediate);

            Assert.That(expander.TryBuildFollowOrder(in order, out Order routeOrder), Is.True);
            Assert.That(routeOrder.OrderTypeId, Is.EqualTo(roadMoveFollowOrderTypeId));
        }

        [Test]
        public void RoadMoveOrderExpander_TrySubmit_PrimesLoadedChunks_ForFarRoadDestination()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm, loadedChunkCapacity: 256);
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
            var orderQueue = new OrderQueue(capacity: 16, new OrderAdmissionResultBuffer(16, 16));
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: OrderSpatial.MaxPoints);
            var agentProfiles = CreateAgentProfiles();
            var globals = CreateGlobals(
                new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(agentProfiles), agentProfiles, pathStore, CreatePathingConfig()),
                pathStore,
                moveToOrderTypeId: 77);
            globals[CoreServiceKeys.LoadedChunks.Name] = new WorldGridLoadedChunks(chunkSizeCm, loadedChunkCapacity: 256);
            globals[RoadNetworkShowcaseIds.GraphLoadedChunksServiceKey] = loadedChunks;

            Entity actor = world.Create(
                new RoadColumnTag(),
                new OrderSpatialPayloadBuffer(),
                WorldPositionCm.FromCm(-9800, 0));
            var expander = new RoadMoveOrderExpander(world, globals, orderQueue, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = CreateMoveOrder(actor, orderTypeId: 77, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);

            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(loadedChunks.ActiveChunkKeys.Count, Is.GreaterThan(initialChunkCount));
            Assert.That(runtime.CurrentGraph.NodeCount, Is.GreaterThan(100));
            Assert.That(orderQueue.TryDequeue(out var routeOrder), Is.True);
            Assert.That(routeOrder.Args.Spatial.PointCount, Is.GreaterThan(20));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(world, in routeOrder, out var finalPoint), Is.True);
            Assert.That(finalPoint.X, Is.EqualTo(18000f));
            Assert.That(finalPoint.Z, Is.EqualTo(0f));
        }

        [Test]
        public void RoadNetworkShowcaseScenario_LoadedCenterWindow_StreamsChunkedGraphAndFindsRoadPath()
        {
            const int chunkSizeCm = 6400;
            RoadNetworkScenarioDefinition scenario = RoadNetworkScenarioDefinition.Create(chunkSizeCm);
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm, loadedChunkCapacity: 256);
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
            var agentProfiles = CreateAgentProfiles();
            var service = new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(agentProfiles), agentProfiles, pathStore, CreatePathingConfig());
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
                if (scenario.TryGetRoadRibbonChunk(chunkKey, out var splines))
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
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm, loadedChunkCapacity: 256);
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
            var agentProfiles = CreateAgentProfiles();
            var service = new AutoPathService(runtime, CreateNavRegistry(), CreateNavProfiles(agentProfiles), agentProfiles, pathStore, CreatePathingConfig());
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
            engine.LoadStartupMap();

            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Assert.That(engine.CurrentMapSession!.PrimaryBoard, Is.TypeOf<NodeGraphBoard>());
            Assert.That(engine.GetService(CoreServiceKeys.PathService), Is.TypeOf<AutoPathService>());
            Assert.That(
                engine.GetService(MassNavigationKeys.RuntimeBinding)?.Current,
                Is.TypeOf<MassNavigationSimulationRuntime>(),
                "Road-network waypoint following must bootstrap the MassNavigation execution runtime instead of the deleted legacy steering stack.");

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
            engine.LoadStartupMap();

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.LoadedChunkCount, Is.GreaterThan(0), "Initial showcase focus should prime the first chunk window so the first move command does not depend on a later streaming tick.");
            Assert.That(runtime.LoadedNodeCount, Is.GreaterThan(0), "Chunk priming should populate the graph store before the player issues the first road move.");
            Assert.That(ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity localObj), Is.True);
            Assert.That(localObj, Is.EqualTo(owner));
            Assert.That(ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity viewOwnerObj), Is.True);
            Assert.That(viewOwnerObj, Is.EqualTo(owner));
            Assert.That(engine.World.Has<CommandSourceDragState>(owner), Is.True);
            EntityCollectionStore collections = GetEntityCollectionStore(engine);
            Assert.That(collections.TryGetView(owner, EntityCollectionKeys.CommandSource, out EntityCollectionView view), Is.True);
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.Explicit));
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.CommandSource));
            Assert.That(view.PrimaryEntity, Is.EqualTo(owner));
            Assert.That(TryGetCommandSourcePrimary(engine, owner, out Entity primary), Is.True);
            Assert.That(primary, Is.EqualTo(owner));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_UpdateLoadedChunks_RepairsLocalPlayerAndSeedsLivePrimarySelectionWithoutReset()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));

            ReplaceCommandSource(engine, owner, ReadOnlySpan<Entity>.Empty);
            ClientLocalSeatAccess.RequireRegistry(engine).Clear();

            runtime.UpdateLoadedChunks(engine);

            Assert.That(ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity localObj), Is.True);
            Assert.That(localObj, Is.EqualTo(owner));
            Assert.That(TryGetCommandSourcePrimary(engine, owner, out Entity repairedPrimary), Is.True);
            Assert.That(repairedPrimary, Is.EqualTo(owner));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_UpdateLoadedChunks_DoesNotOverwriteValidLivePrimarySelection()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Entity selected = FindEntityByInstanceId(engine, BlueNorthColumnInstanceId);
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(selected, Is.Not.EqualTo(Entity.Null));

            Span<Entity> selectedUnits = stackalloc Entity[1];
            selectedUnits[0] = selected;
            ReplaceCommandSource(engine, owner, selectedUnits);
            ClientLocalSeatAccess.RequireRegistry(engine).Clear();

            runtime.UpdateLoadedChunks(engine);

            Assert.That(ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity localObj), Is.True);
            Assert.That(localObj, Is.EqualTo(owner));
            Assert.That(TryGetCommandSourcePrimary(engine, owner, out Entity preservedPrimary), Is.True);
            Assert.That(preservedPrimary, Is.EqualTo(selected));
        }

        [Test]
        public void RoadNetworkShowcaseRuntime_BuildPanelState_FollowsCommandSourcePrimary()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            var runtime = new RoadNetworkShowcaseRuntime();
            var context = new ScriptContext();
            context.Set(CoreServiceKeys.Engine, engine);
            runtime.HandleMapFocusedAsync(context).GetAwaiter().GetResult();

            Entity owner = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Entity commandActor = FindEntityByInstanceId(engine, BlueNorthColumnInstanceId);
            Assert.That(owner, Is.Not.EqualTo(Entity.Null));
            Assert.That(commandActor, Is.Not.EqualTo(Entity.Null));

            Span<Entity> commandActors = stackalloc Entity[1];
            commandActors[0] = commandActor;
            ReplaceCommandSource(engine, owner, commandActors);

            RoadNetworkShowcasePanelState panel = runtime.BuildPanelState(engine);
            Assert.That(panel.CommandSource, Does.Contain("Blue North Column"));
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
                      IsCurrentPrimaryInstance(engine, BlueVanguardInstanceId),
                maxFrames: 12);

            Entity owner = GetLocalPlayer(engine);
            Entity vanguard = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Entity north = FindEntityByInstanceId(engine, BlueNorthColumnInstanceId);
            Entity south = FindEntityByInstanceId(engine, BlueSouthColumnInstanceId);
            Span<Entity> selectedUnits = stackalloc Entity[3];
            selectedUnits[0] = vanguard;
            selectedUnits[1] = north;
            selectedUnits[2] = south;
            ReplaceCommandSource(engine, owner, selectedUnits);
            Tick(engine, 2);
            Assert.That(GetSelectionCount(engine), Is.EqualTo(3), BuildPlayableMoveDiagnostics(engine, BlueColumnInstanceIds));

            Vector2 vanguardStart = ReadWorldPosition(engine, BlueVanguardInstanceId);
            Vector2 northStart = ReadWorldPosition(engine, BlueNorthColumnInstanceId);
            Vector2 southStart = ReadWorldPosition(engine, BlueSouthColumnInstanceId);

            RightClickWorld(engine, backend, FindVisibleGroundScreenPoint(engine));

            TickUntil(
                engine,
                () => HasActiveMassNavigationTarget(engine, BlueVanguardInstanceId) &&
                      HasActiveMassNavigationTarget(engine, BlueNorthColumnInstanceId) &&
                      HasActiveMassNavigationTarget(engine, BlueSouthColumnInstanceId),
                maxFrames: 20,
                failureMessage: BuildPlayableMoveDiagnostics(engine, BlueColumnInstanceIds));

            Tick(engine, 30);

            Vector2 vanguardEnd = ReadWorldPosition(engine, BlueVanguardInstanceId);
            Vector2 northEnd = ReadWorldPosition(engine, BlueNorthColumnInstanceId);
            Vector2 southEnd = ReadWorldPosition(engine, BlueSouthColumnInstanceId);
            string diagnostics = BuildPlayableMoveDiagnostics(engine, BlueColumnInstanceIds);

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
                      IsCurrentPrimaryInstance(engine, BlueVanguardInstanceId),
                maxFrames: 12);

            DragSelectByInstanceIds(engine, backend, BlueColumnInstanceIds);
            Assert.That(GetSelectionCount(engine), Is.EqualTo(3), BuildPlayableMoveDiagnostics(engine, BlueColumnInstanceIds));

            Vector2 vanguardStart = ReadWorldPosition(engine, BlueVanguardInstanceId);
            Vector2 northStart = ReadWorldPosition(engine, BlueNorthColumnInstanceId);
            Vector2 southStart = ReadWorldPosition(engine, BlueSouthColumnInstanceId);

            RightClickWorld(engine, backend, FindVisibleGroundScreenPoint(engine));

            bool startedMoving = false;
            for (int i = 0; i < 20; i++)
            {
                if (HasActiveMassNavigationTarget(engine, BlueVanguardInstanceId) &&
                    HasActiveMassNavigationTarget(engine, BlueNorthColumnInstanceId) &&
                    HasActiveMassNavigationTarget(engine, BlueSouthColumnInstanceId))
                {
                    startedMoving = true;
                    break;
                }

                Tick(engine, 1);
            }

            Assert.That(
                startedMoving,
                Is.True,
                BuildPlayableMoveDiagnostics(engine, BlueColumnInstanceIds));

            Tick(engine, 30);

            Vector2 vanguardEnd = ReadWorldPosition(engine, BlueVanguardInstanceId);
            Vector2 northEnd = ReadWorldPosition(engine, BlueNorthColumnInstanceId);
            Vector2 southEnd = ReadWorldPosition(engine, BlueSouthColumnInstanceId);
            string diagnostics = BuildPlayableMoveDiagnostics(engine, BlueColumnInstanceIds);

            Assert.That(Vector2.Distance(vanguardEnd, vanguardStart), Is.GreaterThan(120f), diagnostics);
            Assert.That(Vector2.Distance(northEnd, northStart), Is.GreaterThan(120f), diagnostics);
            Assert.That(Vector2.Distance(southEnd, southStart), Is.GreaterThan(120f), diagnostics);
        }

        [Test]
        public void RoadNetworkShowcase_EngineFarRoadMove_ReachesDestinationWithoutStoppingMidRoute()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
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
            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued), ReadRoadStatus(engine));
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
            string diagnostics = BuildPlayableMoveDiagnostics(engine, BlueVanguardInstanceId);
            Assert.That(movementStarted, Is.True, "Road move never entered the fixed-step movement pipeline.");
            Assert.That(completed, Is.True, $"Road move should complete instead of stalling mid-route. FurthestX={furthestXcm}, Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}. {diagnostics}");
            Assert.That(furthestXcm, Is.GreaterThan(17000), $"Column should traverse the full eastward road route, not stop in the currently loaded chunk window. Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}");
            Assert.That(finalPosition.X, Is.EqualTo(18000).Within(80));
            Assert.That(finalPosition.Y, Is.EqualTo(0).Within(80));
        }

        /// <summary>稀疏探测组件：只验证「结构变更」本身，无任何系统消费，避免引入语义噪音。</summary>
        private struct SparseComponentAddProbe
        {
            public int Value;
        }

        private static void RunEngineFarRoadMoveAndAssertArrival(GameEngine engine, Entity actor)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null);
            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                orderQueue!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                statusKey: string.Empty);

            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued), ReadRoadStatus(engine));

            bool movementStarted = false;
            bool completed = false;
            int furthestXcm = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2().X;
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
            string diagnostics = BuildPlayableMoveDiagnostics(engine, BlueVanguardInstanceId);
            Assert.That(movementStarted, Is.True, "Road move never entered the fixed-step movement pipeline.");
            Assert.That(completed, Is.True, $"Road move should complete instead of stalling mid-route. FurthestX={furthestXcm}, Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}. {diagnostics}");
            Assert.That(furthestXcm, Is.GreaterThan(17000), $"Column should traverse the full eastward road route, not stop in the currently loaded chunk window. FurthestX={furthestXcm}, Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}");
            Assert.That(finalPosition.X, Is.EqualTo(18000).Within(80), diagnostics);
            Assert.That(finalPosition.Y, Is.EqualTo(0).Within(80), diagnostics);
        }

        [Test]
        public void RoadNetworkShowcase_EngineFarRoadMove_SparseComponentAddOnMovingAgent_KeepsMoveAlive()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            bool movementStarted = false;
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null);
            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                orderQueue!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                statusKey: string.Empty);

            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 18000, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued), ReadRoadStatus(engine));

            bool completed = false;
            for (int i = 0; i < 2400; i++)
            {
                engine.Tick(1f / 60f);

                bool hasActiveOrder = engine.World.Get<OrderBuffer>(actor).HasActive;
                if (!movementStarted && (hasActiveOrder || orderQueue.Count == 0))
                {
                    movementStarted = true;
                    engine.World.Add(actor, new SparseComponentAddProbe { Value = 1 });
                }

                if (movementStarted && !hasActiveOrder && orderQueue.Count == 0)
                {
                    completed = true;
                    break;
                }
            }

            var finalPosition = engine.World.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            string diagnostics = BuildPlayableMoveDiagnostics(engine, BlueVanguardInstanceId);
            Assert.That(movementStarted, Is.True, "Road move never entered the fixed-step movement pipeline.");
            Assert.That(completed, Is.True, $"Sparse component add on a moving agent must not stall the road move. Final=({finalPosition.X},{finalPosition.Y}), IncomingQueue={orderQueue.Count}. {diagnostics}");
            Assert.That(finalPosition.X, Is.EqualTo(18000).Within(80), $"Sparse component add on a moving agent must not stall the road move. {diagnostics}");
            Assert.That(finalPosition.Y, Is.EqualTo(0).Within(80), diagnostics);
        }

        [Test]
        public void RoadNetworkShowcase_EngineFarRoadMove_SparseComponentAddAtMapBinding_KeepsMoveAlive()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.CurrentMapSession!.PlayerEntityLookup.Get(RoadTestPlayerId), Is.EqualTo(actor),
                "Player 1's map representative is the moving vanguard; a seed-style component add targets exactly this entity.");

            engine.World.Add(actor, new SparseComponentAddProbe { Value = 1 });
            RunEngineFarRoadMoveAndAssertArrival(engine, actor);
        }

        [Test]
        public void RoadNetworkShowcase_EngineFarRoadMove_InteractionModeAddAtMapBinding_KeepsMoveAlive()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));
            var modeMap = engine.GetService(CoreServiceKeys.InteractionModeMap) as Ludots.Core.Input.Interaction.InteractionModeMap;
            Assert.That(modeMap, Is.Not.Null, "Engine init must install the interaction mode map.");

            engine.World.Add(actor, new Ludots.Core.Input.Interaction.InteractionMode { ModeId = modeMap!.NormalModeId });
            RunEngineFarRoadMoveAndAssertArrival(engine, actor);
        }

        [Test]
        public void RoadNetworkShowcase_EngineCentralRoadMove_DoesNotBacktrackToBehindSampledWaypoint()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
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
            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued));

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
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
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
            Assert.That(shouldExpand, Is.True, $"Road move expander should recognize the authored vanguard move order. ResolvedMoveToOrderTypeId={resolvedMoveToOrderTypeId}; Status={gateStatus}");
            bool built = expander.TryBuildFollowOrder(in order, out Order routeOrder);
            string status = engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? statusObj) && statusObj is string statusText
                ? statusText
                : "<missing>";
            Assert.That(built, Is.True, $"Branch clicks should resolve to a sampled road-follow route instead of failing path copy. Status={status}");
            try
            {
                Assert.That(routeOrder.OrderTypeId, Is.EqualTo(engine.MergedConfig.Constants.OrderTypeIds[RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey]));
                Assert.That(routeOrder.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.List));
                Assert.That(routeOrder.Args.Spatial.PointCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(OrderWorldSpatialResolver.TryResolveMoveDestination(engine.World, in routeOrder, out var resolvedDestination), Is.True);
                Assert.That(resolvedDestination.Z, Is.GreaterThan(2500f), "Branch clicks should snap onto the northern branch road instead of collapsing back to the origin road sample.");
                float dx = resolvedDestination.X - (-2720f);
                float dz = resolvedDestination.Z - 3810f;
                float distanceToClickCm = System.MathF.Sqrt((dx * dx) + (dz * dz));
                Assert.That(distanceToClickCm, Is.LessThanOrEqualTo(2000f), $"Resolved branch destination should stay near the clicked road sample after snapping to authored road nodes. Destination=({resolvedDestination.X},{resolvedDestination.Z})");
            }
            finally
            {
                OrderSpatialPayloadOps.Release(engine.World, in routeOrder);
            }
        }

        [Test]
        public void RoadNetworkShowcase_EngineNorthColumnRoadMove_ReachesDestination()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            Entity actor = FindEntityByInstanceId(engine, BlueNorthColumnInstanceId);
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
            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued));

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
            Assert.That(movementStarted, Is.True, $"North column should begin moving after a direct road command. Start=({startPosition.X},{startPosition.Y}) Final=({finalPosition.X},{finalPosition.Y})");
            Assert.That(completed, Is.True, $"North column should finish the submitted road move instead of timing out in place. Final=({finalPosition.X},{finalPosition.Y})");
            Assert.That(finalPosition.X, Is.EqualTo(0).Within(120));
            Assert.That(finalPosition.Y, Is.EqualTo(0).Within(120));
        }

        [Test]
        public void RoadNetworkShowcase_StrategyMatrix_WritesAcceptanceArtifacts_ForProfilesWeightsAndTraits()
        {
            using var engine = CreateRoadShowcaseEngine();
            engine.LoadStartupMap();

            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            int roadMoveFollowOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds[RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey];
            var expander = new RoadMoveOrderExpander(
                engine.World,
                engine.GlobalContext,
                engine.GetService(CoreServiceKeys.OrderQueue)!,
                RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var profiles = new RoadRouteProfileCatalog(engine.World);

            Entity vanguard = FindEntityByInstanceId(engine, BlueVanguardInstanceId);
            Entity north = FindEntityByInstanceId(engine, BlueNorthColumnInstanceId);
            Entity south = FindEntityByInstanceId(engine, BlueSouthColumnInstanceId);
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
            planRows.Add(CaptureStrategyMatrixRow(engine, expander, profiles, vanguard, ReadEntityName(engine.World, vanguard), moveToOrderTypeId, targetXcm: 9000, targetYcm: 0));
            planRows.Add(CaptureStrategyMatrixRow(engine, expander, profiles, north, ReadEntityName(engine.World, north), moveToOrderTypeId, targetXcm: 9000, targetYcm: 0));
            planRows.Add(CaptureStrategyMatrixRow(engine, expander, profiles, south, ReadEntityName(engine.World, south), moveToOrderTypeId, targetXcm: 9000, targetYcm: 0));

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

        [Test]
        public void RoadRouteTimeoutPolicy_CountsStall_WhenMotionDoesNotCloseCurrentTarget()
        {
            var policy = new RoadRouteTimeoutPolicy();
            var runtime = new MovePlanRuntime();
            var target = new Vector2(300f, 0f);

            Assert.That(
                policy.Update(ref runtime, new Vector2(100f, 0f), target, waypointIndex: 35, dt: 0f, minProgressCm: 24f, stallTimeoutSeconds: 1f),
                Is.False);

            Assert.That(
                policy.Update(ref runtime, new Vector2(110f, 20f), target, waypointIndex: 35, dt: 0.5f, minProgressCm: 24f, stallTimeoutSeconds: 1f),
                Is.False);
            Assert.That(runtime.StallSeconds, Is.EqualTo(0.5f).Within(0.001f));

            Assert.That(
                policy.Update(ref runtime, new Vector2(120f, -10f), target, waypointIndex: 35, dt: 0.6f, minProgressCm: 24f, stallTimeoutSeconds: 1f),
                Is.True);
            Assert.That(runtime.LastProgressPositionCm, Is.EqualTo(new Vector2(100f, 0f)));
        }

        private static Dictionary<string, object> CreateGlobals(IPathService pathService, PathStore pathStore, int moveToOrderTypeId, int roadMoveFollowOrderTypeId = 171)
        {
            return new Dictionary<string, object>
            {
                [Ludots.Core.Scripting.CoreServiceKeys.PathService.Name] = pathService,
                [Ludots.Core.Scripting.CoreServiceKeys.PathStore.Name] = pathStore,
                [Ludots.Core.Scripting.CoreServiceKeys.OrderTypeRegistry.Name] = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId),
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

            Entity actor = CreateRoadMassAgent(world, "Timeout Column", xcm: 0, ycm: 0);
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntime(world, actor);

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            int moveSpeedId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("MoveSpeed");
            attributes.SetBase(moveSpeedId, 1200f);

            int[] pathXcm = { 0, 300, 600 };
            int[] pathYcm = { 0, 0, 0 };
            var compute = new RoadRouteComputeService(roadMoveFollowOrderTypeId);
            Order sourceOrder = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 600, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            Order followOrder = compute.CreateFollowOrder(world, in sourceOrder, pathXcm, pathYcm, pathXcm.Length, new Vector3(600f, 0f, 0f));
            followOrder.OrderId = 7001;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in followOrder, priority: 100);

            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            MassNavigationRuntimeBinding binding = CreateReadyRoadMassRuntimeBinding(simulation);
            var bindSystem = new RoadMoveOrderBindingSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            var selectionSystem = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            var executionSystem = new RoadMoveExecutionSystem(world, binding);
            var lifecycleSystem = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime, binding);
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

            Entity actor = CreateRoadMassAgent(world, "Foreign Order Column", xcm: 0, ycm: 0);
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntime(world, actor);
            if (!world.Has<MovePlanOrderRuntime>(actor))
            {
                world.Add(actor, default(MovePlanOrderRuntime));
            }

            if (!world.Has<MovePlanRuntime>(actor))
            {
                world.Add(actor, default(MovePlanRuntime));
            }

            if (!world.Has<MovePlanExecutionIntent>(actor))
            {
                world.Add(actor, default(MovePlanExecutionIntent));
            }

            Order moveOrder = CreateMoveOrder(actor, moveToOrderTypeId, xcm: 600, ycm: 0, submitMode: OrderSubmitMode.Immediate);
            moveOrder.OrderId = 9001;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in moveOrder, priority: 100);

            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            MassNavigationRuntimeBinding binding = CreateReadyRoadMassRuntimeBinding(simulation);
            var selectionSystem = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            var lifecycleSystem = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime, binding);

            selectionSystem.Update(0.1f);
            lifecycleSystem.Update(0.1f);

            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.True, "Showcase road systems must not complete a foreign active order just because stale road runtime components remain on the entity.");
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderTypeId, Is.EqualTo(moveToOrderTypeId));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(9001));
        }

        [Test]
        public void RoadMoveShowcaseSystems_SkipSuspendedRoadUnitsWhenRuntimeIsNoLongerPrepared()
        {
            using var world = World.Create();
            const int moveToOrderTypeId = 77;
            const int roadMoveFollowOrderTypeId = 171;
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            Dictionary<string, object> globals = CreateGlobals(new FailingPathService(), pathStore, moveToOrderTypeId, roadMoveFollowOrderTypeId);
            OrderTypeRegistry orderTypes = CreateTimeoutOrderTypeRegistry(moveToOrderTypeId, roadMoveFollowOrderTypeId);

            Entity actor = CreateRoadMassAgent(world, "Suspended Road Column", xcm: 0, ycm: 0);
            world.Add(actor, new SuspendedTag());
            if (!world.Has<MovePlanOrderRuntime>(actor))
            {
                world.Add(actor, default(MovePlanOrderRuntime));
            }

            if (!world.Has<MovePlanRuntime>(actor))
            {
                world.Add(actor, default(MovePlanRuntime));
            }

            if (!world.Has<MovePlanExecutionIntent>(actor))
            {
                world.Add(actor, default(MovePlanExecutionIntent));
            }

            Order routeOrder = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId, (0, 0), (300, 0), (600, 0));
            routeOrder.OrderId = 44;
            world.Get<OrderBuffer>(actor).SetActiveDirect(in routeOrder, priority: 100);
            world.Get<MovePlanOrderRuntime>(actor).ActiveOrderId = routeOrder.OrderId;
            world.Get<MovePlanOrderRuntime>(actor).LifecycleState = MovePlanLifecycleState.Active;
            world.Get<MovePlanRuntime>(actor).BoundOrderId = routeOrder.OrderId;
            world.Get<MovePlanExecutionIntent>(actor).HasTarget = 1;

            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            var binding = new MassNavigationRuntimeBinding();
            var bindSystem = new RoadMoveOrderBindingSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            var selectionSystem = new RoadMovePlanSelectionSystem(world, roadMoveFollowOrderTypeId, plans, runtime, binding);
            var executionSystem = new RoadMoveExecutionSystem(world, binding);
            var lifecycleSystem = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime, binding);

            Assert.DoesNotThrow(() =>
            {
                bindSystem.Update(0.1f);
                selectionSystem.Update(0.1f);
                executionSystem.Update(0.1f);
                lifecycleSystem.Update(0.1f);
            }, "Suspended road-map entities belong to an inactive map session and must not request the current road MassNavigation runtime.");
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

            Entity actor = CreateRoadMassAgent(world, "Refresh Column", xcm: 0, ycm: 0);
            MassNavigationSimulationRuntime simulation = CreateRoadMassRuntime(world, actor);

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            int moveSpeedId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("MoveSpeed");
            attributes.SetBase(moveSpeedId, 1200f);

            Order staleRoute = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId, (0, 0), (300, 0), (600, 0));
            staleRoute.OrderId = 44;
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in staleRoute, priority: 100);

            var plans = new MovePlanStore(world, new RoadRouteFinalTargetMovePlanResolver());
            var runtime = new MovePlanRuntimeService(world, plans);
            Assert.That(runtime.TryBindActiveOrder(actor, in staleRoute, preserveTimeoutCount: false, out _, out _), Is.True);

            ref var orderRuntime = ref world.Get<MovePlanOrderRuntime>(actor);
            ref var planRuntime = ref world.Get<MovePlanRuntime>(actor);
            orderRuntime.LifecycleState = MovePlanLifecycleState.NeedsReplan;
            orderRuntime.TimeoutCount = 1;
            planRuntime.Initialized = 1;
            planRuntime.LastProgressPositionCm = Vector2.Zero;
            planRuntime.LastResolvedWaypointIndex = 0;

            MassNavigationRuntimeBinding binding = CreateReadyRoadMassRuntimeBinding(simulation);
            var lifecycle = new RoadMoveLifecycleSystem(world, globals, orderTypes, roadMoveFollowOrderTypeId, plans, runtime, binding);
            lifecycle.Update(0.1f);

            ref readonly Order refreshedActive = ref world.Get<OrderBuffer>(actor).ActiveOrder.Order;
            Assert.That(refreshedActive.OrderId, Is.EqualTo(44));
            Assert.That(OrderWorldSpatialResolver.GetSpatialPointCount(world, in refreshedActive), Is.GreaterThanOrEqualTo(3));
            Assert.That(OrderWorldSpatialResolver.TryResolveMoveWaypoint(world, in refreshedActive, 1, out Vector3 refreshedWaypoint), Is.True);
            Assert.That(refreshedWaypoint.Z, Is.GreaterThan(0f), "Timeout refresh should replace the stale straight-line payload with the replanned curved road route.");
            Assert.That(world.Get<MovePlanOrderRuntime>(actor).LifecycleState, Is.EqualTo(MovePlanLifecycleState.Active));
        }

        private static OrderTypeRegistry CreateTimeoutOrderTypeRegistry(int moveToOrderTypeId, int roadMoveFollowOrderTypeId)
        {
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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

        private static Order CreateRouteOrder(World world, Entity actor, int roadMoveFollowOrderTypeId, params (int xcm, int ycm)[] points)
        {
            var order = new Order
            {
                OrderTypeId = roadMoveFollowOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs()
            };

            var pointXcm = new int[points.Length];
            var pointYcm = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                pointXcm[i] = points[i].xcm;
                pointYcm[i] = points[i].ycm;
            }

            Vector3 destinationWorldCm = new(points[^1].xcm, 0f, points[^1].ycm);
            OrderSpatialPayloadOps.SetPath(world, actor, ref order, pointXcm, pointYcm, points.Length, destinationWorldCm);

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

            try
            {
                RoadRoutePlannerProfile planner = profiles.ResolvePlanner(actor);
                RoadRouteExecutionProfile execution = profiles.ResolveExecution(actor);
                int pointCount = routeOrder.Args.Spatial.PointCount;
                float maxAbsYcm = 0f;
                for (int waypointIndex = 0; waypointIndex < pointCount; waypointIndex++)
                {
                    if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(engine.World, in routeOrder, waypointIndex, out Vector3 waypointWorldCm))
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
            finally
            {
                OrderSpatialPayloadOps.Release(engine.World, in routeOrder);
            }
        }

        private static void SubmitRoadMove(RoadMoveOrderExpander expander, Entity actor, int moveToOrderTypeId, int xcm, int ycm)
        {
            var order = CreateMoveOrder(actor, moveToOrderTypeId, xcm, ycm, OrderSubmitMode.Immediate);
            Assert.That(expander.TrySubmit(in order), Is.EqualTo(OrderSubmitResult.Queued));
        }

        private static void ActivateExecutionSliceRoute(World world, Entity actor, int roadMoveFollowOrderTypeId, params (int xcm, int ycm)[] points)
        {
            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.ClearQueued();
            buffer.ClearActive();
            Order routeOrder = CreateRouteOrder(world, actor, roadMoveFollowOrderTypeId, points);
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

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }

        private static NavMeshProfileRegistry CreateNavProfiles(AgentProfileRegistry agentProfiles)
        {
            return new NavMeshProfileRegistry(new NavMeshBakeConfig
            {
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = RoadNetworkShowcaseIds.PathPlannerAgentTypeId, MaxClimbCm = 40, MaxSlopeDeg = 45 }
                }
            }, agentProfiles);
        }

        private static NavQueryServiceRegistry CreateNavRegistry()
        {
            return new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>(),
                tileWidthCm: SpatialScaleDefaults.TerrainChunkCells * SpatialScaleDefaults.CellCm,
                tileHeightCm: SpatialScaleDefaults.TerrainChunkCells * SpatialScaleDefaults.CellCm);
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

        private static Entity CreateRoadMassAgent(World world, string name, int xcm, int ycm)
        {
            int profileId = MassNavigationProfileRegistry.Register("Small");
            return world.Create(
                new Name { Value = name },
                new RoadColumnTag(),
                new Team { Id = RoadTestTeamId },
                new PlayerOwner { PlayerId = RoadTestPlayerId },
                new EntityLayer(category: 1u, mask: 1u),
                new MassNavigationAgent { ProfileId = profileId },
                WorldPositionCm.FromCm(xcm, ycm),
                OrderBuffer.CreateEmpty(),
                new AttributeBuffer(),
                new GameplayTagContainer(),
                new OrderSpatialPayloadBuffer());
        }

        private static MassNavigationSimulationRuntime CreateRoadMassRuntime(World world, Entity actor)
        {
            MassNavigationConfig config = CreateRoadMassConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(-25_000, -25_000, 50_000, 50_000), 100),
                new WorldGridLoadedChunks(
                    simulation.WorldConfig.StreamingChunkSizeCm,
                    simulation.Config.ScenarioRuntime.RuntimeCapacity.LoadedChunkCapacity));

            Vector2 worldPosition = world.Get<WorldPositionCm>(actor).Value.ToVector2();
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            simulation.RebuildFromAuthoredAgents(
                world,
                new[] { actor },
                new[]
                {
                    new MassNavigationAgentSeed(
                        teamId: 1,
                        localPositionXCm: simulation.ToLocalXCm(worldPosition.X),
                        localPositionYCm: simulation.ToLocalYCm(worldPosition.Y),
                        heavy: false,
                        navMass: 1f,
                        visualScale: 1f,
                        bodyRadiusCm: 30f,
                        speedCmPerSecond: 1200f,
                        layer),
                },
                new[] { true });
            return simulation;
        }

        private static MassNavigationRuntimeBinding CreateReadyRoadMassRuntimeBinding(MassNavigationSimulationRuntime simulation)
        {
            var binding = new MassNavigationRuntimeBinding();
            var mapId = new MapId(RoadNetworkShowcaseIds.MapId);
            binding.Activate(mapId, simulation);
            binding.MarkPrepared(mapId, simulation);
            return binding;
        }

        private static MassNavigationSimulationRuntime CreateRoadMassRuntimeWithoutAgents()
        {
            MassNavigationConfig config = CreateRoadMassConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(-25_000, -25_000, 50_000, 50_000), 100),
                new WorldGridLoadedChunks(
                    simulation.WorldConfig.StreamingChunkSizeCm,
                    simulation.Config.ScenarioRuntime.RuntimeCapacity.LoadedChunkCapacity));
            return simulation;
        }

        private static MassNavigationConfig CreateRoadMassConfigForTests()
        {
            string repoRoot = FindRepoRoot();
            var vfs = new VirtualFileSystem();
            vfs.Mount(
                "MassNavigationMod",
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"));
            vfs.Mount(
                "RoadNetworkShowcaseMod",
                RoadNetworkShowcaseModRoot());
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("MassNavigationMod");
            modLoader.LoadedModIds.Add("RoadNetworkShowcaseMod");
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            MassNavigationConfig config = new MassNavigationConfigLoader(pipeline).Load(
                catalog,
                new ConfigConflictReport());
            config.AgentProfiles.BindAgentProfiles(new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0,
                },
            }));
            return config;
        }

        private static string RoadNetworkShowcaseModRoot()
        {
            return Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "road_network",
                "RoadNetworkShowcaseMod");
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
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"),
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
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new CoreScreenRayProvider(engine.AuthorityCamera(), view));
            engine.SetService(CoreServiceKeys.ScreenProjector, new CoreScreenProjector(engine.AuthorityCamera(), view));

            var culling = new CameraCullingSystem(engine.World, engine.AuthorityCamera(), engine.SpatialQueries, view, cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            return engine;
        }

        private static void LoadPlayableMap(GameEngine engine, string mapId, int frames = 5)
        {
            if (string.Equals(mapId, engine.MergedConfig.StartupMapId, StringComparison.OrdinalIgnoreCase))
            {
                engine.LoadStartupMap();
            }
            else
            {
                MapLaunchContext? launchContext = engine.MergedConfig.CreateStartupLaunchContext();
                engine.LoadMap(MapLoadRequest.FromMapId(mapId, launchContext));
            }
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

        private static void TickPastFixedStep(GameEngine engine)
        {
            float fixedDt = Ludots.Core.Engine.Time.FixedDeltaTime;
            int frames = fixedDt > 0f
                ? Math.Max(1, (int)MathF.Ceiling((fixedDt * 60f) + 0.01f))
                : 1;
            Tick(engine, frames);
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
            TickPastFixedStep(engine);
            backend.SetButton("<Mouse>/RightButton", false);
            TickPastFixedStep(engine);
        }

        private static void DragSelectByInstanceIds(GameEngine engine, TestInputBackend backend, params string[] instanceIds)
        {
            Assert.That(instanceIds, Is.Not.Null.And.Not.Empty);

            Vector2[] points = System.Array.ConvertAll(instanceIds, instanceId => GetEntityScreen(engine, instanceId));
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
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, instanceIds));
            backend.SetButton("<Mouse>/LeftButton", true);
            TickPastFixedStep(engine);
            gestureDiagnostics.Append(" || phase1=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, instanceIds));
            backend.SetMousePosition(dragEnd);
            TickPastFixedStep(engine);
            gestureDiagnostics.Append(" || phase2=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, instanceIds));
            backend.SetButton("<Mouse>/LeftButton", false);
            TickPastFixedStep(engine);
            gestureDiagnostics.Append(" || phase3=");
            gestureDiagnostics.Append(BuildSelectionInputDiagnostics(engine, dragStart, dragEnd, instanceIds));

            TickUntil(
                engine,
                () => GetSelectionCount(engine) == instanceIds.Length,
                maxFrames: 16,
                failureMessage: $"{BuildSelectionScreenDiagnostics(engine, dragStart, dragEnd, instanceIds)} || {gestureDiagnostics}");
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string instanceId)
        {
            Entity entity = FindEntityByInstanceId(engine, instanceId);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity instance '{instanceId}' was not found.");

            ref WorldPositionCm position = ref engine.World.Get<WorldPositionCm>(entity);
            return GetScreenPositionForWorld(engine, WorldUnitsFix64.WorldCmToVisualMeters(position.Value, yMeters: 0f));
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
            return Ludots.Tests.EntityCollectionTestAccess.GetCommandSourceCount(engine);
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            if (!Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity primary) ||
                !engine.World.TryGet(primary, out Name name))
            {
                return string.Empty;
            }

            return name.Value;
        }

        private static bool IsCurrentPrimaryInstance(GameEngine engine, string instanceId)
        {
            if (!Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity primary))
            {
                return false;
            }

            return primary == FindEntityByInstanceId(engine, instanceId);
        }

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var local) ||
                !engine.World.IsAlive(local))
            {
                return Entity.Null;
            }

            return local;
        }

        private static EntityCollectionStore GetEntityCollectionStore(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        }

        private static void ReplaceCommandSource(GameEngine engine, Entity owner, ReadOnlySpan<Entity> entities)
        {
            EntityCollectionStore collections = GetEntityCollectionStore(engine);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: entities.Length > 0 ? entities[0] : Entity.Null,
                title: "Road network command source",
                summary: "Test-owned command-source collection.");
            Assert.That(collections.Replace(owner, in descriptor, entities, owner).IsValid, Is.True);
            ClientLocalSeatTestBindings.BindSoleSeat(engine.GlobalContext, owner, 1, "seat.0");
        }

        private static bool TryGetCommandSourcePrimary(GameEngine engine, Entity owner, out Entity primary)
        {
            primary = Entity.Null;
            EntityCollectionStore collections = GetEntityCollectionStore(engine);
            return collections.TryGet(owner, EntityCollectionKeys.CommandSource, out EntityCollectionHandle handle) &&
                   collections.TryGetEntityAt(handle, 0, out primary);
        }

        private static Vector2 ReadWorldPosition(GameEngine engine, string instanceId)
        {
            Entity entity = FindEntityByInstanceId(engine, instanceId);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity instance '{instanceId}' was not found.");
            return engine.World.Get<WorldPositionCm>(entity).Value.ToVector2();
        }

        private static bool HasActiveMassNavigationTarget(GameEngine engine, string instanceId)
        {
            Entity entity = FindEntityByInstanceId(engine, instanceId);
            if (entity == Entity.Null ||
                !engine.World.TryGet(entity, out MassNavigationAgentIndex agentIndex) ||
                engine.GetService(MassNavigationKeys.RuntimeBinding) is not { IsReady: true, Current: { } simulation })
            {
                return false;
            }

            return simulation.TryGetAgentNavigationTargetWorldCm(agentIndex.Value, out _, out _);
        }

        private static string BuildPlayableMoveDiagnostics(GameEngine engine, params string[] instanceIds)
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
            for (int i = 0; i < instanceIds.Length; i++)
            {
                string instanceId = instanceIds[i];
                Entity entity = FindEntityByInstanceId(engine, instanceId);
                sb.Append(" | ");
                sb.Append(DescribeEntity(engine, instanceId, entity));
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

                if (engine.World.TryGet(entity, out MassNavigationAgentIndex agentIndex) &&
                    engine.GetService(MassNavigationKeys.RuntimeBinding) is { IsReady: true, Current: { } simulation } &&
                    simulation.TryGetAgentNavigationTargetWorldCm(agentIndex.Value, out float targetX, out float targetY))
                {
                    sb.Append(" massTarget=(");
                    sb.Append(targetX.ToString("0.##"));
                    sb.Append(',');
                    sb.Append(targetY.ToString("0.##"));
                    sb.Append(')');
                }
                else
                {
                    sb.Append(" massTarget=<none>");
                }

                if (engine.World.Has<OrderBuffer>(entity))
                {
                    ref readonly OrderBuffer buffer = ref engine.World.Get<OrderBuffer>(entity);
                    sb.Append(" active=");
                    sb.Append(buffer.HasActive ? buffer.ActiveOrder.Order.OrderTypeId : 0);
                    sb.Append(" queued=");
                    sb.Append(buffer.QueuedCount);
                }

                if (engine.World.Has<MovePlanOrderRuntime>(entity))
                {
                    ref readonly MovePlanOrderRuntime runtime = ref engine.World.Get<MovePlanOrderRuntime>(entity);
                    sb.Append(" lifecycle=");
                    sb.Append(runtime.LifecycleState);
                    sb.Append('/');
                    sb.Append(runtime.FailureReason);
                    sb.Append('#');
                    sb.Append(runtime.TimeoutCount);
                }

                if (engine.World.Has<MovePlanRuntime>(entity))
                {
                    ref readonly MovePlanRuntime planRuntime = ref engine.World.Get<MovePlanRuntime>(entity);
                    sb.Append(" plan=");
                    sb.Append(planRuntime.CurrentWaypointIndex);
                    sb.Append('/');
                    sb.Append(planRuntime.PointCount);
                    sb.Append(" stall=");
                    sb.Append(planRuntime.StallSeconds.ToString("0.###"));
                    sb.Append(" last=(");
                    sb.Append(planRuntime.LastProgressPositionCm.X.ToString("0.##"));
                    sb.Append(',');
                    sb.Append(planRuntime.LastProgressPositionCm.Y.ToString("0.##"));
                    sb.Append(") final=(");
                    sb.Append(planRuntime.FinalGoalXcm);
                    sb.Append(',');
                    sb.Append(planRuntime.FinalGoalYcm);
                    sb.Append(')');
                }
            }

            return sb.ToString();
        }

        private static string BuildSelectionScreenDiagnostics(GameEngine engine, Vector2 dragStart, Vector2 dragEnd, params string[] instanceIds)
        {
            var sb = new StringBuilder();
            sb.Append("cameraTarget=");
            Vector2 target = engine.AuthorityCamera().State.TargetCm;
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
            if (owner != Entity.Null && engine.World.Has<CommandSourceDragState>(owner))
            {
                ref readonly CommandSourceDragState drag = ref engine.World.Get<CommandSourceDragState>(owner);
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

            for (int i = 0; i < instanceIds.Length; i++)
            {
                string instanceId = instanceIds[i];
                Entity entity = FindEntityByInstanceId(engine, instanceId);
                sb.Append(" | ");
                sb.Append(DescribeEntity(engine, instanceId, entity));
                sb.Append(':');
                if (entity == Entity.Null)
                {
                    sb.Append("<missing>");
                    continue;
                }

                if (engine.World.Has<VisualTransform>(entity))
                {
                    Vector2 worldScreen = GetEntityScreen(engine, instanceId);
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
                sb.Append(engine.World.Has<CommandSourceSelectableTag>(entity));
            }

            return sb.ToString();
        }

        private static string BuildSelectionInputDiagnostics(GameEngine engine, Vector2 dragStart, Vector2 dragEnd, params string[] instanceIds)
        {
            var sb = new StringBuilder();
            sb.Append(BuildSelectionScreenDiagnostics(engine, dragStart, dragEnd, instanceIds));

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

        private static string DescribeEntity(GameEngine engine, string instanceId, Entity entity)
        {
            if (entity == Entity.Null)
            {
                return instanceId;
            }

            string name = ReadEntityName(engine.World, entity);
            return string.IsNullOrWhiteSpace(name)
                ? instanceId
                : $"{instanceId}/{name}";
        }

        private static string ReadEntityName(World world, Entity entity)
        {
            return world.TryGet(entity, out Name name) ? name.Value : string.Empty;
        }

        private static Entity FindEntityByInstanceId(GameEngine engine, string instanceId)
        {
            if (engine.CurrentMapSession?.EntityIndex.TryGet(instanceId, out Entity entity) == true &&
                engine.World.IsAlive(entity))
            {
                return entity;
            }

            return Entity.Null;
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

        private sealed class FailAfterNSolvesPathService : IPathService
        {
            private readonly RecordingPathService _inner;
            private int _succeedCount;
            private int _solveCount;

            public FailAfterNSolvesPathService(PathStore store, (int xcm, int ycm)[] points, int succeedCount)
            {
                _inner = new RecordingPathService(store, points);
                _succeedCount = succeedCount;
            }

            public void ResetSolveBudget(int? succeedCount = null)
            {
                if (succeedCount.HasValue)
                {
                    _succeedCount = succeedCount.Value;
                }

                _solveCount = 0;
            }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                if (_solveCount >= _succeedCount)
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.NoPath, default, 0, 0);
                    return false;
                }

                _solveCount++;
                return _inner.TrySolve(in request, out result);
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _inner.TryCopyPath(in handle, xcmOut, ycmOut, out count);
            }
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
