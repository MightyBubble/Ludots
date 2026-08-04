using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AIPlanExecutionReceiptTests
    {
        [Test]
        public void PlanExecution_WaitsForTerminalReceiptBeforeAdvancing()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var terminalResults = new OrderTerminalResultBuffer(8);
            var orders = new OrderQueue(8, admissionResults);
            var orderTypes = CreateOrderTypes(terminalResults);
            var library = CreateCastThenStopLibrary();
            var system = new AIPlanExecutionSystem(world, clock, library, orders, orderTypes);

            var plan = new AIPlan32();
            Assert.That(plan.TryAdd(0), Is.True);
            Assert.That(plan.TryAdd(1), Is.True);
            var ints = new BlackboardIntBuffer();
            ints.Set(AbilitySlotKey, 0);
            Entity actor = world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                ints,
                new BlackboardEntityBuffer());

            system.Update(1f / 60f);

            Assert.That(orders.TryDequeue(out Order submitted), Is.True);
            Assert.That(submitted.OrderTypeId, Is.EqualTo(CastOrderTypeId));
            ref var pendingPlan = ref world.Get<AIPlan32>(actor);
            Assert.That(pendingPlan.Cursor, Is.EqualTo(0));
            Assert.That(pendingPlan.WaitingOrderId, Is.EqualTo(submitted.OrderId));
            Assert.That(pendingPlan.WaitingOrderTypeId, Is.EqualTo(CastOrderTypeId));

            terminalResults.Clear();
            clock.Advance(ClockDomainId.Step, 1);
            system.Update(1f / 60f);

            Assert.That(pendingPlan.Cursor, Is.EqualTo(0));
            Assert.That(pendingPlan.WaitingOrderId, Is.EqualTo(submitted.OrderId));

            terminalResults.Write(new OrderTerminalOutcome(
                submitted.OrderId,
                submitted.OrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                actor));
            terminalResults.Clear();
            clock.Advance(ClockDomainId.Step, 1);
            system.Update(1f / 60f);

            Assert.That(pendingPlan.Cursor, Is.EqualTo(1));
            Assert.That(pendingPlan.WaitingOrderId, Is.Zero);
            Assert.That(terminalResults.LedgerCount, Is.Zero);
            Assert.That(orders.Count, Is.Zero, "The next plan action should not be submitted in the same update that consumes the prior receipt.");
        }

        [Test]
        public void PlanExecution_FailedReceiptClearsPlanInsteadOfAdvancing()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var terminalResults = new OrderTerminalResultBuffer(8);
            var orders = new OrderQueue(8, admissionResults);
            var orderTypes = CreateOrderTypes(terminalResults);
            var library = CreateCastThenStopLibrary();
            var system = new AIPlanExecutionSystem(world, clock, library, orders, orderTypes);

            var plan = new AIPlan32();
            Assert.That(plan.TryAdd(0), Is.True);
            Assert.That(plan.TryAdd(1), Is.True);
            var ints = new BlackboardIntBuffer();
            ints.Set(AbilitySlotKey, 0);
            Entity actor = world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                ints,
                new BlackboardEntityBuffer());

            system.Update(1f / 60f);

            Assert.That(orders.TryDequeue(out Order submitted), Is.True);
            terminalResults.Write(new OrderTerminalOutcome(
                submitted.OrderId,
                submitted.OrderTypeId,
                OrderTerminalState.Failed,
                OrderFailureReason.PreconditionFailed,
                actor));
            terminalResults.Clear();
            clock.Advance(ClockDomainId.Step, 1);

            system.Update(1f / 60f);

            ref var failedPlan = ref world.Get<AIPlan32>(actor);
            Assert.That(failedPlan.IsDone, Is.True);
            Assert.That(failedPlan.Length, Is.Zero);
            Assert.That(failedPlan.WaitingOrderId, Is.Zero);
            Assert.That(orders.Count, Is.Zero);
            Assert.That(terminalResults.LedgerCount, Is.Zero);
        }

        [Test]
        public void PlanExecutor_ReleasesRetainedReceiptWhenQueueRejects()
        {
            using var world = World.Create();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var terminalResults = new OrderTerminalResultBuffer(1);
            var orders = new OrderQueue(1, admissionResults);
            var orderTypes = CreateOrderTypes(terminalResults);
            Entity actor = world.Create();
            var blockingOrder = OrderBuilder.CreateStop(
                StopOrderTypeId,
                playerId: 0,
                actor,
                OrderSubmitMode.Immediate,
                submitStep: 0);
            Assert.That(orders.TryEnqueueAssigned(ref blockingOrder), Is.True);
            var spec = new ActionOrderSpec(AiOrderPayloadKind.CastAbility, CastOrderTypeId, OrderSubmitMode.Immediate);
            var ints = new BlackboardIntBuffer();
            ints.Set(AbilitySlotKey, 0);
            var entities = new BlackboardEntityBuffer();

            bool submitted = PlanExecutor.TrySubmitOrder(
                world,
                in spec,
                new[] { new ActionBinding(ActionBindingOp.IntToAbilitySlot, AbilitySlotKey) },
                actor,
                ref ints,
                ref entities,
                submitStep: 1,
                orders,
                orderTypes,
                out int submittedOrderId);

            Assert.That(submitted, Is.False);
            Assert.That(submittedOrderId, Is.Zero);
            Assert.DoesNotThrow(() => terminalResults.Retain(777));
            Assert.That(terminalResults.Release(777), Is.True);
        }

        [Test]
        public void PlanExecution_MultipleAgentsConsumeOnlyTheirOwnTerminalReceipt()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var terminalResults = new OrderTerminalResultBuffer(8);
            var orders = new OrderQueue(8, admissionResults);
            var orderTypes = CreateOrderTypes(terminalResults);
            var library = CreateCastThenStopLibrary();
            var system = new AIPlanExecutionSystem(world, clock, library, orders, orderTypes);

            Entity firstActor = CreateActorWithCastPlan(world);
            Entity secondActor = CreateActorWithCastPlan(world);

            system.Update(1f / 60f);

            Assert.That(orders.TryDequeue(out Order firstOrder), Is.True);
            Assert.That(orders.TryDequeue(out Order secondOrder), Is.True);
            terminalResults.Write(new OrderTerminalOutcome(
                firstOrder.OrderId,
                firstOrder.OrderTypeId,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                firstOrder.Actor));
            terminalResults.Clear();
            clock.Advance(ClockDomainId.Step, 1);

            system.Update(1f / 60f);

            Entity completedActor = firstOrder.Actor;
            Entity waitingActor = firstOrder.Actor == firstActor ? secondActor : firstActor;
            Order waitingOrder = secondOrder.Actor == waitingActor ? secondOrder : firstOrder;
            ref var completedPlan = ref world.Get<AIPlan32>(completedActor);
            ref var waitingPlan = ref world.Get<AIPlan32>(waitingActor);
            Assert.That(completedPlan.Cursor, Is.EqualTo(1));
            Assert.That(completedPlan.WaitingOrderId, Is.Zero);
            Assert.That(waitingPlan.Cursor, Is.EqualTo(0));
            Assert.That(waitingPlan.WaitingOrderId, Is.EqualTo(waitingOrder.OrderId));
        }

        private const int AbilitySlotKey = 1;
        private const int CastOrderTypeId = 123;
        private const int StopOrderTypeId = 126;

        private static Entity CreateActorWithCastPlan(World world)
        {
            var plan = new AIPlan32();
            Assert.That(plan.TryAdd(0), Is.True);
            var ints = new BlackboardIntBuffer();
            ints.Set(AbilitySlotKey, 0);
            return world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                ints,
                new BlackboardEntityBuffer());
        }

        private static OrderTypeRegistry CreateOrderTypes(OrderTerminalResultBuffer terminalResults)
        {
            var registry = new OrderTypeRegistry(terminalResults);
            registry.Register(new OrderTypeConfig
            {
                Key = "castAbility",
                OrderTypeId = CastOrderTypeId,
                PayloadKind = OrderPayloadKind.CastAbility,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            registry.Register(new OrderTypeConfig
            {
                Key = "stop",
                OrderTypeId = StopOrderTypeId,
                PayloadKind = OrderPayloadKind.Stop,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            return registry;
        }

        private static ActionLibraryCompiled256 CreateCastThenStopLibrary()
        {
            return ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.CastAbility, CastOrderTypeId, OrderSubmitMode.Immediate),
                    bindings: new[] { new ActionBinding(ActionBindingOp.IntToAbilitySlot, AbilitySlotKey) }),
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.Stop, StopOrderTypeId, OrderSubmitMode.Immediate),
                    bindings: Array.Empty<ActionBinding>())
            });
        }
    }
}
