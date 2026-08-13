using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SplineSurfaceUatMod.Runtime;
using SplineSurfaceUatMod.Systems;

namespace SplineSurfaceUatMod.Triggers
{
    internal sealed class InstallSplineSurfaceUatOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly SplineSurfaceUatRuntime _runtime;

        public InstallSplineSurfaceUatOnGameStartTrigger(IModContext context, SplineSurfaceUatRuntime runtime)
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

            if (engine.GlobalContext.TryGetValue(SplineSurfaceUatIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[SplineSurfaceUatIds.InstalledKey] = true;
            engine.RegisterPresentationSystem(new SplineSurfaceUatPresentationSystem(engine, _runtime));
            _context.Log("[SplineSurfaceUatMod] Presenter-driven spline surface UAT presentation system registered.");
            return Task.CompletedTask;
        }
    }
}
