using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>Loads core GAS Script graphs (+ func_lib) into Registry for headless tests.</summary>
    internal static class GraphRegistryTestBootstrap
    {
        public static GraphProgramRegistry LoadCoreScriptsAndFuncLib(out GraphFunctionCatalog catalog)
        {
            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            catalog = new GraphFunctionCatalog();

            string graphsPath = Path.Combine(FindRepoRoot(), "assets", "Configs", "GAS", "graphs.json");
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
                programs.Register(graphId, pkg.Value.Program, GraphKind.Script);
            }

            string funcPath = Path.Combine(FindRepoRoot(), "assets", "Configs", "GAS", "func_lib.json");
            using var funcDoc = JsonDocument.Parse(File.ReadAllText(funcPath));
            foreach (JsonElement el in funcDoc.RootElement.EnumerateArray())
            {
                string name = el.GetProperty("name").GetString()!;
                string graphKey = el.GetProperty("graph").GetString()!;
                string kindText = el.GetProperty("kind").GetString()!;
                if (!GraphKindParser.TryParse(kindText, out GraphKind kind))
                {
                    throw new InvalidOperationException($"Bad func_lib kind {kindText}");
                }

                int id = GraphIdRegistry.GetId(graphKey);
                if (id <= 0)
                {
                    throw new InvalidOperationException($"func_lib '{name}' graph '{graphKey}' missing.");
                }

                catalog.Register(name, id, kind);
            }

            return programs;
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
}
