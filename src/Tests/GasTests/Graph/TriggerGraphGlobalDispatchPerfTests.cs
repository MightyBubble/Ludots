using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// #1123 performance acceptance: "50 map × 200 trigger 只随订阅者数增长".
    /// A world with 50 maps × 200 map-table triggers (10,000 total) but only 3
    /// global subscribers must make FireGlobalEvent cost the same as a 3-subscriber
    /// map fire in a tiny world: zero steady-state allocation and a bounded timing
    /// ratio against the small-world baseline.
    /// </summary>
    [TestFixture]
    [Category("benchmark")]
    public sealed class TriggerGraphGlobalDispatchPerfTests
    {
        private const int MapCount = 50;
        private const int TriggersPerMap = 200;
        private const int GlobalSubscribers = 3;
        private const int SmallMapCount = 3;
        private const int SmallTriggersPerMap = 3;
        private const int Iterations = 100_000;
        private const int WarmupIterations = 2_048;

        [Test]
        public void Benchmark_GlobalEventDispatch_ScalesWithSubscribersOnly()
        {
            EventKey eventKey = new("perf.global.event");

            // Scaled world: 50 maps × 200 map-table triggers on the same event key
            // (worst case: any accidental map-table scan during global fire would pay
            // the 10,000-trigger price) plus exactly 3 global subscriptions.
            var manager = new TriggerManager();
            var mapProbe = new NoOpTrigger(eventKey, 0);
            for (int m = 0; m < MapCount; m++)
            {
                MapId mapId = new($"perf-global-map-{m}");
                var triggers = new List<Trigger>(TriggersPerMap);
                for (int t = 0; t < TriggersPerMap; t++)
                {
                    triggers.Add(m == 0 && t == 0 ? mapProbe : new NoOpTrigger(eventKey, t % 4));
                }

                manager.RegisterMapTriggers(mapId, triggers);
            }

            MapId globalHome = new("perf-global-home");
            var globals = new List<Trigger>(GlobalSubscribers);
            for (int g = 0; g < GlobalSubscribers; g++)
            {
                globals.Add(new NoOpTrigger(eventKey, g));
            }

            manager.RegisterGlobalTriggers(globalHome, globals);

            // Small-world baseline: 3 maps × 3 triggers, one map fired (3 subscribers).
            var smallManager = new TriggerManager();
            MapId smallFiredMap = new("perf-global-small-0");
            for (int m = 0; m < SmallMapCount; m++)
            {
                MapId mapId = new($"perf-global-small-{m}");
                var triggers = new List<Trigger>(SmallTriggersPerMap);
                for (int t = 0; t < SmallTriggersPerMap; t++)
                {
                    triggers.Add(new NoOpTrigger(eventKey, t));
                }

                smallManager.RegisterMapTriggers(mapId, triggers);
            }

            var context = new ScriptContext();
            for (int i = 0; i < WarmupIterations; i++)
            {
                manager.FireGlobalEvent(eventKey, context);
                smallManager.FireMapEvent(smallFiredMap, eventKey, context);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                manager.FireGlobalEvent(eventKey, context);
            }

            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            double globalUsPerEvent = stopwatch.Elapsed.TotalMilliseconds * 1000d / Iterations;

            var smallStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                smallManager.FireMapEvent(smallFiredMap, eventKey, context);
            }

            smallStopwatch.Stop();
            double smallUsPerEvent = smallStopwatch.Elapsed.TotalMilliseconds * 1000d / Iterations;

            double ratio = globalUsPerEvent / Math.Max(smallUsPerEvent, 0.0001);
            Console.WriteLine(
                "[TriggerGraphPerf] GlobalDispatch globalSubscribers={0} mapTable={1}x{2} iterations={3} " +
                "globalUsPerEvent={4:F4} smallMapUsPerEvent={5:F4} ratio={6:F2} allocatedBytes={7}",
                GlobalSubscribers, MapCount, TriggersPerMap, Iterations,
                globalUsPerEvent, smallUsPerEvent, ratio, allocatedBytes);

            Assert.That(((NoOpTrigger)globals[0]).InvocationCount, Is.EqualTo(Iterations + WarmupIterations),
                "every global fire must dispatch every global subscriber");
            Assert.That(mapProbe.InvocationCount, Is.EqualTo(0),
                "map-table triggers must never run from a global fire, no matter the map volume");
            Assert.That(allocatedBytes, Is.LessThanOrEqualTo(64),
                $"Steady-state global dispatch allocated {allocatedBytes} bytes for {GlobalSubscribers} subscribers " +
                $"while {MapCount * TriggersPerMap} map triggers are live.");

            // Hard ceiling generous enough for CI jitter; the Warn.If soft gate below
            // flags the real contract (same order of magnitude as the 3-subscriber
            // small-world map fire) without failing on noisy runners.
            Assert.That(ratio, Is.LessThanOrEqualTo(8.0),
                $"FireGlobalEvent took {globalUsPerEvent:F4}us vs small-map {smallUsPerEvent:F4}us " +
                $"(ratio {ratio:F2}); global dispatch must scale with subscriber count, not map volume.");
            Warn.If(ratio, Is.GreaterThanOrEqualTo(3.0),
                $"FireGlobalEvent ratio {ratio:F2} vs 3-subscriber small-map baseline drifted above 3x.");
        }

        private sealed class NoOpTrigger : Trigger
        {
            public NoOpTrigger(EventKey eventKey, int priority)
            {
                EventKey = eventKey;
                Priority = priority;
            }

            public int InvocationCount { get; private set; }

            public override Task ExecuteAsync(ScriptContext context)
            {
                InvocationCount++;
                return Task.CompletedTask;
            }
        }
    }
}
