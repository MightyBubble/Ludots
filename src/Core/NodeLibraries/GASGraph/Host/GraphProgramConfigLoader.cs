using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
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
        private readonly GasGraphOpRegistry? _opRegistry;
        private readonly BuiltinHandlerRegistry? _builtinHandlers;
        private readonly Ludots.Core.Scripting.EventSchemaRegistry? _eventSchemas;
        private readonly Ludots.Core.Scripting.EnumCatalog? _enums;
        private readonly Dictionary<string, GraphOutputSchema> _pendingOutputSchemas = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GraphInstructionSourceMap> _pendingSourceMaps = new(StringComparer.OrdinalIgnoreCase);
        // #1124 hook weaving source: the authored documents in compile order, consumed by
        // the weave pass after registration (WeaveHooks) and cleared afterwards.
        private readonly List<KeyValuePair<string, GraphControlFlowDocument>> _pendingDocuments = new();

        public GraphProgramConfigLoader(
            ConfigPipeline pipeline,
            GraphProgramRegistry registry,
            IGraphSymbolResolver symbolResolver,
            GraphOutputSchemaRegistry? outputSchemas = null,
            StringIntRegistry? outputValueKeys = null,
            EntityCollectionStore? entityCollections = null,
            GasGraphOpRegistry? opRegistry = null,
            BuiltinHandlerRegistry? builtinHandlers = null,
            Ludots.Core.Scripting.EventSchemaRegistry? eventSchemas = null,
            Ludots.Core.Scripting.EnumCatalog? enums = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
            _outputSchemas = outputSchemas;
            _outputValueKeys = outputValueKeys;
            _entityCollections = entityCollections;
            _opRegistry = opRegistry;
            _builtinHandlers = builtinHandlers;
            _eventSchemas = eventSchemas;
            _enums = enums;
        }

        public List<GraphProgramPackage> LoadIdsAndCompile(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/graphs.json")
        {
            _registry.Clear();
            ModRegistryAmbient.Current.RequireGraphIdsEmptyAndUnfrozen();
            _pendingOutputSchemas.Clear();
            _pendingSourceMaps.Clear();
            _pendingDocuments.Clear();
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
            var documents = new Dictionary<string, GraphControlFlowDocument>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sorted.Count; i++)
            {
                var (id, obj) = sorted[i];
                try
                {
                    GraphIdRegistry.Register(id);
                    GraphKind kind = GraphProgramAuthoringFrontDoor.RequireKind(obj, id);
                    GraphProgramAuthoringFrontDoor.RequireControlFlowAuthoringShape(obj, id, kind);
                    GraphProgramAuthoringFrontDoor.RequireTriggerGraphEntryShape(obj, id, kind);
                    GraphControlFlowDocument? doc = obj.Deserialize<GraphControlFlowDocument>(options);
                    if (doc == null)
                    {
                        throw new InvalidOperationException($"Failed to deserialize ControlFlow graph '{id}'.");
                    }

                    if (string.IsNullOrWhiteSpace(doc.Id))
                    {
                        doc.Id = id;
                    }

                    if (!string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Graph id mismatch: '{id}' vs '{doc.Id}'.");
                    }

                    if (string.IsNullOrWhiteSpace(doc.Kind))
                    {
                        doc.Kind = kind.ToString();
                    }

                    documents[id] = doc;
                }
                catch (Exception ex)
                {
                    errors.Add($"Graph '{id}' in '{relativePath}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[GraphProgramConfigLoader] {errors.Count} graph deserialization error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }

            try
            {
                TriggerGraphInlineWeaver.ExpandDocuments(documents);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[GraphProgramConfigLoader] InlineGraph expand failed in '{relativePath}': {ex.Message}",
                    ex);
            }

            foreach (KeyValuePair<string, GraphControlFlowDocument> pair in documents)
            {
                string id = pair.Key;
                try
                {
                    GraphControlFlowCompileResult compiled =
                        GraphControlFlowCompiler.Compile(pair.Value, _eventSchemas, _enums);
                    GraphProgramPackage? pkg = compiled.Package;
                    GraphOutputSchema outputSchema = compiled.OutputSchema;
                    List<GraphDiagnostic> diags = compiled.Diagnostics;
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
                        _pendingSourceMaps[id] = compiled.SourceMap;
                        _pendingDocuments.Add(new KeyValuePair<string, GraphControlFlowDocument>(id, pair.Value));
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

        public void PatchAndRegister(IReadOnlyList<GraphProgramPackage> packages)
        {
            for (int i = 0; i < packages.Count; i++)
            {
                var (name, symbols, program, kind, mapTriggerEntries) = packages[i];
                GraphProgramSymbolPatcher.Patch(symbols, program, _symbolResolver, _entityCollections, _builtinHandlers);
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

                GraphInstructionSourceMap sourceMap = _pendingSourceMaps.TryGetValue(name, out GraphInstructionSourceMap pendingMap)
                    ? pendingMap
                    : GraphInstructionSourceMap.Empty;
                _registry.Register(id, program, kind, sourceMap, symbols, mapTriggerEntries);
                if (_outputSchemas != null)
                {
                    GraphOutputSchema schema = _pendingOutputSchemas.TryGetValue(name, out GraphOutputSchema pendingSchema)
                        ? ResolveOutputBindingKeys(pendingSchema)
                        : GraphOutputSchema.Empty;
                    _outputSchemas.Register(id, schema);
                }
            }

            GraphIdRegistry.Freeze();
            WeaveHooks();
        }

        /// <summary>
        /// #1124 Route A weave pass: runs once every graph is registered (and ids are
        /// frozen), before any map mounts. Hook-bearing TriggerGraph entries are spliced
        /// into their targets and the merged programs land via ReplaceProgram, which
        /// re-validates op policy, invoke targets, and cycles with rollback on failure.
        /// </summary>
        private void WeaveHooks()
        {
            TriggerGraphHookWeaver.Weave(
                _registry,
                _pendingDocuments,
                _symbolResolver,
                _eventSchemas,
                _entityCollections,
                _builtinHandlers,
                _enums);
            _pendingDocuments.Clear();
        }

        /// <summary>
        /// After <see cref="PatchAndRegister"/> and Func Lib load, resolve InvokeScript functionName symbols.
        /// </summary>
        public void ResolveFuncLibInvokes(IReadOnlyList<GraphProgramPackage> packages, GraphFunctionCatalog catalog)
        {
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            for (int i = 0; i < packages.Count; i++)
            {
                var (_, symbols, program, _, _) = packages[i];
                GraphProgramSymbolPatcher.PatchFuncLib(symbols, program, catalog);
            }

            _registry.ValidateInvokeTargets();
        }

        private GraphOutputSchema ResolveOutputBindingKeys(GraphOutputSchema schema)
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
                if (binding.Destination == GraphOutputDestinationKind.EntityCollection)
                {
                    resolved[i] = _entityCollections != null && !string.IsNullOrWhiteSpace(binding.CollectionKey)
                        ? binding.WithResolvedCollectionKeyId(_entityCollections.KeyRegistry.Register(binding.CollectionKey))
                        : binding;
                    continue;
                }

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
