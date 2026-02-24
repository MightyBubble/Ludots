using Ludots.Core.Modding;
using TcgDemoMod.Triggers;

namespace TcgDemoMod
{
    public sealed class TcgDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[TcgDemoMod] Loaded");
            context.TriggerManager.RegisterTrigger(new InstallTcgDemoOnGameStartTrigger(context));
            context.TriggerManager.RegisterTrigger(new TcgSetupOnMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}

