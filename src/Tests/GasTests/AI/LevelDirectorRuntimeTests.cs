using System.Diagnostics;
using Ludots.Core.Gameplay.Level;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class LevelDirectorRuntimeTests
    {
        [Test]
        public void TwoPhaseTrial_AdvancesOnWavesAndCounter()
        {
            LevelDirector director = LevelBlueprintFactory.CreateTwoPhaseTrial("level.trial");
            var host = new GraphProgramLevelHost(LevelScriptPrograms.CreateTwoPhaseTrialPrograms());
            Assert.That(director.Phase, Is.EqualTo(0));
            director.TickThinkWave(host);
            Assert.That(director.Phase, Is.EqualTo(1));
            director.AddCounter(10);
            director.TickThinkWave(host);
            Assert.That(director.Phase, Is.EqualTo(2));
            director.PulseManual(2, host);
            Assert.That(host.LastRanGraphId, Is.EqualTo(LevelBlueprintFactory.PhaseAdvanceScriptGraphId));
            Assert.That(director.LastSignal, Is.EqualTo(LevelBlueprintFactory.PhaseAdvanceScriptGraphId));
        }

        [Test]
        public void Stress_128Triggers_10kPeakUnitsMarker_UnderFiveMilliseconds()
        {
            // Peak units are host-side; director cost must stay tiny even with 128 armed triggers.
            var actions = new LevelActionDef[128];
            var triggers = new LevelTriggerDef[128];
            for (int i = 0; i < 128; i++)
            {
                actions[i] = new LevelActionDef(LevelActionKind.EmitSignal, arg0: i, arg1: 0);
                triggers[i] = new LevelTriggerDef(LevelTriggerKind.ElapsedThinkWaves, threshold: 1_000_000 + i, actionIndex: i);
            }

            var director = new LevelDirector("level.stress", triggers, actions);
            const int peakUnits = 10_000; // documented coupling for matrix M5
            var sw = Stopwatch.StartNew();
            LevelThinkStats stats = director.TickThinkWave();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            TestContext.WriteLine(
                $"armed={director.TriggerCount} peakUnits={peakUnits} checked={stats.TriggersChecked} T_ms={ms:F3}");
            Assert.That(ms, Is.LessThan(5.0));
            Assert.That(stats.TriggersChecked, Is.EqualTo(128));
        }
    }
}
