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

        private readonly struct FuncLibDefinition
        {
            public FuncLibDefinition(string name, string graphKey, int graphId, GraphKind kind)
            {
                Name = name;
                GraphKey = graphKey;
                GraphId = graphId;
                Kind = kind;
            }

            public string Name { get; }
            public string GraphKey { get; }
            public int GraphId { get; }
            public GraphKind Kind { get; }
        }

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

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog), "FuncLib load requires ConfigCatalog (GAS/func_lib.json is mandatory).");
            }

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "name");
            var fragments = _pipeline.CollectFragmentsWithSources(entry.RelativePath);
            if (fragments.Count == 0)
            {
                throw new InvalidOperationException(
                    $"[GraphFunctionCatalogLoader] '{entry.RelativePath}' is declared in catalog but no file was found.");
            }

            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
            if (merged.Count == 0)
            {
                throw new InvalidOperationException(
                    $"[GraphFunctionCatalogLoader] '{entry.RelativePath}' merged to an empty catalog.");
            }

            var errors = new List<string>();
            var definitions = new List<FuncLibDefinition>(merged.Count);
            var pendingByName = new Dictionary<string, FuncLibDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < merged.Count; i++)
            {
                string name = merged[i].Id;
                JsonObject obj = merged[i].Node;
                try
                {
                    string? graphKey = ReadRequiredString(obj, "graph", name);
                    string? kindText = ReadRequiredString(obj, "kind", name);
                    string purity = ReadOptionalString(obj, "purity") ?? "pure";
                    if (!string.Equals(purity, "pure", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' purity '{purity}' must be pure.");
                    }

                    if (!GraphKindParser.TryParse(kindText, out GraphKind kind) ||
                        kind != GraphKind.Script)
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' kind '{kindText}' must be Script (pure); Score and Validation are deferred until InvokeScore/InvokeValidation exist.");
                    }

                    int graphId = GraphIdRegistry.GetId(graphKey!);
                    if (graphId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' graph '{graphKey}' is not registered. Load graphs before func_lib.");
                    }

                    if (!_programs.TryGetRegistration(graphId, out GraphProgramRegistration registered) ||
                        registered.Kind != kind)
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' kind '{kind}' does not match registered graph '{graphKey}' kind '{registered.Kind}'.");
                    }

                    var definition = new FuncLibDefinition(name, graphKey!, graphId, kind);
                    if (!pendingByName.TryAdd(name, definition))
                    {
                        throw new InvalidOperationException(
                            $"FuncLib '{name}' is duplicated after merge.");
                    }

                    definitions.Add(definition);
                }
                catch (Exception ex)
                {
                    errors.Add($"FuncLib '{name}' in '{relativePath}': {ex.Message}");
                }
            }

            if (errors.Count == 0)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    FuncLibDefinition definition = definitions[i];
                    string rootLabel = $"FuncLib '{definition.Name}' graph '{definition.GraphKey}'";
                    if (!GraphYieldPurityValidator.TryValidateNoReachableYield(
                            _programs,
                            definition.GraphId,
                            rootLabel,
                            ResolvePendingFunction,
                            out string diagnostic))
                    {
                        errors.Add(
                            $"FuncLib '{definition.Name}' in '{relativePath}': {rootLabel} reaches Yield or an invalid pure closure and belongs in ActionLib. Path: {diagnostic}");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[GraphFunctionCatalogLoader] {errors.Count} func_lib error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                FuncLibDefinition definition = definitions[i];
                _catalog.Register(definition.Name, definition.GraphId, definition.Kind);
            }

            bool ResolvePendingFunction(string functionName, out GraphYieldPurityTarget target)
            {
                if (pendingByName.TryGetValue(functionName, out FuncLibDefinition definition))
                {
                    target = new GraphYieldPurityTarget(
                        definition.GraphId,
                        $"FuncLib '{definition.Name}' graph '{definition.GraphKey}'");
                    return true;
                }

                target = default;
                return false;
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

        private static string? ReadOptionalString(JsonObject obj, string property)
        {
            if (!obj.TryGetPropertyValue(property, out JsonNode? node) || node == null)
            {
                return null;
            }

            if (node is not JsonValue value)
            {
                throw new InvalidOperationException($"FuncLib property '{property}' must be a string.");
            }

            string? text = value.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

    }
}
