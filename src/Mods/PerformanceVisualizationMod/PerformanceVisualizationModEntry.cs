using System;
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
            context.TriggerManager.RegisterTrigger(new VisualBenchmarkTrigger(context));
            context.TriggerManager.RegisterTrigger(new VisualBenchmarkEntryMenuTrigger());
            context.TriggerManager.RegisterTrigger(new VisualBenchmarkMapUiTrigger());
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
