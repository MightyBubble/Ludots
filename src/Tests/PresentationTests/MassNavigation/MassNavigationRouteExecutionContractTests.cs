using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationRouteExecutionContractTests
    {
        [SetUp]
        public void ResetProfiles()
        {
            MassNavigationProfileRegistry.Reset();
        }

        [Test]
        public void RouteSink_ExecutesWorldTargetOrderThroughNavMeshOnlyPathService()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed, out _);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            PathingConfig pathingConfig = CreateNavMeshPathingConfig();
            var pathService = CreateNavMeshOnlyPathService(store, pathingConfig);
            var sink = new MassNavigationRouteExecutionSink(pathService, store, pathingConfig);

            sink.BeginSync();
            MassNavigationRouteSinkResult track = sink.TrackRouteTarget(
                runtime,
                world,
                routed,
                agentIndex: 0,
                destinationWorldCm: new Vector2(5_800, 5_000),
                requestId: 1402,
                maxExpanded: 128,
                maxPoints: 8);
            sink.EndSync();

            MassNavigationRouteSinkResult applied = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(track.Tracked, Is.True);
            Assert.That(applied.Applied, Is.True,
                $"A navmesh-only map's path service must honor the world-target order: status={applied.Status}, pathStatus={applied.PathStatus}, domain={applied.ResolvedDomain}, errorCode={applied.ErrorCode}.");
            Assert.That(applied.ResolvedDomain, Is.EqualTo(PathDomain.NavMesh));
            Assert.That(applied.WaypointCount, Is.GreaterThan(0));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float targetX, out float targetY), Is.True,
                "A valid world target order must commit a navigation target to the flow solver.");
        }

        [Test]
        public void NavMeshPathServiceAdapter_KeepsStrictExplicitNavMeshContract()
        {
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var adapter = new NavMeshPathServiceAdapter(CreateFlatNavQuery(), store);

            var autoRequest = new PathRequest(
                requestId: 1403,
                actor: default,
                domain: PathDomain.Auto,
                start: PathEndpoint.FromWorldCm(1_000, 1_000),
                goal: PathEndpoint.FromWorldCm(2_000, 2_000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 8));
            Assert.That(adapter.TrySolve(in autoRequest, out PathResult autoResult), Is.False,
                "The adapter is the explicit NavMesh contract only; PathDomain.Auto must be rejected, never silently routed through the first profile.");
            Assert.That(autoResult.Status, Is.EqualTo(PathStatus.InvalidRequest));
            Assert.That(autoResult.ErrorCode, Is.EqualTo(2));

            var navMeshRequest = new PathRequest(
                requestId: 1403,
                actor: default,
                domain: PathDomain.NavMesh,
                start: PathEndpoint.FromWorldCm(1_000, 1_000),
                goal: PathEndpoint.FromWorldCm(2_000, 2_000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 8));
            Assert.That(adapter.TrySolve(in navMeshRequest, out PathResult navMeshResult), Is.True);
            Assert.That(navMeshResult.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(navMeshResult.ResolvedDomain, Is.EqualTo(PathDomain.NavMesh));
        }

        [Test]
        public void NavMeshOnlyAutoPathService_ResolvesPerRequestAgentProfile()
        {
            const int tileSizeCm = 250 * 64;
            var agentProfiles = new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig { Id = "light", RadiusCm = 20, HeightCm = 180, ClearanceCm = 40, Mass = 1, Layer = 0 },
                new AgentProfileConfig { Id = "heavy", RadiusCm = 40, HeightCm = 220, ClearanceCm = 50, Mass = 4, Layer = 1 },
            });
            var navProfiles = new NavMeshProfileRegistry(
                new NavMeshBakeConfig
                {
                    Profiles =
                    {
                        new NavMeshAgentProfileConfig { Id = "light", MaxClimbCm = 40, MaxSlopeDeg = 45 },
                        new NavMeshAgentProfileConfig { Id = "heavy", MaxClimbCm = 40, MaxSlopeDeg = 45 },
                    },
                },
                agentProfiles);
            // Only the light profile ships a baked store (layer 0, profile 0); the heavy
            // profile's (layer 1, profile 1) store is absent on purpose.
            var navRegistry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = CreateFlatTileStore() },
                tileSizeCm,
                tileSizeCm);
            var pathingConfig = new PathingConfig
            {
                AgentTypes =
                {
                    new PathingAgentTypeConfig { Id = "light.agent", ProfileId = "light", Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferMesh } },
                    new PathingAgentTypeConfig { Id = "heavy.agent", ProfileId = "heavy", Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferMesh } },
                },
            };
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var service = new AutoPathService(navRegistry, navProfiles, agentProfiles, store, pathingConfig);

            var lightRequest = new PathRequest(
                requestId: 1404,
                actor: default,
                domain: PathDomain.Auto,
                agentTypeId: "light.agent",
                start: PathEndpoint.FromWorldCm(1_000, 1_000),
                goal: PathEndpoint.FromWorldCm(2_000, 2_000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 8));
            Assert.That(service.TrySolve(in lightRequest, out PathResult lightResult), Is.True);
            Assert.That(lightResult.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(lightResult.ResolvedDomain, Is.EqualTo(PathDomain.NavMesh));

            var heavyRequest = new PathRequest(
                requestId: 1405,
                actor: default,
                domain: PathDomain.Auto,
                agentTypeId: "heavy.agent",
                start: PathEndpoint.FromWorldCm(1_000, 1_000),
                goal: PathEndpoint.FromWorldCm(2_000, 2_000),
                budget: new PathBudget(maxExpanded: 0, maxPoints: 8));
            Assert.That(service.TrySolve(in heavyRequest, out PathResult heavyResult), Is.True);
            Assert.That(heavyResult.Status, Is.EqualTo(PathStatus.NotReady),
                "The heavy agent's profile has no baked store; its request must not silently fall back to the light profile.");
            Assert.That(heavyResult.ErrorCode, Is.EqualTo(21));
        }

        [Test]
        public void RouteSink_AppliesWaypointOnlyForProfilesDeclaredInPathingConfig()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed, out Entity direct);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var pathService = new FakePathService(
                store,
                new Vector2(5_000, 5_000),
                new Vector2(5_300, 5_000),
                new Vector2(5_800, 5_000));
            var sink = new MassNavigationRouteExecutionSink(pathService, store, CreatePathingConfig());

            sink.BeginSync();
            MassNavigationRouteSinkResult routedTrack = sink.TrackRouteTarget(
                runtime,
                world,
                routed,
                agentIndex: 0,
                destinationWorldCm: new Vector2(5_800, 5_000),
                requestId: 77,
                maxExpanded: 128,
                maxPoints: 8);
            MassNavigationRouteSinkResult directTrack = sink.TrackRouteTarget(
                runtime,
                world,
                direct,
                agentIndex: 1,
                destinationWorldCm: new Vector2(5_800, 5_000),
                requestId: 77,
                maxExpanded: 128,
                maxPoints: 8);
            sink.EndSync();

            Assert.That(routedTrack.Tracked, Is.True);
            Assert.That(directTrack.Status, Is.EqualTo(MassNavigationRouteSinkStatus.NoConfiguredAgentType));
            Assert.That(sink.ActiveRouteCount, Is.EqualTo(1));

            MassNavigationRouteSinkResult applied = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(applied.Applied, Is.True);
            Assert.That(applied.ResolvedDomain, Is.EqualTo(PathDomain.NodeGraph));
            Assert.That(pathService.SolveCount, Is.EqualTo(1));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float routedX, out float routedY), Is.True);
            Assert.That(new Vector2(routedX, routedY), Is.EqualTo(new Vector2(5_300, 5_000)));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(1, out _, out _), Is.False);
        }

        [Test]
        public void RouteSink_CachesRouteAndAdvancesWaypointWithoutReSolving()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed, out _);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var pathService = new FakePathService(
                store,
                new Vector2(5_000, 5_000),
                new Vector2(5_300, 5_000),
                new Vector2(5_800, 5_000));
            var sink = new MassNavigationRouteExecutionSink(pathService, store, CreatePathingConfig());

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(5_800, 5_000), 88, 128, 8);
            sink.EndSync();

            MassNavigationRouteSinkResult first = sink.TryApplyTrackedRouteTargets(runtime, world);
            runtime.GetFlowSolverForTests().ApplyExternalDisplacement(new[] { 0 }, deltaXCm: 300, deltaYCm: 0);
            MassNavigationRouteSinkResult second = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(first.WaypointWorldCm, Is.EqualTo(new Vector2(5_300, 5_000)));
            Assert.That(second.WaypointWorldCm, Is.EqualTo(new Vector2(5_800, 5_000)));
            Assert.That(pathService.SolveCount, Is.EqualTo(1));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float routedX, out float routedY), Is.True);
            Assert.That(new Vector2(routedX, routedY), Is.EqualTo(new Vector2(5_800, 5_000)));
        }

        [Test]
        public void RouteSink_ConfiguredProfilePathFailureIsNotDowngradedToDirectTarget()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed, out _);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var sink = new MassNavigationRouteExecutionSink(
                new FailingPathService(),
                store,
                CreatePathingConfig());

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(5_800, 5_000), 99, 128, 8);
            sink.EndSync();

            MassNavigationRouteSinkResult result = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(result.Status, Is.EqualTo(MassNavigationRouteSinkStatus.SolveFailed));
            Assert.That(result.PathStatus, Is.EqualTo(PathStatus.NoPath));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out _, out _), Is.False);
        }

        [Test]
        public void RouteSink_MemberPathFailureDoesNotApplyPartialBatchTargets()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntimeWithTwoRoutedAgents(world, out Entity first, out Entity second);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var sink = new MassNavigationRouteExecutionSink(
                new SecondActorFailingPathService(store, second),
                store,
                CreatePathingConfig(),
                routeStateCapacity: 4,
                waypointCapacityPerAgent: 8);

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, first, 0, new Vector2(5_800, 5_000), 682, 128, 8);
            sink.TrackRouteTarget(runtime, world, second, 1, new Vector2(5_800, 5_000), 682, 128, 8);
            sink.EndSync();

            MassNavigationRouteSinkResult result = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(result.Status, Is.EqualTo(MassNavigationRouteSinkStatus.SolveFailed));
            Assert.That(result.AgentIndex, Is.EqualTo(1));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out _, out _), Is.False,
                "Route execution must prepare the full OrderId batch before committing any member target.");
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(1, out _, out _), Is.False);
        }

        [Test]
        public void RouteSink_UncommittedSyncDoesNotMutateActiveRouteState()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed, out _);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var sink = new MassNavigationRouteExecutionSink(
                new GoalEchoPathService(store),
                store,
                CreatePathingConfig(),
                routeStateCapacity: 2,
                waypointCapacityPerAgent: 8);

            var firstDestination = new Vector2(5_800, 5_000);
            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, firstDestination, 682, 128, 8);
            sink.EndSync();
            MassNavigationRouteSinkResult firstApply = sink.TryApplyTrackedRouteTargets(runtime, world);
            Assert.That(firstApply.Applied, Is.True);
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float firstX, out float firstY), Is.True);
            Assert.That(new Vector2(firstX, firstY), Is.EqualTo(firstDestination));

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(6_200, 5_000), 682, 128, 8);

            MassNavigationRouteSinkResult uncommittedApply = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(uncommittedApply.Applied, Is.True);
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float afterX, out float afterY), Is.True);
            Assert.That(new Vector2(afterX, afterY), Is.EqualTo(firstDestination),
                "BeginSync/TrackRouteTarget must stage route updates until EndSync commits the full active-key set.");
        }

        [Test]
        public void RouteSink_MemberPathFailureRestoresPreparedRouteState()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntimeWithTwoRoutedAgents(world, out Entity first, out Entity second);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var pathService = new SecondActorFailingPathService(store, second);
            var sink = new MassNavigationRouteExecutionSink(
                pathService,
                store,
                CreatePathingConfig(),
                routeStateCapacity: 4,
                waypointCapacityPerAgent: 8);

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, first, 0, new Vector2(5_800, 5_000), 682, 128, 8);
            sink.TrackRouteTarget(runtime, world, second, 1, new Vector2(5_800, 5_000), 682, 128, 8);
            sink.EndSync();

            MassNavigationRouteSinkResult firstFailure = sink.TryApplyTrackedRouteTargets(runtime, world);
            MassNavigationRouteSinkResult secondFailure = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(firstFailure.Status, Is.EqualTo(MassNavigationRouteSinkStatus.SolveFailed));
            Assert.That(secondFailure.Status, Is.EqualTo(MassNavigationRouteSinkStatus.SolveFailed));
            Assert.That(pathService.FirstActorSolveCount, Is.EqualTo(2),
                "A failed OrderId batch must not keep the successfully prepared path for an earlier member.");
        }

        [Test]
        public void RouteSink_BindingIdentityChangesWhenMapPathingServicesAreRebuilt()
        {
            var firstStore = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var firstService = new FakePathService(firstStore, new Vector2(1, 1));
            PathingConfig firstConfig = CreatePathingConfig();
            var sink = new MassNavigationRouteExecutionSink(firstService, firstStore, firstConfig);

            Assert.That(sink.IsBoundTo(firstService, firstStore, firstConfig), Is.True);

            var resumedStore = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var resumedService = new FakePathService(resumedStore, new Vector2(2, 2));
            PathingConfig resumedConfig = CreatePathingConfig();
            Assert.That(sink.IsBoundTo(resumedService, resumedStore, resumedConfig), Is.False,
                "A push/pop map restore rebuilds pathing services, so ingestion must replace its cached route sink.");
        }

        [Test]
        public void RouteSink_FullCapacityCanRetargetAllMembersWhenOldRoutesWillBeReleased()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntimeWithTwoRoutedAgents(world, out Entity firstOrderAgent, out Entity secondOrderAgent);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var sink = new MassNavigationRouteExecutionSink(
                new GoalEchoPathService(store),
                store,
                CreatePathingConfig(),
                routeStateCapacity: 1,
                waypointCapacityPerAgent: 8);

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, firstOrderAgent, 0, new Vector2(5_800, 5_000), requestId: 101, maxExpanded: 128, maxPoints: 8);
            sink.EndSync();
            sink.TryApplyTrackedRouteTargets(runtime, world);
            Assert.That(sink.ActiveRouteCount, Is.EqualTo(1));

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, secondOrderAgent, 1, new Vector2(6_200, 5_200), requestId: 202, maxExpanded: 128, maxPoints: 8);

            Assert.DoesNotThrow(() => sink.EndSync(),
                "Capacity preflight must account for old inactive routes that EndSync will release before allocating replacement routes.");
            Assert.That(sink.ActiveRouteCount, Is.EqualTo(1));
            MassNavigationRouteSinkResult applied = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(applied.Applied, Is.True);
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(1, out float secondX, out float secondY), Is.True);
            Assert.That(new Vector2(secondX, secondY), Is.EqualTo(new Vector2(6_200, 5_200)));
        }

        [Test]
        public void RouteSink_SettledOutsideAdvanceCircle_AdvancesAndRecoversToDestination()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntimeWithBlockerOnMidwayWaypoint(world, out Entity mover);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            Vector2 midwayWaypoint = new(5_300, 5_000);
            Vector2 destination = new(5_900, 4_400);
            var pathService = new FakePathService(store, new Vector2(5_050, 5_000), midwayWaypoint, destination);
            var sink = new MassNavigationRouteExecutionSink(pathService, store, CreatePathingConfig());

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, mover, agentIndex: 0, destinationWorldCm: destination, requestId: 134, maxExpanded: 128, maxPoints: 8);
            sink.EndSync();

            float advanceThresholdCm = MathF.Max(
                runtime.GetRuntimeGroupSemantics().UnitTargetStopThresholdCm * runtime.GetRuntimeRouteSemantics().WaypointAdvanceStopThresholdScale,
                runtime.GetAgentBodyRadiusCm(0) * runtime.GetRuntimeRouteSemantics().WaypointAdvanceBodyRadiusScale);
            bool settledOutsideAdvanceCircle = false;
            Vector2 targetWhenSettledOutside = default;
            Vector2 finalPosition = runtime.GetAgentWorldPositionCm(0);
            for (int i = 0; i < 600; i++)
            {
                sink.TryApplyTrackedRouteTargets(runtime, world);
                runtime.StepNavigationForTests(world, 0.05f, runHardResolve: true);
                finalPosition = runtime.GetAgentWorldPositionCm(0);
                if (!settledOutsideAdvanceCircle &&
                    runtime.GetFlowSolverForTests().IsUnitSettled(0) &&
                    runtime.TryGetAgentNavigationTargetWorldCm(0, out float settledTargetX, out float settledTargetY))
                {
                    targetWhenSettledOutside = new Vector2(settledTargetX, settledTargetY);
                    settledOutsideAdvanceCircle = Vector2.Distance(finalPosition, midwayWaypoint) > advanceThresholdCm;
                }

                if (Vector2.Distance(finalPosition, destination) <= 50f)
                {
                    break;
                }
            }

            Assert.That(settledOutsideAdvanceCircle, Is.True,
                $"Scenario must park the yielding agent outside the advance circle ({advanceThresholdCm:0}cm) with the cursor still on the blocked waypoint. Final=({finalPosition.X:0},{finalPosition.Y:0}).");
            Assert.That(targetWhenSettledOutside, Is.EqualTo(midwayWaypoint),
                "The deadlock precondition is the cursor holding an unreachable waypoint while the agent is settled outside the advance circle.");
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float finalTargetX, out float finalTargetY), Is.True);
            Assert.That(new Vector2(finalTargetX, finalTargetY), Is.EqualTo(destination),
                $"A settled agent outside the advance circle must re-target the next waypoint instead of parking forever. Final=({finalPosition.X:0},{finalPosition.Y:0}).");
            Assert.That(Vector2.Distance(finalPosition, destination), Is.LessThanOrEqualTo(50f),
                $"Recovery must end at the destination within the stop threshold, not at a yield standoff. Final=({finalPosition.X:0},{finalPosition.Y:0}).");

            bool arrivalSettled = false;
            for (int i = 0; i < 60 && !arrivalSettled; i++)
            {
                sink.TryApplyTrackedRouteTargets(runtime, world);
                runtime.StepNavigationForTests(world, 0.05f, runHardResolve: true);
                arrivalSettled = runtime.GetFlowSolverForTests().IsUnitSettled(0);
            }

            Assert.That(arrivalSettled, Is.True,
                "Completion is only claimable together with the arrival determination: the agent must settle at the destination.");
            Assert.That(Vector2.Distance(runtime.GetAgentWorldPositionCm(0), destination), Is.LessThanOrEqualTo(50f));
            Assert.That(sink.ActiveRouteCount, Is.EqualTo(1),
                "The route contract must stay active until the order side releases it; recovery may not silently drain the route.");
        }

        [Test]
        public void RouteSemantics_AdvanceCircleMustContainUnitStopCircle()
        {
            var belowStopCircle = new MassNavigationRouteSemantics
            {
                WaypointAdvanceStopThresholdScale = 0.9f,
                WaypointAdvanceBodyRadiusScale = 1.5f,
            };
            Assert.That(() => belowStopCircle.Validate(), Throws.InvalidOperationException,
                "An advance circle smaller than the solver's stop circle re-creates the settle-outside-advance-circle deadlock by configuration.");

            var matchingStopCircle = new MassNavigationRouteSemantics
            {
                WaypointAdvanceStopThresholdScale = 1f,
                WaypointAdvanceBodyRadiusScale = 1.5f,
            };
            Assert.That(() => matchingStopCircle.Validate(), Throws.Nothing);
        }

        private static MassNavigationSimulationRuntime CreateRuntime(
            World world,
            out Entity routed,
            out Entity direct)
        {
            int routedProfile = MassNavigationProfileRegistry.Register("routed");
            int directProfile = MassNavigationProfileRegistry.Register("direct");
            routed = world.Create(new MassNavigationAgent { ProfileId = routedProfile }, OrderBuffer.CreateEmpty());
            direct = world.Create(new MassNavigationAgent { ProfileId = directProfile }, OrderBuffer.CreateEmpty());

            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100),
                MassNavigationOrderChainTests.CreateLoadedChunksForTests(runtime));
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            runtime.RebuildFromAuthoredAgents(
                world,
                new[] { routed, direct },
                new[]
                {
                    new MassNavigationAgentSeed(1, 5_000, 5_000, false, 1f, 1f, 20f, 800f, layer),
                    new MassNavigationAgentSeed(1, 5_000, 5_200, false, 1f, 1f, 20f, 800f, layer),
                },
                new[] { true, true });
            return runtime;
        }

        private static MassNavigationSimulationRuntime CreateRuntimeWithTwoRoutedAgents(
            World world,
            out Entity first,
            out Entity second)
        {
            int routedProfile = MassNavigationProfileRegistry.Register("routed");
            first = world.Create(new MassNavigationAgent { ProfileId = routedProfile }, OrderBuffer.CreateEmpty());
            second = world.Create(new MassNavigationAgent { ProfileId = routedProfile }, OrderBuffer.CreateEmpty());

            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100),
                MassNavigationOrderChainTests.CreateLoadedChunksForTests(runtime));
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            runtime.RebuildFromAuthoredAgents(
                world,
                new[] { first, second },
                new[]
                {
                    new MassNavigationAgentSeed(1, 5_000, 5_000, false, 1f, 1f, 20f, 800f, layer),
                    new MassNavigationAgentSeed(1, 5_000, 5_200, false, 1f, 1f, 20f, 800f, layer),
                },
                new[] { true, true });
            return runtime;
        }

        private static MassNavigationSimulationRuntime CreateRuntimeWithBlockerOnMidwayWaypoint(
            World world,
            out Entity mover)
        {
            int routedProfile = MassNavigationProfileRegistry.Register("routed");
            mover = world.Create(new MassNavigationAgent { ProfileId = routedProfile }, OrderBuffer.CreateEmpty());
            Entity blocker = world.Create(new MassNavigationAgent { ProfileId = routedProfile }, OrderBuffer.CreateEmpty());

            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100),
                MassNavigationOrderChainTests.CreateLoadedChunksForTests(runtime));
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            runtime.RebuildFromAuthoredAgents(
                world,
                new[] { mover, blocker },
                new[]
                {
                    new MassNavigationAgentSeed(1, 5_050, 5_000, false, 1f, 1f, 20f, 800f, layer),
                    new MassNavigationAgentSeed(2, 5_300, 5_000, true, 5_000f, 1f, 150f, 800f, layer),
                },
                new[] { true, true });
            return runtime;
        }

        private static NavTileStore CreateFlatTileStore()
        {
            const int cellSizeCm = 250;
            const int chunkSizeCells = 64;
            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX: 0,
                chunkY: 0,
                layer: 0,
                tileVersion: 1,
                chunkSizeCells: chunkSizeCells,
                cellSizeCm: cellSizeCm);
            var blobs = new Dictionary<NavTileId, byte[]>();
            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                blobs[tile.TileId] = ms.ToArray();
            }

            return new NavTileStore(id => new MemoryStream(blobs[id], writable: false));
        }

        private static NavQueryService CreateFlatNavQuery()
        {
            const int tileSizeCm = 250 * 64;
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = CreateFlatTileStore() },
                tileSizeCm,
                tileSizeCm);
            Assert.That(registry.TryCreateQuery(layer: 0, profile: 0, areaCosts: null!, out NavQueryService query), Is.True);
            return query;
        }

        private static PathingConfig CreateNavMeshPathingConfig()
        {
            return new PathingConfig
            {
                AgentTypes =
                {
                    new PathingAgentTypeConfig
                    {
                        Id = "routed.agent",
                        ProfileId = "routed",
                        Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferMesh },
                    },
                },
            };
        }

        private static AutoPathService CreateNavMeshOnlyPathService(PathStore store, PathingConfig pathingConfig)
        {
            var agentProfiles = new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "routed",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0,
                },
            });
            var navProfiles = new NavMeshProfileRegistry(
                new NavMeshBakeConfig
                {
                    Profiles =
                    {
                        new NavMeshAgentProfileConfig { Id = "routed", MaxClimbCm = 40, MaxSlopeDeg = 45 },
                    },
                },
                agentProfiles);
            var navRegistry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = CreateFlatTileStore() },
                250 * 64,
                250 * 64);
            return new AutoPathService(navRegistry, navProfiles, agentProfiles, store, pathingConfig);
        }

        private static PathingConfig CreatePathingConfig()
        {
            return new PathingConfig
            {
                AgentTypes =
                {
                    new PathingAgentTypeConfig
                    {
                        Id = "routed.agent",
                        ProfileId = "routed",
                        Selection = new PathingSelectionConfig { Mode = PathSelectionMode.PreferGraph },
                    },
                },
            };
        }

        private sealed class FakePathService : IPathService
        {
            private readonly PathStore _store;
            private readonly Vector2[] _points;

            public FakePathService(PathStore store, params Vector2[] points)
            {
                _store = store;
                _points = points;
            }

            public int SolveCount { get; private set; }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                SolveCount++;
                if (!_store.TryAllocate(_points.Length, out PathHandle handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                    return true;
                }

                Span<int> xs = stackalloc int[_points.Length];
                Span<int> ys = stackalloc int[_points.Length];
                for (int i = 0; i < _points.Length; i++)
                {
                    xs[i] = (int)_points[i].X;
                    ys[i] = (int)_points[i].Y;
                }

                _store.TryWrite(in handle, xs, ys, _points.Length);
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.Found,
                    handle,
                    expanded: 3,
                    errorCode: 0,
                    resolvedDomain: PathDomain.NodeGraph);
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
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.NoPath,
                    default,
                    expanded: 0,
                    errorCode: 33);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                count = 0;
                return false;
            }
        }

        private sealed class GoalEchoPathService : IPathService
        {
            private readonly PathStore _store;

            public GoalEchoPathService(PathStore store)
            {
                _store = store;
            }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                if (!_store.TryAllocate(2, out PathHandle handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                    return true;
                }

                Span<int> xs = stackalloc int[2];
                Span<int> ys = stackalloc int[2];
                xs[0] = request.Start.Xcm;
                ys[0] = request.Start.Ycm;
                xs[1] = request.Goal.Xcm;
                ys[1] = request.Goal.Ycm;
                _store.TryWrite(in handle, xs, ys, 2);
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.Found,
                    handle,
                    expanded: 2,
                    errorCode: 0,
                    resolvedDomain: PathDomain.NodeGraph);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }

        private sealed class SecondActorFailingPathService : IPathService
        {
            private readonly PathStore _store;
            private readonly Entity _failingActor;

            public SecondActorFailingPathService(PathStore store, Entity failingActor)
            {
                _store = store;
                _failingActor = failingActor;
            }

            public int FirstActorSolveCount { get; private set; }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                if (request.Actor == _failingActor)
                {
                    result = new PathResult(
                        request.RequestId,
                        request.Actor,
                        PathStatus.NoPath,
                        default,
                        expanded: 0,
                        errorCode: 682);
                    return true;
                }

                FirstActorSolveCount++;
                if (!_store.TryAllocate(2, out PathHandle handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                    return true;
                }

                Span<int> xs = stackalloc int[2];
                Span<int> ys = stackalloc int[2];
                xs[0] = request.Start.Xcm;
                ys[0] = request.Start.Ycm;
                xs[1] = request.Goal.Xcm;
                ys[1] = request.Goal.Ycm;
                _store.TryWrite(in handle, xs, ys, 2);
                result = new PathResult(
                    request.RequestId,
                    request.Actor,
                    PathStatus.Found,
                    handle,
                    expanded: 2,
                    errorCode: 0,
                    resolvedDomain: PathDomain.NodeGraph);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }
    }
}
