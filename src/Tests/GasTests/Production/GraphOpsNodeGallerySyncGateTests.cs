using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Epic #990 Stage 0 sync gate: every vignette's title / beat / detailTemplate must stay
/// in lockstep with the generated wiki page and showcase.registry.json summary. Wiki pages
/// are generated artifacts (scripts/generate-graph-op-node-wiki.py); drifting them silently
/// breaks the "evidence equals current fact" gallery contract.
/// </summary>
[TestFixture]
[Category("ci-gate")]
public sealed class GraphOpsNodeGallerySyncGateTests
{
    private const string GalleryRelative =
        "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets";

    [Test]
    public void WikiPages_QuoteCurrentVignetteCopy()
    {
        string repo = FindRepoRoot();
        string vignetteDir = Path.Combine(repo, GalleryRelative, "Vignettes");
        string wikiDir = Path.Combine(repo, "gitbook", "reference", "graph-node-op-wiki");
        var failures = new List<string>();

        foreach (string file in Directory.GetFiles(vignetteDir, "*.json").OrderBy(f => f))
        {
            string op = Path.GetFileNameWithoutExtension(file);
            if (op.StartsWith('_'))
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            string title = root.GetProperty("title").GetString()!;
            string beat = root.GetProperty("beat").GetString()!;
            string detail = root.TryGetProperty("detailTemplate", out JsonElement detailEl)
                ? detailEl.GetString()!
                : beat;

            string wikiPath = Path.Combine(wikiDir, op + ".md");
            if (!File.Exists(wikiPath))
            {
                failures.Add($"{op}: wiki page missing: {wikiPath}");
                continue;
            }

            string wiki = File.ReadAllText(wikiPath);
            if (!wiki.Contains($"# {title}", System.StringComparison.Ordinal))
            {
                failures.Add($"{op}: wiki H1 does not match vignette title");
            }

            if (!wiki.Contains(beat, System.StringComparison.Ordinal))
            {
                failures.Add($"{op}: wiki does not quote vignette beat");
            }

            if (!wiki.Contains($"> {detail}", System.StringComparison.Ordinal))
            {
                failures.Add($"{op}: wiki §3 does not quote current detailTemplate (rerun generate-graph-op-node-wiki.py)");
            }
        }

        Assert.That(failures, Is.Empty, "Wiki/vignette copy drift:\n" + string.Join("\n", failures));
    }

    [Test]
    public void RegistryEntries_MatchVignetteCopy()
    {
        string repo = FindRepoRoot();
        string vignetteDir = Path.Combine(repo, GalleryRelative, "Vignettes");
        using JsonDocument registry = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repo, "showcase.registry.json"))
        );
        var entries = new Dictionary<string, JsonElement>(System.StringComparer.Ordinal);
        foreach (JsonElement entry in registry.RootElement.GetProperty("showcases").EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            if (id.StartsWith("capability_standard_graph_op_", System.StringComparison.Ordinal))
            {
                entries[id] = entry;
            }
        }

        var failures = new List<string>();
        foreach (string file in Directory.GetFiles(vignetteDir, "*.json").OrderBy(f => f))
        {
            string op = Path.GetFileNameWithoutExtension(file);
            if (op.StartsWith('_'))
            {
                continue;
            }

            string sid = "capability_standard_graph_op_" + op;
            if (!entries.TryGetValue(sid, out JsonElement entry))
            {
                failures.Add($"{op}: no registry entry {sid}");
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            string title = root.GetProperty("title").GetString()!;
            string beat = root.GetProperty("beat").GetString()!;
            if (entry.GetProperty("title").GetString() != title)
            {
                failures.Add($"{op}: registry title != vignette title (rerun generate-graph-op-node-galleries.py)");
            }

            if (entry.GetProperty("summary").GetString() != beat)
            {
                failures.Add($"{op}: registry summary != vignette beat (rerun generate-graph-op-node-galleries.py)");
            }
        }

        Assert.That(entries.Count, Is.EqualTo(129), "per-op registry entry count");
        Assert.That(failures, Is.Empty, "Registry/vignette copy drift:\n" + string.Join("\n", failures));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent!;
        }

        Assert.That(dir, Is.Not.Null, "Repository root not found.");
        return dir.FullName;
    }
}
