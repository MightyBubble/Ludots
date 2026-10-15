using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public class CrowdTraversalConfigTests
{
    [Test]
    public void MergedDemoTraversal_KeepsPropertyOrderAsIndices()
    {
        CrowdTraversalConfig traversal = TestDefaults.DemoTraversal();
        Assert.That(traversal.Areas.Select(area => area.Id), Is.EqualTo(new[]
        {
            "open", "shallow", "rough", "sheer", "deep", "road", "deck", "flooded", "burning",
        }));
        Assert.That(traversal.Areas.Where(area => area.IgnoresSlope).Select(area => area.Id), Is.EqualTo(new[] { "deep", "deck", "flooded" }));
        Assert.That(traversal.LinkKinds.Single().Id, Is.EqualTo("jump"));
        Assert.That(traversal.AgentTypes.Select(row => row.Id), Is.EqualTo(new[] { "foot", "hull", "climber", "amphib", "leaper" }));

        Assert.That(traversal.TryGetRow("hull", out TraversalRow hull), Is.True);
        Assert.That(hull.MaxSlopeDeg, Is.Null);
        Assert.That(hull.Cells[0], Is.Null);
        Assert.That(hull.Cells[4]!.Cost, Is.EqualTo(1d));

        Assert.That(traversal.TryGetRow("leaper", out TraversalRow leaper), Is.True);
        Assert.That(leaper.Links[0], Is.EqualTo(new TraversalLinkAbility(2500, 20000, 7, 2d)));
        Assert.That(leaper.Cells[6]!.Depart[0], Is.False, "deck 是上层区域，不做链接端点");
        Assert.That(leaper.Cells[8]!.Arrive[0], Is.True);
    }

    [TestCase("cells", "road", "agentTypes.foot.cells 缺少 \"road\"")]
    [TestCase("links", "jump", "agentTypes.foot.links 缺少 \"jump\"")]
    public void Row_MissingColumn_IsRejected(string table, string key, string fragment)
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        ((JsonObject)json["agentTypes"]!["foot"]![table]!).Remove(key);
        AssertRejected(json, fragment);
    }

    [Test]
    public void RetiredKeys_AreRejectedWithTheirReplacement()
    {
        JsonObject slope = TestDefaults.DefaultTraversalJson();
        slope["areas"]!["open"]!["slopeFree"] = false;
        AssertRejected(slope, "areas.open.slopeFree 已改名为 ignoresSlope");

        JsonObject passable = TestDefaults.DefaultTraversalJson();
        passable["agentTypes"]!["foot"]!["cells"]!["open"]!["passable"] = true;
        AssertRejected(passable, "agentTypes.foot.cells.open.passable 已删除");

        JsonObject areaId = TestDefaults.DefaultTraversalJson();
        areaId["areas"]!["open"]!["areaId"] = 0;
        AssertRejected(areaId, "areas.open.areaId 已删除");
    }

    [TestCase(0d)]
    [TestCase(90d)]
    public void MaxSlope_OutsideOpenInterval_IsRejected(double deg)
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["agentTypes"]!["foot"]!["maxSlopeDeg"] = deg;
        AssertRejected(json, "agentTypes.foot.maxSlopeDeg");
    }

    [Test]
    public void Cell_NonPositiveCostOrSpeed_IsRejected()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["agentTypes"]!["foot"]!["cells"]!["open"]!["speedDivisor"] = 0;
        AssertRejected(json, "agentTypes.foot.cells.open 的 cost 和 speedDivisor 需为 > 0");
    }

    [Test]
    public void Endpoint_ForLinkTheRowCannotUse_IsRejected()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["agentTypes"]!["foot"]!["cells"]!["open"]!["depart"] = new JsonArray("jump");
        AssertRejected(json, "agentTypes.foot.cells.open.depart[0] = \"jump\"，但这一行的 links.jump 是 false");
    }

    [Test]
    public void Endpoint_UnknownLinkKind_IsRejected()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["agentTypes"]!["leaper"]!["cells"]!["open"]!["arrive"] = new JsonArray("climb");
        AssertRejected(json, "agentTypes.leaper.cells.open.arrive[0] = \"climb\" 不是 linkKinds 里的种类");
    }

    [Test]
    public void LinkAbility_WithoutAnyEndpoint_IsRejected()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        foreach (var (_, cell) in (JsonObject)json["agentTypes"]!["leaper"]!["cells"]!)
        {
            if (cell is JsonObject obj)
            {
                obj["depart"] = new JsonArray();
                obj["arrive"] = new JsonArray();
            }
        }

        AssertRejected(json, "agentTypes.leaper.links.jump 是对象，但这一行没有任何一格在 depart 或 arrive 里写它");
    }

    [Test]
    public void UpperLayerArea_AsTerrainTarget_IsRejectedByTheLoader()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["terrainAreas"]!["land"] = "deck";
        var ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.Assemble(traversal: TestDefaults.ParseTraversal(json)));
        Assert.That(ex!.Message, Does.Contain("terrainAreas.land = \"deck\" 是上层区域"));
    }

    [Test]
    public void UpperLayerArea_AsLinkEndpoint_IsRejectedByTheLoader()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["agentTypes"]!["leaper"]!["cells"]!["deck"]!["depart"] = new JsonArray("jump");
        var ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.Assemble(traversal: TestDefaults.ParseTraversal(json)));
        Assert.That(ex!.Message, Does.Contain("agentTypes.leaper.cells.deck 是上层区域，depart / arrive 必须为空"));
    }

    [Test]
    public void SecondLinkKind_IsRejectedByTheLoader()
    {
        JsonObject json = TestDefaults.DefaultTraversalJson();
        json["linkKinds"]!["climb"] = JsonNode.Parse("""{"traversalSpeedRatio":1}""");
        foreach (var (_, row) in (JsonObject)json["agentTypes"]!)
        {
            ((JsonObject)row!["links"]!)["climb"] = false;
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.Assemble(traversal: TestDefaults.ParseTraversal(json)));
        Assert.That(ex!.Message, Does.Contain("linkKinds 有 2 种，群体内核目前只消费一种"));
    }

    private static void AssertRejected(JsonObject json, string fragment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.ParseTraversal(json));
        Assert.That(ex!.Message, Does.StartWith(CrowdTraversalConfig.RelativePath + ": "));
        Assert.That(ex.Message, Does.Contain(fragment));
    }
}
