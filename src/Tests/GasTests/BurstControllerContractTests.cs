using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    public sealed class BurstControllerContractTests
    {
        [Test]
        public void Physics2DRunUntilSleepingCompletesOnlyAfterExternalPhysicsFixedTickCallback()
        {
            using World world = World.Create();
            world.Create(new Physics2DRuntimeState { AnyAwakeDynamicBodies = false });

            var policy = new Physics2DTickPolicy(targetHz: 0, maxStepsPerFixedTick: 4);
            var events = new List<EventKey>();
            ScriptContext? completed = null;
            var controller = new Physics2DController(
                world,
                policy,
                defaultHz: 60,
                () => new ScriptContext(),
                (key, ctx) =>
                {
                    events.Add(key);
                    if (key == GameEvents.Physics2DRunCompleted)
                    {
                        completed = ctx;
                    }
                });

            controller.RunUntilSleeping(maxFixedTicks: 4);

            Assert.Multiple(() =>
            {
                Assert.That(controller.TargetHz, Is.EqualTo(60));
                Assert.That(events, Does.Contain(GameEvents.Physics2DRunStarted));
                Assert.That(events, Does.Not.Contain(GameEvents.Physics2DRunCompleted));
            });

            controller.AfterPhysicsFixedTick();

            Assert.Multiple(() =>
            {
                Assert.That(controller.TargetHz, Is.EqualTo(0));
                Assert.That(events, Does.Contain(GameEvents.Physics2DRunCompleted));
                Assert.That(completed?.Get<string>("Reason"), Is.EqualTo("Sleeping"));
            });
        }

        [Test]
        public void GasRunUntilEffectWindowsClosedSchedulesTurnBasedStepsUntilRuntimeIsIdle()
        {
            using World world = World.Create();
            world.Create(new GasRuntimeState { HasPendingEffects = true });
            using var engine = new GameEngine();
            var loop = new SimulationLoopController(engine);
            var policy = new GasClockStepPolicy(stepEveryFixedTicks: 1, GasStepMode.Manual);
            var simulation = new CountingSimulation();
            var events = new List<EventKey>();
            var controller = new GasController(
                world,
                policy,
                loop,
                () => new ScriptContext(),
                (key, _) => events.Add(key));

            controller.RunUntilEffectWindowsClosed(maxFixedTicks: 4);
            engine.Pacemaker.Update(Time.FixedDeltaTime, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 1);

            controller.AfterFixedTick();
            engine.Pacemaker.Update(Time.FixedDeltaTime, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 1);

            Assert.Multiple(() =>
            {
                Assert.That(loop.Mode, Is.EqualTo(SimulationLoopMode.TurnBased));
                Assert.That(simulation.StepCount, Is.EqualTo(2));
                Assert.That(controller.IsRunning, Is.True);
                Assert.That(events, Does.Contain(GameEvents.GasRunStarted));
                Assert.That(events, Does.Not.Contain(GameEvents.GasRunCompleted));
            });
        }

        private sealed class CountingSimulation : ICooperativeSimulation
        {
            public int StepCount { get; private set; }

            public bool Step(float fixedDt, int timeBudgetMs)
            {
                StepCount++;
                return true;
            }

            public void Reset()
            {
            }
        }
    }
}
