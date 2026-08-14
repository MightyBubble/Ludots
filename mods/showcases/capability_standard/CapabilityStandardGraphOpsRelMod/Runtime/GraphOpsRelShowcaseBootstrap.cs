using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

public static class GraphOpsRelShowcaseBootstrap
{
    private const string ModAssetsRelative = "mods/showcases/capability_standard/CapabilityStandardGraphOpsRelMod/assets";

    public static GraphOpsRelShowcaseBundle LoadStandalone()
    {
        var programs = new GraphProgramRegistry();
        var functions = new GraphOpsRelFunctionIndex();
        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        var flags = new RelationshipFlagRegistry();
        var reasons = new RelationshipReasonRegistry();

        RegisterRelationshipCatalog(types, metrics, flags, reasons);

        string modAssets = Path.Combine(FindRepoRoot(), ModAssetsRelative);
        var resolver = new GasGraphSymbolResolver(
            types,
            metrics,
            flags,
            reasons,
            new TargetDispatchPresetRegistry());

        LoadGraphs(programs, Path.Combine(modAssets, "GAS", "graphs.json"), resolver);
        RegisterFuncLib(functions, programs, Path.Combine(modAssets, "Gallery", "func_lib.json"));

        return new GraphOpsRelShowcaseBundle
        {
            Programs = programs,
            Functions = functions,
            Types = types,
            Metrics = metrics,
            Flags = flags,
            Reasons = reasons,
        };
    }

    private static void RegisterRelationshipCatalog(
        RelationshipTypeRegistry types,
        RelationshipMetricRegistry metrics,
        RelationshipFlagRegistry flags,
        RelationshipReasonRegistry reasons)
    {
        types.Register("SocialBond");
        metrics.Register("Loyalty", -100, 100, 0);
        flags.Register("Trusted");
        flags.Register("Estranged");
        reasons.Register("Scenario.Setup");
    }

    private static void RegisterFuncLib(GraphOpsRelFunctionIndex functions, GraphProgramRegistry programs, string funcLibPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(funcLibPath));
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string name = el.GetProperty("name").GetString()
                ?? throw new InvalidOperationException("func_lib entry missing name.");
            string graphKey = el.GetProperty("graph").GetString()
                ?? throw new InvalidOperationException($"func_lib '{name}' missing graph.");
            string kindText = el.GetProperty("kind").GetString() ?? "Query";
            GraphKind kind = kindText switch
            {
                "Query" => GraphKind.Query,
                "Effect" => GraphKind.Effect,
                _ => throw new InvalidOperationException($"Unsupported func_lib kind '{kindText}'.")
            };

            int graphId = GraphIdRegistry.GetId(graphKey);
            if (graphId <= 0)
            {
                throw new InvalidOperationException($"Graph '{graphKey}' for func_lib '{name}' is not registered.");
            }

            programs.RequireKind(graphId, kind);
            functions.Register(name, graphId, kind);
        }
    }

    private static void LoadGraphs(GraphProgramRegistry programs, string graphsPath, GasGraphSymbolResolver resolver)
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

            GraphProgramPackage package = pkg.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);
            int graphId = GraphIdRegistry.Register(id);
            programs.Register(graphId, package.Program, ParseKind(kind), GraphInstructionSourceMap.Empty, package.Symbols);
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
