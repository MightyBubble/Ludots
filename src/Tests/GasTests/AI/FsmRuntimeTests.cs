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
        public void SentryHfsm_StimulusEntersAlertingSubtree_ThenCyclesToIdle()
        {
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchy("hfsm.sentry");
            var world = new HfsmWorld(hfsm, capacity: 2);
            world.AddAgent();
            Assert.That(world.GetLeafState(0), Is.EqualTo(1)); // Idle
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(1));
            world.LatchStimulus(0);
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(3)); // Alert under Alerting
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(4)); // Combat
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(5)); // Retreat
            world.TickAll();
            Assert.That(world.GetLeafState(0), Is.EqualTo(1)); // Idle
        }

        [Test]
        public void ThinkWave_10k_SentryHfsm_UnderFiveMilliseconds()
        {
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchy("hfsm.perf");
            const int agents = 10_000;
            var world = new HfsmWorld(hfsm, capacity: agents);
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
            HfsmThinkStats stats = world.TickAll();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"A={stats.Agents} preds={stats.PredicatesChecked} taken={stats.TransitionsTaken} T_ai_ms={ms:F3}");
            Assert.That(ms, Is.LessThan(5.0), $"HFSM think wave exceeded 5ms: {ms:F3}ms");
        }
    }
}
