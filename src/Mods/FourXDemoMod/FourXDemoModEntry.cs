using FourXDemoMod.Triggers;
using Ludots.Core.Modding;

namespace FourXDemoMod
{
    public sealed class FourXDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[FourXDemoMod] Loaded");
            context.TriggerManager.RegisterTrigger(new InstallFourXDemoOnGameStartTrigger(context));
            context.TriggerManager.RegisterTrigger(new FourXSetupOnMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}

