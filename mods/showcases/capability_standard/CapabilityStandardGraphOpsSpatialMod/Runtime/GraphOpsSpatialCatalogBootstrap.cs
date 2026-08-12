using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsSpatialMod.Runtime;

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

            int graphId = GraphIdRegistry.Register(id);
            string[] symbols = compiled.Package?.Symbols ?? Array.Empty<string>();
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
