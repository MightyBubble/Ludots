using System;
using System.Diagnostics;
using Arch.Core;
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
    public class AiBenchmarkTests
    {
        [Test]
        public void Benchmark_AI_10kAgents_ZeroAlloc()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 20000);

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
                    orderSpec: new ActionOrderSpec(orderTagId: 1234, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: Array.Empty<ActionBinding>())
            });

            var goalSys = new AIGoalSelectionSystem(world, selector);
            var planner = new GoapAStarPlanner256(maxNodes: 128);
            var goapSys = new GoapPlanningSystem(world, planner, lib, goalTable);
            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders);

            const int agentCount = 10_000;
            for (int i = 0; i < agentCount; i++)
            {
                world.Create(
                    new AIAgent(),
                    new AIWorldState256 { Bits = default, Version = 1 },
                    new AIGoalSelection(),
                    new AIPlanningState(),
                    new AIPlan32(),
                    new GameplayTagContainer(),
                    new BlackboardIntBuffer(),
                    new BlackboardEntityBuffer()
                );
            }

            for (int i = 0; i < 10; i++)
            {
                goalSys.Update(1f / 60f);
                goapSys.Update(1f / 60f);
                execSys.Update(1f / 60f);
                clock.Advance(ClockDomainId.Step, 1);
                orders.Clear();
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
                goalSys.Update(1f / 60f);
                goapSys.Update(1f / 60f);
                execSys.Update(1f / 60f);
                clock.Advance(ClockDomainId.Step, 1);
                orders.Clear();
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
        public void Regression_AIPlanExecution_SubmitsOrders()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(capacity: 128);

            var lib = ActionLibraryCompiled256.Compile(new[]
            {
                new ActionOpDefinition256(
                    preMask: default,
                    preValues: default,
                    postMask: default,
                    postValues: default,
                    cost: 1,
                    executorKind: ActionExecutorKind.SubmitOrder,
                    orderSpec: new ActionOrderSpec(orderTagId: 5678, submitMode: OrderSubmitMode.Immediate, playerId: 0),
                    bindings: Array.Empty<ActionBinding>())
            });

            var execSys = new AIPlanExecutionSystem(world, clock, lib, orders);

            var plan = new AIPlan32();
            plan.TryAdd(0);
            world.Create(
                new AIAgent(),
                plan,
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer()
            );

            execSys.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
        }
    }
}
