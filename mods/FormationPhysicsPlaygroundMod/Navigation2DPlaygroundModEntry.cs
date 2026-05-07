using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Navigation2DPlaygroundMod.Runtime;

namespace Navigation2DPlaygroundMod
{
    public sealed class Navigation2DPlaygroundModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            var runtime = new Navigation2DPlaygroundRuntime(context);
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    ViewModeRegistrar.RegisterFromVfs(
                        context,
                        engine.GlobalContext,
                        defaultModeId: Navigation2DPlaygroundIds.CommandModeId,
                        sourceModId: context.ModId,
                        activateWhenUnset: false);

                    runtime.EnsureSystemsInstalled(engine);
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
