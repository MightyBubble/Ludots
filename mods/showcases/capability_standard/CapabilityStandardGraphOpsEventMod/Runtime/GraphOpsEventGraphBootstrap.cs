using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;

namespace CapabilityStandardGraphOpsEventMod.Runtime;

public static class GraphOpsEventGraphKeys
{
    public const string Dispatch = "Graph.GraphOpsEvent.Dispatch";
    public const string Placement = "Graph.GraphOpsEvent.Placement";
    public const string DispatchStubEffect = "Effect.GraphOpsEvent.DispatchStub";
    public const string SnapCollection = "showcase.graph_ops_event.snap";
    public const string DamageDealtTag = "Event.DamageDealt";
    public const string TargetToResolvedPreset = "TargetToResolved";
}

internal sealed class GraphOpsEventSymbolResolver : IGraphSymbolResolver
{
    private readonly TargetDispatchPresetRegistry _targetDispatchPresets;

    public GraphOpsEventSymbolResolver(TargetDispatchPresetRegistry targetDispatchPresets)
    {
        _targetDispatchPresets = targetDispatchPresets ?? throw new ArgumentNullException(nameof(targetDispatchPresets));
    }

    public int ResolveTag(string name) => GraphOpsMutableRegistry.Tag(name);
    public int ResolveAttribute(string name) => GraphOpsMutableRegistry.Attribute(name);
    public int ResolveEffectTemplate(string name) => GraphOpsMutableRegistry.EffectTemplate(name);
    public int ResolveRelationshipType(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.type.{name}");
    public int ResolveRelationshipMetric(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.metric.{name}");
    public int ResolveRelationshipFlag(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.flag.{name}");
    public int ResolveRelationshipReason(string name) => GraphOpsMutableRegistry.ConfigKey($"relationship.reason.{name}");
    public int ResolveTargetDispatchPreset(string name) => _targetDispatchPresets.GetId(name);
    public int ResolveEntityTemplate(string name) => GraphOpsMutableRegistry.ConfigKey($"entityTemplate.{name}");
}

public static class GraphOpsEventGraphBootstrap
{
    public static GraphProgramRegistry LoadModGraphs(
        string modAssetsRoot,
        out TargetDispatchPresetRegistry targetDispatchPresets,
        out EntityCollectionStore entityCollections)
    {
        if (string.IsNullOrWhiteSpace(modAssetsRoot))
        {
            throw new ArgumentException("Mod assets root is required.", nameof(modAssetsRoot));
        }

        string graphsPath = Path.Combine(modAssetsRoot, "GAS", "graphs.json");
        if (!File.Exists(graphsPath))
        {
            throw new FileNotFoundException($"Missing event showcase graphs: {graphsPath}");
        }

        _ = GraphOpsMutableRegistry.EffectTemplate(GraphOpsEventGraphKeys.DispatchStubEffect);
        _ = GraphOpsMutableRegistry.Tag(GraphOpsEventGraphKeys.DamageDealtTag);

        targetDispatchPresets = new TargetDispatchPresetRegistry();
        targetDispatchPresets.Register(
            GraphOpsEventGraphKeys.TargetToResolvedPreset,
            new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalTarget,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalSource,
            });

        var programs = new GraphProgramRegistry();
        var resolver = new GraphOpsEventSymbolResolver(targetDispatchPresets);
        entityCollections = new EntityCollectionStore(new StringIntRegistry());
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(graphsPath));

        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string id = element.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Graph entry missing id.");
            JsonObject obj = JsonNode.Parse(element.GetRawText())!.AsObject();
            var (pkg, _, diags) = GraphProgramAuthoringFrontDoor.CompileJsonObject(obj, id, options);
            foreach (GraphDiagnostic diagnostic in diags)
            {
                if (diagnostic.Severity == GraphDiagnosticSeverity.Error)
                {
                    throw new InvalidOperationException($"Compile {id}: {diagnostic.Message}");
                }
            }

            if (!pkg.HasValue)
            {
                throw new InvalidOperationException($"Compile {id} produced no package.");
            }

            string kindText = element.GetProperty("kind").GetString() ?? "Effect";
            if (!GraphKindParser.TryParse(kindText, out GraphKind kind))
            {
                throw new InvalidOperationException($"Graph '{id}' has invalid kind '{kindText}'.");
            }

            GraphProgramPackage package = pkg.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver, entityCollections);
            int graphId = GraphIdRegistry.Register(id);
            programs.Register(graphId, package.Program, kind, GraphInstructionSourceMap.Empty, package.Symbols);
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
                "CapabilityStandardGraphOpsEventMod",
                "assets");
            if (File.Exists(Path.Combine(candidate, "GAS", "graphs.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("CapabilityStandardGraphOpsEventMod assets root not found.");
    }
}
