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
    public void Finalize_SingleTerminalOrderSubmission_CertifiesExternalAtomicActivation()
    {
        const int templateId = 114;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(
            EffectPresetType.SubmitOrderFromBlackboard,
            PhaseHandler.Builtin(BuiltinHandlerId.SubmitOrderFromBlackboard));
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.SubmitOrderFromBlackboard,
            LifetimeKind = EffectLifetimeKind.Instant,
        });

        Finalize(templates, presets, new GraphProgramRegistry());

        EffectWindowExecutionPlan plan = templates.RequireExecutionPlans(templateId).Activation;
        Assert.That(plan.Kind, Is.EqualTo(EffectExecutionPlanKind.ExternalAtomicExclusive));
        Assert.That(plan.Domain, Is.EqualTo(EffectAtomicDomain.Order));
        Assert.That(plan.ExternalPhase, Is.EqualTo(EffectPhaseId.OnApply));
        Assert.That(plan.RequiresListenerPreflight, Is.True);
    }

    [Test]
    public void Finalize_RelationSetParent_CertifiesGasTransaction()
    {
        const int templateId = 115;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(
            EffectPresetType.Relation,
            PhaseHandler.Builtin(BuiltinHandlerId.ApplyRelation));
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Relation,
            LifetimeKind = EffectLifetimeKind.Instant,
            Relation = new RelationDescriptor
            {
                Operation = RelationOperation.SetParent,
                Subject = RelationEntitySlot.Source,
                Parent = RelationEntitySlot.Target,
            },
        });

        Finalize(templates, presets, new GraphProgramRegistry());

        Assert.That(
            templates.RequireExecutionPlans(templateId).Activation.Kind,
            Is.EqualTo(EffectExecutionPlanKind.GasTransactional));
    }

    // RemoveParent 不在本用例：#1064 事务对称化后它是已认证的 GasTransactional
    // 原子 op（StageRemoveParent），uncertified fail-closed 合同只覆盖仍无事务路径的操作。
    [TestCase(RelationOperation.EnsureLink, "ApplyRelation.EnsureLink")]
    public void Finalize_UncertifiedRelationOperation_FailsClosed(
        RelationOperation operation,
        string operationName)
    {
        const int templateId = 116;
        var templates = new EffectTemplateRegistry();
        var presets = CreatePreset(
            EffectPresetType.Relation,
            PhaseHandler.Builtin(BuiltinHandlerId.ApplyRelation));
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Relation,
            LifetimeKind = EffectLifetimeKind.Instant,
            Relation = new RelationDescriptor
            {
                Operation = operation,
                Subject = RelationEntitySlot.Source,
                Parent = RelationEntitySlot.Target,
                RelationshipTypeId = 1,
            },
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Finalize(templates, presets, new GraphProgramRegistry()))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.UnsupportedOperationError));
        Assert.That(error.Message, Does.Contain(operationName));
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
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        programs.Register(postGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
    public void Finalize_ListenerGraphWithUnsupportedOperation_ReportsAssetEffectPhaseAndOperation()
    {
        const int templateId = 114;
        const int listenerGraphId = 214;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(listenerGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.BeginLifecycleTransaction },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        EffectPhaseListenerBuffer listenerSetup = default;
        Assert.That(listenerSetup.TryAddTemplate(
            listenTagId: 0,
            listenEffectId: 0,
            EffectPhaseId.OnApply,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.ExecuteGraph,
            listenerGraphId,
            eventTagId: 0,
            priority: 0), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            ListenerSetup = listenerSetup,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.UnsupportedOperationError));
        Assert.That(error.Message, Does.Contain("asset='Test/effects.json'"));
        Assert.That(error.Message, Does.Contain("effect='114'"));
        Assert.That(error.Message, Does.Contain("phase='OnApply'"));
        Assert.That(error.Message, Does.Contain("BeginLifecycleTransaction"));
    }

    [Test]
    public void Finalize_ListenerGraphWithDelegatedBuiltin_FailsClosed()
    {
        const int templateId = 115;
        const int listenerGraphId = 215;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(listenerGraphId,
        [
            new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.InvokeBuiltin,
                Imm = (int)BuiltinHandlerId.ApplyDisplacement,
            },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        EffectPhaseListenerBuffer listenerSetup = default;
        Assert.That(listenerSetup.TryAddTemplate(
            listenTagId: 0,
            listenEffectId: 0,
            EffectPhaseId.OnApply,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.ExecuteGraph,
            listenerGraphId,
            eventTagId: 0,
            priority: 0), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listenerSetup,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.UnsupportedOperationError));
        Assert.That(error.Message, Does.Contain("InvokeBuiltin"));
        Assert.That(error.Message, Does.Contain("listenerIndex=0"));
        Assert.That(error.Message, Does.Contain("graphId=215"));
    }

    [Test]
    public unsafe void Finalize_OnProposeListenerEvent_FailsPurePhaseContract()
    {
        const int templateId = 116;
        var templates = new EffectTemplateRegistry();
        EffectPhaseListenerBuffer listenerSetup = default;
        Assert.That(listenerSetup.TryAddTemplate(
            listenTagId: 0,
            listenEffectId: 0,
            EffectPhaseId.OnApply,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.PublishEvent,
            graphProgramId: 0,
            eventTagId: 5,
            priority: 0), Is.True);
        listenerSetup.Phases[0] = (byte)EffectPhaseId.OnPropose;
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listenerSetup,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), new GraphProgramRegistry()))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.InvalidCompositionError));
        Assert.That(error.Message, Does.Contain("phase='OnPropose'"));
        Assert.That(error.Message, Does.Contain("pure phase"));
    }

    [Test]
    public void Registry_OnCalculateListenerGasWrite_FailsSharedGraphKindPolicy()
    {
        const int listenerGraphId = 217;
        var programs = new GraphProgramRegistry();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            programs.Register(listenerGraphId, GasWriteProgram(), GraphKind.Validation))!;

        Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
        Assert.That(error.Message, Does.Contain("WriteBlackboardFloat"));
        Assert.That(error.Message, Does.Contain("GraphProgramRegistry"));
        Assert.That(error.Message, Does.Contain("graphId=217"));
        Assert.That(error.Message, Does.Contain("kind='Validation'"));
    }

    [Test]
    public void Finalize_ListenerGraphWrongKind_ReportsFullCompositionContext()
    {
        const int templateId = 118;
        const int listenerGraphId = 218;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(listenerGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        EffectPhaseListenerBuffer listenerSetup = default;
        Assert.That(listenerSetup.TryAddTemplate(
            listenTagId: 0,
            listenEffectId: 0,
            EffectPhaseId.OnPropose,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.ExecuteGraph,
            listenerGraphId,
            eventTagId: 0,
            priority: 0), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listenerSetup,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.InvalidCompositionError));
        Assert.That(error.Message, Does.Contain("asset='Test/effects.json'"));
        Assert.That(error.Message, Does.Contain("effect='118'"));
        Assert.That(error.Message, Does.Contain("phase='OnPropose'"));
        Assert.That(error.Message, Does.Contain("listenerIndex=0"));
        Assert.That(error.Message, Does.Contain("graphId=218"));
        Assert.That(error.Message, Does.Contain("Validation"));
    }

    [Test]
    public void Finalize_MissingListenerGraph_ReportsFullCompositionContext()
    {
        const int templateId = 123;
        const int listenerGraphId = 223;
        var templates = new EffectTemplateRegistry();
        EffectPhaseListenerBuffer listenerSetup = default;
        Assert.That(listenerSetup.TryAddTemplate(
            listenTagId: 0,
            listenEffectId: 0,
            EffectPhaseId.OnApply,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.ExecuteGraph,
            listenerGraphId,
            eventTagId: 0,
            priority: 0), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listenerSetup,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), new GraphProgramRegistry()))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.InvalidCompositionError));
        Assert.That(error.Message, Does.Contain("asset='Test/effects.json'"));
        Assert.That(error.Message, Does.Contain("effect='123'"));
        Assert.That(error.Message, Does.Contain("phase='OnApply'"));
        Assert.That(error.Message, Does.Contain("listenerIndex=0"));
        Assert.That(error.Message, Does.Contain("graphId=223"));
    }

    [Test]
    public void Finalize_ListenerGraphMissingOperationMetadata_ReportsInstructionContext()
    {
        const int listenerGraphId = 224;
        const ushort unknownOperation = ushort.MaxValue;
        var programs = new GraphProgramRegistry();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            programs.Register(listenerGraphId,
        [
            new GraphInstruction { Op = unknownOperation },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect))!;

        Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.MissingOperationMetadataError));
        Assert.That(error.Message, Does.Contain("GraphProgramRegistry"));
        Assert.That(error.Message, Does.Contain("graphId=224"));
        Assert.That(error.Message, Does.Contain("instructionIndex=0"));
        Assert.That(error.Message, Does.Contain(unknownOperation.ToString()));
    }

    [Test]
    public void Finalize_ListenerBufferWithInvalidCount_FailsBeforeFixedBufferRead()
    {
        const int templateId = 119;
        var templates = new EffectTemplateRegistry();
        var listenerSetup = new EffectPhaseListenerBuffer
        {
            Count = EffectPhaseListenerBuffer.CAPACITY + 1,
        };
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listenerSetup,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), new GraphProgramRegistry()))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.InvalidCompositionError));
        Assert.That(error.Message, Does.Contain($"capacity={EffectPhaseListenerBuffer.CAPACITY}"));
    }

    [Test]
    public void Finalize_ListenerGasTransactionalGraph_Succeeds()
    {
        const int templateId = 120;
        const int listenerGraphId = 220;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(listenerGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, Imm = 7 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        EffectPhaseListenerBuffer listenerSetup = default;
        Assert.That(listenerSetup.TryAddTemplate(
            listenTagId: 0,
            listenEffectId: 0,
            EffectPhaseId.OnApply,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.ExecuteGraph,
            listenerGraphId,
            eventTagId: 0,
            priority: 0), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listenerSetup,
        });

        Finalize(templates, new PresetTypeRegistry(), programs);

        Assert.That(templates.AreExecutionPlansFinalized, Is.True);
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
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
    public void Registry_OnProposeInvokeBuiltinIsRejectedBySharedGraphKindPolicy()
    {
        const int graphId = 225;
        var programs = new GraphProgramRegistry();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            programs.Register(graphId,
        [
            new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.InvokeBuiltin,
                Imm = (int)BuiltinHandlerId.SpatialQuery,
            },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Validation))!;

        Assert.That(error.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
        Assert.That(error.Message, Does.Contain("InvokeBuiltin"));
        Assert.That(error.Message, Does.Contain("GraphProgramRegistry"));
        Assert.That(error.Message, Does.Contain("graphId=225"));
        Assert.That(error.Message, Does.Contain("kind='Validation'"));
    }

    [Test]
    public void Finalize_ListenerLoadConfigIsRejectedWithoutOwnerTemplateContext()
    {
        const int templateId = 126;
        const int graphId = 226;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(graphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadConfigFloat, Dst = 0, Imm = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        EffectPhaseListenerBuffer listeners = default;
        Assert.That(listeners.TryAddTemplate(
            0,
            0,
            EffectPhaseId.OnApply,
            PhaseListenerScope.Target,
            PhaseListenerActionFlags.ExecuteGraph,
            graphId,
            eventTagId: 0,
            priority: 0), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            ListenerSetup = listeners,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.UnsupportedOperationError));
        Assert.That(error.Message, Does.Contain(nameof(GraphNodeOp.LoadConfigFloat)));
        Assert.That(error.Message, Does.Contain("owner EffectTemplate config context"));
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
    public void Registry_ReservedZeroTemplateId_FailsClosed()
    {
        var templates = new EffectTemplateRegistry();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            templates.Register(0, new EffectTemplateData { LifetimeKind = EffectLifetimeKind.Instant }));
        Assert.That(templates.TryGet(0, out _), Is.False);
    }

    [Test]
    public void Registry_FinalizationRequiresAllFourExecutionWindows()
    {
        const int templateId = 120;
        var templates = new EffectTemplateRegistry();
        templates.Register(templateId, new EffectTemplateData { LifetimeKind = EffectLifetimeKind.Instant });
        var plans = new EffectExecutionPlanSet[EffectTemplateRegistry.MaxTemplates];
        EffectWindowExecutionPlan activation = new(EffectExecutionPlanKind.GasTransactional);
        plans[templateId] = new EffectExecutionPlanSet(
            in activation,
            period: default,
            expire: default,
            remove: default);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            templates.FinalizeExecutionPlans(plans))!;

        Assert.That(error.Message, Does.StartWith(EffectTemplateRegistry.UnfinalizedRegistryError));
        Assert.That(templates.AreExecutionPlansFinalized, Is.False);
        Assert.That(templates.TryGetExecutionPlans(templateId, out _), Is.False);
    }

    [Test]
    public void Registry_FailedFinalize_DoesNotExposePartialExecutionPlans()
    {
        const int validTemplateId = 121;
        const int invalidTemplateId = 122;
        const int invalidGraphId = 222;
        var templates = new EffectTemplateRegistry();
        templates.Register(validTemplateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
        });
        var programs = new GraphProgramRegistry();
        programs.Register(invalidGraphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.BeginLifecycleTransaction },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        ], GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, invalidGraphId), Is.True);
        templates.Register(invalidTemplateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        Assert.Throws<InvalidOperationException>(() =>
            Finalize(templates, new PresetTypeRegistry(), programs));

        Assert.That(templates.AreExecutionPlansFinalized, Is.False);
        Assert.That(templates.TryGetExecutionPlans(validTemplateId, out _), Is.False);
        Assert.That(templates.TryGetExecutionPlans(invalidTemplateId, out _), Is.False);
        Assert.Throws<InvalidOperationException>(templates.RequireFinalized);
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
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
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
