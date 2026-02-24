using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
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

                var target = world.Create(new AttributeBuffer());
                ref var attr = ref world.Get<AttributeBuffer>(target);
                attr.SetCurrent(0, 1000f);

                var abilitySystem = new AbilitySystem(world, requests, abilityDefs);
                var proposalSystem = new EffectProposalProcessingSystem(world, requests, budget: null, templates: templates);

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

            var target = world.Create(new AttributeBuffer(), new Ludots.Core.Physics.ForceInput2D());
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

            var requests = new EffectRequestQueue();
            var chainOrders = new Ludots.Core.Gameplay.GAS.Orders.OrderQueue();
            var proposal = new EffectProposalProcessingSystem(world, requests, budget: null, templates: templates, inputRequests: null, chainOrders: chainOrders);

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
                chainOrders.TryEnqueue(new Ludots.Core.Gameplay.GAS.Orders.Order { OrderTagId = TestGasOrderTags.ChainPass });
                chainOrders.TryEnqueue(new Ludots.Core.Gameplay.GAS.Orders.Order { OrderTagId = TestGasOrderTags.ChainPass });
                requests.Publish(new EffectRequest { Target = target, TemplateId = 1 });
                proposal.Update(0.016f);
                bindingSystem.Update(0.016f);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                chainOrders.TryEnqueue(new Ludots.Core.Gameplay.GAS.Orders.Order { OrderTagId = TestGasOrderTags.ChainPass });
                chainOrders.TryEnqueue(new Ludots.Core.Gameplay.GAS.Orders.Order { OrderTagId = TestGasOrderTags.ChainPass });
                requests.Publish(new EffectRequest { Target = target, TemplateId = 1 });
                proposal.Update(0.016f);
                bindingSystem.Update(0.016f);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            That(after - before, Is.LessThanOrEqualTo(64));
        }
    }
}
