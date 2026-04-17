using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SplineSurfaceUatMod.Runtime;
using SplineSurfaceUatMod.Triggers;

namespace SplineSurfaceUatMod
{
    public sealed class SplineSurfaceUatModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[SplineSurfaceUatMod] Loaded");
            var runtime = new SplineSurfaceUatRuntime();
            context.OnEvent(GameEvents.GameStart, new InstallSplineSurfaceUatOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
