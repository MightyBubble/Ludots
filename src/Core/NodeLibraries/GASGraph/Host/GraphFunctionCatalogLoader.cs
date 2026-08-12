using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Loads GAS/func_lib.json: name → authored graph key → GraphIdRegistry id.
    /// Must run after graphs are registered in <see cref="GraphIdRegistry"/>.
    /// </summary>
    public sealed class GraphFunctionCatalogLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly GraphFunctionCatalog _catalog;
        private readonly GraphProgramRegistry _programs;

        public GraphFunctionCatalogLoader(
            ConfigPipeline pipeline,
            GraphFunctionCatalog catalog,
            GraphProgramRegistry programs)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        }

        public void Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "GAS/func_lib.json")
        {
            _catalog.Clear();

            if (catalog == null || !catalog.TryGet(relativePath, out _))
            {
                // Optional file: empty catalog is valid until authors add functions.
                return;
            }

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "name");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var errors = new List<string>();

            for (int i = 0; i < merged.Count; i++)
            {
                string name = merged[i].Id;
                JsonObject obj = merged[i].Node;
                try
                {
                    string? graphKey = ReadRequiredString(obj, "graph", name);
                    string? kindText = ReadRequiredString(obj, "kind", name);
                    if (!GraphKindParser.TryParse(kindText, out GraphKind kind) ||
                        kind is not (GraphKind.Script or GraphKind.Validation or GraphKind.Score))
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' kind '{kindText}' must be Script, Validation, or Score.");
                    }

                    int graphId = GraphIdRegistry.GetId(graphKey!);
                    if (graphId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' graph '{graphKey}' is not registered. Load graphs before func_lib.");
                    }

                    if (!_programs.TryGetKind(graphId, out GraphKind registeredKind) || registeredKind != kind)
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' kind '{kind}' does not match registered graph '{graphKey}' kind '{registeredKind}'.");
                    }

                    _catalog.Register(name, graphId, kind);
                }
                catch (Exception ex)
                {
                    errors.Add($"FuncLib '{name}' in '{relativePath}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[GraphFunctionCatalogLoader] {errors.Count} func_lib error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }
        }

        private static string ReadRequiredString(JsonObject obj, string property, string funcName)
        {
            if (!obj.TryGetPropertyValue(property, out JsonNode? node) || node is not JsonValue value)
            {
                throw new InvalidOperationException($"FuncLib '{funcName}' requires '{property}'.");
            }

            string? text = value.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"FuncLib '{funcName}' '{property}' is empty.");
            }

            return text.Trim();
        }
    }
}
