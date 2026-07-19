using System;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// MUD-style scenario tests for the Phase Graph architecture.
    /// Exercises the full Pre/Main/Post lifecycle, Blackboard read/write,
    /// Config parameterization, and SkipMain override in a minimal game scenario.
    ///
    /// Scenario: "魔法对决"
    ///   法师对目标释放一个可配置的「冲击」效果——
    ///   ① OnPropose Pre: 写 BB 标记 "被提案"
    ///   ② OnApply   Pre: 从 Config 读取伤害倍率，写 BB "实际伤害"
    ///   ③ OnApply   Main: 预设行为——将 BB 实际伤害 应用到属性 HP
    ///   ④ OnApply   Post: 读 BB 实际伤害，写 BB "累计伤害"
    ///   ⑤ OnExpire  Pre: 读 BB 累计伤害，写 BB "伤害反弹值" (50% 反弹)
    /// </summary>
    [TestFixture]
    public class MudPhaseGraphDemoTests
    {
        // Attribute & BB key constants
        private const int AttrHealth = 0;
        private const int BbKeyProposed = 1;       // int: 1=已提案
        private const int BbKeyActualDamage = 2;    // float: 实际伤害值
        private const int BbKeyAccumDamage = 3;     // float: 累计伤害
        private const int BbKeyReflectValue = 4;    // float: 伤害反弹值
        private const int ConfigKeyDmgMultiplier = 100; // float: 伤害倍率
        private const int ConfigKeyBaseDamage = 101;    // float: 基础伤害

        [Test]
        public void MudPhaseGraph_ImpactSpell_FullLifecycle()
        {
            var world = World.Create();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][PHASE] 魔法对决开始。");
                sb.AppendLine("[MUD][PHASE] 法师准备释放【冲击】。");

                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templateReg = new EffectTemplateRegistry();
                var handlers = GasGraphOpHandlerTable.Instance;
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templateReg);

                // ── Register graph programs ──

                // GP1: OnPropose Pre — mark target as "proposed" via BB int
                int gpProposePre = 1;
                programs.Register(gpProposePre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardInt, A = 0, Imm = BbKeyProposed, B = 0 },
                });

                // GP2: OnApply Pre — read Config(baseDamage, dmgMultiplier), compute actual damage,
                //      write to BB(actualDamage)
                int gpApplyPre = 2;
                programs.Register(gpApplyPre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = ConfigKeyBaseDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 1, Imm = ConfigKeyDmgMultiplier },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    // Write actual damage to target's BB
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = BbKeyActualDamage, B = 2 },
                });

                // GP3: OnApply Main (preset) — read BB(actualDamage), negate it, apply to HP
                int gpApplyMain = 3;
                programs.Register(gpApplyMain, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 0, A = 0, Imm = BbKeyActualDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 1, A = 0 },
                    // ModifyAttributeAdd(target, HP, -actualDamage)
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, Imm = AttrHealth, B = 1 },
                });

                // GP4: OnApply Post — read BB(actualDamage), accumulate into BB(accumDamage)
                int gpApplyPost = 4;
                programs.Register(gpApplyPost, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 0, A = 0, Imm = BbKeyActualDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = BbKeyAccumDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = BbKeyAccumDamage, B = 2 },
                });

                // GP5: OnExpire Pre — read BB(accumDamage), compute 50% reflect, write BB(reflectValue)
                int gpExpirePre = 5;
                programs.Register(gpExpirePre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 0, A = 0, Imm = BbKeyAccumDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 0.5f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = BbKeyReflectValue, B = 2 },
                });

                // ── Register preset: None has Main graph for OnApply ──
                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(gpApplyMain);
                presetTypes.Register(in ptDef);

                // ── Build EffectPhaseGraphBindings ──
                var behavior = new EffectPhaseGraphBindings();
                behavior.TryAddStep(EffectPhaseId.OnPropose, PhaseSlot.Pre, gpProposePre);
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, gpApplyPre);
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, gpApplyPost);
                behavior.TryAddStep(EffectPhaseId.OnExpire, PhaseSlot.Pre, gpExpirePre);

                // ── Build EffectConfigParams ──
                var configParams = new EffectConfigParams();
                configParams.TryAddFloat(ConfigKeyBaseDamage, 20f);
                configParams.TryAddFloat(ConfigKeyDmgMultiplier, 1.5f); // actual = 20 * 1.5 = 30

                // ── Create entities ──
                var caster = world.Create(new AttributeBuffer());
                var target = world.Create(new AttributeBuffer(), new BlackboardFloatBuffer(), new BlackboardIntBuffer());
                world.Get<AttributeBuffer>(caster).SetCurrent(AttrHealth, 100f);
                world.Get<AttributeBuffer>(target).SetCurrent(AttrHealth, 100f);

                var api = new GasGraphRuntimeApi(world, null, null, null);

                // ═══════ Phase 1: OnPropose ═══════
                sb.AppendLine("[MUD][PHASE] ① OnPropose: 法师提案【冲击】效果。");
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnPropose, in behavior, EffectPresetType.None);

                ref var bbInt = ref world.Get<BlackboardIntBuffer>(target);
                That(bbInt.TryGet(BbKeyProposed, out int proposed), Is.True);
                That(proposed, Is.EqualTo(1), "Target should be marked as proposed");
                sb.AppendLine($"[MUD][PHASE]    BB.Proposed={proposed} ✓");

                // ═══════ Phase 2: OnApply (Pre → Main → Post) ═══════
                sb.AppendLine("[MUD][PHASE] ② OnApply: Pre读取Config计算伤害, Main施加属性修改, Post累计记录。");

                // Set config context for graph to read
                api.SetConfigContext(in configParams);
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);
                api.ClearConfigContext();

                float hpAfterApply = world.Get<AttributeBuffer>(target).GetCurrent(AttrHealth);
                ref var bbFloat = ref world.Get<BlackboardFloatBuffer>(target);
                bbFloat.TryGet(BbKeyActualDamage, out float actualDmg);
                bbFloat.TryGet(BbKeyAccumDamage, out float accumDmg);

                That(actualDmg, Is.EqualTo(30f).Within(1e-6f), "Actual damage = 20 * 1.5 = 30");
                That(hpAfterApply, Is.EqualTo(70f).Within(1e-6f), "HP = 100 - 30 = 70");
                That(accumDmg, Is.EqualTo(30f).Within(1e-6f), "Accumulated damage = 30");

                sb.AppendLine($"[MUD][PHASE]    Config(base=20, mul=1.5) → ActualDmg={actualDmg:F1}");
                sb.AppendLine($"[MUD][PHASE]    HP: 100 → {hpAfterApply:F1}");
                sb.AppendLine($"[MUD][PHASE]    BB.AccumDamage={accumDmg:F1}");

                // ═══════ Simulate second application (e.g. periodic tick reusing same behavior) ═══════
                sb.AppendLine("[MUD][PHASE] ③ OnApply (第2次): 再次施加，累计伤害叠加。");
                api.SetConfigContext(in configParams);
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);
                api.ClearConfigContext();

                float hpAfterSecond = world.Get<AttributeBuffer>(target).GetCurrent(AttrHealth);
                bbFloat = ref world.Get<BlackboardFloatBuffer>(target);
                bbFloat.TryGet(BbKeyAccumDamage, out float accumDmg2);

                That(hpAfterSecond, Is.EqualTo(40f).Within(1e-6f), "HP = 70 - 30 = 40");
                That(accumDmg2, Is.EqualTo(60f).Within(1e-6f), "Accumulated = 30 + 30 = 60");

                sb.AppendLine($"[MUD][PHASE]    HP: {hpAfterApply:F1} → {hpAfterSecond:F1}");
                sb.AppendLine($"[MUD][PHASE]    BB.AccumDamage={accumDmg2:F1}");

                // ═══════ Phase 3: OnExpire ═══════
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

        /// <summary>
        /// Scenario: SkipMain override — 用户完全覆盖预设的OnApply行为，
        /// 使用自定义公式：damage = baseDamage * 2 (而非预设的 BB-driven)。
        /// </summary>
        [Test]
        public void MudPhaseGraph_SkipMain_CustomDamageFormula()
        {
            var world = World.Create();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][SKIP] 自定义效果：完全覆盖预设，使用自定义公式。");

                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templateReg = new EffectTemplateRegistry();
                var handlers = GasGraphOpHandlerTable.Instance;
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templateReg);

                // Preset Main: write BB key=99 = 999 (should NOT run)
                int gpMainPreset = 10;
                programs.Register(gpMainPreset, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 999f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 99, B = 0 },
                });

                // User Pre: load config baseDamage, multiply by 2, apply as HP damage
                int gpCustomPre = 11;
                programs.Register(gpCustomPre, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = ConfigKeyBaseDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 3, A = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, Imm = AttrHealth, B = 3 },
                });

                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(gpMainPreset);
                presetTypes.Register(in ptDef);

                var behavior = new EffectPhaseGraphBindings();
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, gpCustomPre);
                behavior.SetSkipMain(EffectPhaseId.OnApply); // Skip the preset!

                var configParams = new EffectConfigParams();
                configParams.TryAddFloat(ConfigKeyBaseDamage, 25f); // damage = 25 * 2 = 50

                var caster = world.Create();
                var target = world.Create(new AttributeBuffer(), new BlackboardFloatBuffer());
                world.Get<AttributeBuffer>(target).SetCurrent(AttrHealth, 100f);

                var api = new GasGraphRuntimeApi(world, null, null, null);

                api.SetConfigContext(in configParams);
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);
                api.ClearConfigContext();

                float hp = world.Get<AttributeBuffer>(target).GetCurrent(AttrHealth);
                That(hp, Is.EqualTo(50f).Within(1e-6f), "HP = 100 - (25*2) = 50");

                // Main should NOT have run — BB key=99 should not exist
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

        /// <summary>
        /// Scenario: Config参数化复用 — 同一套Graph程序，不同的Config产生不同效果。
        /// 火球: baseDamage=15, multiplier=2.0 → 30 damage
        /// 冰矢: baseDamage=10, multiplier=1.0 → 10 damage
        /// </summary>
        [Test]
        public void MudPhaseGraph_ConfigReuse_DifferentParamsProduceDifferentResults()
        {
            var world = World.Create();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[MUD][CONFIG] 配置复用：同一Graph程序，不同参数。");

                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templateReg = new EffectTemplateRegistry();
                var handlers = GasGraphOpHandlerTable.Instance;
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templateReg);

                // Shared graph: read config(baseDamage) * config(multiplier), negate, apply to HP
                int gpShared = 1;
                programs.Register(gpShared, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = ConfigKeyBaseDamage },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 1, Imm = ConfigKeyDmgMultiplier },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 3, A = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, Imm = AttrHealth, B = 3 },
                });

                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(gpShared);
                presetTypes.Register(in ptDef);

                var behavior = new EffectPhaseGraphBindings(); // empty — just use Main

                // Config 1: 火球 (baseDamage=15, mul=2.0 → 30 damage)
                var fireConfig = new EffectConfigParams();
                fireConfig.TryAddFloat(ConfigKeyBaseDamage, 15f);
                fireConfig.TryAddFloat(ConfigKeyDmgMultiplier, 2.0f);

                // Config 2: 冰矢 (baseDamage=10, mul=1.0 → 10 damage)
                var iceConfig = new EffectConfigParams();
                iceConfig.TryAddFloat(ConfigKeyBaseDamage, 10f);
                iceConfig.TryAddFloat(ConfigKeyDmgMultiplier, 1.0f);

                var caster = world.Create();
                var targetA = world.Create(new AttributeBuffer());
                var targetB = world.Create(new AttributeBuffer());
                world.Get<AttributeBuffer>(targetA).SetCurrent(AttrHealth, 100f);
                world.Get<AttributeBuffer>(targetB).SetCurrent(AttrHealth, 100f);

                var api = new GasGraphRuntimeApi(world, null, null, null);

                // Fire → targetA
                api.SetConfigContext(in fireConfig);
                executor.ExecutePhase(world, api, caster, targetA, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);
                api.ClearConfigContext();

                // Ice → targetB
                api.SetConfigContext(in iceConfig);
                executor.ExecutePhase(world, api, caster, targetB, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);
                api.ClearConfigContext();

                float hpA = world.Get<AttributeBuffer>(targetA).GetCurrent(AttrHealth);
                float hpB = world.Get<AttributeBuffer>(targetB).GetCurrent(AttrHealth);

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
    }
}
