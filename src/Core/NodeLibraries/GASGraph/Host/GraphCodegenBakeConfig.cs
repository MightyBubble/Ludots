using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Bake-time graph codegen policy. Path: GAS/graph_codegen_bake.json — not game.json.
    /// </summary>
    public sealed class GraphCodegenBakeConfig
    {
        public const string RelativePath = "GAS/graph_codegen_bake.json";

        /// <summary>interpret | codegen | codegen-prefer</summary>
        public string Mode { get; set; } = "interpret";

        public GraphCodegenLoadMode ParsedMode =>
            GraphCodegenLoadModeParser.Parse(Mode, $"{RelativePath}:mode");
    }

    public sealed class GraphCodegenBakeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public GraphCodegenBakeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public GraphCodegenBakeConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = GraphCodegenBakeConfig.RelativePath)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                relativePath,
                ConfigMergePolicy.DeepObject);
            JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (merged == null || merged.Count == 0)
            {
                return new GraphCodegenBakeConfig();
            }

            GraphCodegenBakeConfig? config = merged.Deserialize<GraphCodegenBakeConfig>(
                StrictJsonOptions.CreateCamelCase());
            if (config == null)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize GraphCodegenBakeConfig from '{relativePath}'.");
            }

            _ = config.ParsedMode;
            return config;
        }
    }
}
