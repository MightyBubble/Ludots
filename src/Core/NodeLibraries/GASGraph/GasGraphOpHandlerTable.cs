using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Concrete delegate for GAS Graph opcode handlers.
    /// Uses a concrete ref struct type (not generic) because .NET 8 does not support
    /// 'allows ref struct' generic constraints.
    /// </summary>
    public delegate void GasGraphOpHandler(ref GraphExecutionState state, in GraphInstruction ins, ref int pc);

    /// <summary>
    /// Opcode handler table for the GAS Graph VM.
    /// Provides a handler array consumable by <see cref="Execute"/>.
    /// </summary>
    public sealed class GasGraphOpHandlerTable
    {
        public static readonly GasGraphOpHandlerTable Instance = new();

        public GasGraphOpHandler[] Handlers { get; }

        private readonly string?[] _descriptions;

        private GasGraphOpHandlerTable()
        {
            Handlers = new GasGraphOpHandler[GraphVmLimits.HandlerTableSize];
            _descriptions = new string?[GraphVmLimits.HandlerTableSize];
            RegisterBuiltins();
            EnsureRegistrationComplete();
        }

        /// <summary>
        /// Registers an executable opcode. Requires a non-empty description and rejects duplicates.
        /// </summary>
        public void Register(GraphNodeOp op, GasGraphOpHandler handler, string description)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException(
                    $"Graph opcode '{op}' requires a non-empty description.",
                    nameof(description));
            }

            ushort code = (ushort)op;
            if (op == GraphNodeOp.None || code >= Handlers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(op), $"Graph opcode '{op}' ({code}) is not registerable.");
            }

            if (Handlers[code] != null)
            {
                throw new InvalidOperationException(
                    $"Graph opcode '{op}' ({code}) is already registered; duplicate registration is not allowed.");
            }

            Handlers[code] = handler;
            _descriptions[code] = description.Trim();
        }

        public bool TryGetDescription(GraphNodeOp op, out string description)
        {
            ushort code = (ushort)op;
            if (code < _descriptions.Length && !string.IsNullOrEmpty(_descriptions[code]))
            {
                description = _descriptions[code]!;
                return true;
            }

            description = string.Empty;
            return false;
        }

        public string GetDescription(GraphNodeOp op)
        {
            if (!TryGetDescription(op, out string description))
            {
                throw new InvalidOperationException($"Graph opcode '{op}' has no registered description.");
            }

            return description;
        }

        private void EnsureRegistrationComplete()
        {
            foreach (GraphNodeOp op in Enum.GetValues<GraphNodeOp>())
            {
                if (op == GraphNodeOp.None)
                {
                    continue;
                }

                ushort code = (ushort)op;
                if (Handlers[code] == null || string.IsNullOrWhiteSpace(_descriptions[code]))
                {
                    throw new InvalidOperationException(
                        $"Executable graph opcode '{op}' ({code}) is missing required handler/description registration.");
                }
            }
        }

        /// <summary>
        /// Execute a graph program using this handler table.
        /// Mirrors <see cref="Ludots.Core.GraphRuntime.GraphExecutor.Execute{TState}"/>
        /// but works with ref struct state.
        /// Fail-fast on unregistered ops (non-zero) and enforces instruction budget.
        /// </summary>
        public static void Execute(ref GraphExecutionState state, ReadOnlySpan<GraphInstruction> program, GasGraphOpHandlerTable handlers)
        {
            var table = handlers.Handlers;
            int pc = 0;
            int steps = 0;
            int maxSteps = GraphVmLimits.MaxInstructionsPerExecution;

            while ((uint)pc < (uint)program.Length)
            {
                if (++steps > maxSteps)
                {
                    throw new InvalidOperationException(
                        $"Graph VM exceeded MaxInstructionsPerExecution ({maxSteps}). Possible infinite loop.");
                }

                ref readonly var ins = ref program[pc];
                pc++;

                if (ins.Op == 0) continue;

                if (ins.Op >= table.Length)
                {
                    throw new InvalidOperationException(
                        $"Graph op {ins.Op} exceeds handler table capacity ({table.Length}).");
                }

                var handler = table[ins.Op];
                if (handler == null)
                {
                    throw new InvalidOperationException(
                        $"No handler registered for graph op {ins.Op}.");
                }

                handler(ref state, in ins, ref pc);
            }
        }

        private void RegisterBuiltins()
        {
            Register(GraphNodeOp.ConstBool, HandleConstBool, "ConstBool graph opcode.");
            Register(GraphNodeOp.ConstInt, HandleConstInt, "ConstInt graph opcode.");
            Register(GraphNodeOp.ConstFloat, HandleConstFloat, "ConstFloat graph opcode.");
            Register(GraphNodeOp.LoadCaster, HandleLoadCaster, "LoadCaster graph opcode.");
            Register(GraphNodeOp.LoadExplicitTarget, HandleLoadExplicitTarget, "LoadExplicitTarget graph opcode.");
            Register(GraphNodeOp.Jump, HandleJump, "Jump graph opcode.");
            Register(GraphNodeOp.JumpIfFalse, HandleJumpIfFalse, "JumpIfFalse graph opcode.");
            Register(GraphNodeOp.LoadAttribute, HandleLoadAttribute, "LoadAttribute graph opcode.");
            Register(GraphNodeOp.AddFloat, HandleAddFloat, "AddFloat graph opcode.");
            Register(GraphNodeOp.MulFloat, HandleMulFloat, "MulFloat graph opcode.");
            Register(GraphNodeOp.CompareGtFloat, HandleCompareGtFloat, "CompareGtFloat graph opcode.");
            Register(GraphNodeOp.SelectEntity, HandleSelectEntity, "SelectEntity graph opcode.");
            Register(GraphNodeOp.QueryRadius, HandleQueryRadius, "QueryRadius graph opcode.");
            Register(GraphNodeOp.QuerySortStable, HandleQuerySortStable, "QuerySortStable graph opcode.");
            Register(GraphNodeOp.QueryLimit, HandleQueryLimit, "QueryLimit graph opcode.");
            Register(GraphNodeOp.QueryCone, HandleQueryCone, "QueryCone graph opcode.");
            Register(GraphNodeOp.QueryRectangle, HandleQueryRectangle, "QueryRectangle graph opcode.");
            Register(GraphNodeOp.QueryLine, HandleQueryLine, "QueryLine graph opcode.");
            Register(GraphNodeOp.QueryFilterNotEntity, HandleQueryFilterNotEntity, "QueryFilterNotEntity graph opcode.");
            Register(GraphNodeOp.QueryFilterLayer, HandleQueryFilterLayer, "QueryFilterLayer graph opcode.");
            Register(GraphNodeOp.QueryFilterRelationship, HandleQueryFilterRelationship, "QueryFilterRelationship graph opcode.");
            Register(GraphNodeOp.AggCount, HandleAggCount, "AggCount graph opcode.");
            Register(GraphNodeOp.AggMinByDistance, HandleAggMinByDistance, "AggMinByDistance graph opcode.");
            Register(GraphNodeOp.TargetListGet, HandleTargetListGet, "TargetListGet graph opcode.");
            Register(GraphNodeOp.ApplyEffectTemplate, HandleApplyEffectTemplate, "ApplyEffectTemplate graph opcode.");
            Register(GraphNodeOp.FanOutApplyEffect, HandleFanOutApplyEffect, "FanOutApplyEffect graph opcode.");
            Register(GraphNodeOp.RemoveEffectTemplate, HandleRemoveEffectTemplate, "RemoveEffectTemplate graph opcode.");
            Register(GraphNodeOp.ModifyAttributeAdd, HandleModifyAttributeAdd, "ModifyAttributeAdd graph opcode.");
            Register(GraphNodeOp.SendEvent, HandleSendEvent, "SendEvent graph opcode.");
            Register(GraphNodeOp.RelationshipEnsureLink, HandleRelationshipEnsureLink, "RelationshipEnsureLink graph opcode.");
            Register(GraphNodeOp.RelationshipRemoveLink, HandleRelationshipRemoveLink, "RelationshipRemoveLink graph opcode.");
            Register(GraphNodeOp.RelationshipSetMetric, HandleRelationshipSetMetric, "RelationshipSetMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAddMetric, HandleRelationshipAddMetric, "RelationshipAddMetric graph opcode.");
            Register(GraphNodeOp.RelationshipGetMetric, HandleRelationshipGetMetric, "RelationshipGetMetric graph opcode.");
            Register(GraphNodeOp.RelationshipHasFlag, HandleRelationshipHasFlag, "RelationshipHasFlag graph opcode.");
            Register(GraphNodeOp.RelationshipSetFlag, HandleRelationshipSetFlag, "RelationshipSetFlag graph opcode.");
            Register(GraphNodeOp.RelationshipQueryOutgoing, HandleRelationshipQueryOutgoing, "RelationshipQueryOutgoing graph opcode.");
            Register(GraphNodeOp.RelationshipQueryIncoming, HandleRelationshipQueryIncoming, "RelationshipQueryIncoming graph opcode.");
            Register(GraphNodeOp.RelationshipQueryMutual, HandleRelationshipQueryMutual, "RelationshipQueryMutual graph opcode.");
            Register(GraphNodeOp.RelationshipQueryBetweenPair, HandleRelationshipQueryBetweenPair, "RelationshipQueryBetweenPair graph opcode.");
            Register(GraphNodeOp.RelationshipFilterMetricRange, HandleRelationshipFilterMetricRange, "RelationshipFilterMetricRange graph opcode.");
            Register(GraphNodeOp.RelationshipFilterFlag, HandleRelationshipFilterFlag, "RelationshipFilterFlag graph opcode.");
            Register(GraphNodeOp.RelationshipSortByMetric, HandleRelationshipSortByMetric, "RelationshipSortByMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggSumMetric, HandleRelationshipAggSumMetric, "RelationshipAggSumMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMaxMetric, HandleRelationshipAggMaxMetric, "RelationshipAggMaxMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggAverageMetric, HandleRelationshipAggAverageMetric, "RelationshipAggAverageMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMinMetric, HandleRelationshipAggMinMetric, "RelationshipAggMinMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMaxEntityByMetric, HandleRelationshipAggMaxEntityByMetric, "RelationshipAggMaxEntityByMetric graph opcode.");
            Register(GraphNodeOp.RelationshipAggMinEntityByMetric, HandleRelationshipAggMinEntityByMetric, "RelationshipAggMinEntityByMetric graph opcode.");
            Register(GraphNodeOp.QueryAllMapEntities, HandleQueryAllMapEntities, "QueryAllMapEntities graph opcode.");
            Register(GraphNodeOp.QueryFromCollection, HandleQueryFromCollection, "QueryFromCollection graph opcode.");
            Register(GraphNodeOp.QueryFilterTeam, HandleQueryFilterTeam, "QueryFilterTeam graph opcode.");
            Register(GraphNodeOp.QueryFilterTemplate, HandleQueryFilterTemplate, "QueryFilterTemplate graph opcode.");
            Register(GraphNodeOp.QueryFilterAttributeRange, HandleQueryFilterAttributeRange, "QueryFilterAttributeRange graph opcode.");
            Register(GraphNodeOp.QueryFilterTagAny, HandleQueryFilterTagAny, "QueryFilterTagAny graph opcode.");
            Register(GraphNodeOp.QueryFilterTagNone, HandleQueryFilterTagNone, "QueryFilterTagNone graph opcode.");
            Register(GraphNodeOp.QuerySortByAttribute, HandleQuerySortByAttribute, "QuerySortByAttribute graph opcode.");
            Register(GraphNodeOp.AggSumAttribute, HandleAggSumAttribute, "AggSumAttribute graph opcode.");
            Register(GraphNodeOp.AggAverageAttribute, HandleAggAverageAttribute, "AggAverageAttribute graph opcode.");
            Register(GraphNodeOp.AggMaxAttribute, HandleAggMaxAttribute, "AggMaxAttribute graph opcode.");
            Register(GraphNodeOp.AggMinAttribute, HandleAggMinAttribute, "AggMinAttribute graph opcode.");
            Register(GraphNodeOp.AggMaxEntityByAttribute, HandleAggMaxEntityByAttribute, "AggMaxEntityByAttribute graph opcode.");
            Register(GraphNodeOp.AggMinEntityByAttribute, HandleAggMinEntityByAttribute, "AggMinEntityByAttribute graph opcode.");
            Register(GraphNodeOp.BeginLifecycleTransaction, HandleBeginLifecycleTransaction, "BeginLifecycleTransaction graph opcode.");
            Register(GraphNodeOp.InvokeBuiltin, HandleInvokeBuiltin, "InvokeBuiltin graph opcode.");
            Register(GraphNodeOp.AddInt, HandleAddInt, "AddInt graph opcode.");
            Register(GraphNodeOp.CompareLtInt, HandleCompareLtInt, "CompareLtInt graph opcode.");
            Register(GraphNodeOp.CompareEqInt, HandleCompareEqInt, "CompareEqInt graph opcode.");
            Register(GraphNodeOp.HasTag, HandleHasTag, "HasTag graph opcode.");
            Register(GraphNodeOp.CompareEqEntity, HandleCompareEqEntity, "CompareEqEntity graph opcode.");
            Register(GraphNodeOp.RandomFloat01, HandleRandomFloat01, "RandomFloat01 graph opcode.");
            Register(GraphNodeOp.QueryHexRange, HandleQueryHexRange, "QueryHexRange graph opcode.");
            Register(GraphNodeOp.QueryHexRing, HandleQueryHexRing, "QueryHexRing graph opcode.");
            Register(GraphNodeOp.QueryHexNeighbors, HandleQueryHexNeighbors, "QueryHexNeighbors graph opcode.");
            Register(GraphNodeOp.SubFloat, HandleSubFloat, "SubFloat graph opcode.");
            Register(GraphNodeOp.DivFloat, HandleDivFloat, "DivFloat graph opcode.");
            Register(GraphNodeOp.MinFloat, HandleMinFloat, "MinFloat graph opcode.");
            Register(GraphNodeOp.MaxFloat, HandleMaxFloat, "MaxFloat graph opcode.");
            Register(GraphNodeOp.ClampFloat, HandleClampFloat, "ClampFloat graph opcode.");
            Register(GraphNodeOp.AbsFloat, HandleAbsFloat, "AbsFloat graph opcode.");
            Register(GraphNodeOp.NegFloat, HandleNegFloat, "NegFloat graph opcode.");
            Register(GraphNodeOp.ReadBlackboardFloat, HandleReadBlackboardFloat, "ReadBlackboardFloat graph opcode.");
            Register(GraphNodeOp.ReadBlackboardInt, HandleReadBlackboardInt, "ReadBlackboardInt graph opcode.");
            Register(GraphNodeOp.ReadBlackboardEntity, HandleReadBlackboardEntity, "ReadBlackboardEntity graph opcode.");
            Register(GraphNodeOp.WriteBlackboardFloat, HandleWriteBlackboardFloat, "WriteBlackboardFloat graph opcode.");
            Register(GraphNodeOp.WriteBlackboardInt, HandleWriteBlackboardInt, "WriteBlackboardInt graph opcode.");
            Register(GraphNodeOp.WriteBlackboardEntity, HandleWriteBlackboardEntity, "WriteBlackboardEntity graph opcode.");
            Register(GraphNodeOp.LoadConfigFloat, HandleLoadConfigFloat, "LoadConfigFloat graph opcode.");
            Register(GraphNodeOp.LoadConfigInt, HandleLoadConfigInt, "LoadConfigInt graph opcode.");
            Register(GraphNodeOp.LoadConfigEffectId, HandleLoadConfigEffectId, "LoadConfigEffectId graph opcode.");
            Register(GraphNodeOp.LoadContextSource, HandleLoadContextSource, "LoadContextSource graph opcode.");
            Register(GraphNodeOp.LoadContextTarget, HandleLoadContextTarget, "LoadContextTarget graph opcode.");
            Register(GraphNodeOp.LoadContextTargetContext, HandleLoadContextTargetContext, "LoadContextTargetContext graph opcode.");
            Register(GraphNodeOp.ApplyEffectDynamic, HandleApplyEffectDynamic, "ApplyEffectDynamic graph opcode.");
            Register(GraphNodeOp.FanOutApplyEffectDynamic, HandleFanOutApplyEffectDynamic, "FanOutApplyEffectDynamic graph opcode.");
            Register(GraphNodeOp.FanOutDispatchEffect, HandleFanOutDispatchEffect, "FanOutDispatchEffect graph opcode.");
            Register(GraphNodeOp.FanOutDispatchEffectDynamic, HandleFanOutDispatchEffectDynamic, "FanOutDispatchEffectDynamic graph opcode.");
            Register(GraphNodeOp.LoadSelfAttribute, HandleLoadSelfAttribute, "LoadSelfAttribute graph opcode.");
            Register(GraphNodeOp.WriteSelfAttribute, HandleWriteSelfAttribute, "WriteSelfAttribute graph opcode.");
            Register(GraphNodeOp.LoadTargetPosX, HandleLoadTargetPosX, "LoadTargetPosX graph opcode.");
            Register(GraphNodeOp.LoadTargetPosY, HandleLoadTargetPosY, "LoadTargetPosY graph opcode.");
            Register(GraphNodeOp.ClampTargetToRange, HandleClampTargetToRange, "ClampTargetToRange graph opcode.");
            Register(GraphNodeOp.IsPointInCircle, HandleIsPointInCircle, "IsPointInCircle graph opcode.");
            Register(GraphNodeOp.SnapToNearestInCollection, HandleSnapToNearestInCollection, "SnapToNearestInCollection graph opcode.");
            Register(GraphNodeOp.SnapToNearestGraphEdge, HandleSnapToNearestGraphEdge, "SnapToNearestGraphEdge graph opcode.");
            Register(GraphNodeOp.LoadViewer, HandleLoadViewer, "LoadViewer graph opcode.");
            Register(GraphNodeOp.LoadEventPayloadInt, HandleLoadEventPayloadInt, "LoadEventPayloadInt graph opcode.");
            Register(GraphNodeOp.LoadEventPayloadFloat, HandleLoadEventPayloadFloat, "LoadEventPayloadFloat graph opcode.");
            Register(GraphNodeOp.RelationshipHasLink, HandleRelationshipHasLink, "RelationshipHasLink graph opcode.");
            Register(GraphNodeOp.ControlDomainResolve, HandleControlDomainResolve, "ControlDomainResolve graph opcode.");
            Register(GraphNodeOp.ControlDomainControls, HandleControlDomainControls, "ControlDomainControls graph opcode.");
            Register(GraphNodeOp.KnowledgeHasProjection, HandleKnowledgeHasProjection, "KnowledgeHasProjection graph opcode.");
        }

        // ── Value Ops ──

        private static void HandleConstBool(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(ins.Imm != 0 ? 1 : 0);
        }

        private static void HandleConstInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = ins.Imm;
        }

        private static void HandleConstFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = ins.ImmF;
        }

        // ── Entity Loading ──

        private static void HandleLoadCaster(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Caster;
        }

        private static void HandleLoadExplicitTarget(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.ExplicitTarget;
        }

        // ── Control Flow ──

        private static void HandleJump(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            pc += ins.Imm;
        }

        private static void HandleJumpIfFalse(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (s.B[ins.A] == 0)
            {
                pc += ins.Imm;
            }
        }

        // ── Attribute ──

        private static void HandleLoadAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var src = s.E[ins.A];
            if (s.World.IsAlive(src) && s.Api.TryGetAttributeCurrent(src, ins.Imm, out float value))
            {
                s.F[ins.Dst] = value;
            }
            else
            {
                s.F[ins.Dst] = 0f;
            }
        }

        // ── Arithmetic ──

        private static void HandleAddFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] + s.F[ins.B];
        }

        private static void HandleMulFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] * s.F[ins.B];
        }

        private static void HandleCompareGtFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.F[ins.A] > s.F[ins.B] ? 1 : 0);
        }

        private static void HandleSelectEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.B[ins.A] != 0 ? s.E[ins.B] : s.E[ins.C];
        }

        // ── Spatial Queries ──

        private static void HandleQueryRadius(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryRadius(s.TargetPosCm, ins.ImmF, s.Targets));
        }

        private static void HandleQueryCone(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryCone(s.TargetPosCm, s.I[ins.A], s.I[ins.B], ins.ImmF, s.Targets));
        }

        private static void HandleQueryRectangle(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryRectangle(s.TargetPosCm, s.I[ins.A], s.I[ins.B], ins.Imm, s.Targets));
        }

        private static void HandleQueryLine(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryLine(s.TargetPosCm, s.I[ins.A], s.I[ins.B], ins.Imm, s.Targets));
        }

        private static void ApplySpatialQueryResult(
            ref GraphExecutionState s,
            in GraphInstruction ins,
            Ludots.Core.Spatial.SpatialQueryResult result)
        {
            if (ins.Flags > 1)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidSpatialQueryCapacityPolicy: flags={ins.Flags}.");
            }

            if ((uint)result.Count > (uint)s.Targets.Length || result.Dropped < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidSpatialQueryResult: count={result.Count}, dropped={result.Dropped}, capacity={s.Targets.Length}.");
            }

            if (result.Dropped > 0 && ins.Flags == 0)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.SpatialQueryIncomplete: count={result.Count}, dropped={result.Dropped}.");
            }

            s.TargetList.SetCount(result.Count);
            if (ins.Flags == 1)
            {
                s.I[ins.Dst] = result.Dropped;
            }
        }

        // ── Query Filters ──

        private static void HandleQuerySortStable(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.SortStableDedup(s.Targets, s.TargetList.Count));
        }

        private static void HandleQueryLimit(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.Limit(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQueryFilterNotEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterNotEntity(s.Targets, s.TargetList.Count, s.E[ins.A]));
        }

        private static void HandleQueryFilterLayer(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterLayer(s.Targets, s.TargetList.Count, unchecked((uint)ins.Imm)));
        }

        private static void HandleQueryFilterRelationship(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTeamRelationship(
                s.Targets,
                s.TargetList.Count,
                s.E[ins.A],
                ParseRelationshipFilterMode(ins.Imm)));
        }

        // ── Aggregation ──

        private static void HandleAggCount(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetList.Count;
        }

        private static void HandleAggMinByDistance(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var centerCm = new WorldCmInt2(s.TargetPosCm.X, s.TargetPosCm.Y);
            s.E[ins.Dst] = s.Api.TryMinEntityByWorldDistanceCm(s.TargetList.Span, centerCm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        // ── Effect / Event Actions ──

        private static void HandleApplyEffectTemplate(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                byte floatCount = ins.Flags;
                if (floatCount == 0)
                {
                    s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm);
                    return;
                }

                float f0 = s.F[ins.B];
                float f1 = floatCount > 1 ? s.F[ins.C] : 0f;
                var args = new EffectArgs(floatCount, f0, f1);
                s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm, in args);
            }
        }

        private static void HandleModifyAttributeAdd(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                s.Api.ModifyAttributeAdd(s.Caster, target, ins.Imm, s.F[ins.B]);
            }
        }

        private static void HandleRemoveEffectTemplate(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                s.Api.RemoveEffectTemplate(target, ins.Imm);
            }
        }

        private static void HandleSendEvent(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            var target = s.E[ins.A];
            if (s.World.IsAlive(target))
            {
                s.Api.SendEvent(s.Caster, target, ins.Imm, s.F[ins.B]);
            }
        }

        // ── TargetList Iteration (123) ──

        private static void HandleTargetListGet(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[Dst] = TargetList[I[A]]; B[Flags] = valid (0/1)
            int idx = s.I[ins.A];
            if (idx >= 0 && idx < s.TargetList.Count)
            {
                s.E[ins.Dst] = s.TargetList.Span[idx];
                s.B[ins.Flags] = 1;
            }
            else
            {
                s.E[ins.Dst] = default;
                s.B[ins.Flags] = 0;
            }
        }

        // ── Batch Effect Application (201) ──

        private static void HandleFanOutApplyEffect(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // Apply Effect(Imm=templateId) to ALL entities in TargetList
            var span = s.TargetList.Span;
            byte floatCount = ins.Flags;
            for (int i = 0; i < span.Length; i++)
            {
                var target = span[i];
                if (!s.World.IsAlive(target)) continue;
                if (floatCount == 0)
                {
                    s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm);
                }
                else
                {
                    float f0 = s.F[ins.A];
                    float f1 = floatCount > 1 ? s.F[ins.B] : 0f;
                    s.Api.ApplyEffectTemplate(s.Caster, target, ins.Imm, new EffectArgs(floatCount, f0, f1));
                }
            }
        }

        // ── Int Math / Bool (29, 31-33) ──

        private static void HandleRelationshipEnsureLink(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.EnsureRelationshipLink(s.E[ins.A], s.E[ins.B], RequireExplicitRelationshipTypeId(ins.Dst));
        }

        private static void HandleRelationshipRemoveLink(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.RemoveRelationshipLink(s.E[ins.A], s.E[ins.B], RequireExplicitRelationshipTypeId(ins.Dst));
        }

        private static void HandleRelationshipSetMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int reasonId = ins.Dst == byte.MaxValue ? 0 : ins.Dst;
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.Api.SetRelationshipMetric(s.E[ins.A], s.E[ins.B], ins.Imm, s.I[ins.C], reasonId, typeId);
        }

        private static void HandleRelationshipAddMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int reasonId = ins.Dst == byte.MaxValue ? 0 : ins.Dst;
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.Api.AddRelationshipMetric(s.E[ins.A], s.E[ins.B], ins.Imm, s.I[ins.C], reasonId, typeId);
        }

        private static void HandleRelationshipGetMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.Api.GetRelationshipMetric(s.E[ins.A], s.E[ins.B], ins.Imm, RequireExplicitRelationshipTypeId(ins.Flags));
        }

        private static void HandleRelationshipHasFlag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.HasRelationshipFlag(s.E[ins.A], s.E[ins.B], ins.Imm, RequireExplicitRelationshipTypeId(ins.Flags)) ? 1 : 0);
        }

        private static void HandleRelationshipSetFlag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int reasonId = ins.Dst == byte.MaxValue ? 0 : ins.Dst;
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.Api.SetRelationshipFlag(s.E[ins.A], s.E[ins.B], ins.Imm, s.B[ins.C] != 0, reasonId, typeId);
        }

        private static void HandleRelationshipQueryOutgoing(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CollectOutgoing(s.E[ins.A], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipQueryIncoming(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CollectIncoming(s.E[ins.A], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipQueryMutual(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CollectMutual(s.E[ins.A], s.E[ins.B], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipQueryBetweenPair(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CollectBetweenPair(s.E[ins.A], s.E[ins.B], s.Targets, ResolveQueryTypeId(ins.Dst)));
        }

        private static void HandleRelationshipFilterMetricRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Dst);
            short min = (short)s.F[ins.B];
            short max = (short)s.F[ins.C];
            s.TargetList.SetCount(s.Api.FilterRelationshipMetricRange(s.Targets, s.TargetList.Count, source, typeId, ins.Imm, min, max));
        }

        private static void HandleRelationshipFilterFlag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Dst);
            bool expected = ins.Flags != 0 || s.B[ins.B] != 0;
            s.TargetList.SetCount(s.Api.FilterRelationshipFlag(s.Targets, s.TargetList.Count, source, typeId, ins.Imm, expected));
        }

        private static void HandleRelationshipSortByMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Dst);
            bool descending = ins.Flags != 0;
            s.Api.SortByRelationshipMetric(s.Targets, s.TargetList.Count, source, typeId, ins.Imm, descending);
        }

        private static void HandleRelationshipAggSumMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.SumRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggMaxMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.MaxRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggAverageMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.AverageRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggMinMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.I[ins.Dst] = s.Api.MinRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm);
        }

        private static void HandleRelationshipAggMaxEntityByMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.E[ins.Dst] = s.Api.TryMaxEntityByRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static void HandleRelationshipAggMinEntityByMetric(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            Entity source = s.E[ins.A];
            int typeId = RequireExplicitRelationshipTypeId(ins.Flags);
            s.E[ins.Dst] = s.Api.TryMinEntityByRelationshipMetric(s.TargetList.Span, source, typeId, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static void HandleQueryAllMapEntities(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CollectMapEntities(s.Targets));
        }

        private static void HandleQueryFromCollection(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.CopyEntityCollection(s.E[ins.A], ins.Imm, s.Targets));
        }

        private static void HandleQueryFilterTeam(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int teamId = ins.Flags != 0 ? s.I[ins.A] : ins.Imm;
            s.TargetList.SetCount(s.Api.FilterTeam(s.Targets, s.TargetList.Count, teamId));
        }

        private static void HandleQueryFilterTemplate(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTemplate(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQueryFilterAttributeRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterAttributeRange(s.Targets, s.TargetList.Count, ins.Imm, s.F[ins.B], s.F[ins.C]));
        }

        private static void HandleQueryFilterTagAny(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTagAny(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQueryFilterTagNone(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.FilterTagNone(s.Targets, s.TargetList.Count, ins.Imm));
        }

        private static void HandleQuerySortByAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.SortByAttribute(s.Targets, s.TargetList.Count, ins.Imm, ins.Flags != 0);
        }

        private static void HandleAggSumAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.SumAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggAverageAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.AverageAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggMaxAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.MaxAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggMinAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.MinAttribute(s.TargetList.Span, ins.Imm);
        }

        private static void HandleAggMaxEntityByAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Api.TryMaxEntityByAttribute(s.TargetList.Span, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static void HandleAggMinEntityByAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Api.TryMinEntityByAttribute(s.TargetList.Span, ins.Imm, out Entity entity, out _)
                ? entity
                : Entity.Null;
        }

        private static int RequireExplicitRelationshipTypeId(byte encoded)
        {
            if (encoded == byte.MaxValue)
            {
                throw new InvalidOperationException("Graph relationship op requires an explicit relationshipType symbol.");
            }

            return encoded;
        }

        private static int ResolveQueryTypeId(byte encoded)
        {
            return encoded == byte.MaxValue ? RelationshipTypeRegistry.AnyTypeId : encoded;
        }

        private static RelationshipFilter ParseRelationshipFilterMode(int mode)
        {
            return mode switch
            {
                1 => RelationshipFilter.Hostile,
                2 => RelationshipFilter.Friendly,
                3 => RelationshipFilter.Neutral,
                4 => RelationshipFilter.NotFriendly,
                5 => RelationshipFilter.NotHostile,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported graph relationship filter mode.")
            };
        }

        private static void HandleAddInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.I[ins.A] + s.I[ins.B];
        }

        private static void HandleCompareLtInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.I[ins.A] < s.I[ins.B] ? 1 : 0);
        }

        private static void HandleCompareEqInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.I[ins.A] == s.I[ins.B] ? 1 : 0);
        }

        private static void HandleHasTag(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // B[Dst] = E[A].HasTag(Imm) ? 1 : 0
            var entity = s.E[ins.A];
            s.B[ins.Dst] = (byte)(s.Api.HasTag(entity, ins.Imm) ? 1 : 0);
        }

        private static void HandleCompareEqEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.E[ins.A] == s.E[ins.B] ? 1 : 0);
        }

        // ── Event evaluation context (410-412) ──

        private static void HandleLoadViewer(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Viewer;
        }

        private static void HandleLoadEventPayloadInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = ins.Imm switch
            {
                0 => s.EventPayload.PayloadA,
                1 => s.EventPayload.PayloadB,
                _ => throw new InvalidOperationException($"LoadEventPayloadInt slot {ins.Imm} is out of range (0=PayloadA, 1=PayloadB)."),
            };
        }

        private static void HandleLoadEventPayloadFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = ins.Imm switch
            {
                0 => s.EventPayload.FloatA,
                1 => s.EventPayload.FloatB,
                2 => s.EventPayload.FloatC,
                3 => s.EventPayload.FloatD,
                _ => throw new InvalidOperationException($"LoadEventPayloadFloat slot {ins.Imm} is out of range (0..3 = FloatA..FloatD)."),
            };
        }

        // ── Topology predicates (397, 420-422) ──

        private static void HandleRelationshipHasLink(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.HasRelationshipLink(s.E[ins.A], s.E[ins.B], RequireExplicitRelationshipTypeId(ins.Flags)) ? 1 : 0);
        }

        private static void HandleControlDomainResolve(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.Api.ResolveControlDomain(s.E[ins.A]);
        }

        private static void HandleControlDomainControls(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.IsControllableBy(s.E[ins.A], s.E[ins.B]) ? 1 : 0);
        }

        private static void HandleKnowledgeHasProjection(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.B[ins.Dst] = (byte)(s.Api.HasKnowledgeProjection(s.E[ins.A], s.E[ins.B]) ? 1 : 0);
        }

        private static void HandleRandomFloat01(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            uint x = s.RandomSeed;
            if (x == 0u)
            {
                x = 2463534242u;
            }

            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            s.RandomSeed = x;
            s.F[ins.Dst] = (x & 0x00FFFFFFu) / 16777215f;
        }

        // ── Hex Spatial Queries (130-132) ──

        private static void HandleQueryHexRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryHexRange(s.TargetPosCm, ins.Imm, s.Targets));
        }

        private static void HandleQueryHexRing(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryHexRing(s.TargetPosCm, ins.Imm, s.Targets));
        }

        private static void HandleQueryHexNeighbors(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            ApplySpatialQueryResult(ref s, in ins, s.Api.QueryHexNeighbors(s.TargetPosCm, s.Targets));
        }

        // ── Additional Math Ops (22-28) ──

        private static void HandleSubFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] - s.F[ins.B];
        }

        private static void HandleDivFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            float divisor = s.F[ins.B];
            s.F[ins.Dst] = divisor == 0f ? 0f : s.F[ins.A] / divisor;
        }

        private static void HandleMinFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] < s.F[ins.B] ? s.F[ins.A] : s.F[ins.B];
        }

        private static void HandleMaxFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.F[ins.A] > s.F[ins.B] ? s.F[ins.A] : s.F[ins.B];
        }

        private static void HandleClampFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = clamp(F[A], min=F[B], max=F[C])
            float val = s.F[ins.A];
            float min = s.F[ins.B];
            float max = s.F[ins.C];
            s.F[ins.Dst] = val < min ? min : (val > max ? max : val);
        }

        private static void HandleAbsFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            float v = s.F[ins.A];
            s.F[ins.Dst] = v < 0f ? -v : v;
        }

        private static void HandleNegFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = -s.F[ins.A];
        }

        // ── Blackboard immediate read/write (300-305) ──
        // Encoding: A = entity register index, Imm = blackboard keyId, Dst/B = value register

        private static void HandleReadBlackboardFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = E[A].BB_Float[Imm]
            var entity = s.E[ins.A];
            if (s.Api.TryReadBlackboardFloat(entity, ins.Imm, out float value))
                s.F[ins.Dst] = value;
            else
                s.F[ins.Dst] = 0f;
        }

        private static void HandleReadBlackboardInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[dst] = E[A].BB_Int[Imm]
            var entity = s.E[ins.A];
            if (s.Api.TryReadBlackboardInt(entity, ins.Imm, out int value))
                s.I[ins.Dst] = value;
            else
                s.I[ins.Dst] = 0;
        }

        private static void HandleReadBlackboardEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = E[A].BB_Entity[Imm]
            var entity = s.E[ins.A];
            if (s.Api.TryReadBlackboardEntity(entity, ins.Imm, out Entity value))
                s.E[ins.Dst] = value;
            else
                s.E[ins.Dst] = default;
        }

        private static void HandleWriteBlackboardFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[A].BB_Float[Imm] = F[B]   (immediate write)
            var entity = s.E[ins.A];
            s.Api.WriteBlackboardFloat(entity, ins.Imm, s.F[ins.B]);
        }

        private static void HandleWriteBlackboardInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[A].BB_Int[Imm] = I[B]
            var entity = s.E[ins.A];
            s.Api.WriteBlackboardInt(entity, ins.Imm, s.I[ins.B]);
        }

        private static void HandleWriteBlackboardEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[A].BB_Entity[Imm] = E[B]
            var entity = s.E[ins.A];
            s.Api.WriteBlackboardEntity(entity, ins.Imm, s.E[ins.B]);
        }

        // ── Config parameter reading (310-312) ──
        // Encoding: Imm = config keyId, Dst = destination register

        private static void HandleLoadConfigFloat(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = EffectTemplate.ConfigParams[Imm]
            if (s.Api.TryLoadConfigFloat(ins.Imm, out float value))
                s.F[ins.Dst] = value;
            else
                s.F[ins.Dst] = 0f;
        }

        private static void HandleLoadConfigInt(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[dst] = EffectTemplate.ConfigParams[Imm]
            if (s.Api.TryLoadConfigInt(ins.Imm, out int value))
                s.I[ins.Dst] = value;
            else
                s.I[ins.Dst] = 0;
        }

        private static void HandleLoadConfigEffectId(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // I[dst] = EffectTemplate.ConfigParams[Imm] (effectTemplateId, stored as int)
            if (s.Api.TryLoadConfigInt(ins.Imm, out int value))
                s.I[ins.Dst] = value;
            else
                s.I[ins.Dst] = 0;
        }

        // ── Context entity loading (320-322) ──

        private static void HandleLoadContextSource(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = EffectContext.Source (same entity as Caster in current model)
            s.E[ins.Dst] = s.Caster;
        }

        private static void HandleLoadContextTarget(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = EffectContext.Target (same entity as ExplicitTarget in current model)
            s.E[ins.Dst] = s.ExplicitTarget;
        }

        private static void HandleLoadContextTargetContext(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // E[dst] = EffectContext.TargetContext (additional context entity)
            s.E[ins.Dst] = s.TargetContext;
        }

        // ── Dynamic dispatch (202-203) ──

        private static void HandleApplyEffectDynamic(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // source=Caster, target=E[A], templateId=I[B]
            var target = s.E[ins.A];
            int templateId = s.I[ins.B];
            if (s.World.IsAlive(target) && templateId > 0)
            {
                s.Api.ApplyEffectTemplate(s.Caster, target, templateId);
            }
        }

        private static void HandleFanOutApplyEffectDynamic(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // source=Caster, TargetList ALL, templateId=I[A]
            int templateId = s.I[ins.A];
            if (templateId <= 0) return;
            var span = s.TargetList.Span;
            for (int i = 0; i < span.Length; i++)
            {
                var target = span[i];
                if (!s.World.IsAlive(target)) continue;
                s.Api.ApplyEffectTemplate(s.Caster, target, templateId);
            }
        }

        private static void HandleFanOutDispatchEffect(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (ins.Imm <= 0 || ins.Dst == 0)
            {
                return;
            }

            s.Api.FanOutDispatchEffect(s.Caster, s.ExplicitTarget, s.TargetContext, s.TargetList.Span, ins.Imm, ins.Dst);
        }

        private static void HandleFanOutDispatchEffectDynamic(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            int templateId = s.I[ins.A];
            if (templateId <= 0 || ins.Dst == 0)
            {
                return;
            }

            s.Api.FanOutDispatchEffect(s.Caster, s.ExplicitTarget, s.TargetContext, s.TargetList.Span, templateId, ins.Dst);
        }

        // ── Self attribute access for derived graphs (330-331) ──

        private static void HandleLoadSelfAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.F[ins.Dst] = s.Api.TryGetAttributeCurrent(s.Caster, ins.Imm, out float value)
                ? value
                : 0f;
        }

        private static void HandleWriteSelfAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // Caster.Attribute[Imm] = F[A] — direct SetCurrent bypassing modifier pipeline
            var self = s.Caster;
            if (s.World.IsAlive(self) && s.World.Has<AttributeBuffer>(self))
            {
                s.Api.ModifyAttributeSet(s.Caster, self, ins.Imm, s.F[ins.A]);
            }
        }

        private static void HandleLoadTargetPosX(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetPosCm.X;
        }

        private static void HandleLoadTargetPosY(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetPosCm.Y;
        }

        private static void HandleClampTargetToRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (!PlacementValidation.TryGetEntityWorldPositionCm(s.World, s.E[ins.A], out Fix64Vec2 originCm))
            {
                s.B[ins.Dst] = 0;
                return;
            }

            Fix64Vec2 targetCm = Fix64Vec2.FromInt(s.TargetPosCm.X, s.TargetPosCm.Y);
            PlacementValidation.ClampToRange(
                in originCm,
                ref targetCm,
                Fix64.FromFloat(s.F[ins.B]),
                out bool inRange);
            var rounded = targetCm.RoundToInt();
            s.TargetPosCm = new IntVector2(rounded.x, rounded.y);
            s.B[ins.Dst] = (byte)(inRange ? 1 : 0);
        }

        private static void HandleIsPointInCircle(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            if (!PlacementValidation.TryGetEntityWorldPositionCm(s.World, s.E[ins.A], out Fix64Vec2 centerCm))
            {
                s.B[ins.Dst] = 0;
                return;
            }

            Fix64Vec2 pointCm = Fix64Vec2.FromInt(s.TargetPosCm.X, s.TargetPosCm.Y);
            bool inside = PlacementValidation.IsPointInCircle(
                in pointCm,
                in centerCm,
                Fix64.FromFloat(s.F[ins.B]));
            s.B[ins.Dst] = (byte)(inside ? 1 : 0);
        }

        private static void HandleSnapToNearestInCollection(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            bool found = s.Api.TrySnapTargetToNearestInCollection(
                s.E[ins.A],
                ins.Imm,
                ref s.TargetPosCm,
                s.F[ins.B],
                out Entity snappedEntity);
            s.E[ins.Dst] = snappedEntity;
            if (ins.Flags != byte.MaxValue)
            {
                s.B[ins.Flags] = (byte)(found ? 1 : 0);
            }
        }

        private static void HandleSnapToNearestGraphEdge(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            bool found = s.Api.TrySnapTargetToNearestGraphEdge(
                ref s.TargetPosCm,
                s.F[ins.A],
                out _);
            s.B[ins.Dst] = (byte)(found ? 1 : 0);
        }

        private static void HandleBeginLifecycleTransaction(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.BeginLifecycleTransaction();
        }

        private static void HandleInvokeBuiltin(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.Api.InvokeBuiltin(ins.Imm);
        }
    }
}
