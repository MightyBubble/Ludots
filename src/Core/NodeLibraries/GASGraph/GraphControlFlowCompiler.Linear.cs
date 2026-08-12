using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphControlFlowCompiler
    {
        private static bool IsLinearControlFlowAuthorable(GraphNodeOp op)
            => op is GraphNodeOp.ConstFloat or
                      GraphNodeOp.ConstBool or
                      GraphNodeOp.ConstInt or
                      GraphNodeOp.AddFloat or
                      GraphNodeOp.MulFloat or
                      GraphNodeOp.SubFloat or
                      GraphNodeOp.DivFloat or
                      GraphNodeOp.MinFloat or
                      GraphNodeOp.MaxFloat or
                      GraphNodeOp.ClampFloat or
                      GraphNodeOp.AbsFloat or
                      GraphNodeOp.NegFloat or
                      GraphNodeOp.AddInt or
                      GraphNodeOp.CompareGtFloat or
                      GraphNodeOp.CompareLtInt or
                      GraphNodeOp.CompareEqInt or
                      GraphNodeOp.SelectEntity or
                      GraphNodeOp.LoadCaster or
                      GraphNodeOp.LoadExplicitTarget or
                      GraphNodeOp.LoadContextTarget or
                      GraphNodeOp.LoadAttribute or
                      GraphNodeOp.LoadSelfAttribute or
                      GraphNodeOp.RandomFloat01 or
                      GraphNodeOp.ModifyAttributeAdd or
                      GraphNodeOp.WriteSelfAttribute or
                      GraphNodeOp.ApplyEffectTemplate or
                      GraphNodeOp.FanOutApplyEffect or
                      GraphNodeOp.ApplyEffectDynamic or
                      GraphNodeOp.FanOutApplyEffectDynamic or
                      GraphNodeOp.RemoveEffectTemplate or
                      GraphNodeOp.FanOutDispatchEffect or
                      GraphNodeOp.FanOutDispatchEffectDynamic or
                      GraphNodeOp.BeginLifecycleTransaction or
                      GraphNodeOp.InvokeBuiltin or
                      GraphNodeOp.WriteBlackboardInt or
                      GraphNodeOp.WriteBlackboardFloat or
                      GraphNodeOp.WriteBlackboardEntity or
                      GraphNodeOp.ReadBlackboardInt or
                      GraphNodeOp.ReadBlackboardFloat or
                      GraphNodeOp.ReadBlackboardEntity or
                      GraphNodeOp.LoadConfigFloat or
                      GraphNodeOp.LoadConfigInt or
                      GraphNodeOp.LoadConfigEffectId or
                      GraphNodeOp.QueryCone or
                      GraphNodeOp.QueryRectangle or
                      GraphNodeOp.QueryLine or
                      GraphNodeOp.QueryHexRange or
                      GraphNodeOp.QueryHexRing or
                      GraphNodeOp.QueryHexNeighbors or
                      GraphNodeOp.QueryFilterLayer or
                      GraphNodeOp.QueryFilterNotEntity or
                      GraphNodeOp.QueryFilterRelationship or
                      GraphNodeOp.AggCount or
                      GraphNodeOp.AggMinByDistance or
                      GraphNodeOp.TargetListGet or
                      GraphNodeOp.RelationshipGetMetric or
                      GraphNodeOp.SnapToNearestInCollection or
                      GraphNodeOp.InvokeScript;

        private static GraphValueType GetLinearOutputType(GraphNodeOp op)
            => op switch
            {
                GraphNodeOp.ConstFloat or
                    GraphNodeOp.AddFloat or
                    GraphNodeOp.MulFloat or
                    GraphNodeOp.SubFloat or
                    GraphNodeOp.DivFloat or
                    GraphNodeOp.MinFloat or
                    GraphNodeOp.MaxFloat or
                    GraphNodeOp.ClampFloat or
                    GraphNodeOp.AbsFloat or
                    GraphNodeOp.NegFloat or
                    GraphNodeOp.LoadAttribute or
                    GraphNodeOp.LoadSelfAttribute or
                    GraphNodeOp.RandomFloat01 or
                    GraphNodeOp.ReadBlackboardFloat or
                    GraphNodeOp.LoadConfigFloat => GraphValueType.Float,
                GraphNodeOp.ConstInt or
                    GraphNodeOp.AddInt or
                    GraphNodeOp.AggCount or
                    GraphNodeOp.RelationshipGetMetric or
                    GraphNodeOp.ReadBlackboardInt or
                    GraphNodeOp.LoadConfigInt or
                    GraphNodeOp.LoadConfigEffectId or
                    GraphNodeOp.InvokeScript => GraphValueType.Int,
                GraphNodeOp.ConstBool or
                    GraphNodeOp.CompareGtFloat or
                    GraphNodeOp.CompareLtInt or
                    GraphNodeOp.CompareEqInt => GraphValueType.Bool,
                GraphNodeOp.LoadCaster or
                    GraphNodeOp.LoadExplicitTarget or
                    GraphNodeOp.LoadContextTarget or
                    GraphNodeOp.SelectEntity or
                    GraphNodeOp.AggMinByDistance or
                    GraphNodeOp.ReadBlackboardEntity or
                    GraphNodeOp.SnapToNearestInCollection or
                    GraphNodeOp.TargetListGet => GraphValueType.Entity,
                _ => GraphValueType.Void
            };

        private static bool IsAllowedLinearControlPort(string port)
            => port == GraphControlFlowPorts.Next;

        private static bool IsAllowedLinearInputPort(GraphNodeOp op, string port)
            => op switch
            {
                GraphNodeOp.AddFloat or GraphNodeOp.MulFloat or GraphNodeOp.SubFloat or
                    GraphNodeOp.DivFloat or GraphNodeOp.MinFloat or GraphNodeOp.MaxFloat or
                    GraphNodeOp.CompareGtFloat or GraphNodeOp.AddInt or
                    GraphNodeOp.CompareLtInt or GraphNodeOp.CompareEqInt
                    => port is GraphControlFlowPorts.A or GraphControlFlowPorts.B,
                GraphNodeOp.ClampFloat
                    => port is GraphControlFlowPorts.Value or GraphControlFlowPorts.Min or GraphControlFlowPorts.Max,
                GraphNodeOp.AbsFloat or GraphNodeOp.NegFloat
                    => port == GraphControlFlowPorts.Value,
                GraphNodeOp.SelectEntity
                    => port is GraphControlFlowPorts.Condition or GraphControlFlowPorts.A or GraphControlFlowPorts.B,
                GraphNodeOp.LoadAttribute => port == GraphControlFlowPorts.Source,
                GraphNodeOp.ModifyAttributeAdd
                    => port is GraphControlFlowPorts.Target or GraphControlFlowPorts.Value,
                GraphNodeOp.WriteSelfAttribute => port == GraphControlFlowPorts.Value,
                GraphNodeOp.ApplyEffectTemplate
                    => port is GraphControlFlowPorts.Target or GraphControlFlowPorts.A or GraphControlFlowPorts.B,
                GraphNodeOp.ApplyEffectDynamic => port is GraphControlFlowPorts.Target or GraphControlFlowPorts.Value,
                GraphNodeOp.FanOutApplyEffectDynamic or GraphNodeOp.FanOutDispatchEffectDynamic
                    => port == GraphControlFlowPorts.Value,
                GraphNodeOp.RemoveEffectTemplate => port == GraphControlFlowPorts.Target,
                GraphNodeOp.RelationshipGetMetric => port is GraphControlFlowPorts.Source or GraphControlFlowPorts.Target,
                GraphNodeOp.SnapToNearestInCollection => port is GraphControlFlowPorts.Source or GraphControlFlowPorts.Value,
                GraphNodeOp.WriteBlackboardInt or GraphNodeOp.WriteBlackboardFloat or GraphNodeOp.WriteBlackboardEntity
                    => port is GraphControlFlowPorts.Source or GraphControlFlowPorts.Value,
                GraphNodeOp.ReadBlackboardInt or GraphNodeOp.ReadBlackboardFloat or GraphNodeOp.ReadBlackboardEntity
                    => port == GraphControlFlowPorts.Source,
                GraphNodeOp.QueryCone or GraphNodeOp.QueryRectangle or GraphNodeOp.QueryLine
                    => port is GraphControlFlowPorts.A or GraphControlFlowPorts.B,
                GraphNodeOp.QueryFilterNotEntity or GraphNodeOp.QueryFilterRelationship
                    => port == GraphControlFlowPorts.Source,
                GraphNodeOp.TargetListGet => port == GraphControlFlowPorts.Value,
                _ => false
            };

        private static bool IsAllowedLinearOutputPort(GraphNodeOp op, string port)
            => GetLinearOutputType(op) != GraphValueType.Void && port == GraphControlFlowPorts.Value;

        private static void ValidateLinearNode(
            GraphControlFlowNode node,
            AuthoredOp op,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            switch (op.NodeOp)
            {
                case GraphNodeOp.ConstFloat:
                case GraphNodeOp.ConstBool:
                case GraphNodeOp.ConstInt:
                case GraphNodeOp.LoadCaster:
                case GraphNodeOp.LoadExplicitTarget:
                case GraphNodeOp.LoadContextTarget:
                case GraphNodeOp.RandomFloat01:
                case GraphNodeOp.BeginLifecycleTransaction:
                case GraphNodeOp.AggCount:
                case GraphNodeOp.QueryFilterLayer:
                    break;

                case GraphNodeOp.InvokeScript:
                    if (string.IsNullOrWhiteSpace(node.FunctionName))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"InvokeScript node '{node.Id}' requires functionName for linear FuncLib calls.", node.Id));
                    }

                    if (node.GraphId > 0)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"InvokeScript node '{node.Id}' cannot use graphId in linear FuncLib authoring.", node.Id));
                    }

                    break;

                case GraphNodeOp.QueryHexNeighbors:
                    RequireSpatialCapacityPolicy(node, graphId, diagnostics);
                    break;

                case GraphNodeOp.AddFloat:
                case GraphNodeOp.MulFloat:
                case GraphNodeOp.SubFloat:
                case GraphNodeOp.DivFloat:
                case GraphNodeOp.MinFloat:
                case GraphNodeOp.MaxFloat:
                case GraphNodeOp.CompareGtFloat:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.ClampFloat:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Min, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Max, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.AbsFloat:
                case GraphNodeOp.NegFloat:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.AddInt:
                case GraphNodeOp.CompareLtInt:
                case GraphNodeOp.CompareEqInt:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.SelectEntity:
                    RequireValueInput(node, GraphControlFlowPorts.Condition, GraphValueType.Bool, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadAttribute:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadSelfAttribute:
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteSelfAttribute:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ModifyAttributeAdd:
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ApplyEffectTemplate:
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    bool hasA = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.A));
                    bool hasB = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.B));
                    if (hasB && !hasA)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                            $"Node '{node.Id}' cannot wire float arg B without A.", node.Id));
                    }

                    if (hasA)
                    {
                        RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    if (hasB)
                    {
                        RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    RequireNonEmpty(node.EffectTemplate, "effectTemplate", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.RemoveEffectTemplate:
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.EffectTemplate, "effectTemplate", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.InvokeBuiltin:
                    RequireNonEmpty(node.BuiltinHandler, "builtinHandler", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardInt:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.BlackboardKey, "blackboardKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardFloat:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.BlackboardKey, "blackboardKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardEntity:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.BlackboardKey, "blackboardKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ReadBlackboardInt:
                case GraphNodeOp.ReadBlackboardFloat:
                case GraphNodeOp.ReadBlackboardEntity:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.BlackboardKey, "blackboardKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadConfigFloat:
                case GraphNodeOp.LoadConfigInt:
                case GraphNodeOp.LoadConfigEffectId:
                    RequireNonEmpty(node.ConfigKey, "configKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryCone:
                case GraphNodeOp.QueryRectangle:
                case GraphNodeOp.QueryLine:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireSpatialCapacityPolicy(node, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryHexRange:
                case GraphNodeOp.QueryHexRing:
                    RequireSpatialCapacityPolicy(node, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryFilterNotEntity:
                case GraphNodeOp.QueryFilterRelationship:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (op.NodeOp == GraphNodeOp.QueryFilterRelationship)
                    {
                        RequireNonEmpty(node.RelationshipMode, "relationshipMode", node, graphId, diagnostics);
                    }

                    break;

                case GraphNodeOp.TargetListGet:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                default:
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Op '{op.NodeOp}' is not supported by linear ControlFlow compiler.", node.Id));
                    break;
            }

            _ = controlEdges;
        }

        private static void RequireNonEmpty(
            string? value,
            string fieldName,
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires a non-empty {fieldName}.", node.Id));
            }
        }

        private static void RequireSpatialCapacityPolicy(
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (!string.Equals(node.QueryCapacityPolicy, "RequireComplete", StringComparison.Ordinal) &&
                !string.Equals(node.QueryCapacityPolicy, "AllowTruncated", StringComparison.Ordinal))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' must declare queryCapacityPolicy as 'RequireComplete' or 'AllowTruncated'.",
                    node.Id));
            }

            if (string.Equals(node.QueryCapacityPolicy, "AllowTruncated", StringComparison.Ordinal))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' AllowTruncated droppedOutput is not yet authorable on ControlFlow linear kinds.",
                    node.Id));
            }
        }

        private static void CompileLinearNode(
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

                case GraphNodeOp.ConstBool:
                    instruction.Imm = node.BoolValue ? 1 : 0;
                    break;

                case GraphNodeOp.ConstInt:
                    instruction.Imm = node.IntValue;
                    break;

                case GraphNodeOp.LoadCaster:
                case GraphNodeOp.LoadExplicitTarget:
                case GraphNodeOp.LoadContextTarget:
                case GraphNodeOp.RandomFloat01:
                case GraphNodeOp.BeginLifecycleTransaction:
                case GraphNodeOp.AggCount:
                    break;

                case GraphNodeOp.QueryHexNeighbors:
                    instruction.Flags = 0;
                    break;

                case GraphNodeOp.AddFloat:
                case GraphNodeOp.MulFloat:
                case GraphNodeOp.SubFloat:
                case GraphNodeOp.DivFloat:
                case GraphNodeOp.MinFloat:
                case GraphNodeOp.MaxFloat:
                case GraphNodeOp.CompareGtFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.ClampFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Min, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Max, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.AbsFloat:
                case GraphNodeOp.NegFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.AddInt:
                case GraphNodeOp.CompareLtInt:
                case GraphNodeOp.CompareEqInt:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SelectEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadAttribute:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadSelfAttribute:
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteSelfAttribute:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ModifyAttributeAdd:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ApplyEffectTemplate:
                {
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.EffectTemplate, "effectTemplate", node, symbolToIndex, symbols, graphId, diagnostics);
                    byte floatCount = 0;
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.A)))
                    {
                        instruction.B = ResolveValueInput(
                            node, GraphControlFlowPorts.A, GraphValueType.Float,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                        floatCount = 1;
                    }

                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.B)))
                    {
                        instruction.C = ResolveValueInput(
                            node, GraphControlFlowPorts.B, GraphValueType.Float,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                        floatCount = 2;
                    }

                    instruction.Flags = floatCount;
                    break;
                }

                case GraphNodeOp.RemoveEffectTemplate:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.EffectTemplate, "effectTemplate", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.InvokeBuiltin:
                    instruction.Imm = RequireSymbol(node.BuiltinHandler, "builtinHandler", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardInt:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ReadBlackboardInt:
                case GraphNodeOp.ReadBlackboardFloat:
                case GraphNodeOp.ReadBlackboardEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadConfigFloat:
                case GraphNodeOp.LoadConfigInt:
                case GraphNodeOp.LoadConfigEffectId:
                    instruction.Imm = RequireSymbol(node.ConfigKey, "configKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryCone:
                    instruction.Flags = 0;
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.ImmF = node.RangeCm;
                    break;

                case GraphNodeOp.QueryRectangle:
                    instruction.Flags = 0;
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = node.RotationDeg;
                    break;

                case GraphNodeOp.QueryLine:
                    instruction.Flags = 0;
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = node.HalfWidthCm;
                    break;

                case GraphNodeOp.QueryHexRange:
                case GraphNodeOp.QueryHexRing:
                    instruction.Flags = 0;
                    instruction.Imm = node.HexRadius;
                    break;

                case GraphNodeOp.QueryFilterLayer:
                    instruction.Imm = unchecked((int)node.LayerMask);
                    break;

                case GraphNodeOp.QueryFilterNotEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryFilterRelationship:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = ParseLinearRelationshipFilterMode(node.RelationshipMode, node, graphId, diagnostics);
                    break;

                case GraphNodeOp.TargetListGet:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, definedInts, definedBools, graphId, diagnostics);
                    // Scratch validity bool; ValidOutput authoring is not yet on the linear CF matrix.
                    instruction.Flags = (byte)(GraphVmLimits.MaxBoolRegisters - 1);
                    break;

                case GraphNodeOp.InvokeScript:
                    instruction.Imm = RequireSymbol(node.FunctionName, "functionName", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = GraphInstructionFlags.FuncLibName;
                    break;

                default:
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.UnknownNodeOp,
                        $"Op '{op.NodeOp}' is not supported by linear ControlFlow compiler.", node.Id));
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
        }

        private static int ParseLinearRelationshipFilterMode(
            string? mode,
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{node.Id}' requires a non-empty relationshipMode.", node.Id));
                return 0;
            }

            return mode switch
            {
                "Hostile" => 1,
                "Friendly" => 2,
                "Neutral" => 3,
                "NotFriendly" => 4,
                "NotHostile" => 5,
                _ => AddLinearUnsupportedRelationshipMode(mode, node, graphId, diagnostics),
            };
        }

        private static int AddLinearUnsupportedRelationshipMode(
            string mode,
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                $"Node '{node.Id}' has unsupported relationshipMode '{mode}'. Supported: Hostile, Friendly, Neutral, NotFriendly, NotHostile.",
                node.Id));
            return 0;
        }
    }
}
