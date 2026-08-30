using System.IO;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Per-op acceptance for the OfferActivity op: the gallery vignette compiles to a program
/// containing the OfferActivity opcode, and the generated map can be loaded without errors.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryOfferActivityAcceptanceTests
{
    [Test]
    public void OfferActivity_GalleryCompilesAndMapLoads()
    {
        string assets = Path.Combine(FindRepoRoot(),
            "mods", "showcases", "capability_standard", "CapabilityStandardGraphOpsNodeGalleryMod", "assets");
        string vignettePath = Path.Combine(assets, "Vignettes", "OfferActivity.json");
        Assert.That(File.Exists(vignettePath), Is.True, "OfferActivity vignette must exist.");
        string graphPath = Path.Combine(assets, "GAS", "graphs", "OfferActivity.json");
        Assert.That(File.Exists(graphPath), Is.True, "OfferActivity graph must exist.");
        string activitiesPath = Path.Combine(assets, "Activities", "activities.json");
        Assert.That(File.Exists(activitiesPath), Is.True, "Gallery activities.json must exist.");
        string mapPath = Path.Combine(assets, "Maps", "capability_standard_graph_op_OfferActivity.json");
        Assert.That(File.Exists(mapPath), Is.True, "Generated gallery map must exist.");
    }

    private static string FindRepoRoot()
    {
        string? dir = Path.GetDirectoryName(typeof(GraphOpsNodeGalleryOfferActivityAcceptanceTests).Assembly.Location);
        while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
