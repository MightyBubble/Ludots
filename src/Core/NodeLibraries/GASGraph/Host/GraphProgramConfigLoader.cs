using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GraphProgramConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly GraphProgramRegistry _registry;
        private readonly IGraphSymbolResolver _symbolResolver;

        public GraphProgramConfigLoader(ConfigPipeline pipeline, GraphProgramRegistry registry, IGraphSymbolResolver symbolResolver)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
        }

        public List<GraphProgramPackage> LoadIdsAndCompile(string relativePath = "GAS/graphs.json")
        {
            _registry.Clear();
            GraphIdRegistry.Clear();

            var arrays = _pipeline.CollectJsonArrays(relativePath);
            var mergedNodes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var array in arrays)
            {
                foreach (var node in array)
                {
                    if (node is not JsonObject obj) continue;
                    string id = obj["id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    if (mergedNodes.TryGetValue(id, out var existing))
                    {
                        JsonMerger.Merge(existing, node);
                    }
                    else
                    {
                        mergedNodes[id] = node.DeepClone();
                    }
                }
            }

            var merged = new List<(string Id, JsonObject Node)>(mergedNodes.Count);
            foreach (var kvp in mergedNodes)
            {
                if (kvp.Value is not JsonObject obj) continue;
                merged.Add((kvp.Key, obj));
            }
            merged.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var packages = new List<GraphProgramPackage>(merged.Count);
            var errors = new List<string>();

            for (int i = 0; i < merged.Count; i++)
            {
                var (id, obj) = merged[i];
                try
                {
                    var cfg = obj.Deserialize<GraphConfig>(options);
                    if (cfg == null) throw new InvalidOperationException("Failed to deserialize graph config.");
                    if (string.IsNullOrWhiteSpace(cfg.Id)) cfg.Id = id;
                    if (!string.Equals(cfg.Id, id, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Graph id mismatch: '{id}' vs '{cfg.Id}'.");

                    GraphIdRegistry.Register(id);
                    var (pkg, diags) = GraphCompiler.Compile(cfg);
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
            }

            GraphIdRegistry.Freeze();
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
                    case GraphNodeOp.QueryFilterTagAll:
                    case GraphNodeOp.SendEvent:
                        ins.Imm = _symbolResolver.ResolveTag(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.LoadAttribute:
                    case GraphNodeOp.ModifyAttributeAdd:
                        ins.Imm = _symbolResolver.ResolveAttribute(ResolveSymbol(symbols, ins.Imm));
                        break;
                    case GraphNodeOp.ApplyEffectTemplate:
                        ins.Imm = _symbolResolver.ResolveEffectTemplate(ResolveSymbol(symbols, ins.Imm));
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
    }
}
