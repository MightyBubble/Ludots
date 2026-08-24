using System;
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
    /// Unit tests for the Phase Graph architecture components:
    ///   EffectPhaseGraphBindings, EffectConfigParams,
    ///   EffectPhaseExecutor, new Math/BB/Config Graph Ops.
    /// </summary>
    [TestFixture]
    public class EffectPhaseArchitectureTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  EffectPhaseGraphBindings
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void PhaseGraphBindings_TryAddStep_AndGetGraphId_Roundtrips()
        {
            var bt = new EffectPhaseGraphBindings();
            That(bt.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, 101), Is.True);
            That(bt.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, 102), Is.True);
            That(bt.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Pre, 201), Is.True);
            That(bt.StepCount, Is.EqualTo(3));

            That(bt.GetGraphId(EffectPhaseId.OnApply, PhaseSlot.Pre), Is.EqualTo(101));
            That(bt.GetGraphId(EffectPhaseId.OnApply, PhaseSlot.Post), Is.EqualTo(102));
            That(bt.GetGraphId(EffectPhaseId.OnPeriod, PhaseSlot.Pre), Is.EqualTo(201));
        }

        [Test]
        public void PhaseGraphBindings_GetGraphId_ReturnsZero_WhenNotConfigured()
        {
            var bt = new EffectPhaseGraphBindings();
            That(bt.GetGraphId(EffectPhaseId.OnHit, PhaseSlot.Pre), Is.EqualTo(0));
            That(bt.GetGraphId(EffectPhaseId.OnHit, PhaseSlot.Post), Is.EqualTo(0));
        }

        [Test]
        public void PhaseGraphBindings_TryAddStep_ReturnsFalse_WhenCapacityExceeded()
        {
            var bt = new EffectPhaseGraphBindings();
            for (int i = 0; i < EffectPhaseGraphBindings.MAX_STEPS; i++)
            {
                That(bt.TryAddStep((EffectPhaseId)(i % 8), PhaseSlot.Pre, i + 1), Is.True);
            }
            That(bt.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, 999), Is.False);
            That(bt.StepCount, Is.EqualTo(EffectPhaseGraphBindings.MAX_STEPS));
        }

        [Test]
        public void PhaseGraphBindings_SkipMain_SetAndCheck()
        {
            var bt = new EffectPhaseGraphBindings();
            That(bt.IsSkipMain(EffectPhaseId.OnApply), Is.False);
            That(bt.IsSkipMain(EffectPhaseId.OnHit), Is.False);

            bt.SetSkipMain(EffectPhaseId.OnApply);
            That(bt.IsSkipMain(EffectPhaseId.OnApply), Is.True);
            That(bt.IsSkipMain(EffectPhaseId.OnHit), Is.False);

            bt.SetSkipMain(EffectPhaseId.OnHit);
            That(bt.IsSkipMain(EffectPhaseId.OnApply), Is.True);
            That(bt.IsSkipMain(EffectPhaseId.OnHit), Is.True);
        }

        [Test]
        public void PhaseGraphBindings_SkipMain_AllPhases()
        {
            var bt = new EffectPhaseGraphBindings();
            for (int i = 0; i < EffectPhaseConstants.PhaseCount; i++)
            {
                bt.SetSkipMain((EffectPhaseId)i);
            }
            for (int i = 0; i < EffectPhaseConstants.PhaseCount; i++)
            {
                That(bt.IsSkipMain((EffectPhaseId)i), Is.True, $"Phase {(EffectPhaseId)i} should be SkipMain");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  EffectConfigParams
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void ConfigParams_Float_AddAndGet_Roundtrips()
        {
            var cp = new EffectConfigParams();
            That(cp.TryAddFloat(keyId: 42, 3.14f), Is.True);
            That(cp.TryAddFloat(keyId: 99, -1.5f), Is.True);
            That(cp.Count, Is.EqualTo(2));

            That(cp.TryGetFloat(42, out float v1), Is.True);
            That(v1, Is.EqualTo(3.14f).Within(1e-6f));

            That(cp.TryGetFloat(99, out float v2), Is.True);
            That(v2, Is.EqualTo(-1.5f).Within(1e-6f));
        }

        [Test]
        public unsafe void ConfigParams_Int_AddAndGet_Roundtrips()
        {
            var cp = new EffectConfigParams();
            That(cp.TryAddInt(keyId: 10, 777), Is.True);
            That(cp.TryGetInt(10, out int v), Is.True);
            That(v, Is.EqualTo(777));
        }

        [Test]
        public unsafe void ConfigParams_EffectTemplateId_AddAndGet_Roundtrips()
        {
            var cp = new EffectConfigParams();
            That(cp.TryAddEffectTemplateId(keyId: 50, templateId: 3001), Is.True);
            // EffectTemplateId is stored as int �?retrieved via TryGetInt
            That(cp.TryGetInt(50, out int v), Is.True);
            That(v, Is.EqualTo(3001));
        }

        [Test]
        public void ConfigParams_TryGetFloat_ReturnsFalse_WhenKeyMissing()
        {
            var cp = new EffectConfigParams();
            That(cp.TryGetFloat(999, out _), Is.False);
        }

        [Test]
        public void ConfigParams_TryAddFloat_ReturnsFalse_WhenCapacityExceeded()
        {
            var cp = new EffectConfigParams();
            for (int i = 0; i < EffectConfigParams.MAX_PARAMS; i++)
            {
                That(cp.TryAddFloat(i + 1, i * 0.1f), Is.True);
            }
            That(cp.TryAddFloat(999, 0f), Is.False);
            That(cp.Count, Is.EqualTo(EffectConfigParams.MAX_PARAMS));
        }

        // ════════════════════════════════════════════════════════════════════
        //  EffectPhaseExecutor �?Pre/Main/Post ordering
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that EffectPhaseExecutor calls Pre �?Main �?Post in the correct order
        /// by building 3 trivial Graph programs that each write a different float register
        /// to a sequential counter value via ConstFloat.
        /// </summary>
        [Test]
        public unsafe void PhaseExecutor_ExecutesPreMainPost_InOrder()
        {
            var world = World.Create();
            try
            {
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templates = new EffectTemplateRegistry();
                var handlers = new GasGraphOpHandlerTable();

                // Graph programs that write BB floats to prove Pre → Main → Post ordering.
                int preGraphId = 1;
                int mainGraphId = 2;
                int postGraphId = 3;

                // Preset: Main graph for OnApply
                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(mainGraphId);
                presetTypes.Register(in ptDef);

                // Behavior: Pre and Post graphs for OnApply
                var behavior = new EffectPhaseGraphBindings();
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, preGraphId);
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, postGraphId);

                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
                var api = new GasGraphRuntimeApi(world, null, null, null);
                var caster = world.Create();
                var target = world.Create();
                world.Add(target, new BlackboardFloatBuffer());

                // Pre graph: F[0]=10.0, E[0]=target, BB.write(E[0], key=1, F[0])
                programs.Register(preGraphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 10f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                }, GraphKind.Effect);
                // Main graph: read BB key=1 into F[1], add 20, write back to BB key=1
                programs.Register(mainGraphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 20f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 3, A = 1, B = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                }, GraphKind.Effect);
                // Post graph: read BB key=1 into F[1], add 30, write back to BB key=1
                programs.Register(postGraphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 30f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 3, A = 1, B = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                }, GraphKind.Effect);

                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);

                // Result: Pre writes 10, Main reads 10+20=30, Post reads 30+30=60
                ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
                That(bb.TryGet(1, out float finalVal), Is.True);
                That(finalVal, Is.EqualTo(60f).Within(1e-6f), "Pre(10) �?Main(+20=30) �?Post(+30=60)");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public unsafe void PhaseExecutor_SkipMain_SkipsPresetGraph()
        {
            var world = World.Create();
            try
            {
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templates = new EffectTemplateRegistry();
                var handlers = new GasGraphOpHandlerTable();

                var target = world.Create(new BlackboardFloatBuffer());
                var caster = world.Create();

                int preGraphId = 10;
                int mainGraphId = 11;
                int postGraphId = 12;

                // Pre: write BB key=1 = 5.0
                programs.Register(preGraphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 5f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                }, GraphKind.Effect);
                // Main: would write BB key=1 = 999.0 (should NOT run)
                programs.Register(mainGraphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 999f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                }, GraphKind.Effect);
                // Post: read BB key=1 into F[1], add 100, write back
                programs.Register(postGraphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 100f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 3, A = 1, B = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                }, GraphKind.Effect);

                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                ptDef.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(mainGraphId);
                presetTypes.Register(in ptDef);

                var behavior = new EffectPhaseGraphBindings();
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, preGraphId);
                behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, postGraphId);
                behavior.SetSkipMain(EffectPhaseId.OnApply);

                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
                var api = new GasGraphRuntimeApi(world, null, null, null);

                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);

                // Pre writes 5, Main skipped, Post reads 5+100=105
                ref var bb = ref world.Get<BlackboardFloatBuffer>(target);
                That(bb.TryGet(1, out float val), Is.True);
                That(val, Is.EqualTo(105f).Within(1e-6f), "Pre(5) �?Main(skipped) �?Post(+100=105)");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void PhaseExecutor_NoGraphs_DoesNotThrow()
        {
            var world = World.Create();
            try
            {
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templates = new EffectTemplateRegistry();
                var handlers = new GasGraphOpHandlerTable();
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var caster = world.Create();
                var target = world.Create();
                var behavior = new EffectPhaseGraphBindings(); // empty

                // Should not throw
                executor.ExecutePhase(world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None);

                Pass("No graphs = no-op, no throw");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void PhaseExecutor_ConfiguredMissingGraph_ThrowsWithGraphId()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, new GasGraphOpHandlerTable(), templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, 404);

            var ex = Throws<InvalidOperationException>(() => executor.ExecutePhase(
                world,
                api,
                world.Create(),
                world.Create(),
                default,
                default,
                EffectPhaseId.OnApply,
                in behavior,
                EffectPresetType.None));

            That(ex!.Message, Does.Contain("graphId=404"));
        }

        [Test]
        public void PhaseExecutor_OwnsMergedConfigScopeAndClearsItAfterSuccess()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            var templates = new EffectTemplateRegistry();
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                templates);
            var api = new GasGraphRuntimeApi(world);
            Entity caster = world.Create();
            Entity target = world.Create(new BlackboardIntBuffer());
            const int graphId = 405;
            const int configKey = 71;
            const int blackboardKey = 72;
            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigInt, Dst = 0, Imm = configKey },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardInt, A = 1, B = 0, Imm = blackboardKey },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, GraphKind.Effect);
            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, graphId);
            var mergedConfig = new EffectConfigParams();
            mergedConfig.TryAddInt(configKey, 42);

            executor.ExecutePhase(
                world,
                api,
                caster,
                target,
                default,
                default,
                EffectPhaseId.OnApply,
                in behavior,
                EffectPresetType.None,
                0,
                0,
                in mergedConfig);

            Assert.That(world.Get<BlackboardIntBuffer>(target).TryGet(blackboardKey, out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(api.TryLoadConfigInt(configKey, out _), Is.False);
        }

        [Test]
        public void PhaseExecutor_ClearsMergedConfigScopeWhenGraphThrows()
        {
            using var world = World.Create();
            var executor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            var api = new GasGraphRuntimeApi(world);
            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, 406);
            var mergedConfig = new EffectConfigParams();
            mergedConfig.TryAddInt(73, 99);

            Assert.Throws<InvalidOperationException>(() => executor.ExecutePhase(
                world,
                api,
                world.Create(),
                world.Create(),
                default,
                default,
                EffectPhaseId.OnApply,
                in behavior,
                EffectPresetType.None,
                0,
                0,
                in mergedConfig));

            Assert.That(api.TryLoadConfigInt(73, out _), Is.False);
        }

        [Test]
        public void PhaseExecutor_ValidationResult_DefaultsToRejectWhenGraphDoesNotWriteB0()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, new GasGraphOpHandlerTable(), templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create();
            const int graphId = 501;
            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 7f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            }, GraphKind.Validation);

            var behavior = new EffectPhaseGraphBindings();
            behavior.TryAddStep(EffectPhaseId.OnPropose, PhaseSlot.Pre, graphId);

            bool accepted = executor.ExecutePhaseWithValidationResult(
                world,
                api,
                caster,
                target,
                target,
                default,
                EffectPhaseId.OnPropose,
                in behavior,
                EffectPresetType.None,
                0,
                0,
                default);

            That(accepted, Is.False, "OnPropose validation must fail closed unless graph explicitly writes B[0]=1.");
        }

        [Test]
        public void PhaseExecutor_ValidationResult_VacantPhasePasses()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var caster = world.Create();
            var target = world.Create();
            var behavior = new EffectPhaseGraphBindings();

            bool accepted = executor.ExecutePhaseWithValidationResult(
                world,
                api,
                caster,
                target,
                target,
                default,
                EffectPhaseId.OnPropose,
                in behavior,
                EffectPresetType.None,
                0,
                0,
                default);

            That(accepted, Is.True, "Vacant OnPropose phases have no validating graph work and must pass.");
        }

        [Test]
        public void AbilityActivationPrecondition_MissingGraph_ThrowsWithGraphId()
        {
            using var world = World.Create();
            var precondition = new AbilityActivationPrecondition { ValidationGraphId = 405 };
            var programs = new GraphProgramRegistry();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var ex = Throws<InvalidOperationException>(() => AbilityActivationPreconditionEvaluator.Evaluate(
                world,
                world.Create(),
                world.Create(),
                default,
                abilityId: 9001,
                in precondition,
                programs,
                api));

            That(ex!.Message, Does.Contain("graphId=405"));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Math Graph Ops
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_SubFloat_Correct()
        {
            RunSingleMathOp(
                GraphNodeOp.SubFloat,
                fA: 10f, fB: 3f,
                expected: 7f);
        }

        [Test]
        public void GraphOps_DivFloat_Correct()
        {
            RunSingleMathOp(
                GraphNodeOp.DivFloat,
                fA: 10f, fB: 4f,
                expected: 2.5f);
        }

        [Test]
        public void GraphOps_DivFloat_ByZero_ReturnsZero()
        {
            RunSingleMathOp(
                GraphNodeOp.DivFloat,
                fA: 10f, fB: 0f,
                expected: 0f);
        }

        [Test]
        public void GraphOps_MinFloat_Correct()
        {
            RunSingleMathOp(
                GraphNodeOp.MinFloat,
                fA: 5f, fB: 3f,
                expected: 3f);
        }

        [Test]
        public void GraphOps_MaxFloat_Correct()
        {
            RunSingleMathOp(
                GraphNodeOp.MaxFloat,
                fA: 5f, fB: 8f,
                expected: 8f);
        }

        [Test]
        public void GraphOps_AbsFloat_Negative_ReturnsPositive()
        {
            RunSingleUnaryOp(GraphNodeOp.AbsFloat, -7.5f, 7.5f);
        }

        [Test]
        public void GraphOps_NegFloat_FlipsSign()
        {
            RunSingleUnaryOp(GraphNodeOp.NegFloat, 3f, -3f);
        }

        [Test]
        public void GraphOps_ClampFloat_WithinRange_Unchanged()
        {
            RunClampOp(value: 5f, min: 0f, max: 10f, expected: 5f);
        }

        [Test]
        public void GraphOps_ClampFloat_BelowMin_ClampsToMin()
        {
            RunClampOp(value: -3f, min: 0f, max: 10f, expected: 0f);
        }

        [Test]
        public void GraphOps_ClampFloat_AboveMax_ClampsToMax()
        {
            RunClampOp(value: 15f, min: 0f, max: 10f, expected: 10f);
        }

        // ════════════════════════════════════════════════════════════════════
        //  Blackboard Graph Ops
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_BlackboardFloat_WriteAndRead_Roundtrips()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create(new BlackboardFloatBuffer());
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // Program: F[0]=42.5, E[0]=entity, WriteBlackboardFloat(E[0], key=7, F[0]),
                //          ReadBlackboardFloat(Dst=F[1], E[0], key=7)
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 42.5f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 7, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = 7 },
                };

                var result = ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 1);
                That(result, Is.EqualTo(42.5f).Within(1e-6f));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_BlackboardInt_WriteAndRead_Roundtrips()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create(new BlackboardIntBuffer());
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // I[0]=12345, E[0]=entity, WriteBBInt(E[0], key=3, I[0]), ReadBBInt(Dst=I[1], E[0], key=3)
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 12345 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardInt, A = 0, Imm = 3, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardInt, Dst = 1, A = 0, Imm = 3 },
                };

                var result = ExecuteAndGetInt(world, api, entity, entity, program, intReg: 1);
                That(result, Is.EqualTo(12345));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_BlackboardEntity_WriteAndRead_Roundtrips()
        {
            var world = World.Create();
            try
            {
                var owner = world.Create(new BlackboardEntityBuffer());
                var stored = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // E[0]=caster(owner), E[1]=target(stored), WriteBBEntity(E[0], key=5, E[1]),
                // ReadBBEntity(Dst=E[2], E[0], key=5)
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardEntity, A = 0, Imm = 5, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardEntity, Dst = 2, A = 0, Imm = 5 },
                };

                var result = ExecuteAndGetEntity(world, api, owner, stored, program, entityReg: 2);
                That(result, Is.EqualTo(stored));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_BlackboardRead_MissingComponent_FailsFast()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create(); // NO BlackboardFloatBuffer
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = 99 },
                };

                var ex = Throws<InvalidOperationException>(() =>
                    ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 1));
                That(ex!.Message, Does.Contain("GAS.GRAPH.ERR.MissingBlackboard"));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_BlackboardWrite_RequiresPreAddedComponent()
        {
            var world = World.Create();
            try
            {
                // Entity WITHOUT BB component must fail without archetype migration in the hot path.
                var entityNoBB = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 77f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 },
                };

                var error = Throws<InvalidOperationException>(() =>
                    ExecuteProgram(world, api, entityNoBB, entityNoBB, program));

                // BB component should NOT be auto-added (archetype migration removed for hot-path safety)
                That(error!.Message, Does.StartWith(GasGraphRuntimeApi.MissingBlackboardError));
                That(world.Has<BlackboardFloatBuffer>(entityNoBB), Is.False,
                    "WriteBlackboardFloat must not auto-add BB component");

                // Entity WITH pre-added BB component �?write should succeed
                var entityWithBB = world.Create(new BlackboardFloatBuffer());
                ExecuteProgram(world, api, entityWithBB, entityWithBB, program);

                That(world.Has<BlackboardFloatBuffer>(entityWithBB), Is.True);
                ref var bb = ref world.Get<BlackboardFloatBuffer>(entityWithBB);
                That(bb.TryGet(1, out float v), Is.True);
                That(v, Is.EqualTo(77f).Within(1e-6f));
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Config Graph Ops
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_LoadConfigFloat_ReadsFromConfigContext()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var cp = new EffectConfigParams();
                cp.TryAddFloat(keyId: 100, 2.718f);
                api.SetConfigContext(in cp);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = 100 },
                };

                var result = ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 0);
                That(result, Is.EqualTo(2.718f).Within(1e-6f));

                api.ClearConfigContext();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_LoadConfigInt_ReadsFromConfigContext()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var cp = new EffectConfigParams();
                cp.TryAddInt(keyId: 200, 42);
                api.SetConfigContext(in cp);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigInt, Dst = 0, Imm = 200 },
                };

                var result = ExecuteAndGetInt(world, api, entity, entity, program, intReg: 0);
                That(result, Is.EqualTo(42));

                api.ClearConfigContext();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_LoadConfigEffectId_ReadsFromConfigContext()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                var cp = new EffectConfigParams();
                cp.TryAddEffectTemplateId(keyId: 300, templateId: 5001);
                api.SetConfigContext(in cp);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigEffectId, Dst = 0, Imm = 300 },
                };

                var result = ExecuteAndGetInt(world, api, entity, entity, program, intReg: 0);
                That(result, Is.EqualTo(5001));

                api.ClearConfigContext();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GraphOps_LoadConfigFloat_NoContext_ReturnsZero()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);
                // no SetConfigContext call

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = 100 },
                };

                var result = ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 0);
                That(result, Is.EqualTo(0f));
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        private static void RunSingleMathOp(GraphNodeOp op, float fA, float fB, float expected)
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // F[0] = fA, F[1] = fB, F[2] = op(F[0], F[1])
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = fA },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = fB },
                    new GraphInstruction { Op = (ushort)op, Dst = 2, A = 0, B = 1 },
                };

                var result = ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 2);
                That(result, Is.EqualTo(expected).Within(1e-6f));
            }
            finally
            {
                world.Dispose();
            }
        }

        private static void RunSingleUnaryOp(GraphNodeOp op, float fA, float expected)
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // F[0] = fA, F[1] = op(F[0])
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = fA },
                    new GraphInstruction { Op = (ushort)op, Dst = 1, A = 0 },
                };

                var result = ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 1);
                That(result, Is.EqualTo(expected).Within(1e-6f));
            }
            finally
            {
                world.Dispose();
            }
        }

        private static void RunClampOp(float value, float min, float max, float expected)
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // F[0]=value, F[1]=min, F[2]=max, F[3]=clamp(F[0], F[1], F[2])
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = value },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = min },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = max },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ClampFloat, Dst = 3, A = 0, B = 1, C = 2 },
                };

                var result = ExecuteAndGetFloat(world, api, entity, entity, program, floatReg: 3);
                That(result, Is.EqualTo(expected).Within(1e-6f));
            }
            finally
            {
                world.Dispose();
            }
        }

        /// <summary>
        /// Execute a graph program and return the value in a float register.
        /// </summary>
        private static float ExecuteAndGetFloat(World world, IGraphRuntimeApi api, Entity caster, Entity target,
            GraphInstruction[] program, int floatReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];

            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, WithHalt(program), GasGraphOpHandlerTable.Instance);
            return f[floatReg];
        }

        private static int ExecuteAndGetInt(World world, IGraphRuntimeApi api, Entity caster, Entity target,
            GraphInstruction[] program, int intReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var iArr = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];

            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = f,
                I = iArr,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, WithHalt(program), GasGraphOpHandlerTable.Instance);
            return iArr[intReg];
        }

        private static Entity ExecuteAndGetEntity(World world, IGraphRuntimeApi api, Entity caster, Entity target,
            GraphInstruction[] program, int entityReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];

            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, WithHalt(program), GasGraphOpHandlerTable.Instance);
            return e[entityReg];
        }

        private static void ExecuteProgram(World world, IGraphRuntimeApi api, Entity caster, Entity target,
            GraphInstruction[] program)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];

            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, WithHalt(program), GasGraphOpHandlerTable.Instance);
        }

        private static GraphInstruction[] WithHalt(GraphInstruction[] program)
        {
            if (program.Length > 0 && program[^1].Op == (ushort)GraphNodeOp.HaltReturnInt)
            {
                return program;
            }

            var copy = new GraphInstruction[program.Length + 1];
            program.CopyTo(copy, 0);
            copy[^1] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt };
            return copy;
        }
    }
}
