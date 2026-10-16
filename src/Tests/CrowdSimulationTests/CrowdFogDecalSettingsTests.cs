using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public sealed class CrowdFogDecalSettingsTests
{
    [Test]
    public void StandardPalette_Loads()
    {
        string path = Path.Combine(
            RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationStandardMod", "assets",
            CrowdFogDecalSettings.RelativePath);
        FogDecalPalette palette = CrowdFogDecalSettings.Load(JsonNode.Parse(File.ReadAllText(path))!.AsObject());

        Assert.That(palette.Visible.A, Is.Zero);
        Assert.That(palette.Unseen.A, Is.GreaterThan(palette.Explored.A));
    }

    [TestCase("""{"unseen":{"red":0,"green":0,"blue":0,"alpha":255},"explored":{"red":0,"green":0,"blue":0,"alpha":128},"visible":{"red":0,"green":0,"blue":0,"alpha":0}}""", "denied")]
    [TestCase("""{"unseen":{"red":0,"green":0,"blue":0},"explored":{"red":0,"green":0,"blue":0,"alpha":128},"visible":{"red":0,"green":0,"blue":0,"alpha":0},"denied":{"red":1,"green":1,"blue":1,"alpha":1}}""", "alpha")]
    [TestCase("""{"unseen":{"red":0,"green":0,"blue":0,"alpha":256},"explored":{"red":0,"green":0,"blue":0,"alpha":128},"visible":{"red":0,"green":0,"blue":0,"alpha":0},"denied":{"red":1,"green":1,"blue":1,"alpha":1}}""", "unseen")]
    [TestCase("""{"unseen":{"red":0,"green":0,"blue":0,"alpha":1},"explored":{"red":0,"green":0,"blue":0,"alpha":128},"visible":{"red":0,"green":0,"blue":0,"alpha":0},"denied":{"red":1,"green":1,"blue":1,"alpha":1},"extra":1}""", "extra")]
    public void BadPalette_Throws(string json, string fragment)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CrowdFogDecalSettings.Load(JsonNode.Parse(json)!.AsObject()));
        Assert.That(ex!.Message, Does.Contain(CrowdFogDecalSettings.RelativePath));
        Assert.That(ex.Message, Does.Contain(fragment));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
