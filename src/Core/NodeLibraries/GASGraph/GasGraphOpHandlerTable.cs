using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;

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

        private GasGraphOpHandlerTable()
        {
            Handlers = CreateHandlers();
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

        private static GasGraphOpHandler[] CreateHandlers()
        {
            var h = new GasGraphOpHandler[GraphVmLimits.HandlerTableSize];

            h[(ushort)GraphNodeOp.ConstBool] = HandleConstBool;
            h[(ushort)GraphNodeOp.ConstInt] = HandleConstInt;
            h[(ushort)GraphNodeOp.ConstFloat] = HandleConstFloat;
            h[(ushort)GraphNodeOp.LoadCaster] = HandleLoadCaster;
            h[(ushort)GraphNodeOp.LoadExplicitTarget] = HandleLoadExplicitTarget;
            h[(ushort)GraphNodeOp.Jump] = HandleJump;
            h[(ushort)GraphNodeOp.JumpIfFalse] = HandleJumpIfFalse;
            h[(ushort)GraphNodeOp.LoadAttribute] = HandleLoadAttribute;
            h[(ushort)GraphNodeOp.AddFloat] = HandleAddFloat;
            h[(ushort)GraphNodeOp.MulFloat] = HandleMulFloat;
            h[(ushort)GraphNodeOp.CompareGtFloat] = HandleCompareGtFloat;
            h[(ushort)GraphNodeOp.SelectEntity] = HandleSelectEntity;
            h[(ushort)GraphNodeOp.QueryRadius] = HandleQueryRadius;
            h[(ushort)GraphNodeOp.QueryFilterTagAll] = HandleQueryFilterTagAll;
            h[(ushort)GraphNodeOp.QuerySortStable] = HandleQuerySortStable;
            h[(ushort)GraphNodeOp.QueryLimit] = HandleQueryLimit;
            h[(ushort)GraphNodeOp.QueryCone] = HandleQueryCone;
            h[(ushort)GraphNodeOp.QueryRectangle] = HandleQueryRectangle;
            h[(ushort)GraphNodeOp.QueryLine] = HandleQueryLine;
            h[(ushort)GraphNodeOp.QueryFilterNotEntity] = HandleQueryFilterNotEntity;
            h[(ushort)GraphNodeOp.QueryFilterLayer] = HandleQueryFilterLayer;
            h[(ushort)GraphNodeOp.QueryFilterRelationship] = HandleQueryFilterRelationship;
            h[(ushort)GraphNodeOp.AggCount] = HandleAggCount;
            h[(ushort)GraphNodeOp.AggMinByDistance] = HandleAggMinByDistance;
            h[(ushort)GraphNodeOp.TargetListGet] = HandleTargetListGet;
            h[(ushort)GraphNodeOp.ApplyEffectTemplate] = HandleApplyEffectTemplate;
            h[(ushort)GraphNodeOp.FanOutApplyEffect] = HandleFanOutApplyEffect;
            h[(ushort)GraphNodeOp.ModifyAttributeAdd] = HandleModifyAttributeAdd;
            h[(ushort)GraphNodeOp.SendEvent] = HandleSendEvent;

            // ── Int Math / Bool (29, 31-33) ──
            h[(ushort)GraphNodeOp.AddInt] = HandleAddInt;
            h[(ushort)GraphNodeOp.CompareLtInt] = HandleCompareLtInt;
            h[(ushort)GraphNodeOp.CompareEqInt] = HandleCompareEqInt;
            h[(ushort)GraphNodeOp.HasTag] = HandleHasTag;

            // ── Hex spatial queries (130-132) ──
            h[(ushort)GraphNodeOp.QueryHexRange] = HandleQueryHexRange;
            h[(ushort)GraphNodeOp.QueryHexRing] = HandleQueryHexRing;
            h[(ushort)GraphNodeOp.QueryHexNeighbors] = HandleQueryHexNeighbors;

            // ── Math (22-28) ──
            h[(ushort)GraphNodeOp.SubFloat] = HandleSubFloat;
            h[(ushort)GraphNodeOp.DivFloat] = HandleDivFloat;
            h[(ushort)GraphNodeOp.MinFloat] = HandleMinFloat;
            h[(ushort)GraphNodeOp.MaxFloat] = HandleMaxFloat;
            h[(ushort)GraphNodeOp.ClampFloat] = HandleClampFloat;
            h[(ushort)GraphNodeOp.AbsFloat] = HandleAbsFloat;
            h[(ushort)GraphNodeOp.NegFloat] = HandleNegFloat;

            // ── Blackboard immediate read/write (300-305) ──
            h[(ushort)GraphNodeOp.ReadBlackboardFloat] = HandleReadBlackboardFloat;
            h[(ushort)GraphNodeOp.ReadBlackboardInt] = HandleReadBlackboardInt;
            h[(ushort)GraphNodeOp.ReadBlackboardEntity] = HandleReadBlackboardEntity;
            h[(ushort)GraphNodeOp.WriteBlackboardFloat] = HandleWriteBlackboardFloat;
            h[(ushort)GraphNodeOp.WriteBlackboardInt] = HandleWriteBlackboardInt;
            h[(ushort)GraphNodeOp.WriteBlackboardEntity] = HandleWriteBlackboardEntity;

            // ── Config parameter reading (310-312) ──
            h[(ushort)GraphNodeOp.LoadConfigFloat] = HandleLoadConfigFloat;
            h[(ushort)GraphNodeOp.LoadConfigInt] = HandleLoadConfigInt;
            h[(ushort)GraphNodeOp.LoadConfigEffectId] = HandleLoadConfigEffectId;

            // ── Context entity loading (320-322) ──
            h[(ushort)GraphNodeOp.LoadContextSource] = HandleLoadContextSource;
            h[(ushort)GraphNodeOp.LoadContextTarget] = HandleLoadContextTarget;
            h[(ushort)GraphNodeOp.LoadContextTargetContext] = HandleLoadContextTargetContext;

            // ── Dynamic dispatch (202-203) ──
            h[(ushort)GraphNodeOp.ApplyEffectDynamic] = HandleApplyEffectDynamic;
            h[(ushort)GraphNodeOp.FanOutApplyEffectDynamic] = HandleFanOutApplyEffectDynamic;

            // ── Self attribute access for derived graphs (330-331) ──
            h[(ushort)GraphNodeOp.LoadSelfAttribute] = HandleLoadSelfAttribute;
            h[(ushort)GraphNodeOp.WriteSelfAttribute] = HandleWriteSelfAttribute;

            return h;
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
            s.TargetList.SetCount(s.Api.QueryRadius(s.TargetPos, ins.ImmF, s.Targets));
        }

        private static void HandleQueryCone(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.QueryCone(s.TargetPos, s.I[ins.A], s.I[ins.B], ins.ImmF, s.Targets));
        }

        private static void HandleQueryRectangle(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.QueryRectangle(s.TargetPos, s.I[ins.A], s.I[ins.B], ins.Imm, s.Targets));
        }

        private static void HandleQueryLine(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.QueryLine(s.TargetPos, s.I[ins.A], s.I[ins.B], ins.Imm, s.Targets));
        }

        // ── Query Filters ──

        private static void HandleQueryFilterTagAll(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.FilterRequireTag(s.World, s.Api, ins.Imm);
        }

        private static void HandleQuerySortStable(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SortStableDedup();
        }

        private static void HandleQueryLimit(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.Limit(ins.Imm);
        }

        private static void HandleQueryFilterNotEntity(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.FilterNotEntity(s.E[ins.A]);
        }

        private static void HandleQueryFilterLayer(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.FilterLayer(s.World, s.Api, unchecked((uint)ins.Imm));
        }

        private static void HandleQueryFilterRelationship(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.FilterRelationship(s.World, s.Api, s.E[ins.A], ins.Imm);
        }

        // ── Aggregation ──

        private static void HandleAggCount(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.I[ins.Dst] = s.TargetList.Count;
        }

        private static void HandleAggMinByDistance(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.E[ins.Dst] = s.TargetList.MinByDistance(s.World, s.Api, s.TargetPos);
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

        // ── Hex Spatial Queries (130-132) ──

        private static void HandleQueryHexRange(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.QueryHexRange(s.TargetPos, ins.Imm, s.Targets));
        }

        private static void HandleQueryHexRing(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.QueryHexRing(s.TargetPos, ins.Imm, s.Targets));
        }

        private static void HandleQueryHexNeighbors(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            s.TargetList.SetCount(s.Api.QueryHexNeighbors(s.TargetPos, s.Targets));
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

        // ── Self attribute access for derived graphs (330-331) ──

        private static void HandleLoadSelfAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // F[dst] = Caster.Attribute[Imm] — reads current aggregated value from self entity
            var self = s.Caster;
            if (s.World.IsAlive(self) && s.World.Has<AttributeBuffer>(self))
            {
                ref var buf = ref s.World.Get<AttributeBuffer>(self);
                s.F[ins.Dst] = buf.GetCurrent(ins.Imm);
            }
            else
            {
                s.F[ins.Dst] = 0f;
            }
        }

        private static void HandleWriteSelfAttribute(ref GraphExecutionState s, in GraphInstruction ins, ref int pc)
        {
            // Caster.Attribute[Imm] = F[A] — direct SetCurrent bypassing modifier pipeline
            var self = s.Caster;
            if (s.World.IsAlive(self) && s.World.Has<AttributeBuffer>(self))
            {
                ref var buf = ref s.World.Get<AttributeBuffer>(self);
                buf.SetCurrent(ins.Imm, s.F[ins.A]);
            }
        }
    }
}
