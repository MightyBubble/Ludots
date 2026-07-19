using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsDemoMod.Triggers;

namespace RtsDemoMod
{
    public sealed class RtsDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RtsDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallRtsDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new RtsSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
