using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.GAS
{
    public enum EffectPeriodKernelClassification : byte
    {
        NotPeriodic = 0,
        Compiled = 1,
        Interpreted = 2,
    }

    /// <summary>
    /// OnPeriod 纯属性增量编译内核（双车道合同的快车道）。加载期对每个周期模板做静态证明：
    /// Pre/Post 图仅由 ConstFloat/RandomFloat01/浮点算术/LoadContext* 构成，终点恰一个
    /// ModifyAttributeAdd 且两个操作数在同段内有定义（目标操作数追溯到 LoadContext*）；
    /// Main 处理器缺席或为可证明空转（ApplyModifiers 且模板修饰符为空）。可证明者编译为
    /// 稠密指令流，到期时逐条求值并经 EffectPhaseSideEffectTransaction.StageAttributeAdd
    /// 直写——与解释 VM 同事务、同 RNG 种子链、同写入顺序，逐位等价。不可证明者留解释
    /// VM：这是编译健全性边界，不是 fallback；快车道存在的前提正是复杂图保有完整慢车道。
    /// </summary>
    public sealed class EffectPeriodKernelTable
    {
        public const int MaxTemplates = EffectTemplateRegistry.MaxTemplates;

        private readonly EffectPeriodKernelProgram?[] _programs = new EffectPeriodKernelProgram?[MaxTemplates];
        private readonly byte[] _classifications = new byte[MaxTemplates];
        private readonly ulong[] _hasBits = new ulong[MaxTemplates >> 6];

        public int CompiledTemplateCount { get; private set; }
        public int InterpretedTemplateCount { get; private set; }

        public bool TryGetProgram(int templateId, out EffectPeriodKernelProgram program)
        {
            program = null!;
            if ((uint)templateId >= (uint)MaxTemplates)
            {
                return false;
            }

            int word = templateId >> 6;
            int bit = templateId & 63;
            if ((_hasBits[word] & (1UL << bit)) == 0UL)
            {
                return false;
            }

            EffectPeriodKernelProgram? candidate = _programs[templateId];
            if (candidate == null)
            {
                return false;
            }

            program = candidate;
            return true;
        }

        public EffectPeriodKernelClassification GetClassification(int templateId)
        {
            return (uint)templateId >= (uint)MaxTemplates
                ? EffectPeriodKernelClassification.NotPeriodic
                : (EffectPeriodKernelClassification)_classifications[templateId];
        }

        internal void Record(int templateId, EffectPeriodKernelProgram? program)
        {
            if ((uint)templateId >= (uint)MaxTemplates)
            {
                throw new ArgumentOutOfRangeException(nameof(templateId));
            }

            _classifications[templateId] = (byte)(program != null
                ? EffectPeriodKernelClassification.Compiled
                : EffectPeriodKernelClassification.Interpreted);
            _programs[templateId] = program;
            int word = templateId >> 6;
            int bit = templateId & 63;
            if (program != null)
            {
                _hasBits[word] |= 1UL << bit;
                CompiledTemplateCount++;
            }
            else
            {
                _hasBits[word] &= ~(1UL << bit);
                InterpretedTemplateCount++;
            }
        }
    }

    public sealed class EffectPeriodKernelProgram
    {
        internal struct Segment
        {
            public int GraphProgramId;
            public int InstructionStart;
            public int InstructionCount;
        }

        public int TemplateId;
        public int AttributeId;
        public ContextSlot ModifyTargetSlot;

        internal EffectPeriodKernelInstruction[] Instructions = Array.Empty<EffectPeriodKernelInstruction>();
        internal Segment[] Segments = Array.Empty<Segment>();
        internal int ModifyInstructionIndex = -1;
        internal int FloatRegisterCount;
        internal int EntityRegisterCount;

        private float[] _floatRegs = Array.Empty<float>();
        private Entity[] _entityRegs = Array.Empty<Entity>();

        /// <summary>
        /// 求值增量与写入目标。target 为 Entity.Null 表示复现解释 VM 的静默跳过语义
        /// （HandleModifyAttributeAdd 对死亡目标的 no-write 行为）。返回 false 表示命中
        /// 解释路径的失败面（目标缺 AttributeBuffer/DirtyFlags），调用方必须落回逐事件
        /// 解释路径重放以复现既有错误。
        /// </summary>
        public bool TryEvaluate(
            World world,
            Entity effectEntity,
            in EffectContext context,
            int clockTick,
            out Entity target,
            out float delta)
        {
            target = Entity.Null;
            delta = 0f;
            if (Instructions.Length == 0)
            {
                return true;
            }

            if (_floatRegs.Length < FloatRegisterCount)
            {
                _floatRegs = new float[FloatRegisterCount];
                _entityRegs = new Entity[EntityRegisterCount];
            }

            Array.Clear(_floatRegs, 0, FloatRegisterCount);
            Array.Clear(_entityRegs, 0, EntityRegisterCount);

            uint rngState = 0;
            bool rngSeeded = false;
            int segmentIndex = 0;
            Segment segment = Segments[0];
            Span<EffectPeriodKernelInstruction> instructions = Instructions;
            for (int i = 0; i < instructions.Length; i++)
            {
                if (i - segment.InstructionStart >= segment.InstructionCount)
                {
                    segmentIndex++;
                    segment = Segments[segmentIndex];
                    rngSeeded = false;
                }

                ref readonly EffectPeriodKernelInstruction instruction = ref instructions[i];
                switch (instruction.Op)
                {
                    case EffectPeriodKernelOp.ConstFloat:
                        _floatRegs[instruction.Dst] = instruction.ImmF;
                        break;
                    case EffectPeriodKernelOp.RandomFloat01:
                        if (!rngSeeded)
                        {
                            uint executionSeed = Systems.EffectLifetimeSystem.BuildExecutionSeedForKernel(
                                effectEntity, EffectPhaseId.OnPeriod, TemplateId, clockTick, in context);
                            rngState = Systems.EffectPhaseExecutor.BuildRandomSeedForKernel(
                                context.Source, context.Target, context.TargetContext,
                                segment.GraphProgramId, TemplateId, EffectPhaseId.OnPeriod, executionSeed);
                            rngSeeded = true;
                        }

                        rngState = AdvanceRng(rngState);
                        _floatRegs[instruction.Dst] = (rngState & 0x00FFFFFFu) / 16777215f;
                        break;
                    case EffectPeriodKernelOp.AddFloat:
                        _floatRegs[instruction.Dst] = _floatRegs[instruction.A] + _floatRegs[instruction.B];
                        break;
                    case EffectPeriodKernelOp.SubFloat:
                        _floatRegs[instruction.Dst] = _floatRegs[instruction.A] - _floatRegs[instruction.B];
                        break;
                    case EffectPeriodKernelOp.MulFloat:
                        _floatRegs[instruction.Dst] = _floatRegs[instruction.A] * _floatRegs[instruction.B];
                        break;
                    case EffectPeriodKernelOp.DivFloat:
                        float divisor = _floatRegs[instruction.B];
                        _floatRegs[instruction.Dst] = divisor == 0f ? 0f : _floatRegs[instruction.A] / divisor;
                        break;
                    case EffectPeriodKernelOp.MinFloat:
                        _floatRegs[instruction.Dst] = _floatRegs[instruction.A] < _floatRegs[instruction.B]
                            ? _floatRegs[instruction.A]
                            : _floatRegs[instruction.B];
                        break;
                    case EffectPeriodKernelOp.MaxFloat:
                        _floatRegs[instruction.Dst] = _floatRegs[instruction.A] > _floatRegs[instruction.B]
                            ? _floatRegs[instruction.A]
                            : _floatRegs[instruction.B];
                        break;
                    case EffectPeriodKernelOp.ClampFloat:
                        float value = _floatRegs[instruction.A];
                        float min = _floatRegs[instruction.B];
                        float max = _floatRegs[instruction.C];
                        _floatRegs[instruction.Dst] = value < min ? min : (value > max ? max : value);
                        break;
                    case EffectPeriodKernelOp.AbsFloat:
                        float absValue = _floatRegs[instruction.A];
                        _floatRegs[instruction.Dst] = absValue < 0f ? -absValue : absValue;
                        break;
                    case EffectPeriodKernelOp.NegFloat:
                        _floatRegs[instruction.Dst] = -_floatRegs[instruction.A];
                        break;
                    case EffectPeriodKernelOp.LoadContextSource:
                        _entityRegs[instruction.Dst] = context.Source;
                        break;
                    case EffectPeriodKernelOp.LoadContextTarget:
                        _entityRegs[instruction.Dst] = context.Target;
                        break;
                    case EffectPeriodKernelOp.LoadContextTargetContext:
                        _entityRegs[instruction.Dst] = context.TargetContext;
                        break;
                    case EffectPeriodKernelOp.ModifyAttributeAdd:
                        target = _entityRegs[instruction.A];
                        delta = _floatRegs[instruction.B];
                        if (!world.IsAlive(target))
                        {
                            target = Entity.Null;
                            return true;
                        }

                        if (!world.Has<AttributeBuffer>(target) || !world.Has<DirtyFlags>(target))
                        {
                            return false;
                        }

                        return true;
                    default:
                        throw new InvalidOperationException(
                            $"GAS.EFFECT_PERIOD_KERNEL.ERR.UnsupportedOp: templateId={TemplateId}, op={instruction.Op}.");
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint AdvanceRng(uint x)
        {
            if (x == 0u)
            {
                x = 2463534242u;
            }

            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }
    }

    internal enum EffectPeriodKernelOp : byte
    {
        ConstFloat = 0,
        RandomFloat01 = 1,
        AddFloat = 2,
        SubFloat = 3,
        MulFloat = 4,
        DivFloat = 5,
        MinFloat = 6,
        MaxFloat = 7,
        ClampFloat = 8,
        AbsFloat = 9,
        NegFloat = 10,
        LoadContextSource = 11,
        LoadContextTarget = 12,
        LoadContextTargetContext = 13,
        ModifyAttributeAdd = 14,
    }

    internal struct EffectPeriodKernelInstruction
    {
        public EffectPeriodKernelOp Op;
        public byte Dst;
        public byte A;
        public byte B;
        public byte C;
        public float ImmF;
        public int Imm;
    }

    public static class EffectPeriodKernelCompiler
    {
        public static EffectPeriodKernelTable Compile(
            EffectTemplateRegistry templates,
            PresetTypeRegistry presetTypes,
            GraphProgramRegistry graphPrograms)
        {
            ArgumentNullException.ThrowIfNull(templates);
            ArgumentNullException.ThrowIfNull(presetTypes);
            ArgumentNullException.ThrowIfNull(graphPrograms);

            var table = new EffectPeriodKernelTable();
            for (int templateId = 1; templateId < EffectTemplateRegistry.MaxTemplates; templateId++)
            {
                if (!templates.TryGetRef(templateId, out _))
                {
                    continue;
                }

                ref readonly EffectTemplateData template = ref templates.GetRef(templateId);
                if (template.PeriodTicks <= 0)
                {
                    continue;
                }

                table.Record(templateId, TryCompileTemplate(templateId, in template, presetTypes, graphPrograms));
            }

            return table;
        }

        private static EffectPeriodKernelProgram? TryCompileTemplate(
            int templateId,
            in EffectTemplateData template,
            PresetTypeRegistry presetTypes,
            GraphProgramRegistry graphPrograms)
        {
            if (!TryResolveMainAsProvableNoOp(in template, presetTypes))
            {
                return null;
            }

            int preGraphId = template.PhaseGraphBindings.GetGraphId(EffectPhaseId.OnPeriod, PhaseSlot.Pre);
            int postGraphId = template.PhaseGraphBindings.GetGraphId(EffectPhaseId.OnPeriod, PhaseSlot.Post);

            var instructions = new List<EffectPeriodKernelInstruction>(16);
            var segments = new List<EffectPeriodKernelProgram.Segment>(2);
            int modifyCount = 0;
            int lastModifyListIndex = -1;

            if (!TryAppendGraph(preGraphId, graphPrograms, instructions, segments, ref modifyCount, ref lastModifyListIndex) ||
                !TryAppendGraph(postGraphId, graphPrograms, instructions, segments, ref modifyCount, ref lastModifyListIndex))
            {
                return null;
            }

            if (modifyCount > 1 || (modifyCount == 1 && lastModifyListIndex != instructions.Count - 1))
            {
                return null;
            }

            if (modifyCount == 0)
            {
                return new EffectPeriodKernelProgram
                {
                    TemplateId = templateId,
                    AttributeId = AttributeRegistry.InvalidId,
                };
            }

            Span<EffectPeriodKernelInstruction> span = CollectionsMarshal.AsSpan(instructions);
            ref readonly EffectPeriodKernelInstruction modify = ref span[lastModifyListIndex];
            int attributeId = modify.Imm;
            if ((uint)attributeId >= (uint)AttributeBuffer.MAX_ATTRS)
            {
                return null;
            }

            int segmentStart = FindOwningSegmentStart(segments, lastModifyListIndex);
            int entityDefinitionIndex = FindLastContextLoadIndex(span, segmentStart, lastModifyListIndex, modify.A);
            if (entityDefinitionIndex < 0)
            {
                return null;
            }

            if (FindLastFloatDefinitionIndex(span, segmentStart, lastModifyListIndex, modify.B) < 0)
            {
                return null;
            }

            var program = new EffectPeriodKernelProgram
            {
                TemplateId = templateId,
                AttributeId = attributeId,
                ModifyTargetSlot = ResolveContextSlot(span[entityDefinitionIndex].Op),
                Instructions = instructions.ToArray(),
                Segments = segments.ToArray(),
                ModifyInstructionIndex = lastModifyListIndex,
            };
            CountRegisters(program);
            return program;
        }

        private static bool TryResolveMainAsProvableNoOp(
            in EffectTemplateData template,
            PresetTypeRegistry presetTypes)
        {
            int mainGraphId = template.PhaseGraphBindings.GetGraphId(EffectPhaseId.OnPeriod, PhaseSlot.Main);
            if (mainGraphId > 0)
            {
                return false;
            }

            if (template.PhaseGraphBindings.IsSkipMain(EffectPhaseId.OnPeriod))
            {
                return true;
            }

            int presetTypeId = template.EffectivePresetTypeId;
            if (!presetTypes.IsRegistered(presetTypeId))
            {
                return presetTypeId == 0;
            }

            PhaseHandler handler = presetTypes.Get(presetTypeId).DefaultPhaseHandlers[EffectPhaseId.OnPeriod];
            if (!handler.IsValid)
            {
                return true;
            }

            // ApplyModifiers 对空修饰符集为可证明空转：模板修饰符提交后不可变，
            // 本相位入口 ResetPerEffect 清除运行时 override；其余 builtin/graph 不可证明。
            return handler.Kind == PhaseHandlerKind.Builtin &&
                (BuiltinHandlerId)handler.HandlerId == BuiltinHandlerId.ApplyModifiers &&
                template.Modifiers.Count == 0;
        }

        private static bool TryAppendGraph(
            int graphId,
            GraphProgramRegistry graphPrograms,
            List<EffectPeriodKernelInstruction> instructions,
            List<EffectPeriodKernelProgram.Segment> segments,
            ref int modifyCount,
            ref int lastModifyListIndex)
        {
            if (graphId <= 0)
            {
                return true;
            }

            if (!graphPrograms.TryGetProgram(graphId, out var program))
            {
                return false;
            }

            int start = instructions.Count;
            for (int i = 0; i < program.Length; i++)
            {
                ref readonly GraphInstruction source = ref program[i];
                if (source.Op == 0)
                {
                    continue;
                }

                // HaltReturnInt 是图的显式终结（注册期强制存在）：VM 在此停止，
                // 线性纯图内 halt 之后的指令不可达，编译取 halt 前缀即为全部可执行效果。
                if ((GraphNodeOp)source.Op == GraphNodeOp.HaltReturnInt)
                {
                    break;
                }

                // GraphControlFlowCompiler 对线性控制边生成直落 Jump(+0)：VM 等价 no-op；
                // 非零跳转破坏线性可证性，整体留解释 VM。
                if ((GraphNodeOp)source.Op == GraphNodeOp.Jump)
                {
                    if (source.Imm != 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (!TryMapOp((GraphNodeOp)source.Op, out EffectPeriodKernelOp mapped))
                {
                    return false;
                }

                if (mapped == EffectPeriodKernelOp.ModifyAttributeAdd)
                {
                    modifyCount++;
                    lastModifyListIndex = instructions.Count;
                    instructions.Add(new EffectPeriodKernelInstruction
                    {
                        Op = mapped,
                        A = source.A,
                        B = source.B,
                        Imm = source.Imm,
                    });
                    continue;
                }

                if ((uint)source.Dst >= (uint)GraphVmLimits.MaxFloatRegisters ||
                    (uint)source.A >= (uint)GraphVmLimits.MaxFloatRegisters ||
                    (uint)source.B >= (uint)GraphVmLimits.MaxFloatRegisters ||
                    (uint)source.C >= (uint)GraphVmLimits.MaxFloatRegisters)
                {
                    return false;
                }

                instructions.Add(new EffectPeriodKernelInstruction
                {
                    Op = mapped,
                    Dst = source.Dst,
                    A = source.A,
                    B = source.B,
                    C = source.C,
                    ImmF = source.ImmF,
                });
            }

            if (instructions.Count > start)
            {
                segments.Add(new EffectPeriodKernelProgram.Segment
                {
                    GraphProgramId = graphId,
                    InstructionStart = start,
                    InstructionCount = instructions.Count - start,
                });
            }

            return true;
        }

        private static int FindOwningSegmentStart(List<EffectPeriodKernelProgram.Segment> segments, int instructionIndex)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (instructionIndex >= segments[i].InstructionStart &&
                    instructionIndex < segments[i].InstructionStart + segments[i].InstructionCount)
                {
                    return segments[i].InstructionStart;
                }
            }

            throw new InvalidOperationException(
                $"GAS.EFFECT_PERIOD_KERNEL.ERR.SegmentMissing: instructionIndex={instructionIndex}.");
        }

        private static int FindLastContextLoadIndex(
            Span<EffectPeriodKernelInstruction> instructions,
            int segmentStart,
            int modifyIndex,
            byte register)
        {
            for (int i = modifyIndex - 1; i >= segmentStart; i--)
            {
                if (IsContextLoad(instructions[i].Op) && instructions[i].Dst == register)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindLastFloatDefinitionIndex(
            Span<EffectPeriodKernelInstruction> instructions,
            int segmentStart,
            int modifyIndex,
            byte register)
        {
            for (int i = modifyIndex - 1; i >= segmentStart; i--)
            {
                EffectPeriodKernelOp op = instructions[i].Op;
                if (op == EffectPeriodKernelOp.ModifyAttributeAdd || IsContextLoad(op))
                {
                    continue;
                }

                if (instructions[i].Dst == register)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsContextLoad(EffectPeriodKernelOp op)
        {
            return op is EffectPeriodKernelOp.LoadContextSource or
                EffectPeriodKernelOp.LoadContextTarget or
                EffectPeriodKernelOp.LoadContextTargetContext;
        }

        private static ContextSlot ResolveContextSlot(EffectPeriodKernelOp contextLoad)
        {
            return contextLoad switch
            {
                EffectPeriodKernelOp.LoadContextSource => ContextSlot.OriginalSource,
                EffectPeriodKernelOp.LoadContextTargetContext => ContextSlot.OriginalTargetContext,
                _ => ContextSlot.OriginalTarget,
            };
        }

        private static bool TryMapOp(GraphNodeOp op, out EffectPeriodKernelOp mapped)
        {
            switch (op)
            {
                case GraphNodeOp.ConstFloat: mapped = EffectPeriodKernelOp.ConstFloat; return true;
                case GraphNodeOp.RandomFloat01: mapped = EffectPeriodKernelOp.RandomFloat01; return true;
                case GraphNodeOp.AddFloat: mapped = EffectPeriodKernelOp.AddFloat; return true;
                case GraphNodeOp.SubFloat: mapped = EffectPeriodKernelOp.SubFloat; return true;
                case GraphNodeOp.MulFloat: mapped = EffectPeriodKernelOp.MulFloat; return true;
                case GraphNodeOp.DivFloat: mapped = EffectPeriodKernelOp.DivFloat; return true;
                case GraphNodeOp.MinFloat: mapped = EffectPeriodKernelOp.MinFloat; return true;
                case GraphNodeOp.MaxFloat: mapped = EffectPeriodKernelOp.MaxFloat; return true;
                case GraphNodeOp.ClampFloat: mapped = EffectPeriodKernelOp.ClampFloat; return true;
                case GraphNodeOp.AbsFloat: mapped = EffectPeriodKernelOp.AbsFloat; return true;
                case GraphNodeOp.NegFloat: mapped = EffectPeriodKernelOp.NegFloat; return true;
                case GraphNodeOp.LoadContextSource: mapped = EffectPeriodKernelOp.LoadContextSource; return true;
                case GraphNodeOp.LoadContextTarget: mapped = EffectPeriodKernelOp.LoadContextTarget; return true;
                case GraphNodeOp.LoadContextTargetContext: mapped = EffectPeriodKernelOp.LoadContextTargetContext; return true;
                case GraphNodeOp.ModifyAttributeAdd: mapped = EffectPeriodKernelOp.ModifyAttributeAdd; return true;
                default: mapped = default; return false;
            }
        }

        private static void CountRegisters(EffectPeriodKernelProgram program)
        {
            int maxFloat = 0;
            int maxEntity = 0;
            Span<EffectPeriodKernelInstruction> span = program.Instructions;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly EffectPeriodKernelInstruction instruction = ref span[i];
                if (instruction.Op == EffectPeriodKernelOp.ModifyAttributeAdd)
                {
                    maxFloat = Math.Max(maxFloat, instruction.B + 1);
                    maxEntity = Math.Max(maxEntity, instruction.A + 1);
                }
                else if (IsContextLoad(instruction.Op))
                {
                    maxEntity = Math.Max(maxEntity, instruction.Dst + 1);
                }
                else
                {
                    maxFloat = Math.Max(maxFloat, instruction.Dst + 1);
                    maxFloat = Math.Max(maxFloat, instruction.A + 1);
                    maxFloat = Math.Max(maxFloat, instruction.B + 1);
                    maxFloat = Math.Max(maxFloat, instruction.C + 1);
                }
            }

            program.FloatRegisterCount = Math.Max(1, maxFloat);
            program.EntityRegisterCount = Math.Max(1, maxEntity);
        }
    }
}
