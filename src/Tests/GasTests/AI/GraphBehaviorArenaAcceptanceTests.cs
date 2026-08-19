using System.Diagnostics;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Fsm;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.AI
{
    /// <summary>
    /// Headless stand-in for the showcases' think-wave contract before full Raylib mods land.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("ci-gate")]
    public sealed class GraphBehaviorArenaAcceptanceTests
    {
        [Test]
        public void CombinedThinkWaves_60fpsCadence_AiEvery12Frames_StayWithinFrameBudget()
        {
            const int agents = 10_000;
            const int waves = 25; // 5s / 0.2s
            const double combinedWaveBudgetMs = 5.0;
            const double combinedWaveCiEnvelopeMs = 10.0;
            // Showcase default topology N=8; N=16 remains BT-only stress (see BehaviorTreeRuntimeTests).
            _ = Ludots.Tests.Gas.Graph.GraphRegistryTestBootstrap.LoadCoreScriptsFuncLibAndActionLib(
                out _,
                out _,
                out GraphBehaviorCatalog behavior);
            BehaviorTreeDefinition bt = BehaviorTreeFactory.CreateAlwaysSuccessSequence("arena.bt", leafCount: 7);
            HfsmDefinition hfsm = behavior.RequireHfsm("hfsm.sentry");

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

            for (int w = 0; w < 8; w++)
            {
                btWorld.RestartAllThinking();
                btWorld.TickAll(32);
                hfsmWorld.TickAll();
            }

            var samples = new double[waves];
            if (!GC.TryStartNoGCRegion(16 * 1024 * 1024))
            {
                throw new InvalidOperationException("Graph behavior acceptance timing requires a no-GC region.");
            }

            try
            {
                for (int w = 0; w < waves; w++)
                {
                    btWorld.RestartAllThinking();
                    long start = Stopwatch.GetTimestamp();
                    btWorld.TickAll(32);
                    hfsmWorld.TickAll();
                    samples[w] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
            }
            finally
            {
                GC.EndNoGCRegion();
            }

            double sumMs = 0;
            double maxMs = 0;
            int over = 0;
            for (int w = 0; w < waves; w++)
            {
                sumMs += samples[w];
                if (samples[w] > maxMs) maxMs = samples[w];
                if (samples[w] >= combinedWaveBudgetMs) over++;
            }

            double avgMs = sumMs / waves;
            Array.Sort(samples);
            double p95 = samples[(int)(waves * 0.95)];
            TestContext.WriteLine(
                $"waves={waves} A={agents} N_topo={bt.NodeCount} avg={avgMs:F3} p95={p95:F3} max={maxMs:F3} over5ms={over}");
            Warn.If(over, Is.GreaterThan(0), $"Combined think wave exceeded {combinedWaveBudgetMs:F0}ms in {over} of {waves} samples");
            Assert.That(avgMs, Is.LessThan(15.0), $"Combined think avg exceeded 15ms: {avgMs:F3}");
            Assert.That(p95, Is.LessThan(15.0), $"Combined think p95 exceeded 15ms: {p95:F3}");
            Assert.That(maxMs, Is.LessThan(combinedWaveCiEnvelopeMs), $"Combined think max exceeded CI envelope: {maxMs:F3}ms");
        }
    }
}
