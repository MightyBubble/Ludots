using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MassNavigationRouteRevisionTests
{
    [SetUp]
    public void ResetProfiles()
    {
        MassNavigationProfileRegistry.Reset();
    }

    [Test]
    public void RouteSink_InvalidatesAndReSolvesWhenStoreRevisionChanges()
    {
        using var world = World.Create();
        MassNavigationSimulationRuntime runtime = CreateRuntime(world, out Entity routed);
        var store = new PathStore(maxPaths: 4, maxPointsPerPath: 8);
        var tiles = new NavTileStore(_ => throw new InvalidOperationException("revision test does not load tile bytes"));
        var pathService = new PublishingPathService(store, tiles);
        var sink = new MassNavigationRouteExecutionSink(pathService, store, CreatePathingConfig());
        var log = new LineLog();
        Log.AddBackend(log);

        sink.BeginSync();
        sink.TrackRouteTarget(runtime, world, routed, 0, new Vector2(8_000, 5_000), 75, 128, 8);
        sink.EndSync();

        MassNavigationRouteSinkResult first = sink.TryApplyTrackedRouteTargets(runtime, world);
        MassNavigationRouteSinkResult cached = sink.TryApplyTrackedRouteTargets(runtime, world);

        pathService.WaypointXcm = 8_000;
        tiles.Replace(DefaultGridNavTileFactory.CreateFlatTile(0, 0, 0, 1, 4, 100));
        MassNavigationRouteSinkResult resolved = sink.TryApplyTrackedRouteTargets(runtime, world);
        MassNavigationRouteSinkResult steady = sink.TryApplyTrackedRouteTargets(runtime, world);

        Assert.That(first.Applied, Is.True);
        Assert.That(first.WaypointWorldCm, Is.EqualTo(new Vector2(7_000, 5_000)));
        Assert.That(cached.WaypointWorldCm, Is.EqualTo(new Vector2(7_000, 5_000)));
        Assert.That(resolved.Applied, Is.True);
        Assert.That(resolved.WaypointWorldCm, Is.EqualTo(new Vector2(8_000, 5_000)));
        Assert.That(steady.WaypointWorldCm, Is.EqualTo(new Vector2(8_000, 5_000)));
        Assert.That(pathService.SolveCount, Is.EqualTo(2));
        Assert.That(log.Text, Does.Contain("MassNavigation route invalidated:"));
        Assert.That(log.Text, Does.Contain("solvedRevision=0"));
        Assert.That(log.Text, Does.Contain("storeRevision=1"));

        Directory.CreateDirectory("/opt/cursor/artifacts/screenshots/l75q");
        File.WriteAllText("/opt/cursor/artifacts/screenshots/l75q/route-invalidation.log", log.Text);
    }

    private static MassNavigationSimulationRuntime CreateRuntime(World world, out Entity routed)
    {
        int routedProfile = MassNavigationProfileRegistry.Register("routed");
        routed = world.Create(
            new MassNavigationAgent { ProfileId = routedProfile, AgentTypeId = "routed.agent" },
            OrderBuffer.CreateEmpty());

        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        var runtime = new MassNavigationSimulationRuntime(config);
        runtime.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 10_000, 10_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(runtime));
        var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
        runtime.RebuildFromAuthoredAgents(
            world,
            new[] { routed },
            new[] { new MassNavigationAgentSeed(1, 5_000, 5_000, false, 1f, 1f, 20f, 800f, layer) },
            new[] { true });
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

    private sealed class PublishingPathService : IPathService
    {
        private readonly PathStore _store;
        private readonly NavTileStore _tiles;

        public PublishingPathService(PathStore store, NavTileStore tiles)
        {
            _store = store;
            _tiles = tiles;
        }

        public int SolveCount { get; private set; }

        public int WaypointXcm { get; set; } = 7_000;

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            SolveCount++;
            if (!_store.TryAllocate(1, out PathHandle handle))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, 4);
                return true;
            }

            Span<int> xs = stackalloc int[1];
            Span<int> ys = stackalloc int[1];
            xs[0] = WaypointXcm;
            ys[0] = 5_000;
            _store.TryWrite(in handle, xs, ys, 1);
            result = new PathResult(
                request.RequestId,
                request.Actor,
                PathStatus.Found,
                handle,
                expanded: 1,
                errorCode: 0,
                resolvedDomain: PathDomain.NavMesh);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }

        public bool TryReadNavPublication(string agentTypeId, out int layer, out int profile, out uint revision)
        {
            layer = 0;
            profile = 0;
            revision = _tiles.Revision;
            return agentTypeId == "routed.agent";
        }
    }

    private sealed class LineLog : ILogBackend
    {
        private readonly StringBuilder _text = new();

        public string Text => _text.ToString();

        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            if (message.Contains("route invalidated", StringComparison.Ordinal))
            {
                _text.AppendLine(message);
            }
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}
