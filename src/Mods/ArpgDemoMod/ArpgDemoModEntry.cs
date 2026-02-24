using ArpgDemoMod.Triggers;
using Ludots.Core.Modding;

namespace ArpgDemoMod
{
    public sealed class ArpgDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[ArpgDemoMod] Loaded");
            context.TriggerManager.RegisterTrigger(new InstallArpgDemoOnGameStartTrigger(context));
            context.TriggerManager.RegisterTrigger(new ArpgSetupOnMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}

