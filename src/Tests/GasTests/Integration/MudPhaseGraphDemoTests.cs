using System;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class MudPhaseGraphDemoTests
    {
        [SetUp]
        public void ResetRegistries()
        {
            // Engine-booting fixtures freeze the shared ambient; these demos register
            // their own attributes lazily, so start from a fresh unfrozen table.
            AttributeRegistry.Clear();
        }

        private const string AttrHealthName = "tests.mud.phase.health";
        private const int BbKeyActualDamage = 2;
        private const int BbKeyAccumDamage = 3;
        private const int BbKeyReflectValue = 4;
        private const int ConfigKeyDmgMultiplier = 100;
        private const int ConfigKeyBaseDamage = 101;

        [Test]
        public void MudPhaseGraph_ImpactSpell_FullLifecycle()
        {
            var world = World.Create();
            try
            {
                int attrHealth = ResolveAttributeId(AttrHealthName);
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][PHASE] 魔法对决开始。");
                sb.AppendLine("[MUD][PHASE] 法师准备释放【冲击】。");

                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templateReg = new EffectTemplateRegistry();
                var handlers = new GasGraphOpHandlerTable();
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templateReg);

                int gpProposePre = 1;
                programs.Register(gpProposePre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Validation);

                int gpApplyPre = 2;
                programs.Register(gpApplyPre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = ConfigKeyBaseDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 1, Imm = ConfigKeyDmgMultiplier },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = BbKeyActualDamage, B = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                int gpApplyMain = 3;
                programs.Register(gpApplyMain, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 0, A = 0, Imm = BbKeyActualDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 1, A = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, Imm = attrHealth, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                int gpApplyPost = 4;
                programs.Register(gpApplyPost, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 0, A = 0, Imm = BbKeyActualDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = BbKeyAccumDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = BbKeyAccumDamage, B = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                int gpExpirePre = 5;
                programs.Register(gpExpirePre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 0, A = 0, Imm = BbKeyAccumDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 0.5f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = BbKeyReflectValue, B = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(gpApplyMain);
                presetTypes.Register(in ptDef);

                var behavior = new EffectPhaseGraphBindings();
                behavior.TryAddStep(EffectPhaseId.OnPropose, PhaseSlot.Pre, gpProposePre);
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, gpApplyPre);
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, gpApplyPost);
                behavior.TryAddStep(EffectPhaseId.OnExpire, PhaseSlot.Pre, gpExpirePre);

                var configParams = new EffectConfigParams();
                configParams.TryAddFloat(ConfigKeyBaseDamage, 20f);
                configParams.TryAddFloat(ConfigKeyDmgMultiplier, 1.5f);

                var caster = world.Create(new AttributeBuffer(), new DirtyFlags());
                var target = world.Create(new AttributeBuffer(), new DirtyFlags(), new BlackboardFloatBuffer());
                world.Get<AttributeBuffer>(caster).SetBase(attrHealth, 100f);
                world.Get<AttributeBuffer>(target).SetBase(attrHealth, 100f);
                // ReadBlackboardFloat fails closed on missing keys; the accumulated-damage
                // entry is read by the OnApply Post step before the first write.
                world.Get<BlackboardFloatBuffer>(target).Set(BbKeyAccumDamage, 0f);

                var api = new GasGraphRuntimeApi(world, null, null, null, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                sb.AppendLine("[MUD][PHASE] ① OnPropose: 法师提案【冲击】效果。");
                bool accepted = executor.ExecutePhaseWithValidationResult(
                    world, api, caster, target, default, default,
                    EffectPhaseId.OnPropose, in behavior, EffectPresetType.None,
                    effectCategoryId: 0, effectTemplateId: 0, mergedParams: default);
                That(accepted, Is.True, "Impact proposal should be accepted");
                sb.AppendLine("[MUD][PHASE]    提案通过 ✓");

                sb.AppendLine("[MUD][PHASE] ② OnApply: Pre读取Config计算伤害, Main施加属性修改, Post累计记录。");

                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    0, 0, in configParams);

                float hpAfterApply = world.Get<AttributeBuffer>(target).GetCurrent(attrHealth);
                ref var bbFloat = ref world.Get<BlackboardFloatBuffer>(target);
                bbFloat.TryGet(BbKeyActualDamage, out float actualDmg);
                bbFloat.TryGet(BbKeyAccumDamage, out float accumDmg);

                That(actualDmg, Is.EqualTo(30f).Within(1e-6f), "Actual damage = 20 * 1.5 = 30");
                That(hpAfterApply, Is.EqualTo(70f).Within(1e-6f), "HP = 100 - 30 = 70");
                That(accumDmg, Is.EqualTo(30f).Within(1e-6f), "Accumulated damage = 30");

                sb.AppendLine($"[MUD][PHASE]    Config(base=20, mul=1.5) → ActualDmg={actualDmg:F1}");
                sb.AppendLine($"[MUD][PHASE]    HP: 100 → {hpAfterApply:F1}");
                sb.AppendLine($"[MUD][PHASE]    BB.AccumDamage={accumDmg:F1}");

                // Second application simulates a periodic tick reusing the same bindings.
                sb.AppendLine("[MUD][PHASE] ③ OnApply (第2次): 再次施加，累计伤害叠加。");
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    0, 0, in configParams);

                float hpAfterSecond = world.Get<AttributeBuffer>(target).GetCurrent(attrHealth);
                bbFloat = ref world.Get<BlackboardFloatBuffer>(target);
                bbFloat.TryGet(BbKeyAccumDamage, out float accumDmg2);

                That(hpAfterSecond, Is.EqualTo(40f).Within(1e-6f), "HP = 70 - 30 = 40");
                That(accumDmg2, Is.EqualTo(60f).Within(1e-6f), "Accumulated = 30 + 30 = 60");

                sb.AppendLine($"[MUD][PHASE]    HP: {hpAfterApply:F1} → {hpAfterSecond:F1}");
                sb.AppendLine($"[MUD][PHASE]    BB.AccumDamage={accumDmg2:F1}");

                sb.AppendLine("[MUD][PHASE] ④ OnExpire: 效果到期，计算50%伤害反弹值。");
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnExpire, in behavior, EffectPresetType.None);

                bbFloat = ref world.Get<BlackboardFloatBuffer>(target);
                bbFloat.TryGet(BbKeyReflectValue, out float reflectVal);

                That(reflectVal, Is.EqualTo(30f).Within(1e-6f), "Reflect = 60 * 0.5 = 30");
                sb.AppendLine($"[MUD][PHASE]    BB.ReflectValue={reflectVal:F1} (累计{accumDmg2:F1} × 50%)");

                sb.AppendLine("[MUD][PHASE] 魔法对决结束。");

                string logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mud_phase_graph_demo.log");
                File.WriteAllText(logPath, sb.ToString());
                Console.WriteLine($"[MUD][PHASE] LogFile={logPath}");
                Console.WriteLine(sb.ToString());

                Pass("MUD Phase Graph lifecycle demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void MudPhaseGraph_SkipMain_CustomDamageFormula()
        {
            var world = World.Create();
            try
            {
                int attrHealth = ResolveAttributeId(AttrHealthName);
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][SKIP] 自定义效果：完全覆盖预设，使用自定义公式。");

                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templateReg = new EffectTemplateRegistry();
                var handlers = new GasGraphOpHandlerTable();
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templateReg);

                int gpMainPreset = 10;
                programs.Register(gpMainPreset, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 999f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 99, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                int gpCustomPre = 11;
                programs.Register(gpCustomPre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = ConfigKeyBaseDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 3, A = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, Imm = attrHealth, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(gpMainPreset);
                presetTypes.Register(in ptDef);

                var behavior = new EffectPhaseGraphBindings();
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, gpCustomPre);
                behavior.SetSkipMain(EffectPhaseId.OnApply);

                var configParams = new EffectConfigParams();
                configParams.TryAddFloat(ConfigKeyBaseDamage, 25f);

                var caster = world.Create();
                var target = world.Create(new AttributeBuffer(), new DirtyFlags(), new BlackboardFloatBuffer());
                world.Get<AttributeBuffer>(target).SetBase(attrHealth, 100f);

                var api = new GasGraphRuntimeApi(world, null, null, null, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    0, 0, in configParams);

                float hp = world.Get<AttributeBuffer>(target).GetCurrent(attrHealth);
                That(hp, Is.EqualTo(50f).Within(1e-6f), "HP = 100 - (25*2) = 50");

                ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
                That(bb.TryGet(99, out _), Is.False, "Main graph should not have run (SkipMain)");

                sb.AppendLine($"[MUD][SKIP] Config(baseDamage=25) × 2 = 50伤害");
                sb.AppendLine($"[MUD][SKIP] HP: 100 → {hp:F1}");
                sb.AppendLine($"[MUD][SKIP] 预设Main未执行 ✓");

                Console.WriteLine(sb.ToString());
                Pass("SkipMain custom formula demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void MudPhaseGraph_ConfigReuse_DifferentParamsProduceDifferentResults()
        {
            var world = World.Create();
            try
            {
                int attrHealth = ResolveAttributeId(AttrHealthName);
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][CONFIG] 配置复用：同一Graph程序，不同参数。");

                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templateReg = new EffectTemplateRegistry();
                var handlers = new GasGraphOpHandlerTable();
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templateReg);

                int gpShared = 1;
                programs.Register(gpShared, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = ConfigKeyBaseDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 1, Imm = ConfigKeyDmgMultiplier },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 3, A = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, Imm = attrHealth, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Effect);

                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(gpShared);
                presetTypes.Register(in ptDef);

                var behavior = new EffectPhaseGraphBindings(); // Empty bindings — the preset Main graph runs as-is.

                var fireConfig = new EffectConfigParams();
                fireConfig.TryAddFloat(ConfigKeyBaseDamage, 15f);
                fireConfig.TryAddFloat(ConfigKeyDmgMultiplier, 2.0f);

                var iceConfig = new EffectConfigParams();
                iceConfig.TryAddFloat(ConfigKeyBaseDamage, 10f);
                iceConfig.TryAddFloat(ConfigKeyDmgMultiplier, 1.0f);

                var caster = world.Create();
                var targetA = world.Create(new AttributeBuffer(), new DirtyFlags());
                var targetB = world.Create(new AttributeBuffer(), new DirtyFlags());
                world.Get<AttributeBuffer>(targetA).SetBase(attrHealth, 100f);
                world.Get<AttributeBuffer>(targetB).SetBase(attrHealth, 100f);

                var api = new GasGraphRuntimeApi(world, null, null, null, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                executor.ExecutePhase(world, api, caster, targetA, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    0, 0, in fireConfig);

                executor.ExecutePhase(world, api, caster, targetB, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    0, 0, in iceConfig);

                float hpA = world.Get<AttributeBuffer>(targetA).GetCurrent(attrHealth);
                float hpB = world.Get<AttributeBuffer>(targetB).GetCurrent(attrHealth);

                That(hpA, Is.EqualTo(70f).Within(1e-6f), "火球: 100 - 30 = 70");
                That(hpB, Is.EqualTo(90f).Within(1e-6f), "冰矢: 100 - 10 = 90");

                sb.AppendLine($"[MUD][CONFIG] 火球(base=15,mul=2.0): HP 100→{hpA:F0}");
                sb.AppendLine($"[MUD][CONFIG] 冰矢(base=10,mul=1.0): HP 100→{hpB:F0}");
                sb.AppendLine("[MUD][CONFIG] 同一Graph程序，不同参数 ✓");

                Console.WriteLine(sb.ToString());
                Pass("Config reuse demo complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        private static int ResolveAttributeId(string name)
        {
            int id = AttributeRegistry.GetId(name);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(name);
        }
    }
}
