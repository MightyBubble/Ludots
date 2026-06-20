using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using MobaDemoMod.GAS;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Navigation2D.Components;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests covering the features introduced by the Input/Order/Ability audit:
    ///   - OrderBuffer PendingBuffer (SetPending, ClearPending, ExpirePending)
    ///   - GrantedSlotBuffer + AbilitySlotResolver
    ///   - AbilityToggleSpec registration
    ///   - GraphExecutor.ExecuteValidation
    /// </summary>
    [TestFixture]
    public class InputOrderAbilityAuditTests
    {
        // ════════════════════════════════════════════════════════════════════
        // Region: OrderBuffer �?PendingBuffer
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PendingBuffer_SetPending_StoresOrderCorrectly()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 42, PlayerId = 1 };
            buffer.SetPending(in order, priority: 5, expireStep: 100, insertStep: 10);

            That(buffer.HasPending, Is.True);
            That(buffer.PendingOrder.Order.OrderTypeId, Is.EqualTo(42));
            That(buffer.PendingOrder.Priority, Is.EqualTo(5));
            That(buffer.PendingOrder.ExpireStep, Is.EqualTo(100));
            That(buffer.PendingOrder.InsertStep, Is.EqualTo(10));
        }

        [Test]
        public void PendingBuffer_ClearPending_ResetsSlot()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 42 };
            buffer.SetPending(in order, 5, 100, 10);
            That(buffer.HasPending, Is.True);

            buffer.ClearPending();
            That(buffer.HasPending, Is.False);
            That(buffer.PendingOrder.Order.OrderTypeId, Is.EqualTo(0));
        }

        [Test]
        public void PendingBuffer_ExpirePending_ExpiresWhenStepReached()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 7 };
            buffer.SetPending(in order, 5, expireStep: 50, insertStep: 10);

            // Before expiration �?should not expire
            bool expired = buffer.ExpirePending(currentStep: 49);
            That(expired, Is.False);
            That(buffer.HasPending, Is.True);

            // At expiration step �?should expire
            expired = buffer.ExpirePending(currentStep: 50);
            That(expired, Is.True);
            That(buffer.HasPending, Is.False);
        }

        [Test]
        public void PendingBuffer_ExpirePending_DoesNothingWhenEmpty()
        {
            var buffer = OrderBuffer.CreateEmpty();
            bool expired = buffer.ExpirePending(currentStep: 999);
            That(expired, Is.False);
            That(buffer.HasPending, Is.False);
        }

        [Test]
        public void PendingBuffer_ExpirePending_NoExpirationNegativeOne()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 1 };
            buffer.SetPending(in order, 5, expireStep: -1, insertStep: 0);

            // -1 = no expiration; should never expire
            bool expired = buffer.ExpirePending(currentStep: 999999);
            That(expired, Is.False);
            That(buffer.HasPending, Is.True);
        }

        [Test]
        public void PendingBuffer_SetPending_LastWriteWins()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order1 = new Order { OrderTypeId = 1 };
            var order2 = new Order { OrderTypeId = 2 };

            buffer.SetPending(in order1, 5, 100, 10);
            buffer.SetPending(in order2, 3, 200, 20);

            That(buffer.HasPending, Is.True);
            That(buffer.PendingOrder.Order.OrderTypeId, Is.EqualTo(2), "Last-write-wins: order2 should overwrite order1");
            That(buffer.PendingOrder.Priority, Is.EqualTo(3));
        }

        [Test]
        public void PendingBuffer_Clear_AlsoClearsPending()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTypeId = 5 };
            buffer.SetPending(in order, 1, 50, 0);
            buffer.Enqueue(in order, 1, -1, 0);

            buffer.Clear();
            That(buffer.HasPending, Is.False, "Clear() should reset pending");
            That(buffer.HasQueued, Is.False, "Clear() should reset queue");
            That(buffer.HasActive, Is.False, "Clear() should reset active");
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: GrantedSlotBuffer + AbilitySlotResolver
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OrderTypeRegistry_GetUnknownType_Throws()
        {
            var registry = new OrderTypeRegistry();

            var ex = Throws<KeyNotFoundException>(() => registry.Get(999));

            That(ex!.Message, Does.Contain("999"));
        }

        [Test]
        public void GrantedSlotBuffer_Grant_OverridesSlot()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(slotIndex: 2, abilityId: 99, sourceTagId: 10);

            That(granted.HasOverride(2), Is.True);
            var slot = granted.GetOverride(2);
            That(slot.AbilityId, Is.EqualTo(99));
        }

        [Test]
        public void GrantedSlotBuffer_Revoke_ClearsSlot()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(0, 50, 10);
            That(granted.HasOverride(0), Is.True);

            granted.Revoke(0);
            That(granted.HasOverride(0), Is.False);
        }

        [Test]
        public void GrantedSlotBuffer_RevokeBySource_RemovesAllMatchingSlots()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(0, 10, sourceTagId: 5);
            granted.Grant(1, 20, sourceTagId: 5);
            granted.Grant(2, 30, sourceTagId: 7);

            int revoked = granted.RevokeBySource(sourceTagId: 5);
            That(revoked, Is.EqualTo(2));
            That(granted.HasOverride(0), Is.False);
            That(granted.HasOverride(1), Is.False);
            That(granted.HasOverride(2), Is.True, "Source 7 should be unaffected");
        }

        [Test]
        public void GrantedSlotBuffer_OutOfBounds_Ignored()
        {
            var granted = new GrantedSlotBuffer();
            granted.Grant(-1, 1, 1);
            granted.Grant(GrantedSlotBuffer.CAPACITY, 1, 1);
            That(granted.HasOverride(-1), Is.False);
            That(granted.HasOverride(GrantedSlotBuffer.CAPACITY), Is.False);
        }

        [Test]
        public void AbilitySlotResolver_ReturnsGrantedWhenOverrideExists()
        {
            var baseSlots = new AbilityStateBuffer();
            baseSlots.AddAbility(100); // slot 0
            baseSlots.AddAbility(200); // slot 1

            var granted = new GrantedSlotBuffer();
            granted.Grant(0, abilityId: 999, sourceTagId: 1);

            var resolved = AbilitySlotResolver.Resolve(in baseSlots, in granted, hasGranted: true, slotIndex: 0);
            That(resolved.AbilityId, Is.EqualTo(999), "Should return granted override");

            var resolvedBase = AbilitySlotResolver.Resolve(in baseSlots, in granted, hasGranted: true, slotIndex: 1);
            That(resolvedBase.AbilityId, Is.EqualTo(200), "Slot 1 has no override, should return base");
        }

        [Test]
        public void AbilitySlotResolver_IgnoresGrantedWhenHasGrantedIsFalse()
        {
            var baseSlots = new AbilityStateBuffer();
            baseSlots.AddAbility(100);

            var granted = new GrantedSlotBuffer();
            granted.Grant(0, abilityId: 999, sourceTagId: 1);

            var resolved = AbilitySlotResolver.Resolve(in baseSlots, in granted, hasGranted: false, slotIndex: 0);
            That(resolved.AbilityId, Is.EqualTo(100), "hasGranted=false should skip granted buffer");
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: AbilityToggleSpec
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AbilityToggleSpec_RegisterAndRetrieve()
        {
            var registry = new AbilityDefinitionRegistry();

            var toggleSpec = new AbilityToggleSpec
            {
                ToggleTagId = 42
            };

            var def = new AbilityDefinition
            {
                HasToggleSpec = true,
                ToggleSpec = toggleSpec
            };

            registry.Register(1, in def);
            That(registry.TryGet(1, out var retrieved), Is.True);
            That(retrieved.HasToggleSpec, Is.True);
            That(retrieved.ToggleSpec.ToggleTagId, Is.EqualTo(42));
        }

        [Test]
        public void AbilityToggleSpec_NonToggle_HasToggleSpecIsFalse()
        {
            var registry = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasToggleSpec = false
            };

            registry.Register(2, in def);
            That(registry.TryGet(2, out var retrieved), Is.True);
            That(retrieved.HasToggleSpec, Is.False);
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_ParsesToggleSpecAndIndicator()
        {
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            EffectTemplateIdRegistry.Register("Effect.Toggle.GuardAura");

            var obj = JsonNode.Parse(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "items": [
                      { "kind": "End", "tick": 0 }
                    ]
                  },
                  "toggleSpec": {
                    "toggleTag": "State.Guarding",
                    "activeEffects": ["Effect.Toggle.GuardAura"],
                    "deactivateExec": {
                      "clockId": "FixedFrame",
                      "items": [
                        { "kind": "End", "tick": 0 }
                      ]
                    }
                  },
                  "indicator": {
                    "shape": "Ring",
                    "range": 620,
                    "radius": 180,
                    "innerRadius": 90,
                    "angleDeg": 45,
                    "showRangeCircle": true,
                    "validColor": "#33CC66AA",
                    "invalidColor": [1.0, 0.2, 0.2, 0.5]
                  }
                }
                """)!.AsObject();

            var def = Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.Guard", "GAS/abilities.json");

            That(def.HasToggleSpec, Is.True);
            That(def.ToggleSpec.ToggleTagId, Is.EqualTo(TagRegistry.Register("State.Guarding")));
            That(def.ToggleSpec.ActiveEffectCount, Is.EqualTo(1));
            unsafe
            {
                That(def.ToggleSpec.ActiveEffectTemplateIds[0], Is.EqualTo(EffectTemplateIdRegistry.GetId("Effect.Toggle.GuardAura")));
            }

            That(def.HasIndicator, Is.True);
            That(def.Indicator.Shape, Is.EqualTo(TargetShape.Ring));
            That(def.Indicator.Range, Is.EqualTo(620f));
            That(def.Indicator.Radius, Is.EqualTo(180f));
            That(def.Indicator.InnerRadius, Is.EqualTo(90f));
            That(def.Indicator.Angle, Is.EqualTo(MathF.PI / 4f).Within(0.0001f));
            That(def.Indicator.ShowRangeCircle, Is.True);
        }

        [Test]
        public void AbilityExecLoader_CompileAbility_ParsesActivationPrecondition()
        {
            GraphIdRegistry.Clear();
            int validationGraphId = GraphIdRegistry.Register("Graph.Ability.ManaEnough");

            var obj = JsonNode.Parse(
                """
                {
                  "exec": {
                    "clockId": "FixedFrame",
                    "items": [
                      { "kind": "End", "tick": 0 }
                    ]
                  },
                  "activationPrecondition": {
                    "validationGraph": "Graph.Ability.ManaEnough"
                  }
                }
                """)!.AsObject();

            var def = Ludots.Core.Gameplay.GAS.Config.AbilityExecLoader.CompileAbility(obj, "Ability.Test.ManaCheck", "GAS/abilities.json");

            That(def.HasActivationPrecondition, Is.True);
            That(def.ActivationPrecondition.ValidationGraphId, Is.EqualTo(validationGraphId));
        }

        [Test]
        public void AbilityDefinitionRegistry_RegisterFromEntity_CopiesActivationPrecondition()
        {
            using var world = World.Create();
            var template = world.Create(
                new AbilityTemplate(),
                new AbilityExecSpec(),
                new AbilityActivationPrecondition { ValidationGraphId = 77 });

            var defs = new AbilityDefinitionRegistry();
            defs.RegisterFromEntity(world, template, abilityId: 6010);

            That(defs.TryGet(6010, out var def), Is.True);
            That(def.HasActivationPrecondition, Is.True);
            That(def.ActivationPrecondition.ValidationGraphId, Is.EqualTo(77));
        }

        [Test]
        public void AbilityDefinitionRegistry_RegisterFromEntity_CopiesTechnologyRequirements()
        {
            using var world = World.Create();
            var template = world.Create(
                new AbilityTemplate(),
                new AbilityExecSpec(),
                new AbilityTechnologyRequirements
                {
                    UseRequirementId = 1201,
                    ShowRequirementId = 1202
                });

            var defs = new AbilityDefinitionRegistry();
            defs.RegisterFromEntity(world, template, abilityId: 6011);

            That(defs.TryGet(6011, out var def), Is.True);
            That(def.HasUseTechnologyRequirement, Is.True);
            That(def.UseTechnologyRequirementId, Is.EqualTo(1201));
            That(def.HasShowTechnologyRequirement, Is.True);
            That(def.ShowTechnologyRequirementId, Is.EqualTo(1202));
        }

        [Test]
        public void OrderBufferSystem_PromoteQueued_WritesBlackboard()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());
            var target = world.Create();

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 10,
                AllowQueuedMode = true,
                ClearQueueOnActivate = false,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
            });

            var orderRules = new OrderRuleRegistry();
            var clock = new DiscreteClock();
            var system = new OrderBufferSystem(world, clock, orderTypes, orderRules);

            var order = new Order
            {
                Actor = actor,
                Target = target,
                OrderTypeId = 10,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 2 }
            };

            var submit = OrderSubmitter.Submit(world, actor, in order, orderTypes, orderRules, currentStep: 0, stepRateHz: 30);
            That(submit, Is.EqualTo(OrderSubmitResult.Queued));

            system.Update(0);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            ref var bbE = ref world.Get<BlackboardEntityBuffer>(actor);

            That(buffer.HasActive, Is.True);
            That(buffer.HasQueued, Is.False);
            That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(10));

            That(bbI.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex), Is.True);
            That(slotIndex, Is.EqualTo(2));

            That(bbE.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out Entity bbTarget), Is.True);
            That(bbTarget, Is.EqualTo(target));
        }

        [Test]
        public void OrderSubmitter_QueuedMode_UsesBufferWindowExpiry()
        {
            using var world = World.Create();
            var actor = world.Create(OrderBuffer.CreateEmpty(), new GameplayTagContainer());

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 11,
                AllowQueuedMode = true,
                BufferWindowMs = 300,
                ClearQueueOnActivate = false,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });

            var order = new Order
            {
                Actor = actor,
                OrderTypeId = 11,
                SubmitMode = OrderSubmitMode.Queued
            };

            var submit = OrderSubmitter.Submit(world, actor, in order, orderTypes, orderRuleRegistry: null, currentStep: 0, stepRateHz: 30);
            That(submit, Is.EqualTo(OrderSubmitResult.Queued));

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            That(buffer.QueuedCount, Is.EqualTo(1));
            That(buffer.GetQueued(0).ExpireStep, Is.EqualTo(9));
        }

        // ════════════════════════════════════════════════════════════════════
        [Test]
        public void AbilityExecSystem_ActiveCastOrderFromOrderBuffer_DoesNotRequireGameplayTag()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(9001);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 7,
                Actor = actor,
                OrderTypeId = 100,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition();
            defs.Register(9001, in def);

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new SelectionRequestQueue(),
                new SelectionResponseBuffer(),
                new EffectRequestQueue(),
                defs,
                castAbilityOrderTypeId: 100,
                orderTypeRegistry: new OrderTypeRegistry());
            system.MaxWorkUnitsPerSlice = 1;

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.False, "Budget should stop after Phase 1 so the spawned exec can be inspected.");
            That(world.Has<AbilityExecInstance>(actor), Is.True, "Cast ability should start from OrderBuffer active order without any gameplay order tag.");

            ref var exec = ref world.Get<AbilityExecInstance>(actor);
            That(exec.AbilityId, Is.EqualTo(9001));
            That(exec.OrderId, Is.EqualTo(7));
            That(exec.AbilitySlot, Is.EqualTo(0));
        }

        [Test]
        public void AbilityExecSystem_ValidCast_PublishesCastCommittedPresentationEvent()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9003;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 10,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition();
            defs.Register(abilityId, in def);

            var presentationEvents = new GasPresentationEventBuffer(8);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new SelectionRequestQueue(),
                new SelectionResponseBuffer(),
                new EffectRequestQueue(),
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: new OrderTypeRegistry());
            system.MaxWorkUnitsPerSlice = 1;

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.False, "Budget should stop after Phase 1 so activation events can be inspected.");
            var events = presentationEvents.Events;
            That(events.Length, Is.EqualTo(2));
            That(events[0].Kind, Is.EqualTo(GasPresentationEventKind.CastStarted));
            That(events[1].Kind, Is.EqualTo(GasPresentationEventKind.CastCommitted));
            That(events[1].Actor, Is.EqualTo(actor));
            That(events[1].AbilitySlot, Is.EqualTo(0));
            That(events[1].AbilityId, Is.EqualTo(abilityId));
        }

        [Test]
        public void AbilityExecSystem_ToggleDeactivate_BypassesBlockedAnyCooldown_AndClearsTagCount()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags());

            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9002;
            const int toggleTagId = 41;
            const int cooldownTagId = 42;

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 8,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var tagOps = new TagOps();
            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            ref var counts = ref world.Get<TagCountContainer>(actor);
            ref var dirty = ref world.Get<DirtyFlags>(actor);
            tagOps.AddTag(ref tags, ref counts, toggleTagId, ref dirty);
            tagOps.AddTag(ref tags, ref counts, cooldownTagId, ref dirty);

            var blockTags = new AbilityActivationBlockTags();
            blockTags.BlockedAny.AddTag(cooldownTagId);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags,
                HasToggleSpec = true,
                ToggleSpec = new AbilityToggleSpec
                {
                    ToggleTagId = toggleTagId
                }
            };
            defs.Register(abilityId, in def);

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new SelectionRequestQueue(),
                new SelectionResponseBuffer(),
                new EffectRequestQueue(),
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                tagOps: tagOps);

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.True);
            That(world.Get<GameplayTagContainer>(actor).HasTag(toggleTagId), Is.False, "Toggle should turn off even when reactivate cooldown is present.");
            That(world.Get<TagCountContainer>(actor).GetCount(toggleTagId), Is.EqualTo(0), "Toggle removal must clear TagCountContainer as well as the bitset.");
            That(world.Get<GameplayTagContainer>(actor).HasTag(cooldownTagId), Is.True, "Turning off a toggle should not remove the reactivation cooldown tag.");
            That(world.Get<TagCountContainer>(actor).GetCount(cooldownTagId), Is.EqualTo(1));
        }

        [Test]
        public void AbilityExecSystem_ActivationPreconditionGraphRejectsCast_AndCancelsOrder()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int abilityId = 9101;
            const int validationGraphId = 301;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order
            {
                OrderId = 9,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasActivationPrecondition = true,
                ActivationPrecondition = new AbilityActivationPrecondition
                {
                    ValidationGraphId = validationGraphId
                }
            };
            defs.Register(abilityId, in def);

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(validationGraphId, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            });

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
            });

            var presentationEvents = new GasPresentationEventBuffer(8);
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new SelectionRequestQueue(),
                new SelectionResponseBuffer(),
                new EffectRequestQueue(),
                defs,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                graphPrograms: graphPrograms,
                graphApi: graphApi,
                orderTypeRegistry: orderTypes);

            bool completed = system.UpdateSlice(0f, int.MaxValue);

            That(completed, Is.True);
            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False, "Rejected casts must cancel the active order so queued orders can promote.");
            That(presentationEvents.Count, Is.EqualTo(1));
            That(presentationEvents.Events[0].Kind, Is.EqualTo(GasPresentationEventKind.CastFailed));
            That(presentationEvents.Events[0].FailReason, Is.EqualTo(AbilityCastFailReason.PreconditionFailed));
        }

        [Test]
        public void AbilitySystem_RegistryAbility_ActivationPreconditionGraphRejectsActivation()
        {
            using var world = World.Create();
            const int abilityId = 9201;
            const int validationGraphId = 302;

            var actor = world.Create(new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            var effects = new AbilityOnActivateEffects();
            effects.Add(4001);

            var defs = new AbilityDefinitionRegistry();
            var def = new AbilityDefinition
            {
                HasOnActivateEffects = true,
                OnActivateEffects = effects,
                HasActivationPrecondition = true,
                ActivationPrecondition = new AbilityActivationPrecondition
                {
                    ValidationGraphId = validationGraphId
                }
            };
            defs.Register(abilityId, in def);

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(validationGraphId, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            });

            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: effectRequests);
            var system = new AbilitySystem(world, effectRequests, defs, graphPrograms: graphPrograms, graphApi: graphApi);

            bool activated = system.TryActivateAbility(actor, 0);

            That(activated, Is.False);
            That(effectRequests.Count, Is.EqualTo(0));
        }

        [Test]
        public void AbilitySystem_TemplateAbility_ActivationPreconditionGraphRejectsActivation()
        {
            using var world = World.Create();
            const int validationGraphId = 303;

            var templateEffects = new AbilityOnActivateEffects();
            templateEffects.Add(4002);

            var template = world.Create(
                new AbilityTemplate(),
                templateEffects,
                new AbilityActivationPrecondition { ValidationGraphId = validationGraphId });

            var actor = world.Create(new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(template);

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(validationGraphId, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            });

            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: effectRequests);
            var system = new AbilitySystem(world, effectRequests, graphPrograms: graphPrograms, graphApi: graphApi);

            bool activated = system.TryActivateAbility(actor, 0);

            That(activated, Is.False);
            That(effectRequests.Count, Is.EqualTo(0));
        }

        // Region: GraphExecutor.ExecuteValidation
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ExecuteValidation_EmptyProgram_ReturnsTrue()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Empty program �?B[0] starts at 1 (pass), no instructions change it
            ReadOnlySpan<GraphInstruction> program = ReadOnlySpan<GraphInstruction>.Empty;
            bool result = GasGraphExecutor.ExecuteValidation(world, caster, target, default, program, null!);
            That(result, Is.True, "Empty validation program should pass by default (B[0]=1)");
        }

        [Test]
        public void ExecuteValidation_SetBoolFalse_ReturnsFalse()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Create a program with a single instruction: ConstBool B[0] = 0 (reject)
            var instruction = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.ConstBool,
                Dst = 0,  // register index B[0]
                Imm = 0   // value = false
            };
            ReadOnlySpan<GraphInstruction> program = new[] { instruction };
            bool result = GasGraphExecutor.ExecuteValidation(world, caster, target, default, program, null!);
            That(result, Is.False, "ConstBool B[0]=0 should cause validation to fail");
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: OrderBuffer queue stress
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OrderBuffer_Enqueue_RespectsPriorityOrdering()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, priority: 1, -1, insertStep: 0);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, priority: 3, -1, insertStep: 1);
            buffer.Enqueue(new Order { OrderTypeId = 3 }, priority: 2, -1, insertStep: 2);

            That(buffer.QueuedCount, Is.EqualTo(3));
            That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(2), "Highest priority first");
            That(buffer.GetQueued(1).Order.OrderTypeId, Is.EqualTo(3), "Second priority");
            That(buffer.GetQueued(2).Order.OrderTypeId, Is.EqualTo(1), "Lowest priority last");
        }

        [Test]
        public void OrderBuffer_Enqueue_FIFOWithinSamePriority()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, priority: 5, -1, insertStep: 10);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, priority: 5, -1, insertStep: 20);
            buffer.Enqueue(new Order { OrderTypeId = 3 }, priority: 5, -1, insertStep: 30);

            That(buffer.GetQueued(0).Order.OrderTypeId, Is.EqualTo(1), "FIFO: first inserted comes first");
            That(buffer.GetQueued(1).Order.OrderTypeId, Is.EqualTo(2));
            That(buffer.GetQueued(2).Order.OrderTypeId, Is.EqualTo(3));
        }

        [Test]
        public void OrderBuffer_Enqueue_FullQueueReturnsFalse()
        {
            var buffer = OrderBuffer.CreateEmpty();
            for (int i = 0; i < OrderBuffer.MAX_QUEUED_ORDERS; i++)
            {
                bool ok = buffer.Enqueue(new Order { OrderTypeId = i }, 0, -1, i);
                That(ok, Is.True, $"Enqueue {i} should succeed");
            }

            bool overflow = buffer.Enqueue(new Order { OrderTypeId = 999 }, 0, -1, 100);
            That(overflow, Is.False, "Queue full �?should reject");
            That(buffer.QueuedCount, Is.EqualTo(OrderBuffer.MAX_QUEUED_ORDERS));
        }

        [Test]
        public void OrderBuffer_RemoveExpired_CleansUpCorrectly()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, 0, expireStep: 10, insertStep: 0);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, 0, expireStep: 50, insertStep: 1);
            buffer.Enqueue(new Order { OrderTypeId = 3 }, 0, expireStep: -1, insertStep: 2); // no expiration

            int removed = buffer.RemoveExpired(currentStep: 30);
            That(removed, Is.EqualTo(1), "Only order with expireStep=10 should be expired");
            That(buffer.QueuedCount, Is.EqualTo(2));
        }

        [Test]
        public void OrderBuffer_PromoteNext_MovesFirstQueuedToActive()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTypeId = 1 }, priority: 10, -1, 0);
            buffer.Enqueue(new Order { OrderTypeId = 2 }, priority: 5, -1, 1);

            bool promoted = buffer.PromoteNext();
            That(promoted, Is.True);
            That(buffer.HasActive, Is.True);
            That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(1), "Highest priority promoted");
            That(buffer.QueuedCount, Is.EqualTo(1), "One remaining in queue");
        }

        [Test]
        public void StopOrderSystem_ActiveStopOrder_DoesNotRequireGameplayTagContainer()
        {
            TagRegistry.Clear();

            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new AbilityExecInstance(),
                new NavGoal2D { Kind = NavGoalKind2D.Point });

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var stopOrder = new Order { Actor = actor, OrderTypeId = 103 };
            buffer.SetActiveDirect(in stopOrder, priority: 200);

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { OrderTypeId = 103, AllowQueuedMode = false, ClearQueueOnActivate = true });

            var system = new StopOrderSystem(world, orderTypes, 103);
            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<NavGoal2D>(actor).Kind, Is.EqualTo(NavGoalKind2D.None));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void StopOrderNavMoveCleanupSystem_ActiveStopOrder_ClearsNavigationTag()
        {
            TagRegistry.Clear();
            int navMoveTagId = TagRegistry.Register("Ability.Nav.Move");

            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new AbilityExecInstance(),
                new NavGoal2D { Kind = NavGoalKind2D.Point },
                new GameplayTagContainer());

            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            tags.AddTag(navMoveTagId);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var stopOrder = new Order { Actor = actor, OrderTypeId = 103 };
            buffer.SetActiveDirect(in stopOrder, priority: 200);

            var system = new StopOrderNavMoveCleanupSystem(world, 103, navMoveTagId);
            system.Update(0f);

            That(tags.HasTag(navMoveTagId), Is.False);
            That(world.Has<AbilityExecInstance>(actor), Is.True, "Navigation tag cleanup must stay separate from generic stop processing.");
            That(world.Get<NavGoal2D>(actor).Kind, Is.EqualTo(NavGoalKind2D.Point));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_ActiveMoveToOrder_MovesAndCompletes()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var args = new OrderArgs();
            args.Spatial.Kind = OrderSpatialKind.WorldCm;
            args.Spatial.Mode = OrderCollectionMode.Single;
            args.Spatial.WorldCm = new Vector3(90f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var order = new Order { Actor = actor, OrderTypeId = 101, Args = args };
            buffer.SetActiveDirect(in order, priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, defaultSpeedCmPerSec: 300f, stopRadiusCm: 5f);
            system.Update(0.10f);

            var firstStepPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(firstStepPos.X, Is.EqualTo(30).Within(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.True);

            system.Update(0.10f);
            system.Update(0.10f);

            var finalPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(finalPos.X, Is.EqualTo(90).Within(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_Arrival_PromotesQueuedWaypoint()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            var actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new AttributeBuffer(),
                OrderBuffer.CreateEmpty(),
                new BlackboardSpatialBuffer());

            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(moveSpeedId, 300f);

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = 101,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                SpatialBlackboardKey = OrderBlackboardKeys.Generic_TargetPosition
            });

            var firstArgs = new OrderArgs();
            firstArgs.Spatial.Kind = OrderSpatialKind.WorldCm;
            firstArgs.Spatial.Mode = OrderCollectionMode.Single;
            firstArgs.Spatial.WorldCm = new Vector3(30f, 0f, 0f);

            var secondArgs = new OrderArgs();
            secondArgs.Spatial.Kind = OrderSpatialKind.WorldCm;
            secondArgs.Spatial.Mode = OrderCollectionMode.Single;
            secondArgs.Spatial.WorldCm = new Vector3(60f, 0f, 0f);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            var firstOrder = new Order { Actor = actor, OrderTypeId = 101, Args = firstArgs };
            var secondOrder = new Order { Actor = actor, OrderTypeId = 101, Args = secondArgs, SubmitMode = OrderSubmitMode.Queued };
            buffer.SetActiveDirect(in firstOrder, priority: 60);
            buffer.Enqueue(in secondOrder, priority: 60, expireStep: -1, insertStep: 1);

            var system = new MoveToWorldCmOrderSystem(world, orderTypes, 101, defaultSpeedCmPerSec: 300f, stopRadiusCm: 5f);
            system.Update(0.10f);

            ref var promotedBuffer = ref world.Get<OrderBuffer>(actor);
            That(promotedBuffer.HasActive, Is.True);
            That(promotedBuffer.ActiveOrder.Order.Args.Spatial.WorldCm.X, Is.EqualTo(60f).Within(0.001f));
            That(promotedBuffer.QueuedCount, Is.EqualTo(0));

            system.Update(0.10f);

            var finalPos = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            That(finalPos.X, Is.EqualTo(60).Within(1));
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        private static GraphInstruction[] RejectValidationProgram()
        {
            return new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstBool,
                    Dst = 0,
                    Imm = 0
                }
            };
        }

    }
}



