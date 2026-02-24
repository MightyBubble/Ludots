using Ludots.Core.Modding;
using RtsDemoMod.Triggers;

namespace RtsDemoMod
{
    public sealed class RtsDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RtsDemoMod] Loaded");
            context.TriggerManager.RegisterTrigger(new InstallRtsDemoOnGameStartTrigger(context));
            context.TriggerManager.RegisterTrigger(new RtsSetupOnMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}
