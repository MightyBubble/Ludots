using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

[TestFixture]
public class CrowdDebugViewConfigTests
{
    [Test]
    public void ShippedFile_Loads()
    {
        string path = Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "CrowdSimulationDebugView.json");
        var node = CrowdPresentationPaletteTestMerge.MergedDebugView(
            JsonNode.Parse(File.ReadAllText(path))!.AsObject());
        var cfg = CrowdSimulationDebugViewConfig.Load(node, path);
        Assert.That(cfg.ArrowMinScreenPx, Is.EqualTo(10));
        Assert.That(cfg.Overlays.Funnel, Is.False);
        Assert.That(cfg.UnitColorMode, Is.EqualTo(CrowdSimulationDebugViewConfig.ColorOff));
        Assert.That(cfg.Layers.Flow, Is.Not.EqualTo(cfg.Layers.Paper));
    }

    [Test]
    public void MissingField_NamesPathAndField()
    {
        var node = Shipped();
        node.Remove("arrowMinScreenPx");
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugViewConfig.Load(node, "CrowdSimulationDebugView.json"));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationDebugView.json"));
        Assert.That(ex.Message, Does.Contain("arrowMinScreenPx"));
    }

    [Test]
    public void UnknownKey_NamesPathAndField()
    {
        var node = Shipped();
        node["extra"] = 1;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugViewConfig.Load(node, "CrowdSimulationDebugView.json"));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationDebugView.json"));
        Assert.That(ex.Message, Does.Contain("extra"));
    }

    [Test]
    public void OutOfRange_NamesPathAndField()
    {
        var node = Shipped();
        node["arrowMinScreenPx"] = 0;
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdSimulationDebugViewConfig.Load(node, "CrowdSimulationDebugView.json"));
        Assert.That(ex!.Message, Does.Contain("CrowdSimulationDebugView.json"));
        Assert.That(ex.Message, Does.Contain("arrowMinScreenPx"));
    }

    private static JsonObject Shipped()
    {
        string path = Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "CrowdSimulationDebugView.json");
        return CrowdPresentationPaletteTestMerge.MergedDebugView(
            JsonNode.Parse(File.ReadAllText(path))!.AsObject());
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
