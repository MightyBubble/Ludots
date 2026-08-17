using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public static class GraphOpsNodeIds
{
    public const string ShowcaseIdPrefix = "capability_standard_graph_op_";
    public const string GraphIdPrefix = "showcase.graph_op.";
    public const string ModAssetsRelative =
        "mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets";

    public static string ShowcaseId(string op) => ShowcaseIdPrefix + RequireOpName(op);

    public static string MapId(string op) => ShowcaseId(op);

    public static string GraphId(string op) => GraphIdPrefix + RequireOpName(op);

    public static string RequireOpName(string op)
    {
        if (!GraphNodeOpParser.TryParse(op, out GraphNodeOp parsed))
        {
            throw new InvalidOperationException($"Unknown GraphNodeOp '{op}'.");
        }

        return parsed.ToString();
    }

    public static bool TryParseOpFromMapId(string? mapId, out string op)
    {
        op = string.Empty;
        if (string.IsNullOrWhiteSpace(mapId) ||
            !mapId.StartsWith(ShowcaseIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = mapId[ShowcaseIdPrefix.Length..];
        if (!GraphNodeOpParser.TryParse(candidate, out GraphNodeOp parsed))
        {
            return false;
        }

        op = parsed.ToString();
        return true;
    }
}
