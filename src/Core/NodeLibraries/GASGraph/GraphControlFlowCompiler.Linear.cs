using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.UI.PanelHosting;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphControlFlowCompiler
    {
        private static bool IsAllowedLinearControlPort(string port)
            => port == GraphControlFlowPorts.Next;

        private static void ValidateLinearNode(
            GraphControlFlowNode node,
            AuthoredOp op,
            Dictionary<ControlKey, string> controlEdges,
            Dictionary<ValueInputKey, GraphControlFlowValueEdge> valueEdges,
            Dictionary<string, int> nodeIndices,
            GraphValueType[] outputTypes,
            string graphId,
            List<GraphDiagnostic> diagnostics,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null)
        {
            switch (op.NodeOp)
            {
                case GraphNodeOp.ConstFloat:
                case GraphNodeOp.ConstBool:
                case GraphNodeOp.ConstInt:
                case GraphNodeOp.LoadCaster:
                case GraphNodeOp.LoadExplicitTarget:
                case GraphNodeOp.LoadViewer:
                case GraphNodeOp.LoadContextSource:
                case GraphNodeOp.LoadContextTarget:
                case GraphNodeOp.LoadContextTargetContext:
                case GraphNodeOp.RandomFloat01:
                case GraphNodeOp.BeginLifecycleTransaction:
                case GraphNodeOp.AggCount:
                case GraphNodeOp.QueryFilterLayer:
                case GraphNodeOp.LoadTargetPosX:
                case GraphNodeOp.LoadTargetPosY:
                case GraphNodeOp.LoadPointerScreenX:
                case GraphNodeOp.LoadPointerScreenY:
                    break;

                case GraphNodeOp.HaltReturnInt:
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Value)))
                    {
                        RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

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

                case GraphNodeOp.QueryRadius:
                    RequireSpatialCapacityPolicy(node, graphId, diagnostics);
                    break;

                case GraphNodeOp.QuerySortStable:
                case GraphNodeOp.QueryLimit:
                case GraphNodeOp.AggMinByDistance:
                case GraphNodeOp.QueryAllMapEntities:
                case GraphNodeOp.QueryFilterTemplate:
                case GraphNodeOp.QueryFilterTagAny:
                case GraphNodeOp.QueryFilterTagNone:
                    // Query-class carve-outs in the linear dialect chain through the shared
                    // target list; only their symbol fields need authoring validation here.
                    if (op.NodeOp == GraphNodeOp.QueryFilterTemplate && string.IsNullOrWhiteSpace(node.Template))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty template.", node.Id));
                    }

                    if ((op.NodeOp == GraphNodeOp.QueryFilterTagAny || op.NodeOp == GraphNodeOp.QueryFilterTagNone) &&
                        string.IsNullOrWhiteSpace(node.Tag))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a non-empty tag.", node.Id));
                    }

                    break;

                case GraphNodeOp.QueryFromCollection:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.CollectionKey, "collectionKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryFilterTeam:
                {
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
                }

                case GraphNodeOp.QueryFilterAttributeRange:
                    RequireValueInput(node, GraphControlFlowPorts.Min, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Max, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ScreenPointToGround:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.ScreenPointToEntity:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (node.PickRadiusPx <= 0f)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a positive pickRadiusPx.", node.Id));
                    }

                    break;

                case GraphNodeOp.ScreenRegionToEntities:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.C, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Max, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.PointToDirection:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.StickToDirection:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
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

                case GraphNodeOp.HasTag:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Tag, "tag", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.CompareEqEntity:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadAttribute:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ResolveTableRow:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.LookupTable, "lookupTable", node, graphId, diagnostics);
                    break;
                case GraphNodeOp.WeightedPick:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Distribution, "distribution", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.TableReadInt:
                case GraphNodeOp.TableReadFloat:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.LookupTable) || string.IsNullOrWhiteSpace(node.LookupField))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires non-empty lookupTable and lookupField.", node.Id));
                    }

                    break;

                case GraphNodeOp.LoadSelfAttribute:
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadEffectTiming:
                    RequireEffectTimingAttribute(node, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadEffectStack:
                    break;

                case GraphNodeOp.WriteSelfAttribute:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Attribute, "attribute", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ModifyAttributeAdd:
                case GraphNodeOp.ModifyAttributeSet:
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

                case GraphNodeOp.FanOutApplyEffect:
                    RequireNonEmpty(node.EffectTemplate, "effectTemplate", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ApplyEffectDynamic:
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.FanOutApplyEffectDynamic:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.RemoveEffectTemplate:
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.EffectTemplate, "effectTemplate", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.FanOutDispatchEffect:
                    RequireNonEmpty(node.EffectTemplate, "effectTemplate", node, graphId, diagnostics);
                    RequireNonEmpty(node.PayloadPreset, "payloadPreset", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.FanOutDispatchEffectDynamic:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.PayloadPreset, "payloadPreset", node, graphId, diagnostics);
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

                case GraphNodeOp.ShowPanel:
                case GraphNodeOp.HidePanel:
                    RequireNonEmpty(node.PanelType, "panelType", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.CreatePanel:
                    RequireNonEmpty(node.PanelType, "panelType", node, graphId, diagnostics);
                    RequireNonEmpty(node.PanelAnchor, "panelAnchor", node, graphId, diagnostics);
                    if (!PanelAnchorCatalog.IsSupported(node.PanelAnchor))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.InvalidPanelAnchor,
                            $"Node '{node.Id}' panelAnchor '{node.PanelAnchor}' is not a supported panel anchor. Supported anchors: {PanelAnchorCatalog.Describe()}.", node.Id));
                    }
                    break;

                case GraphNodeOp.DestroyPanel:
                    RequireNonEmpty(node.PanelType, "panelType", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.SpawnTemplate:
                    RequireNonEmpty(node.Template, "template", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.SetWorldPosition:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.SetInteractionMode:
                    RequireNonEmpty(node.Mode, "mode", node, graphId, diagnostics);
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source)))
                    {
                        RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    break;

                case GraphNodeOp.ActivateContext:
                case GraphNodeOp.DeactivateContext:
                    RequireNonEmpty(node.Context, "context", node, graphId, diagnostics);
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source)))
                    {
                        RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    break;

                case GraphNodeOp.DispatchCollectionEvent:
                    RequireNonEmpty(node.Event, "event", node, graphId, diagnostics);
                    RequireNonEmpty(node.CollectionKey, "collectionKey", node, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.SetPanelAudience:
                    RequireNonEmpty(node.PanelType, "panelType", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ReadMapVarInt:
                case GraphNodeOp.ReadMapVarFloat:
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source)))
                    {
                        RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    RequireNonEmpty(node.Var, "var", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteMapVarInt:
                case GraphNodeOp.WriteMapVarFloat:
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source)))
                    {
                        RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    }

                    RequireValueInput(
                        node,
                        GraphControlFlowPorts.Value,
                        op.NodeOp == GraphNodeOp.WriteMapVarInt ? GraphValueType.Int : GraphValueType.Float,
                        valueEdges,
                        nodeIndices,
                        outputTypes,
                        graphId,
                        diagnostics);
                    RequireNonEmpty(node.Var, "var", node, graphId, diagnostics);
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

                case GraphNodeOp.RelationshipEnsureLink:
                case GraphNodeOp.RelationshipRemoveLink:
                case GraphNodeOp.RelationshipHasLink:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.RelationshipType, "relationshipType", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.RelationshipSetMetric:
                case GraphNodeOp.RelationshipAddMetric:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.RelationshipType, "relationshipType", node, graphId, diagnostics);
                    RequireNonEmpty(node.Metric, "metric", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.RelationshipGetMetric:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.RelationshipType, "relationshipType", node, graphId, diagnostics);
                    RequireNonEmpty(node.Metric, "metric", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.RelationshipHasFlag:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.RelationshipType, "relationshipType", node, graphId, diagnostics);
                    RequireNonEmpty(node.Flag, "flag", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.RelationshipSetFlag:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Bool, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.RelationshipType, "relationshipType", node, graphId, diagnostics);
                    RequireNonEmpty(node.Flag, "flag", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ClampTargetToRange:
                case GraphNodeOp.IsPointInCircle:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.SnapToNearestInCollection:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.CollectionKey, "collectionKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.SnapToNearestGraphEdge:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadEventPayloadInt:
                    if (node.Slot is < 0 or > 1)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"Node '{node.Id}' LoadEventPayloadInt slot must be 0 (PayloadA) or 1 (PayloadB).",
                            node.Id));
                    }

                    break;

                case GraphNodeOp.LoadEventPayloadFloat:
                    if (node.Slot is < 0 or > 3)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"Node '{node.Id}' LoadEventPayloadFloat slot must be 0..3 (FloatA..FloatD).",
                            node.Id));
                    }

                    break;

                case GraphNodeOp.LoadEntryPayloadEntity:
                case GraphNodeOp.LoadEntryPayloadInt:
                case GraphNodeOp.LoadEntryPayloadFloat:
                    // Built-in event payloads use MapTrigger.* constants (a reserved
                    // namespace: unknown MapTrigger.* keys are typos and fail closed);
                    // custom-event schemas and InvokeGraph argument keys use dot-namespaced
                    // keys (EventSchemaRegistry key shape). Unknown-but-well-shaped keys
                    // fail closed at run time (EntryPayloadKeyNotCarried).
                    if (IsInvalidEntryPayloadKey(node.PayloadKey))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"Node '{node.Id}' {op.NodeOp} payloadKey must be a MapTriggerEventPayloadKeys constant or a dot-namespaced key (got '{node.PayloadKey}').",
                            node.Id));
                    }

                    break;

                case GraphNodeOp.LoadPlacedEntity:
                case GraphNodeOp.LoadPlacedRegion:
                case GraphNodeOp.LoadPlacedAnchor:
                    // The compiler has no map context, so membership in the mounting map's
                    // placed-instance / region catalog is validated fail-closed at mount time
                    // (TriggerGraphMounting); here only the non-empty authoring shape is checked.
                    // LoadPlacedAnchor additionally requires InstanceId to contain "anchor".
                    RequireNonEmpty(node.InstanceId, "instanceId", node, graphId, diagnostics);
                    if (op.NodeOp == GraphNodeOp.LoadPlacedAnchor &&
                        !string.IsNullOrWhiteSpace(node.InstanceId) &&
                        !Ludots.Core.Systems.PlacedInstanceKinds.IsAnchorInstanceId(node.InstanceId))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"Node '{node.Id}' LoadPlacedAnchor instanceId must contain 'anchor' (got '{node.InstanceId}').",
                            node.Id));
                    }

                    break;

                case GraphNodeOp.InvokeGraph:
                {
                    bool hasGraphId = node.GraphId > 0;
                    bool hasName = !string.IsNullOrWhiteSpace(node.FunctionName);
                    if (hasGraphId && hasName)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"InvokeGraph node '{node.Id}' cannot set both functionName and graphId.", node.Id));
                    }
                    else if (!hasGraphId && !hasName)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"InvokeGraph node '{node.Id}' requires functionName (graph key) or graphId.", node.Id));
                    }

                    break;
                }

                case GraphNodeOp.StoreArgInt:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.ArgKey, "argKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.StoreArgFloat:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.ArgKey, "argKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.StoreArgEntity:
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.ArgKey, "argKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.AwaitCallback:
                    RequireNonEmpty(node.CallbackType, "callbackType", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ConstText:
                    if (node.Text == null)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a text field (empty string is allowed).", node.Id));
                    }

                    break;

                case GraphNodeOp.LoadTextKey:
                    RequireNonEmpty(node.TextKey, "textKey", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.OfferActivity:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.ActivityId, "activityId", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.OfferTask:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.TaskId, "taskId", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.StartDialogue:
                    RequireNonEmpty(node.DialogueId, "dialogueId", node, graphId, diagnostics);
                    break;

                case GraphNodeOp.ConcatText:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Text, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Text, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.IntToText:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Int, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.FloatToText:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.SinkPresentationText:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Text, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    if (!TryParsePresentationSurface(node.PresentationSurface, out _))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"SinkPresentationText node '{node.Id}' requires presentationSurface 'Subtitle' or 'Dialogue'.",
                            node.Id));
                    }

                    break;

                case GraphNodeOp.DispatchMapEvent:
                {
                    if (dispatchSchemas == null || !dispatchSchemas.TryGetValue(node.Id, out Ludots.Core.Scripting.EventSchema schema))
                    {
                        break;
                    }

                    for (int i = 0; i < schema.Params.Count; i++)
                    {
                        Ludots.Core.Scripting.EventParamSchema param = schema.Params[i];
                        if (param.Type == Ludots.Core.Scripting.EventParamType.String)
                        {
                            if (valueEdges.ContainsKey(new ValueInputKey(node.Id, param.Name)))
                            {
                                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                                    $"DispatchMapEvent node '{node.Id}' parameter '{param.Name}' is a String; String parameters have no register port and ride the schema contract instead.",
                                    node.Id));
                            }

                            continue;
                        }

                        if (valueEdges.ContainsKey(new ValueInputKey(node.Id, param.Name)))
                        {
                            RequireValueInput(
                                node,
                                param.Name,
                                ParamPortType(param.Type),
                                valueEdges,
                                nodeIndices,
                                outputTypes,
                                graphId,
                                diagnostics);
                        }
                    }

                    foreach (GraphControlFlowValueEdge edge in valueEdges.Values)
                    {
                        if (!string.Equals(edge.To, node.Id, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        bool declared = false;
                        for (int i = 0; i < schema.Params.Count; i++)
                        {
                            if (string.Equals(schema.Params[i].Name, edge.ToPort, StringComparison.Ordinal))
                            {
                                declared = true;
                                break;
                            }
                        }

                        if (!declared)
                        {
                            diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingValueInput,
                                $"DispatchMapEvent node '{node.Id}' has no schema parameter '{edge.ToPort}' for event '{schema.EventName}'.",
                                node.Id));
                        }
                    }

                    break;
                }

                case GraphNodeOp.ControlDomainResolve:
                    RequireValueInput(node, GraphControlFlowPorts.Source, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.ControlDomainControls:
                case GraphNodeOp.KnowledgeHasProjection:
                    RequireValueInput(node, GraphControlFlowPorts.A, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.B, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    break;

                case GraphNodeOp.SendEvent:
                    RequireValueInput(node, GraphControlFlowPorts.Target, GraphValueType.Entity, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireValueInput(node, GraphControlFlowPorts.Value, GraphValueType.Float, valueEdges, nodeIndices, outputTypes, graphId, diagnostics);
                    RequireNonEmpty(node.Tag, "tag", node, graphId, diagnostics);
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

        private static GraphValueType ParamPortType(Ludots.Core.Scripting.EventParamType type)
        {
            return type switch
            {
                Ludots.Core.Scripting.EventParamType.Entity => GraphValueType.Entity,
                Ludots.Core.Scripting.EventParamType.Int => GraphValueType.Int,
                Ludots.Core.Scripting.EventParamType.Float => GraphValueType.Float,
                _ => GraphValueType.Void,
            };
        }

        private static bool IsInvalidEntryPayloadKey(string? payloadKey)
        {
            if (string.IsNullOrWhiteSpace(payloadKey))
            {
                return true;
            }

            if (Ludots.Core.Scripting.EventSchemaRegistry.IsReservedPayloadKey(payloadKey))
            {
                return !Ludots.Core.Scripting.MapTriggerEventPayloadKeys.IsKnownKey(payloadKey);
            }

            return !Ludots.Core.Scripting.EventSchemaRegistry.IsNamespacedPayloadKey(payloadKey, out _);
        }

        private static void RequireSpatialCapacityPolicy(
            GraphControlFlowNode node,
            string graphId,
            List<GraphDiagnostic> diagnostics)
        {
            if (!IsRequireComplete(node) && !IsAllowTruncated(node))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' must declare queryCapacityPolicy as 'RequireComplete' or 'AllowTruncated'.",
                    node.Id));
                return;
            }

            if (IsAllowTruncated(node))
            {
                if (string.IsNullOrWhiteSpace(node.DroppedOutput))
                {
                    diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                        $"Node '{node.Id}' AllowTruncated requires a non-empty droppedOutput.",
                        node.Id));
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(node.DroppedOutput))
            {
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                    $"Node '{node.Id}' droppedOutput is only valid with queryCapacityPolicy 'AllowTruncated'.",
                    node.Id));
            }
        }

        private static void CompileLinearNode(
            GraphControlFlowDocument document,
            GraphControlFlowNode node,
            AuthoredOp op,
            byte[] outputRegisters,
            GraphValueType[] outputTypes,
            byte[] boolScratches,
            byte[] droppedRegisters,
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
            List<GraphDiagnostic> diagnostics,
            Dictionary<string, Ludots.Core.Scripting.EventSchema>? dispatchSchemas = null,
            BtSugarPlan? btPlan = null,
            int nodeIndex = -1)
        {
            nodeIndex = nodeIndices[node.Id];
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
                case GraphNodeOp.LoadViewer:
                case GraphNodeOp.LoadContextSource:
                case GraphNodeOp.LoadContextTarget:
                case GraphNodeOp.LoadContextTargetContext:
                case GraphNodeOp.RandomFloat01:
                case GraphNodeOp.BeginLifecycleTransaction:
                case GraphNodeOp.AggCount:
                case GraphNodeOp.LoadTargetPosX:
                case GraphNodeOp.LoadTargetPosY:
                case GraphNodeOp.LoadPointerScreenX:
                case GraphNodeOp.LoadPointerScreenY:
                    break;

                case GraphNodeOp.QueryHexNeighbors:
                    instruction.Flags = 0;
                    ApplySpatialCapacityPolicy(node, droppedRegisters[nodeIndex], ref instruction);
                    break;

                case GraphNodeOp.QueryRadius:
                    instruction.Flags = 0;
                    instruction.ImmF = node.RadiusCm;
                    ApplySpatialCapacityPolicy(node, droppedRegisters[nodeIndex], ref instruction);
                    break;

                case GraphNodeOp.QuerySortStable:
                    break;

                case GraphNodeOp.QueryLimit:
                    instruction.Imm = node.IntValue;
                    break;

                case GraphNodeOp.AggMinByDistance:
                    break;

                case GraphNodeOp.QueryAllMapEntities:
                    break;

                case GraphNodeOp.QueryFromCollection:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.CollectionKey, "collectionKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryFilterTeam:
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.TeamId)))
                    {
                        instruction.A = ResolveValueInput(
                            node, GraphControlFlowPorts.TeamId, GraphValueType.Int,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
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

                case GraphNodeOp.QueryFilterAttributeRange:
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Min, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Max, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryFilterTagAny:
                case GraphNodeOp.QueryFilterTagNone:
                    instruction.Imm = RequireSymbol(node.Tag, "tag", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ScreenPointToGround:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Seat);
                    break;

                case GraphNodeOp.ScreenPointToEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Seat);
                    instruction.ImmF = node.PickRadiusPx;
                    break;

                case GraphNodeOp.ScreenRegionToEntities:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.C, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Flags = ResolveValueInput(
                        node, GraphControlFlowPorts.Max, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = InternOptional(symbolToIndex, symbols, node.Seat);
                    break;

                case GraphNodeOp.PointToDirection:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Flags = boolScratches[nodeIndex];
                    break;

                case GraphNodeOp.StickToDirection:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Flags = boolScratches[nodeIndex];
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
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.ClampFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Min, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Max, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.AbsFloat:
                case GraphNodeOp.NegFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.AddInt:
                case GraphNodeOp.CompareLtInt:
                case GraphNodeOp.CompareEqInt:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SelectEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Condition, GraphValueType.Bool,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.HasTag:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Tag, "tag", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.CompareEqEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadAttribute:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ResolveTableRow:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.LookupTable, "lookupTable", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;
                case GraphNodeOp.WeightedPick:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Distribution, "distribution", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.TableReadInt:
                case GraphNodeOp.TableReadFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireLookupFieldSymbol(node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadSelfAttribute:
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadEffectTiming:
                    instruction.Flags = ResolveEffectTimingFlags(node, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadEffectStack:
                    break;

                case GraphNodeOp.WriteSelfAttribute:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ModifyAttributeAdd:
                case GraphNodeOp.ModifyAttributeSet:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Attribute, "attribute", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ApplyEffectTemplate:
                {
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.EffectTemplate, "effectTemplate", node, symbolToIndex, symbols, graphId, diagnostics);
                    byte floatCount = 0;
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.A)))
                    {
                        instruction.B = ResolveValueInput(
                            node, GraphControlFlowPorts.A, GraphValueType.Float,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                        floatCount = 1;
                    }

                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.B)))
                    {
                        instruction.C = ResolveValueInput(
                            node, GraphControlFlowPorts.B, GraphValueType.Float,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                        floatCount = 2;
                    }

                    instruction.Flags = floatCount;
                    break;
                }

                case GraphNodeOp.FanOutApplyEffect:
                    instruction.Imm = RequireSymbol(node.EffectTemplate, "effectTemplate", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ApplyEffectDynamic:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.FanOutApplyEffectDynamic:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.RemoveEffectTemplate:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.EffectTemplate, "effectTemplate", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.FanOutDispatchEffect:
                    instruction.Imm = RequireSymbol(node.EffectTemplate, "effectTemplate", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = RequirePayloadPresetSymbol(node.PayloadPreset, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.FanOutDispatchEffectDynamic:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Dst = RequirePayloadPresetSymbol(node.PayloadPreset, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.InvokeBuiltin:
                    instruction.Imm = RequireSymbol(node.BuiltinHandler, "builtinHandler", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardInt:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardFloat:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.WriteBlackboardEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ReadBlackboardInt:
                case GraphNodeOp.ReadBlackboardFloat:
                case GraphNodeOp.ReadBlackboardEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.BlackboardKey, "blackboardKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadConfigFloat:
                case GraphNodeOp.LoadConfigInt:
                case GraphNodeOp.LoadConfigEffectId:
                    instruction.Imm = RequireSymbol(node.ConfigKey, "configKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ShowPanel:
                case GraphNodeOp.HidePanel:
                    instruction.Imm = RequireSymbol(node.PanelType, "panelType", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.CreatePanel:
                    instruction.Imm = RequireSymbol(node.PanelType, "panelType", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = EncodeByteSymbol(node.PanelAnchor, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    if (string.IsNullOrWhiteSpace(node.PanelSkin))
                    {
                        instruction.B = byte.MaxValue;
                    }
                    else
                    {
                        // Flag bit 0 marks "skin authored": hand-built legacy instructions
                        // default B to 0 (a register index), not the unspecified sentinel.
                        instruction.Flags = 1;
                        instruction.B = EncodeByteSymbol(node.PanelSkin, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    }
                    instruction.ImmF = node.PanelZOrder ?? 0f;
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.DestroyPanel:
                    instruction.Imm = RequireSymbol(node.PanelType, "panelType", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.SpawnTemplate:
                    instruction.Imm = RequireSymbol(node.Template, "template", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    bool hasX = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.A));
                    bool hasY = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.B));
                    if (hasX != hasY)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"Node '{node.Id}': SpawnTemplate explicit position requires both 'a' (xCm) and 'b' (yCm) value edges.",
                            node.Id));
                        break;
                    }

                    if (hasX)
                    {
                        instruction.Flags = 1;
                        instruction.B = ResolveValueInput(
                            node, GraphControlFlowPorts.A, GraphValueType.Float,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                        instruction.C = ResolveValueInput(
                            node, GraphControlFlowPorts.B, GraphValueType.Float,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    }

                    break;

                case GraphNodeOp.SetWorldPosition:
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SetInteractionMode:
                    instruction.Imm = RequireSymbol(node.Mode, "mode", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.ActivateContext:
                    instruction.Imm = RequireSymbol(node.Context, "context", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = EncodeByteSymbol(node.ParentContext, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.DeactivateContext:
                    instruction.Imm = RequireSymbol(node.Context, "context", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.DispatchCollectionEvent:
                    instruction.Imm = RequireSymbol(node.Event, "event", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = EncodeByteSymbol(node.CollectionKey, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SetPanelAudience:
                    instruction.Imm = RequireSymbol(node.PanelType, "panelType", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = EncodeByteSymbol(node.PanelSeat, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.ReadMapVarInt:
                case GraphNodeOp.ReadMapVarFloat:
                    instruction.Imm = RequireSymbol(node.Var, "var", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.WriteMapVarInt:
                case GraphNodeOp.WriteMapVarFloat:
                    instruction.Imm = RequireSymbol(node.Var, "var", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value,
                        op.NodeOp == GraphNodeOp.WriteMapVarInt ? GraphValueType.Int : GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Source))
                        ? ResolveValueInput(
                            node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics)
                        : byte.MaxValue;
                    break;

                case GraphNodeOp.QueryCone:
                    instruction.Flags = 0;
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.ImmF = node.RangeCm;
                    ApplySpatialCapacityPolicy(node, droppedRegisters[nodeIndex], ref instruction);
                    break;

                case GraphNodeOp.QueryRectangle:
                    instruction.Flags = 0;
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = node.RotationDeg;
                    ApplySpatialCapacityPolicy(node, droppedRegisters[nodeIndex], ref instruction);
                    break;

                case GraphNodeOp.QueryLine:
                    instruction.Flags = 0;
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = node.HalfWidthCm;
                    ApplySpatialCapacityPolicy(node, droppedRegisters[nodeIndex], ref instruction);
                    break;

                case GraphNodeOp.QueryHexRange:
                case GraphNodeOp.QueryHexRing:
                    instruction.Flags = 0;
                    instruction.Imm = node.HexRadius;
                    ApplySpatialCapacityPolicy(node, droppedRegisters[nodeIndex], ref instruction);
                    break;

                case GraphNodeOp.QueryFilterLayer:
                    instruction.Imm = unchecked((int)node.LayerMask);
                    break;

                case GraphNodeOp.QueryFilterNotEntity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.QueryFilterRelationship:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = ParseLinearRelationshipFilterMode(node.RelationshipMode, node, graphId, diagnostics);
                    break;

                case GraphNodeOp.TargetListGet:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Flags = boolScratches[nodeIndex];
                    break;

                case GraphNodeOp.RelationshipEnsureLink:
                case GraphNodeOp.RelationshipRemoveLink:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Dst = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.RelationshipSetMetric:
                case GraphNodeOp.RelationshipAddMetric:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Metric, "metric", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = byte.MaxValue;
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.RelationshipGetMetric:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Metric, "metric", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.RelationshipHasFlag:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Flag, "flag", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.RelationshipSetFlag:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.C = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Bool,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Flag, "flag", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Dst = byte.MaxValue;
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.RelationshipHasLink:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Flags = RequireRelationshipTypeSymbol(node.RelationshipType, symbolToIndex, symbols, graphId, node.Id, diagnostics);
                    break;

                case GraphNodeOp.ClampTargetToRange:
                case GraphNodeOp.IsPointInCircle:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SnapToNearestInCollection:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.CollectionKey, "collectionKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = boolScratches[nodeIndex];
                    break;

                case GraphNodeOp.SnapToNearestGraphEdge:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadEventPayloadInt:
                case GraphNodeOp.LoadEventPayloadFloat:
                    instruction.Imm = node.Slot;
                    break;

                case GraphNodeOp.LoadEntryPayloadEntity:
                case GraphNodeOp.LoadEntryPayloadInt:
                case GraphNodeOp.LoadEntryPayloadFloat:
                    instruction.Imm = RequireSymbol(node.PayloadKey, "payloadKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.LoadPlacedEntity:
                case GraphNodeOp.LoadPlacedRegion:
                case GraphNodeOp.LoadPlacedAnchor:
                    instruction.Imm = RequireSymbol(node.InstanceId, "instanceId", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ControlDomainResolve:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.ControlDomainControls:
                case GraphNodeOp.KnowledgeHasProjection:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SendEvent:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Target, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.Value, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.Tag, "tag", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.InvokeScript:
                    instruction.Imm = RequireSymbol(node.FunctionName, "functionName", node, symbolToIndex, symbols, graphId, diagnostics);
                    instruction.Flags = GraphInstructionFlags.FuncLibName;
                    break;

                case GraphNodeOp.InvokeGraph:
                {
                    bool hasGraphId = node.GraphId > 0;
                    bool hasName = !string.IsNullOrWhiteSpace(node.FunctionName);
                    if (hasGraphId && hasName)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"InvokeGraph node '{node.Id}' cannot set both graphId and functionName.", node.Id));
                        break;
                    }

                    if (!hasGraphId && !hasName)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"InvokeGraph node '{node.Id}' requires functionName (graph key) or graphId.", node.Id));
                        break;
                    }

                    if (hasName)
                    {
                        instruction.Imm = Intern(symbolToIndex, symbols, node.FunctionName!.Trim());
                        instruction.Flags |= GraphInstructionFlags.FuncLibName;
                    }
                    else
                    {
                        instruction.Imm = node.GraphId;
                    }

                    if (!string.IsNullOrWhiteSpace(node.EntryLabel))
                    {
                        int labelSymbol = Intern(symbolToIndex, symbols, node.EntryLabel.Trim());
                        instruction.Flags |= 2;
                        instruction.B = (byte)(labelSymbol & 0xFF);
                        instruction.C = (byte)((labelSymbol >> 8) & 0xFF);
                    }

                    break;
                }

                case GraphNodeOp.StoreArgInt:
                case GraphNodeOp.StoreArgFloat:
                case GraphNodeOp.StoreArgEntity:
                    instruction.A = ResolveValueInput(
                        node,
                        GraphControlFlowPorts.Value,
                        ParamPortType(op.NodeOp == GraphNodeOp.StoreArgInt
                            ? Ludots.Core.Scripting.EventParamType.Int
                            : op.NodeOp == GraphNodeOp.StoreArgFloat
                                ? Ludots.Core.Scripting.EventParamType.Float
                                : Ludots.Core.Scripting.EventParamType.Entity),
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.ArgKey, "argKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.AwaitCallback:
                    instruction.Imm = RequireSymbol(node.CallbackType, "callbackType", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ConstText:
                    if (node.Text == null)
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"Node '{node.Id}' requires a text field (empty string is allowed).", node.Id));
                        return;
                    }

                    instruction.Imm = Intern(symbolToIndex, symbols, node.Text);
                    break;

                case GraphNodeOp.LoadTextKey:
                    instruction.Imm = RequireSymbol(node.TextKey, "textKey", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.OfferActivity:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.ActivityId, "activityId", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.OfferTask:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.Source, GraphValueType.Entity,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.Imm = RequireSymbol(node.TaskId, "taskId", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.StartDialogue:
                    instruction.Imm = RequireSymbol(node.DialogueId, "dialogueId", node, symbolToIndex, symbols, graphId, diagnostics);
                    break;

                case GraphNodeOp.ConcatText:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Text,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    instruction.B = ResolveValueInput(
                        node, GraphControlFlowPorts.B, GraphValueType.Text,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.IntToText:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Int,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.FloatToText:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Float,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    break;

                case GraphNodeOp.SinkPresentationText:
                    instruction.A = ResolveValueInput(
                        node, GraphControlFlowPorts.A, GraphValueType.Text,
                        valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                    if (!TryParsePresentationSurface(node.PresentationSurface, out GraphPresentationTextSurface surface))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.TypeMismatch,
                            $"SinkPresentationText node '{node.Id}' requires presentationSurface 'Subtitle' or 'Dialogue'.",
                            node.Id));
                        return;
                    }

                    instruction.Imm = (int)surface;
                    break;

                case GraphNodeOp.DispatchMapEvent:
                {
                    if (dispatchSchemas == null || !dispatchSchemas.TryGetValue(node.Id, out Ludots.Core.Scripting.EventSchema schema))
                    {
                        diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                            $"DispatchMapEvent node '{node.Id}' has no resolved event schema.", node.Id));
                        return;
                    }

                    // One StoreArg* per wired schema parameter (schema order), then the fire.
                    int emitIndex = bodyIndex;
                    for (int i = 0; i < schema.Params.Count; i++)
                    {
                        Ludots.Core.Scripting.EventParamSchema param = schema.Params[i];
                        if (param.Type == Ludots.Core.Scripting.EventParamType.String ||
                            !valueEdges.ContainsKey(new ValueInputKey(node.Id, param.Name)))
                        {
                            continue;
                        }

                        byte sourceRegister = ResolveValueInput(
                            node,
                            param.Name,
                            ParamPortType(param.Type),
                            valueEdges, nodeIndices, outputTypes, outputRegisters, boolScratches, droppedRegisters, definedInts, definedBools, graphId, diagnostics);
                        GraphNodeOp storeOp = param.Type switch
                        {
                            Ludots.Core.Scripting.EventParamType.Entity => GraphNodeOp.StoreArgEntity,
                            Ludots.Core.Scripting.EventParamType.Int => GraphNodeOp.StoreArgInt,
                            _ => GraphNodeOp.StoreArgFloat,
                        };
                        program[emitIndex] = new GraphInstruction
                        {
                            Op = (ushort)storeOp,
                            A = sourceRegister,
                            Imm = Intern(symbolToIndex, symbols, param.PayloadKey)
                        };
                        SetSource(sources, emitIndex, graphId, node, storeOp.ToString(), GraphControlFlowPorts.Enter);
                        emitIndex++;
                    }

                    string dispatchScope = (node.Scope ?? "map").Trim().ToLowerInvariant();
                    bool selfScope = string.Equals(dispatchScope, "self", StringComparison.Ordinal);
                    bool globalScope = string.Equals(dispatchScope, "global", StringComparison.Ordinal);
                    program[emitIndex] = new GraphInstruction
                    {
                        Op = (ushort)GraphNodeOp.DispatchMapEvent,
                        Imm = Intern(symbolToIndex, symbols, schema.EventName),
                        Flags = (byte)(globalScope ? 2 : selfScope ? 1 : 0)
                    };
                    SetSource(sources, emitIndex, graphId, node, nameof(GraphNodeOp.DispatchMapEvent), GraphControlFlowPorts.Enter);

                    if (controlEdges.ContainsKey(new ControlKey(node.Id, GraphControlFlowPorts.Next)))
                    {
                        EmitRelativeJump(
                            document,
                            node,
                            GraphControlFlowPorts.Next,
                            emitIndex + 1,
                            controlEdges,
                            nodeIndices,
                            layouts,
                            program,
                            sources,
                            graphId);
                    }
                    else if (btPlan != null && nodeIndex >= 0 && btPlan.IsChainTerminal(nodeIndex))
                    {
                        EmitBtLeafEpilogue(
                            btPlan,
                            node,
                            nodeIndex,
                            outputTypes[nodeIndex],
                            outputRegisters[nodeIndex],
                            emitIndex + 1,
                            program,
                            sources,
                            graphId);
                    }
                    else
                    {
                        EmitExplicitHalt(program, sources, emitIndex + 1, graphId, node);
                    }

                    return;
                }

                case GraphNodeOp.HaltReturnInt:
                    if (valueEdges.ContainsKey(new ValueInputKey(node.Id, GraphControlFlowPorts.Value)))
                    {
                        instruction.A = ResolveValueInput(
                            node,
                            GraphControlFlowPorts.Value,
                            GraphValueType.Int,
                            valueEdges,
                            nodeIndices,
                            outputTypes,
                            outputRegisters,
                            boolScratches,
                            droppedRegisters,
                            definedInts,
                            definedBools,
                            graphId,
                            diagnostics);
                    }

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
            else if (btPlan != null && nodeIndex >= 0 && btPlan.IsChainTerminal(nodeIndex))
            {
                EmitBtLeafEpilogue(
                    btPlan,
                    node,
                    nodeIndex,
                    outputTypes[nodeIndex],
                    outputRegisters[nodeIndex],
                    bodyIndex + 1,
                    program,
                    sources,
                    graphId);
            }
            else if (op.NodeOp != GraphNodeOp.HaltReturnInt)
            {
                EmitExplicitHalt(program, sources, bodyIndex + 1, graphId, node);
            }
        }

        private static bool TryParsePresentationSurface(string? authored, out GraphPresentationTextSurface surface)
        {
            surface = default;
            if (string.IsNullOrWhiteSpace(authored))
            {
                return false;
            }

            string trimmed = authored.Trim();
            return Enum.TryParse(trimmed, ignoreCase: false, out surface) &&
                   Enum.IsDefined(typeof(GraphPresentationTextSurface), surface) &&
                   string.Equals(surface.ToString(), trimmed, StringComparison.Ordinal);
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
                diagnostics.Add(Error(graphId, GraphDiagnosticCodes.MissingNodeRef,
                    $"Node '{nodeId}' requires a non-empty payloadPreset.", nodeId));
                return byte.MaxValue;
            }

            return EncodeByteSymbol(symbol, symbolToIndex, symbols, graphId, nodeId, diagnostics);
        }
    }
}
