using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraShowcaseMod.Runtime;

namespace CameraShowcaseMod
{
    public sealed class CameraShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraShowcaseMod] Loaded");
            var runtime = new CameraShowcaseRuntime(context);

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                ViewModeRegistrar.RegisterFromVfs(context, engine.GlobalContext, sourceModId: context.ModId, activateWhenUnset: false);
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
