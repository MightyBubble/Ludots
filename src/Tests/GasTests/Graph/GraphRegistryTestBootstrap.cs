using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Config;
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
            => LoadCoreScriptsFuncLibAndActionLib(out catalog, out _, out _);

        public static GraphProgramRegistry LoadCoreScriptsFuncLibAndActionLib(
            out GraphFunctionCatalog catalog,
            out GraphActionCatalog actions)
            => LoadCoreScriptsFuncLibAndActionLib(out catalog, out actions, out _);

        public static GraphProgramRegistry LoadCoreScriptsFuncLibAndActionLib(
            out GraphFunctionCatalog catalog,
            out GraphActionCatalog actions,
            out GraphBehaviorCatalog behavior)
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
            configCatalog.Add(new ConfigCatalogEntry("AI/behavior_trees.json", ConfigMergePolicy.ArrayById, "id"));
            configCatalog.Add(new ConfigCatalogEntry("AI/hfsm.json", ConfigMergePolicy.ArrayById, "id"));

            List<GraphProgramPackage> graphPackages = LoadScriptGraphs(programs, repoRoot);

            var graphConfigLoader = new GraphProgramConfigLoader(pipeline, programs, new BootstrapGraphSymbolResolver());
            new GraphFunctionCatalogLoader(pipeline, catalog, programs).Load(configCatalog);
            graphConfigLoader.ResolveFuncLibInvokes(graphPackages, catalog);
            new GraphActionCatalogLoader(pipeline, actions, programs, catalog).Load(configCatalog);
            behavior = new GraphBehaviorDefinitionLoader(pipeline, actions).Load(configCatalog);

            return programs;
        }

        private static List<GraphProgramPackage> LoadScriptGraphs(GraphProgramRegistry programs, string repoRoot)
        {
            string graphsPath = Path.Combine(repoRoot, "assets", "Configs", "GAS", "graphs.json");
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            var packages = new List<GraphProgramPackage>();
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
                packages.Add(pkg.Value);
            }

            return packages;
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

        private sealed class BootstrapGraphSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw Unsupported(name);
            public int ResolveAttribute(string name) => throw Unsupported(name);
            public int ResolveEffectTemplate(string name) => throw Unsupported(name);
            public int ResolveRelationshipType(string name) => throw Unsupported(name);
            public int ResolveRelationshipMetric(string name) => throw Unsupported(name);
            public int ResolveRelationshipFlag(string name) => throw Unsupported(name);
            public int ResolveRelationshipReason(string name) => throw Unsupported(name);
            public int ResolveTargetDispatchPreset(string name) => throw Unsupported(name);
            public int ResolveEntityTemplate(string name) => throw Unsupported(name);
            public int ResolveTagDisplayTable(string name) => throw Unsupported(name);

            private static InvalidOperationException Unsupported(string name)
                => new($"GraphRegistryTestBootstrap only resolves FuncLib invokes; symbol '{name}' requires the production graph config loader path.");
        }
    }
}
