using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

public static class GraphOpsRelShowcaseBootstrap
{
    private const string ModAssetsRelative = "mods/showcases/capability_standard/CapabilityStandardGraphOpsRelMod/assets";

    public static GraphProgramRegistry Load(out GraphFunctionCatalog catalog)
    {
        GraphIdRegistry.Clear();
        var programs = new GraphProgramRegistry();
        catalog = new GraphFunctionCatalog();

        string repoRoot = FindRepoRoot();
        string modAssets = Path.Combine(repoRoot, ModAssetsRelative);
        string modGraphsPath = Path.Combine(modAssets, "GAS", "graphs.json");
        string modFuncLibPath = Path.Combine(modAssets, "GAS", "func_lib.json");

        LoadGraphs(programs, modGraphsPath);
        RegisterFuncLib(catalog, programs, modFuncLibPath);

        return programs;
    }

    private static void RegisterFuncLib(GraphFunctionCatalog catalog, GraphProgramRegistry programs, string funcLibPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(funcLibPath));
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string name = el.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("func_lib entry missing name.");
            string graphKey = el.GetProperty("graph").GetString()
                ?? throw new InvalidOperationException($"func_lib '{name}' missing graph.");
            string kindText = el.GetProperty("kind").GetString() ?? "Script";
            GraphKind kind = kindText switch
            {
                "Query" => GraphKind.Query,
                "Effect" => GraphKind.Effect,
                "Script" => GraphKind.Script,
                _ => throw new InvalidOperationException($"Unsupported func_lib kind '{kindText}'.")
            };

            int graphId = GraphIdRegistry.GetId(graphKey);
            if (graphId <= 0)
            {
                throw new InvalidOperationException($"Graph '{graphKey}' for func_lib '{name}' is not registered.");
            }

            programs.RequireKind(graphId, kind);
            catalog.Register(name, graphId, kind);
        }
    }

    private static void LoadGraphs(GraphProgramRegistry programs, string graphsPath)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        using var doc = JsonDocument.Parse(File.ReadAllText(graphsPath));
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string kind = el.GetProperty("kind").GetString() ?? "";
            string id = el.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Graph missing id.");
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
            programs.Register(graphId, pkg.Value.Program, ParseKind(kind), GraphInstructionSourceMap.Empty, pkg.Value.Symbols);
        }
    }

    private static GraphKind ParseKind(string kind)
        => kind switch
        {
            "Script" => GraphKind.Script,
            "Query" => GraphKind.Query,
            "Effect" => GraphKind.Effect,
            _ => throw new InvalidOperationException($"Unsupported graph kind '{kind}'.")
        };

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
