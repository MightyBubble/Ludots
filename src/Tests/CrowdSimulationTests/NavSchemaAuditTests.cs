using System.Text.Json.Nodes;
using Arch.Core;
using DotRecast.Detour;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public class NavSchemaAuditTests
{
    [Test]
    public void PassableFalse_IsExcludedFromCrowdCostAndDetourFilter()
    {
        var runtime = TestDefaults.Assemble();
        var foot = runtime.AgentTypes.Single(agent => agent.Id == "foot");
        Assert.That(foot.CostByArea[2], Is.EqualTo(Fix64.Zero));
        Assert.That(foot.CostByArea[3], Is.EqualTo(Fix64.Zero));
        Assert.That(foot.CostByArea[4], Is.EqualTo(Fix64.Zero));
        Assert.That(foot.SpeedDivisorByArea[4], Is.EqualTo(Fix64.Zero));
        Assert.That(foot.CostByArea[0], Is.EqualTo(Fix64.OneValue));

        PathingAgentTypeConfig pathingFoot = TestDefaults.DemoPathing().AgentTypes.Single(agent => agent.Id == "foot");
        NavAreaCostTable table = PathingAreaCostCompiler.Compile(pathingFoot.NavMesh);
        Assert.That(table.IsPassable(4), Is.False);
        Assert.That(table.Get(4) > Fix64.Zero, Is.True);
        Assert.That(table.IsPassable(0), Is.True);
        Assert.That(table.Get(9), Is.EqualTo(Fix64.OneValue));
        Assert.That(table.IsPassable(9), Is.True);

        IDtQueryFilter filter = DetourNavQueryEngine.BuildDefaultFilter(table);
        Assert.That(filter, Is.InstanceOf<NavAreaQueryFilter>());
        var blocked = new DtPoly(0, 3) { flags = 1 };
        blocked.SetArea(4);
        var open = new DtPoly(1, 3) { flags = 1 };
        open.SetArea(0);
        Assert.That(filter.PassFilter(0, null!, blocked), Is.False);
        Assert.That(filter.PassFilter(0, null!, open), Is.True);
    }

    [Test]
    public void UnlistedCrowdArea_StaysCostOne()
    {
        JsonObject navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["areas"]!["lava"] = JsonNode.Parse("""{"areaId":9,"slopeFree":false}""");
        var runtime = TestDefaults.Assemble(navmesh: TestDefaults.ParseNavmesh(navmesh));
        var foot = runtime.AgentTypes.Single(agent => agent.Id == "foot");
        Assert.That(foot.CostByArea[9], Is.EqualTo(Fix64.OneValue));
        Assert.That(foot.SpeedDivisorByArea[9], Is.EqualTo(Fix64.OneValue));
    }

    [Test]
    public void AutoPath_ThrowsWhenSelectionIsMissing()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CreateAutoPath(selection: null))!;
        Assert.That(ex.Message, Does.Contain("selection"));
    }

    [Test]
    public void ResolveAgent_ThrowsOnBlankOrUnknownId()
    {
        AutoPathService service = CreateAutoPath(new PathingSelectionConfig { Mode = PathSelectionMode.Direct });
        InvalidOperationException blank = Assert.Throws<InvalidOperationException>(() =>
            service.TrySolve(Request(""), out _))!;
        Assert.That(blank.Message, Does.Contain("agent type id"));
        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() =>
            service.TrySolve(Request("no-such-agent"), out _))!;
        Assert.That(unknown.Message, Does.Contain("no-such-agent"));
    }

    [Test]
    public void UnknownNavProfileId_FailsLoad()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.Parse(MinimalNavmesh(), Bodies("Missing")))!;
        Assert.That(ex.Message, Does.Contain("navProfileId"));
        Assert.That(ex.Message, Does.Contain("Missing"));
    }

    [Test]
    public void Template_MissingEachRequiredField_Fails()
    {
        string[] fields = { "profileId", "agentTypeId", "speedCmPerSecond", "personalRadiusCm", "pushPriority", "bulkDeploy" };
        foreach (string field in fields)
        {
            JsonObject template = FullTemplate();
            template.Remove(field);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CrowdUnitTemplateFacts.Parse("unit", template))!;
            Assert.That(ex.Message, Does.Contain(field), field);
        }
    }

    [Test]
    public void ArrayFormCatalogs_AreRejected()
    {
        JsonObject profiles = MinimalNavmesh();
        profiles["profiles"] = JsonNode.Parse("""[{"maxClimbCm":40,"maxSlopeDeg":45}]""");
        AssertNamesObject(Assert.Throws<InvalidOperationException>(() => NavMeshBakeConfigLoader.Parse(profiles, Bodies("Small")))!);

        JsonObject areas = MinimalNavmesh();
        areas["areas"] = JsonNode.Parse("""[{"areaId":0,"slopeFree":false}]""");
        AssertNamesObject(Assert.Throws<InvalidOperationException>(() => NavMeshBakeConfigLoader.Parse(areas, Bodies("Small")))!);

        JsonObject agentTypes = JsonNode.Parse("""
            {
              "agentTypes": [
                { "profileId": "Small", "navMesh": { "areaCosts": [] } }
              ]
            }
            """)!.AsObject();
        AssertNamesObject(Assert.Throws<InvalidOperationException>(() => PathingConfigLoader.Parse(agentTypes))!);
    }

    [Test]
    public void MaxSlopeDegZero_IsRejected()
    {
        JsonObject navmesh = MinimalNavmesh();
        navmesh["profiles"]!["Small"]!["maxSlopeDeg"] = JsonNode.Parse("0");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.Parse(navmesh, Bodies("Small")))!;
        Assert.That(ex.Message, Does.Contain("maxSlopeDeg"));
        Assert.That(ex.Message, Does.Contain("0 或负数无效"));
    }

    [Test]
    public void MapWithoutAgentTypes_IsRejected()
    {
        JsonObject navmesh = MinimalNavmesh();
        navmesh["maps"] = JsonNode.Parse("""
            {
              "demo": {
                "boards": { "default": { "tileWorldWidthCm": 100, "tileWorldHeightCm": 100 } }
              }
            }
            """);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.Parse(navmesh, Bodies("Small")))!;
        Assert.That(ex.Message, Does.Contain("maps.demo"));
        Assert.That(ex.Message, Does.Contain("agentTypes"));
    }

    [Test]
    public void ComponentLoader_RetiredKeys_NameTheReplacement()
    {
        using World world = World.Create();
        Entity entity = world.Create();
        InvalidOperationException agentType = Assert.Throws<InvalidOperationException>(() =>
            Ludots.Core.Config.ComponentRegistry.Apply(entity, "CrowdSimulationAgent", JsonNode.Parse("""{ "agentType": "foot" }""")!))!;
        Assert.That(agentType.Message, Does.Contain("agentTypeId"));

        InvalidOperationException radiusClass = Assert.Throws<InvalidOperationException>(() =>
            Ludots.Core.Config.ComponentRegistry.Apply(entity, "CrowdSimulationAgent", JsonNode.Parse("""{ "radiusClassCm": 100 }""")!))!;
        Assert.That(radiusClass.Message, Does.Contain("profileId"));
        Assert.That(radiusClass.Message, Does.Contain("personalRadiusCm"));

        InvalidOperationException radius = Assert.Throws<InvalidOperationException>(() =>
            Ludots.Core.Config.ComponentRegistry.Apply(entity, "CrowdSimulationAgent", JsonNode.Parse("""{ "radiusCm": 100 }""")!))!;
        Assert.That(radius.Message, Does.Contain("profileId"));
        Assert.That(radius.Message, Does.Contain("personalRadiusCm"));
    }

    [Test]
    public void AgentProfile_TemplateId_NamesTheReplacement()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-agent-profile-templateid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "Navigation"));
        File.WriteAllText(Path.Combine(tempRoot, "config_catalog.json"),
            """[{ "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" }]""");
        File.WriteAllText(Path.Combine(tempRoot, "Navigation", "agent_profiles.json"),
            """
            [
              {
                "id": "Small",
                "radiusCm": 30,
                "heightCm": 180,
                "clearanceCm": 40,
                "draftCm": 0,
                "beamCm": 0,
                "mass": 1,
                "layer": 0,
                "navProfileId": "Small",
                "templateId": "foot_r100"
              }
            ]
            """);
        try
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", tempRoot);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new AgentProfileConfigLoader(pipeline).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("templateId"));
            Assert.That(ex.Message, Does.Contain("CrowdSimulationAgent.speedCmPerSecond"));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void AssertNamesObject(InvalidOperationException ex)
        => Assert.That(ex.Message, Does.Contain("以 id 为键的对象，不再使用数组。"));

    private static JsonObject FullTemplate()
        => JsonNode.Parse("""
            {
              "profileId": "foot_r100",
              "agentTypeId": "foot",
              "speedCmPerSecond": 1400,
              "personalRadiusCm": 600,
              "pushPriority": 2,
              "bulkDeploy": true
            }
            """)!.AsObject();

    private static AgentProfileRegistry Bodies(string navProfileId)
        => new(new[]
        {
            new AgentProfileConfig
            {
                Id = "Small",
                NavProfileId = navProfileId,
                RadiusCm = 30,
                HeightCm = 180,
                ClearanceCm = 40,
                DraftCm = 0,
                BeamCm = 0,
                Mass = 1,
                Layer = 0,
            }
        });

    private static JsonObject MinimalNavmesh()
        => JsonNode.Parse("""
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": {
                "Small": { "maxClimbCm": 40, "maxSlopeDeg": 45, "agentProfileId": "Small" }
              },
              "layers": [ { "id": "Ground", "layer": 0 } ],
              "areas": { "Default": { "areaId": 0, "slopeFree": false } },
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 1,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1
              }
            }
            """)!.AsObject();

    private static AutoPathService CreateAutoPath(PathingSelectionConfig? selection)
    {
        var builder = new NodeGraphBuilder(2, 1);
        builder.AddNode(0, 0);
        builder.AddNode(100, 0);
        builder.AddEdge(0, 1, 100f);
        return new AutoPathService(
            builder.Build(),
            Bodies("Small"),
            new PathStore(maxPaths: 4, maxPointsPerPath: 8),
            new PathingConfig
            {
                AgentTypes = new List<PathingAgentTypeConfig>
                {
                    new()
                    {
                        Id = "foot",
                        ProfileId = "Small",
                        Selection = selection!,
                        NavMesh = new PathingNavMeshConfig(),
                    }
                }
            });
    }

    private static PathRequest Request(string agentTypeId)
        => new(1, default, PathDomain.Auto, agentTypeId, PathEndpoint.FromNodeId(0), PathEndpoint.FromNodeId(1), new PathBudget(0, 8));
}
