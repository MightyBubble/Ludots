using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Items;
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
        public void AbilityExecSystem_UsesFormOverrideWhenActivating()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;

            var abilities = CreateAbilities(1000);
            var formSlots = new AbilityFormSlotBuffer();
            formSlots.SetOverride(0, 2000);
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                abilities,
                formSlots);

            var execSpec = default(AbilityExecSpec);
            execSpec.ClockId = GasClockId.Step;
            execSpec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: 4001);
            execSpec.SetItem(1, ExecItemKind.End, tick: 0);
            var defs = new AbilityDefinitionRegistry();
            var formDefinition = new AbilityDefinition { ExecSpec = execSpec };
            defs.Register(2000, in formDefinition);

            var requests = new EffectRequestQueue();
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: 8));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                SpatialBlackboardKey = -1,
            }.UseCastAbilityPayload());
            var order = OrderBuilder.CreateCastAbility(
                castAbilityOrderTypeId,
                playerId: 0,
                actor,
                Entity.Null,
                Entity.Null,
                abilitySlotIndex: 0,
                OrderSubmitMode.Immediate,
                submitStep: 0);
            order.OrderId = 1;
            world.Get<OrderBuffer>(actor).SetActiveDirect(in order, priority: 100);
            world.Get<BlackboardIntBuffer>(actor).Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var effectReceipts = new EffectTransactionReceiptBuffer();
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                requests,
                snapshotCapacity: 8,
                abilityDefinitions: defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                orderTypeRegistry: orderTypes,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()),
                effectReceipts: effectReceipts);

            system.Update(0f);

            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(requests[0].TemplateId, Is.EqualTo(4001));
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref readonly var waitingExec = ref world.Get<AbilityExecInstance>(actor);
            effectReceipts.Write(new EffectTransactionReceipt
            {
                RequestId = waitingExec.WaitRequestId,
                Outcome = EffectTransactionOutcome.Succeeded,
                TemplateId = 4001,
            });
            system.Update(0f);
            system.Update(0f);

            Assert.That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
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
