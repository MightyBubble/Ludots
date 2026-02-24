using System;
using Ludots.Core.Modding;
using PerformanceMod.Triggers;

namespace PerformanceMod
{
    public class PerformanceModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("PerformanceMod Loaded!");
            context.TriggerManager.RegisterTrigger(new BenchmarkTrigger());
            context.TriggerManager.RegisterTrigger(new EntryBenchmarkMenuTrigger());
        }

        public void OnUnload()
        {
        }
    }
}
