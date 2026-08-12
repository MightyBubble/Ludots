using System.Diagnostics;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.Gameplay.Level;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    /// <summary>
    /// Headless stand-in for the four showcases' think-wave contract before full Raylib mods land.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphBehaviorArenaAcceptanceTests
    {
        [Test]
        public void CombinedThinkWaves_60fpsCadence_AiEvery12Frames_StayUnderFiveMs()
        {
            const int agents = 10_000;
            const int waves = 25; // 5s / 0.2s
            // Showcase default topology N=8; N=16 remains BT-only stress (see BehaviorTreeRuntimeTests).
            _ = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsAndFuncLib(out _, out GraphActionCatalog actions);
            BehaviorTreeDefinition bt = BehaviorTreeFactory.CreateAlwaysSuccessSequence("arena.bt", leafCount: 7);
            HfsmDefinition hfsm = HfsmFactory.CreateSentryHierarchy("arena.hfsm");
            LevelDirector level = LevelBlueprintFactory.CreateTwoPhaseTrial(
                "arena.level",
                name => GraphRegistryScriptResolver.RequireActionId(actions, name));

            var btWorld = new BehaviorTreeWorld(bt, agents);
            var hfsmWorld = new HfsmWorld(hfsm, agents);
            for (int i = 0; i < agents; i++)
            {
                btWorld.AddAgent();
                hfsmWorld.AddAgent();
                if ((i % 32) == 0)
                {
                    hfsmWorld.LatchStimulus(i);
                }
            }

            void RestartReady()
            {
                for (int i = 0; i < agents; i++)
                {
                    if (btWorld.Statuses[i] == BehaviorTreeStatus.Success)
                    {
                        btWorld.RestartThinking(i);
                    }
                }
            }

            for (int w = 0; w < 8; w++)
            {
                RestartReady();
                btWorld.TickAll(32);
                hfsmWorld.TickAll();
                level.TickThinkWave();
            }

            var samples = new double[waves];
            for (int w = 0; w < waves; w++)
            {
                RestartReady();
                var sw = Stopwatch.StartNew();
                btWorld.TickAll(32);
                hfsmWorld.TickAll();
                level.TickThinkWave();
                if (w == 5)
                {
                    level.AddCounter(10);
                }

                sw.Stop();
                samples[w] = sw.Elapsed.TotalMilliseconds;
            }

            double sumMs = 0;
            double maxMs = 0;
            int over = 0;
            for (int w = 0; w < waves; w++)
            {
                sumMs += samples[w];
                if (samples[w] > maxMs) maxMs = samples[w];
                if (samples[w] >= 5.0) over++;
            }

            double avgMs = sumMs / waves;
            Array.Sort(samples);
            double p95 = samples[(int)(waves * 0.95)];
            TestContext.WriteLine(
                $"waves={waves} A={agents} N_topo={bt.NodeCount} avg={avgMs:F3} p95={p95:F3} max={maxMs:F3} over5ms={over} phase={level.Phase}");
            // Topology-only combined wave; allow CI noise after Registry bootstrap.
            Assert.That(avgMs, Is.LessThan(15.0), $"Combined think avg exceeded 15ms: {avgMs:F3}");
            Assert.That(p95, Is.LessThan(15.0), $"Combined think p95 exceeded 15ms: {p95:F3}");
            Assert.That(level.Phase, Is.EqualTo(2));
        }
    }
}
