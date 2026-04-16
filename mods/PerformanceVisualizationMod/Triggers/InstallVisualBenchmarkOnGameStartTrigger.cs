using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using PerformanceVisualizationMod.Runtime;
using PerformanceVisualizationMod.Systems;

namespace PerformanceVisualizationMod.Triggers
{
    internal sealed class InstallVisualBenchmarkOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly VisualBenchmarkRuntime _runtime;

        public InstallVisualBenchmarkOnGameStartTrigger(IModContext context, VisualBenchmarkRuntime runtime)
        {
            _context = context;
            _runtime = runtime;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(VisualBenchmarkIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[VisualBenchmarkIds.InstalledKey] = true;
            engine.GlobalContext[VisualBenchmarkIds.RuntimeServiceKey] = _runtime;
            engine.RegisterPresentationSystem(new VisualBenchmarkPresentationSystem(engine, _runtime));
            _context.Log("[PerformanceVisualizationMod] Visual benchmark runtime registered.");
            return Task.CompletedTask;
        }
    }
}
