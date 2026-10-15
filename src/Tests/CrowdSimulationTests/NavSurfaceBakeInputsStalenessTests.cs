using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using CrowdSimulationTests.Parity;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 入库 .navsurface 走进图装配（<see cref="CrowdSimulationWorldIngest.Build"/>）：任何一项烘焙输入改了而资产没重烘，进图报“重烘”；什么都不改就能进图。
/// </summary>
[TestFixture]
public class NavSurfaceBakeInputsStalenessTests
{
    private const string Seed = "s1337";
    private const string StaleMessage = "烘焙输入和当前配置不一致，重烘";

    [Test]
    public void UnchangedInputs_Activate()
    {
        Assert.DoesNotThrow(() => Activate());
    }

    [Test]
    public void LayeredPathEndpointChanged_FailsActivation()
    {
        AssertStale(tuneMap: map => FirstBridgeSpan(map)["fromXCm"] = FirstBridgeSpan(map)["fromXCm"]!.GetValue<int>() - 6250);
    }

    [Test]
    public void LayeredPathWidthChanged_FailsActivation()
    {
        AssertStale(tuneMap: map => FirstBridgeSpan(map)["widthCm"] = FirstBridgeSpan(map)["widthCm"]!.GetValue<int>() + 6250);
    }

    [Test]
    public void LayeredPathRemoved_FailsActivation()
    {
        AssertStale(tuneMap: map =>
        {
            var entities = (JsonArray)map["Entities"]!;
            entities.Remove(entities.First(e => e!["Overrides"]?["CrowdSimulationPathSpan"] != null));
        });
    }

    [Test]
    public void SurfaceTemplatePortalReachChanged_FailsActivation()
    {
        AssertStale(tuneConfig: config =>
        {
            JsonNode effect = config["structures"]!["templates"]!["bridge"]!["effect"]!;
            effect["portalReachNavCells"] = effect["portalReachNavCells"]!.GetValue<int>() + 1;
        });
    }

    [TestCase("maxGapCm", 6250)]
    [TestCase("linkSpacingCm", 6250)]
    public void LinkGeometryParamChanged_FailsActivation(string field, int deltaCm)
    {
        AssertStale(tuneNavmesh: navmesh =>
        {
            JsonNode jump = navmesh["maps"]!["crowd_simulation_" + Seed]!["linkCandidateBake"]!["jump"]!;
            jump[field] = jump[field]!.GetValue<int>() + deltaCm;
        });
    }

    [Test]
    public void LinkEndpointSlopeChanged_FailsActivation()
    {
        AssertStale(tuneNavmesh: navmesh =>
            navmesh["maps"]!["crowd_simulation_" + Seed]!["linkCandidateBake"]!["jump"]!["endpointMaxSlopeDeg"] = 23.0);
    }

    [Test]
    public void MatrixDepartArriveChanged_FailsActivation()
    {
        AssertStale(tuneTraversal: traversal =>
        {
            JsonNode rough = traversal["agentTypes"]!["leaper"]!["cells"]!["rough"]!;
            rough["depart"] = new JsonArray();
            rough["arrive"] = new JsonArray();
        });
    }

    [Test]
    public void HeightChanged_FailsActivation()
    {
        AssertStale(tuneHeight: height => height.HeightSamplesRaw[0] = (ushort)(height.HeightSamplesRaw[0] + 1000));
    }

    private static void AssertStale(
        Action<JsonObject>? tuneConfig = null,
        Action<JsonObject>? tuneNavmesh = null,
        Action<JsonObject>? tuneTraversal = null,
        Action<JsonObject>? tuneMap = null,
        Action<ContinuousHeightmapAsset>? tuneHeight = null)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Activate(tuneConfig, tuneNavmesh, tuneTraversal, tuneMap, tuneHeight));
        Assert.That(ex!.Message, Does.Contain(StaleMessage));
        Assert.That(ex.Message, Does.Contain("crowd_simulation_" + Seed + ".navsurface"));
    }

    private static void Activate(
        Action<JsonObject>? tuneConfig = null,
        Action<JsonObject>? tuneNavmesh = null,
        Action<JsonObject>? tuneTraversal = null,
        Action<JsonObject>? tuneMap = null,
        Action<ContinuousHeightmapAsset>? tuneHeight = null)
    {
        JsonObject traversal = TestDefaults.DefaultTraversalJson();
        tuneTraversal?.Invoke(traversal);
        var bodies = new CrowdTestBodies(TestDefaults.DemoProfiles(), TestDefaults.DemoTemplates(), TestDefaults.ParseTraversal(traversal));
        var runtime = S1SurfaceTruthTests.LoadRuntime(Seed, lineOfSight: false, pairRules: null, tuneConfig, bodies, tuneNavmesh);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(Seed, tuneMap);
        var map = JsonNode.Parse(File.ReadAllText(Path.Combine(S1SurfaceTruthTests.SeedDir(Seed), "Maps", $"crowd_simulation_{Seed}.json")))!;
        string heightAssetPath = map["ContinuousHeightmap"]!["Asset"]!.GetValue<string>();
        ContinuousHeightmapAsset height;
        using (FileStream input = File.OpenRead(Path.Combine(S1SurfaceTruthTests.SeedDir(Seed), heightAssetPath["assets/".Length..])))
        {
            height = ContinuousHeightmapBinary.Read(input);
        }

        tuneHeight?.Invoke(height);
        CrowdSimulationWorldIngest.Build(
            runtime,
            map["Boards"]![0]!["WidthCm"]!.GetValue<int>(),
            map["Boards"]![0]!["HeightCm"]!.GetValue<int>(),
            S1SurfaceTruthTests.ReadSurface(Seed),
            "assets/" + runtime.SurfaceAsset,
            height,
            heightAssetPath,
            mapSurface.Blockers,
            mapSurface.LayeredPaths);
    }

    private static JsonObject FirstBridgeSpan(JsonObject map)
        => (JsonObject)((JsonArray)map["Entities"]!)
            .First(e => e!["Overrides"]?["CrowdSimulationPathSpan"] != null)!["Overrides"]!["CrowdSimulationPathSpan"]!;
}
