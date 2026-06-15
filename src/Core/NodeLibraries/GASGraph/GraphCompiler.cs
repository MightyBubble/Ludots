using System;
using System.Collections.Generic;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphCompiler
    {
        public static (GraphProgramPackage? Package, List<GraphDiagnostic> Diagnostics) Compile(GraphConfig cfg)
        {
            var (package, _, diagnostics) = CompileWithOutputs(cfg);
            return (package, diagnostics);
        }

        public static (GraphProgramPackage? Package, GraphOutputSchema OutputSchema, List<GraphDiagnostic> Diagnostics) CompileWithOutputs(GraphConfig cfg)
        {
            var diagnostics = GraphValidator.Validate(cfg);
            if (HasErrors(diagnostics))
            {
                return (null, GraphOutputSchema.Empty, diagnostics);
            }

            var nodesById = new Dictionary<string, GraphNodeConfig>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cfg.Nodes.Count; i++)
            {
                var n = cfg.Nodes[i];
                if (n == null) continue;
                if (string.IsNullOrWhiteSpace(n.Id)) continue;
                nodesById[n.Id] = n;
            }

            var ordered = new List<GraphNodeConfig>(cfg.Nodes.Count);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = cfg.Entry;
            while (!string.IsNullOrWhiteSpace(current) && nodesById.TryGetValue(current, out var node))
            {
                if (!visited.Add(current))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.NextCycle, $"Cycle detected in Next chain at node '{current}'.", cfg.Id, current));
                    return (null, GraphOutputSchema.Empty, diagnostics);
                }
                ordered.Add(node);
                current = node.Next ?? string.Empty;
            }

            var symbolToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var symbols = new List<string>();

            int floatNext = 0;
            int intNext = 0;
            int boolNext = 0;
            int entityNext = 2;

            var valueMap = new Dictionary<string, (GraphValueType Type, byte Reg)>(StringComparer.OrdinalIgnoreCase);
            var instructions = new List<GraphInstruction>(ordered.Count);

            for (int idx = 0; idx < ordered.Count; idx++)
            {
                var node = ordered[idx];
                if (!GraphNodeOpParser.TryParse(node.Op, out var op))
                {
                    diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.UnknownNodeOp, $"Unknown node op '{node.Op}'.", cfg.Id, node.Id));
                    continue;
                }

                var (outType, fixedReg) = GetOutputTypeAndFixedReg(op);
                byte dstReg = 0;
                if (outType != GraphValueType.Void)
                {
                    if (fixedReg.HasValue)
                    {
                        dstReg = fixedReg.Value;
                    }
                    else
                    {
                        dstReg = outType switch
                        {
                            GraphValueType.Float => Alloc(ref floatNext, GraphVmLimits.MaxFloatRegisters, cfg.Id, node.Id, diagnostics),
                            GraphValueType.Int => Alloc(ref intNext, GraphVmLimits.MaxIntRegisters, cfg.Id, node.Id, diagnostics),
                            GraphValueType.Bool => Alloc(ref boolNext, GraphVmLimits.MaxBoolRegisters, cfg.Id, node.Id, diagnostics),
                            GraphValueType.Entity => Alloc(ref entityNext, GraphVmLimits.MaxEntityRegisters, cfg.Id, node.Id, diagnostics),
                            _ => 0
                        };
                    }
                    valueMap[node.Id] = (outType, dstReg);
                }

                if (HasErrors(diagnostics)) return (null, GraphOutputSchema.Empty, diagnostics);

                var ins = new GraphInstruction { Op = (ushort)op, Dst = dstReg };

                switch (op)
                {
                    case GraphNodeOp.ConstBool:
                        ins.Imm = node.BoolValue ? 1 : 0;
                        break;
                    case GraphNodeOp.ConstInt:
                        ins.Imm = node.IntValue;
                        break;
                    case GraphNodeOp.ConstFloat:
                        ins.ImmF = node.FloatValue;
                        break;
                    case GraphNodeOp.LoadCaster:
                    case GraphNodeOp.LoadExplicitTarget:
                    case GraphNodeOp.LoadContextSource:
                    case GraphNodeOp.LoadContextTarget:
                    case GraphNodeOp.LoadContextTargetContext:
                    case GraphNodeOp.RandomFloat01:
                        break;
                    case GraphNodeOp.Jump:
                        ins.Imm = node.IntValue;
                        break;
                    case GraphNodeOp.JumpIfFalse:
                        ins.A = RequireInput(node, 0, GraphValueType.Bool, valueMap, cfg.Id, diagnostics);
                        ins.Imm = node.IntValue;
                        break;
                    case GraphNodeOp.LoadAttribute:
                        ins.A = node.Inputs.Count > 0
                            ? RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics)
                            : (byte)0;
                        ins.Imm = Intern(symbolToIndex, symbols, node.Attribute);
                        break;
                    case GraphNodeOp.AddFloat:
                    case GraphNodeOp.MulFloat:
                    case GraphNodeOp.SubFloat:
                    case GraphNodeOp.DivFloat:
                    case GraphNodeOp.MinFloat:
                    case GraphNodeOp.MaxFloat:
                    case GraphNodeOp.CompareGtFloat:
                        ins.A = RequireInput(node, 0, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        break;
                    case GraphNodeOp.ClampFloat:
                        ins.A = RequireInput(node, 0, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 2, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        break;
                    case GraphNodeOp.AbsFloat:
                    case GraphNodeOp.NegFloat:
                        ins.A = RequireInput(node, 0, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        break;
                    case GraphNodeOp.SelectEntity:
                        ins.A = RequireInput(node, 0, GraphValueType.Bool, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 2, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        break;
                    case GraphNodeOp.QueryRadius:
                        ins.ImmF = node.Radius;
                        break;
                    case GraphNodeOp.QueryFilterTagAll:
                        ins.Imm = Intern(symbolToIndex, symbols, node.Tag);
                        break;
                    case GraphNodeOp.QuerySortStable:
                        break;
                    case GraphNodeOp.QueryLimit:
                        ins.Imm = node.Limit > 0 ? node.Limit : node.IntValue;
                        break;
                    case GraphNodeOp.AggCount:
                    case GraphNodeOp.AggMinByDistance:
                        break;
                    case GraphNodeOp.ApplyEffectTemplate:
                        ins.A = node.Inputs.Count > 0
                            ? RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics)
                            : (byte)1;
                        ins.Imm = Intern(symbolToIndex, symbols, node.EffectTemplate);
                        if (node.Inputs.Count > 3)
                        {
                            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.BudgetExceeded, "ApplyEffectTemplate supports up to 2 float args.", cfg.Id, node.Id));
                            break;
                        }
                        byte floatCount = 0;
                        if (node.Inputs.Count > 1)
                        {
                            ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                            floatCount = 1;
                        }
                        if (node.Inputs.Count > 2)
                        {
                            ins.C = RequireInput(node, 2, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                            floatCount = 2;
                        }
                        ins.Flags = floatCount;
                        break;
                    case GraphNodeOp.ApplyEffectDynamic:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Int, valueMap, cfg.Id, diagnostics);
                        break;
                    case GraphNodeOp.FanOutApplyEffect:
                        ins.Imm = Intern(symbolToIndex, symbols, node.EffectTemplate);
                        break;
                    case GraphNodeOp.FanOutApplyEffectDynamic:
                        ins.A = RequireInput(node, 0, GraphValueType.Int, valueMap, cfg.Id, diagnostics);
                        break;
                    case GraphNodeOp.FanOutDispatchEffect:
                        ins.Imm = Intern(symbolToIndex, symbols, node.EffectTemplate);
                        ins.Dst = RequirePayloadPresetSymbol(node.PayloadPreset, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.FanOutDispatchEffectDynamic:
                        ins.A = RequireInput(node, 0, GraphValueType.Int, valueMap, cfg.Id, diagnostics);
                        ins.Dst = RequirePayloadPresetSymbol(node.PayloadPreset, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RemoveEffectTemplate:
                        ins.A = node.Inputs.Count > 0
                            ? RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics)
                            : (byte)1;
                        ins.Imm = Intern(symbolToIndex, symbols, node.EffectTemplate);
                        break;
                    case GraphNodeOp.HasTag:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Tag);
                        break;
                    case GraphNodeOp.ModifyAttributeAdd:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Attribute);
                        break;
                    case GraphNodeOp.SendEvent:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Tag);
                        break;
                    case GraphNodeOp.RelationshipEnsureLink:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipRemoveLink:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipSetMetric:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 2, GraphValueType.Int, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Dst = node.Reason == null ? byte.MaxValue : checked((byte)Intern(symbolToIndex, symbols, node.Reason));
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipAddMetric:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 2, GraphValueType.Int, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Dst = node.Reason == null ? byte.MaxValue : checked((byte)Intern(symbolToIndex, symbols, node.Reason));
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipGetMetric:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipHasFlag:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Flag);
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipSetFlag:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 2, GraphValueType.Bool, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Flag);
                        ins.Dst = node.Reason == null ? byte.MaxValue : checked((byte)Intern(symbolToIndex, symbols, node.Reason));
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipQueryOutgoing:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Dst = EncodeByteSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipQueryIncoming:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Dst = EncodeByteSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipQueryMutual:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Dst = EncodeByteSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipQueryBetweenPair:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Dst = EncodeByteSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipFilterMetricRange:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 2, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipFilterFlag:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        if (node.Inputs.Count > 1)
                        {
                            ins.B = RequireInput(node, 1, GraphValueType.Bool, valueMap, cfg.Id, diagnostics);
                            ins.Flags = 0;
                        }
                        else
                        {
                            ins.B = 0;
                            ins.Flags = 1;
                        }
                        ins.Imm = Intern(symbolToIndex, symbols, node.Flag);
                        ins.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.RelationshipSortByMetric:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        ins.Flags = node.Descending ? (byte)1 : (byte)0;
                        break;
                    case GraphNodeOp.RelationshipAggSumMetric:
                    case GraphNodeOp.RelationshipAggMaxMetric:
                    case GraphNodeOp.RelationshipAggAverageMetric:
                    case GraphNodeOp.RelationshipAggMinMetric:
                    case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                    case GraphNodeOp.RelationshipAggMinEntityByMetric:
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                    case GraphNodeOp.QueryAllMapEntities:
                        break;
                    case GraphNodeOp.QueryFromCollection:
                        ins.A = node.Inputs.Count > 0
                            ? RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics)
                            : (byte)0;
                        ins.Imm = Intern(symbolToIndex, symbols, node.CollectionKey);
                        break;
                    case GraphNodeOp.QueryFilterTeam:
                        if (node.Inputs.Count > 0)
                        {
                            ins.A = RequireInput(node, 0, GraphValueType.Int, valueMap, cfg.Id, diagnostics);
                            ins.Flags = 1;
                        }
                        else
                        {
                            ins.Imm = node.TeamId != 0 ? node.TeamId : node.IntValue;
                            ins.Flags = 0;
                        }
                        break;
                    case GraphNodeOp.QueryFilterTemplate:
                        ins.Imm = Intern(symbolToIndex, symbols, node.Template);
                        break;
                    case GraphNodeOp.QueryFilterAttributeRange:
                        ins.B = RequireInput(node, 0, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.C = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Attribute);
                        break;
                    case GraphNodeOp.QueryFilterTagAny:
                    case GraphNodeOp.QueryFilterTagNone:
                        ins.Imm = Intern(symbolToIndex, symbols, node.Tag);
                        break;
                    case GraphNodeOp.QuerySortByAttribute:
                        ins.Imm = Intern(symbolToIndex, symbols, node.Attribute);
                        ins.Flags = node.Descending ? (byte)1 : (byte)0;
                        break;
                    case GraphNodeOp.AggSumAttribute:
                    case GraphNodeOp.AggAverageAttribute:
                    case GraphNodeOp.AggMaxAttribute:
                    case GraphNodeOp.AggMinAttribute:
                    case GraphNodeOp.AggMaxEntityByAttribute:
                    case GraphNodeOp.AggMinEntityByAttribute:
                        ins.Imm = Intern(symbolToIndex, symbols, node.Attribute);
                        break;
                }

                instructions.Add(ins);
            }

            if (HasErrors(diagnostics))
            {
                return (null, GraphOutputSchema.Empty, diagnostics);
            }

            GraphOutputSchema outputSchema = CompileOutputSchema(cfg, valueMap, diagnostics);
            if (HasErrors(diagnostics))
            {
                return (null, GraphOutputSchema.Empty, diagnostics);
            }

            return (new GraphProgramPackage(cfg.Id, symbols.ToArray(), instructions.ToArray()), outputSchema, diagnostics);
        }

        private static (GraphValueType Type, byte? FixedReg) GetOutputTypeAndFixedReg(GraphNodeOp op)
        {
            return op switch
            {
                GraphNodeOp.ConstBool => (GraphValueType.Bool, null),
                GraphNodeOp.ConstInt => (GraphValueType.Int, null),
                GraphNodeOp.ConstFloat => (GraphValueType.Float, null),
                GraphNodeOp.LoadCaster => (GraphValueType.Entity, 0),
                GraphNodeOp.LoadExplicitTarget => (GraphValueType.Entity, 1),
                GraphNodeOp.LoadContextSource => (GraphValueType.Entity, null),
                GraphNodeOp.LoadContextTarget => (GraphValueType.Entity, null),
                GraphNodeOp.LoadContextTargetContext => (GraphValueType.Entity, null),
                GraphNodeOp.RandomFloat01 => (GraphValueType.Float, null),
                GraphNodeOp.LoadAttribute => (GraphValueType.Float, null),
                GraphNodeOp.AddFloat => (GraphValueType.Float, null),
                GraphNodeOp.MulFloat => (GraphValueType.Float, null),
                GraphNodeOp.SubFloat => (GraphValueType.Float, null),
                GraphNodeOp.DivFloat => (GraphValueType.Float, null),
                GraphNodeOp.MinFloat => (GraphValueType.Float, null),
                GraphNodeOp.MaxFloat => (GraphValueType.Float, null),
                GraphNodeOp.ClampFloat => (GraphValueType.Float, null),
                GraphNodeOp.AbsFloat => (GraphValueType.Float, null),
                GraphNodeOp.NegFloat => (GraphValueType.Float, null),
                GraphNodeOp.CompareGtFloat => (GraphValueType.Bool, null),
                GraphNodeOp.HasTag => (GraphValueType.Bool, null),
                GraphNodeOp.SelectEntity => (GraphValueType.Entity, null),
                GraphNodeOp.AggCount => (GraphValueType.Int, null),
                GraphNodeOp.AggMinByDistance => (GraphValueType.Entity, null),
                GraphNodeOp.RelationshipGetMetric => (GraphValueType.Int, null),
                GraphNodeOp.RelationshipHasFlag => (GraphValueType.Bool, null),
                GraphNodeOp.RelationshipAggSumMetric => (GraphValueType.Int, null),
                GraphNodeOp.RelationshipAggMaxMetric => (GraphValueType.Int, null),
                GraphNodeOp.RelationshipAggAverageMetric => (GraphValueType.Int, null),
                GraphNodeOp.RelationshipAggMinMetric => (GraphValueType.Int, null),
                GraphNodeOp.RelationshipAggMaxEntityByMetric => (GraphValueType.Entity, null),
                GraphNodeOp.RelationshipAggMinEntityByMetric => (GraphValueType.Entity, null),
                GraphNodeOp.AggSumAttribute => (GraphValueType.Float, null),
                GraphNodeOp.AggAverageAttribute => (GraphValueType.Float, null),
                GraphNodeOp.AggMaxAttribute => (GraphValueType.Float, null),
                GraphNodeOp.AggMinAttribute => (GraphValueType.Float, null),
                GraphNodeOp.AggMaxEntityByAttribute => (GraphValueType.Entity, null),
                GraphNodeOp.AggMinEntityByAttribute => (GraphValueType.Entity, null),
                _ => (GraphValueType.Void, null)
            };
        }

        private static GraphOutputSchema CompileOutputSchema(
            GraphConfig cfg,
            Dictionary<string, (GraphValueType Type, byte Reg)> valueMap,
            List<GraphDiagnostic> diagnostics)
        {
            if (cfg.Outputs == null || cfg.Outputs.Count == 0)
            {
                return GraphOutputSchema.Empty;
            }

            var bindings = new List<GraphOutputBinding>(cfg.Outputs.Count);
            for (int i = 0; i < cfg.Outputs.Count; i++)
            {
                GraphOutputConfig output = cfg.Outputs[i];
                if (output == null)
                {
                    continue;
                }

                string outputId = string.IsNullOrWhiteSpace(output.Id)
                    ? $"output[{i}]"
                    : output.Id;

                if (!TryParseOutputDestination(output.Destination, out GraphOutputDestinationKind destination))
                {
                    diagnostics.Add(new GraphDiagnostic(
                        GraphDiagnosticSeverity.Error,
                        GraphDiagnosticCodes.TypeMismatch,
                        $"Graph output '{outputId}' has unsupported destination '{output.Destination}'.",
                        cfg.Id,
                        outputId));
                    continue;
                }

                if (!TryParseOutputValueKind(output.Type, out GraphOutputValueKind valueKind))
                {
                    diagnostics.Add(new GraphDiagnostic(
                        GraphDiagnosticSeverity.Error,
                        GraphDiagnosticCodes.TypeMismatch,
                        $"Graph output '{outputId}' has unsupported type '{output.Type}'.",
                        cfg.Id,
                        outputId));
                    continue;
                }

                if (destination == GraphOutputDestinationKind.EntityCollection)
                {
                    CompileCollectionOutput(cfg, output, outputId, valueKind, bindings, diagnostics);
                    continue;
                }

                CompileSummaryOutput(cfg, output, outputId, valueKind, valueMap, bindings, diagnostics);
            }

            return bindings.Count == 0
                ? GraphOutputSchema.Empty
                : new GraphOutputSchema(bindings.ToArray());
        }

        private static void CompileCollectionOutput(
            GraphConfig cfg,
            GraphOutputConfig output,
            string outputId,
            GraphOutputValueKind valueKind,
            List<GraphOutputBinding> bindings,
            List<GraphDiagnostic> diagnostics)
        {
            if (valueKind != GraphOutputValueKind.TargetList)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.TypeMismatch,
                    $"Graph collection output '{outputId}' must use type TargetList.",
                    cfg.Id,
                    outputId));
                return;
            }

            if (string.IsNullOrWhiteSpace(output.CollectionKey))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.MissingNodeRef,
                    $"Graph collection output '{outputId}' requires collectionKey.",
                    cfg.Id,
                    outputId));
                return;
            }

            EntityCollectionRoleKind role = EntityCollectionRoleKind.Display;
            if (!string.IsNullOrWhiteSpace(output.Role) &&
                !Enum.TryParse(output.Role, ignoreCase: false, out role))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.TypeMismatch,
                    $"Graph collection output '{outputId}' has unsupported role '{output.Role}'.",
                    cfg.Id,
                    outputId));
                return;
            }

            bindings.Add(new GraphOutputBinding(
                outputId,
                GraphOutputDestinationKind.EntityCollection,
                GraphOutputValueKind.TargetList,
                register: 0,
                keyId: 0,
                key: string.Empty,
                collectionKey: output.CollectionKey.Trim(),
                collectionRole: role,
                title: output.Title,
                summary: output.Summary));
        }

        private static void CompileSummaryOutput(
            GraphConfig cfg,
            GraphOutputConfig output,
            string outputId,
            GraphOutputValueKind valueKind,
            Dictionary<string, (GraphValueType Type, byte Reg)> valueMap,
            List<GraphOutputBinding> bindings,
            List<GraphDiagnostic> diagnostics)
        {
            if (valueKind == GraphOutputValueKind.TargetList)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.TypeMismatch,
                    $"Graph summary output '{outputId}' cannot use type TargetList.",
                    cfg.Id,
                    outputId));
                return;
            }

            if (string.IsNullOrWhiteSpace(output.Source) ||
                !valueMap.TryGetValue(output.Source, out (GraphValueType Type, byte Reg) source))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.MissingNodeRef,
                    $"Graph summary output '{outputId}' references missing source '{output.Source}'.",
                    cfg.Id,
                    outputId));
                return;
            }

            if (!MatchesOutputType(valueKind, source.Type))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.TypeMismatch,
                    $"Graph summary output '{outputId}' type {valueKind} does not match source '{output.Source}' type {source.Type}.",
                    cfg.Id,
                    outputId));
                return;
            }

            string key = string.IsNullOrWhiteSpace(output.Key) ? outputId : output.Key.Trim();
            bindings.Add(new GraphOutputBinding(
                outputId,
                GraphOutputDestinationKind.Summary,
                valueKind,
                source.Reg,
                keyId: 0,
                key,
                collectionKey: string.Empty,
                collectionRole: EntityCollectionRoleKind.Display,
                title: output.Title,
                summary: output.Summary));
        }

        private static bool TryParseOutputDestination(string value, out GraphOutputDestinationKind destination)
        {
            destination = GraphOutputDestinationKind.Summary;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value, ignoreCase: false, out destination) &&
                   Enum.IsDefined(typeof(GraphOutputDestinationKind), destination);
        }

        private static bool TryParseOutputValueKind(string value, out GraphOutputValueKind valueKind)
        {
            valueKind = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value, ignoreCase: false, out valueKind) &&
                   Enum.IsDefined(typeof(GraphOutputValueKind), valueKind);
        }

        private static bool MatchesOutputType(GraphOutputValueKind outputKind, GraphValueType graphType)
        {
            return outputKind switch
            {
                GraphOutputValueKind.Bool => graphType == GraphValueType.Bool,
                GraphOutputValueKind.Int => graphType == GraphValueType.Int,
                GraphOutputValueKind.Float => graphType == GraphValueType.Float,
                GraphOutputValueKind.Entity => graphType == GraphValueType.Entity,
                GraphOutputValueKind.TargetList => graphType == GraphValueType.TargetList,
                _ => false,
            };
        }

        private static bool HasErrors(List<GraphDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == GraphDiagnosticSeverity.Error) return true;
            }
            return false;
        }

        private static byte Alloc(ref int next, int max, string graphId, string nodeId, List<GraphDiagnostic> diagnostics)
        {
            if (next >= max)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.BudgetExceeded, $"Register budget exceeded (max={max}).", graphId, nodeId));
                return 0;
            }
            return (byte)next++;
        }

        private static byte RequireInput(
            GraphNodeConfig node,
            int inputIndex,
            GraphValueType expectedType,
            Dictionary<string, (GraphValueType Type, byte Reg)> valueMap,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (node.Inputs == null || node.Inputs.Count <= inputIndex)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingNodeRef, $"Node '{node.Id}' missing input[{inputIndex}].", graphId, node.Id));
                return 0;
            }

            var depId = node.Inputs[inputIndex];
            if (!valueMap.TryGetValue(depId, out var v))
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.MissingNodeRef, $"Node '{node.Id}' references input '{depId}' that is not produced earlier.", graphId, node.Id));
                return 0;
            }

            if (v.Type != expectedType)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, GraphDiagnosticCodes.TypeMismatch, $"Node '{node.Id}' input[{inputIndex}] expected {expectedType} but got {v.Type} from '{depId}'.", graphId, node.Id));
                return 0;
            }

            return v.Reg;
        }

        private static int Intern(Dictionary<string, int> symbolToIndex, List<string> symbols, string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return -1;
            if (symbolToIndex.TryGetValue(symbol, out var existing)) return existing;
            int idx = symbols.Count;
            symbolToIndex[symbol] = idx;
            symbols.Add(symbol);
            return idx;
        }

        private static byte EncodeByteSymbol(
            string? symbol,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return byte.MaxValue;
            }

            int symbolIndex = Intern(symbolToIndex, symbols, symbol);
            if (symbolIndex >= byte.MaxValue)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.BudgetExceeded,
                    $"Graph symbol budget exceeded for byte-encoded relationship symbol '{symbol}'.",
                    graphId,
                    nodeId));
                return byte.MaxValue;
            }

            return checked((byte)symbolIndex);
        }

        private static byte RequireRelationshipTypeSymbol(
            string? symbol,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{nodeId}' requires a non-empty relationshipType.",
                    graphId,
                    nodeId));
                return byte.MaxValue;
            }

            return EncodeByteSymbol(symbol, symbolToIndex, symbols, graphId, nodeId, diagnostics);
        }

        private static byte RequirePayloadPresetSymbol(
            string? symbol,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            string nodeId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{nodeId}' requires a non-empty payloadPreset.",
                    graphId,
                    nodeId));
                return byte.MaxValue;
            }

            return EncodeByteSymbol(symbol, symbolToIndex, symbols, graphId, nodeId, diagnostics);
        }
    }
}

