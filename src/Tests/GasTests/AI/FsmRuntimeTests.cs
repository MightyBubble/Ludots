using System.Diagnostics;
using Ludots.Core.Gameplay.AI.Fsm;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class FsmRuntimeTests
    {
        [Test]
        public void SentryLoop_StimulusAdvancesIdleToAlert()
        {
            FsmDefinition fsm = FsmFactory.CreateSentryLoop("fsm.sentry");
            var world = new FsmWorld(fsm, capacity: 2);
            world.AddAgent();
            world.AddAgent();
            Assert.That(world.States[0], Is.EqualTo(0));
            world.TickAll();
            Assert.That(world.States[0], Is.EqualTo(0));
            world.LatchStimulus(0);
            world.TickAll();
            Assert.That(world.States[0], Is.EqualTo(1));
            world.TickAll();
            Assert.That(world.States[0], Is.EqualTo(2));
        }

        [Test]
        public void ThinkWave_10k_Sentry_UnderFiveMilliseconds()
        {
            FsmDefinition fsm = FsmFactory.CreateSentryLoop("fsm.perf");
            const int agents = 10_000;
            var world = new FsmWorld(fsm, capacity: agents);
            for (int i = 0; i < agents; i++)
            {
                world.AddAgent();
                if ((i & 1) == 0)
                {
                    world.LatchStimulus(i);
                }
            }

            world.TickAll();
            var sw = Stopwatch.StartNew();
            FsmThinkStats stats = world.TickAll();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"A={stats.Agents} preds={stats.PredicatesChecked} taken={stats.TransitionsTaken} T_ai_ms={ms:F3}");
            Assert.That(ms, Is.LessThan(5.0), $"FSM think wave exceeded 5ms: {ms:F3}ms");
        }
    }
}
