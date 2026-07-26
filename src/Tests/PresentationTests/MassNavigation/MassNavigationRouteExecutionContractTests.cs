using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;

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
        public void RouteSink_TryGetActiveRouteEvidence_IsAbsentBeforeRoute_PresentAfterApply_WithoutMutating()
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

            Assert.That(sink.TryGetActiveRouteEvidence(routed, out MassNavigationRouteEvidence before), Is.False);
            Assert.That(before, Is.EqualTo(default(MassNavigationRouteEvidence)));

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(5_800, 5_000), 441, 128, 8);
            sink.EndSync();

            MassNavigationRouteSinkResult applied = sink.TryApplyTrackedRouteTargets(runtime, world);
            Assert.That(applied.Applied, Is.True);
            Assert.That(pathService.SolveCount, Is.EqualTo(1));

            Assert.That(sink.TryGetActiveRouteEvidence(routed, out MassNavigationRouteEvidence evidence), Is.True);
            Assert.That(evidence.OrderToken, Is.EqualTo(441));
            Assert.That(evidence.AgentIndex, Is.EqualTo(0));
            Assert.That(evidence.ResolvedDomain, Is.EqualTo(PathDomain.NodeGraph));
            Assert.That(evidence.WaypointCount, Is.EqualTo(3));
            Assert.That(evidence.CurrentWaypointIndex, Is.EqualTo(1),
                "Solve copies the start sample then advances past it when the agent already sits on it.");
            Assert.That(evidence.RouteReady, Is.True);
            Assert.That(evidence.WaypointGeometrySignature, Is.Not.EqualTo(0UL));

            int solveBeforeLookup = pathService.SolveCount;
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float targetX, out float targetY), Is.True);
            var targetBefore = new Vector2(targetX, targetY);

            Assert.That(sink.TryGetActiveRouteEvidence(routed, out MassNavigationRouteEvidence second), Is.True);
            Assert.That(second.OrderToken, Is.EqualTo(evidence.OrderToken));
            Assert.That(second.AgentIndex, Is.EqualTo(evidence.AgentIndex));
            Assert.That(second.ResolvedDomain, Is.EqualTo(evidence.ResolvedDomain));
            Assert.That(second.WaypointCount, Is.EqualTo(evidence.WaypointCount));
            Assert.That(second.CurrentWaypointIndex, Is.EqualTo(evidence.CurrentWaypointIndex));
            Assert.That(second.RouteReady, Is.EqualTo(evidence.RouteReady));
            Assert.That(second.WaypointGeometrySignature, Is.EqualTo(evidence.WaypointGeometrySignature));
            Assert.That(pathService.SolveCount, Is.EqualTo(solveBeforeLookup));
            Assert.That(runtime.TryGetAgentNavigationTargetWorldCm(0, out float afterX, out float afterY), Is.True);
            Assert.That(new Vector2(afterX, afterY), Is.EqualTo(targetBefore));
            Assert.That(sink.ActiveRouteCount, Is.EqualTo(1));
        }

        [TestCase(PathSelectionMode.PreferMesh, PathDomain.NavMesh)]
        [TestCase(PathSelectionMode.PreferGraph, PathDomain.NodeGraph)]
        [TestCase(PathSelectionMode.AutoCheapest, PathDomain.Auto)]
        [TestCase(PathSelectionMode.Direct, PathDomain.Auto)]
        public void RouteSink_UsesConfiguredSelectionDomainForPathRequest(
            PathSelectionMode selectionMode,
            PathDomain expectedDomain)
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed, out _);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var pathService = new FakePathService(
                store,
                new Vector2(5_000, 5_000),
                new Vector2(5_800, 5_000));
            var sink = new MassNavigationRouteExecutionSink(
                pathService,
                store,
                CreatePathingConfig(selectionMode));

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(5_800, 5_000), 441, 128, 8);
            sink.EndSync();
            MassNavigationRouteSinkResult applied = sink.TryApplyTrackedRouteTargets(runtime, world);

            Assert.That(applied.Applied, Is.True);
            Assert.That(pathService.LastRequestDomain, Is.EqualTo(expectedDomain));
        }

        [Test]
        public void RouteSink_TargetedRemoval_RemovesOnlyRequestedAgentAndPreservesSibling()
        {
            using var world = World.Create();
            MassNavigationSimulationRuntime runtime = CreateRuntimeWithTwoRoutedAgents(
                world,
                out Entity agentA,
                out Entity agentB);
            var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
            var sink = new MassNavigationRouteExecutionSink(
                new GoalEchoPathService(store),
                store,
                CreatePathingConfig(),
                routeStateCapacity: 4,
                waypointCapacityPerAgent: 8);

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, agentA, 0, new Vector2(5_800, 5_000), requestId: 701, maxExpanded: 128, maxPoints: 8);
            sink.TrackRouteTarget(runtime, world, agentB, 1, new Vector2(6_200, 5_200), requestId: 702, maxExpanded: 128, maxPoints: 8);
            sink.EndSync();
            Assert.That(sink.TryApplyTrackedRouteTargets(runtime, world).Applied, Is.True);
            Assert.That(sink.ActiveRouteCount, Is.EqualTo(2));
            Assert.That(sink.TryGetActiveRouteEvidence(agentA, out MassNavigationRouteEvidence evidenceA), Is.True);
            Assert.That(sink.TryGetActiveRouteEvidence(agentB, out MassNavigationRouteEvidence evidenceB), Is.True);

            Assert.Throws<ArgumentException>(() => sink.RemoveAgent(Entity.Null));

            sink.RemoveAgent(agentA);

            Assert.That(sink.ActiveRouteCount, Is.EqualTo(1));
            Assert.That(sink.TryGetActiveRouteEvidence(agentA, out _), Is.False);
            Assert.That(sink.TryGetActiveRouteEvidence(agentB, out MassNavigationRouteEvidence preservedB), Is.True);
            Assert.That(preservedB.OrderToken, Is.EqualTo(evidenceB.OrderToken));
            Assert.That(preservedB.AgentIndex, Is.EqualTo(evidenceB.AgentIndex));
            Assert.That(preservedB.ResolvedDomain, Is.EqualTo(evidenceB.ResolvedDomain));
            Assert.That(preservedB.WaypointCount, Is.EqualTo(evidenceB.WaypointCount));
            Assert.That(preservedB.CurrentWaypointIndex, Is.EqualTo(evidenceB.CurrentWaypointIndex));
            Assert.That(preservedB.RouteReady, Is.EqualTo(evidenceB.RouteReady));
            Assert.That(evidenceA.OrderToken, Is.EqualTo(701));
        }

        [Test]
        public void RouteSink_TryGetActiveRouteEvidence_RejectsAmbiguousRoutesForOneEntity()
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

            sink.BeginSync();
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(5_800, 5_000), 441, 128, 8);
            sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(6_200, 5_000), 442, 128, 8);
            sink.EndSync();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => sink.TryGetActiveRouteEvidence(routed, out _))!;
            Assert.That(error.Message, Does.Contain("more than one active RouteState"));
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

        private static PathingConfig CreatePathingConfig(
            PathSelectionMode selectionMode = PathSelectionMode.PreferGraph)
        {
            return new PathingConfig
            {
                AgentTypes =
                {
                    new PathingAgentTypeConfig
                    {
                        Id = "routed.agent",
                        ProfileId = "routed",
                        Selection = new PathingSelectionConfig { Mode = selectionMode },
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
            public PathDomain LastRequestDomain { get; private set; }

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                SolveCount++;
                LastRequestDomain = request.Domain;
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
