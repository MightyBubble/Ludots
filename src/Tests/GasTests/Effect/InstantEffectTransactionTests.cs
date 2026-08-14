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

    [Test]
    public unsafe void RollbackWorldWrites_WhenBlackboardHolderIsDestroyed_CompletesRemainingRestores()
    {
        using World world = World.Create();
        int healthId = AttributeRegistry.Register("Test.S4.Rollback.Health");
        var tagOps = new TagOps(new DirtyEntityQueue(8), new TagRuleRegistry());
        Entity victim = world.Create(new BlackboardFloatBuffer());
        Entity survivor = world.Create(new AttributeBuffer(), new DirtyFlags());
        world.Get<AttributeBuffer>(survivor).SetBase(healthId, 100f);
        Entity source = world.Create();
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 8);
        transaction.Begin();
        transaction.StageBlackboardFloat(victim, keyId: 7, value: 3.5f);
        transaction.StageAttributeAdd(survivor, healthId, -25f);
        EffectPhaseListenerBuffer setup = default;
        setup.Count = 1;
        setup.Phases[0] = (byte)EffectPhaseId.OnApply;
        setup.Scopes[0] = (byte)PhaseListenerScope.Target;
        setup.ActionFlags[0] = (byte)PhaseListenerActionFlags.ExecuteGraph;
        setup.GraphProgramIds[0] = 1;
        var context = new EffectContext { Source = source, Target = survivor };
        transaction.StageListenerRegistration(in context, in setup, ownerEffectId: 11);

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
        FieldInfo attributeValuesField = transactionType.GetField(
            "_attributeValues",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        prepareCommitState.Invoke(transaction, null);
        worldCommitStartedField.SetValue(transaction, true);
        ((CommandBuffer)structuralCommandsField.GetValue(transaction)!).Playback(world);
        world.Get<AttributeBuffer>(survivor) = ((AttributeBuffer[])attributeValuesField.GetValue(transaction)!)[0];
        world.Destroy(victim);

        Assert.DoesNotThrow(() => transaction.Rollback());

        Assert.Multiple(() =>
        {
            Assert.That(world.IsAlive(victim), Is.False);
            Assert.That(world.Get<AttributeBuffer>(survivor).GetCurrent(healthId), Is.EqualTo(100f));
            Assert.That(world.Has<EffectPhaseListenerBuffer>(survivor), Is.False);
        });
    }

    [Test]
    public void StagePresentationEvent_WhenBufferIsMissing_ThrowsNamedError()
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

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            transaction.StagePresentationEvent(new GasPresentationEvent
            {
                Kind = GasPresentationEventKind.EffectActivated,
            }))!;

        Assert.That(error.Message, Does.StartWith(EffectPhaseSideEffectTransaction.MissingPresentationEventBufferError));
        transaction.Rollback();
    }

    [Test]
    public void StageGrantedTagGrant_CommitsThroughTransactionAndRollsBackOnAbort()
    {
        using World world = World.Create();
        const int tagId = 91;
        var dirtyQueue = new DirtyEntityQueue(8);
        var tagOps = new TagOps(dirtyQueue, new TagRuleRegistry());
        Entity target = world.Create(new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
        var grantedTags = new EffectGrantedTags();
        Assert.That(grantedTags.Add(new TagContribution
        {
            TagId = tagId,
            Formula = TagContributionFormula.Fixed,
            Amount = 1,
        }), Is.True);
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 4);

        transaction.Begin();
        transaction.StageGrantedTagGrant(target, in grantedTags, stackCount: 1);
        Assert.That(world.Get<GameplayTagContainer>(target).HasTag(tagId), Is.False);
        transaction.Rollback();
        Assert.That(world.Get<GameplayTagContainer>(target).HasTag(tagId), Is.False);
        Assert.That(dirtyQueue.Count, Is.Zero);

        transaction.Begin();
        transaction.StageGrantedTagGrant(target, in grantedTags, stackCount: 1);
        transaction.Commit();

        Assert.That(world.Get<GameplayTagContainer>(target).HasTag(tagId), Is.True);
        Assert.That(world.Get<TagCountContainer>(target).GetCount(tagId), Is.EqualTo(1));
        Assert.That(dirtyQueue.Count, Is.EqualTo(1));
    }

    [Test]
    public void FanOutBuiltins_WhenRequiredServicesAreMissing_ThrowNamedErrors()
    {
        using World world = World.Create();
        Entity source = world.Create();
        Entity target = world.Create();
        var context = new EffectContext { Source = source, Target = target };
        EffectConfigParams mergedParams = default;
        EffectTemplateData template = default;

        using (BuiltinHandlerRuntimeScope.Push(new BuiltinHandlerExecutionContext()))
        {
            InvalidOperationException spatial = Assert.Throws<InvalidOperationException>(() =>
                BuiltinHandlers.HandleSpatialQuery(world, default, ref context, in mergedParams, in template))!;
            Assert.That(spatial.Message, Does.StartWith(BuiltinHandlers.MissingSpatialQueriesError));

            InvalidOperationException dispatch = Assert.Throws<InvalidOperationException>(() =>
                BuiltinHandlers.HandleDispatchPayload(world, default, ref context, in mergedParams, in template))!;
            Assert.That(dispatch.Message, Does.StartWith(BuiltinHandlers.MissingFanOutBudgetError));

            InvalidOperationException reresolve = Assert.Throws<InvalidOperationException>(() =>
                BuiltinHandlers.HandleReResolveAndDispatch(world, default, ref context, in mergedParams, in template))!;
            Assert.That(reresolve.Message, Does.StartWith(BuiltinHandlers.MissingSpatialQueriesError));
        }

        InvalidOperationException missingRuntime = Assert.Throws<InvalidOperationException>(() =>
            BuiltinHandlers.HandleSpatialQuery(world, default, ref context, in mergedParams, in template))!;
        Assert.That(missingRuntime.Message, Does.StartWith(BuiltinHandlers.MissingHandlerRuntimeError));
    }

    [Test]
    public void EffectApplication_AttachIsVisibleIntermediateState_BeforeActivateSettlement()
    {
        using World world = World.Create();
        Entity source = world.Create();
        Entity target = world.Create(new ActiveEffectContainer());
        Entity effect = GameplayEffectFactory.CreateEffect(
            world,
            rootId: 1,
            source,
            target,
            durationTicks: 30,
            lifetimeKind: EffectLifetimeKind.After);
        var system = new EffectApplicationSystem(
            world,
            GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
            new DiscreteClock())
        {
            MaxWorkUnitsPerSlice = 1,
        };

        Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
        Assert.That(world.IsAlive(effect), Is.True);
        Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));
        Assert.That(world.Get<GameplayEffect>(effect).State, Is.EqualTo(EffectState.Apply));

        system.MaxWorkUnitsPerSlice = int.MaxValue;
        int slices = 0;
        while (!system.UpdateSlice(0f, int.MaxValue))
        {
            slices++;
            Assert.That(slices, Is.LessThan(16));
        }

        Assert.That(world.Get<GameplayEffect>(effect).State, Is.EqualTo(EffectState.Committed));
        Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));
    }

    [Test]
    public void EffectApplication_ActivateFailure_LeavesVisibleAttachment_AndResetSliceReclaimsIt()
    {
        using World world = World.Create();
        const int templateId = 1942;
        const int graphId = 1943;
        var programs = new GraphProgramRegistry();
        programs.Register(graphId,
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, B = 0, Imm = 1 },
        ], GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, graphId), Is.True);
        var templates = new EffectTemplateRegistry();
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.After,
            DurationTicks = 30,
            PhaseGraphBindings = bindings,
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
            "Test/s4-attach-intermediate-effects.json");
        var phaseExecutor = new EffectPhaseExecutor(
            programs,
            presets,
            builtins,
            GasGraphOpHandlerTable.Instance,
            templates);
        var graphApi = new GasGraphRuntimeApi(world);
        Entity source = world.Create();
        Entity target = world.Create(new ActiveEffectContainer());
        Entity effect = GameplayEffectFactory.CreateEffect(
            world,
            rootId: 2,
            source,
            target,
            durationTicks: 30,
            lifetimeKind: EffectLifetimeKind.After);
        world.Add(effect, new EffectTemplateRef { TemplateId = templateId });
        var system = new EffectApplicationSystem(
            world,
            GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
            new DiscreteClock(),
            templates: templates,
            phaseExecutor: phaseExecutor,
            graphApi: graphApi)
        {
            MaxWorkUnitsPerSlice = 1,
        };

        Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
        Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));

        system.MaxWorkUnitsPerSlice = int.MaxValue;
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            while (!system.UpdateSlice(0f, int.MaxValue))
            {
            }
        })!;

        Assert.That(error.Message, Does.StartWith("GAS.EFFECT_TRANSACTION.ERR.MissingBlackboard"));
        Assert.That(world.IsAlive(effect), Is.True);
        Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));
        Assert.That(world.Get<GameplayEffect>(effect).State, Is.EqualTo(EffectState.Apply));

        system.ResetSlice();

        Assert.That(world.IsAlive(effect), Is.False);
        Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.Zero);
    }

    [Test]
    public void StageEffectDestroy_LandsOnlyAfterSuccessfulCommit()
    {
        using World world = World.Create();
        Entity effect = world.Create(new GameplayEffect());
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps: null,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 2);
        transaction.Begin();
        transaction.StageEffectDestroy(effect);
        Assert.That(world.IsAlive(effect), Is.True);
        transaction.Commit();
        Assert.That(world.IsAlive(effect), Is.False);
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
