using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Orders;
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
            var system = new AbilityFormRoutingSystem(world, formSets, new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

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
            var itemGrantedSlots = default(ItemGrantedSlotBuffer);
            var grantedSlots = default(GrantedSlotBuffer);

            Assert.That(formSlots.HasOverride(0), Is.True);
            Assert.That(formSlots.HasOverride(1), Is.True);
            Assert.That(AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in itemGrantedSlots, hasItemGranted: false, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId, Is.EqualTo(2000));
            Assert.That(AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in itemGrantedSlots, hasItemGranted: false, in grantedSlots, hasGranted: false, slotIndex: 1).AbilityId, Is.EqualTo(2001));
        }

        [Test]
        public void AbilityFormRoutingSystem_NoMatchingRoute_ClearsPreviousOverrides()
        {
            using var world = World.Create();

            int meleeTagId = TagRegistry.Register("State.Form.Melee");
            var formSets = CreateFormSets(meleeTagId);
            var system = new AbilityFormRoutingSystem(world, formSets, new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

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
        public void AbilitySlotResolver_PrefersGrantedOverItemOverFormOverBase()
        {
            int publicResolveOverloads = 0;
            foreach (var method in typeof(AbilitySlotResolver).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (method.Name == nameof(AbilitySlotResolver.Resolve))
                {
                    publicResolveOverloads++;
                }
            }
            Assert.That(publicResolveOverloads, Is.EqualTo(1),
                "Incomplete resolver overloads would allow production callers to omit the item-granted layer.");

            var baseSlots = CreateAbilities(1000);

            var formSlots = new AbilityFormSlotBuffer();
            formSlots.SetOverride(0, 2000);

            var itemGrantedSlots = new ItemGrantedSlotBuffer();
            itemGrantedSlots.SetOverride(0, 2500, sourceItem: Entity.Null);

            var grantedSlots = new GrantedSlotBuffer();
            grantedSlots.Grant(0, 3000, sourceTagId: 99);

            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: true, in itemGrantedSlots, hasItemGranted: true, in grantedSlots, hasGranted: true, slotIndex: 0).AbilityId,
                Is.EqualTo(3000));
            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: true, in itemGrantedSlots, hasItemGranted: true, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId,
                Is.EqualTo(2500));

            itemGrantedSlots.ClearAll();
            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: true, in itemGrantedSlots, hasItemGranted: true, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId,
                Is.EqualTo(2000));
            Assert.That(
                AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm: false, in itemGrantedSlots, hasItemGranted: false, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId,
                Is.EqualTo(1000));
        }

        [Test]
        public void SkillMappingOverrideResolver_TracksGrantedItemFormAndBasePrecedence()
        {
            using var world = World.Create();
            var baseSlots = CreateAbilities(1000);
            var formSlots = new AbilityFormSlotBuffer();
            formSlots.SetOverride(0, 2000);
            var itemGrantedSlots = new ItemGrantedSlotBuffer();
            itemGrantedSlots.SetOverride(0, 3000, Entity.Null);
            var grantedSlots = new GrantedSlotBuffer();
            grantedSlots.Grant(0, 4000, sourceTagId: 1);
            Entity actor = world.Create(baseSlots, formSlots, itemGrantedSlots, grantedSlots);

            var definitions = new AbilityDefinitionRegistry();
            RegisterInputOverride(definitions, 1000, InputTriggerType.PressedThisFrame);
            RegisterInputOverride(definitions, 2000, InputTriggerType.ReleasedThisFrame);
            RegisterInputOverride(definitions, 3000, InputTriggerType.Held);
            RegisterInputOverride(definitions, 4000, InputTriggerType.DoubleTap);
            var resolver = new LocalOrderSourceHelper.SkillMappingOverrideResolver(world, definitions);
            var mapping = new InputOrderMapping
            {
                ActionId = "Skill1",
                IsSkillMapping = true,
                ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
            };

            Assert.That(resolver.TryResolve(actor, mapping, out InputOrderMapping grantedMapping), Is.True);
            Assert.That(grantedMapping.Trigger, Is.EqualTo(InputTriggerType.DoubleTap));

            for (int i = 0; i < 16; i++)
            {
                resolver.TryResolve(actor, mapping, out _);
            }

            long allocated = MeasureSkillMappingOverrideResolutionAllocations(
                resolver,
                actor,
                mapping,
                out int resolvedCount,
                out int triggerSum);
            Assert.That(resolvedCount, Is.EqualTo(10_000), "Warmed item/granted input override unexpectedly disappeared.");
            Assert.That(triggerSum, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero, "Warmed skill override resolution must not allocate.");

            world.Get<GrantedSlotBuffer>(actor).Revoke(0);
            Assert.That(resolver.TryResolve(actor, mapping, out InputOrderMapping itemMapping), Is.True);
            Assert.That(itemMapping.Trigger, Is.EqualTo(InputTriggerType.Held));

            world.Get<ItemGrantedSlotBuffer>(actor).ClearAll();
            Assert.That(resolver.TryResolve(actor, mapping, out InputOrderMapping formMapping), Is.True);
            Assert.That(formMapping.Trigger, Is.EqualTo(InputTriggerType.ReleasedThisFrame));

            world.Get<AbilityFormSlotBuffer>(actor).Clear(0);
            Assert.That(resolver.TryResolve(actor, mapping, out InputOrderMapping baseMapping), Is.True);
            Assert.That(baseMapping.Trigger, Is.EqualTo(InputTriggerType.PressedThisFrame));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureSkillMappingOverrideResolutionAllocations(
            LocalOrderSourceHelper.SkillMappingOverrideResolver resolver,
            Entity actor,
            InputOrderMapping mapping,
            out int resolvedCount,
            out int triggerSum)
        {
            System.GC.GetAllocatedBytesForCurrentThread();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            int count = 0;
            int sum = 0;
            for (int i = 0; i < 10_000; i++)
            {
                if (resolver.TryResolve(actor, mapping, out InputOrderMapping warmedMapping))
                {
                    count++;
                    sum += (int)warmedMapping.Trigger;
                }
            }

            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            resolvedCount = count;
            triggerSum = sum;
            return allocated;
        }

        private static void RegisterInputOverride(
            AbilityDefinitionRegistry definitions,
            int abilityId,
            InputTriggerType trigger)
        {
            var definition = new AbilityDefinition
            {
                HasInputBindingOverride = true,
                InputBindingOverride = new AbilityInputBindingOverride
                {
                    HasTrigger = true,
                    Trigger = trigger,
                },
            };
            definitions.Register(abilityId, in definition);
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
            var system = new AbilitySystem(world, requests, defs, new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            bool activated = system.TryActivateAbility(actor, 0);

            Assert.That(activated, Is.True);
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].TemplateId, Is.EqualTo(4001));
        }

        private static AbilityFormSetRegistry CreateFormSets(int meleeTagId)
        {
            var requiredAll = default(GameplayTagBitSet);
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
