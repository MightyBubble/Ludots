using System;
using System.Reflection;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class InstantEffectTransactionTests
{
    [TestCase(-1)]
    [TestCase(EffectPhaseListenerBuffer.CAPACITY + 1)]
    public void ListenerRegistration_InvalidSetupCountFailsBeforeStaging(int invalidCount)
    {
        using World world = World.Create();
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps: null,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 2);
        transaction.Begin();
        EffectPhaseListenerBuffer setup = default;
        setup.Count = invalidCount;
        var context = new EffectContext
        {
            Source = world.Create(),
            Target = world.Create(),
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            transaction.StageListenerRegistration(in context, in setup, ownerEffectId: 1))!;

        Assert.That(error.Message, Does.StartWith(EffectPhaseListenerContract.InvalidBufferCountError));
        transaction.Rollback();
    }

    [Test]
    public unsafe void ListenerRegistration_InvalidEntryFailsBeforeStaging()
    {
        using World world = World.Create();
        Entity source = world.Create();
        Entity target = world.Create();
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps: null,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 2);
        transaction.Begin();
        EffectPhaseListenerBuffer setup = default;
        setup.Count = 1;
        setup.Phases[0] = (byte)EffectPhaseId.OnApply;
        setup.Scopes[0] = (byte)PhaseListenerScope.Target;
        setup.ActionFlags[0] = (byte)PhaseListenerActionFlags.ExecuteGraph;
        setup.GraphProgramIds[0] = 0;
        var context = new EffectContext { Source = source, Target = target };

        InvalidOperationException error;
        try
        {
            transaction.StageListenerRegistration(in context, in setup, ownerEffectId: 1);
            Assert.Fail("Expected invalid listener entry to fail before staging.");
            return;
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }
        setup.GraphProgramIds[0] = 1;
        transaction.StageListenerRegistration(in context, in setup, ownerEffectId: 1);
        transaction.Commit();

        Assert.That(error.Message, Does.StartWith(EffectPhaseListenerContract.InvalidRegistrationError));
        Assert.That(world.Has<EffectPhaseListenerBuffer>(target), Is.True);
        Assert.That(world.Get<EffectPhaseListenerBuffer>(target).Count, Is.EqualTo(1));
    }

    [Test]
    public void InstantGraph_WhenLaterOperationFails_RollsBackAllStagedSideEffects()
    {
        using World world = World.Create();
        int healthId = AttributeRegistry.Register("Test.InstantTransaction.Health");
        const int rootTemplateId = 1801;
        const int childTemplateId = 1802;
        const int graphId = 1803;
        const int eventTagId = 1804;

        var programs = new GraphProgramRegistry();
        programs.Register(graphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 0, Imm = healthId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = childTemplateId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectDynamic, A = 0, B = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 0, B = 0, Imm = eventTagId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, B = 0, Imm = 1 },
        ], GraphKind.Effect);

        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, graphId), Is.True);
        var templates = new EffectTemplateRegistry();
        templates.Register(rootTemplateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });
        templates.Register(childTemplateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
        });

        var presets = new PresetTypeRegistry();
        var builtins = new BuiltinHandlerRegistry();
        BuiltinHandlers.RegisterAll(builtins);
        EffectExecutionPlanCompiler.FinalizeAll(
            templates,
            presets,
            builtins,
            programs,
            GasGraphOpHandlerTable.Instance,
            "Test/instant-transaction-effects.json");

        var requests = new EffectRequestQueue(8);
        var presentationEvents = new GasPresentationEventBuffer(8);
        var eventBus = new GameplayEventBus();
        var tagOps = new TagOps(new DirtyEntityQueue(8), new TagRuleRegistry());
        var phaseExecutor = new EffectPhaseExecutor(
            programs,
            presets,
            builtins,
            GasGraphOpHandlerTable.Instance,
            templates,
            eventBus: eventBus);
        var graphApi = new GasGraphRuntimeApi(
            world,
            eventBus: eventBus,
            effectRequests: requests,
            tagOps: tagOps);
        Entity source = world.Create();
        Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
        world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);
        requests.Publish(new EffectRequest
        {
            RootId = 99,
            Source = source,
            Target = target,
            TemplateId = rootTemplateId,
        });
        using var proposal = new EffectProposalProcessingSystem(
            world,
            requests,
            fanOutCommandCapacity: 8,
            clock: new DiscreteClock(),
            templates: templates,
            responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
            presentationEvents: presentationEvents,
            phaseExecutor: phaseExecutor,
            graphApi: graphApi,
            tagOps: tagOps);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => proposal.Update(0f))!;

        Assert.That(error.Message, Does.StartWith("GAS.EFFECT_TRANSACTION.ERR.MissingBlackboard"));
        Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthId), Is.EqualTo(100f));
        Assert.That(presentationEvents.Count, Is.Zero);
        for (int i = 0; i < requests.Count; i++)
        {
            Assert.That(requests[i].TemplateId, Is.Not.EqualTo(childTemplateId));
        }
        eventBus.Update();
        Assert.That(eventBus.Events.Count, Is.Zero);
    }

    [Test]
    public void RelationSetParent_InstantEffect_CommitsRelationAndPositionTogether()
    {
        using World world = World.Create();
        const int templateId = 1810;
        Entity child = world.Create(
            WorldPositionCm.FromCm(10, 20),
            new PreviousWorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(5, 15) });
        Entity parent = world.Create(
            WorldPositionCm.FromCm(1200, 800),
            new PreviousWorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(1100, 700) });
        using EffectProposalProcessingSystem proposal = CreateSetParentRuntime(
            world,
            templateId,
            out EffectRequestQueue requests);
        requests.Publish(new EffectRequest
        {
            RootId = 1,
            Source = child,
            Target = parent,
            TemplateId = templateId,
        });

        proposal.Update(0f);
        requests.Publish(new EffectRequest
        {
            RootId = 2,
            Source = child,
            Target = parent,
            TemplateId = templateId,
        });
        proposal.Update(0f);

        Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
        Assert.That(world.Get<ChildrenBuffer>(parent).Contains(in child), Is.True);
        Assert.That(world.Get<ChildrenBuffer>(parent).Count, Is.EqualTo(1));
        Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(world.Get<WorldPositionCm>(parent).Value));
        Assert.That(
            world.Get<PreviousWorldPositionCm>(child).Value,
            Is.EqualTo(world.Get<PreviousWorldPositionCm>(parent).Value));
    }

    [Test]
    public void RelationSetParent_WhenDestinationIsFull_LeavesRelationAndPositionUntouched()
    {
        using World world = World.Create();
        const int templateId = 1811;
        Entity oldParent = world.Create(new ChildrenBuffer());
        Entity child = world.Create(
            WorldPositionCm.FromCm(10, 20),
            new PreviousWorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(5, 15) });
        RelationOps.SetParent(world, child, oldParent);
        ChildrenBuffer destinationChildren = default;
        for (int i = 0; i < GasConstants.MAX_CHILDREN_BUFFER_CAPACITY; i++)
        {
            Entity existingChild = world.Create();
            Assert.That(destinationChildren.Add(in existingChild), Is.True);
        }
        Entity fullParent = world.Create(
            destinationChildren,
            WorldPositionCm.FromCm(1200, 800),
            new PreviousWorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(1100, 700) });
        WorldPositionCm originalPosition = world.Get<WorldPositionCm>(child);
        PreviousWorldPositionCm originalPreviousPosition = world.Get<PreviousWorldPositionCm>(child);
        using EffectProposalProcessingSystem proposal = CreateSetParentRuntime(
            world,
            templateId,
            out EffectRequestQueue requests);
        requests.Publish(new EffectRequest
        {
            RootId = 2,
            Source = child,
            Target = fullParent,
            TemplateId = templateId,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => proposal.Update(0f))!;

        Assert.That(error.Message, Does.StartWith(EffectPhaseSideEffectTransaction.CapacityExceededError));
        Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(oldParent));
        Assert.That(world.Get<ChildrenBuffer>(oldParent).Contains(in child), Is.True);
        Assert.That(world.Get<ChildrenBuffer>(fullParent).Count, Is.EqualTo(GasConstants.MAX_CHILDREN_BUFFER_CAPACITY));
        Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(originalPosition.Value));
        Assert.That(world.Get<PreviousWorldPositionCm>(child).Value, Is.EqualTo(originalPreviousPosition.Value));
    }

    [Test]
    public void RelationSetParent_WhenParentPositionIsMissing_LeavesCurrentRelationUntouched()
    {
        using World world = World.Create();
        const int templateId = 1812;
        Entity oldParent = world.Create(new ChildrenBuffer());
        Entity child = world.Create(WorldPositionCm.FromCm(10, 20));
        RelationOps.SetParent(world, child, oldParent);
        Entity parentWithoutPosition = world.Create(new ChildrenBuffer());
        using EffectProposalProcessingSystem proposal = CreateSetParentRuntime(
            world,
            templateId,
            out EffectRequestQueue requests);
        requests.Publish(new EffectRequest
        {
            RootId = 3,
            Source = child,
            Target = parentWithoutPosition,
            TemplateId = templateId,
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => proposal.Update(0f))!;

        Assert.That(error.Message, Does.StartWith("GAS.EFFECT_TRANSACTION.ERR.RelationParentPositionMissing"));
        Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(oldParent));
        Assert.That(world.Get<ChildrenBuffer>(oldParent).Contains(in child), Is.True);
        Assert.That(world.Get<ChildrenBuffer>(parentWithoutPosition).Count, Is.Zero);
        Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(WorldPositionCm.FromCm(10, 20).Value));
    }

    [Test]
    public void RelationSetParent_WhenSnapSourceMovesAfterStaging_FailsClosed()
    {
        using World world = World.Create();
        Entity oldParent = world.Create(new ChildrenBuffer());
        Entity child = world.Create(WorldPositionCm.FromCm(10, 20));
        RelationOps.SetParent(world, child, oldParent);
        Entity newParent = world.Create(
            new ChildrenBuffer(),
            WorldPositionCm.FromCm(1200, 800));
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps: null,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 8);
        transaction.Begin();
        transaction.StageSetParent(child, newParent, snapSubjectToParentPosition: true);
        world.Get<WorldPositionCm>(newParent) = WorldPositionCm.FromCm(1400, 900);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => transaction.Commit())!;
        transaction.Rollback();

        Assert.That(error.Message, Does.StartWith(EffectPhaseSideEffectTransaction.RelationTargetInvalidError));
        Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(oldParent));
        Assert.That(world.Get<ChildrenBuffer>(oldParent).Contains(in child), Is.True);
        Assert.That(world.Get<ChildrenBuffer>(newParent).Count, Is.Zero);
        Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(WorldPositionCm.FromCm(10, 20).Value));
    }

    [Test]
    public void RelationSetParent_WhenFaultOccursAfterStructuralPlayback_RestoresAllRelationState()
    {
        using World world = World.Create();
        Entity oldParent = world.Create(new ChildrenBuffer());
        Entity child = world.Create(
            WorldPositionCm.FromCm(10, 20),
            new PreviousWorldPositionCm
            {
                Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(5, 15),
            });
        RelationOps.SetParent(world, child, oldParent);
        Entity newParent = world.Create(
            WorldPositionCm.FromCm(1200, 800),
            new PreviousWorldPositionCm
            {
                Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(1100, 700),
            });
        WorldPositionCm originalPosition = world.Get<WorldPositionCm>(child);
        PreviousWorldPositionCm originalPreviousPosition = world.Get<PreviousWorldPositionCm>(child);
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps: null,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 8);
        transaction.Begin();
        transaction.StageSetParent(child, newParent, snapSubjectToParentPosition: true);

        Type transactionType = typeof(EffectPhaseSideEffectTransaction);
        MethodInfo prepareCommitState = transactionType.GetMethod(
            "PrepareCommitState",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo structuralCommandsField = transactionType.GetField(
            "_structuralCommands",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo worldCommitStartedField = transactionType.GetField(
            "_worldCommitStarted",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Exercise the synchronous post-playback failure window without a production test hook.
        prepareCommitState.Invoke(transaction, null);
        worldCommitStartedField.SetValue(transaction, true);
        ((CommandBuffer)structuralCommandsField.GetValue(transaction)!).Playback(world);
        world.Get<ChildrenBuffer>(oldParent).Remove(in child);
        world.Get<ChildOf>(child) = new ChildOf { Parent = newParent };
        world.Get<WorldPositionCm>(child) = world.Get<WorldPositionCm>(newParent);
        world.Get<PreviousWorldPositionCm>(child) = world.Get<PreviousWorldPositionCm>(newParent);

        transaction.Rollback();

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<ChildrenBuffer>(oldParent).Contains(in child), Is.True);
            Assert.That(world.Has<ChildrenBuffer>(newParent), Is.False);
            Assert.That(world.Get<ChildOf>(child).Parent, Is.EqualTo(oldParent));
            Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(originalPosition.Value));
            Assert.That(
                world.Get<PreviousWorldPositionCm>(child).Value,
                Is.EqualTo(originalPreviousPosition.Value));
        });
    }

    private static EffectProposalProcessingSystem CreateSetParentRuntime(
        World world,
        int templateId,
        out EffectRequestQueue requests)
    {
        var templates = new EffectTemplateRegistry();
        templates.Register(templateId, new EffectTemplateData
        {
            PresetType = EffectPresetType.Relation,
            LifetimeKind = EffectLifetimeKind.Instant,
            Relation = new RelationDescriptor
            {
                Operation = RelationOperation.SetParent,
                Subject = RelationEntitySlot.Source,
                Parent = RelationEntitySlot.Target,
                SnapSubjectToParentPosition = true,
            },
        });
        var presets = new PresetTypeRegistry();
        var relationPreset = new PresetTypeDefinition
        {
            Type = EffectPresetType.Relation,
            ActivePhases = PhaseFlags.OnApply,
            AllowedLifetimes = LifetimeFlags.InstantOnly,
        };
        relationPreset.DefaultPhaseHandlers[EffectPhaseId.OnApply] =
            PhaseHandler.Builtin(BuiltinHandlerId.ApplyRelation);
        presets.Register(in relationPreset);
        var builtins = new BuiltinHandlerRegistry();
        BuiltinHandlers.RegisterAll(builtins);
        var programs = new GraphProgramRegistry();
        EffectExecutionPlanCompiler.FinalizeAll(
            templates,
            presets,
            builtins,
            programs,
            GasGraphOpHandlerTable.Instance,
            "Test/relation-effects.json");
        var phaseExecutor = new EffectPhaseExecutor(
            programs,
            presets,
            builtins,
            GasGraphOpHandlerTable.Instance,
            templates);
        var graphApi = new GasGraphRuntimeApi(world);
        requests = new EffectRequestQueue(8);
        return new EffectProposalProcessingSystem(
            world,
            requests,
            fanOutCommandCapacity: 8,
            clock: new DiscreteClock(),
            templates: templates,
            responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
            phaseExecutor: phaseExecutor,
            graphApi: graphApi);
    }
}
