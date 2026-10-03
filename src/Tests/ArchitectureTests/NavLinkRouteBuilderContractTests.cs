using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// NavLinkRouteBuilder 的合同：同层单腿；跨层必须经合法且允许该 profile 的 Link；
/// 找不到 Link 时报明确原因，**不回退为跨层直线**。
/// </summary>
[TestFixture]
public sealed class NavLinkRouteBuilderContractTests
{
    private static readonly NavLayerConfig[] LayerConfigs =
    {
        new NavLayerConfig { Id = "Ground", Layer = 0 },
        new NavLayerConfig { Id = "Mountain", Layer = 1 },
        new NavLayerConfig { Id = "Water", Layer = 2 }
    };

    [Test]
    public void SameLayer_ProducesSingleSurfaceSegment()
    {
        var fixture = new Fixture(links: Array.Empty<NavLinkConfig>());
        var px = new int[64];
        var py = new int[64];
        var segs = new PathSegment[8];

        bool ok = fixture.Builder.TryBuild(
            default, "amphibious", "strait", profileIndex: 2,
            startLayer: 0, PathEndpoint.FromWorldCm(100, 100),
            goalLayer: 0, PathEndpoint.FromWorldCm(900, 900),
            px, py, segs, out int pointCount, out int segmentCount, out string failure);

        Assert.That(ok, Is.True, failure);
        Assert.That(segmentCount, Is.EqualTo(1));
        Assert.That(segs[0].Kind, Is.EqualTo(PathSegmentKind.Surface));
        Assert.That(segs[0].LinkId, Is.EqualTo(-1));
        Assert.That(pointCount, Is.EqualTo(2), "fake solver emits straight two-point legs.");
    }

    [Test]
    public void CrossLayer_ProducesSurfaceLinkSurface()
    {
        var fixture = new Fixture(new[]
        {
            new NavLinkConfig
            {
                Id = "pier",
                From = new NavLinkEndpointConfig { Layer = "Ground", XCm = 400, YCm = 200 },
                To = new NavLinkEndpointConfig { Layer = "Water", XCm = 420, YCm = 190 },
                Bidirectional = true,
                AllowedProfiles = new List<string> { "amphibious" }
            }
        });
        var px = new int[64];
        var py = new int[64];
        var segs = new PathSegment[8];

        bool ok = fixture.Builder.TryBuild(
            default, "amphibious", "strait", profileIndex: 2,
            startLayer: 0, PathEndpoint.FromWorldCm(100, 100),
            goalLayer: 2, PathEndpoint.FromWorldCm(900, 900),
            px, py, segs, out int pointCount, out int segmentCount, out string failure);

        Assert.That(ok, Is.True, failure);
        Assert.That(segmentCount, Is.EqualTo(3), "surface -> link -> surface.");
        Assert.That(segs[0].Kind, Is.EqualTo(PathSegmentKind.Surface));
        Assert.That(segs[1].Kind, Is.EqualTo(PathSegmentKind.Link));
        Assert.That(segs[1].LinkId, Is.EqualTo(0));
        Assert.That(segs[2].Kind, Is.EqualTo(PathSegmentKind.Surface));

        Assert.That(segs[1].PointCount, Is.EqualTo(2), "a link segment spans exactly its two endpoints.");
        Assert.That(px[segs[1].StartPointIndex], Is.EqualTo(400));
        Assert.That(py[segs[1].StartPointIndex], Is.EqualTo(200));
        Assert.That(px[segs[1].StartPointIndex + 1], Is.EqualTo(420));
        Assert.That(py[segs[1].StartPointIndex + 1], Is.EqualTo(190));
    }

    [Test]
    public void CrossLayer_WithoutLinks_ReportsNoConnectionInsteadOfStraightLine()
    {
        var fixture = new Fixture(links: Array.Empty<NavLinkConfig>());
        var px = new int[64];
        var py = new int[64];
        var segs = new PathSegment[8];

        bool ok = fixture.Builder.TryBuild(
            default, "amphibious", "quiet_map", profileIndex: 2,
            startLayer: 0, PathEndpoint.FromWorldCm(100, 100),
            goalLayer: 2, PathEndpoint.FromWorldCm(900, 900),
            px, py, segs, out int pointCount, out int segmentCount, out string failure);

        Assert.That(ok, Is.False, "a cross-layer request without links must fail.");
        Assert.That(pointCount, Is.Zero, "no straight-line fallback may be emitted.");
        Assert.That(segmentCount, Is.Zero);
        Assert.That(failure, Does.Contain("declares no cross-surface links"));
    }

