using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GraphCompiler
    {
        public static (GraphProgramPackage? Package, List<GraphDiagnostic> Diagnostics) Compile(GraphConfig cfg)
        {
            var diagnostics = GraphValidator.Validate(cfg);
            if (HasErrors(diagnostics))
            {
                return (null, diagnostics);
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
                    return (null, diagnostics);
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

                if (HasErrors(diagnostics)) return (null, diagnostics);

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
                    case GraphNodeOp.CompareGtFloat:
                        ins.A = RequireInput(node, 0, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
                        ins.B = RequireInput(node, 1, GraphValueType.Float, valueMap, cfg.Id, diagnostics);
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
                        ins.A = RequireInput(node, 0, GraphValueType.Entity, valueMap, cfg.Id, diagnostics);
                        ins.Imm = Intern(symbolToIndex, symbols, node.Metric);
                        ins.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, cfg.Id, node.Id, diagnostics);
                        break;
                }

                instructions.Add(ins);
            }

            if (HasErrors(diagnostics))
            {
                return (null, diagnostics);
            }

            return (new GraphProgramPackage(cfg.Id, symbols.ToArray(), instructions.ToArray()), diagnostics);
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
                GraphNodeOp.LoadAttribute => (GraphValueType.Float, null),
                GraphNodeOp.AddFloat => (GraphValueType.Float, null),
                GraphNodeOp.MulFloat => (GraphValueType.Float, null),
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
                _ => (GraphValueType.Void, null)
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

