using System;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using PerformanceVisualizationMod.Triggers;

namespace PerformanceVisualizationMod
{
    public class PerformanceVisualizationModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[PerformanceVisualizationMod] Loaded!");
            context.OnEvent(VisualBenchmarkEvents.RunVisualBenchmark, new VisualBenchmarkTrigger(context).ExecuteAsync);
            var entryTrigger = new VisualBenchmarkEntryMenuTrigger();
            context.OnEvent(GameEvents.MapLoaded, ctx => entryTrigger.CheckConditions(ctx) ? entryTrigger.ExecuteAsync(ctx) : Task.CompletedTask);
            var mapUiTrigger = new VisualBenchmarkMapUiTrigger();
            context.OnEvent(GameEvents.MapLoaded, ctx => mapUiTrigger.CheckConditions(ctx) ? mapUiTrigger.ExecuteAsync(ctx) : Task.CompletedTask);
        }

        public void OnUnload()
        {
        }
    }

    public static class VisualBenchmarkMapIds
    {
        public static readonly MapId VisualBenchmark = new MapId("visual_benchmark");
    }

    public static class VisualBenchmarkEvents
    {
        public static readonly EventKey RunVisualBenchmark = new EventKey("RunVisualBenchmark");
    }
}
