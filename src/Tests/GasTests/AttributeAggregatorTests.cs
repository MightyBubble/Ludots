using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Tests for attribute aggregation with multiple modifiers:
    /// - Add + Multiply + Override stacking
    /// - Multiple modifiers on the same attribute
    /// - Order-of-operations correctness
    /// </summary>
    [TestFixture]
    public class AttributeAggregatorTests
    {
        [Test]
        public unsafe void SingleAdd_ModifiesBase()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 25f);

            var entry = mods.Get(0);
            That(entry.AttributeId, Is.EqualTo(0));
            That(entry.Operation, Is.EqualTo(ModifierOp.Add));
            That(entry.Value, Is.EqualTo(25f));
        }

        [Test]
        public unsafe void MultipleAdds_Stack()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 10f);
            mods.Add(attrId: 0, ModifierOp.Add, 15f);
            mods.Add(attrId: 0, ModifierOp.Add, 5f);

            That(mods.Count, Is.EqualTo(3));

            float sum = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.Operation == ModifierOp.Add && entry.AttributeId == 0)
                    sum += entry.Value;
            }

            That(sum, Is.EqualTo(30f), "Multiple Add modifiers should sum to 30");
        }

        [Test]
        public unsafe void AddAndMultiply_Combined()
        {
            // Base = 100, Add +20, Multiply x1.5 → expected (100+20)*1.5 = 180
            float baseValue = 100f;
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 20f);
            mods.Add(attrId: 0, ModifierOp.Multiply, 1.5f);

            // Simulate aggregation: Add first, then Multiply
            float addSum = 0;
            float mulProduct = 1f;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.Operation == ModifierOp.Add) addSum += entry.Value;
                else if (entry.Operation == ModifierOp.Multiply) mulProduct *= entry.Value;
            }

            float result = (baseValue + addSum) * mulProduct;
            That(result, Is.EqualTo(180f), "Add then Multiply: (100+20)*1.5 = 180");
        }

        [Test]
        public unsafe void Override_TakesLastValue()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 50f);
            mods.Add(attrId: 0, ModifierOp.Override, 42f);

            // Override should discard all Add values and set to 42
            float overrideValue = float.NaN;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.Operation == ModifierOp.Override)
                    overrideValue = entry.Value;
            }

            That(float.IsNaN(overrideValue), Is.False, "Override modifier should be present");
            That(overrideValue, Is.EqualTo(42f), "Override value should be 42");
        }

        [Test]
        public unsafe void DifferentAttributes_Independent()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 10f);  // HP
            mods.Add(attrId: 1, ModifierOp.Add, -5f);   // Mana
            mods.Add(attrId: 0, ModifierOp.Add, 20f);  // HP

            float hpSum = 0;
            float manaSum = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.AttributeId == 0) hpSum += entry.Value;
                else if (entry.AttributeId == 1) manaSum += entry.Value;
            }

            That(hpSum, Is.EqualTo(30f), "HP modifiers should sum independently");
            That(manaSum, Is.EqualTo(-5f), "Mana modifiers should sum independently");
        }

        [Test]
        public unsafe void EffectModifiers_Capacity_IsEight()
        {
            var mods = new EffectModifiers();
            That(EffectModifiers.CAPACITY, Is.EqualTo(8),
                "EffectModifiers should support 8 entries per effect");
        }

        [Test]
        public unsafe void ClampToBaseAttribute_PreservesCurrentAcrossAggregation()
        {
            int healthId = EnsureAttribute("Health");
            AttributeRegistry.SetConstraints(healthId, AttributeRegistry.AttributeConstraints.ClampToBase());

            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            ref var attr = ref world.Get<AttributeBuffer>(entity);
            attr.SetBase(healthId, 100f);
            attr.SetCurrent(healthId, 70f);

            var aggregator = new AttributeAggregatorSystem(world);
            aggregator.Update(0f);

            That(attr.GetCurrent(healthId), Is.EqualTo(70f));
            That(attr.GetBase(healthId), Is.EqualTo(100f));
        }

        [Test]
        public unsafe void ClampToBaseAttribute_TracksAggregatedCapWithoutResettingCurrent()
        {
            int healthId = EnsureAttribute("Health");
            AttributeRegistry.SetConstraints(healthId, AttributeRegistry.AttributeConstraints.ClampToBase());

            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            ref var attr = ref world.Get<AttributeBuffer>(entity);
            attr.SetBase(healthId, 100f);
            attr.SetCurrent(healthId, 70f);

            var effect = world.Create();
            world.Add(effect, new EffectModifiers());
            ref var modifiers = ref world.Get<EffectModifiers>(effect);
            modifiers.Add(healthId, ModifierOp.Add, 25f);

            ref var container = ref world.Get<ActiveEffectContainer>(entity);
            That(container.Add(effect), Is.True);

            var aggregator = new AttributeAggregatorSystem(world);
            aggregator.Update(0f);

            That(attr.GetCurrent(healthId), Is.EqualTo(70f));
            That(attr.GetBase(healthId), Is.EqualTo(125f));
        }

        [Test]
        public unsafe void NonAggregatedAttribute_PreservesCurrentAcrossAggregation()
        {
            int durabilityId = EnsureAttribute("Durability");

            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            ref var attr = ref world.Get<AttributeBuffer>(entity);
            attr.SetBase(durabilityId, 100f);
            attr.SetCurrent(durabilityId, 93f);

            var aggregator = new AttributeAggregatorSystem(world);
            aggregator.Update(0f);

            That(attr.GetCurrent(durabilityId), Is.EqualTo(93f));
            That(attr.GetBase(durabilityId), Is.EqualTo(100f));
        }

        [Test]
        public unsafe void NonAggregatingGameplayEffect_DoesNotModifyEffectiveCap()
        {
            int healthId = EnsureAttribute("Health");
            AttributeRegistry.SetConstraints(healthId, AttributeRegistry.AttributeConstraints.ClampToBase());

            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            ref var attr = ref world.Get<AttributeBuffer>(entity);
            attr.SetBase(healthId, 100f);
            attr.SetCurrent(healthId, 70f);

            var effect = world.Create(
                new GameplayEffect { AggregatesModifiers = false },
                new EffectModifiers());
            ref var modifiers = ref world.Get<EffectModifiers>(effect);
            modifiers.Add(healthId, ModifierOp.Add, 25f);

            ref var container = ref world.Get<ActiveEffectContainer>(entity);
            That(container.Add(effect), Is.True);

            var aggregator = new AttributeAggregatorSystem(world);
            aggregator.Update(0f);

            That(attr.GetCurrent(healthId), Is.EqualTo(70f));
            That(attr.GetBase(healthId), Is.EqualTo(100f));
        }

        [Test]
        public void InstantDamage_PreservesCurrentStateAfterAggregation()
        {
            int healthId = EnsureAttribute("Health");
            AttributeRegistry.SetConstraints(healthId, AttributeRegistry.AttributeConstraints.ClampToBase());

            using var world = World.Create();
            var target = world.Create(new AttributeBuffer());
            ref var attributes = ref world.Get<AttributeBuffer>(target);
            attributes.SetBase(healthId, 100f);
            attributes.SetCurrent(healthId, 60f);

            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(healthId, ModifierOp.Add, -10f);
            templates.Register(1101, new EffectTemplateData
            {
                TagId = 1,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                DurationTicks = 0,
                PeriodTicks = 0,
                Modifiers = modifiers,
            });

            var requests = new EffectRequestQueue();
            var proposal = new EffectProposalProcessingSystem(world, requests, templates: templates);
            var application = new EffectApplicationSystem(world, requests, templates: templates);
            var aggregator = new AttributeAggregatorSystem(world);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = Entity.Null,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 1101,
            });

            proposal.Update(0f);
            application.Update(0f);
            aggregator.Update(0f);

            That(attributes.GetCurrent(healthId), Is.EqualTo(50f));
            That(attributes.GetBase(healthId), Is.EqualTo(100f));
        }

        [Test]
        public void Heal_PreservesCurrentStateAfterAggregation()
        {
            int healthId = EnsureAttribute("Health");
            AttributeRegistry.SetConstraints(healthId, AttributeRegistry.AttributeConstraints.ClampToBase());

            using var world = World.Create();
            var target = world.Create(new AttributeBuffer());
            ref var attributes = ref world.Get<AttributeBuffer>(target);
            attributes.SetBase(healthId, 100f);
            attributes.SetCurrent(healthId, 60f);

            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(healthId, ModifierOp.Add, 15f);
            templates.Register(1102, new EffectTemplateData
            {
                TagId = 2,
                PresetType = EffectPresetType.Heal,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                DurationTicks = 0,
                PeriodTicks = 0,
                Modifiers = modifiers,
            });

            var requests = new EffectRequestQueue();
            var proposal = new EffectProposalProcessingSystem(world, requests, templates: templates);
            var application = new EffectApplicationSystem(world, requests, templates: templates);
            var aggregator = new AttributeAggregatorSystem(world);

            requests.Publish(new EffectRequest
            {
                RootId = 2,
                Source = Entity.Null,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = 1102,
            });

            proposal.Update(0f);
            application.Update(0f);
            aggregator.Update(0f);

            That(attributes.GetCurrent(healthId), Is.EqualTo(75f));
            That(attributes.GetBase(healthId), Is.EqualTo(100f));
        }

        private static int EnsureAttribute(string name)
        {
            int id = AttributeRegistry.GetId(name);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(name);
        }
    }
}
