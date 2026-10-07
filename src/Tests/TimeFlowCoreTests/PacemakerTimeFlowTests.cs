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

    [Test]
    public void RealtimePacemaker_DtSpike_ConsumesAtMostClampedTicks()
    {
        float previousFixedDeltaTime = Time.FixedDeltaTime;
        try
        {
            Time.FixedDeltaTime = 0.02f;
            var pacemaker = new RealtimePacemaker { MaxAccumulatedSeconds = 0.1 };
            var system = new CountingSystem();

            pacemaker.Update(1f, system);

            Assert.That(system.Updates, Is.EqualTo(5),
                "a 1s spike must consume at most MaxAccumulatedSeconds/fdt ticks (5 at 50ms fdt, 100ms clamp).");

            pacemaker.Update(0.02f, system);
            Assert.That(system.Updates, Is.EqualTo(6),
                "after the clamp the leftover backlog is bounded, not a second spike.");
        }
        finally
        {
            Time.FixedDeltaTime = previousFixedDeltaTime;
        }
    }

    [Test]
    public void RealtimePacemaker_SteadyFrameStream_UnaffectedByClamp()
    {
        float previousFixedDeltaTime = Time.FixedDeltaTime;
        try
        {
            Time.FixedDeltaTime = 0.02f;
            var pacemaker = new RealtimePacemaker { MaxAccumulatedSeconds = 0.1 };
            var system = new CountingSystem();

            for (int i = 0; i < 60; i++)
            {
                pacemaker.Update(1f / 60f, system);
            }

            Assert.That(system.Updates, Is.EqualTo(50),
                "60 frames of 16.67ms = 1s = exactly 50 ticks at 20ms fdt; the clamp never engages.");
        }
        finally
        {
            Time.FixedDeltaTime = previousFixedDeltaTime;
        }
    }

    [Test]
    public void RealtimePacemaker_ClampDuringInFlightCooperativeStep_StepStillCompletes()
    {
        float previousFixedDeltaTime = Time.FixedDeltaTime;
        try
        {
            Time.FixedDeltaTime = 0.05f;
            var pacemaker = new RealtimePacemaker { MaxAccumulatedSeconds = 0.1 };
            var simulation = new YieldThenCompleteCooperativeSimulation();

            pacemaker.Update(0.05f, simulation, timeBudgetMs: 10, maxSlicesPerLogicFrame: 10);
            Assert.That(simulation.Steps, Is.EqualTo(1));
            Assert.That(pacemaker.IsBudgetFused, Is.False);

            // Host stall while the first step is still in flight: the clamp must keep enough
            // backlog for the in-flight step to complete instead of starving it.
            pacemaker.Update(1f, simulation, timeBudgetMs: 10, maxSlicesPerLogicFrame: 10);

            Assert.That(simulation.Steps, Is.EqualTo(2),
                "the in-flight step must complete on the next frame despite the clamp.");
            Assert.That(pacemaker.IsBudgetFused, Is.False);
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
