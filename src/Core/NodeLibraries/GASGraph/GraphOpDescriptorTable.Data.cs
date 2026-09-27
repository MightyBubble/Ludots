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

        // Named per-op TriggerGraph authoring carve-outs over the Query-class set (never a
        // class-wide mask widening): the aimsource kernel ops and the collection-query
        // seed/filter family are the read-side vocabulary input-event TriggerGraphs need.
        // The runtime triple is kind=TriggerGraph (this mask bit) && op listed by name on a
        // QueryAndTriggerGraph row && Pure effect metadata; every other Query-class op stays
        // Query-only.
        private const GraphKindMask QueryAndTriggerGraph = GraphKindMask.Query | GraphKindMask.TriggerGraph;

        // TriggerGraph mirrors the Script authorable set, including Yield (host resumption is the host's contract).
        private const GraphKindMask ScriptAndTriggerGraph = GraphKindMask.Script | GraphKindMask.TriggerGraph;
        private const GraphKindMask TriggerGraphOnly = GraphKindMask.TriggerGraph;
        private const GraphKindMask ScriptTriggerQuery = ScriptAndTriggerGraph | GraphKindMask.Query;

        private const GraphKindMask LinearAndQuery = LinearAll | GraphKindMask.Query;

        private const GraphKindMask LinearAndScript = LinearAll | ScriptAndTriggerGraph;

        private const GraphKindMask LinearScriptAndQuery = LinearAndScript | GraphKindMask.Query;

        private const GraphKindMask LinearQueryScript = LinearAll | GraphKindMask.Query | ScriptAndTriggerGraph;

        private const GraphKindMask EffectAndScript = GraphKindMask.Effect | ScriptAndTriggerGraph;
        private const GraphKindMask EffectAndScriptOnly = GraphKindMask.Effect | GraphKindMask.Script;
        private const GraphKindMask EffectAndTriggerGraph = GraphKindMask.Effect | GraphKindMask.TriggerGraph;

        private static GraphOpDescriptor[] Build()
        {
            string[] noPorts = Array.Empty<string>();
            string[] portValue = { GraphControlFlowPorts.Value };
            string[] portAB = { GraphControlFlowPorts.A, GraphControlFlowPorts.B };
            string[] portSourceAB = { GraphControlFlowPorts.Source, GraphControlFlowPorts.A, GraphControlFlowPorts.B };
            string[] portSource = { GraphControlFlowPorts.Source };
            string[] portList = { GraphControlFlowPorts.List };
            string[] portTarget = { GraphControlFlowPorts.Target };
            string[] portTargetValue = { GraphControlFlowPorts.Target, GraphControlFlowPorts.Value };
            string[] portSourceValue = { GraphControlFlowPorts.Source, GraphControlFlowPorts.Value };
            string[] portSourceTarget = { GraphControlFlowPorts.Source, GraphControlFlowPorts.Target };
            string[] portSourceTargetValue =
            {
                GraphControlFlowPorts.Source, GraphControlFlowPorts.Target, GraphControlFlowPorts.Value
            };
            string[] portValueMinMax =
            {
                GraphControlFlowPorts.Value, GraphControlFlowPorts.Min, GraphControlFlowPorts.Max
            };
            string[] portConditionAB =
            {
                GraphControlFlowPorts.Condition, GraphControlFlowPorts.A, GraphControlFlowPorts.B
            };
            string[] portListTeamId =
            {
                GraphControlFlowPorts.List, GraphControlFlowPorts.TeamId
            };
            string[] portListMinMax =
            {
                GraphControlFlowPorts.List, GraphControlFlowPorts.Min, GraphControlFlowPorts.Max
            };
            string[] portListSource =
            {
                GraphControlFlowPorts.List, GraphControlFlowPorts.Source
            };
            string[] portListSourceMinMax =
            {
                GraphControlFlowPorts.List, GraphControlFlowPorts.Source,
                GraphControlFlowPorts.Min, GraphControlFlowPorts.Max
            };
            string[] portSourceB =
            {
                GraphControlFlowPorts.Source, GraphControlFlowPorts.B
            };
            string[] portApplyTemplate =
            {
                GraphControlFlowPorts.Target, GraphControlFlowPorts.A, GraphControlFlowPorts.B
            };
            string[] portA = { GraphControlFlowPorts.A };
            string[] portRect =
            {
                GraphControlFlowPorts.List, GraphControlFlowPorts.A, GraphControlFlowPorts.B,
                GraphControlFlowPorts.C, GraphControlFlowPorts.Max
            };
            string[] portRectPick =
            {
                GraphControlFlowPorts.List, GraphControlFlowPorts.A, GraphControlFlowPorts.B,
                GraphControlFlowPorts.C, GraphControlFlowPorts.Max, GraphControlFlowPorts.Tolerance
            };
            string[] portRectCorners =
            {
                GraphControlFlowPorts.A, GraphControlFlowPorts.B, GraphControlFlowPorts.C, GraphControlFlowPorts.Max
            };
            string[] portTeamId = { GraphControlFlowPorts.TeamId };
            string[] portMinMax = { GraphControlFlowPorts.Min, GraphControlFlowPorts.Max };
            string[] portTargetCondition = { GraphControlFlowPorts.Target, GraphControlFlowPorts.Condition };

            var rows = new List<GraphOpDescriptor>(160);
            Add(rows, GraphNodeOp.ConstBool, LinearAll, GraphValueType.Bool);
            Add(rows, GraphNodeOp.ConstInt, LinearQueryScript, GraphValueType.Int, scriptOut: GraphValueType.Int, queryOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ConstFloat, LinearQueryScript, GraphValueType.Float, queryOut: GraphValueType.Float, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.LoadCaster, LinearQueryScript, GraphValueType.Entity, queryOut: GraphValueType.Entity, scriptOut: GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadExplicitTarget, LinearAndScript, GraphValueType.Entity, scriptOut: GraphValueType.Entity);
            Add(rows, GraphNodeOp.Jump, ScriptAndTriggerGraph, scriptPorts: noPorts);
            Add(rows, GraphNodeOp.JumpIfFalse, EffectAndScript, scriptPorts: new[] { GraphControlFlowPorts.Condition });
            Add(rows, GraphNodeOp.LoadAttribute, LinearAndScript, GraphValueType.Float, portSource, scriptPorts: portSource, scriptOut: GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AddFloat, LinearAndScript, GraphValueType.Float, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.MulFloat, LinearAndScript, GraphValueType.Float, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.SubFloat, LinearAndScript, GraphValueType.Float, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.DivFloat, LinearAndScript, GraphValueType.Float, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.MinFloat, LinearAll, GraphValueType.Float, portAB);
            Add(rows, GraphNodeOp.MaxFloat, LinearAndScript, GraphValueType.Float, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.ClampFloat, LinearAll, GraphValueType.Float, portValueMinMax);
            Add(rows, GraphNodeOp.AbsFloat, LinearAll, GraphValueType.Float, portValue);
            Add(rows, GraphNodeOp.NegFloat, LinearAll, GraphValueType.Float, portValue);
            Add(rows, GraphNodeOp.RandomFloat01, LinearAll, GraphValueType.Float);
            Add(rows, GraphNodeOp.WeightedPick, LinearAndScript, GraphValueType.Int, portValue, scriptPorts: portValue, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AddInt, LinearAndScript, GraphValueType.Int, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.SubInt, LinearAndScript, GraphValueType.Int, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.CompareGtFloat, LinearAndScript, GraphValueType.Bool, portAB, scriptPorts: portAB, scriptOut: GraphValueType.Bool);
            Add(rows, GraphNodeOp.CompareLtInt, LinearAndScript, GraphValueType.Bool, portAB, scriptPorts: portAB);
            Add(rows, GraphNodeOp.CompareEqInt, LinearAndScript, GraphValueType.Bool, portAB, scriptPorts: portAB);
            Add(rows, GraphNodeOp.HasTag, LinearQueryScript, GraphValueType.Bool, portSource, queryOut: GraphValueType.Bool, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Bool, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.CompareEqEntity, LinearAndQuery, GraphValueType.Bool, portAB, queryOut: GraphValueType.Bool, queryPorts: portAB);
            Add(rows, GraphNodeOp.SelectEntity, LinearAll, GraphValueType.Entity, portConditionAB);
            Add(rows, GraphNodeOp.QueryRadius, LinearQueryScript, GraphValueType.Void, queryOut: GraphValueType.TargetList, flags: GraphOperandRole.SpatialCapacityFlags, imm: GraphOperandRole.ImmediateFloat);
            Add(rows, GraphNodeOp.QuerySortStable, LinearQueryScript, GraphValueType.Void, queryOut: GraphValueType.TargetList, queryPorts: portList);
            Add(rows, GraphNodeOp.QueryLimit, LinearQueryScript, GraphValueType.Void, queryOut: GraphValueType.TargetList, queryPorts: portList, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryCone, LinearAll, GraphValueType.Void, portAB, flags: GraphOperandRole.SpatialCapacityFlags);
            Add(rows, GraphNodeOp.QueryRectangle, LinearAll, GraphValueType.Void, portAB, flags: GraphOperandRole.SpatialCapacityFlags);
            Add(rows, GraphNodeOp.QueryLine, LinearAll, GraphValueType.Void, portAB, flags: GraphOperandRole.SpatialCapacityFlags);
            Add(rows, GraphNodeOp.QueryFilterNotEntity, LinearAll, GraphValueType.Void, portSource);
            Add(rows, GraphNodeOp.QueryFilterLayer, LinearAll, GraphValueType.Void, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryFilterRelationship, LinearAll, GraphValueType.Void, portSource, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.AggCount, LinearQueryScript, GraphValueType.Int, queryOut: GraphValueType.Int, queryPorts: portList, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.AggMinByDistance, LinearQueryScript, GraphValueType.Entity, queryOut: GraphValueType.Entity, queryPorts: portList, scriptOut: GraphValueType.Entity);
            Add(rows, GraphNodeOp.TargetListGet, LinearAndScript, GraphValueType.Entity, portValue, scriptPorts: portValue, scriptOut: GraphValueType.Entity, flags: GraphOperandRole.BoolScratchFlags);
            Add(rows, GraphNodeOp.QueryHexRange, LinearAll, GraphValueType.Void, flags: GraphOperandRole.SpatialCapacityFlags, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryHexRing, LinearAll, GraphValueType.Void, flags: GraphOperandRole.SpatialCapacityFlags, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.QueryHexNeighbors, LinearAll, GraphValueType.Void);
            // Effect-transactional: Script hosts may apply templates (#1536 behavior hosts),
            // but the TriggerGraph dialect stays effect-free by contract.
            Add(rows, GraphNodeOp.ApplyEffectTemplate, EffectAndScriptOnly, GraphValueType.Void, portApplyTemplate, scriptPorts: portApplyTemplate, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.FanOutApplyEffect, LinearEffect, GraphValueType.Void, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ApplyEffectDynamic, LinearEffect, GraphValueType.Void, portTargetValue);
            Add(rows, GraphNodeOp.FanOutApplyEffectDynamic, LinearEffect, GraphValueType.Void, portValue);
            Add(rows, GraphNodeOp.RemoveEffectTemplate, LinearEffect, GraphValueType.Void, portTarget, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.FanOutDispatchEffect, LinearEffect, GraphValueType.Void, dst: GraphOperandRole.DispatchPresetDst, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.FanOutDispatchEffectDynamic, LinearEffect, GraphValueType.Void, portValue, dst: GraphOperandRole.DispatchPresetDst);
            Add(rows, GraphNodeOp.ModifyAttributeAdd, EffectAndScript, GraphValueType.Void, portTargetValue, imm: GraphOperandRole.SymbolImm, scriptPorts: portTargetValue);
            Add(rows, GraphNodeOp.SendEvent, LinearEffect, GraphValueType.Void, portTargetValue, imm: GraphOperandRole.SymbolImm);
            // ReadBlackboardFloat includes Query: Case E box_hit (Query) reads press corners from the operator rep.
            Add(rows, GraphNodeOp.ReadBlackboardFloat, LinearScriptAndQuery, GraphValueType.Float, portSource, queryOut: GraphValueType.Float, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadBlackboardInt, LinearAndScript, GraphValueType.Int, portSource, scriptPorts: portSource, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadBlackboardEntity, LinearAndScript, GraphValueType.Entity, portSource, scriptPorts: portSource, scriptOut: GraphValueType.Entity, imm: GraphOperandRole.SymbolImm);
            // WriteBlackboardFloat includes TriggerGraph: Case E box_begin stores press corners on the operator rep.
            Add(rows, GraphNodeOp.WriteBlackboardFloat, EffectAndScript, GraphValueType.Void, portSourceValue, scriptPorts: portSourceValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteBlackboardInt, EffectAndScript, GraphValueType.Void, portSourceValue, scriptPorts: portSourceValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteBlackboardEntity, EffectAndScript, GraphValueType.Void, portSourceValue, scriptPorts: portSourceValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadConfigFloat, LinearAll, GraphValueType.Float, imm: GraphOperandRole.SymbolImm, listenerOwner: true);
            Add(rows, GraphNodeOp.LoadConfigInt, LinearAll, GraphValueType.Int, imm: GraphOperandRole.SymbolImm, listenerOwner: true);
            Add(rows, GraphNodeOp.LoadConfigEffectId, LinearAll, GraphValueType.Int, imm: GraphOperandRole.SymbolImm, listenerOwner: true);
            Add(rows, GraphNodeOp.ResolveTableRow, LinearQueryScript, GraphValueType.Int, portA, queryOut: GraphValueType.Int, queryPorts: portA, scriptPorts: portA, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.TableReadInt, LinearQueryScript, GraphValueType.Int, portA, queryOut: GraphValueType.Int, queryPorts: portA, scriptPorts: portA, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            // World / presentation side effects: Effect gallery + Script/TriggerGraph hosts only.
            // Must not sit on LinearQueryScript — that mask leaked them into Score/Validation
            // authoring while metadata still said Pure (#1410).
            Add(rows, GraphNodeOp.ShowPanel, EffectAndScript, GraphValueType.Void, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.HidePanel, EffectAndScript, GraphValueType.Void, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.CreatePanel, EffectAndScript, GraphValueType.Void, portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, dst: GraphOperandRole.SymbolDst, worldSideEffect: true);
            Add(rows, GraphNodeOp.SpawnTemplate, EffectAndScript, GraphValueType.Void, portSourceAB, scriptPorts: portSourceAB, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.SetWorldPosition, EffectAndScript, GraphValueType.Void, portSourceAB, scriptPorts: portSourceAB, worldSideEffect: true);
            Add(rows, GraphNodeOp.SetInteractionMode, EffectAndScript, GraphValueType.Void, portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.ActivateContext, EffectAndScript, GraphValueType.Void, portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, dst: GraphOperandRole.SymbolDst, worldSideEffect: true);
            Add(rows, GraphNodeOp.DeactivateContext, EffectAndScript, GraphValueType.Void, portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            // Host TriggerGraphs write collections themselves (pure Query callees leave the TargetList
            // to the caller; GraphReturnWriter must not steal collection writes).
            Add(rows, GraphNodeOp.WriteCollection, ScriptAndTriggerGraph, GraphValueType.Void, portValue, scriptPorts: portValue, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            // Input-edge order bridge (constitution §12): TriggerGraph-only — the submit op is
            // the graph side of the intent buffer and never routes inline.
            Add(rows, GraphNodeOp.SubmitCommandIntent, TriggerGraphOnly, GraphValueType.Void, scriptPorts: portTargetCondition, worldSideEffect: true);
            string[] portSlotTargetGround = { GraphControlFlowPorts.Value, GraphControlFlowPorts.Target, GraphControlFlowPorts.Condition };
            string[] portSlotTarget = { GraphControlFlowPorts.Value, GraphControlFlowPorts.Target };
            // Cast side of the §12 order bridge: slot lands as Args.I0 on each active-set member.
            Add(rows, GraphNodeOp.SubmitCast, TriggerGraphOnly, GraphValueType.Void, scriptPorts: portSlotTargetGround, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.SubmitEngageBatch, TriggerGraphOnly, GraphValueType.Void, scriptPorts: portSlotTarget, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            // to the caller; GraphReturnWriter must not steal collection writes). Optional source
            // resolves the owner entity (defaults to caster) — map-domain observers writing another
            // rep's collection declare it explicitly.

            Add(rows, GraphNodeOp.BindQueryCollection, ScriptAndTriggerGraph, GraphValueType.Void, portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.SetPanelAudience, EffectAndScript, GraphValueType.Void, imm: GraphOperandRole.SymbolImm, dst: GraphOperandRole.SymbolDst, worldSideEffect: true);
            Add(rows, GraphNodeOp.ModifyAttributeSet, EffectAndTriggerGraph, GraphValueType.Void, portTargetValue, scriptPorts: portTargetValue, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.OfferActivity, ScriptAndTriggerGraph, GraphValueType.Void, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.OfferTask, ScriptAndTriggerGraph, GraphValueType.Void, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.DestroyPanel, EffectAndScript, GraphValueType.Void, portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.ReadMapVarInt, ScriptTriggerQuery, GraphValueType.Int, portSource, queryOut: GraphValueType.Int, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadMapVarFloat, ScriptTriggerQuery, GraphValueType.Float, portSource, queryOut: GraphValueType.Float, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteMapVarInt, ScriptAndTriggerGraph, GraphValueType.Void, portSourceValue, scriptPorts: portSourceValue, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.WriteMapVarFloat, ScriptAndTriggerGraph, GraphValueType.Void, portSourceValue, scriptPorts: portSourceValue, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.TableReadFloat, LinearQueryScript, GraphValueType.Float, portA, queryOut: GraphValueType.Float, queryPorts: portA, scriptPorts: portA, scriptOut: GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadContextSource, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadContextTarget, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadContextTargetContext, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadSelfAttribute, LinearAndScript | QueryOnly, GraphValueType.Float, scriptOut: GraphValueType.Float, queryOut: GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.WriteSelfAttribute, LinearEffectDerived, GraphValueType.Void, portValue, imm: GraphOperandRole.SymbolImm, derivedWrite: true);
            Add(rows, GraphNodeOp.RelationshipEnsureLink, EffectAndScript, GraphValueType.Void, portSourceTarget, scriptPorts: portSourceTarget, dst: GraphOperandRole.SymbolDst, worldSideEffect: true);
            Add(rows, GraphNodeOp.RelationshipRemoveLink, EffectAndScript, GraphValueType.Void, portSourceTarget, scriptPorts: portSourceTarget, dst: GraphOperandRole.SymbolDst, worldSideEffect: true);
            Add(rows, GraphNodeOp.RelationshipSetMetric, LinearEffect, GraphValueType.Void, portSourceTargetValue, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAddMetric, LinearEffect, GraphValueType.Void, portSourceTargetValue, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipGetMetric, LinearAndScript, GraphValueType.Int, portSourceTarget, scriptPorts: portSourceTarget, scriptOut: GraphValueType.Int, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipHasFlag, LinearScriptAndQuery, GraphValueType.Bool, portSourceTarget, queryOut: GraphValueType.Bool, queryPorts: portSourceTarget, scriptPorts: portSourceTarget, scriptOut: GraphValueType.Bool, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipSetFlag, LinearEffect, GraphValueType.Void, portSourceTargetValue, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipQueryOutgoing, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSource, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipQueryIncoming, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSource, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipQueryMutual, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSourceB, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipQueryBetweenPair, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSourceB, dst: GraphOperandRole.SymbolDst);
            Add(rows, GraphNodeOp.RelationshipFilterMetricRange, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portListSourceMinMax, dst: GraphOperandRole.SymbolDst, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipFilterFlag, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portListSource, dst: GraphOperandRole.SymbolDst, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipSortByMetric, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portListSource, dst: GraphOperandRole.SymbolDst, flags: GraphOperandRole.SortDescendingFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggSumMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: portListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMaxMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: portListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggAverageMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: portListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            // D15 治根：集合查询读面 = Query | Script | TriggerGraph。Script 与 TriggerGraph
            // 共用同一线性（Script 镜像）编译路径；此前 Script 能写集合（WriteCollection 含
            // Script）却读不回来，是 D2 按需放行时遗留的不对称。槽图（函数体）读交接集合
            // 因此不需要伪装成 TriggerGraph。Query 纯函数禁写不变量不受影响（写操作不含 Query）。
            Add(rows, GraphNodeOp.QueryAllMapEntities, ScriptTriggerQuery, queryOut: GraphValueType.TargetList);
            Add(rows, GraphNodeOp.QueryFromCollection, ScriptTriggerQuery, queryOut: GraphValueType.TargetList, queryPorts: portSource, scriptPorts: portSource, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryCollectActiveEffects, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectEffectTemplates, QueryOnly, queryOut: GraphValueType.IntIdList);
            Add(rows, GraphNodeOp.QueryCollectAbilitySlots, QueryOnly, queryOut: GraphValueType.IntIdList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectInventoryItems, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectItemDefinitions, QueryOnly, queryOut: GraphValueType.IntIdList);
            Add(rows, GraphNodeOp.QueryCollectPresentTags, QueryOnly, queryOut: GraphValueType.IntIdList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectActiveTasks, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectActiveActivities, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectProgressionNodes, QueryOnly, queryOut: GraphValueType.IntIdList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryCollectAbilityHolders, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryCollectActiveDialogueChoices, QueryOnly, queryOut: GraphValueType.IntIdList);
            // ── Aimsource pure helpers: read-only screen/pointer/stick math for aim graphs ──
            Add(rows, GraphNodeOp.ScreenPointToGround, QueryAndTriggerGraph, GraphValueType.Bool, queryOut: GraphValueType.Bool, queryPorts: portAB, scriptPorts: portAB);
            Add(rows, GraphNodeOp.ScreenPointToEntity, QueryAndTriggerGraph, GraphValueType.Entity, queryOut: GraphValueType.Entity, queryPorts: portSourceAB, scriptPorts: portSourceAB, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadPointerScreenX, LinearQueryScript, GraphValueType.Float, queryOut: GraphValueType.Float, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.LoadPointerScreenY, LinearQueryScript, GraphValueType.Float, queryOut: GraphValueType.Float, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.ScreenRegionToEntities, QueryAndTriggerGraph, GraphValueType.Void, queryPorts: portRectPick, scriptPorts: portRectCorners, queryOut: GraphValueType.TargetList, flags: GraphOperandRole.SrcRegisterFlags);
            Add(rows, GraphNodeOp.QueryScreenRegionCollection, QueryAndTriggerGraph, GraphValueType.Void,
                queryPorts: new[] { GraphControlFlowPorts.Source, GraphControlFlowPorts.A, GraphControlFlowPorts.B, GraphControlFlowPorts.C, GraphControlFlowPorts.Max },
                scriptPorts: new[] { GraphControlFlowPorts.Source, GraphControlFlowPorts.A, GraphControlFlowPorts.B, GraphControlFlowPorts.C, GraphControlFlowPorts.Max },
                queryOut: GraphValueType.TargetList, flags: GraphOperandRole.SrcRegisterFlags);
            Add(rows, GraphNodeOp.PointToDirection, QueryAndTriggerGraph, GraphValueType.Float, queryOut: GraphValueType.Float, queryPorts: portSource, scriptPorts: portSource, flags: GraphOperandRole.BoolScratchFlags);
            Add(rows, GraphNodeOp.StickToDirection, QueryAndTriggerGraph, GraphValueType.Float, queryOut: GraphValueType.Float, queryPorts: portAB, scriptPorts: portAB, flags: GraphOperandRole.BoolScratchFlags);
            Add(rows, GraphNodeOp.LoadEffectTiming, LinearAndScript | QueryOnly, GraphValueType.Float, scriptOut: GraphValueType.Float, queryOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.LoadEffectStack, LinearAndScript | QueryOnly, GraphValueType.Float, scriptOut: GraphValueType.Float, queryOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.QueryFilterTeam, ScriptTriggerQuery, queryOut: GraphValueType.TargetList, queryPorts: portListTeamId, scriptPorts: portTeamId, flags: GraphOperandRole.TeamIdSourceFlags);
            Add(rows, GraphNodeOp.QueryFilterTemplate, ScriptTriggerQuery, queryOut: GraphValueType.TargetList, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterKnowledgeVisible, QueryAndTriggerGraph, queryOut: GraphValueType.TargetList, queryPorts: portSource);
            Add(rows, GraphNodeOp.QueryFilterSelectable, QueryAndTriggerGraph, queryOut: GraphValueType.TargetList);
            Add(rows, GraphNodeOp.QueryFilterAttributeRange, ScriptTriggerQuery, queryOut: GraphValueType.TargetList, queryPorts: portListMinMax, scriptPorts: portMinMax, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterTagAny, ScriptTriggerQuery, queryOut: GraphValueType.TargetList, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QueryFilterTagNone, ScriptTriggerQuery, queryOut: GraphValueType.TargetList, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.QuerySortByAttribute, QueryOnly, queryOut: GraphValueType.TargetList, queryPorts: portList, flags: GraphOperandRole.SortDescendingFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggSumAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggAverageAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMaxAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMinAttribute, QueryOnly, queryOut: GraphValueType.Float, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMaxEntityByAttribute, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AggMinEntityByAttribute, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: portList, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMinMetric, QueryOnly, queryOut: GraphValueType.Int, queryPorts: portListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMaxEntityByMetric, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: portListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipAggMinEntityByMetric, QueryOnly, queryOut: GraphValueType.Entity, queryPorts: portListSource, flags: GraphOperandRole.RelationshipTypeFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.RelationshipHasLink, LinearScriptAndQuery, GraphValueType.Bool, portSourceTarget, queryOut: GraphValueType.Bool, queryPorts: portSourceTarget, scriptPorts: portSourceTarget, scriptOut: GraphValueType.Bool, flags: GraphOperandRole.RelationshipTypeFlags);
            Add(rows, GraphNodeOp.BeginLifecycleTransaction, LinearEffect, GraphValueType.Void);
            Add(rows, GraphNodeOp.InvokeBuiltin, LinearEffect, GraphValueType.Void, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadTargetPosX, LinearAndScript, GraphValueType.Int);
            Add(rows, GraphNodeOp.LoadTargetPosY, LinearAndScript, GraphValueType.Int);
            Add(rows, GraphNodeOp.ClampTargetToRange, LinearAll, GraphValueType.Bool, portAB);
            Add(rows, GraphNodeOp.IsPointInCircle, LinearAll, GraphValueType.Bool, portAB);
            Add(rows, GraphNodeOp.SnapToNearestInCollection, LinearAll, GraphValueType.Entity, portSourceValue, flags: GraphOperandRole.BoolScratchFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.SnapToNearestGraphEdge, LinearAll, GraphValueType.Bool, portValue);
            Add(rows, GraphNodeOp.LoadViewer, LinearAll, GraphValueType.Entity);
            Add(rows, GraphNodeOp.LoadEventPayloadInt, LinearAll, GraphValueType.Int, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.LoadEventPayloadFloat, LinearAll, GraphValueType.Float, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.LoadEntryPayloadEntity, TriggerGraphOnly, GraphValueType.Entity, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadEntryPayloadInt, TriggerGraphOnly, GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadEntryPayloadFloat, TriggerGraphOnly, GraphValueType.Float, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadPlacedEntity, TriggerGraphOnly, GraphValueType.Entity, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadPlacedRegion, TriggerGraphOnly, GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadPlacedAnchor, TriggerGraphOnly, GraphValueType.Entity, imm: GraphOperandRole.SymbolImm);
            // InvokeGraph Imm is a compile-time graph id literal (not a symbol); Flags bit 0 marks
            // an authored entry label whose symbol index is packed as B | (C << 8).
            Add(rows, GraphNodeOp.InvokeGraph, TriggerGraphOnly, GraphValueType.Int, scriptPorts: noPorts, scriptOut: GraphValueType.Int, worldSideEffect: true);
            Add(rows, GraphNodeOp.StoreArgInt, TriggerGraphOnly, GraphValueType.Void, scriptPorts: portValue, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.StoreArgFloat, TriggerGraphOnly, GraphValueType.Void, scriptPorts: portValue, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.StoreArgEntity, TriggerGraphOnly, GraphValueType.Void, scriptPorts: portValue, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            // DispatchMapEvent payload ports are dynamic (one per non-String schema parameter,
            // named after the parameter); the static table intentionally declares none.
            Add(rows, GraphNodeOp.DispatchMapEvent, TriggerGraphOnly, GraphValueType.Void, scriptPorts: noPorts, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.ControlDomainResolve, LinearAll, GraphValueType.Entity, portSource);
            Add(rows, GraphNodeOp.ControlDomainControls, LinearAll, GraphValueType.Bool, portAB);
            Add(rows, GraphNodeOp.KnowledgeHasProjection, LinearAll, GraphValueType.Bool, portAB);
            Add(rows, GraphNodeOp.Call, ScriptAndTriggerGraph, scriptPorts: noPorts, imm: GraphOperandRole.Immediate);
            Add(rows, GraphNodeOp.Return, ScriptAndTriggerGraph, scriptPorts: noPorts);
            Add(rows, GraphNodeOp.Yield, ScriptAndTriggerGraph, scriptPorts: noPorts, scriptSliceOnly: true);
            // LinearOutputType carries the Bool result for Script/TriggerGraph emit
            // (scriptOut is not stored on the descriptor; UsesLinearDescriptorEmit reads LinearOut).
            Add(rows, GraphNodeOp.AwaitCallback, ScriptAndTriggerGraph, GraphValueType.Bool, scriptPorts: noPorts, scriptOut: GraphValueType.Bool, scriptSliceOnly: true);
            Add(rows, GraphNodeOp.HaltReturnInt, LinearQueryScript, linearPorts: portValue, queryPorts: portValue, scriptPorts: portValue, scriptOut: GraphValueType.Void);
            Add(rows, GraphNodeOp.InvokeScript, LinearQueryScript, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, flags: GraphOperandRole.FuncLibNameFlags, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.MoveInt, ScriptAndTriggerGraph, GraphValueType.Int, scriptPorts: portValue, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ConstText, ScriptAndTriggerGraph, GraphValueType.Text, scriptPorts: noPorts);
            Add(rows, GraphNodeOp.ConcatText, ScriptAndTriggerGraph, GraphValueType.Text, portAB, scriptPorts: portAB);
            Add(rows, GraphNodeOp.IntToText, ScriptAndTriggerGraph, GraphValueType.Text, portA, scriptPorts: portA);
            Add(rows, GraphNodeOp.FloatToText, ScriptAndTriggerGraph, GraphValueType.Text, portA, scriptPorts: portA);
            Add(rows, GraphNodeOp.SinkPresentationText, ScriptAndTriggerGraph, GraphValueType.Void, portA, scriptPorts: portA, imm: GraphOperandRole.Immediate, worldSideEffect: true);
            Add(rows, GraphNodeOp.LoadTextKey, ScriptAndTriggerGraph, GraphValueType.Text, scriptPorts: noPorts, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.StartDialogue, ScriptAndTriggerGraph, GraphValueType.Void, scriptPorts: noPorts, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            // ── Order-driven graph brains (issue #1536) ──
            Add(rows, GraphNodeOp.LoadEntityPosX, LinearQueryScript, GraphValueType.Int, portSource, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Int, flags: GraphOperandRole.BoolScratchFlags);
            Add(rows, GraphNodeOp.LoadEntityPosY, LinearQueryScript, GraphValueType.Int, portSource, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Int, flags: GraphOperandRole.BoolScratchFlags);
            Add(rows, GraphNodeOp.IntToFloat, LinearQueryScript, GraphValueType.Float, portA, queryPorts: portA, scriptPorts: portA, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.FloatToInt, LinearQueryScript, GraphValueType.Int, portA, queryPorts: portA, scriptPorts: portA, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.SqrtFloat, LinearQueryScript, GraphValueType.Float, portA, queryPorts: portA, scriptPorts: portA, scriptOut: GraphValueType.Float);
            Add(rows, GraphNodeOp.LoadEntityPosValid, LinearQueryScript, GraphValueType.Bool, portSource, queryPorts: portSource, scriptPorts: portSource, scriptOut: GraphValueType.Bool);
            Add(rows, GraphNodeOp.LoadOrderTypeId, ScriptAndTriggerGraph, GraphValueType.Int, scriptPorts: noPorts, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.SubmitAssignedOrder, ScriptAndTriggerGraph, GraphValueType.Void, portApplyTemplate, scriptPorts: portApplyTemplate, imm: GraphOperandRole.SymbolImm, scriptSliceOnly: true, worldSideEffect: true);
            Add(rows, GraphNodeOp.CompleteActiveOrder, ScriptAndTriggerGraph, GraphValueType.Void, scriptPorts: noPorts, scriptSliceOnly: true, worldSideEffect: true);
            Add(rows, GraphNodeOp.ReadCalendarEnabled, ScriptTriggerQuery, GraphValueType.Bool, queryOut: GraphValueType.Bool, scriptOut: GraphValueType.Bool);
            Add(rows, GraphNodeOp.ReadCalendarDayIndex, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ReadCalendarTicksIntoDay, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ReadCalendarDayPermille, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ReadCalendarDayPhase, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int);
            Add(rows, GraphNodeOp.ReadCalendarYear, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadCalendarCyclePhase, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadCalendarCycleDay, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.LoadConfigKey, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadCalendarCyclePhaseIndex, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadCalendarDaysUntilPhase, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ApplyCalendarStart, ScriptAndTriggerGraph, GraphValueType.Void, portAB, scriptPorts: portAB, worldSideEffect: true);
            Add(rows, GraphNodeOp.SetCalendarDayIndex, ScriptAndTriggerGraph, GraphValueType.Void, portValue, scriptPorts: portValue, worldSideEffect: true);
            Add(rows, GraphNodeOp.SetCalendarTicksIntoDay, ScriptAndTriggerGraph, GraphValueType.Void, portValue, scriptPorts: portValue, worldSideEffect: true);
            Add(rows, GraphNodeOp.ReadTimeFlowPaused, ScriptTriggerQuery, GraphValueType.Bool, queryOut: GraphValueType.Bool, scriptOut: GraphValueType.Bool, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.ReadTimeFlowScalePermille, ScriptTriggerQuery, GraphValueType.Int, queryOut: GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm);
            Add(rows, GraphNodeOp.AcquireTimeFlowPause, ScriptAndTriggerGraph, GraphValueType.Int, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.AcquireTimeFlowScale, ScriptAndTriggerGraph, GraphValueType.Int, portValue, scriptPorts: portValue, scriptOut: GraphValueType.Int, imm: GraphOperandRole.SymbolImm, worldSideEffect: true);
            Add(rows, GraphNodeOp.ReleaseTimeFlowToken, ScriptAndTriggerGraph, GraphValueType.Void, portValue, scriptPorts: portValue, worldSideEffect: true);

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
            bool scriptSliceOnly = false,
            bool derivedWrite = false,
            bool listenerOwner = false,
            bool worldSideEffect = false)
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
                linearPorts ?? Array.Empty<string>(),
                queryPorts ?? Array.Empty<string>(),
                scriptPorts ?? Array.Empty<string>(),
                dst,
                flags,
                imm,
                scriptSliceOnly,
                derivedWrite,
                listenerOwner,
                worldSideEffect));
        }
    }
}
