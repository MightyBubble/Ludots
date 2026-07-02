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

        private static MassNavigationSimulationRuntime CreateRuntime(
            World world,
            out Entity routed,
            out Entity direct)
        {
            int routedProfile = MassNavigationProfileRegistry.Register("routed");
            int directProfile = MassNavigationProfileRegistry.Register("direct");
            routed = world.Create(new MassNavigationAgent { ProfileId = routedProfile }, OrderBuffer.CreateEmpty());
            direct = world.Create(new MassNavigationAgent { ProfileId = directProfile }, OrderBuffer.CreateEmpty());

            MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
            var runtime = new MassNavigationSimulationRuntime(config);
            runtime.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100));
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
    }
}
