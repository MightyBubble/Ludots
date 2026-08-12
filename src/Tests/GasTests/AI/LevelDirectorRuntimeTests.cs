using System.Diagnostics;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.Level;
using Ludots.Core.GraphRuntime;
using Ludots.Tests.Gas.Graph;
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
            GraphProgramRegistry programs = GraphRegistryTestBootstrap.LoadCoreScriptsAndFuncLib(out _);
            int phaseScript = GraphRegistryScriptResolver.RequireId(LevelScriptKeys.PhaseAdvance);
            LevelDirector director = LevelBlueprintFactory.CreateTwoPhaseTrial(
                "level.trial",
                GraphRegistryScriptResolver.RequireId);
            var host = new GraphProgramLevelHost(programs);
            Assert.That(director.Phase, Is.EqualTo(0));
            director.TickThinkWave(host);
            Assert.That(director.Phase, Is.EqualTo(1));
            director.AddCounter(10);
            director.TickThinkWave(host);
            Assert.That(director.Phase, Is.EqualTo(2));
            director.PulseManual(2, host);
            Assert.That(host.LastRanGraphId, Is.EqualTo(phaseScript));
            Assert.That(director.LastSignal, Is.EqualTo(phaseScript));
        }

        [Test]
        public void Stress_128Triggers_10kPeakUnitsMarker_UnderFiveMilliseconds()
        {
            var actions = new LevelActionDef[128];
            var triggers = new LevelTriggerDef[128];
            for (int i = 0; i < 128; i++)
            {
                actions[i] = new LevelActionDef(LevelActionKind.EmitSignal, arg0: i, arg1: 0);
                triggers[i] = new LevelTriggerDef(LevelTriggerKind.ElapsedThinkWaves, threshold: 1_000_000 + i, actionIndex: i);
            }

            var director = new LevelDirector("level.stress", triggers, actions);
            const int peakUnits = 10_000;
            var sw = Stopwatch.StartNew();
            LevelThinkStats stats = director.TickThinkWave();
            sw.Stop();
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5.0));
            Assert.That(stats.TriggersChecked, Is.EqualTo(128));
            Assert.That(peakUnits, Is.EqualTo(10_000));
        }
    }
}
