using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CoreInputMod.ViewMode;
using RoadNetworkShowcaseMod.Runtime;
using RoadNetworkShowcaseMod.Triggers;

namespace RoadNetworkShowcaseMod
{
    public sealed class RoadNetworkShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RoadNetworkShowcaseMod] Loaded");
            RoadNetworkShowcaseComponentAuthoring.Register(context.ModId);

            var runtime = new RoadNetworkShowcaseRuntime();
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                ViewModeRegistrar.RegisterFromVfs(context, engine.GlobalContext, sourceModId: context.ModId, activateWhenUnset: false);
                return System.Threading.Tasks.Task.CompletedTask;
            });
            context.OnEvent(GameEvents.GameStart, new InstallRoadNetworkShowcaseOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
