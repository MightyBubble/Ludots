using ArpgDemoMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ArpgDemoMod
{
    public sealed class ArpgDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[ArpgDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallArpgDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new ArpgSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

