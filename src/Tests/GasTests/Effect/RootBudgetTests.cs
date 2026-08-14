using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class RootBudgetTests
    {
        private const int TestSnapshotCapacity = 64;
        private const int TestFanOutCommandCapacity = 64;

        private static TagOps CreateTagOps() =>
            new(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());

        [Test]
        public void EffectRequestQueue_AssignsRootId_WhenMissing_AndPreservesExplicit()
        {
            var q = new EffectRequestQueue();

            q.Publish(new EffectRequest { RootId = 0, TemplateId = 1 });
            q.Publish(new EffectRequest { RootId = 0, TemplateId = 2 });
            q.Publish(new EffectRequest { RootId = 123, TemplateId = 3 });

            That(q.Count, Is.EqualTo(3));
            That(q[0].RootId, Is.Not.EqualTo(0));
            That(q[1].RootId, Is.Not.EqualTo(0));
            That(q[1].RootId, Is.Not.EqualTo(q[0].RootId));
            That(q[2].RootId, Is.EqualTo(123));
        }

        [Test]
        public void EffectRequestQueue_Reserve_ExpandsCapacity()
        {
            var q = new EffectRequestQueue(initialCapacity: 4096);
            int before = q.Capacity;
            q.Reserve(100_000);
            That(q.Capacity, Is.GreaterThanOrEqualTo(100_000));
            That(q.Capacity, Is.GreaterThan(before));
        }

        [Test]
        [Timeout(1000)]
        public void RootBudgetTable_WhenDistinctRootsFillTable_ReturnsFalseWithoutDeadLoop()
        {
            var table = new RootBudgetTable(capacity: 2);

            That(table.TryConsume(rootId: 1, limit: GasConstants.MAX_CREATES_PER_ROOT), Is.True);
            That(table.TryConsume(rootId: 2, limit: GasConstants.MAX_CREATES_PER_ROOT), Is.True);

            That(table.TryConsume(rootId: 3, limit: GasConstants.MAX_CREATES_PER_ROOT), Is.False);
        }

        [Test]
        public void EffectPhaseTransaction_Rollback_RestoresRootBudgetConsumption()
        {
            using var world = World.Create();
            var budget = new RootBudgetTable(capacity: 8);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 4,
                rootBudget: budget);

            transaction.Begin();
            That(budget.TryConsume(rootId: 77, limit: 1), Is.True);
            transaction.Rollback();

            That(budget.TryConsume(rootId: 77, limit: 1), Is.True);
        }

        [Test]
        public void EffectPhaseTransaction_Commit_PreservesRootBudgetConsumption()
        {
            using var world = World.Create();
            var budget = new RootBudgetTable(capacity: 8);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 4,
                rootBudget: budget);

            transaction.Begin();
            That(budget.TryConsume(rootId: 88, limit: 1), Is.True);
            transaction.Commit();

            That(budget.TryConsume(rootId: 88, limit: 1), Is.False);
        }

        [Test]
        public void TargetResolverFanOut_WhenRootBudgetExceeded_ThrowsBeforeDroppingTarget()
        {
            using var world = World.Create();
            var source = world.Create();
            var target = world.Create();
            var candidates = new[] { target };
            var budget = new RootBudgetTable(capacity: 8);
            for (int i = 0; i < GasConstants.MAX_CREATES_PER_ROOT; i++)
            {
                That(budget.TryConsume(rootId: 77, limit: GasConstants.MAX_CREATES_PER_ROOT), Is.True);
            }

            var commands = new FanOutCommandBuffer(capacity: 8);
            var ctx = new EffectContext
            {
                RootId = 77,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
            };
            var query = new TargetQueryDescriptor();
            var filter = new TargetFilterDescriptor();
            var dispatch = new TargetDispatchDescriptor
            {
                PayloadEffectTemplateId = 1001,
                ContextMapping = TargetResolverContextMapping.Default,
            };
            var error = Throws<InvalidOperationException>(() =>
                TargetResolverFanOutHelper.ValidateAndCollect(
                    world,
                    in ctx,
                    in query,
                    in filter,
                    in dispatch,
                    candidates,
                    candidates.Length,
                    budget,
                    commands));

            That(error!.Message, Does.StartWith(TargetResolverFanOutHelper.RootBudgetExceededError));
            That(commands.Count, Is.Zero);
        }

        // Note: EffectCallbackComponent has been removed per the "Everything is Graph" architecture.
        // OnApply/OnExpire callbacks are now Phase Graph bindings in EffectPhaseGraphBindings.
        // Budget tests for Phase Graph-based callbacks will be added once graph programs
        // are available in the test fixture.

        [Test]
        public void EffectApplicationSystem_ProcessesInstantEffects_WithoutCallbacks()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var templates = new EffectTemplateRegistry();
                templates.Register(2001, new EffectTemplateData
                {
                    TagId = 0,
                    PresetType = EffectPresetType.None,
                    LifetimeKind = EffectLifetimeKind.Instant,
                });
                _ = FinalizeTemplates(templates);
                var proposal = new EffectProposalProcessingSystem(
                    world,
                    requests,
                    fanOutCommandCapacity: TestFanOutCommandCapacity,
                    clock: new DiscreteClock(),
                    budget: budget,
                    templates: templates,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: CreateTagOps());

                var source = world.Create();
                var target = world.Create(new DirtyFlags());

                for (int i = 0; i < 10; i++)
                {
                    requests.Publish(new EffectRequest
                    {
                        RootId = 1,
                        Source = source,
                        Target = target,
                        TargetContext = Entity.Null,
                        TemplateId = 2001,
                    });
                }

                proposal.Update(0.016f);

                // Without callbacks, no EffectRequests should be published
                That(requests.Count, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectApplicationSystem_InstantEffectPublishesEffectApplied_WhenPhaseExecutorAppliesModifiers()
        {
            using var world = World.Create();

            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            int hpAttrId = AttributeRegistry.Register("Test.RootBudget.PhaseExecutor.Health");
            var modifiers = default(EffectModifiers);
            modifiers.Add(hpAttrId, ModifierOp.Add, -15f);
            templates.Register(2002, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                Modifiers = modifiers,
            });

            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition
            {
                Type = EffectPresetType.InstantDamage,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.OnApply,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in preset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var graphPrograms = new GraphProgramRegistry();
            var phaseExecutor = FinalizeTemplates(
                templates,
                presetTypes,
                builtinHandlers,
                graphPrograms);
            var tagOps = CreateTagOps();
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, tagOps: tagOps);
            var requests = new EffectRequestQueue();
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                fanOutCommandCapacity: TestFanOutCommandCapacity,
                clock: new DiscreteClock(),
                budget: null,
                presentationEvents: presentationEvents,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(hpAttrId, 100f);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2002,
            });

            proposal.Update(0.016f);

            That(world.Get<AttributeBuffer>(target).GetCurrent(hpAttrId), Is.EqualTo(85f));
            That(presentationEvents.Count, Is.EqualTo(1));
            ref readonly GasPresentationEvent evt = ref presentationEvents.Events[0];
            That(evt.Kind, Is.EqualTo(GasPresentationEventKind.EffectApplied));
            That(evt.Actor, Is.EqualTo(source));
            That(evt.Target, Is.EqualTo(target));
            That(evt.EffectTemplateId, Is.EqualTo(2002));
            That(evt.AttributeId, Is.EqualTo(hpAttrId));
            That(evt.Delta, Is.EqualTo(-15f));
        }

        [Test]
        public void EffectProposalProcessingSystem_PureInstantPublishesEffectApplied()
        {
            using var world = World.Create();

            var requests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            int hpAttrId = AttributeRegistry.Register("Test.RootBudget.PureInstant.Health");
            var modifiers = default(EffectModifiers);
            modifiers.Add(hpAttrId, ModifierOp.Add, -15f);
            templates.Register(2003, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                Modifiers = modifiers,
            });
            var instantDamagePreset = CreateModifierPreset(
                EffectPresetType.InstantDamage,
                ComponentFlags.ModifierParams,
                PhaseFlags.OnApply,
                LifetimeFlags.InstantOnly);
            _ = FinalizeTemplates(templates, in instantDamagePreset);

            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                fanOutCommandCapacity: TestFanOutCommandCapacity,
                clock: new DiscreteClock(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                presentationEvents: presentationEvents,
                tagOps: CreateTagOps());

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(hpAttrId, 100f);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2003,
            });

            proposal.Update(0.016f);

            That(world.Get<AttributeBuffer>(target).GetCurrent(hpAttrId), Is.EqualTo(85f));
            That(presentationEvents.Count, Is.EqualTo(1));
            ref readonly GasPresentationEvent evt = ref presentationEvents.Events[0];
            That(evt.Kind, Is.EqualTo(GasPresentationEventKind.EffectApplied));
            That(evt.Actor, Is.EqualTo(source));
            That(evt.Target, Is.EqualTo(target));
            That(evt.EffectTemplateId, Is.EqualTo(2003));
            That(evt.AttributeId, Is.EqualTo(hpAttrId));
            That(evt.Delta, Is.EqualTo(-15f));
        }

        [Test]
        public void EffectProcessingLoopSystem_ParticipatingInstantPublishesEffectApplied()
        {
            using var world = World.Create();

            var requests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            int hpAttrId = AttributeRegistry.Register("Test.RootBudget.LoopInstant.Health");
            var modifiers = default(EffectModifiers);
            modifiers.Add(hpAttrId, ModifierOp.Add, -15f);
            templates.Register(2004, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                ParticipatesInResponse = true,
                Modifiers = modifiers,
            });
            var instantDamagePreset = CreateModifierPreset(
                EffectPresetType.InstantDamage,
                ComponentFlags.ModifierParams,
                PhaseFlags.OnApply,
                LifetimeFlags.InstantOnly);
            _ = FinalizeTemplates(templates, in instantDamagePreset);

            var loop = new EffectProcessingLoopSystem(
                world,
                requests,
                new DiscreteClock(),
                new GasConditionRegistry(),
                lifetimeSnapshotCapacity: TestSnapshotCapacity,
                fanOutCommandCapacity: TestFanOutCommandCapacity,
                budget: null,
                templates: templates,
                inputRequests: null,
                chainOrders: null,
                telemetry: new ResponseChainTelemetryBuffer(),
                orderRequests: new OrderRequestQueue(),
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                presentationEvents: presentationEvents,
                tagOps: CreateTagOps());

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(hpAttrId, 100f);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2004,
            });

            loop.Update(0.016f);

            That(world.Get<AttributeBuffer>(target).GetCurrent(hpAttrId), Is.EqualTo(85f));
            That(requests.Count, Is.EqualTo(0));
            That(presentationEvents.Count, Is.EqualTo(1));
            ref readonly GasPresentationEvent evt = ref presentationEvents.Events[0];
            That(evt.Kind, Is.EqualTo(GasPresentationEventKind.EffectApplied));
            That(evt.Actor, Is.EqualTo(source));
            That(evt.Target, Is.EqualTo(target));
            That(evt.EffectTemplateId, Is.EqualTo(2004));
            That(evt.AttributeId, Is.EqualTo(hpAttrId));
            That(evt.Delta, Is.EqualTo(-15f));
        }

        [Test]
        public void EffectDurationSystem_ExpiresEffects_WithoutCallbacks()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var clock = new DiscreteClock();
                var clocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var lifetime = new EffectLifetimeSystem(
                    world,
                    clock,
                    conditions,
                    snapshotCapacity: TestSnapshotCapacity,
                    fanOutCommandCapacity: TestFanOutCommandCapacity,
                    effectRequests: requests,
                    budget: budget);

                var source = world.Create();
                var target = world.Create();

                for (int i = 0; i < 10; i++)
                {
                    var e = GameplayEffectFactory.CreateEffect(world, rootId: 7, source, target, durationTicks: 0, lifetimeKind: EffectLifetimeKind.After);
                    ref var ge = ref world.Get<GameplayEffect>(e);
                    ge.State = EffectState.Committed;
                }

                lifetime.Update(0.016f);

                // Without callbacks, no EffectRequests should be published
                That(requests.Count, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectApplicationSystem_WhenActiveEffectContainerFull_ThrowsBeforeDroppingEffect()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var app = new EffectApplicationSystem(
                    world,
                    fanOutCommandCapacity: TestFanOutCommandCapacity,
                    clock: new DiscreteClock(),
                    effectRequests: null,
                    budget: budget);

                var source = world.Create();
                var target = world.Create();

                var container = new ActiveEffectContainer();
                for (int i = 0; i < ActiveEffectContainer.CAPACITY; i++)
                {
                    That(container.Add(world.Create()), Is.True);
                }
                world.Add(target, container);

                var effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: 1,
                    source: source,
                    target: target,
                    durationTicks: 60,
                    lifetimeKind: EffectLifetimeKind.After);

                var error = Throws<InvalidOperationException>(() => app.Update(0.016f));

                That(error!.Message, Does.StartWith(EffectApplicationSystem.ActiveEffectContainerCapacityExceededError));
                That(world.IsAlive(effect), Is.True, "overflow attachment must fail explicitly instead of destroying the effect silently");
                That(budget.ActiveEffectContainerAttachDropped, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectApplicationSystem_DoT_UsesActiveEffectContainerForStackMerge_ButDoesNotAggregateModifiers()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var templates = new EffectTemplateRegistry();
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                fanOutCommandCapacity: TestFanOutCommandCapacity,
                clock: new DiscreteClock(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types);
            var application = new EffectApplicationSystem(
                world,
                fanOutCommandCapacity: TestFanOutCommandCapacity,
                clock: new DiscreteClock(),
                effectRequests: requests,
                templates: templates);

            var source = world.Create();
            var target = world.Create(new AttributeBuffer(), new ActiveEffectContainer());

            var modifiers = default(EffectModifiers);
            modifiers.Add(attrId: 0, ModifierOp.Add, -5f);
            templates.Register(2001, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.DoT,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 30,
                PeriodTicks = 10,
                Modifiers = modifiers,
                HasStackPolicy = true,
                StackPolicy = StackPolicy.AddDuration,
                StackOverflowPolicy = StackOverflowPolicy.RejectNew,
                StackLimit = 5,
            });
            var dotPreset = CreateModifierPreset(
                EffectPresetType.DoT,
                ComponentFlags.ModifierParams | ComponentFlags.DurationParams,
                PhaseFlags.OnApply | PhaseFlags.OnPeriod,
                LifetimeFlags.After);
            _ = FinalizeTemplates(templates, in dotPreset);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2001,
            });
            proposal.Update(0f);
            application.Update(0f);

            ref var container = ref world.Get<ActiveEffectContainer>(target);
            That(container.Count, Is.EqualTo(1));

            Entity activeEffect = container.GetEntity(0);
            That(world.IsAlive(activeEffect), Is.True);
            That(world.Has<EffectStack>(activeEffect), Is.True);
            That(world.Get<EffectStack>(activeEffect).Count, Is.EqualTo(1));
            That(world.Get<GameplayEffect>(activeEffect).AggregatesModifiers, Is.False);

            requests.Publish(new EffectRequest
            {
                RootId = 2,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2001,
            });
            proposal.Update(0f);
            application.Update(0f);

            That(container.Count, Is.EqualTo(1), "DoT re-apply should merge into the tracked active effect.");
            That(world.Get<EffectStack>(activeEffect).Count, Is.EqualTo(2));
            That(world.Get<GameplayEffect>(activeEffect).RemainingTicks, Is.EqualTo(60));
        }

        [Test]
        public void EffectPhaseExecutor_DispatchesAllListenerScopesWithoutTruncation()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var eventBus = new GameplayEventBus();
                var globalRegistry = new GlobalPhaseListenerRegistry();

                var executor = new EffectPhaseExecutor(
                    new Ludots.Core.GraphRuntime.GraphProgramRegistry(),
                    new PresetTypeRegistry(),
                    new BuiltinHandlerRegistry(),
                    Ludots.Core.NodeLibraries.GASGraph.GasGraphOpHandlerTable.Instance,
                    new EffectTemplateRegistry(),
                    globalListeners: globalRegistry,
                    eventBus: eventBus,
                    budget: budget);

                var caster = world.Create();
                var target = world.Create();

                var targetBuffer = new EffectPhaseListenerBuffer();
                for (int i = 0; i < EffectPhaseListenerBuffer.CAPACITY; i++)
                {
                    That(targetBuffer.TryAdd(
                        listenTagId: 0,
                        listenEffectId: 0,
                        phase: EffectPhaseId.OnApply,
                        scope: PhaseListenerScope.Target,
                        flags: PhaseListenerActionFlags.PublishEvent,
                        graphProgramId: 0,
                        eventTagId: i + 1,
                        priority: 0,
                        ownerEffectId: 1), Is.True);
                }
                world.Add(target, targetBuffer);

                var sourceBuffer = new EffectPhaseListenerBuffer();
                for (int i = 0; i < EffectPhaseListenerBuffer.CAPACITY; i++)
                {
                    That(sourceBuffer.TryAdd(
                        listenTagId: 0,
                        listenEffectId: 0,
                        phase: EffectPhaseId.OnApply,
                        scope: PhaseListenerScope.Source,
                        flags: PhaseListenerActionFlags.PublishEvent,
                        graphProgramId: 0,
                        eventTagId: 1000 + i + 1,
                        priority: 0,
                        ownerEffectId: 1), Is.True);
                }
                world.Add(caster, sourceBuffer);

                for (int i = 0; i < GlobalPhaseListenerRegistry.MAX_LISTENERS; i++)
                {
                    That(globalRegistry.Register(
                        listenTagId: 0,
                        listenEffectId: 0,
                        phase: EffectPhaseId.OnApply,
                        flags: PhaseListenerActionFlags.PublishEvent,
                        graphProgramId: 0,
                        eventTagId: 2000 + i + 1,
                        priority: 0), Is.True);
                }
                var context = new EffectContext
                {
                    RootId = 0,
                    Source = caster,
                    Target = target,
                    TargetContext = default,
                };
                EffectConfigParams mergedParams = default;

                var api = new GasGraphRuntimeApi(world, eventBus: eventBus);
                using var transaction = new EffectPhaseSideEffectTransaction(
                    world,
                    tagOps: null,
                    effectRequests: null,
                    spawnRequests: null,
                    presentationEvents: null,
                    attributeEntityCapacity: 2);
                transaction.Begin();
                api.BeginEffectSideEffectTransaction(transaction);

                executor.DispatchPhaseListeners(
                    world,
                    api,
                    caster: caster,
                    target: target,
                    targetContext: default,
                    targetPos: default,
                    phase: EffectPhaseId.OnApply,
                    effectTagId: 1,
                    effectTemplateId: 1);
                transaction.Commit();
                api.EndEffectSideEffectTransaction(transaction);

                eventBus.Update();

                That(eventBus.Events.Count, Is.EqualTo(
                    EffectPhaseListenerBuffer.CAPACITY * 2 + GlobalPhaseListenerRegistry.MAX_LISTENERS));
                That(budget.PhaseListenerDispatchDropped, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GameplayEventBus_WhenFull_ThrowsBeforeDroppingEvent()
        {
            var bus = new GameplayEventBus();

            for (int i = 0; i < GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME; i++)
            {
                bus.Publish(new GameplayEvent { TagId = i + 1 });
            }

            var error = Throws<InvalidOperationException>(() =>
                bus.Publish(new GameplayEvent { TagId = GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME + 1 }));

            That(error!.Message, Does.StartWith(GameplayEventBus.CapacityExceededError));
        }

        private static EffectPhaseExecutor FinalizeTemplates(EffectTemplateRegistry templates)
        {
            return FinalizeTemplates(templates, new PresetTypeRegistry());
        }

        private static EffectPhaseExecutor FinalizeTemplates(
            EffectTemplateRegistry templates,
            in PresetTypeDefinition preset)
        {
            var presetTypes = new PresetTypeRegistry();
            presetTypes.Register(in preset);
            return FinalizeTemplates(templates, presetTypes);
        }

        private static EffectPhaseExecutor FinalizeTemplates(
            EffectTemplateRegistry templates,
            PresetTypeRegistry presetTypes)
        {
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            return FinalizeTemplates(templates, presetTypes, builtinHandlers, new GraphProgramRegistry());
        }

        private static EffectPhaseExecutor FinalizeTemplates(
            EffectTemplateRegistry templates,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GraphProgramRegistry graphPrograms)
        {
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                graphPrograms,
                GasGraphOpHandlerTable.Instance,
                "Test/RootBudgetTests.cs");
            return new EffectPhaseExecutor(
                graphPrograms,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
        }

        private static PresetTypeDefinition CreateModifierPreset(
            EffectPresetType type,
            ComponentFlags components,
            PhaseFlags activePhases,
            LifetimeFlags allowedLifetimes)
        {
            var preset = new PresetTypeDefinition
            {
                Type = type,
                Components = components,
                ActivePhases = activePhases,
                AllowedLifetimes = allowedLifetimes,
            };
            if (activePhases.Has(EffectPhaseId.OnApply))
            {
                preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            }
            if (activePhases.Has(EffectPhaseId.OnPeriod))
            {
                preset.DefaultPhaseHandlers[EffectPhaseId.OnPeriod] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            }
            return preset;
        }
    }
}
