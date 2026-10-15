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

        PathingAgentTypeConfig pathingFoot = PathingConfigLoader.Parse(JsonNode.Parse("""
            {
              "agentTypes": {
                "foot": {
                  "profileId": "Small",
                  "navMesh": { "areaCosts": [
                    { "areaId": 0, "cost": 1, "passable": true },
                    { "areaId": 4, "cost": 1, "passable": false }
                  ] }
                }
              }
            }
            """)!.AsObject()).AgentTypes.Single(agent => agent.Id == "foot");
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
    public void RequireComplete_NullAreas_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            PathingAreaCosts.RequireComplete(new PathingAgentTypeConfig { Id = "foot" }, null))!;
        Assert.That(ex.Message, Does.Contain("null"));
    }

    [Test]
    public void MergedHumanoid_SelectionEqualsCore_InTotalWar()
    {
        string root = RepoRoot();
        JsonObject core = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(root, "assets", "Navigation", "pathing.json")))!;
        JsonObject totalWar = (JsonObject)core.DeepClone();
        ConfigPipeline.DeepMerge(totalWar, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(
            root, "mods/showcases/total_war_flow/TotalWarFlowShowcaseMod/assets/Navigation/pathing.json")))!);

        PathingSelectionConfig coreSelection = PathingConfigLoader.Parse(core).AgentTypes.Single(agent => agent.Id == "Humanoid").Selection;
        AssertSelectionEquals(coreSelection, PathingConfigLoader.Parse(totalWar).AgentTypes.Single(agent => agent.Id == "Humanoid").Selection);
        Assert.That(File.Exists(Path.Combine(root, "mods/capabilities/navigation/CrowdSimulationStandardMod/assets/Navigation/pathing.json")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(root, "mods/showcases/total_war_flow/TotalWarFlowShowcaseMod/assets/Navigation/pathing.json")), Does.Not.Contain("Humanoid"));
    }

    [Test]
    public void MeshQueries_OnlyMapDeclaredProfiles_UnboundRequestThrowsWithIds()
    {
        AgentProfileRegistry bodies = new(new[]
        {
            Body("Small", "Small"),
            Body("Medium", "Medium"),
        });
        NavMeshBakeConfig bake = NavMeshBakeConfigLoader.Parse(JsonNode.Parse("""
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": {
                "Small": { "maxClimbCm": 40, "maxSlopeDeg": 45, "agentProfileId": "Small" },
                "Medium": { "maxClimbCm": 45, "maxSlopeDeg": 45, "agentProfileId": "Medium" }
              },
              "layers": [ { "id": "Ground", "layer": 0 } ],
              "areas": { "Default": { "areaId": 0, "slopeFree": false } },
              "maps": { "demo": { "solver": "navmesh", "navProfiles": ["Small"], "navAreas": ["Default"] } },
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 1,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1
              }
            }
            """)!.AsObject(), bodies);
        var navProfiles = new NavMeshProfileRegistry(bake, bodies);
        var store = new NavTileStore(_ => throw new InvalidOperationException("unbound query must not open a tile"));
        var registry = new NavQueryServiceRegistry(new Dictionary<NavQueryServiceKey, NavTileStore>
        {
            [new NavQueryServiceKey(layer: 0, profile: 0)] = store,
        }, tileWidthCm: 100, tileHeightCm: 100);
        PathingConfig pathing = PathingConfigLoader.Parse(JsonNode.Parse("""
            {
              "agentTypes": {
                "Humanoid": {
                  "profileId": "Small",
                  "selection": { "mode": "PreferMesh", "graphBias": 0, "meshBias": 0, "graphCostWeight": 1, "meshCostWeight": 1 },
                  "navMesh": { "areaCosts": [ { "areaId": 0, "cost": 1, "passable": true } ] }
                },
                "Ship": {
                  "profileId": "Medium",
                  "selection": { "mode": "PreferMesh", "graphBias": 0, "meshBias": 0, "graphCostWeight": 1, "meshCostWeight": 1 },
                  "navMesh": { "areaCosts": [ { "areaId": 0, "cost": 1, "passable": true } ] }
                }
              }
            }
            """)!.AsObject());
        var scope = new NavMapQueryScope("demo", new[] { "Small" });
        var adapter = new NavMeshPathServiceAdapter(registry, navProfiles, bodies, pathing, new PathStore(4, 8), scope);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            adapter.TrySolve(new PathRequest(1, default, PathDomain.NavMesh, "Ship", PathEndpoint.FromWorldCm(0, 0), PathEndpoint.FromWorldCm(10, 0), new PathBudget(8, 8)), out _))!;
        Assert.That(ex.Message, Does.Contain("Ship"));
        Assert.That(ex.Message, Does.Contain("Medium"));
        Assert.That(ex.Message, Does.Contain("demo"));

        var auto = new AutoPathService(registry, navProfiles, bodies, new PathStore(4, 8), pathing, meshScope: scope);
        InvalidOperationException autoEx = Assert.Throws<InvalidOperationException>(() =>
            auto.TrySolve(new PathRequest(2, default, PathDomain.Auto, "Ship", PathEndpoint.FromWorldCm(0, 0), PathEndpoint.FromWorldCm(10, 0), new PathBudget(8, 8)), out _))!;
        Assert.That(autoEx.Message, Does.Contain("Ship"));
        Assert.That(autoEx.Message, Does.Contain("Medium"));
        Assert.That(autoEx.Message, Does.Contain("demo"));
    }

    [Test]
    public void UnknownMassNavigationAgentType_FailsAtBinding()
    {
        NavMeshBakeConfig bake = NavMeshBakeConfigLoader.Parse(JsonNode.Parse("""
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": {
                "Small": { "maxClimbCm": 40, "maxSlopeDeg": 45, "agentProfileId": "Small" }
              },
              "layers": [ { "id": "Ground", "layer": 0 } ],
              "areas": { "Default": { "areaId": 0, "slopeFree": false } },
              "maps": { "demo": { "solver": "navmesh", "navProfiles": ["Small"], "navAreas": ["Default"] } },
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 1,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1
              }
            }
            """)!.AsObject(), Bodies("Small"));
        PathingConfig pathing = PathingConfigLoader.Parse(JsonNode.Parse("""
            {
              "agentTypes": {
                "Humanoid": {
                  "profileId": "Small",
                  "selection": { "mode": "PreferMesh", "graphBias": 0, "meshBias": 0, "graphCostWeight": 1, "meshCostWeight": 1 },
                  "navMesh": { "areaCosts": [ { "areaId": 0, "cost": 1, "passable": true } ] }
                }
              }
            }
            """)!.AsObject());
        NavMapAgentBinding binding = NavMapAgentBinding.Create("demo", navTagged: false, bake, pathing, Bodies("Small"));
        var template = new EntityTemplate
        {
            Id = "mass_unit",
            Components = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
            {
                ["MassNavigationAgent"] = JsonNode.Parse("""{ "profileId": "Small", "agentTypeId": "Ghost" }""")!,
            },
        };
        NavSessionBinding.Set(binding);
        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                NavSessionBinding.RequireMassNavigationTemplate(template))!;
            Assert.That(ex.Message, Does.Contain("mass_unit"));
            Assert.That(ex.Message, Does.Contain("Ghost"));
            Assert.That(ex.Message, Does.Contain("demo"));
        }
        finally
        {
            NavSessionBinding.Clear();
        }
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
        NavMeshBakeConfig config = NavMeshBakeConfigLoader.Parse(MinimalNavmesh(), Bodies("Missing"));
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.ValidateReferencedAgentNavProfiles(config, Bodies("Missing"), new[] { "Small" }))!;
        Assert.That(ex.Message, Does.Contain("navProfileId"));
        Assert.That(ex.Message, Does.Contain("Missing"));
    }

    [Test]
    public void Template_MissingEachRequiredField_Fails()
    {
        string[] fields = { "profileId", "agentTypeId", "speedCmPerSecond", "personalRadiusCm", "pushPriority", "special" };
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
    public void MapAgentTypes_NamesNavProfiles()
    {
        JsonObject navmesh = MinimalNavmesh();
        navmesh["maps"] = JsonNode.Parse("""
            {
              "demo": {
                "agentTypes": ["Humanoid"]
              }
            }
            """);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.Parse(navmesh, Bodies("Small")))!;
        Assert.That(ex.Message, Does.Contain("maps.demo.agentTypes"));
        Assert.That(ex.Message, Does.Contain("navProfiles"));
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
            ConfigLoadException ex = Assert.Throws<ConfigLoadException>(() =>
                new AgentProfileConfigLoader(pipeline).Load(catalog))!;
            Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
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
              "special": false
            }
            """)!.AsObject();

    private static void AssertSelectionEquals(PathingSelectionConfig expected, PathingSelectionConfig actual)
    {
        Assert.That(actual.Mode, Is.EqualTo(expected.Mode));
        Assert.That(actual.GraphBias, Is.EqualTo(expected.GraphBias));
        Assert.That(actual.MeshBias, Is.EqualTo(expected.MeshBias));
        Assert.That(actual.GraphCostWeight, Is.EqualTo(expected.GraphCostWeight));
        Assert.That(actual.MeshCostWeight, Is.EqualTo(expected.MeshCostWeight));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "assets", "Navigation", "pathing.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }

    private static AgentProfileConfig Body(string id, string navProfileId)
        => new()
        {
            Id = id,
            NavProfileId = navProfileId,
            RadiusCm = 30,
            HeightCm = 180,
            ClearanceCm = 40,
            DraftCm = 0,
            BeamCm = 0,
            Mass = 1,
            Layer = 0,
        };

    private static AgentProfileRegistry Bodies(string navProfileId)
        => new(new[] { Body("Small", navProfileId) });

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
