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

    [Test]
    public void GeneratedMaps_DeclareVignetteVariables()
    {
        string repo = FindRepoRoot();
        string vignetteDir = Path.Combine(repo, GalleryRelative, "Vignettes");
        string mapsDir = Path.Combine(repo, GalleryRelative, "Maps");
        var failures = new List<string>();

        foreach (string file in Directory.GetFiles(vignetteDir, "*.json").OrderBy(f => f))
        {
            string op = Path.GetFileNameWithoutExtension(file);
            if (op.StartsWith('_'))
            {
                continue;
            }

            using JsonDocument vignette = JsonDocument.Parse(File.ReadAllText(file));
            if (!vignette.RootElement.TryGetProperty("variables", out JsonElement expected) ||
                expected.ValueKind != JsonValueKind.Array ||
                expected.GetArrayLength() == 0)
            {
                continue;
            }

            string mapPath = Path.Combine(mapsDir, "capability_standard_graph_op_" + op + ".json");
            if (!File.Exists(mapPath))
            {
                failures.Add($"{op}: generated map missing {mapPath}");
                continue;
            }

            using JsonDocument map = JsonDocument.Parse(File.ReadAllText(mapPath));
            if (!map.RootElement.TryGetProperty("Variables", out JsonElement actual) ||
                actual.ValueKind != JsonValueKind.Array)
            {
                failures.Add($"{op}: map has no Variables[] (rerun generate-graph-op-node-galleries.py after generator fix)");
                continue;
            }

            if (actual.GetArrayLength() != expected.GetArrayLength())
            {
                failures.Add($"{op}: map Variables count {actual.GetArrayLength()} != vignette {expected.GetArrayLength()}");
                continue;
            }

            for (int i = 0; i < expected.GetArrayLength(); i++)
            {
                JsonElement want = expected[i];
                JsonElement got = actual[i];
                string wantName = want.GetProperty("name").GetString()!;
                string gotName = got.GetProperty("name").GetString()!;
                if (wantName != gotName)
                {
                    failures.Add($"{op}: Variables[{i}].name map '{gotName}' != vignette '{wantName}'");
                }

                string wantType = want.GetProperty("type").GetString()!;
                string gotType = got.GetProperty("type").GetString()!;
                if (!string.Equals(wantType, gotType, System.StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{op}: Variables[{i}].type map '{gotType}' != vignette '{wantType}'");
                }

                if (!JsonElementNumericEquals(want.GetProperty("initial"), got.GetProperty("initial")))
                {
                    failures.Add($"{op}: Variables[{i}].initial map {got.GetProperty("initial")} != vignette {want.GetProperty("initial")}");
                }

                bool wantPhase = want.TryGetProperty("phase", out JsonElement wantPhaseEl) &&
                                 wantPhaseEl.ValueKind == JsonValueKind.True;
                bool gotPhase = got.TryGetProperty("phase", out JsonElement gotPhaseEl) &&
                                gotPhaseEl.ValueKind == JsonValueKind.True;
                if (wantPhase != gotPhase)
                {
                    failures.Add($"{op}: Variables[{i}].phase map {gotPhase} != vignette {wantPhase}");
                }
            }
        }

        Assert.That(failures, Is.Empty, "Map/vignette Variables drift:\n" + string.Join("\n", failures));
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

    private static bool JsonElementNumericEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != JsonValueKind.Number || right.ValueKind != JsonValueKind.Number)
        {
            return left.GetRawText() == right.GetRawText();
        }

        return left.GetDouble() == right.GetDouble();
    }
}
