using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using NUnit.Framework;

namespace CrowdSimulationTests;

public class CrowdTerrainTypeColorsTests
{
    [Test]
    public void MissingTerrainId_NamesIt()
    {
        CrowdTerrainTypeColorTable table = CrowdTerrainTypeColors.Load(Parse("""
            { "colors": { "water": { "red": 1, "green": 2, "blue": 3, "alpha": 4 } } }
            """));

        var ex = Assert.Throws<InvalidOperationException>(() => table.RequireExact(["water", "land"], "crowd_simulation_s1337"));

        Assert.That(ex!.Message, Does.Contain("land"));
        Assert.That(ex.Message, Does.Contain("crowd_simulation_s1337"));
        Assert.That(ex.Message, Does.Contain("没有颜色"));
    }

    [Test]
    public void ExtraTerrainId_NamesIt()
    {
        CrowdTerrainTypeColorTable table = CrowdTerrainTypeColors.Load(Parse("""
            {
              "colors": {
                "water": { "red": 1, "green": 2, "blue": 3, "alpha": 4 },
                "lava": { "red": 9, "green": 0, "blue": 0, "alpha": 255 }
              }
            }
            """));

        var ex = Assert.Throws<InvalidOperationException>(() => table.RequireExact(["water"], "crowd_simulation_s1337"));

        Assert.That(ex!.Message, Does.Contain("lava"));
        Assert.That(ex.Message, Does.Contain("terrainAreas"));
    }

    [Test]
    public void UnknownProperty_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdTerrainTypeColors.Load(Parse("""
            {
              "colors": { "water": { "red": 1, "green": 2, "blue": 3, "alpha": 4 } },
              "palette": true
            }
            """)));

        Assert.That(ex!.Message, Does.Contain("palette").IgnoreCase);
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;
}
