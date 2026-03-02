using System;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using PerformanceMod.Triggers;

namespace PerformanceMod
{
    public class PerformanceModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("PerformanceMod Loaded!");
            var benchTrigger = new BenchmarkTrigger();
            context.OnEvent(GameEvents.MapLoaded, ctx => benchTrigger.CheckConditions(ctx) ? benchTrigger.ExecuteAsync(ctx) : Task.CompletedTask);
            var entryTrigger = new EntryBenchmarkMenuTrigger();
            context.OnEvent(GameEvents.MapLoaded, ctx => entryTrigger.CheckConditions(ctx) ? entryTrigger.ExecuteAsync(ctx) : Task.CompletedTask);
        }

        public void OnUnload()
        {
        }
    }
}
