using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// NavLinkGraph / NavLinkRegistry 的运行时合同：按 (layer, profile) 过滤、
/// 方向语义、可用性开关与 revision、以及"未声明 Link = 无连接"而不是直线补救。
/// </summary>
[TestFixture]
public sealed class NavLinkRuntimeContractTests
{
    private static readonly NavLayerConfig[] Layers =
    {
        new NavLayerConfig { Id = "Ground", Layer = 0 },
        new NavLayerConfig { Id = "Mountain", Layer = 1 },
        new NavLayerConfig { Id = "Water", Layer = 2 }
    };

    private static AgentProfileRegistry AgentProfiles() => new(new[]
    {
        new AgentProfileConfig { Id = "infantry", RadiusCm = 50, HeightCm = 180, ClearanceCm = 50, Mass = 80, Layer = 0 },
        new AgentProfileConfig { Id = "ship", RadiusCm = 300, HeightCm = 400, ClearanceCm = 300, DraftCm = 120, BeamCm = 600, Mass = 5000, Layer = 2 },
        new AgentProfileConfig { Id = "amphibious", RadiusCm = 250, HeightCm = 350, ClearanceCm = 250, DraftCm = 100, BeamCm = 500, Mass = 4000, Layer = 0 }
    });

    private static NavMeshProfileRegistry Profiles(AgentProfileRegistry agents)
    {
        var cfg = new NavMeshBakeConfig
        {
            Profiles = new List<NavMeshAgentProfileConfig>
            {
                new NavMeshAgentProfileConfig { Id = "infantry", MaxClimbCm = 45, MaxSlopeDeg = 35 },
                new NavMeshAgentProfileConfig { Id = "ship", MaxClimbCm = 0, MaxSlopeDeg = 1 },
                new NavMeshAgentProfileConfig { Id = "amphibious", MaxClimbCm = 45, MaxSlopeDeg = 35 }
            }
        };
        return new NavMeshProfileRegistry(cfg, agents);
    }

    private static NavLinkGraph BuildGraph(IReadOnlyList<NavLinkConfig> links)
    {
        AgentProfileRegistry agents = AgentProfiles();
        NavMeshProfileRegistry profiles = Profiles(agents);
        var layers = new NavLayerResolver(Layers);
        return new NavLinkGraph(links, profiles, agents, layers.RequireLayer);
    }

    private static NavLinkConfig Link(
        string id,
        string fromLayer,
        string toLayer,
        bool bidirectional = true,
        float cost = 10f,
        string? action = null,
        params string[] allowedProfiles)
    {
        return new NavLinkConfig
        {
            Id = id,
            From = new NavLinkEndpointConfig { Layer = fromLayer, XCm = 1000, YCm = 2000 },
            To = new NavLinkEndpointConfig { Layer = toLayer, XCm = 3000, YCm = 4000 },
            Bidirectional = bidirectional,
            Cost = cost,
            Action = action,
            AllowedProfiles = allowedProfiles.ToList()
        };
    }

    [Test]
    public void Graph_ResolvesLayerNamesToIndices()
    {
        NavLinkGraph graph = BuildGraph(new[] { Link("pier", "Ground", "Water", allowedProfiles: "amphibious") });

        NavLink link = graph.Get(0);
        Assert.That(link.FromLayer, Is.EqualTo(0));
        Assert.That(link.ToLayer, Is.EqualTo(2));
        Assert.That(link.IsCrossLayer, Is.True);
    }

    [Test]
    public void Graph_FiltersByProfile()
    {
        NavLinkGraph graph = BuildGraph(new[] { Link("pier", "Ground", "Water", allowedProfiles: "amphibious") });

        Assert.That(graph.Get(0).AllowsProfile(2), Is.True, "amphibious is profile index 2.");
        Assert.That(graph.Get(0).AllowsProfile(0), Is.False, "infantry is not allowed on this link.");
        Assert.That(graph.Get(0).AllowsProfile(1), Is.False, "ship is not allowed on this link.");
    }

    [Test]
    public void Graph_EachEndpointLayerIndexesTheLink()
    {
        NavLinkGraph graph = BuildGraph(new[] { Link("pier", "Ground", "Water", allowedProfiles: "amphibious") });

        Assert.That(graph.LinksTouchingLayer(0).ToArray(), Is.EqualTo(new[] { 0 }), "ground end.");
        Assert.That(graph.LinksTouchingLayer(2).ToArray(), Is.EqualTo(new[] { 0 }), "water end.");
        Assert.That(graph.LinksTouchingLayer(1).ToArray(), Is.Empty, "mountain has no connection here.");
    }

    [Test]
    public void Graph_LeavingLayerIsDirectional()
    {
        NavLinkGraph graph = BuildGraph(new[]
        {
            Link("pier", "Ground", "Water", allowedProfiles: "amphibious"),
            Link("beach", "Water", "Ground", bidirectional: false, allowedProfiles: "amphibious")
        });

        Assert.That(graph.LinksLeavingLayer(0).ToArray(), Is.EqualTo(new[] { 0 }), "only pier departs ground.");
        Assert.That(graph.LinksLeavingLayer(2).ToArray(), Is.EqualTo(new[] { 1 }), "only beach departs water.");
    }

