using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SpatialBoundsShowcaseMod.Runtime;
using SpatialBoundsShowcaseMod.Systems;

namespace SpatialBoundsShowcaseMod
{
    public sealed class SpatialBoundsShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[SpatialBoundsShowcaseMod] Loaded");
            var runtime = new SpatialBoundsShowcaseRuntime();

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.RegisterPresentationSystem(new SpatialBoundsShowcasePresentationSystem(engine, runtime));
                }

                return Task.CompletedTask;
            });
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
