using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
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
    public class AllocationTests
    {
        [Test]
        public void AbilityActivation_AndProposalProcessing_AllocatesZero()
        {
            var world = World.Create();
            try
            {
                var templates = new EffectTemplateRegistry();
                var requests = new EffectRequestQueue();

                var mods = new EffectModifiers();
                mods.Add(attrId: 0, ModifierOp.Add, -1f);
                templates.Register(1, new EffectTemplateData
                {
                    TagId = 0,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.FixedFrame,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = mods
                });

                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                BuiltinHandlers.RegisterAll(builtinHandlers);
                GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                    templates,
                    presetTypes,
                    builtinHandlers,
                    new GraphProgramRegistry(),
                    "Test/AllocationTests.AbilityActivation.json");

                var abilityTemplate = world.Create();
                world.Add(abilityTemplate, new AbilityTemplate());
                world.Add(abilityTemplate, new AbilityOnActivateEffects());
                world.Add(abilityTemplate, new AbilityExecSpec());
                unsafe
                {
                    ref var onActivate = ref world.Get<AbilityOnActivateEffects>(abilityTemplate);
                    onActivate.Add(1);
                }
                var abilityDefs = new AbilityDefinitionRegistry();
                abilityDefs.RegisterFromEntity(world, abilityTemplate, 5001);

                var caster = world.Create(new AbilityStateBuffer());
                ref var abilityState = ref world.Get<AbilityStateBuffer>(caster);
                abilityState.AddAbility(5001);

                var target = world.Create(new AttributeBuffer(), new DirtyFlags());
                ref var attr = ref world.Get<AttributeBuffer>(target);
                attr.SetCurrent(0, 1000f);

                var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
                var abilitySystem = new AbilitySystem(world, requests, abilityDefs, tagOps);
                var proposalSystem = new EffectProposalProcessingSystem(
                    world,
                    requests,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    budget: null,
                    templates: templates,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: tagOps);

                var args = new AbilitySystem.AbilityActivationArgs(explicitTarget: target);

                for (int i = 0; i < 16; i++)
                {
                    abilitySystem.TryActivateAbility(caster, 0, in args);
                    proposalSystem.Update(0.016f);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                abilitySystem.TryActivateAbility(caster, 0, in args);
                proposalSystem.Update(0.016f);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                GC.GetAllocatedBytesForCurrentThread();
                long before = GC.GetAllocatedBytesForCurrentThread();

                for (int i = 0; i < 10_000; i++)
                {
                    abilitySystem.TryActivateAbility(caster, 0, in args);
                    proposalSystem.Update(0.016f);
                }

                long after = GC.GetAllocatedBytesForCurrentThread();
                That(after - before, Is.LessThanOrEqualTo(64));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ApplyForce_Preset_AndBinding_AllocatesZero()
        {
            using var world = World.Create();

            int fxId = AttributeRegistry.Register("Physics.ForceRequestX");
            int fyId = AttributeRegistry.Register("Physics.ForceRequestY");

            var target = world.Create(new AttributeBuffer(), new DirtyFlags(), new Ludots.Core.Physics.ForceInput2D());
            ref var attr = ref world.Get<AttributeBuffer>(target);
            attr.SetCurrent(fxId, 0f);
            attr.SetCurrent(fyId, 0f);

            var templates = new EffectTemplateRegistry();
            templates.Register(1, new EffectTemplateData
            {
                TagId = 0,
                PresetType = EffectPresetType.ApplyForce2D,
                PresetAttribute0 = fxId,
                PresetAttribute1 = fyId,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 0,
                PeriodTicks = 0,
                ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = default
            });

            var presetTypes = new PresetTypeRegistry();
            var applyForcePreset = new PresetTypeDefinition { Type = EffectPresetType.ApplyForce2D };
            applyForcePreset.DefaultPhaseHandlers[EffectPhaseId.OnApply] =
                PhaseHandler.Builtin(BuiltinHandlerId.ApplyForce);
            presetTypes.Register(in applyForcePreset);
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                new GraphProgramRegistry(),
                "Test/AllocationTests.ApplyForce.json");

            var requests = new EffectRequestQueue();
            var admissionResults = new Ludots.Core.Gameplay.GAS.Orders.OrderAdmissionResultBuffer(4, 4);
            var chainOrders = new Ludots.Core.Gameplay.GAS.Orders.OrderQueue(
                64,
                admissionResults);
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new Ludots.Core.Engine.DiscreteClock(),
                budget: null,
                templates: templates,
                inputRequests: null,
                chainOrders: chainOrders,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            var sinks = new Ludots.Core.Gameplay.GAS.Bindings.AttributeSinkRegistry();
            Ludots.Core.Gameplay.GAS.Bindings.GasAttributeSinks.RegisterBuiltins(sinks);
            var bindings = new Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingRegistry();
            bindings.Set(
                new[]
                {
                    new Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingEntry(fxId, sinkId: 0, channel: 0, mode: Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingMode.Override, resetPolicy: Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingResetPolicy.ResetToZeroPerLogicFrame, scale: 1f),
                    new Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingEntry(fyId, sinkId: 0, channel: 1, mode: Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingMode.Override, resetPolicy: Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingResetPolicy.ResetToZeroPerLogicFrame, scale: 1f)
                },
                new[] { new Ludots.Core.Gameplay.GAS.Bindings.AttributeBindingGroup(sinkId: 0, start: 0, count: 2) }
            );
            var bindingSystem = new Ludots.Core.Gameplay.GAS.Systems.AttributeBindingSystem(world, sinks, bindings);

            for (int i = 0; i < 64; i++)
            {
                admissionResults.BeginLogicStep();
                requests.Publish(new EffectRequest { Target = target, TemplateId = 1 });
                proposal.Update(0.016f);
                bindingSystem.Update(0.016f);
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                admissionResults.BeginLogicStep();
                requests.Publish(new EffectRequest { Target = target, TemplateId = 1 });
                proposal.Update(0.016f);
                bindingSystem.Update(0.016f);
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            That(after - before, Is.LessThanOrEqualTo(64));
        }

        [Test]
        public void RelationSetParent_Transaction_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Entity parentA = world.Create(new ChildrenBuffer());
            Entity parentB = world.Create(new ChildrenBuffer());
            Entity child = world.Create();
            RelationOps.SetParent(world, child, parentA);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 8);

            RunRelationReparentCycles(transaction, child, parentA, parentB, 64);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocated = MeasureRelationReparentAllocations(
                transaction,
                child,
                parentA,
                parentB,
                10_000);

            That(allocated, Is.Zero);
        }

        [Test]
        public void PhaseListenerGraphDispatch_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            const int graphId = 1;
            var programs = new GraphProgramRegistry();
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
            ], GraphKind.Effect);

            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                listenTagId: 0,
                listenEffectId: 0,
                EffectPhaseId.OnApply,
                PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph,
                graphId,
                eventTagId: 0,
                priority: 0,
                ownerEffectId: 1), Is.True);
            world.Add(target, listeners);

            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            var api = new GasGraphRuntimeApi(world);
            EffectPhaseGraphBindings behavior = default;
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 2);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            RunPhaseListenerGraphDispatch(executor, world, api, caster, target, in behavior, 64);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocated = MeasurePhaseListenerGraphDispatchAllocations(
                executor,
                world,
                api,
                caster,
                target,
                in behavior,
                10_000);

            api.EndEffectSideEffectTransaction(transaction);
            transaction.Rollback();
            That(allocated, Is.Zero);
        }

        [Test]
        public void DurationEffectTick_AllocatesZeroAfterWarmup()
        {
            using var world = World.Create();
            const int attrId = 0;
            var clock = new DiscreteClock();
            var requests = new EffectRequestQueue();
            var dirtyQueue = new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME);
            var tagOps = new TagOps(dirtyQueue, new TagRuleRegistry());
            var triggerQueue = new DeferredTriggerQueue();
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                new GraphProgramRegistry(),
                "Test/AllocationTests.DurationEffectTick.json");

            using var application = new EffectApplicationSystem(
                world,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock,
                requests,
                templates: templates,
                tagOps: tagOps);
            using var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps);
            using var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                conditions,
                snapshotCapacity: 4096,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                effectRequests: requests,
                templates: templates,
                tagOps: tagOps);
            using var timedTags = new TimedTagExpirationSystem(world, clock, tagOps);
            using var deferred = new DeferredTriggerCollectionSystem(world, triggerQueue, tagOps, dirtyQueue);

            Entity source = world.Create();
            Entity target = world.Create(
                new AttributeBuffer(),
                new DirtyFlags(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer());
            ref var attributes = ref world.Get<AttributeBuffer>(target);
            attributes.SetBase(attrId, 100f);
            attributes.SetCurrent(attrId, 100f);

            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                source,
                target,
                durationTicks: 100_000,
                lifetimeKind: EffectLifetimeKind.After,
                periodTicks: 0,
                clockId: GasClockId.Step);
            ref var gameplayEffect = ref world.Get<GameplayEffect>(effect);
            gameplayEffect.State = EffectState.Committed;
            gameplayEffect.AggregatesModifiers = true;
            GameplayEffectFactory.AddModifier(world, effect, attrId, ModifierOp.Add, 7f);
            That(world.Get<ActiveEffectContainer>(target).Add(effect), Is.True);

            const int timedTagId = 7;
            ref var tags = ref world.Get<GameplayTagContainer>(target);
            ref var counts = ref world.Get<TagCountContainer>(target);
            ref var timed = ref world.Get<TimedTagBuffer>(target);
            tags.AddTag(timedTagId);
            counts.AddCount(timedTagId, 1);
            That(timed.TryAdd(timedTagId, expireAt: 1_000_000, GasClockId.Step), Is.True);

            TickDurationSystems(clock, application, aggregator, lifetime, timedTags, deferred, triggerQueue, 64);
            That(world.IsAlive(effect), Is.True);
            That(world.Get<AttributeBuffer>(target).GetBase(attrId), Is.EqualTo(100f));
            That(world.Get<AttributeBuffer>(target).GetCurrent(attrId), Is.EqualTo(107f));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocated = MeasureDurationTickAllocations(
                clock,
                application,
                aggregator,
                lifetime,
                timedTags,
                deferred,
                triggerQueue,
                10_000);

            That(world.IsAlive(effect), Is.True);
            That(allocated, Is.LessThanOrEqualTo(64),
                $"Duration-effect tick loop allocated {allocated} bytes after warmup.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasureDurationTickAllocations(
            DiscreteClock clock,
            EffectApplicationSystem application,
            AttributeAggregatorSystem aggregator,
            EffectLifetimeSystem lifetime,
            TimedTagExpirationSystem timedTags,
            DeferredTriggerCollectionSystem deferred,
            DeferredTriggerQueue triggerQueue,
            int count)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            TickDurationSystems(clock, application, aggregator, lifetime, timedTags, deferred, triggerQueue, count);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TickDurationSystems(
            DiscreteClock clock,
            EffectApplicationSystem application,
            AttributeAggregatorSystem aggregator,
            EffectLifetimeSystem lifetime,
            TimedTagExpirationSystem timedTags,
            DeferredTriggerCollectionSystem deferred,
            DeferredTriggerQueue triggerQueue,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.Advance(ClockDomainId.Step, 1);
                application.Update(0f);
                aggregator.Update(0f);
                lifetime.Update(0f);
                timedTags.Update(0f);
                deferred.Update(0f);
                triggerQueue.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasurePhaseListenerGraphDispatchAllocations(
            EffectPhaseExecutor executor,
            World world,
            GasGraphRuntimeApi api,
            Entity caster,
            Entity target,
            in EffectPhaseGraphBindings behavior,
            int count)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunPhaseListenerGraphDispatch(executor, world, api, caster, target, in behavior, count);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunPhaseListenerGraphDispatch(
            EffectPhaseExecutor executor,
            World world,
            GasGraphRuntimeApi api,
            Entity caster,
            Entity target,
            in EffectPhaseGraphBindings behavior,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
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
                    effectTagId: 0,
                    effectTemplateId: 1);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasureRelationReparentAllocations(
            EffectPhaseSideEffectTransaction transaction,
            Entity child,
            Entity parentA,
            Entity parentB,
            int count)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunRelationReparentCycles(transaction, child, parentA, parentB, count);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunRelationReparentCycles(
            EffectPhaseSideEffectTransaction transaction,
            Entity child,
            Entity parentA,
            Entity parentB,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                Entity parent = (i & 1) == 0 ? parentB : parentA;
                transaction.Begin();
                transaction.StageSetParent(child, parent, snapSubjectToParentPosition: false);
                transaction.Commit();
            }
        }
    }
}
