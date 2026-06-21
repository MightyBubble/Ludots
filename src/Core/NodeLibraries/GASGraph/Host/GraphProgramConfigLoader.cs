using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Gameplay.GAS.Registry;
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

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var packages = new List<GraphProgramPackage>(sorted.Count);
            var errors = new List<string>();

            for (int i = 0; i < sorted.Count; i++)
            {
                var (id, obj) = sorted[i];
                try
                {
                    var cfg = obj.Deserialize<GraphConfig>(options);
                    if (cfg == null) throw new InvalidOperationException("Failed to deserialize graph config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id)) cfg.Id = id;
                    if (!string.Equals(cfg.Id, id, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Graph id mismatch: '{id}' vs '{cfg.Id}'.");

                    GraphIdRegistry.Register(id);
                    var (pkg, outputSchema, diags) = GraphCompiler.CompileWithOutputs(cfg);
                    for (int d = 0; d < diags.Count; d++)
                    {
                        if (diags[d].Severity == GraphDiagnosticSeverity.Error)
                        {
                            errors.Add($"Graph '{id}': {diags[d].Code} {diags[d].Message}");
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
                    errors.Add($"Graph '{id}': {ex.Message}");
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
                var (name, symbols, program) = packages[i];
                PatchSymbols(symbols, program);
                int id = GraphIdRegistry.GetId(name);
                if (id <= 0) id = GraphIdRegistry.Register(name);
                _registry.Register(id, program);
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

        private void PatchSymbols(string[] symbols, GraphInstruction[] program)
        {
            if (symbols == null || symbols.Length == 0) return;
            if (program == null || program.Length == 0) return;

            for (int i = 0; i < program.Length; i++)
            {
                ref var ins = ref program[i];
                var op = (GraphNodeOp)ins.Op;
                switch (op)
                {
                    case GraphNodeOp.QueryFilterTagAny:
                    case GraphNodeOp.QueryFilterTagNone:
                    case GraphNodeOp.SendEvent:
                    case GraphNodeOp.HasTag:
                        ins.Imm = _symbolResolver.ResolveTag(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.LoadAttribute:
                    case GraphNodeOp.ModifyAttributeAdd:
                    case GraphNodeOp.QueryFilterAttributeRange:
                    case GraphNodeOp.QuerySortByAttribute:
                    case GraphNodeOp.AggSumAttribute:
                    case GraphNodeOp.AggAverageAttribute:
                    case GraphNodeOp.AggMaxAttribute:
                    case GraphNodeOp.AggMinAttribute:
                    case GraphNodeOp.AggMaxEntityByAttribute:
                    case GraphNodeOp.AggMinEntityByAttribute:
                        ins.Imm = _symbolResolver.ResolveAttribute(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.QueryFilterTemplate:
                        ins.Imm = _symbolResolver.ResolveEntityTemplate(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.QueryFromCollection:
                        ins.Imm = ResolveEntityCollectionKey(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ApplyEffectTemplate:
                    case GraphNodeOp.FanOutApplyEffect:
                    case GraphNodeOp.RemoveEffectTemplate:
                        ins.Imm = _symbolResolver.ResolveEffectTemplate(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.FanOutDispatchEffect:
                        ins.Imm = _symbolResolver.ResolveEffectTemplate(ResolveSymbol(symbols, ins.Imm));
                        ins.Dst = checked((byte)_symbolResolver.ResolveTargetDispatchPreset(ResolveSymbol(symbols, ins.Dst)));
                        break;
                    case GraphNodeOp.FanOutDispatchEffectDynamic:
                        ins.Dst = checked((byte)_symbolResolver.ResolveTargetDispatchPreset(ResolveSymbol(symbols, ins.Dst)));
                        break;
                case GraphNodeOp.ReadBlackboardFloat:
                case GraphNodeOp.ReadBlackboardInt:
                case GraphNodeOp.ReadBlackboardEntity:
                case GraphNodeOp.WriteBlackboardFloat:
                    case GraphNodeOp.WriteBlackboardInt:
                    case GraphNodeOp.WriteBlackboardEntity:
                    case GraphNodeOp.LoadConfigFloat:
                    case GraphNodeOp.LoadConfigInt:
                    case GraphNodeOp.LoadConfigEffectId:
                        ins.Imm = ConfigKeyRegistry.Register(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.RelationshipSetMetric:
                    case GraphNodeOp.RelationshipAddMetric:
                    case GraphNodeOp.RelationshipGetMetric:
                    case GraphNodeOp.RelationshipAggSumMetric:
                    case GraphNodeOp.RelationshipAggMaxMetric:
                    case GraphNodeOp.RelationshipAggAverageMetric:
                    case GraphNodeOp.RelationshipAggMinMetric:
                    case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                    case GraphNodeOp.RelationshipAggMinEntityByMetric:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = _symbolResolver.ResolveRelationshipMetric(ResolveSymbol(symbols, ins.Imm));
                        }

                        if ((op == GraphNodeOp.RelationshipSetMetric || op == GraphNodeOp.RelationshipAddMetric) &&
                            ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)_symbolResolver.ResolveRelationshipReason(ResolveSymbol(symbols, ins.Dst)));
                        }

                        if ((op == GraphNodeOp.RelationshipSetMetric ||
                             op == GraphNodeOp.RelationshipAddMetric ||
                             op == GraphNodeOp.RelationshipGetMetric ||
                             op == GraphNodeOp.RelationshipAggSumMetric ||
                             op == GraphNodeOp.RelationshipAggMaxMetric ||
                             op == GraphNodeOp.RelationshipAggAverageMetric ||
                             op == GraphNodeOp.RelationshipAggMinMetric ||
                             op == GraphNodeOp.RelationshipAggMaxEntityByMetric ||
                             op == GraphNodeOp.RelationshipAggMinEntityByMetric) &&
                            ins.Flags != byte.MaxValue)
                        {
                            ins.Flags = checked((byte)_symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Flags)));
                        }
                        break;
                    case GraphNodeOp.RelationshipFilterMetricRange:
                    case GraphNodeOp.RelationshipSortByMetric:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = _symbolResolver.ResolveRelationshipMetric(ResolveSymbol(symbols, ins.Imm));
                        }

                        if (ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)_symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Dst)));
                        }
                        break;
                    case GraphNodeOp.RelationshipHasFlag:
                    case GraphNodeOp.RelationshipSetFlag:
                    case GraphNodeOp.RelationshipFilterFlag:
                        if (ins.Imm >= 0)
                        {
                            ins.Imm = _symbolResolver.ResolveRelationshipFlag(ResolveSymbol(symbols, ins.Imm));
                        }

                        if (op == GraphNodeOp.RelationshipSetFlag && ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)_symbolResolver.ResolveRelationshipReason(ResolveSymbol(symbols, ins.Dst)));
                        }

                        if (op == GraphNodeOp.RelationshipSetFlag || op == GraphNodeOp.RelationshipHasFlag)
                        {
                            if (ins.Flags != byte.MaxValue)
                            {
                                ins.Flags = checked((byte)_symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Flags)));
                            }
                        }
                        else if (ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)_symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Dst)));
                        }
                        break;
                    case GraphNodeOp.RelationshipEnsureLink:
                    case GraphNodeOp.RelationshipRemoveLink:
                    case GraphNodeOp.RelationshipQueryOutgoing:
                    case GraphNodeOp.RelationshipQueryIncoming:
                    case GraphNodeOp.RelationshipQueryMutual:
                    case GraphNodeOp.RelationshipQueryBetweenPair:
                        if (ins.Dst != byte.MaxValue)
                        {
                            ins.Dst = checked((byte)_symbolResolver.ResolveRelationshipType(ResolveSymbol(symbols, ins.Dst)));
                        }
                        break;
                }
            }
        }

        private static string ResolveSymbol(string[] symbols, int symbolIndex)
        {
            if ((uint)symbolIndex >= (uint)symbols.Length)
            {
                throw new InvalidOperationException($"Graph symbol index out of range: {symbolIndex} (len={symbols.Length}).");
            }
            return symbols[symbolIndex] ?? string.Empty;
        }

        private int ResolveEntityCollectionKey(string key)
        {
            if (_entityCollections == null)
            {
                throw new InvalidOperationException(
                    $"Graph collection query key '{key}' requires an EntityCollectionStore.");
            }

            return _entityCollections.KeyRegistry.Register(key);
        }
    }
}
