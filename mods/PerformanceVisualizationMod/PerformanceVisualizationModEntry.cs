using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using PerformanceVisualizationMod.Runtime;
using PerformanceVisualizationMod.Triggers;

namespace PerformanceVisualizationMod
{
    public sealed class PerformanceVisualizationModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[PerformanceVisualizationMod] Loaded.");

            var runtime = new VisualBenchmarkRuntime(context);
            var install = new InstallVisualBenchmarkOnGameStartTrigger(context, runtime);

            context.OnEvent(GameEvents.GameStart, install.ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
