using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Presentation.TagDisplay;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphControlFlowCompiler
    {
        private static bool IsAllowedQueryControlPort(string port)
            => port == GraphControlFlowPorts.Next;

        private static void ValidateQueryNode(
            List<GraphControlFlowNode> nodes,
            int nodeIndex,
            AuthoredOp op,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            GraphControlFlowNode node = nodes[nodeIndex];

            switch (op.NodeOp)
            {
                case GraphNodeOp.ConstFloat:
                case GraphNodeOp.LoadCaster:
                case GraphNodeOp.QueryAllMapEntities:
                    break;
                case GraphNodeOp.QueryRadius:
                    RequireSpatialCapacityPolicy(node, graphId, diagnostics);
                    break;
                case GraphNodeOp.QuerySortStable:
                case GraphNodeOp.QueryLimit:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.AggMinByDistance:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.QueryFromCollection:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.CollectionKey))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty collectionKey.", node.Id));
                    }

                    break;
                case GraphNodeOp.QueryFilterTeam:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    bool hasTeamField = node.TeamId != 0;
                    bool hasTeamPin = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.TeamId));
                    if (hasTeamField == hasTeamPin)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                            $"Node '{node.Id}' must provide exactly one team source: TeamId field or teamId value pin.", node.Id));
                    }
                    else if (hasTeamPin)
                    {
                        RequireValueInput(node, GraphControlFlowPorts.TeamId, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    break;
                case GraphNodeOp.QueryFilterTemplate:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.Template))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty template.", node.Id));
                    }

                    break;
                case GraphNodeOp.QueryFilterTagAny:
                case GraphNodeOp.QueryFilterTagNone:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.Tag))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty tag.", node.Id));
                    }

                    break;
                case GraphNodeOp.HasTag:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.Tag))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty tag.", node.Id));
                    }

                    break;
                case GraphNodeOp.CompareEqEntity:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.SelectTagInMask:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.DisplayTable))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty displayTable.", node.Id));
                    }

                    _ = ParseQueryTagSelectPolicy(node.TagSelectPolicy, node, graphId, diagnostics);
                    break;
                case GraphNodeOp.LookupTagDisplayToken:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.DisplayTable))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty displayTable.", node.Id));
                    }

                    break;
                case GraphNodeOp.QueryFilterAttributeRange:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Min, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Max, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.Attribute))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty attribute.", node.Id));
                    }

                    break;
                case GraphNodeOp.QuerySortByAttribute:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.Attribute))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty attribute.", node.Id));
                    }

                    break;
                case GraphNodeOp.AggCount:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.InvokeScript:
                {
                    bool hasName = !string.IsNullOrWhiteSpace(node.FunctionName);
                    bool hasId = node.GraphId > 0;
                    if (hasName && hasId)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"InvokeScript node '{node.Id}' cannot set both functionName and graphId.", node.Id));
                    }
                    else if (!hasName && !hasId)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"InvokeScript node '{node.Id}' requires functionName (Func Lib) or graphId.", node.Id));
                    }

                    break;
                }
                case GraphNodeOp.AggSumAttribute:
                case GraphNodeOp.AggAverageAttribute:
                case GraphNodeOp.AggMaxAttribute:
                case GraphNodeOp.AggMinAttribute:
                case GraphNodeOp.AggMaxEntityByAttribute:
                case GraphNodeOp.AggMinEntityByAttribute:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.Attribute))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty attribute.", node.Id));
                    }

                    break;
                case GraphNodeOp.RelationshipQueryOutgoing:
                case GraphNodeOp.RelationshipQueryIncoming:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipQueryMutual:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipQueryBetweenPair:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireRelationshipFields(node, requireMetric: false, requireFlag: false, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipFilterMetricRange:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Min, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Max, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireRelationshipFields(node, requireMetric: true, requireFlag: false, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipHasFlag:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireRelationshipFields(node, requireMetric: false, requireFlag: true, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipHasLink:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireRelationshipFields(node, requireMetric: false, requireFlag: false, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipFilterFlag:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireRelationshipFields(node, requireMetric: false, requireFlag: true, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipSortByMetric:
                case GraphNodeOp.RelationshipAggSumMetric:
                case GraphNodeOp.RelationshipAggMaxMetric:
                case GraphNodeOp.RelationshipAggAverageMetric:
                case GraphNodeOp.RelationshipAggMinMetric:
                case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                case GraphNodeOp.RelationshipAggMinEntityByMetric:
                    RequireValueInput(node, GraphControlFlowPorts.List, GraphValueType.TargetList, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireRelationshipFields(node, requireMetric: true, requireFlag: false, graphId, diagnostics);
                    break;
                default:
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Op '{op.NodeOp}' is not supported by Query ControlFlow compiler.", node.Id));
                    break;
            }
        }

        private static void RequireRelationshipFields(
            GraphControlFlowNode node,
            bool requireMetric,
            bool requireFlag,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(node.RelationshipType))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires a non-empty relationshipType.", node.Id));
            }

            if (requireMetric && string.IsNullOrWhiteSpace(node.Metric))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires a non-empty metric.", node.Id));
            }

            if (requireFlag && string.IsNullOrWhiteSpace(node.Flag))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires a non-empty flag.", node.Id));
            }
        }

        private static void CompileQueryNode(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            AuthoredOp op,
            byte[] outputRegisters,
            GraphValueType[] outputTypes,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            NodeLayout[] layouts,
            GraphInstruction[] program,
            GraphInstructionSource[] sources,
            bool[] definedInts,
            bool[] definedBools,
            Dictionary<string, int> symbolToIndex,
            List<string> symbols,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            int nodeIndex = nodeIndices[node.Id];
            int bodyIndex = layouts[nodeIndex].BodyIndex;
            var instruction = new GraphInstruction
            {
                Op = (ushort)op.NodeOp,
                Dst = outputRegisters[nodeIndex]
            };

            switch (op.NodeOp)
            {
                case GraphNodeOp.ConstFloat:
                    instruction.ImmF = node.FloatValue;
                    break;
                case GraphNodeOp.LoadCaster:
                case GraphNodeOp.QueryAllMapEntities:
                    break;
                case GraphNodeOp.QueryRadius:
                    instruction.Flags = 0;
                    instruction.ImmF = node.RadiusCm;
                    break;
                case GraphNodeOp.QuerySortStable:
                    break;
                case GraphNodeOp.QueryLimit:
                    instruction.Imm = node.IntValue;
                    break;
                case GraphNodeOp.AggMinByDistance:
                    break;
                case GraphNodeOp.QueryFromCollection:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.CollectionKey, "collectionKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.QueryFilterTeam:
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.TeamId)))
                    {
                        instruction.A = ResolveValueInput(
                            node, GraphControlFlowPorts.TeamId, GraphValueType.Int,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                        instruction.Flags = 1;
                    }
                    else
                    {
                        instruction.Imm = node.TeamId;
                        instruction.Flags = 0;
                    }

                    break;
                case GraphNodeOp.QueryFilterTemplate:
                    instruction.Imm = RequireSymbol(node.Template, "template", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.QueryFilterTagAny:
                case GraphNodeOp.QueryFilterTagNone:
                    instruction.Imm = RequireSymbol(node.Tag, "tag", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.HasTag:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Tag, "tag", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.CompareEqEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;
                case GraphNodeOp.SelectTagInMask:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.DisplayTable, "displayTable", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = ParseQueryTagSelectPolicy(node.TagSelectPolicy, node, graphId, diagnostics);
                    break;
                case GraphNodeOp.LookupTagDisplayToken:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.DisplayTable, "displayTable", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.QueryFilterAttributeRange:
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Min, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Max, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.QuerySortByAttribute:
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = node.Descending ? (byte)1 : (byte)0;
                    break;
                case GraphNodeOp.AggCount:
                    break;
                case GraphNodeOp.InvokeScript:
                {
                    bool hasName = !string.IsNullOrWhiteSpace(node.FunctionName);
                    if (hasName)
                    {
                        instruction.Imm = RequireSymbol(node.FunctionName!.Trim(), "functionName", node, symbolToIndex, symbols, graphId, diagnostics);
                        instruction.Flags = GraphInstructionFlags.FuncLibName;
                    }
                    else
                    {
                        instruction.Imm = node.GraphId;
                        instruction.Flags = 0;
                    }

                    break;
                }
                case GraphNodeOp.AggSumAttribute:
                case GraphNodeOp.AggAverageAttribute:
                case GraphNodeOp.AggMaxAttribute:
                case GraphNodeOp.AggMinAttribute:
                case GraphNodeOp.AggMaxEntityByAttribute:
                case GraphNodeOp.AggMinEntityByAttribute:
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.RelationshipQueryOutgoing:
                case GraphNodeOp.RelationshipQueryIncoming:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Dst = EncodeByteSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipQueryMutual:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Dst = EncodeByteSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipQueryBetweenPair:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipFilterMetricRange:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Min, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Max, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Metric);
                    instruction.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipHasFlag:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Flag);
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipHasLink:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipFilterFlag:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = 0;
                    instruction.Flags = 1;
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Flag);
                    instruction.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                case GraphNodeOp.RelationshipSortByMetric:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Metric);
                    instruction.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    instruction.Flags = node.Descending ? (byte)1 : (byte)0;
                    break;
                case GraphNodeOp.RelationshipAggSumMetric:
                case GraphNodeOp.RelationshipAggMaxMetric:
                case GraphNodeOp.RelationshipAggAverageMetric:
                case GraphNodeOp.RelationshipAggMinMetric:
                case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                case GraphNodeOp.RelationshipAggMinEntityByMetric:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Metric);
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;
                default:
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Op '{op.NodeOp}' is not supported by Query ControlFlow compiler.", node.Id));
                    return;
            }

            program[bodyIndex] = instruction;
            SetSource(sources, bodyIndex, graphId, node, op.NodeOp.ToString(), GraphControlFlowPorts.Enter);

            if (outputTypes[nodeIndex] == GraphValueType.Int)
            {
                definedInts[outputRegisters[nodeIndex]] = true;
            }

            if (outputTypes[nodeIndex] == GraphValueType.Bool)
            {
                definedBools[outputRegisters[nodeIndex]] = true;
            }

            if (controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)))
            {
                EmitRelativeJump(
                    document,
                    node,
                    GraphControlFlowPorts.Next,
                    bodyIndex + 1,
                    controlEdges,
                    nodeIndices,
                    layouts,
                    program,
                    sources,
                    graphId);
            }
            else if (op.NodeOp != GraphNodeOp.HaltReturnInt)
            {
                EmitExplicitHalt(program, sources, bodyIndex + 1, graphId, node);
            }
        }

        private static int InternOptional(Dictionary<string, int> symbolToIndex, List<string> symbols, string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return -1;
            }

            return Intern(symbolToIndex, symbols, symbol);
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
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.BudgetExceeded,
                    $"Graph symbol budget exceeded for byte-encoded relationship symbol '{symbol}'.", nodeId));
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
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{nodeId}' requires a non-empty relationshipType.", nodeId));
                return byte.MaxValue;
            }

            return EncodeByteSymbol(symbol, symbolToIndex, symbols, graphId, nodeId, diagnostics);
        }

        private static byte ParseQueryTagSelectPolicy(
            string? policy,
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(policy))
            {
                return (byte)TagSelectPolicy.RequireOne;
            }

            if (!Enum.TryParse(policy, ignoreCase: false, out TagSelectPolicy parsed) ||
                !Enum.IsDefined(typeof(TagSelectPolicy), parsed))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' has unsupported tagSelectPolicy '{policy}'. Supported: RequireOne, AllowNone, LowestId.",
                    node.Id));
                return (byte)TagSelectPolicy.RequireOne;
            }

            return (byte)parsed;
        }
    }
}
