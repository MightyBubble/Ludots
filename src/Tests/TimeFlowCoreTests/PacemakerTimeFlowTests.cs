using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using NUnit.Framework;

namespace Ludots.Tests.TimeFlowCore;

[TestFixture]
public sealed class PacemakerTimeFlowTests
{
    [Test]
    public void TurnBasedPacemaker_DoesNotAdvanceQueuedStepsWhileScaledDeltaIsZero()
    {
        float previousFixedDeltaTime = Time.FixedDeltaTime;
        double startFixedTotalTime = Time.FixedTotalTime;
        try
        {
            Time.FixedDeltaTime = 0.02f;
            var pacemaker = new TurnBasedPacemaker();
            var system = new CountingSystem();

            pacemaker.Step();
            pacemaker.Update(0f, system);

            Assert.Multiple(() =>
            {
                Assert.That(system.Updates, Is.EqualTo(0));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime));
            });

            pacemaker.Update(Time.FixedDeltaTime, system);

            Assert.Multiple(() =>
            {
                Assert.That(system.Updates, Is.EqualTo(1));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime + Time.FixedDeltaTime).Within(0.000001d));
            });
        }
        finally
        {
            Time.FixedDeltaTime = previousFixedDeltaTime;
        }
    }

    [Test]
    public void TurnBasedPacemaker_DoesNotAdvanceQueuedCooperativeStepsWhileScaledDeltaIsZero()
    {
        float previousFixedDeltaTime = Time.FixedDeltaTime;
        double startFixedTotalTime = Time.FixedTotalTime;
        try
        {
            Time.FixedDeltaTime = 0.02f;
            var pacemaker = new TurnBasedPacemaker();
            var simulation = new CountingCooperativeSimulation();

            pacemaker.Step();
            pacemaker.Update(0f, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 10);

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Steps, Is.EqualTo(0));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime));
            });

            pacemaker.Update(Time.FixedDeltaTime, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 10);

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Steps, Is.EqualTo(1));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime + Time.FixedDeltaTime).Within(0.000001d));
            });
        }
        finally
        {
            Time.FixedDeltaTime = previousFixedDeltaTime;
        }
    }

    [Test]
    public void RealtimePacemaker_DoesNotContinueCooperativeStepWhileScaledDeltaIsZero()
    {
        float previousFixedDeltaTime = Time.FixedDeltaTime;
        double startFixedTotalTime = Time.FixedTotalTime;
        try
        {
            Time.FixedDeltaTime = 0.02f;
            var pacemaker = new RealtimePacemaker();
            var simulation = new YieldThenCompleteCooperativeSimulation();

            pacemaker.Update(Time.FixedDeltaTime, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 10);

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Steps, Is.EqualTo(1));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime));
            });

            pacemaker.Update(0f, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 10);

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Steps, Is.EqualTo(1));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime));
            });

            pacemaker.Update(float.Epsilon, simulation, timeBudgetMs: 1, maxSlicesPerLogicFrame: 10);

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Steps, Is.EqualTo(2));
                Assert.That(Time.FixedTotalTime, Is.EqualTo(startFixedTotalTime + Time.FixedDeltaTime).Within(0.000001d));
            });
        }
        finally
        {
            Time.FixedDeltaTime = previousFixedDeltaTime;
        }
    }

    private sealed class CountingSystem : ISystem<float>
    {
        public int Updates { get; private set; }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            Updates++;
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CountingCooperativeSimulation : ICooperativeSimulation
    {
        public int Steps { get; private set; }

        public bool Step(float fixedDt, int timeBudgetMs)
        {
            Steps++;
            return true;
        }

        public void Reset()
        {
        }
    }

    private sealed class YieldThenCompleteCooperativeSimulation : ICooperativeSimulation
    {
        public int Steps { get; private set; }

        public bool Step(float fixedDt, int timeBudgetMs)
        {
            Steps++;
            return Steps == 2;
        }

        public void Reset()
        {
        }
    }
}
