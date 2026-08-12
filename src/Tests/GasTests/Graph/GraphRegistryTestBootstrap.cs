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

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>Loads core GAS Script graphs plus func_lib / action_lib via production catalog loaders.</summary>
    internal static class GraphRegistryTestBootstrap
    {
        public static GraphProgramRegistry LoadCoreScriptsFuncLibAndActionLib(out GraphFunctionCatalog catalog)
            => LoadCoreScriptsFuncLibAndActionLib(out catalog, out _);

        public static GraphProgramRegistry LoadCoreScriptsFuncLibAndActionLib(
            out GraphFunctionCatalog catalog,
            out GraphActionCatalog actions)
        {
            GraphIdRegistry.Clear();
            var programs = new GraphProgramRegistry();
            catalog = new GraphFunctionCatalog();
            actions = new GraphActionCatalog();

            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var configCatalog = new ConfigCatalog();
            configCatalog.Add(new ConfigCatalogEntry("GAS/func_lib.json", ConfigMergePolicy.ArrayById, "name"));
            configCatalog.Add(new ConfigCatalogEntry("GAS/action_lib.json", ConfigMergePolicy.ArrayById, "name"));

            LoadScriptGraphs(programs, repoRoot);

            new GraphFunctionCatalogLoader(pipeline, catalog, programs).Load(configCatalog);
            new GraphActionCatalogLoader(pipeline, actions, programs, catalog).Load(configCatalog);

            return programs;
        }

        private static void LoadScriptGraphs(GraphProgramRegistry programs, string repoRoot)
        {
            string graphsPath = Path.Combine(repoRoot, "assets", "Configs", "GAS", "graphs.json");
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
