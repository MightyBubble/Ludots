using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.World;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>地图上的阻挡和条带按实体上的 CrowdSimulationStructure 点名模板入仓；点错、漏写、写了不匹配的模板都在开图时报错。</summary>
[TestFixture]
public class CrowdMapStructureTemplateTests
{
    [Test]
    public void MapLayeredPath_NamingMissingTemplate_Throws()
    {
        var json = TestDefaults.DefaultConfigJson();
        json["structures"]!["templates"]!.AsObject().Remove("bridge");
        var config = TestDefaults.Assemble(CrowdSimulationConfig.Load(json));
        var store = BuildStore(config);

        var ex = Assert.Throws<InvalidOperationException>(() => store.InstallMapLayeredPaths(new[] { Path(config, "bridge") }));
        Assert.That(ex!.Message, Does.Contain("\"bridge\"").And.Contain("不存在"));
    }

    [Test]
    public void MapLayeredPath_NamingBlockerTemplate_Throws()
    {
        var config = TestDefaults.Assemble();
        var store = BuildStore(config);

        var ex = Assert.Throws<InvalidOperationException>(() => store.InstallMapLayeredPaths(new[] { Path(config, "building") }));
        Assert.That(ex!.Message, Does.Contain("\"building\"").And.Contain("path"));
    }

    [Test]
    public void MapLayeredPath_NamingTimedTemplate_Throws()
    {
        var config = TestDefaults.Assemble();
        var store = BuildStore(config);

        var ex = Assert.Throws<InvalidOperationException>(() => store.InstallMapLayeredPaths(new[] { Path(config, "timedBridge") }));
        Assert.That(ex!.Message, Does.Contain("寿命"));
    }

    [Test]
    public void MapLayeredPaths_InstallWithNamedTemplate()
    {
        var config = TestDefaults.Assemble();
        var store = BuildStore(config);
        int before = store.EntityIds.Count;

        store.InstallMapLayeredPaths(new[] { Path(config, "bridge"), Path(config, "bridge") });

        Assert.That(store.EntityIds.Count, Is.EqualTo(before + 2));
        int last = store.EntityIds[^1];
        Assert.That(store.LiveTemplate(last).Id, Is.EqualTo("bridge"));
        Assert.That(store.FootprintOf(last).Shape, Is.EqualTo(CrowdStructureShape.Path));
    }

