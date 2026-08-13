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

        MergeField(assetsRoot, vignette, path);
        Validate(vignette, opName, path);
        return vignette;
    }

    private static void MergeField(string assetsRoot, GraphOpsNodeVignette vignette, string path)
    {
        if (string.IsNullOrWhiteSpace(vignette.Field))
        {
            return;
        }

        if (vignette.Actors is { Length: > 0 })
        {
            throw new InvalidOperationException(
                $"Vignette '{path}' sets field '{vignette.Field}' and also actors; field owns the scene.");
        }

        string fieldPath = Path.Combine(assetsRoot, "Vignettes", "_fields", vignette.Field + ".json");
        if (!File.Exists(fieldPath))
        {
            throw new FileNotFoundException(
                $"Vignette '{path}' field '{vignette.Field}' is missing.",
                fieldPath);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        GraphOpsNodeField? field = JsonSerializer.Deserialize<GraphOpsNodeField>(
            File.ReadAllText(fieldPath),
            options);
        if (field == null)
        {
            throw new InvalidOperationException($"Field '{fieldPath}' deserialized to null.");
        }

        vignette.Actors = field.Actors ?? Array.Empty<GraphOpsNodeActor>();
        vignette.Collections = field.Collections ?? Array.Empty<GraphOpsNodeCollection>();
        vignette.Links = field.Links ?? Array.Empty<GraphOpsNodeLink>();
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

        var actorIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < vignette.Actors.Length; i++)
        {
            GraphOpsNodeActor actor = vignette.Actors[i];
            RequireText(actor.Id, $"actors[{i}].id", path);
            RequireText(actor.Role, $"actors[{i}].role", path);
            RequireText(actor.Template, $"actors[{i}].template", path);
            RequireText(actor.Name, $"actors[{i}].name", path);
            if (!actorIds.Add(actor.Id))
            {
                throw new InvalidOperationException($"Vignette '{path}' duplicate actor id '{actor.Id}'.");
            }
        }

        vignette.Collections ??= Array.Empty<GraphOpsNodeCollection>();
        vignette.Links ??= Array.Empty<GraphOpsNodeLink>();
        for (int i = 0; i < vignette.Collections.Length; i++)
        {
            GraphOpsNodeCollection collection = vignette.Collections[i];
            RequireText(collection.Key, $"collections[{i}].key", path);
            if (collection.Members == null || collection.Members.Length == 0)
            {
                throw new InvalidOperationException($"Vignette '{path}' collections[{i}] requires members.");
            }

            for (int m = 0; m < collection.Members.Length; m++)
            {
                RequireKnownActor(actorIds, collection.Members[m], path, $"collections[{i}].members[{m}]");
            }
        }

        for (int i = 0; i < vignette.Links.Length; i++)
        {
            GraphOpsNodeLink link = vignette.Links[i];
            RequireText(link.From, $"links[{i}].from", path);
            RequireText(link.To, $"links[{i}].to", path);
            RequireText(link.Type, $"links[{i}].type", path);
            RequireKnownActor(actorIds, link.From, path, $"links[{i}].from");
            RequireKnownActor(actorIds, link.To, path, $"links[{i}].to");
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

    private static void RequireKnownActor(HashSet<string> actorIds, string actorId, string path, string field)
    {
        if (!actorIds.Contains(actorId))
        {
            throw new InvalidOperationException($"Vignette '{path}' {field} references unknown actor '{actorId}'.");
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

internal sealed class GraphOpsNodeField
{
    public GraphOpsNodeActor[] Actors { get; set; } = Array.Empty<GraphOpsNodeActor>();
    public GraphOpsNodeCollection[] Collections { get; set; } = Array.Empty<GraphOpsNodeCollection>();
    public GraphOpsNodeLink[] Links { get; set; } = Array.Empty<GraphOpsNodeLink>();
}
