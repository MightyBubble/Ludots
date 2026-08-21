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
        private readonly Dictionary<string, GraphOutputSchema> _pendingOutputSchemas = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GraphInstructionSourceMap> _pendingSourceMaps = new(StringComparer.OrdinalIgnoreCase);

        public GraphProgramConfigLoader(
            ConfigPipeline pipeline,
            GraphProgramRegistry registry,
            IGraphSymbolResolver symbolResolver,
            GraphOutputSchemaRegistry? outputSchemas = null,
            StringIntRegistry? outputValueKeys = null,
            EntityCollectionStore? entityCollections = null,
            GasGraphOpRegistry? opRegistry = null,
            BuiltinHandlerRegistry? builtinHandlers = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
            _outputSchemas = outputSchemas;
            _outputValueKeys = outputValueKeys;
            _entityCollections = entityCollections;
            _opRegistry = opRegistry;
            _builtinHandlers = builtinHandlers;
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
                    GraphControlFlowCompileResult compiled =
                        GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, id, options);
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
