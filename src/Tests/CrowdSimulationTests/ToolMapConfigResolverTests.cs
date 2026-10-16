using System.Text.Json.Nodes;
using Ludots.Tool;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public class ToolMapConfigResolverTests
{
    [Test]
    public void UnknownRootOrBoardKey_IsRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "ludots-map-keys-" + Guid.NewGuid().ToString("N"));
        string mod = Path.Combine(root, "mods", "ProbeMod");
        Directory.CreateDirectory(Path.Combine(mod, "assets", "Maps"));
        File.WriteAllText(
            Path.Combine(mod, "mod.json"),
            """{"name":"ProbeMod","version":"1.0.0","priority":0}""");
        string mapPath = Path.Combine(mod, "assets", "Maps", "probe.json");
        try
        {
            File.WriteAllText(mapPath, """
                {"Id":"probe","Boards":[{"Name":"default","WidthCm":100,"HeightCm":100}]}
                """);
            Assert.That(ToolMapConfigResolver.LoadMap(root, "probe", "ProbeMod").Id, Is.EqualTo("probe"));

            JsonObject map = JsonNode.Parse(File.ReadAllText(mapPath))!.AsObject();
            map["WidthInTiles"] = 4;
            File.WriteAllText(mapPath, map.ToJsonString());
            InvalidOperationException rootKey = Assert.Throws<InvalidOperationException>(
                () => ToolMapConfigResolver.LoadMap(root, "probe", "ProbeMod"))!;
            Assert.That(rootKey.Message, Does.Contain("unknown property 'WidthInTiles'"));

            map.Remove("WidthInTiles");
            map["Boards"]![0]!["HeightInPages"] = 2;
            File.WriteAllText(mapPath, map.ToJsonString());
            InvalidOperationException boardKey = Assert.Throws<InvalidOperationException>(
                () => ToolMapConfigResolver.LoadMap(root, "probe", "ProbeMod"))!;
            Assert.That(boardKey.Message, Does.Contain("unknown property 'HeightInPages'"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
