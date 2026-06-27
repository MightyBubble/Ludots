using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
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
                var lifetime = new EffectLifetimeSystem(world, clock, conditions, requests, budget);

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
    }
}
