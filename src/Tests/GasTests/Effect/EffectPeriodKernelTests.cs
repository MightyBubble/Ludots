using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// OnPeriod 纯属性增量编译内核的健全性边界：判定器命中/不命中/混合边界、
    /// 内核与解释 VM 的逐位等价（含 RNG 种子链）、守卫失败落回解释路径并保持
    /// 事务回滚语义、死亡目标的静默跳过、无相位工作模板的空程序编译。
    /// </summary>
    [TestFixture]
    public sealed class EffectPeriodKernelTests
    {
        private const int SnapshotCapacity = 64;
        private const int FanOutCapacity = 64;

        // 生产形态：GraphControlFlowCompiler 对线性控制边生成直落 Jump(+0)，尾部显式 HaltReturnInt。
        private static GraphInstruction[] PureDriftProgram(int attributeId) => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.RandomFloat01, Dst = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 0.5f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.SubFloat, Dst = 3, A = 1, B = 2 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 4, ImmF = 16f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 5, A = 3, B = 4 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 5, Imm = attributeId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
        };

        private sealed class KernelHarness
        {
            public World World = default!;
            public DiscreteClock Clock = default!;
            public EffectTemplateRegistry Templates = default!;
            public GraphProgramRegistry Programs = default!;
            public PresetTypeRegistry PresetTypes = default!;
            public BuiltinHandlerRegistry BuiltinHandlers = default!;
            public EffectLifetimeSystem Lifetime = default!;
            public AttributeAggregatorSystem Aggregator = default!;
            public TagOps TagOps = default!;

            public static KernelHarness Create(
                Action<GraphProgramRegistry, int, int>? registerGraphs = null,
                Action<EffectTemplateRegistry, int>? registerTemplates = null,
                Action<PresetTypeRegistry>? registerPresets = null,
                bool withKernel = true)
            {
                var world = World.Create();
                var tagOps = new TagOps(
                    new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                    new TagRuleRegistry(),
                    aggregateDirty: new AttributeAggregateDirtyRegistry());
                var clock = new DiscreteClock();
                var templates = new EffectTemplateRegistry();
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                Ludots.Core.Gameplay.GAS.BuiltinHandlers.RegisterAll(builtinHandlers);
                registerPresets?.Invoke(presetTypes);
                registerGraphs?.Invoke(programs, 9001, 701);
                registerTemplates?.Invoke(templates, 701);

                if (withKernel)
                {
                    GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                        templates, presetTypes, builtinHandlers, programs, "Test/EffectPeriodKernelTests.json");
                }
                else
                {
                    var plans = new EffectExecutionPlanSet[EffectTemplateRegistry.MaxTemplates];
                    for (int templateId = 1; templateId < EffectTemplateRegistry.MaxTemplates; templateId++)
                    {
                        if (!templates.TryGetRef(templateId, out _))
                        {
                            continue;
                        }

                        plans[templateId] = new EffectExecutionPlanSet(
                            new EffectWindowExecutionPlan(EffectExecutionPlanKind.GasTransactional),
                            new EffectWindowExecutionPlan(EffectExecutionPlanKind.GasTransactional),
                            new EffectWindowExecutionPlan(EffectExecutionPlanKind.GasTransactional),
                            new EffectWindowExecutionPlan(EffectExecutionPlanKind.GasTransactional));
                    }

                    templates.FinalizeExecutionPlans(plans);
                }

                var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, new GasGraphOpHandlerTable(), templates);
                var lifetime = new EffectLifetimeSystem(
                    world,
                    clock,
                    new GasConditionRegistry(),
                    SnapshotCapacity,
                    FanOutCapacity,
                    templates: templates,
                    phaseExecutor: executor,
                    graphApi: graphApi,
                    tagOps: tagOps);
                var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps, aggregateDirty: tagOps.AggregateDirty);

                return new KernelHarness
                {
                    World = world,
                    Clock = clock,
                    Templates = templates,
                    Programs = programs,
                    PresetTypes = presetTypes,
                    BuiltinHandlers = builtinHandlers,
                    Lifetime = lifetime,
                    Aggregator = aggregator,
                    TagOps = tagOps,
                };
            }
        }

        [Test]
        public void PureDriftTemplate_CompilesIntoKernelTable()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelPureHealth");
            var harness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) =>
                    programs.Register(graphId, PureDriftProgram(healthId), GraphKind.Effect),
                registerTemplates: (templates, templateId) =>
                {
                    var bindings = new EffectPhaseGraphBindings();
                    That(bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, 9001), Is.True);
                    templates.Register(templateId, new EffectTemplateData
                    {
                        PresetType = EffectPresetType.None,
                        LifetimeKind = EffectLifetimeKind.Infinite,
                        ClockId = GasClockId.FixedFrame,
                        PeriodTicks = 2,
                        PhaseGraphBindings = bindings,
                    });
                });

            var table = harness.Templates.PeriodKernelTable;
            That(table, Is.Not.Null);
            That(table!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Compiled));
            That(table.CompiledTemplateCount, Is.EqualTo(1));
            That(table.InterpretedTemplateCount, Is.EqualTo(0));
            That(table.TryGetProgram(701, out var program), Is.True);
            That(program!.AttributeId, Is.EqualTo(healthId));
            That(program.ModifyTargetSlot, Is.EqualTo(ContextSlot.OriginalTarget));
        }

        [Test]
        public void ComplexGraph_WithQueryAndSend_StaysInterpreted()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelComplexHealth");
            var harness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.RandomFloat01, Dst = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, Imm = 100 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = healthId },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect),
                registerTemplates: (templates, templateId) =>
                {
                    var bindings = new EffectPhaseGraphBindings();
                    That(bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, 9001), Is.True);
                    templates.Register(templateId, new EffectTemplateData
                    {
                        PresetType = EffectPresetType.None,
                        LifetimeKind = EffectLifetimeKind.Infinite,
                        ClockId = GasClockId.FixedFrame,
                        PeriodTicks = 2,
                        PhaseGraphBindings = bindings,
                    });
                });

            var table = harness.Templates.PeriodKernelTable;
            That(table!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Interpreted));
            That(table.TryGetProgram(701, out _), Is.False);
            That(table.InterpretedTemplateCount, Is.EqualTo(1));
        }

        [Test]
        public void TwoModifyAttributeAdds_StaysInterpreted()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelDoubleHealth");
            var harness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = healthId },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = healthId },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect),
                registerTemplates: RegisterPurePeriodicTemplate);

            That(harness.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Interpreted));
        }

        [Test]
        public void ModifyNotFinalInstruction_StaysInterpreted()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelTrailingHealth");
            var harness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = healthId },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 9f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect),
                registerTemplates: RegisterPurePeriodicTemplate);

            That(harness.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Interpreted));
        }

        [Test]
        public void PresetMainApplyModifiers_EmptyModifierSet_Compiles_NonEmpty_StaysInterpreted()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelDotHealth");
            GraphInstruction[] drift = PureDriftProgram(healthId);

            var emptyModifiers = KernelHarness.Create(
                registerPresets: RegisterDotPreset,
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, drift, GraphKind.Effect),
                registerTemplates: (templates, templateId) => RegisterDotTemplate(templates, templateId, modifiersCount: 0));
            That(emptyModifiers.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Compiled));

            var withModifiers = KernelHarness.Create(
                registerPresets: RegisterDotPreset,
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, drift, GraphKind.Effect),
                registerTemplates: (templates, templateId) => RegisterDotTemplate(templates, templateId, modifiersCount: 1));
            That(withModifiers.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Interpreted));
        }

        [Test]
        public void PeriodicTemplateWithoutPhaseWork_CompilesEmptyProgram()
        {
            var harness = KernelHarness.Create(registerTemplates: (templates, templateId) =>
                templates.Register(templateId, new EffectTemplateData
                {
                    PresetType = EffectPresetType.None,
                    LifetimeKind = EffectLifetimeKind.Infinite,
                    ClockId = GasClockId.FixedFrame,
                    PeriodTicks = 2,
                }));

            var table = harness.Templates.PeriodKernelTable;
            That(table!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Compiled));
            That(table.TryGetProgram(701, out var program), Is.True);
            That(program!.AttributeId, Is.EqualTo(Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId));
        }

        [Test]
        public void KernelExecution_IsBitIdenticalToInterpretedExecution()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelParityHealth");
            const int periods = 24;

            float kernelFinal = RunDriftScenario(withKernel: true, healthId, periods, out int kernelEffectId, out int kernelTargetId);
            float interpretedFinal = RunDriftScenario(withKernel: false, healthId, periods, out int interpretedEffectId, out int interpretedTargetId);

            That(kernelEffectId, Is.EqualTo(interpretedEffectId), "same construction order must produce same entity ids for seed parity");
            That(kernelTargetId, Is.EqualTo(interpretedTargetId));
            That(
                BitConverter.SingleToInt32Bits(kernelFinal),
                Is.EqualTo(BitConverter.SingleToInt32Bits(interpretedFinal)),
                $"kernel={kernelFinal} interpreted={interpretedFinal} must be bit-identical (RNG seed chain + write order parity)");
        }

        [Test]
        public void GuardFailure_TargetMissingDirtyFlags_RoutesToInterpretedAndRollsBackBatch()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelGuardHealth");

            var kernelHarness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, PureDriftProgram(healthId), GraphKind.Effect),
                registerTemplates: RegisterPurePeriodicTemplate);
            That(kernelHarness.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Compiled));

            var world = kernelHarness.World;
            var healthyTarget = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new DirtyFlags());
            ref var healthyAttributes = ref world.Get<AttributeBuffer>(healthyTarget);
            healthyAttributes.SetBase(healthId, 100f);
            healthyAttributes.SetCurrent(healthId, 100f);
            var guardedTarget = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            var source = world.Create();

            var healthyEffect = CreateCommittedEffect(world, source, healthyTarget, periodTicks: 1);
            var guardedEffect = CreateCommittedEffect(world, source, guardedTarget, periodTicks: 1);

            kernelHarness.Clock.Advance(ClockDomainId.FixedFrame);
            kernelHarness.Lifetime.Update(0.016f);
            kernelHarness.Clock.Advance(ClockDomainId.FixedFrame);
            InvalidOperationException thrown = Throws<InvalidOperationException>(() => kernelHarness.Lifetime.Update(0.016f));
            That(thrown.Message, Does.Contain(TagOps.MissingDirtyFlagsError));

            That(
                world.Get<AttributeBuffer>(healthyTarget).GetCurrent(healthId),
                Is.EqualTo(100f).Within(0.0001f),
                "guard failure must roll back the whole slice transaction, including already-staged kernel writes");
        }

        [Test]
        public void DeadTarget_KernelSilentlySkipsLikeInterpreted()
        {
            int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("KernelDeadHealth");
            var harness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, PureDriftProgram(healthId), GraphKind.Effect),
                registerTemplates: RegisterPurePeriodicTemplate);
            That(harness.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Compiled));

            var world = harness.World;
            var target = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new DirtyFlags());
            var source = world.Create();
            var effect = CreateCommittedEffect(world, source, target, periodTicks: 1);
            world.Destroy(target);

            harness.Clock.Advance(ClockDomainId.FixedFrame);
            harness.Lifetime.Update(0.016f);
            harness.Clock.Advance(ClockDomainId.FixedFrame);
            DoesNotThrow(() => harness.Lifetime.Update(0.016f));
        }

        private static float RunDriftScenario(bool withKernel, int healthId, int periods, out int effectId, out int targetId)
        {
            var harness = KernelHarness.Create(
                registerGraphs: (programs, graphId, templateId) => programs.Register(graphId, PureDriftProgram(healthId), GraphKind.Effect),
                registerTemplates: RegisterPurePeriodicTemplate,
                withKernel: withKernel);
            if (withKernel)
            {
                That(harness.Templates.PeriodKernelTable!.GetClassification(701), Is.EqualTo(EffectPeriodKernelClassification.Compiled));
            }
            else
            {
                That(harness.Templates.PeriodKernelTable, Is.Null);
            }

            var world = harness.World;
            var target = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new DirtyFlags());
            ref var attributes = ref world.Get<AttributeBuffer>(target);
            attributes.SetBase(healthId, 5000f);
            attributes.SetCurrent(healthId, 5000f);
            var source = world.Create();
            var effect = CreateCommittedEffect(world, source, target, periodTicks: 1);
            effectId = effect.Id;
            targetId = target.Id;

            for (int i = 0; i < periods * 2 + 2; i++)
            {
                harness.Clock.Advance(ClockDomainId.FixedFrame);
                harness.Lifetime.Update(0.016f);
                harness.Aggregator.Update(0.016f);
            }

            return world.Get<AttributeBuffer>(target).GetCurrent(healthId);
        }

        private static Entity CreateCommittedEffect(World world, Entity source, Entity target, int periodTicks)
        {
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 0,
                lifetimeKind: EffectLifetimeKind.Infinite,
                periodTicks: periodTicks,
                targetContext: target,
                clockId: GasClockId.FixedFrame);
            world.Add(effect, new EffectTemplateRef { TemplateId = 701 });
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);
            return effect;
        }

        private static void RegisterPurePeriodicTemplate(EffectTemplateRegistry templates, int templateId)
        {
            var bindings = new EffectPhaseGraphBindings();
            That(bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, 9001), Is.True);
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.Infinite,
                ClockId = GasClockId.FixedFrame,
                PeriodTicks = 2,
                PhaseGraphBindings = bindings,
            });
        }

        private static void RegisterDotPreset(PresetTypeRegistry presetTypes)
        {
            var definition = new PresetTypeDefinition
            {
                Type = EffectPresetType.DoT,
                TypeId = (int)EffectPresetType.DoT,
                TypeKey = nameof(EffectPresetType.DoT),
            };
            definition.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            definition.DefaultPhaseHandlers[EffectPhaseId.OnPeriod] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(definition);
        }

        private static void RegisterDotTemplate(EffectTemplateRegistry templates, int templateId, int modifiersCount)
        {
            var bindings = new EffectPhaseGraphBindings();
            That(bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, 9001), Is.True);
            var data = new EffectTemplateData
            {
                PresetType = EffectPresetType.DoT,
                LifetimeKind = EffectLifetimeKind.Infinite,
                ClockId = GasClockId.FixedFrame,
                PeriodTicks = 2,
                PhaseGraphBindings = bindings,
            };
            for (int i = 0; i < modifiersCount; i++)
            {
                That(data.Modifiers.Add(3, ModifierOp.Add, -1f), Is.True);
            }

            templates.Register(templateId, data);
        }
    }
}
