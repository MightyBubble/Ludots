using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace GasTests.Effect;

[TestFixture]
public sealed class EffectExecutionPlanTests
{
    [Test]
    public void Finalize_TemplateMainOverride_ClassifiesActualMainInsteadOfUnsupportedPresetDefault()
    {
        const int templateId = 101;
        const int templateMainGraphId = 201;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(EffectPresetType.Relation, PhaseHandler.Builtin(BuiltinHandlerId.ApplyRelation));
        var programs = new GraphProgramRegistry();
        programs.Register(templateMainGraphId, GasWriteProgram(), GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, templateMainGraphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Relation,
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        Finalize(templates, presets, programs);

        ref readonly EffectExecutionPlanSet plans = ref templates.RequireExecutionPlans(templateId);
        Assert.That(plans.Activation.Kind, Is.EqualTo(EffectExecutionPlanKind.GasTransactional));
    }

    [Test]
    public void Finalize_SkipMain_DoesNotClassifyUnsupportedPresetDefault()
    {
        const int templateId = 102;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(EffectPresetType.Relation, PhaseHandler.Builtin(BuiltinHandlerId.ApplyRelation));
        EffectPhaseGraphBindings bindings = default;
        bindings.SetSkipMain(EffectPhaseId.OnApply);
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Relation,
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        Finalize(templates, presets, new GraphProgramRegistry());

        Assert.That(
            templates.RequireExecutionPlans(templateId).Activation.Kind,
            Is.EqualTo(EffectExecutionPlanKind.GasTransactional));
    }

    [Test]
    public void Finalize_SingleTerminalDisplacement_CertifiesExternalAtomicActivation()
    {
        const int templateId = 103;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(EffectPresetType.Displacement, PhaseHandler.Builtin(BuiltinHandlerId.ApplyDisplacement));
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Displacement,
            LifetimeKind = EffectLifetimeKind.Instant,
        });

        Finalize(templates, presets, new GraphProgramRegistry());

