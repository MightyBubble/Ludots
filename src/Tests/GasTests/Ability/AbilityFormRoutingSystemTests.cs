using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AbilityFormRoutingSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            AbilityFormSetIdRegistry.Clear();
        }

        [Test]
        public void AbilityFormRoutingSystem_MatchingRoute_AppliesFormOverrides()
        {
            using var world = World.Create();

            int meleeTagId = TagRegistry.Register("State.Form.Melee");
            var formSets = CreateFormSets(meleeTagId);
            var system = new AbilityFormRoutingSystem(world, formSets);

            var actor = world.Create(
                CreateAbilities(1000, 1001),
                new GameplayTagContainer(),
                new AbilityFormSetRef { FormSetId = 1 },
                new AbilityFormSlotBuffer());
            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            tags.AddTag(meleeTagId);

            system.Update(0f);

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            ref var formSlots = ref world.Get<AbilityFormSlotBuffer>(actor);
            var grantedSlots = default(GrantedSlotBuffer);

            Assert.That(formSlots.HasOverride(0), Is.True);
            Assert.That(formSlots.HasOverride(1), Is.True);
            Assert.That(AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId, Is.EqualTo(2000));
            Assert.That(AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 1).AbilityId, Is.EqualTo(2001));
        }

        [Test]
        public void AbilityFormRoutingSystem_NoMatchingRoute_ClearsPreviousOverrides()
        {
            using var world = World.Create();

            int meleeTagId = TagRegistry.Register("State.Form.Melee");
            var formSets = CreateFormSets(meleeTagId);
            var system = new AbilityFormRoutingSystem(world, formSets);

            var actor = world.Create(
                CreateAbilities(1000, 1001),
                new GameplayTagContainer(),
                new AbilityFormSetRef { FormSetId = 1 },
                new AbilityFormSlotBuffer());
            ref var tags = ref world.Get<GameplayTagContainer>(actor);

            tags.AddTag(meleeTagId);
            system.Update(0f);
            Assert.That(world.Get<AbilityFormSlotBuffer>(actor).HasOverride(0), Is.True);

            tags.RemoveTag(meleeTagId);
            system.Update(0f);

            Assert.That(world.Get<AbilityFormSlotBuffer>(actor).HasOverride(0), Is.False);
            Assert.That(world.Get<AbilityFormSlotBuffer>(actor).HasOverride(1), Is.False);
        }

        [Test]
        public void AbilitySlotResolver_PrefersGrantedOverFormOverBase()
        {
            var baseSlots = CreateAbilities(1000);

            var formSlots = new AbilityFormSlotBuffer();
            formSlots.SetOverride(0, 2000);

            var grantedSlots = new GrantedSlotBuffer();
            grantedSlots.Grant(0, 3000, sourceTagId: 99);

            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: true, in grantedSlots, hasGranted: true, slotIndex: 0).AbilityId,
                Is.EqualTo(3000));
            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId,
                Is.EqualTo(2000));
            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: false, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId,
                Is.EqualTo(1000));
        }

        [Test]
        public void AbilitySystem_UsesFormOverrideWhenActivating()
        {
            using var world = World.Create();

            var abilities = CreateAbilities(1000);
            var formSlots = new AbilityFormSlotBuffer();
            formSlots.SetOverride(0, 2000);
            var actor = world.Create(abilities, formSlots);

            var effects = new AbilityOnActivateEffects();
            effects.Add(4001);

            var defs = new AbilityDefinitionRegistry();
            var formDefinition = new AbilityDefinition
            {
                HasOnActivateEffects = true,
                OnActivateEffects = effects
            };
            defs.Register(2000, in formDefinition);

            var requests = new EffectRequestQueue();
            var system = new AbilitySystem(world, requests, defs);

            bool activated = system.TryActivateAbility(actor, 0);

            Assert.That(activated, Is.True);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].TemplateId, Is.EqualTo(4001));
        }

        private static AbilityFormSetRegistry CreateFormSets(int meleeTagId)
        {
            var requiredAll = default(GameplayTagContainer);
            requiredAll.AddTag(meleeTagId);

            var formSets = new AbilityFormSetRegistry();
            formSets.Register(1, new AbilityFormSetDefinition(new[]
            {
                new AbilityFormRouteDefinition(
                    requiredAll,
                    default,
                    priority: 100,
                    new[]
                    {
                        new AbilityFormSlotOverride(0, 2000),
                        new AbilityFormSlotOverride(1, 2001)
                    })
            }));
            return formSets;
        }

        private static AbilityStateBuffer CreateAbilities(params int[] abilityIds)
        {
            var abilities = new AbilityStateBuffer();
            for (int i = 0; i < abilityIds.Length; i++)
            {
                abilities.AddAbility(abilityIds[i]);
            }

            return abilities;
        }
    }
}