    [Test]
    public void Graph_SelectUsableLinkSkipsDisabledAndWrongProfile()
    {
        NavLinkGraph graph = BuildGraph(new[]
        {
            Link("infantry_pass", "Ground", "Mountain", allowedProfiles: "infantry"),
            Link("amphibious_pass", "Ground", "Mountain", allowedProfiles: "amphibious")
        });

        // profile 0 = infantry, 1 = ship, 2 = amphibious
        Assert.That(graph.TrySelectUsableLink(0, profileIndex: 0, out NavLink first), Is.True);
        Assert.That(first.LinkId, Is.EqualTo("infantry_pass"));

        Assert.That(graph.SetEnabled(0, false), Is.True);
        Assert.That(graph.TrySelectUsableLink(0, profileIndex: 0, out _), Is.False,
            "infantry has no remaining ground-departing link after its own is disabled.");

        Assert.That(graph.TrySelectUsableLink(0, profileIndex: 2, out NavLink second), Is.True);
        Assert.That(second.LinkId, Is.EqualTo("amphibious_pass"), "amphibious still has its own link.");

        Assert.That(graph.TrySelectUsableLink(0, profileIndex: 1, out _), Is.False,
            "ship profile is allowed on no ground-departing link.");
    }

    [Test]
    public void Graph_DisablingAdvancesRevision_ReEnablingIsIdempotent()
    {
        NavLinkGraph graph = BuildGraph(new[] { Link("pier", "Ground", "Water", allowedProfiles: "amphibious") });
        uint initial = graph.Revision;

        Assert.That(graph.SetEnabled(0, false), Is.True);
        uint afterDisable = graph.Revision;
        Assert.That(afterDisable, Is.Not.EqualTo(initial), "availability change must invalidate referencing paths.");

        Assert.That(graph.SetEnabled(0, false), Is.False, "redundant disable is a no-op.");
        Assert.That(graph.Revision, Is.EqualTo(afterDisable), "no-op must not churn revision.");

        Assert.That(graph.SetEnabled(0, true), Is.True);
        Assert.That(graph.Revision, Is.Not.EqualTo(afterDisable));
    }

    [Test]
    public void Graph_LinkWithoutAllowedProfilesIsUsableByNobody()
    {
        NavLinkGraph graph = BuildGraph(new[] { Link("orphan", "Ground", "Water") });

        Assert.That(graph.Get(0).Usage.IsNone, Is.True);
        Assert.That(graph.Get(0).AllowsProfile(0), Is.False);
        Assert.That(graph.TrySelectUsableLink(0, profileIndex: 0, out _), Is.False);
    }

    [Test]
    public void Graph_UndeclaredLayerIsRejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            BuildGraph(new[] { Link("bad", "Ground", "Sky", allowedProfiles: "infantry") }))!;

        Assert.That(ex.Message, Does.Contain("NavMesh layer 'Sky' is not declared"));
    }

    [Test]
    public void Registry_MapWithoutLinksHasNoGraph()
    {
        var registry = new NavLinkRegistry(new Dictionary<string, NavLinkGraph>());

        Assert.That(registry.TryGetGraph("some_map", out _), Is.False,
            "a map that declares no links must report no connection, not a fallback.");
        Assert.That(
            () => registry.RequireGraph("some_map"),
            Throws.InvalidOperationException.With.Message.Contains("no link graph"));
    }

    [Test]
    public void Registry_BuildOnlyRegistersMapsWithLinks()
    {
        AgentProfileRegistry agents = AgentProfiles();
        NavMeshProfileRegistry profiles = Profiles(agents);
        var layers = new NavLayerResolver(Layers);

        var withoutLinks = new NavMeshBakeConfig { Links = new List<NavLinkConfig>() };
        Assert.That(
            NavLinkRegistry.Build(withoutLinks, profiles, agents, layers, "quiet_map").MapCount,
            Is.Zero);

        var withLinks = new NavMeshBakeConfig
        {
            Links = new List<NavLinkConfig> { Link("pier", "Ground", "Water", allowedProfiles: "amphibious") }
        };
        NavLinkRegistry registry = NavLinkRegistry.Build(withLinks, profiles, agents, layers, "strait");
        Assert.That(registry.MapCount, Is.EqualTo(1));
        Assert.That(registry.RequireGraph("strait").Count, Is.EqualTo(1));
    }

    [Test]
    public void Graph_RejectsProfileThatHasNoBakeProfile()
    {
        AgentProfileRegistry agents = new(new[]
        {
            new AgentProfileConfig { Id = "ghost", RadiusCm = 50, HeightCm = 180, ClearanceCm = 50, Mass = 1, Layer = 0 }
        });
        var layers = new NavLayerResolver(Layers);
        NavMeshProfileRegistry profiles = Profiles(AgentProfiles());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new NavLinkGraph(
                new[] { Link("bad", "Ground", "Water", allowedProfiles: "ghost") },
                profiles,
                agents,
                layers.RequireLayer))!;

        Assert.That(ex.Message, Does.Contain("declares no bake profile for it"));
    }
}
