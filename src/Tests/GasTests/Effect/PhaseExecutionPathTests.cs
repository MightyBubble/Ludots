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
    [TestFixture]
    public class PhaseExecutionPathTests
    {
        // BB key constants: each graph writes to a unique key so we can verify ordering
        private const int BbKeyPre = 1;
        private const int BbKeyMain = 2;
        private const int BbKeyPost = 3;
        private const int BbKeyAccum = 10;

        private static GraphInstruction[] MakeBbWriteProgram(int bbKeyId, float value)
        {
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = value },
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = bbKeyId, B = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            };
        }

        private static GraphInstruction[] MakeBbAccumProgram(int bbKeyId, float delta)
        {
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = bbKeyId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = delta },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 3, A = 1, B = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = bbKeyId, B = 3 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            };
        }

        private void RunPreMainPostOrderTest(EffectPhaseId phase)
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = new GasGraphOpHandlerTable();

            int preId = 100;
            int mainId = 101;
            int postId = 102;

            programs.Register(preId, MakeBbWriteProgram(BbKeyAccum, 10f), GraphKind.Effect);
            programs.Register(mainId, MakeBbAccumProgram(BbKeyAccum, 20f), GraphKind.Effect);
            programs.Register(postId, MakeBbAccumProgram(BbKeyAccum, 30f), GraphKind.Effect);

            var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
            ptDef.DefaultPhaseHandlers[phase] = PhaseHandler.Graph(mainId);
            presetTypes.Register(in ptDef);

            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(phase, PhaseSlot.Pre, preId);
            behavior.TryAddStep(phase, PhaseSlot.Post, postId);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());
            var context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = target,
                TargetContext = default,
            };

            executor.ExecutePhase(world, api, context.Source, context.Target, context.TargetContext, default,
                phase, in behavior, EffectPresetType.None);

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            That(bb.TryGet(BbKeyAccum, out float result), Is.True,
                $"Phase {phase}: BB key should have been written");
            That(result, Is.EqualTo(60f).Within(1e-6f),
                $"Phase {phase}: Pre(10) â†?Main(+20=30) â†?Post(+30=60)");
        }

        private void RunPreOnlyTest(EffectPhaseId phase)
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = new GasGraphOpHandlerTable();

            int preId = 200;
            programs.Register(preId, MakeBbWriteProgram(BbKeyPre, 42f), GraphKind.Effect);

            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(phase, PhaseSlot.Pre, preId);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());
            var context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = target,
                TargetContext = default,
            };

            executor.ExecutePhase(world, api, context.Source, context.Target, context.TargetContext, default,
                phase, in behavior, EffectPresetType.None);

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            That(bb.TryGet(BbKeyPre, out float result), Is.True,
                $"Phase {phase}: Pre graph should have written to BB");
            That(result, Is.EqualTo(42f).Within(1e-6f),
                $"Phase {phase}: Pre graph wrote expected value");
        }


        [Test]
        public void OnCalculate_PreMainPost_ExecuteInOrder()
        {
            RunPreMainPostOrderTest(EffectPhaseId.OnCalculate);
        }

        [Test]
        public void OnCalculate_PreOnly_WritesToBB()
        {
            RunPreOnlyTest(EffectPhaseId.OnCalculate);
        }


        [Test]
        public void OnResolve_PreMainPost_ExecuteInOrder()
        {
            RunPreMainPostOrderTest(EffectPhaseId.OnResolve);
        }

        [Test]
        public void OnResolve_PreOnly_WritesToBB()
        {
            RunPreOnlyTest(EffectPhaseId.OnResolve);
        }


        [Test]
        public void OnHit_PreMainPost_ExecuteInOrder()
        {
            RunPreMainPostOrderTest(EffectPhaseId.OnHit);
        }

        [Test]
        public void OnHit_PreOnly_WritesToBB()
        {
            RunPreOnlyTest(EffectPhaseId.OnHit);
        }


        [Test]
        public void OnPeriod_PreMainPost_ExecuteInOrder()
        {
            RunPreMainPostOrderTest(EffectPhaseId.OnPeriod);
        }

        [Test]
        public void OnPeriod_PreOnly_WritesToBB()
        {
            RunPreOnlyTest(EffectPhaseId.OnPeriod);
        }


        [Test]
        public void OnRemove_PreMainPost_ExecuteInOrder()
        {
            RunPreMainPostOrderTest(EffectPhaseId.OnRemove);
        }

        [Test]
        public void OnRemove_PreOnly_WritesToBB()
        {
            RunPreOnlyTest(EffectPhaseId.OnRemove);
        }


        [Test]
        public void AllPhases_IndependentDispatching()
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = new GasGraphOpHandlerTable();

            var behavior = new EffectPhaseGraphBindings();
            var allPhases = new[]
            {
                EffectPhaseId.OnPropose,
                EffectPhaseId.OnCalculate,
                EffectPhaseId.OnResolve,
                EffectPhaseId.OnHit,
                EffectPhaseId.OnApply,
                EffectPhaseId.OnPeriod,
                EffectPhaseId.OnExpire,
                EffectPhaseId.OnRemove,
            };

            for (int i = 0; i < allPhases.Length; i++)
            {
                int graphId = 300 + i;
                int bbKey = 50 + i;
                GraphKind kind = allPhases[i] == EffectPhaseId.OnPropose
                    ? GraphKind.Validation
                    : GraphKind.Effect;
                GraphInstruction[] program;
                if (allPhases[i] == EffectPhaseId.OnPropose)
                {
                    program =
                    [
                        new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
                        new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                    ];
                }
                else
                {
                    program = MakeBbWriteProgram(bbKey, (float)(i + 1));
                }
                programs.Register(graphId, program, kind);
                behavior.TryAddStep(allPhases[i], PhaseSlot.Pre, graphId);
            }

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());
            var context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = target,
                TargetContext = default,
            };

            for (int i = 0; i < allPhases.Length; i++)
            {
                if (allPhases[i] == EffectPhaseId.OnPropose)
                {
                    That(executor.ExecutePhaseWithValidationResult(
                        world, api, caster, target, default, default,
                        allPhases[i], in behavior, EffectPresetType.None,
                        effectCategoryId: 0, effectTemplateId: 0, mergedParams: default), Is.True);
                    continue;
                }

                executor.ExecutePhase(
                    world, api, context.Source, context.Target, context.TargetContext, default,
                    allPhases[i], in behavior, EffectPresetType.None);
            }

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            for (int i = 0; i < allPhases.Length; i++)
            {
                if (allPhases[i] == EffectPhaseId.OnPropose)
                    continue;

                int bbKey = 50 + i;
                That(bb.TryGet(bbKey, out float val), Is.True,
                    $"Phase {allPhases[i]}: BB key {bbKey} should exist");
                That(val, Is.EqualTo((float)(i + 1)).Within(1e-6f),
                    $"Phase {allPhases[i]}: BB value should be {i + 1}");
            }
        }


        [Test]
        public void OnCalculate_SkipMain_OnlyPrePostRun()
        {
            RunSkipMainTest(EffectPhaseId.OnCalculate);
        }

        [Test]
        public void OnResolve_SkipMain_OnlyPrePostRun()
        {
            RunSkipMainTest(EffectPhaseId.OnResolve);
        }

        [Test]
        public void OnHit_SkipMain_OnlyPrePostRun()
        {
            RunSkipMainTest(EffectPhaseId.OnHit);
        }

        [Test]
        public void OnPeriod_SkipMain_OnlyPrePostRun()
        {
            RunSkipMainTest(EffectPhaseId.OnPeriod);
        }

        [Test]
        public void OnRemove_SkipMain_OnlyPrePostRun()
        {
            RunSkipMainTest(EffectPhaseId.OnRemove);
        }

        private void RunSkipMainTest(EffectPhaseId phase)
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = new GasGraphOpHandlerTable();

            int preId = 400;
            int mainId = 401;
            int postId = 402;

            programs.Register(preId, MakeBbWriteProgram(BbKeyAccum, 10f), GraphKind.Effect);
            programs.Register(mainId, MakeBbWriteProgram(BbKeyAccum, 999f), GraphKind.Effect);
            programs.Register(postId, MakeBbAccumProgram(BbKeyAccum, 30f), GraphKind.Effect);

            var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
            ptDef.DefaultPhaseHandlers[phase] = PhaseHandler.Graph(mainId);
            presetTypes.Register(in ptDef);

            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(phase, PhaseSlot.Pre, preId);
            behavior.TryAddStep(phase, PhaseSlot.Post, postId);
            behavior.SetSkipMain(phase);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());
            var context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = target,
                TargetContext = default,
            };

            executor.ExecutePhase(world, api, context.Source, context.Target, context.TargetContext, default,
                phase, in behavior, EffectPresetType.None);

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            That(bb.TryGet(BbKeyAccum, out float result), Is.True,
                $"Phase {phase}: BB key should exist after SkipMain execution");
            That(result, Is.EqualTo(40f).Within(1e-6f),
                $"Phase {phase}: Pre(10) â†?Main(SKIPPED) â†?Post(+30=40), not 999");
        }
    }
}
