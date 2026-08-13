using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;

namespace CapabilityStandardGraphOpsAttrMod.Runtime;

public static class GraphOpsAttrGraphKeys
{
    public const string ReadHealth = "Graph.GraphOpsAttr.ReadHealth";
    public const string Strike = "Graph.GraphOpsAttr.Strike";
    public const string ApplyMark = "Graph.GraphOpsAttr.ApplyMark";
    public const string RemoveMark = "Graph.GraphOpsAttr.RemoveMark";
    public const string MarkEffect = "Effect.GraphOpsAttr.Mark";
}

internal sealed class GraphOpsAttrSymbolResolver : IGraphSymbolResolver
{
    public int ResolveTag(string name) => GraphOpsMutableRegistry.Tag(name);
    public int ResolveAttribute(string name) => GraphOpsMutableRegistry.Attribute(name);
    public int ResolveEffectTemplate(string name) => GraphOpsMutableRegistry.EffectTemplate(name);
    public int ResolveRelationshipType(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.type.{name}");
    public int ResolveRelationshipMetric(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.metric.{name}");
    public int ResolveRelationshipFlag(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.flag.{name}");
    public int ResolveRelationshipReason(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.reason.{name}");
    public int ResolveTargetDispatchPreset(string name) => GraphOpsMutableRegistry.ConfigKey($"targetDispatch.{name}");
    public int ResolveEntityTemplate(string name) => GraphOpsMutableRegistry.ConfigKey($"entityTemplate.{name}");
}

public static class GraphOpsAttrGraphBootstrap
{
    public static GraphProgramRegistry LoadModGraphs(string modAssetsRoot)
    {
        if (string.IsNullOrWhiteSpace(modAssetsRoot))
        {
            throw new ArgumentException("Mod assets root is required.", nameof(modAssetsRoot));
        }

        string graphsPath = Path.Combine(modAssetsRoot, "GAS", "graphs.json");
        if (!File.Exists(graphsPath))
        {
            throw new FileNotFoundException($"Missing attr showcase graphs: {graphsPath}");
        }

        GraphIdRegistry.Clear();
        _ = GraphOpsMutableRegistry.EffectTemplate(GraphOpsAttrGraphKeys.MarkEffect);

        var programs = new GraphProgramRegistry();
        var resolver = new GraphOpsAttrSymbolResolver();
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(graphsPath));

        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string id = element.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Graph entry missing id.");
            JsonObject obj = JsonNode.Parse(element.GetRawText())!.AsObject();
            GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, id, options);
            foreach (GraphDiagnostic diagnostic in compiled.Diagnostics)
            {
                if (diagnostic.Severity == GraphDiagnosticSeverity.Error)
                {
                    throw new InvalidOperationException($"Compile {id}: {diagnostic.Message}");
                }
            }

            if (!compiled.Succeeded || !compiled.Package.HasValue)
            {
                throw new InvalidOperationException($"Compile {id} produced no package.");
            }

            string kindText = element.GetProperty("kind").GetString() ?? "Effect";
            if (!GraphKindParser.TryParse(kindText, out GraphKind kind))
            {
                throw new InvalidOperationException($"Graph '{id}' has invalid kind '{kindText}'.");
            }

            GraphProgramPackage package = compiled.Package.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);
            int graphId = GraphIdRegistry.Register(id);
            programs.Register(graphId, package.Program, kind, compiled.SourceMap, package.Symbols);
        }

        return programs;
    }

    public static string FindModAssetsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardGraphOpsAttrMod",
                "assets");
            if (File.Exists(Path.Combine(candidate, "GAS", "graphs.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("CapabilityStandardGraphOpsAttrMod assets root not found.");
    }
}
