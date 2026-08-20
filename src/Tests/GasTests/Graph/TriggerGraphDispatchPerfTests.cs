using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("benchmark")]
    public sealed class TriggerGraphDispatchPerfTests
    {
        private const int TriggerCount = 16;
        private const int Iterations = 100_000;

        [Test]
        public void Benchmark_MapEventDispatch_SteadyState()
        {
            MapId mapId = new("trigger-graph-perf");
            EventKey eventKey = new("perf.map.event");
            var manager = new TriggerManager();
            var triggers = new List<Trigger>(TriggerCount);
            for (int i = 0; i < TriggerCount; i++)
            {
                triggers.Add(new NoOpTrigger(eventKey, i % 4));
            }

            manager.RegisterMapTriggers(mapId, triggers);
            var context = new ScriptContext();
            for (int i = 0; i < 2_048; i++)
            {
                manager.FireMapEvent(mapId, eventKey, context);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Stopwatch stopwatch = new();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Start();
            for (int i = 0; i < Iterations; i++)
            {
                manager.FireMapEvent(mapId, eventKey, context);
            }

            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            double microsecondsPerEvent = stopwatch.Elapsed.TotalMilliseconds * 1000d / Iterations;

            Console.WriteLine(
                $"[TriggerGraphPerf] MapEventDispatch triggers={TriggerCount} iterations={Iterations} " +
                $"totalMs={stopwatch.Elapsed.TotalMilliseconds:F2} usPerEvent={microsecondsPerEvent:F4} " +
                $"allocatedBytes={allocatedBytes}");

            Assert.That(((NoOpTrigger)triggers[0]).InvocationCount, Is.EqualTo(Iterations + 2_048));
            Assert.That(allocatedBytes, Is.LessThanOrEqualTo(64),
                $"Steady-state map event dispatch allocated {allocatedBytes} bytes.");
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