        EffectWindowExecutionPlan plan = templates.RequireExecutionPlans(templateId).Activation;
        Assert.That(plan.Kind, Is.EqualTo(EffectExecutionPlanKind.ExternalAtomicExclusive));
        Assert.That(plan.Domain, Is.EqualTo(EffectAtomicDomain.Displacement));
        Assert.That(plan.ExternalPhase, Is.EqualTo(EffectPhaseId.OnApply));
        Assert.That(plan.RequiresListenerPreflight, Is.True);
    }

    [Test]
    public void Finalize_ExternalCombinedWithGasWrite_FailsAtLoadTime()
    {
        const int templateId = 104;
        const int preGraphId = 204;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(EffectPresetType.Displacement, PhaseHandler.Builtin(BuiltinHandlerId.ApplyDisplacement));
        var programs = new GraphProgramRegistry();
        programs.Register(preGraphId, GasWriteProgram(), GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, preGraphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Displacement,
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, presets, programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.InvalidCompositionError));
        Assert.That(error.Message, Does.Contain("effect='104'"));
        Assert.That(error.Message, Does.Contain("phase='OnApply'"));
        Assert.That(error.Message, Does.Contain(nameof(BuiltinHandlerId.ApplyDisplacement)));
    }

    [Test]
    public void Finalize_TwoExternalOperations_FailsAtLoadTime()
    {
        const int templateId = 105;
        const int preGraphId = 205;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(EffectPresetType.CompleteProgression, PhaseHandler.Builtin(BuiltinHandlerId.CompleteProgression));
        var programs = new GraphProgramRegistry();
        programs.Register(preGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeBuiltin, Imm = (int)BuiltinHandlerId.ApplyDisplacement },
        ], GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, preGraphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.CompleteProgression,
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, presets, programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.InvalidCompositionError));
        Assert.That(error.Message, Does.Contain("exactly one external operation"));
    }

    [Test]
    public void Finalize_OperationAfterExternal_FailsAtLoadTime()
    {
        const int templateId = 106;
        const int preGraphId = 206;
        const int postGraphId = 207;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(preGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeBuiltin, Imm = (int)BuiltinHandlerId.ApplyDisplacement },
        ], GraphKind.Effect);
        programs.Register(postGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
        ], GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, preGraphId), Is.True);
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Post, postGraphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs))!;

        Assert.That(error.Message, Does.Contain("final executable operation"));
    }

    [Test]
    public void Finalize_PersistentExternalOperation_FailsAtLoadTime()
    {
        const int templateId = 107;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(EffectPresetType.Displacement, PhaseHandler.Builtin(BuiltinHandlerId.ApplyDisplacement));
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Displacement,
            LifetimeKind = EffectLifetimeKind.After,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, presets, new GraphProgramRegistry()))!;

        Assert.That(error.Message, Does.Contain("persistent effects"));
    }

    [Test]
    public void Finalize_UnsupportedLifecycleGraph_ReportsAssetEffectPhaseAndOperation()
    {
        const int templateId = 108;
        const int graphId = 208;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(graphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.BeginLifecycleTransaction },
        ], GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, graphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.UnsupportedOperationError));
        Assert.That(error.Message, Does.Contain("asset='Test/effects.json'"));
        Assert.That(error.Message, Does.Contain("effect='108'"));
        Assert.That(error.Message, Does.Contain("phase='OnApply'"));
        Assert.That(error.Message, Does.Contain("BeginLifecycleTransaction"));
    }

    [Test]
    public void Finalize_OnProposeRequiresValidationGraphKind()
    {
        const int templateId = 109;
        const int graphId = 209;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(graphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
        ], GraphKind.Validation);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnPropose, PhaseSlot.Main, graphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        Finalize(templates, new PresetTypeRegistry(), programs);

        Assert.That(templates.TryGetExecutionPlans(templateId, out _), Is.True);
    }

    [Test]
    public void Registry_UnfinalizedTemplate_FailsClosed()
    {
        var templates = new EffectTemplateRegistry();
        templates.Register(110, new EffectTemplateData { LifetimeKind = EffectLifetimeKind.Instant });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = templates.RequireExecutionPlans(110);
        })!;

        Assert.That(error.Message, Does.StartWith("GAS.EFFECT_PLAN.ERR.UnfinalizedTemplate"));
    }

    [Test]
    public void Registry_FinalizeAll_FreezesRegistrationAndMarksWholeRegistryReady()
    {
        var templates = new EffectTemplateRegistry();
        templates.Register(111, new EffectTemplateData { LifetimeKind = EffectLifetimeKind.Instant });

        Finalize(templates, new PresetTypeRegistry(), new GraphProgramRegistry());

        Assert.That(templates.AreExecutionPlansFinalized, Is.True);
        Assert.DoesNotThrow(templates.RequireFinalized);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            templates.Register(112, new EffectTemplateData { LifetimeKind = EffectLifetimeKind.Instant }))!;
        Assert.That(error.Message, Does.StartWith(EffectTemplateRegistry.RegistrationAfterFinalizationError));
    }

    [Test]
    public void RuntimeSystems_RejectUnfinalizedTemplateRegistryBeforeProcessing()
    {
        using World world = World.Create();
        var templates = new EffectTemplateRegistry();
        templates.Register(113, new EffectTemplateData { LifetimeKind = EffectLifetimeKind.Instant });
        var requests = new EffectRequestQueue(8);
        var clock = new DiscreteClock();
        var proposal = new EffectProposalProcessingSystem(
            world,
            requests,
            8,
            clock,
            templates: templates,
            responseChainOrderTypes: Ludots.Tests.GAS.TestResponseChainOrderTypeIds.Types);
        var application = new EffectApplicationSystem(world, 8, clock, requests, templates: templates);
        var lifetime = new EffectLifetimeSystem(
            world,
            clock,
            new GasConditionRegistry(),
            snapshotCapacity: 8,
            fanOutCommandCapacity: 8,
            effectRequests: requests,
            templates: templates);

        InvalidOperationException proposalError = Assert.Throws<InvalidOperationException>(() => proposal.UpdateSlice(0f, int.MaxValue))!;
        InvalidOperationException applicationError = Assert.Throws<InvalidOperationException>(() => application.UpdateSlice(0f, int.MaxValue))!;
        InvalidOperationException lifetimeError = Assert.Throws<InvalidOperationException>(() => lifetime.UpdateSlice(0f, int.MaxValue))!;

        Assert.That(proposalError.Message, Does.StartWith(EffectTemplateRegistry.UnfinalizedRegistryError));
        Assert.That(applicationError.Message, Does.StartWith(EffectTemplateRegistry.UnfinalizedRegistryError));
        Assert.That(lifetimeError.Message, Does.StartWith(EffectTemplateRegistry.UnfinalizedRegistryError));
    }

    private static PresetTypeRegistry CreatePreset(EffectPresetType type, PhaseHandler onApply)
    {
        var presets = new PresetTypeRegistry();
        var definition = new PresetTypeDefinition { Type = type };
        definition.DefaultPhaseHandlers[EffectPhaseId.OnApply] = onApply;
        presets.Register(in definition);
        return presets;
    }

    private static GraphInstruction[] GasWriteProgram()
    {
        return
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, B = 0, Imm = 1 },
        ];
    }

    private static void Finalize(
        EffectTemplateRegistry templates,
        PresetTypeRegistry presets,
        GraphProgramRegistry programs)
    {
        var builtins = new BuiltinHandlerRegistry();
        BuiltinHandlers.RegisterAll(builtins);
        EffectExecutionPlanCompiler.FinalizeAll(
            templates,
            presets,
            builtins,
            programs,
            GasGraphOpHandlerTable.Instance,
            "Test/effects.json");
    }
}
