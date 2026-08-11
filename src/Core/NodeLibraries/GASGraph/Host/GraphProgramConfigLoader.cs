using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GraphProgramConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly GraphProgramRegistry _registry;
        private readonly IGraphSymbolResolver _symbolResolver;
        private readonly GraphOutputSchemaRegistry? _outputSchemas;
        private readonly StringIntRegistry? _outputValueKeys;
        private readonly EntityCollectionStore? _entityCollections;
        private readonly Dictionary<string, GraphOutputSchema> _pendingOutputSchemas = new(StringComparer.OrdinalIgnoreCase);

        public GraphProgramConfigLoader(
            ConfigPipeline pipeline,
            GraphProgramRegistry registry,
            IGraphSymbolResolver symbolResolver,
            GraphOutputSchemaRegistry? outputSchemas = null,
            StringIntRegistry? outputValueKeys = null,
            EntityCollectionStore? entityCollections = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
            _outputSchemas = outputSchemas;
            _outputValueKeys = outputValueKeys;
            _entityCollections = entityCollections;
        }

        public List<GraphProgramPackage> LoadIdsAndCompile(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/graphs.json")
        {
            _registry.Clear();
            GraphIdRegistry.Clear();
            _pendingOutputSchemas.Clear();
            _outputSchemas?.Clear();

            return CompileMergedGraphs(
                catalog,
                report,
                relativePath,
                GraphIdBindingMode.RegisterNew,
                errorNoun: "compilation");
        }

        public void PatchAndRegister(IReadOnlyList<GraphProgramPackage> packages)
        {
            for (int i = 0; i < packages.Count; i++)
            {
                var (name, symbols, program, kind) = packages[i];
                GraphProgramSymbolPatcher.Patch(symbols, program, _symbolResolver, _entityCollections);
                int id = GraphIdRegistry.GetId(name);
                if (id <= 0) id = GraphIdRegistry.Register(name);
                ApplyProgram(id, name, program, kind, replaceExisting: false);
            }

            GraphIdRegistry.Freeze();
        }

        /// <summary>
        /// Recompile GAS/graphs.json and replace programs for already-registered graph ids.
        /// Does not clear or renumber <see cref="GraphIdRegistry"/> (safe after Freeze).
        /// New graph ids in the file are rejected fail-closed.
        /// </summary>
        public void ReloadExistingAndReplace(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/graphs.json")
        {
            if (!GraphIdRegistry.IsFrozen)
            {
                throw new InvalidOperationException(
                    "ReloadExistingAndReplace requires GraphIdRegistry to be frozen after boot registration.");
            }

            _pendingOutputSchemas.Clear();
            List<GraphProgramPackage> packages = CompileMergedGraphs(
                catalog,
                report,
                relativePath,
                GraphIdBindingMode.RequireExisting,
                errorNoun: "reload");

            for (int i = 0; i < packages.Count; i++)
            {
                var (name, symbols, program, kind) = packages[i];
                GraphProgramSymbolPatcher.Patch(symbols, program, _symbolResolver, _entityCollections);
                int graphId = GraphIdRegistry.GetId(name);
                if (graphId == GraphIdRegistry.InvalidId)
                {
                    throw new InvalidOperationException(
                        $"Graph '{name}' disappeared from GraphIdRegistry during reload.");
                }

                ApplyProgram(graphId, name, program, kind, replaceExisting: true);
            }
        }

        private enum GraphIdBindingMode
        {
            RegisterNew,
            RequireExisting
        }

        private List<GraphProgramPackage> CompileMergedGraphs(
            ConfigCatalog catalog,
            ConfigConflictReport report,
            string relativePath,
            GraphIdBindingMode idMode,
            string errorNoun)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            var sorted = new List<(string Id, JsonObject Node)>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
                sorted.Add((merged[i].Id, merged[i].Node));
            sorted.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            var packages = new List<GraphProgramPackage>(sorted.Count);
            var errors = new List<string>();

            for (int i = 0; i < sorted.Count; i++)
            {
                var (id, obj) = sorted[i];
                try
                {
                    if (idMode == GraphIdBindingMode.RegisterNew)
                    {
                        GraphIdRegistry.Register(id);
                    }
                    else
                    {
                        int graphId = GraphIdRegistry.GetId(id);
                        if (graphId == GraphIdRegistry.InvalidId)
                        {
                            throw new InvalidOperationException(
                                $"Graph '{id}' is not registered; hot reload cannot introduce new graph ids.");
                        }
                    }

                    GraphProgramPackage? pkg;
                    GraphOutputSchema outputSchema;
                    List<GraphDiagnostic> diags;
                    if (IsControlFlowGraphObject(obj))
                    {
                        if (HasLegacyNextChain(obj))
                        {
                            throw new InvalidOperationException(
                                "ControlFlow graph JSON cannot mix controlEdges/valueEdges with nodes[].next.");
                        }

                        GraphControlFlowDocument? doc;
                        try
                        {
                            doc = obj.Deserialize<GraphControlFlowDocument>(options);
                        }
                        catch (JsonException ex)
                        {
                            throw new InvalidOperationException(
                                $"Strict JSON rejected ControlFlow graph '{id}' in '{relativePath}': {ex.Message}",
                                ex);
                        }

                        if (doc == null) throw new InvalidOperationException("Failed to deserialize ControlFlow graph config.");
                        if (string.IsNullOrWhiteSpace(doc.Id)) doc.Id = id;
                        if (!string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Graph id mismatch: '{id}' vs '{doc.Id}'.");

                        (pkg, outputSchema, diags) = GraphControlFlowCompiler.CompileWithOutputs(doc);
                    }
                    else
                    {
                        GraphConfig? cfg;
                        try
                        {
                            cfg = obj.Deserialize<GraphConfig>(options);
                        }
                        catch (JsonException ex)
                        {
                            throw new InvalidOperationException(
                                $"Strict JSON rejected graph '{id}' in '{relativePath}': {ex.Message}",
                                ex);
                        }

                        if (cfg == null) throw new InvalidOperationException("Failed to deserialize graph config.");
                        if (string.IsNullOrWhiteSpace(cfg.Id)) cfg.Id = id;
                        if (!string.Equals(cfg.Id, id, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Graph id mismatch: '{id}' vs '{cfg.Id}'.");

                        (pkg, outputSchema, diags) = GraphCompiler.CompileWithOutputs(cfg);
                    }

                    for (int d = 0; d < diags.Count; d++)
                    {
                        if (diags[d].Severity == GraphDiagnosticSeverity.Error)
                        {
                            errors.Add($"Graph '{id}' in '{relativePath}': {diags[d].Code} {diags[d].Message}");
                        }
                    }

                    if (pkg.HasValue)
                    {
                        packages.Add(pkg.Value);
                        _pendingOutputSchemas[id] = outputSchema;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Graph '{id}' in '{relativePath}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[GraphProgramConfigLoader] {errors.Count} graph {errorNoun} error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }

            return packages;
        }

        private void ApplyProgram(
            int graphId,
            string name,
            GraphInstruction[] program,
            GraphKind kind,
            bool replaceExisting)
        {
            if (kind == GraphKind.None)
            {
                throw new InvalidOperationException(
                    $"Graph '{name}' (id={graphId}) cannot be {(replaceExisting ? "replaced" : "registered")} without an authored kind.");
            }

            GraphKindOperationPolicy.RequireAllowed(
                kind,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId,
                nameof(GraphProgramConfigLoader));

            if (replaceExisting)
            {
                _registry.Replace(graphId, program, kind);
            }
            else
            {
                _registry.Register(graphId, program, kind);
            }

            if (_outputSchemas != null)
            {
                GraphOutputSchema schema = _pendingOutputSchemas.TryGetValue(name, out GraphOutputSchema pendingSchema)
                    ? ResolveOutputValueKeys(pendingSchema)
                    : GraphOutputSchema.Empty;
                _outputSchemas.Register(graphId, schema);
            }
        }

        private static bool IsControlFlowGraphObject(JsonObject obj)
            => obj.ContainsKey("controlEdges") || obj.ContainsKey("valueEdges");

        private static bool HasLegacyNextChain(JsonObject obj)
        {
            if (obj["nodes"] is not JsonArray nodes)
            {
                return false;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is JsonObject node && node.ContainsKey("next"))
                {
                    return true;
                }
            }

            return false;
        }

        private GraphOutputSchema ResolveOutputValueKeys(GraphOutputSchema schema)
        {
            if (!schema.HasBindings)
            {
                return schema;
            }

            GraphOutputBinding[] source = schema.Bindings;
            GraphOutputBinding[] resolved = new GraphOutputBinding[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                GraphOutputBinding binding = source[i];
                if (binding.Destination != GraphOutputDestinationKind.Summary)
                {
                    resolved[i] = binding;
                    continue;
                }

                if (_outputValueKeys == null)
                {
                    throw new InvalidOperationException(
                        $"Graph summary output '{binding.Id}' requires a GraphOutputValueKeyRegistry.");
                }

                if (string.IsNullOrWhiteSpace(binding.Key))
                {
                    throw new InvalidOperationException(
                        $"Graph summary output '{binding.Id}' requires a key.");
                }

                resolved[i] = binding.WithResolvedKeyId(_outputValueKeys.Register(binding.Key));
            }

            return new GraphOutputSchema(resolved);
        }
    }
}
