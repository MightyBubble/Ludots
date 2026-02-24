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
    /// Graph execution path tests for all 8 EffectPhaseId values.
    /// Ensures that EffectPhaseExecutor correctly dispatches Pre/Main/Post
    /// graphs for every lifecycle phase. Covers the 5 previously untested
    /// phases: OnCalculate, OnResolve, OnHit, OnPeriod, OnRemove.
    ///
    /// Each test uses BlackboardFloatBuffer writes to verify execution occurred
    /// and Pre→Main→Post ordering is respected.
    /// </summary>
    [TestFixture]
    public class PhaseExecutionPathTests
    {
        // BB key constants — each graph writes to a unique key so we can verify ordering
        private const int BbKeyPre = 1;
        private const int BbKeyMain = 2;
        private const int BbKeyPost = 3;
        private const int BbKeyAccum = 10; // accumulator for ordering verification

        /// <summary>
        /// Helper: create a graph program that writes a float value to a BB key on the target entity.
        /// Instructions: F[0] = value, E[0] = LoadExplicitTarget, WriteBlackboardFloat(E[0], keyId, F[0])
        /// </summary>
        private static GraphInstruction[] MakeBbWriteProgram(int bbKeyId, float value)
        {
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = value },
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = bbKeyId, B = 0 },
            };
        }

        /// <summary>
        /// Helper: create a graph program that reads BB key, adds delta, writes back.
        /// Used to verify ordering (Pre writes initial, Main adds, Post adds).
        /// Instructions: E[0]=Target, F[1]=ReadBB(key), F[2]=delta, F[3]=F[1]+F[2], WriteBB(key, F[3])
        /// </summary>
        private static GraphInstruction[] MakeBbAccumProgram(int bbKeyId, float delta)
        {
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = bbKeyId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = delta },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 3, A = 1, B = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = bbKeyId, B = 3 },
            };
        }

        /// <summary>
        /// Runs Pre→Main→Post for the given phase and verifies the accumulated BB value.
        /// Pre writes 10, Main adds 20 (=30), Post adds 30 (=60).
        /// </summary>
        private void RunPreMainPostOrderTest(EffectPhaseId phase)
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            int preId = 100;
            int mainId = 101;
            int postId = 102;

            // Pre: write BB[Accum] = 10
            programs.Register(preId, MakeBbWriteProgram(BbKeyAccum, 10f));
            // Main: BB[Accum] += 20
            programs.Register(mainId, MakeBbAccumProgram(BbKeyAccum, 20f));
            // Post: BB[Accum] += 30
            programs.Register(postId, MakeBbAccumProgram(BbKeyAccum, 30f));

            // Register preset with Main graph for target phase
            var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
            ptDef.DefaultPhaseHandlers[phase] = PhaseHandler.Graph(mainId);
            presetTypes.Register(in ptDef);

            // Behavior with Pre and Post
            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(phase, PhaseSlot.Pre, preId);
            behavior.TryAddStep(phase, PhaseSlot.Post, postId);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());

            executor.ExecutePhase(world, api, caster, target, default, default,
                phase, in behavior, EffectPresetType.None);

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            That(bb.TryGet(BbKeyAccum, out float result), Is.True,
                $"Phase {phase}: BB key should have been written");
            That(result, Is.EqualTo(60f).Within(1e-6f),
                $"Phase {phase}: Pre(10) → Main(+20=30) → Post(+30=60)");
        }

        /// <summary>
        /// Runs Pre-only graph for the given phase and verifies BB write.
        /// </summary>
        private void RunPreOnlyTest(EffectPhaseId phase)
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            int preId = 200;
            programs.Register(preId, MakeBbWriteProgram(BbKeyPre, 42f));

            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(phase, PhaseSlot.Pre, preId);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());

            executor.ExecutePhase(world, api, caster, target, default, default,
                phase, in behavior, EffectPresetType.None);

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            That(bb.TryGet(BbKeyPre, out float result), Is.True,
                $"Phase {phase}: Pre graph should have written to BB");
            That(result, Is.EqualTo(42f).Within(1e-6f),
                $"Phase {phase}: Pre graph wrote expected value");
        }

        // ════════════════════════════════════════════════════════════════════
        //  OnCalculate (Phase 1) — compute final Modifier values
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        //  OnResolve (Phase 2) — target resolution
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        //  OnHit (Phase 3) — per-target hit validation
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        //  OnPeriod (Phase 5) — periodic tick
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        //  OnRemove (Phase 7) — forced removal
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        //  Cross-phase verification — ensure each phase dispatches independently
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AllPhases_IndependentDispatching()
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            // Register a unique graph per phase that writes the phase ordinal as BB float
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
                int bbKey = 50 + i; // unique BB key per phase
                programs.Register(graphId, MakeBbWriteProgram(bbKey, (float)(i + 1)));
                behavior.TryAddStep(allPhases[i], PhaseSlot.Pre, graphId);
            }

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create(new BlackboardFloatBuffer());

            // Execute each phase independently
            for (int i = 0; i < allPhases.Length; i++)
            {
                executor.ExecutePhase(world, api, caster, target, default, default,
                    allPhases[i], in behavior, EffectPresetType.None);
            }

            // Verify each phase wrote its unique value
            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            for (int i = 0; i < allPhases.Length; i++)
            {
                int bbKey = 50 + i;
                That(bb.TryGet(bbKey, out float val), Is.True,
                    $"Phase {allPhases[i]}: BB key {bbKey} should exist");
                That(val, Is.EqualTo((float)(i + 1)).Within(1e-6f),
                    $"Phase {allPhases[i]}: BB value should be {i + 1}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  SkipMain — verify SkipMain works across all phases
        // ════════════════════════════════════════════════════════════════════

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

        /// <summary>
        /// Verifies that SkipMain prevents the Main handler from executing.
        /// Pre writes 10, Main would write 999 (should be skipped), Post adds 30 → expect 40.
        /// </summary>
        private void RunSkipMainTest(EffectPhaseId phase)
        {
            using var world = World.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var handlers = GasGraphOpHandlerTable.Instance;

            int preId = 400;
            int mainId = 401;
            int postId = 402;

            // Pre: BB[Accum] = 10
            programs.Register(preId, MakeBbWriteProgram(BbKeyAccum, 10f));
            // Main: would overwrite BB[Accum] = 999 (should be SKIPPED)
            programs.Register(mainId, MakeBbWriteProgram(BbKeyAccum, 999f));
            // Post: BB[Accum] += 30
            programs.Register(postId, MakeBbAccumProgram(BbKeyAccum, 30f));

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

            executor.ExecutePhase(world, api, caster, target, default, default,
                phase, in behavior, EffectPresetType.None);

            ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
            That(bb.TryGet(BbKeyAccum, out float result), Is.True,
                $"Phase {phase}: BB key should exist after SkipMain execution");
            That(result, Is.EqualTo(40f).Within(1e-6f),
                $"Phase {phase}: Pre(10) → Main(SKIPPED) → Post(+30=40), not 999");
        }
    }
}
