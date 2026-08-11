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
                    GraphIdRegistry.Register(id);
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
                    $"[GraphProgramConfigLoader] {errors.Count} graph compilation error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }

            return packages;
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

        public void PatchAndRegister(IReadOnlyList<GraphProgramPackage> packages)
        {
            for (int i = 0; i < packages.Count; i++)
            {
                var (name, symbols, program, kind) = packages[i];
                GraphProgramSymbolPatcher.Patch(symbols, program, _symbolResolver, _entityCollections);
                int id = GraphIdRegistry.GetId(name);
                if (id <= 0) id = GraphIdRegistry.Register(name);
                if (kind == GraphKind.None)
                {
                    throw new InvalidOperationException(
                        $"Graph '{name}' (id={id}) cannot be registered without an authored kind.");
                }

                GraphKindOperationPolicy.RequireAllowed(
                    kind,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    id,
                    nameof(GraphProgramConfigLoader));

                _registry.Register(id, program, kind);
                if (_outputSchemas != null)
                {
                    GraphOutputSchema schema = _pendingOutputSchemas.TryGetValue(name, out GraphOutputSchema pendingSchema)
                        ? ResolveOutputValueKeys(pendingSchema)
                        : GraphOutputSchema.Empty;
                    _outputSchemas.Register(id, schema);
                }
            }

            GraphIdRegistry.Freeze();
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
