using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GraphActionCatalogLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly GraphActionCatalog _catalog;
        private readonly GraphProgramRegistry _programs;
        private readonly GraphFunctionCatalog _functions;

        public GraphActionCatalogLoader(
            ConfigPipeline pipeline,
            GraphActionCatalog catalog,
            GraphProgramRegistry programs,
            GraphFunctionCatalog functions)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
            _functions = functions ?? throw new ArgumentNullException(nameof(functions));
        }

        public void Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "GAS/action_lib.json")
        {
            _catalog.Clear();

            if (catalog == null)
            {
                throw new ArgumentNullException(
                    nameof(catalog),
                    "ActionLib load requires ConfigCatalog (GAS/action_lib.json is mandatory).");
            }

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "name");
            var fragments = _pipeline.CollectFragmentsWithSources(entry.RelativePath);
            if (fragments.Count == 0)
            {
                throw new InvalidOperationException(
                    $"[GraphActionCatalogLoader] '{entry.RelativePath}' is declared in catalog but no file was found.");
            }

            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
            if (merged.Count == 0)
            {
                throw new InvalidOperationException(
                    $"[GraphActionCatalogLoader] '{entry.RelativePath}' merged to an empty catalog.");
            }

            var errors = new List<string>();

            for (int i = 0; i < merged.Count; i++)
            {
                string name = merged[i].Id;
                JsonObject obj = merged[i].Node;
                try
                {
                    if (_functions.TryGet(name, out _))
                    {
                        throw new InvalidOperationException($"ActionLib '{name}' duplicates FuncLib name.");
                    }

                    string graphKey = ReadRequiredString(obj, "graph", name);
                    string kindText = ReadRequiredString(obj, "kind", name);
                    if (!GraphKindParser.TryParse(kindText, out GraphKind kind) || kind != GraphKind.Script)
                    {
                        throw new InvalidOperationException(
                            $"ActionLib '{name}' kind '{kindText}' must be Script.");
                    }

                    int graphId = GraphIdRegistry.GetId(graphKey);
                    if (graphId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"ActionLib '{name}' graph '{graphKey}' is not registered. Load graphs before action_lib.");
                    }

                    if (!_programs.TryGetKind(graphId, out GraphKind registeredKind) || registeredKind != GraphKind.Script)
                    {
                        throw new InvalidOperationException(
                            $"ActionLib '{name}' graph '{graphKey}' kind '{registeredKind}' must be Script.");
                    }

                    if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"ActionLib '{name}' graph '{graphKey}' has no registered program.");
                    }

                    string hostText = ReadRequiredString(obj, "host", name);
                    if (!GraphActionHostYieldPolicy.TryParse(hostText, out GraphActionHost host))
                    {
                        throw new InvalidOperationException(
                            $"ActionLib '{name}' host '{hostText}' must be BehaviorTree, Hfsm, Level, or Script.");
                    }

                    if (_programs.TryGetRegistration(graphId, out GraphProgramRegistration registration) &&
                        registration.ContainsYield &&
                        !GraphActionHostYieldPolicy.AllowsYield(host))
                    {
                        throw new InvalidOperationException(
                            $"ActionLib '{name}' host '{host}' cannot bind a program that reaches Yield.");
                    }

                    _catalog.Register(name, graphId, GraphKind.Script);
                }
                catch (Exception ex)
                {
                    errors.Add($"ActionLib '{name}' in '{relativePath}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[GraphActionCatalogLoader] {errors.Count} action_lib error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }
        }

        private static string ReadRequiredString(JsonObject obj, string property, string actionName)
        {
            if (!obj.TryGetPropertyValue(property, out JsonNode? node) || node is not JsonValue value)
            {
                throw new InvalidOperationException($"ActionLib '{actionName}' requires '{property}'.");
            }

            string? text = value.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"ActionLib '{actionName}' '{property}' is empty.");
            }

            return text.Trim();
        }
    }
}
