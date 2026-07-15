using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Spawning;
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
                var app = new EffectApplicationSystem(world, requests, budget);

                var source = world.Create();
                var target = world.Create();

                for (int i = 0; i < 10; i++)
                {
                    GameplayEffectFactory.CreateEffect(world, rootId: 1, source, target, durationTicks: 0, lifetimeKind: EffectLifetimeKind.Instant);
                }

                app.Update(0.016f);

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
            var modifiers = default(EffectModifiers);
            modifiers.Add(attrId: 0, ModifierOp.Add, -15f);
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
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in preset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var phaseExecutor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var application = new EffectApplicationSystem(
                world,
                effectRequests: null,
                budget: null,
                presentationEvents,
                templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(0, 100f);
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 0,
                lifetimeKind: EffectLifetimeKind.Instant);
            world.Get<EffectModifiers>(effect) = modifiers;
            world.Add(effect, new EffectTemplateRef { TemplateId = 2002 });

            application.Update(0.016f);

            That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(85f));
            That(presentationEvents.Count, Is.EqualTo(1));
            ref readonly GasPresentationEvent evt = ref presentationEvents.Events[0];
            That(evt.Kind, Is.EqualTo(GasPresentationEventKind.EffectApplied));
            That(evt.Actor, Is.EqualTo(source));
            That(evt.Target, Is.EqualTo(target));
            That(evt.EffectTemplateId, Is.EqualTo(2002));
            That(evt.AttributeId, Is.EqualTo(0));
            That(evt.Delta, Is.EqualTo(-15f));
        }

        [Test]
        public void EffectProposalProcessingSystem_PureInstantPublishesEffectApplied()
        {
            using var world = World.Create();

            var requests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(attrId: 0, ModifierOp.Add, -15f);
            templates.Register(2003, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                Modifiers = modifiers,
            });

            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                templates: templates,
                presentationEvents: presentationEvents,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)));

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(0, 100f);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2003,
            });

            proposal.Update(0.016f);

            That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(85f));
            That(presentationEvents.Count, Is.EqualTo(1));
            ref readonly GasPresentationEvent evt = ref presentationEvents.Events[0];
            That(evt.Kind, Is.EqualTo(GasPresentationEventKind.EffectApplied));
            That(evt.Actor, Is.EqualTo(source));
            That(evt.Target, Is.EqualTo(target));
            That(evt.EffectTemplateId, Is.EqualTo(2003));
            That(evt.AttributeId, Is.EqualTo(0));
            That(evt.Delta, Is.EqualTo(-15f));
        }

        [Test]
        public void EffectProcessingLoopSystem_ParticipatingInstantPublishesEffectApplied()
        {
            using var world = World.Create();

            var requests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(attrId: 0, ModifierOp.Add, -15f);
            templates.Register(2004, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                ParticipatesInResponse = true,
                Modifiers = modifiers,
            });

            var loop = new EffectProcessingLoopSystem(
                world,
                requests,
                new DiscreteClock(),
                new GasConditionRegistry(),
                lifetimeSnapshotCapacity: 16384,
                budget: null,
                templates: templates,
                inputRequests: null,
                chainOrders: null,
                telemetry: new ResponseChainTelemetryBuffer(),
                orderRequests: new OrderRequestQueue(),
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                presentationEvents: presentationEvents,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)));

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(0, 100f);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2004,
            });

            loop.Update(0.016f);

            That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(85f));
            That(requests.Count, Is.EqualTo(0));
            That(presentationEvents.Count, Is.EqualTo(1));
            ref readonly GasPresentationEvent evt = ref presentationEvents.Events[0];
            That(evt.Kind, Is.EqualTo(GasPresentationEventKind.EffectApplied));
            That(evt.Actor, Is.EqualTo(source));
            That(evt.Target, Is.EqualTo(target));
            That(evt.EffectTemplateId, Is.EqualTo(2004));
            That(evt.AttributeId, Is.EqualTo(0));
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
                var lifetime = new EffectLifetimeSystem(world, clock, conditions, snapshotCapacity: 4096, effectRequests: requests, budget: budget);

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
        public void EffectApplicationSystem_WhenActiveEffectContainerFull_TracksDroppedInBudget()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var app = new EffectApplicationSystem(world, effectRequests: null, budget: budget);

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

                app.Update(0.016f);

                That(world.IsAlive(effect), Is.False, "overflow attachment should drop and destroy effect");
                That(budget.ActiveEffectContainerAttachDropped, Is.EqualTo(1));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectApplicationSystem_WhenPersistentAttachFails_DoesNotRunOnApplyOrPublishActivation()
        {
            using var world = World.Create();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(attrId: 0, ModifierOp.Add, -15f);
            templates.Register(2003, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                Modifiers = modifiers,
            });

            var phaseExecutor = CreateBuffOnApplyModifierExecutor(templates);
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var application = new EffectApplicationSystem(
                world,
                effectRequests: null,
                budget: new GasBudget(),
                presentationEvents,
                templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<AttributeBuffer>(target).SetBase(0, 100f);
            ref ActiveEffectContainer container = ref world.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < ActiveEffectContainer.CAPACITY; i++)
            {
                Assert.That(container.Add(world.Create()), Is.True);
            }

            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Get<EffectModifiers>(effect) = modifiers;
            world.Add(effect, new EffectTemplateRef { TemplateId = 2003 });

            application.Update(0.016f);

            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(100f));
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(ActiveEffectContainer.CAPACITY));
            Assert.That(presentationEvents.Count, Is.Zero);
        }

        [Test]
        public void EffectProposalProcessingSystem_WhenStackTagCommitFails_RestoresStackAndDuration()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var templates = new EffectTemplateRegistry();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                presentationEvents: presentationEvents,
                tagOps: null);

            var grantedTags = default(EffectGrantedTags);
            grantedTags.Add(new TagContribution
            {
                TagId = 10,
                Formula = TagContributionFormula.Linear,
                Amount = 1,
            });
            templates.Register(2004, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                HasStackPolicy = true,
                StackPolicy = StackPolicy.RefreshDuration,
                StackOverflowPolicy = StackOverflowPolicy.RejectNew,
                StackLimit = 5,
                GrantedTags = grantedTags,
            });

            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            TagStateInstaller.EnsureInstalled(world, target);
            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(target);
            ref TagCountContainer counts = ref world.Get<TagCountContainer>(target);
            tags.AddTag(10);
            counts.AddCount(10);

            Entity existing = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 25,
                lifetimeKind: EffectLifetimeKind.After);
            ref GameplayEffect existingEffect = ref world.Get<GameplayEffect>(existing);
            existingEffect.State = EffectState.Committed;
            existingEffect.RemainingTicks = 25;
            existingEffect.ExpiresAtTick = 99;
            world.Add(existing, new EffectTemplateRef { TemplateId = 2004 });
            world.Add(existing, new EffectStack
            {
                Count = 1,
                Limit = 5,
                Policy = StackPolicy.RefreshDuration,
                OverflowPolicy = StackOverflowPolicy.RejectNew,
            });
            world.Add(existing, grantedTags);
            Assert.That(world.Get<ActiveEffectContainer>(target).Add(existing), Is.True);

            requests.Publish(new EffectRequest
            {
                RootId = 2,
                Source = source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 2004,
            });

            var error = Assert.Throws<InvalidOperationException>(() => proposal.Update(0f));

            Assert.That(error!.Message, Is.EqualTo(TagOps.MissingTagOpsError));
            Assert.That(world.Get<EffectStack>(existing).Count, Is.EqualTo(1));
            Assert.That(world.Get<GameplayEffect>(existing).RemainingTicks, Is.EqualTo(25));
            Assert.That(world.Get<GameplayEffect>(existing).ExpiresAtTick, Is.EqualTo(99));
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(10), Is.True);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(10), Is.EqualTo(1));
            Assert.That(world.Get<DirtyFlags>(target).IsTagDirty(10), Is.False);
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));
            Assert.That(presentationEvents.Count, Is.Zero);
        }

        [Test]
        public void EffectApplicationSystem_WhenTargetDiesBeforeDeferredAttach_DestroysEffectWithoutActivation()
        {
            using var world = World.Create();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            templates.Register(2005, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
            });
            var application = new EffectApplicationSystem(
                world,
                presentationEvents: presentationEvents,
                templates: templates)
            {
                MaxWorkUnitsPerSlice = 1,
            };

            Entity source = world.Create();
            Entity target = world.Create();
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Add(effect, new EffectTemplateRef { TemplateId = 2005 });

            Assert.That(application.UpdateSlice(0f, timeBudgetMs: 0), Is.False);
            world.Destroy(target);
            while (!application.UpdateSlice(0f, timeBudgetMs: 0)) { }

            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(presentationEvents.Count, Is.Zero);
        }

        [Test]
        public void EffectApplicationSystem_WhenTagCommitFails_RollsBackAttachmentBeforeRunningPhases()
        {
            using var world = World.Create();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(attrId: 0, ModifierOp.Add, -15f);
            templates.Register(2006, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                Modifiers = modifiers,
            });

            var phaseExecutor = CreateBuffOnApplyModifierExecutor(templates);
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
            var application = new EffectApplicationSystem(
                world,
                presentationEvents: presentationEvents,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: null);

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer());
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<AttributeBuffer>(target).SetBase(0, 100f);
            var grantedTags = default(EffectGrantedTags);
            grantedTags.Add(new TagContribution
            {
                TagId = 20,
                Formula = TagContributionFormula.Fixed,
                Amount = 1,
            });

            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Get<EffectModifiers>(effect) = modifiers;
            world.Add(effect, new EffectTemplateRef { TemplateId = 2006 });
            world.Add(effect, grantedTags);

            var error = Assert.Throws<InvalidOperationException>(() => application.Update(0f));

            Assert.That(error!.Message, Is.EqualTo(TagOps.MissingTagOpsError));
            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);
            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(100f));
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(20), Is.False);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(20), Is.Zero);
            Assert.That(world.Get<DirtyFlags>(target).IsTagDirty(20), Is.False);
            Assert.That(presentationEvents.Count, Is.Zero);
        }

        [Test]
        public void EffectApplicationSystem_WhenPersistentPhaseThrows_RestoresAttachmentTagsAndGraphContext()
        {
            using var world = World.Create();
            var templates = new EffectTemplateRegistry();
            var phaseBindings = new EffectPhaseGraphBindings();
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnResolve, PhaseSlot.Pre, 9_999), Is.True);
            var configParams = new EffectConfigParams();
            Assert.That(configParams.TryAddInt(keyId: 77, value: 99), Is.True);
            templates.Register(2007, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                PhaseGraphBindings = phaseBindings,
                ConfigParams = configParams,
            });

            var phaseExecutor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                templates);
            var dirtyEntities = new DirtyEntityQueue(capacity: 8);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            var application = new EffectApplicationSystem(
                world,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create();
            TagStateInstaller.EnsureInstalled(world, target);
            var grantedTags = default(EffectGrantedTags);
            grantedTags.Add(new TagContribution
            {
                TagId = 20,
                Formula = TagContributionFormula.Fixed,
                Amount = 1,
            });
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Add(effect, new EffectTemplateRef { TemplateId = 2007 });
            world.Add(effect, grantedTags);

            Assert.Throws<InvalidOperationException>(() => application.Update(0f));

            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(20), Is.False);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(20), Is.Zero);
            Assert.That(world.Get<DirtyFlags>(target).IsTagDirty(20), Is.False);
            Assert.That(dirtyEntities.Count, Is.Zero);
            Assert.That(graphApi.TryLoadConfigInt(77, out _), Is.False);
        }

        [Test]
        public void EffectApplicationSystem_WhenLaterPersistentPhaseThrows_DiscardsEarlierAttributeAndChildEffectSideEffects()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            const int onResolveGraphId = 7_001;
            const int onHitGraphId = 7_002;
            const int parentTemplateId = 2_008;
            const int childTemplateId = 2_009;
            const int healthAttributeId = 0;

            programs.Register(onResolveGraphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ModifyAttributeAdd,
                    A = 0,
                    B = 0,
                    Imm = healthAttributeId,
                },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ApplyEffectTemplate,
                    A = 0,
                    Imm = childTemplateId,
                },
            });
            programs.Register(onHitGraphId, new[]
            {
                new GraphInstruction { Op = ushort.MaxValue },
            });

            var phaseBindings = new EffectPhaseGraphBindings();
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnResolve, PhaseSlot.Pre, onResolveGraphId), Is.True);
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnHit, PhaseSlot.Pre, onHitGraphId), Is.True);
            templates.Register(parentTemplateId, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                PhaseGraphBindings = phaseBindings,
            });

            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                templates);
            var dirtyEntities = new DirtyEntityQueue(capacity: 8);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(world, effectRequests: requests, tagOps: tagOps);
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var application = new EffectApplicationSystem(
                world,
                effectRequests: requests,
                presentationEvents: presentationEvents,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer());
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<AttributeBuffer>(target).SetBase(healthAttributeId, 100f);
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Add(effect, new EffectTemplateRef { TemplateId = parentTemplateId });

            Assert.Throws<InvalidOperationException>(() => application.Update(0f));

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthAttributeId), Is.EqualTo(100f));
            Assert.That(requests.Count, Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);
            Assert.That(dirtyEntities.Count, Is.Zero);
            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);
        }

        [Test]
        public void EffectApplicationSystem_WhenLaterPersistentPhaseThrows_DiscardsBuiltinModifierAndProjectile()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var spawnRequests = new RuntimeEntitySpawnQueue(capacity: 16);
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            const int failingGraphId = 7_003;
            const int parentTemplateId = 2_010;
            const int healthAttributeId = 0;

            programs.Register(failingGraphId, new[]
            {
                new GraphInstruction { Op = ushort.MaxValue },
            });
            var preset = new PresetTypeDefinition { Type = EffectPresetType.Buff };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnResolve] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            preset.DefaultPhaseHandlers[EffectPhaseId.OnHit] = PhaseHandler.Builtin(BuiltinHandlerId.CreateProjectile);
            presetTypes.Register(in preset);

            var phaseBindings = new EffectPhaseGraphBindings();
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, failingGraphId), Is.True);
            var modifiers = default(EffectModifiers);
            Assert.That(modifiers.Add(healthAttributeId, ModifierOp.Add, -30f), Is.True);
            templates.Register(parentTemplateId, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                Modifiers = modifiers,
                PhaseGraphBindings = phaseBindings,
                Projectile = new ProjectileDescriptor
                {
                    Speed = 1_000,
                    Range = 1_200,
                    HitEffectTemplateId = 88,
                    TravelMode = ProjectileTravelMode.Direction,
                    ImpactPolicy = ProjectileImpactPolicy.DestroyOnFirstHit,
                    CollisionHalfWidthCm = 10,
                    CollisionRelationFilter = Ludots.Core.Gameplay.Teams.RelationshipFilter.All,
                    CollisionExcludeSource = true,
                    MaxHitCount = 1,
                },
            });

            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
            var dirtyEntities = new DirtyEntityQueue(capacity: 8);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(world, effectRequests: requests, tagOps: tagOps);
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var application = new EffectApplicationSystem(
                world,
                effectRequests: requests,
                presentationEvents: presentationEvents,
                templates: templates,
                spawnRequests: spawnRequests,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(0, 0));
            Entity target = world.Create(
                new AttributeBuffer(),
                Ludots.Core.Components.WorldPositionCm.FromCm(100, 0));
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<AttributeBuffer>(target).SetBase(healthAttributeId, 100f);
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Get<EffectModifiers>(effect) = modifiers;
            world.Add(effect, new EffectTemplateRef { TemplateId = parentTemplateId });

            Assert.Throws<InvalidOperationException>(() => application.Update(0f));

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthAttributeId), Is.EqualTo(100f));
            Assert.That(spawnRequests.Count, Is.Zero);
            Assert.That(requests.Count, Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);
            Assert.That(dirtyEntities.Count, Is.Zero);
            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);
        }

        [Test]
        public void EffectApplicationSystem_WhenLaterPersistentPhaseThrows_DiscardsEventBlackboardAndCancellation()
        {
            using var world = World.Create();
            var eventBus = new GameplayEventBus();
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            const int onResolveGraphId = 7_004;
            const int onHitGraphId = 7_005;
            const int parentTemplateId = 2_011;
            const int existingTemplateId = 2_012;
            const int blackboardKeyId = 41;

            programs.Register(onResolveGraphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 99 },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.WriteBlackboardInt,
                    A = 0,
                    B = 0,
                    Imm = blackboardKeyId,
                },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 12f },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.SendEvent,
                    A = 0,
                    B = 0,
                    Imm = 77,
                },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.RemoveEffectTemplate,
                    A = 0,
                    Imm = existingTemplateId,
                },
            });
            programs.Register(onHitGraphId, new[]
            {
                new GraphInstruction { Op = ushort.MaxValue },
            });

            var phaseBindings = new EffectPhaseGraphBindings();
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnResolve, PhaseSlot.Pre, onResolveGraphId), Is.True);
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnHit, PhaseSlot.Pre, onHitGraphId), Is.True);
            templates.Register(parentTemplateId, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                PhaseGraphBindings = phaseBindings,
            });

            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                templates,
                eventBus: eventBus);
            var dirtyEntities = new DirtyEntityQueue(capacity: 8);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(world, eventBus: eventBus, tagOps: tagOps);
            var application = new EffectApplicationSystem(
                world,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create(new BlackboardIntBuffer(), new ActiveEffectContainer());
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<BlackboardIntBuffer>(target).Set(blackboardKeyId, 7);

            Entity existing = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Get<GameplayEffect>(existing).State = EffectState.Committed;
            world.Add(existing, new EffectTemplateRef { TemplateId = existingTemplateId });
            Assert.That(world.Get<ActiveEffectContainer>(target).Add(existing), Is.True);

            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 2,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Add(effect, new EffectTemplateRef { TemplateId = parentTemplateId });

            Assert.Throws<InvalidOperationException>(() => application.Update(0f));
            eventBus.Update();

            Assert.That(world.Get<BlackboardIntBuffer>(target).TryGet(blackboardKeyId, out int blackboardValue), Is.True);
            Assert.That(blackboardValue, Is.EqualTo(7));
            Assert.That(world.Get<GameplayEffect>(existing).CancelRequested, Is.False);
            Assert.That(eventBus.Events.Count, Is.Zero);
            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));
        }

        [Test]
        public void EffectApplicationSystem_WhenPersistentPhasesSucceed_CommitsEverySideEffectExactlyOnce()
        {
            using var world = World.Create();
            var eventBus = new GameplayEventBus();
            var requests = new EffectRequestQueue();
            var spawnRequests = new RuntimeEntitySpawnQueue(capacity: 16);
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            const int onResolveGraphId = 7_006;
            const int parentTemplateId = 2_013;
            const int existingTemplateId = 2_014;
            const int childTemplateId = 2_015;
            const int blackboardKeyId = 42;
            const int healthAttributeId = 0;

            programs.Register(onResolveGraphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 99 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardInt, A = 0, B = 0, Imm = blackboardKeyId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 12f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 0, B = 0, Imm = 78 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = -5f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = healthAttributeId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 0, Imm = childTemplateId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.RemoveEffectTemplate, A = 0, Imm = existingTemplateId },
            });

            var preset = new PresetTypeDefinition { Type = EffectPresetType.Buff };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnHit] = PhaseHandler.Builtin(BuiltinHandlerId.CreateProjectile);
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in preset);

            var phaseBindings = new EffectPhaseGraphBindings();
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnResolve, PhaseSlot.Pre, onResolveGraphId), Is.True);
            var modifiers = default(EffectModifiers);
            Assert.That(modifiers.Add(healthAttributeId, ModifierOp.Add, -30f), Is.True);
            var listeners = default(EffectPhaseListenerBuffer);
            Assert.That(listeners.TryAddTemplate(
                listenTagId: 0,
                listenEffectId: 0,
                EffectPhaseId.OnHit,
                PhaseListenerScope.Target,
                PhaseListenerActionFlags.PublishEvent,
                graphProgramId: 0,
                eventTagId: 79,
                priority: 1), Is.True);
            templates.Register(parentTemplateId, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                Modifiers = modifiers,
                PhaseGraphBindings = phaseBindings,
                ListenerSetup = listeners,
                Projectile = new ProjectileDescriptor
                {
                    Speed = 1_000,
                    Range = 1_200,
                    HitEffectTemplateId = 88,
                    TravelMode = ProjectileTravelMode.Direction,
                    ImpactPolicy = ProjectileImpactPolicy.DestroyOnFirstHit,
                    CollisionHalfWidthCm = 10,
                    CollisionRelationFilter = Ludots.Core.Gameplay.Teams.RelationshipFilter.All,
                    CollisionExcludeSource = true,
                    MaxHitCount = 1,
                },
            });

            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates,
                eventBus: eventBus);
            var dirtyEntities = new DirtyEntityQueue(capacity: 8);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(
                world,
                eventBus: eventBus,
                effectRequests: requests,
                tagOps: tagOps);
            var application = new EffectApplicationSystem(
                world,
                effectRequests: requests,
                presentationEvents: presentationEvents,
                templates: templates,
                spawnRequests: spawnRequests,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(0, 0));
            Entity target = world.Create(
                new AttributeBuffer(),
                new BlackboardIntBuffer(),
                new ActiveEffectContainer(),
                Ludots.Core.Components.WorldPositionCm.FromCm(100, 0));
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<AttributeBuffer>(target).SetBase(healthAttributeId, 100f);

            Entity existing = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Get<GameplayEffect>(existing).State = EffectState.Committed;
            world.Add(existing, new EffectTemplateRef { TemplateId = existingTemplateId });
            Assert.That(world.Get<ActiveEffectContainer>(target).Add(existing), Is.True);

            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 2,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            ref GameplayEffect effectState = ref world.Get<GameplayEffect>(effect);
            effectState.AggregatesModifiers = true;
            world.Get<EffectModifiers>(effect) = modifiers;
            world.Add(effect, new EffectTemplateRef { TemplateId = parentTemplateId });

            application.Update(0f);
            eventBus.Update();

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthAttributeId), Is.EqualTo(65f));
            Assert.That(world.Get<BlackboardIntBuffer>(target).TryGet(blackboardKeyId, out int blackboardValue), Is.True);
            Assert.That(blackboardValue, Is.EqualTo(99));
            Assert.That(world.Get<GameplayEffect>(existing).CancelRequested, Is.True);
            Assert.That(world.Get<GameplayEffect>(effect).State, Is.EqualTo(EffectState.Committed));
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(2));
            Assert.That(world.Get<EffectPhaseListenerBuffer>(target).Count, Is.EqualTo(1));
            Assert.That(world.Has<AttributeAggregateDirty>(target), Is.True);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].TemplateId, Is.EqualTo(childTemplateId));
            Assert.That(spawnRequests.Count, Is.EqualTo(1));
            Assert.That(presentationEvents.Count, Is.EqualTo(2));
            Assert.That(eventBus.Events.Count, Is.EqualTo(1));
            Assert.That(eventBus.Events[0].TagId, Is.EqualTo(78));
            Assert.That(dirtyEntities.Count, Is.EqualTo(1));
        }

        [Test]
        public void EffectApplicationSystem_WhenPersistentCommitCapacityIsUnavailable_RestoresWorldAndQueues()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(capacity: 1);
            presentationEvents.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.CastStarted });
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            const int graphId = 7_007;
            const int parentTemplateId = 2_016;
            const int childTemplateId = 2_017;

            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 0, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 0, Imm = childTemplateId },
            });
            var phaseBindings = new EffectPhaseGraphBindings();
            Assert.That(phaseBindings.TryAddStep(EffectPhaseId.OnResolve, PhaseSlot.Pre, graphId), Is.True);
            var grantedTags = default(EffectGrantedTags);
            Assert.That(grantedTags.Add(new TagContribution
            {
                TagId = 20,
                Formula = TagContributionFormula.Fixed,
                Amount = 1,
            }), Is.True);
            templates.Register(parentTemplateId, new EffectTemplateData
            {
                TagId = 10,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                DurationTicks = 60,
                PhaseGraphBindings = phaseBindings,
                GrantedTags = grantedTags,
            });

            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                templates);
            var dirtyEntities = new DirtyEntityQueue(capacity: 8);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var graphApi = new GasGraphRuntimeApi(world, effectRequests: requests, tagOps: tagOps);
            var application = new EffectApplicationSystem(
                world,
                effectRequests: requests,
                presentationEvents: presentationEvents,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer());
            TagStateInstaller.EnsureInstalled(world, target);
            world.Get<AttributeBuffer>(target).SetBase(0, 100f);
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 60,
                lifetimeKind: EffectLifetimeKind.After);
            world.Add(effect, new EffectTemplateRef { TemplateId = parentTemplateId });
            world.Add(effect, grantedTags);

            var error = Assert.Throws<InvalidOperationException>(() => application.Update(0f));

            Assert.That(error!.Message, Does.StartWith(EffectPhaseSideEffectTransaction.CapacityExceededError));
            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(100f));
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(20), Is.False);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(20), Is.Zero);
            Assert.That(world.Get<DirtyFlags>(target).IsTagDirty(20), Is.False);
            Assert.That(requests.Count, Is.Zero);
            Assert.That(presentationEvents.Count, Is.EqualTo(1));
            Assert.That(dirtyEntities.Count, Is.Zero);
            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);
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
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types);
            var application = new EffectApplicationSystem(world, requests, templates: templates);

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
        public void EffectPhaseExecutor_WhenListenerCollectionTruncates_TracksDroppedInBudget()
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

                executor.DispatchPhaseListeners(
                    world,
                    api: null!,
                    caster: caster,
                    target: target,
                    targetContext: default,
                    targetPos: default,
                    phase: EffectPhaseId.OnApply,
                    effectTagId: 1,
                    effectTemplateId: 1);

                That(budget.PhaseListenerDispatchDropped, Is.EqualTo(16));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void GameplayEventDispatchSystem_WhenBusOverflows_TracksDroppedInBudget()
        {
            var bus = new GameplayEventBus();
            var budget = new GasBudget();
            var dispatch = new GameplayEventDispatchSystem(bus, budget);

            for (int i = 0; i < GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME + 7; i++)
            {
                bus.Publish(new GameplayEvent { TagId = i + 1 });
            }

            dispatch.Update(0.016f);

            That(budget.GameplayEventBusDropped, Is.EqualTo(7));
        }

        private static EffectPhaseExecutor CreateBuffOnApplyModifierExecutor(EffectTemplateRegistry templates)
        {
            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition
            {
                Type = EffectPresetType.Buff,
                Components = ComponentFlags.ModifierParams | ComponentFlags.DurationParams,
                ActivePhases = PhaseFlags.OnApply,
                AllowedLifetimes = LifetimeFlags.Duration,
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in preset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            return new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
        }
    }
}
