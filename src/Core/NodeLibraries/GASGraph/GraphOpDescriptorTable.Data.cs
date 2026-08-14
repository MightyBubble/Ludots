using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static partial class GraphOpDescriptorTable
    {
        private const GraphKindMask LinearAll =
            GraphKindMask.Effect | GraphKindMask.Score | GraphKindMask.Validation | GraphKindMask.Derived;

        private const GraphKindMask LinearEffect = GraphKindMask.Effect;

        private const GraphKindMask LinearEffectDerived = GraphKindMask.Effect | GraphKindMask.Derived;

        private const GraphKindMask QueryOnly = GraphKindMask.Query;

        private const GraphKindMask ScriptOnlyMask = GraphKindMask.Script;

        private const GraphKindMask LinearAndQuery = LinearAll | GraphKindMask.Query;

        private const GraphKindMask LinearAndScript = LinearAll | GraphKindMask.Script;

        private const GraphKindMask LinearQueryScript = LinearAll | GraphKindMask.Query | GraphKindMask.Script;

        private const GraphKindMask EffectAndScript = GraphKindMask.Effect | GraphKindMask.Script;

        private static readonly string[] NoPorts = Array.Empty<string>();
        private static readonly string[] PortValue = { GraphControlFlowPorts.Value };
        private static readonly string[] PortAB = { GraphControlFlowPorts.A, GraphControlFlowPorts.B };
        private static readonly string[] PortSource = { GraphControlFlowPorts.Source };
        private static readonly string[] PortList = { GraphControlFlowPorts.List };
        private static readonly string[] PortTarget = { GraphControlFlowPorts.Target };
        private static readonly string[] PortTargetValue = { GraphControlFlowPorts.Target, GraphControlFlowPorts.Value };
        private static readonly string[] PortSourceValue = { GraphControlFlowPorts.Source, GraphControlFlowPorts.Value };
        private static readonly string[] PortSourceTarget = { GraphControlFlowPorts.Source, GraphControlFlowPorts.Target };
        private static readonly string[] PortSourceTargetValue =
        {
            GraphControlFlowPorts.Source, GraphControlFlowPorts.Target, GraphControlFlowPorts.Value
        };
        private static readonly string[] PortValueMinMax =
        {
            GraphControlFlowPorts.Value, GraphControlFlowPorts.Min, GraphControlFlowPorts.Max
        };
        private static readonly string[] PortConditionAB =
        {
            GraphControlFlowPorts.Condition, GraphControlFlowPorts.A, GraphControlFlowPorts.B
        };
        private static readonly string[] PortListTeamId =
        {
            GraphControlFlowPorts.List, GraphControlFlowPorts.TeamId
        };
        private static readonly string[] PortListMinMax =
        {
            GraphControlFlowPorts.List, GraphControlFlowPorts.Min, GraphControlFlowPorts.Max
        };
        private static readonly string[] PortListSource =
        {
            GraphControlFlowPorts.List, GraphControlFlowPorts.Source
        };
        private static readonly string[] PortListSourceMinMax =
        {
            GraphControlFlowPorts.List, GraphControlFlowPorts.Source,
            GraphControlFlowPorts.Min, GraphControlFlowPorts.Max
        };
        private static readonly string[] PortSourceB =
        {
            GraphControlFlowPorts.Source, GraphControlFlowPorts.B
        };
        private static readonly string[] PortApplyTemplate =
        {
            GraphControlFlowPorts.Target, GraphControlFlowPorts.A, GraphControlFlowPorts.B
        };
        private static readonly string[] PortA = { GraphControlFlowPorts.A };

        private static GraphOpDescriptor[] Build()
        {
            var rows = new List<GraphOpDescriptor>(160);
            Add(rows, GraphNodeOp.ConstBool, LinearAll, GraphValueType.Bool);
            Add(rows, GraphNodeOp.ConstInt, LinearAndScript, GraphValueType.Int, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ConstFloat, LinearAndQuery, GraphValueType.Float, queryOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.LoadCaster, LinearAndQuery, GraphValueType.Entity, queryOut: GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadExplicitTarget, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.Jump, ScriptOnlyMask, scriptPorts: NoPorts);
            Add(rows, GraphNodeOp.JumpIfFalse, EffectAndScript, scriptPorts: new[] { GraphControlFlowPorts.Condition });
            Add(rows, GraphNodeOp.LoadAttribute, LinearAll, GraphValueType.Float, PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AddFloat, LinearAll, GraphValueType.Float, PortAB);
            Add(rows, GraphNodeOp.MulFloat, LinearAll, GraphValueType.Float, PortAB);
            Add(rows, GraphNodeOp.SubFloat, LinearAll, GraphValueType.Float, PortAB);
            Add(rows, GraphNodeOp.DivFloat, LinearAll, GraphValueType.Float, PortAB);
            Add(rows, GraphNodeOp.MinFloat, LinearAll, GraphValueType.Float, PortAB);
            Add(rows, GraphNodeOp.MaxFloat, LinearAll, GraphValueType.Float, PortAB);
            Add(rows, GraphNodeOp.ClampFloat, LinearAll, GraphValueType.Float, PortValueMinMax);
            Add(rows, GraphNodeOp.AbsFloat, LinearAll, GraphValueType.Float, PortValue);
            Add(rows, GraphNodeOp.NegFloat, LinearAll, GraphValueType.Float, PortValue);
            Add(rows, GraphNodeOp.RandomFloat01, LinearAll, GraphValueType.Float);
            Add(rows, GraphNodeOp.AddInt, LinearAndScript, GraphValueType.Int, PortAB, scriptPorts: PortAB, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.CompareGtFloat, LinearAll, GraphValueType.Bool, PortAB);
            Add(rows, GraphNodeOp.CompareLtInt, LinearAndScript, GraphValueType.Bool, PortAB, scriptPorts: PortAB);
            Add(rows, GraphNodeOp.CompareEqInt, LinearAll, GraphValueType.Bool, PortAB);
            Add(rows, GraphNodeOp.HasTag, LinearAndQuery, GraphValueType.Bool, PortSource, queryOut: GraphValueType.Bool, queryPorts: PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.CompareEqEntity, LinearAndQuery, GraphValueType.Bool, PortAB, queryOut: GraphValueType.Bool, queryPorts: PortAB);
            Add(rows, GraphNodeOp.SelectTagInMask, LinearAndQuery, GraphValueType.Int, PortSource, queryOut: GraphValueType.Int, queryPorts: PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LookupTagDisplayToken, LinearAndQuery, GraphValueType.Int, PortA, queryOut: GraphValueType.Int, queryPorts: PortA, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.SelectEntity, LinearAll, GraphValueType.Entity, PortConditionAB);
            Add(rows, GraphNodeOp.QueryRadius, LinearAndQuery, GraphValueType.Void, queryOut: GraphValueType.TargetList, flags: GraphOperandRole.SpatialCapacityFlags, imm: GraphOperandRole.ImmediateFloat);
            Add(rows, GraphNodeOp.QuerySortStable, LinearAndQuery, GraphValueType.Void, queryOut: GraphValueType.TargetList, queryPorts: PortList);
            Add(rows, GraphNodeOp.QueryLimit, LinearAndQuery, GraphValueType.Void, queryOut: GraphValueType.TargetList, queryPorts: PortList, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryCone, LinearAll, GraphValueType.Void, PortAB, flags: GraphOperandRole.SpatialCapacityFlags);
            Add(rows, GraphNodeOp.QueryRectangle, LinearAll, GraphValueType.Void, PortAB, flags: GraphOperandRole.SpatialCapacityFlags);
            Add(rows, GraphNodeOp.QueryLine, LinearAll, GraphValueType.Void, PortAB, flags: GraphOperandRole.SpatialCapacityFlags);
            Add(rows, GraphNodeOp.QueryFilterNotEntity, LinearAll, GraphValueType.Void, PortSource);
            Add(rows, GraphNodeOp.QueryFilterLayer, LinearAll, GraphValueType.Void, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryFilterRelationship, LinearAll, GraphValueType.Void, PortSource, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.AggCount, LinearAndQuery, GraphValueType.Int, queryOut: GraphValueType.Int, queryPorts: PortList);
            Add(rows, GraphNodeOp.AggMinByDistance, LinearAndQuery, GraphValueType.Entity, queryOut: GraphValueType.Entity, queryPorts: PortList);
            Add(rows, GraphNodeOp.TargetListGet, LinearAll, GraphValueType.Entity, PortValue, flags: GraphOperandRole.BoolScratchFlags);
            Add(rows, GraphNodeOp.QueryHexRange, LinearAll, GraphValueType.Void, flags: GraphOperandRole.SpatialCapacityFlags, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryHexRing, LinearAll, GraphValueType.Void, flags: GraphOperandRole.SpatialCapacityFlags, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryHexNeighbors, LinearAll, GraphValueType.Void);
            Add(rows, GraphNodeOp.ApplyEffectTemplate, LinearEffect, GraphValueType.Void, PortApplyTemplate, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.FanOutApplyEffect, LinearEffect, GraphValueType.Void, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ApplyEffectDynamic, LinearEffect, GraphValueType.Void, PortTargetValue);
            Add(rows, GraphNodeOp.FanOutApplyEffectDynamic, LinearEffect, GraphValueType.Void, PortValue);
            Add(rows, GraphNodeOp.RemoveEffectTemplate, LinearEffect, GraphValueType.Void, PortTarget, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.FanOutDispatchEffect, LinearEffect, GraphValueType.Void, dst: GraphOperandRole.DispatchPresetDst, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.FanOutDispatchEffectDynamic, LinearEffect, GraphValueType.Void, PortValue, dst: GraphOperandRole.DispatchPresetDst);
            Add(rows, GraphNodeOp.ModifyAttributeAdd, LinearEffect, GraphValueType.Void, PortTargetValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.SendEvent, LinearEffect, GraphValueType.Void, PortTargetValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadBlackboardFloat, LinearAll, GraphValueType.Float, PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadBlackboardInt, LinearAll, GraphValueType.Int, PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadBlackboardEntity, LinearAll, GraphValueType.Entity, PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteBlackboardFloat, LinearEffect, GraphValueType.Void, PortSourceValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteBlackboardInt, LinearEffect, GraphValueType.Void, PortSourceValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteBlackboardEntity, LinearEffect, GraphValueType.Void, PortSourceValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadConfigFloat, LinearAll, GraphValueType.Float, imm: GraphOperandRole.SymbolImm, listenerOwner: true);
            Add(rows, GraphNodeOp.LoadConfigInt, LinearAll, GraphValueType.Int, imm: GraphOperandRole.SymbolImm, listenerOwner: true);
            Add(rows, GraphNodeOp.LoadConfigEffectId, LinearAll, GraphValueType.Int, imm: GraphOperandRole.SymbolImm, listenerOwner: true);
            Add(rows, GraphNodeOp.LoadContextSource, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadContextTarget, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadContextTargetContext, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadSelfAttribute, LinearAll, GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteSelfAttribute, LinearEffectDerived, GraphValueType.Void, PortValue, imm: GraphOperandRole.SymbolImm, derivedWrite: true);
            Add(rows, GraphNodeOp.RelationshipEnsureLink, LinearEffect, GraphValueType.Void, PortSourceTarget, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipRemoveLink, LinearEffect, GraphValueType.Void, PortSourceTarget, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipSetMetric, LinearEffect, GraphValueType.Void, PortSourceTargetValue, dst: GraphOperandRole.ReasonIdDst, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAddMetric, LinearEffect, GraphValueType.Void, PortSourceTargetValue, dst: GraphOperandRole.ReasonIdDst, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipGetMetric, LinearAll, GraphValueType.Int, PortSourceTarget, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipHasFlag, LinearAndQuery, GraphValueType.Bool, PortSourceTarget, queryOut: GraphValueType.Bool, queryPorts: PortSourceTarget, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipSetFlag, LinearEffect, GraphValueType.Void, PortSourceTargetValue, dst: GraphOperandRole.ReasonIdDst, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipQueryOutgoing, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortSource, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipQueryIncoming, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortSource, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipQueryMutual, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortSourceB, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipQueryBetweenPair, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortSourceB, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipFilterMetricRange, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortListSourceMinMax, dst: GraphOperandRole.SymbolDst, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipFilterFlag, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortListSource, dst: GraphOperandRole.SymbolDst, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipSortByMetric, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortListSource, dst: GraphOperandRole.SymbolDst, flags: GraphOperandRole.SortDescendingFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggSumMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: PortListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMaxMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: PortListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggAverageMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: PortListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryAllMapEntities, QueryOnly, queryOut: GraphValueType.TargetList);
            Add(rows, GraphNodeOp.QueryFromCollection, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterTeam, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortListTeamId, flags: GraphOperandRole.TeamIdSourceFlags);
            Add(rows, GraphNodeOp.QueryFilterTemplate, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterAttributeRange, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortListMinMax, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterTagAny, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterTagNone, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QuerySortByAttribute, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: PortList, flags: GraphOperandRole.SortDescendingFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggSumAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggAverageAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMaxAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMinAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMaxEntityByAttribute, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMinEntityByAttribute, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: PortList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMinMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: PortListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMaxEntityByMetric, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: PortListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMinEntityByMetric, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: PortListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipHasLink, LinearAndQuery, GraphValueType.Bool, PortSourceTarget, queryOut: GraphValueType.Bool, queryPorts: PortSourceTarget, flags: GraphOperandRole.RelationshipTypeFlags);
            Add(rows, GraphNodeOp.BeginLifecycleTransaction, LinearEffect, GraphValueType.Void);
            Add(rows, GraphNodeOp.InvokeBuiltin, LinearEffect, GraphValueType.Void, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadTargetPosX, LinearAll, GraphValueType.Int);
            Add(rows, GraphNodeOp.LoadTargetPosY, LinearAll, GraphValueType.Int);
            Add(rows, GraphNodeOp.ClampTargetToRange, LinearAll, GraphValueType.Bool, PortAB);
            Add(rows, GraphNodeOp.IsPointInCircle, LinearAll, GraphValueType.Bool, PortAB);
            Add(rows, GraphNodeOp.SnapToNearestInCollection, LinearAll, GraphValueType.Entity, PortSourceValue, flags: GraphOperandRole.BoolScratchFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.SnapToNearestGraphEdge, LinearAll, GraphValueType.Bool, PortValue);
            Add(rows, GraphNodeOp.LoadViewer, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadEventPayloadInt, LinearAll, GraphValueType.Int, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.LoadEventPayloadFloat, LinearAll, GraphValueType.Float, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.ControlDomainResolve, LinearAll, GraphValueType.Entity, PortSource);
            Add(rows, GraphNodeOp.ControlDomainControls, LinearAll, GraphValueType.Bool, PortAB);
            Add(rows, GraphNodeOp.KnowledgeHasProjection, LinearAll, GraphValueType.Bool, PortAB);
            Add(rows, GraphNodeOp.Call, ScriptOnlyMask, scriptPorts: NoPorts, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.Return, ScriptOnlyMask, scriptPorts: NoPorts);
            Add(rows, GraphNodeOp.Yield, ScriptOnlyMask, scriptPorts: NoPorts, scriptOnly: true);
            Add(rows, GraphNodeOp.HaltReturnInt, ScriptOnlyMask, scriptPorts: PortValue, scriptOut: GraphValueType.Void);
            Add(rows, GraphNodeOp.InvokeScript, LinearQueryScript, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, flags: GraphOperandRole.FuncLibNameFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.MoveInt, ScriptOnlyMask, GraphValueType.Int, scriptPorts: PortValue, scriptOut: GraphValueType.Int);

            var table = new GraphOpDescriptor[GraphVmLimits.HandlerTableSize];
            for (int i = 0; i < rows.Count; i++)
            {
                GraphOpDescriptor row = rows[i];
                ushort code = (ushort)row.Op;
                if (table[code].Op != GraphNodeOp.None)
                {
                    throw new InvalidOperationException($"Duplicate graph op descriptor for '{row.Op}'.");
                }

                table[code] = row;
            }

            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                if (op == GraphNodeOp.None)
                {
                    continue;
                }

                if (table[(ushort)op].Op != op)
                {
                    throw new InvalidOperationException($"Graph opcode '{op}' is missing a descriptor row.");
                }
            }

            return table;
        }

        private static void Add(
            List<GraphOpDescriptor> rows,
            GraphNodeOp op,
            GraphKindMask authorable,
            GraphValueType linearOut = GraphValueType.Void,
            string[]? linearPorts = null,
            GraphValueType queryOut = GraphValueType.Void,
            string[]? queryPorts = null,
            string[]? scriptPorts = null,
            GraphValueType scriptOut = GraphValueType.Void,
            GraphOperandRole dst = GraphOperandRole.None,
            GraphOperandRole flags = GraphOperandRole.None,
            GraphOperandRole imm = GraphOperandRole.None,
            bool scriptOnly = false,
            bool derivedWrite = false,
            bool listenerOwner = false)
        {
            if (dst == GraphOperandRole.None &&
                (linearOut != GraphValueType.Void || queryOut != GraphValueType.Void || scriptOut != GraphValueType.Void))
            {
                dst = GraphOperandRole.DstRegister;
            }

            rows.Add(new GraphOpDescriptor(
                op,
                authorable,
                linearOut,
                queryOut,
                linearPorts ?? NoPorts,
                queryPorts ?? NoPorts,
                scriptPorts ?? NoPorts,
                dst,
                flags,
                imm,
                scriptOnly,
                derivedWrite,
                listenerOwner));
        }
    }
}
