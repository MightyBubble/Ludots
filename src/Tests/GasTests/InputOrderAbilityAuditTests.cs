using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
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
        // Region: OrderBuffer — PendingBuffer
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void PendingBuffer_SetPending_StoresOrderCorrectly()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTagId = 42, PlayerId = 1 };
            buffer.SetPending(in order, priority: 5, expireStep: 100, insertStep: 10);

            That(buffer.HasPending, Is.True);
            That(buffer.PendingOrder.Order.OrderTagId, Is.EqualTo(42));
            That(buffer.PendingOrder.Priority, Is.EqualTo(5));
            That(buffer.PendingOrder.ExpireStep, Is.EqualTo(100));
            That(buffer.PendingOrder.InsertStep, Is.EqualTo(10));
        }

        [Test]
        public void PendingBuffer_ClearPending_ResetsSlot()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTagId = 42 };
            buffer.SetPending(in order, 5, 100, 10);
            That(buffer.HasPending, Is.True);

            buffer.ClearPending();
            That(buffer.HasPending, Is.False);
            That(buffer.PendingOrder.Order.OrderTagId, Is.EqualTo(0));
        }

        [Test]
        public void PendingBuffer_ExpirePending_ExpiresWhenStepReached()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTagId = 7 };
            buffer.SetPending(in order, 5, expireStep: 50, insertStep: 10);

            // Before expiration — should not expire
            bool expired = buffer.ExpirePending(currentStep: 49);
            That(expired, Is.False);
            That(buffer.HasPending, Is.True);

            // At expiration step — should expire
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
            var order = new Order { OrderTagId = 1 };
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
            var order1 = new Order { OrderTagId = 1 };
            var order2 = new Order { OrderTagId = 2 };

            buffer.SetPending(in order1, 5, 100, 10);
            buffer.SetPending(in order2, 3, 200, 20);

            That(buffer.HasPending, Is.True);
            That(buffer.PendingOrder.Order.OrderTagId, Is.EqualTo(2), "Last-write-wins: order2 should overwrite order1");
            That(buffer.PendingOrder.Priority, Is.EqualTo(3));
        }

        [Test]
        public void PendingBuffer_Clear_AlsoClearsPending()
        {
            var buffer = OrderBuffer.CreateEmpty();
            var order = new Order { OrderTagId = 5 };
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
                OrderTagId = 10,
                OrderStateTagId = OrderStateTags.Active_CastAbility,
                AllowQueuedMode = true,
                ClearQueueOnActivate = false,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
            });

            var tagRules = new TagRuleRegistry();
            var clock = new DiscreteClock();
            var system = new OrderBufferSystem(world, clock, orderTypes, tagRules);

            var order = new Order
            {
                Actor = actor,
                Target = target,
                OrderTagId = 10,
                SubmitMode = OrderSubmitMode.Queued,
                Args = new OrderArgs { I0 = 2 }
            };

            var submit = OrderSubmitter.Submit(world, actor, in order, orderTypes, tagRules, currentStep: 0, stepRateHz: 30);
            That(submit, Is.EqualTo(OrderSubmitResult.Queued));

            system.Update(0);

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            ref var bbE = ref world.Get<BlackboardEntityBuffer>(actor);

            That(buffer.HasActive, Is.True);
            That(buffer.HasQueued, Is.False);
            That(tags.HasTag(OrderStateTags.Active_CastAbility), Is.True);
            That(tags.HasTag(OrderStateTags.State_HasActive), Is.True);
            That(tags.HasTag(OrderStateTags.State_HasQueued), Is.False);

            That(bbI.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex), Is.True);
            That(slotIndex, Is.EqualTo(2));

            That(bbE.TryGet(OrderBlackboardKeys.Cast_TargetEntity, out Entity bbTarget), Is.True);
            That(bbTarget, Is.EqualTo(target));
        }

        // ════════════════════════════════════════════════════════════════════
        // Region: GraphExecutor.ExecuteValidation
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ExecuteValidation_EmptyProgram_ReturnsTrue()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Empty program — B[0] starts at 1 (pass), no instructions change it
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
            buffer.Enqueue(new Order { OrderTagId = 1 }, priority: 1, -1, insertStep: 0);
            buffer.Enqueue(new Order { OrderTagId = 2 }, priority: 3, -1, insertStep: 1);
            buffer.Enqueue(new Order { OrderTagId = 3 }, priority: 2, -1, insertStep: 2);

            That(buffer.QueuedCount, Is.EqualTo(3));
            That(buffer.GetQueued(0).Order.OrderTagId, Is.EqualTo(2), "Highest priority first");
            That(buffer.GetQueued(1).Order.OrderTagId, Is.EqualTo(3), "Second priority");
            That(buffer.GetQueued(2).Order.OrderTagId, Is.EqualTo(1), "Lowest priority last");
        }

        [Test]
        public void OrderBuffer_Enqueue_FIFOWithinSamePriority()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTagId = 1 }, priority: 5, -1, insertStep: 10);
            buffer.Enqueue(new Order { OrderTagId = 2 }, priority: 5, -1, insertStep: 20);
            buffer.Enqueue(new Order { OrderTagId = 3 }, priority: 5, -1, insertStep: 30);

            That(buffer.GetQueued(0).Order.OrderTagId, Is.EqualTo(1), "FIFO: first inserted comes first");
            That(buffer.GetQueued(1).Order.OrderTagId, Is.EqualTo(2));
            That(buffer.GetQueued(2).Order.OrderTagId, Is.EqualTo(3));
        }

        [Test]
        public void OrderBuffer_Enqueue_FullQueueReturnsFalse()
        {
            var buffer = OrderBuffer.CreateEmpty();
            for (int i = 0; i < OrderBuffer.MAX_QUEUED_ORDERS; i++)
            {
                bool ok = buffer.Enqueue(new Order { OrderTagId = i }, 0, -1, i);
                That(ok, Is.True, $"Enqueue {i} should succeed");
            }

            bool overflow = buffer.Enqueue(new Order { OrderTagId = 999 }, 0, -1, 100);
            That(overflow, Is.False, "Queue full — should reject");
            That(buffer.QueuedCount, Is.EqualTo(OrderBuffer.MAX_QUEUED_ORDERS));
        }

        [Test]
        public void OrderBuffer_RemoveExpired_CleansUpCorrectly()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTagId = 1 }, 0, expireStep: 10, insertStep: 0);
            buffer.Enqueue(new Order { OrderTagId = 2 }, 0, expireStep: 50, insertStep: 1);
            buffer.Enqueue(new Order { OrderTagId = 3 }, 0, expireStep: -1, insertStep: 2); // no expiration

            int removed = buffer.RemoveExpired(currentStep: 30);
            That(removed, Is.EqualTo(1), "Only order with expireStep=10 should be expired");
            That(buffer.QueuedCount, Is.EqualTo(2));
        }

        [Test]
        public void OrderBuffer_PromoteNext_MovesFirstQueuedToActive()
        {
            var buffer = OrderBuffer.CreateEmpty();
            buffer.Enqueue(new Order { OrderTagId = 1 }, priority: 10, -1, 0);
            buffer.Enqueue(new Order { OrderTagId = 2 }, priority: 5, -1, 1);

            bool promoted = buffer.PromoteNext();
            That(promoted, Is.True);
            That(buffer.HasActive, Is.True);
            That(buffer.ActiveOrder.Order.OrderTagId, Is.EqualTo(1), "Highest priority promoted");
            That(buffer.QueuedCount, Is.EqualTo(1), "One remaining in queue");
        }
    }
}