    [Test]
    public void MapBlocker_NamingAreaTemplate_Throws()
    {
        var config = TestDefaults.Assemble();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdStructuresStore.Build(config, EmptyGrid(config), new[] { new BlockerFootprint(0, 0, 100, "road") }));
        Assert.That(ex!.Message, Does.Contain("\"road\"").And.Contain("square").And.Contain("block"));
    }

    [Test]
    public void Extract_BlockerWithoutStructureComponent_Throws()
    {
        var template = Template("wall", Obstacle());
        var ex = Assert.Throws<InvalidOperationException>(() => Extract(template, Instance("wall_1", "wall", Position(100, 200))));
        Assert.That(ex!.Message, Does.Contain("wall_1").And.Contain("CrowdSimulationStructure"));
    }

    [Test]
    public void Extract_ObstacleMissingField_Throws()
    {
        var obstacle = Obstacle();
        obstacle.Item2.Remove("sinkNavigationObstacle");
        var template = Template("wall", obstacle, Structure("building"));
        var ex = Assert.Throws<InvalidOperationException>(() => Extract(template, Instance("wall_1", "wall", Position(100, 200))));
        Assert.That(ex!.Message, Does.Contain("sinkNavigationObstacle"));
    }

    [Test]
    public void Extract_StructureWithoutFootprint_Throws()
    {
        var template = Template("marker", Structure("building"));
        var ex = Assert.Throws<InvalidOperationException>(() => Extract(template, Instance("m_1", "marker", Position(0, 0))));
        Assert.That(ex!.Message, Does.Contain("\"building\""));
    }

    [Test]
    public void Extract_CarriesNamedTemplates()
    {
        var wall = Template("wall", Obstacle(), Structure("building"));
        var path = Template("span", Structure("bridge"));
        var spanOverride = new Dictionary<string, JsonNode>
        {
            ["CrowdSimulationPathSpan"] = new JsonObject
            {
                ["fromXCm"] = 1, ["fromYCm"] = 2, ["toXCm"] = 3, ["toYCm"] = 4, ["widthCm"] = 5,
            },
        };
        var map = new MapConfig
        {
            Id = "t",
            Entities =
            {
                Instance("wall_1", "wall", Position(100, 200)),
                new EntitySpawnData { InstanceId = "span_1", Template = "span", Overrides = spanOverride },
            },
        };
        var surface = CrowdSimulationMapSurfaceSource.Extract(map, new Dictionary<string, EntityTemplate> { ["wall"] = wall, ["span"] = path });

        Assert.That(surface.Blockers, Has.Count.EqualTo(1));
        Assert.That(surface.Blockers[0].TemplateId, Is.EqualTo("building"));
        Assert.That((surface.Blockers[0].XCm, surface.Blockers[0].YCm, surface.Blockers[0].HalfSizeCm), Is.EqualTo((100, 200, 700)));
        Assert.That(surface.LayeredPaths, Has.Count.EqualTo(1));
        Assert.That(surface.LayeredPaths[0].TemplateId, Is.EqualTo("bridge"));
        var span = surface.LayeredPaths[0].Span;
        Assert.That((span.FromXCm, span.FromYCm, span.ToXCm, span.ToYCm, span.WidthCm), Is.EqualTo((1, 2, 3, 4, 5)));
    }

    private static CrowdSimulationMapSurface Extract(EntityTemplate template, EntitySpawnData entity) =>
        CrowdSimulationMapSurfaceSource.Extract(
            new MapConfig { Id = "t", Entities = { entity } },
            new Dictionary<string, EntityTemplate> { [template.Id] = template });

    private static EntityTemplate Template(string id, params (string Name, JsonObject Value)[] components)
    {
        var template = new EntityTemplate { Id = id };
        foreach (var (name, value) in components) template.Components[name] = value;
        return template;
    }

    private static EntitySpawnData Instance(string instanceId, string template, (string Name, JsonObject Value) position) => new()
    {
        InstanceId = instanceId,
        Template = template,
        Overrides = new Dictionary<string, JsonNode> { [position.Name] = position.Value },
    };

    private static (string, JsonObject) Position(int x, int y) =>
        ("WorldPositionCm", new JsonObject { ["Value"] = new JsonObject { ["X"] = x, ["Y"] = y } });

    private static (string, JsonObject) Structure(string template) =>
        ("CrowdSimulationStructure", new JsonObject { ["template"] = template });

    private static (string, JsonObject) Obstacle() =>
        ("ManifestationObstacleIntent2D", new JsonObject
        {
            ["shape"] = "Box",
            ["sinkPhysicsCollider"] = false,
            ["sinkNavigationObstacle"] = true,
            ["halfWidthCm"] = 700,
            ["halfHeightCm"] = 700,
        });

    private static LayeredPathRecord Path(CrowdSimulationRuntimeConfig config, string templateId)
    {
        int cell = config.NavCellSizeCm;
        return new LayeredPathRecord(new CrowdSimulationPathSpan
        {
            FromXCm = cell / 2,
            FromYCm = cell + cell / 2,
            ToXCm = 3 * cell + cell / 2,
            ToYCm = cell + cell / 2,
            WidthCm = cell,
        }, templateId);
    }

    private static SurfaceGrid EmptyGrid(CrowdSimulationRuntimeConfig config)
    {
        int side = config.NavCellsPerSide;
        int n2 = side * side;
        return new SurfaceGrid
        {
            NavCellsPerSide = side,
            NavCellSizeCm = config.NavCellSizeCm,
            TerrainType = new byte[n2],
            Area = new byte[n2],
            Blocked = new byte[n2],
            JumpCandidates = [],
        };
    }

    private static CrowdStructuresStore BuildStore(CrowdSimulationRuntimeConfig config) =>
        CrowdStructuresStore.Build(config, EmptyGrid(config), []);
}
