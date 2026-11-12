using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;

namespace CrowdSimulationTests.Presentation;

internal static class CrowdPresentationPaletteTestMerge
{
    private static readonly string UiAssets = Path.Combine(
        ModConfigChain.RepoRoot(),
        "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets");

    public static JsonObject Palette()
    {
        string path = Path.Combine(UiAssets, CrowdPresentationPalette.RelativePath);
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    public static JsonObject MergedDebugView(JsonObject view)
        => CrowdPresentationPalette.MergeInto(
            (JsonObject)view.DeepClone(), Palette(), "CrowdSimulationDebugView.json");

    public static JsonObject MergedPlayerView(JsonObject view)
        => CrowdPresentationPalette.MergeInto(
            (JsonObject)view.DeepClone(), Palette(), "CrowdSimulationPlayerView.json");
}
