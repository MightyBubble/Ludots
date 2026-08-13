using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public static class GraphOpsNodeVignetteLoader
{
    public static GraphOpsNodeVignette Load(string assetsRoot, string op)
    {
        string opName = GraphOpsNodeIds.RequireOpName(op);
        string path = Path.Combine(assetsRoot, "Vignettes", opName + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Missing per-op vignette for {opName}. Each GraphNodeOp must have assets/Vignettes/{opName}.json.",
                path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        GraphOpsNodeVignette? vignette = JsonSerializer.Deserialize<GraphOpsNodeVignette>(
            File.ReadAllText(path),
            options);
        if (vignette == null)
        {
            throw new InvalidOperationException($"Vignette '{path}' deserialized to null.");
        }

        Validate(vignette, opName, path);
        return vignette;
    }

    public static void Validate(GraphOpsNodeVignette vignette, string expectedOp, string path)
    {
        if (!string.Equals(vignette.Op, expectedOp, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Vignette '{path}' op '{vignette.Op}' must equal '{expectedOp}'.");
        }

        if (!GraphNodeOpParser.TryParse(vignette.Op, out _))
        {
            throw new InvalidOperationException($"Vignette '{path}' has unknown op '{vignette.Op}'.");
        }

        RequireText(vignette.Driver, "driver", path);
        RequireText(vignette.Title, "title", path);
        RequireText(vignette.Beat, "beat", path);
        RequireText(vignette.DetailTemplate, "detailTemplate", path);
        RequireText(vignette.FeaturedNodeId, "featuredNodeId", path);
        RequireText(vignette.GraphKind, "graphKind", path);
        if (vignette.Actors == null || vignette.Actors.Length == 0)
        {
            throw new InvalidOperationException($"Vignette '{path}' requires at least one actor.");
        }

        for (int i = 0; i < vignette.Actors.Length; i++)
        {
            GraphOpsNodeActor actor = vignette.Actors[i];
            RequireText(actor.Id, $"actors[{i}].id", path);
            RequireText(actor.Role, $"actors[{i}].role", path);
            RequireText(actor.Template, $"actors[{i}].template", path);
            RequireText(actor.Name, $"actors[{i}].name", path);
        }

        RejectBannedCaption(vignette.Title, path, "title");
        RejectBannedCaption(vignette.Beat, path, "beat");
        RejectBannedCaption(vignette.DetailTemplate, path, "detailTemplate");
    }

    public static void RejectBannedCaption(string text, string path, string field)
    {
        string[] banned =
        [
            "FuncLib", "Validation", "tally", "True", "False", "耗时", "GraphNodeOp",
            "opcode", "Opcode"
        ];
        for (int i = 0; i < banned.Length; i++)
        {
            if (text.Contains(banned[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Vignette '{path}' {field} contains banned token '{banned[i]}'.");
            }
        }
    }

    private static void RequireText(string? value, string field, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Vignette '{path}' missing {field}.");
        }
    }
}
