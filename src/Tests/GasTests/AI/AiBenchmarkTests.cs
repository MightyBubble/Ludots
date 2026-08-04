using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public class AiBenchmarkTests
    {
        [Test]
        public void Benchmark_AI_10kAgents_ZeroAlloc()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(10000, 10000);
            var orders = new OrderQueue(capacity: 20000, admissionResults);
            var terminalResults = new OrderTerminalResultBuffer(capacity: 20000);
            var orderTypes = CreatePlanningOrderTypes(terminalResults);

            var selector = UtilityGoalSelectorCompiled256.Compile(new[]
            {
                new UtilityGoalPresetDefinition(goalPresetId: 1, planningStrategyId: AIPlanningStrategyIds.Goap, weight: 1f, considerations: Array.Empty<UtilityConsiderationBool256>())
            }, enableCompensationFactor: false, enableMomentumBonus: false);

            var atomMask = new WorldStateBits256();
            atomMask.SetBit(0, true);
            var atomValues = new WorldStateBits256();
            atomValues.SetBit(0, true);
            var goal = new WorldStateCondition256(in atomMask, in atomValues);
            var goalTable = new GoapGoalTable256(new[]
            {
                new GoapGoalPreset256(goalPresetId: 1, goal: in goal, heuristicWeight: 1)
            });

            var preMask = new WorldStateBits256();
            var preValues = new WorldStateBits256();
            var postMask = new WorldStateBits256();
            postMask.SetBit(0, true);
            var postValues = new WorldStateBits256();
            postValues.SetBit(0, true);

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: in preMask,
                    preValues: in preValues,
                    postMask: in postMask,
                    postValues: in postValues,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.CastAbility, orderTypeId: 123, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: new[] { new ActionBinding(ActionBindingOp.IntToAbilitySlot, sourceKey: 1) })
            });

            var goalSys = new AIGoalSelectionSystem(world, selector);
            var planner = new GoapAStarPlanner256(maxNodes: 128);
            var goapSys = new GoapPlanningSystem(world, planner, lib, goalTable);
            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders, orderTypes);

            const int agentCount = 10_000;
            for (int i = 0; i < agentCount; i++)
            {
                var ints = new BlackboardIntBuffer();
                ints.Set(1, 0);
                world.Create(
                    new AIAgent(),
                    new AIWorldState256 { Bits = default, Version = 1 },
                    new AIGoalSelection(),
                    new AIPlanningState(),
                    new AIPlan32(),
                    OrderBuffer.CreateEmpty(),
                    new GameplayTagContainer(),
                    ints,
                    new BlackboardEntityBuffer()
                );
            }

            for (int i = 0; i < 10; i++)
            {
                terminalResults.Clear();
                admissionResults.BeginLogicStep();
                goalSys.Update(1f / 60f);
                goapSys.Update(1f / 60f);
                execSys.Update(1f / 60f);
                DrainSubmittedOrdersAsCompleted(orders, terminalResults);
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
                clock.Advance(ClockDomainId.Step, 1);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            const int iterations = 120;
            for (int i = 0; i < iterations; i++)
            {
                terminalResults.Clear();
                admissionResults.BeginLogicStep();
                goalSys.Update(1f / 60f);
                goapSys.Update(1f / 60f);
                execSys.Update(1f / 60f);
                DrainSubmittedOrdersAsCompleted(orders, terminalResults);
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
                clock.Advance(ClockDomainId.Step, 1);
            }

            sw.Stop();
            long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

            double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            Console.WriteLine($"[Benchmark] AI Pipeline (Utility+GOAP+OrderSubmit)");
            Console.WriteLine($"  Agents: {agentCount}");
            Console.WriteLine($"  Iterations: {iterations}");
            Console.WriteLine($"  Total Time: {sw.Elapsed.TotalMilliseconds:F2}ms");
            Console.WriteLine($"  Avg per Tick: {avgMs:F4}ms");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {afterAlloc - beforeAlloc}");

            Assert.That(afterAlloc - beforeAlloc, Is.LessThanOrEqualTo(64));
        }

        [Test]
        public void Regression_AIPlanExecution_SubmitsCastAbilityOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 128, new OrderAdmissionResultBuffer(128, 128));
            var orderTypes = CreatePlanningOrderTypes(new OrderTerminalResultBuffer(128));

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.CastAbility, orderTypeId: 123, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: new[] { new ActionBinding(ActionBindingOp.IntToAbilitySlot, sourceKey: 1) })
            });

            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders, orderTypes);

            var plan = new AIPlan32();
            plan.TryAdd(0);
            var ints = new BlackboardIntBuffer();
            ints.Set(1, 0);
            world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                ints,
                new BlackboardEntityBuffer()
            );

            execSys.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryPeek(out var order), Is.True);
            Assert.That(order.Args.I0, Is.EqualTo(0));
        }

        [Test]
        public void Regression_AIPlanExecution_SubmitsTargetEntityOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 128, new OrderAdmissionResultBuffer(128, 128));
            var orderTypes = CreatePlanningOrderTypes(new OrderTerminalResultBuffer(128));

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.TargetEntity, orderTypeId: 124, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: new[] { new ActionBinding(ActionBindingOp.EntityToTarget, sourceKey: 2) })
            });

            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders, orderTypes);
            Entity target = world.Create();

            var plan = new AIPlan32();
            plan.TryAdd(0);
            var entities = new BlackboardEntityBuffer();
            entities.Set(2, target);
            world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                entities
            );

            execSys.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryPeek(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(124));
            Assert.That(order.Target, Is.EqualTo(target));
            Assert.That(order.Args.I0, Is.EqualTo(0));
        }

        [Test]
        public void Regression_AIPlanExecution_SubmitsMoveToWorldCmOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 128, new OrderAdmissionResultBuffer(128, 128));
            var orderTypes = CreatePlanningOrderTypes(new OrderTerminalResultBuffer(128));

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.MoveToWorldCm, orderTypeId: 125, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: new[] { new ActionBinding(ActionBindingOp.EntityPositionToMoveDestination, sourceKey: 3) })
            });

            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders, orderTypes);
            Entity destination = world.Create(WorldPositionCm.FromCm(120, -45));

            var plan = new AIPlan32();
            plan.TryAdd(0);
            var entities = new BlackboardEntityBuffer();
            entities.Set(3, destination);
            world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                entities
            );

            execSys.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryPeek(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(125));
            Assert.That(order.Target, Is.EqualTo(Entity.Null));
            Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(order.Args.Spatial.WorldCm, Is.EqualTo(new Vector3(120f, 0f, -45f)));
        }

        [Test]
        public void Regression_AIPlanExecution_SubmitsStopOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 128, new OrderAdmissionResultBuffer(128, 128));
            var orderTypes = CreatePlanningOrderTypes(new OrderTerminalResultBuffer(128));

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.Stop, orderTypeId: 126, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: Array.Empty<ActionBinding>())
            });

            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders, orderTypes);

            var plan = new AIPlan32();
            plan.TryAdd(0);
            Entity actor = world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer()
            );

            execSys.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryPeek(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(126));
            Assert.That(order.Actor, Is.EqualTo(actor));
            Assert.That(order.Target, Is.EqualTo(Entity.Null));
            Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.None));
        }

        [Test]
        public void Regression_AIPlanExecution_ThrowsForUntypedOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 128, new OrderAdmissionResultBuffer(128, 128));
            var orderTypes = CreatePlanningOrderTypes(new OrderTerminalResultBuffer(128));

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(AiOrderPayloadKind.None, orderTypeId: 123, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: Array.Empty<ActionBinding>())
            });

            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders, orderTypes);

            var plan = new AIPlan32();
            plan.TryAdd(0);
            world.Create(
                new AIAgent(),
                plan,
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer()
            );

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => execSys.Update(1f / 60f))!;

            Assert.That(ex.Message, Does.Contain("ORDER.BUILDER.ERR.UnsupportedAiOrderPayloadKind"));
            Assert.That(orders.Count, Is.EqualTo(0));
        }

        private static OrderTypeRegistry CreatePlanningOrderTypes(OrderTerminalResultBuffer terminalResults)
        {
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "castAbility",
                OrderTypeId = 123,
                PayloadKind = OrderPayloadKind.CastAbility,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "targetEntity",
                OrderTypeId = 124,
                PayloadKind = OrderPayloadKind.TargetEntity,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "move",
                OrderTypeId = 125,
                PayloadKind = OrderPayloadKind.MoveToWorldCm,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "stop",
                OrderTypeId = 126,
                PayloadKind = OrderPayloadKind.Stop,
                SpatialBlackboardKey = -1,
                EntityBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            return orderTypes;
        }

        private static void DrainSubmittedOrdersAsCompleted(OrderQueue orders, OrderTerminalResultBuffer terminalResults)
        {
            while (orders.TryDequeue(out Order order))
            {
                terminalResults.Write(new OrderTerminalOutcome(
                    order.OrderId,
                    order.OrderTypeId,
                    OrderTerminalState.Completed,
                    OrderFailureReason.None,
                    order.Actor));
            }
        }
    }
}
