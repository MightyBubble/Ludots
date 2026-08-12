using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;

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

        var vfs = new VirtualFileSystem();
        vfs.Mount("CapabilityStandardGraphOpsSpatialMod", modAssets);
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        var pipeline = new ConfigPipeline(vfs, modLoader);
        var configCatalog = new ConfigCatalog();
        configCatalog.Add(new ConfigCatalogEntry("GAS/func_lib.json", ConfigMergePolicy.ArrayById, "name"));

        LoadScriptGraphs(programs, Path.Combine(modAssets, "GAS", "graphs.json"));
        new GraphFunctionCatalogLoader(pipeline, catalog, programs).Load(configCatalog);

        return programs;
    }

    private static void LoadScriptGraphs(GraphProgramRegistry programs, string graphsPath)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        using var doc = JsonDocument.Parse(File.ReadAllText(graphsPath));
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string kind = el.GetProperty("kind").GetString() ?? "";
            if (!string.Equals(kind, "Script", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string id = el.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Script graph missing id.");
            var obj = JsonNode.Parse(el.GetRawText())!.AsObject();
            var (pkg, _, diags) = GraphProgramAuthoringFrontDoor.CompileJsonObject(obj, id, options);
            foreach (var d in diags)
            {
                if (d.Severity == GraphDiagnosticSeverity.Error)
                {
                    throw new InvalidOperationException($"Compile {id}: {d.Message}");
                }
            }

            if (!pkg.HasValue)
            {
                throw new InvalidOperationException($"Compile {id} produced no package.");
            }

            int graphId = GraphIdRegistry.Register(id);
            programs.Register(graphId, pkg.Value.Program, GraphKind.Script, GraphInstructionSourceMap.Empty, pkg.Value.Symbols);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
