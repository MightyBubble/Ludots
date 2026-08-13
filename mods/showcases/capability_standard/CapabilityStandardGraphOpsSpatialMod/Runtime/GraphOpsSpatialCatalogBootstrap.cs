using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsSpatialMod.Runtime;

internal sealed class GraphOpsSpatialSymbolResolver : IGraphSymbolResolver
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

internal static class GraphOpsSpatialCatalogBootstrap
{
    private const string ModAssetsRelativePath =
        "mods/showcases/capability_standard/CapabilityStandardGraphOpsSpatialMod/assets";

    public static GraphProgramRegistry Load(out GraphFunctionCatalog catalog)
    {
        GraphIdRegistry.Clear();
        var programs = new GraphProgramRegistry();
        catalog = new GraphFunctionCatalog();

        string repoRoot = FindRepoRoot();
        string modAssets = Path.Combine(repoRoot, ModAssetsRelativePath);
        LoadEffectGraphs(programs, Path.Combine(modAssets, "GAS", "graphs.json"));
        return programs;
    }

    private static void LoadEffectGraphs(GraphProgramRegistry programs, string graphsPath)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        var resolver = new GraphOpsSpatialSymbolResolver();
        using var doc = JsonDocument.Parse(File.ReadAllText(graphsPath));
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string kind = el.GetProperty("kind").GetString() ?? "";
            if (!string.Equals(kind, "Effect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string id = el.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Effect graph missing id.");
            var obj = JsonNode.Parse(el.GetRawText())!.AsObject();
            GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, id, options);
            if (!compiled.Succeeded)
            {
                string message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
                throw new InvalidOperationException($"Compile {id}: {message}");
            }

            GraphKindOperationPolicy.RequireAllowed(
                GraphKind.Effect,
                compiled.Program,
                GasGraphOpHandlerTable.Instance,
                entrypoint: nameof(GraphOpsSpatialCatalogBootstrap));

            string[] symbols = compiled.Package?.Symbols
                ?? throw new InvalidOperationException($"Compile {id} produced no symbol table; blackboard keys cannot be patched.");
            if (symbols.Length == 0)
            {
                throw new InvalidOperationException($"Compile {id} produced an empty symbol table; blackboard keys cannot be patched.");
            }

            GraphProgramSymbolPatcher.Patch(symbols, compiled.Program, resolver);

            int graphId = GraphIdRegistry.Register(id);
            programs.Register(
                graphId,
                compiled.Program,
                GraphKind.Effect,
                compiled.SourceMap,
                symbols);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Repository root not found for Spatial GraphOps assets.");
    }
}
