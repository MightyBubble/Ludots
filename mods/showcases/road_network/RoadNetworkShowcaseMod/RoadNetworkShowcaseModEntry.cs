using Ludots.Core.Modding;
using Ludots.Core.Scripting;
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
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapLoaded, new InstallRoadNetworkShowcaseOnMapFocusTrigger(context, runtime, GameEvents.MapLoaded).ExecuteAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, new InstallRoadNetworkShowcaseOnMapFocusTrigger(context, runtime, GameEvents.MapResumed).ExecuteAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