    [Test]
    public void CrossLayer_WrongProfile_IsRefused()
    {
        var fixture = new Fixture(new[]
        {
            new NavLinkConfig
            {
                Id = "land_bridge",
                From = new NavLinkEndpointConfig { Layer = "Ground", XCm = 400, YCm = 200 },
                To = new NavLinkEndpointConfig { Layer = "Water", XCm = 420, YCm = 190 },
                Bidirectional = true,
                AllowedProfiles = new List<string> { "infantry" }
            }
        });
        var px = new int[64];
        var py = new int[64];
        var segs = new PathSegment[8];

        bool ok = fixture.Builder.TryBuild(
            default, "amphibious", "strait", profileIndex: 2,
            startLayer: 0, PathEndpoint.FromWorldCm(100, 100),
            goalLayer: 2, PathEndpoint.FromWorldCm(900, 900),
            px, py, segs, out _, out _, out string failure);

        Assert.That(ok, Is.False);
        Assert.That(failure, Does.Contain("No usable link leaves layer Ground"));
    }

    [Test]
    public void CrossLayer_DisabledLink_IsRefused()
    {
        var fixture = new Fixture(new[]
        {
            new NavLinkConfig
            {
                Id = "pier",
                From = new NavLinkEndpointConfig { Layer = "Ground", XCm = 400, YCm = 200 },
                To = new NavLinkEndpointConfig { Layer = "Water", XCm = 420, YCm = 190 },
                Bidirectional = true,
                AllowedProfiles = new List<string> { "amphibious" }
            }
        });
        NavLinkGraph graph = fixture.Links.RequireGraph("strait");
        Assert.That(graph.SetEnabled(0, false), Is.True);

        var px = new int[64];
        var py = new int[64];
        var segs = new PathSegment[8];

        bool ok = fixture.Builder.TryBuild(
            default, "amphibious", "strait", profileIndex: 2,
            startLayer: 0, PathEndpoint.FromWorldCm(100, 100),
            goalLayer: 2, PathEndpoint.FromWorldCm(900, 900),
            px, py, segs, out _, out _, out string failure);

        Assert.That(ok, Is.False);
        Assert.That(failure, Does.Contain("No usable link leaves layer Ground"));
    }

    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<NavLinkConfig> links)
        {
            PathStore = new PathStore(maxPaths: 64, maxPointsPerPath: 64);
            var service = new StraightLinePathService(PathStore);
            AgentProfileRegistry agents = new(new[]
            {
                new AgentProfileConfig { Id = "infantry", RadiusCm = 50, HeightCm = 180, ClearanceCm = 50, Mass = 80, Layer = 0 },
                new AgentProfileConfig { Id = "ship", RadiusCm = 300, HeightCm = 400, ClearanceCm = 300, DraftCm = 120, BeamCm = 600, Mass = 5000, Layer = 2 },
                new AgentProfileConfig { Id = "amphibious", RadiusCm = 250, HeightCm = 350, ClearanceCm = 250, DraftCm = 100, BeamCm = 500, Mass = 4000, Layer = 0 }
            });
            var profileConfig = new NavMeshBakeConfig
            {
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "infantry", MaxClimbCm = 45, MaxSlopeDeg = 35 },
                    new NavMeshAgentProfileConfig { Id = "ship", MaxClimbCm = 0, MaxSlopeDeg = 1 },
                    new NavMeshAgentProfileConfig { Id = "amphibious", MaxClimbCm = 45, MaxSlopeDeg = 35 }
                }
            };
            var profiles = new NavMeshProfileRegistry(profileConfig, agents);
            var layers = new NavLayerResolver(LayerConfigs);
            Links = NavLinkRegistry.Build(
                new NavMeshBakeConfig { Links = links.ToList() },
                profiles,
                agents,
                layers,
                "strait");
            Builder = new NavLinkRouteBuilder(service, PathStore, Links, layers);
        }

        public PathStore PathStore { get; }

        public NavLinkRegistry Links { get; }

        public NavLinkRouteBuilder Builder { get; }
    }

    /// <summary>
    /// 假求解器：每个请求向共享 store 写入"起点 → 终点"两点直线。
    /// 让本测试只验证路线分段合同，不依赖 Recast 烘焙。
    /// </summary>
    private sealed class StraightLinePathService : IPathService
    {
        private readonly PathStore _store;

        public StraightLinePathService(PathStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (!_store.TryAllocate(pointCapacity: 2, out PathHandle handle))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Error, default, 0, errorCode: 99);
                return false;
            }

            bool written = _store.TryWrite(
                in handle,
                new[] { request.Start.Xcm, request.Goal.Xcm },
                new[] { request.Start.Ycm, request.Goal.Ycm },
                count: 2);
            if (!written)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Error, default, 0, errorCode: 98);
                return false;
            }

            result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, expanded: 2, errorCode: 0, resolvedDomain: PathDomain.NavMesh);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }
    }
}
