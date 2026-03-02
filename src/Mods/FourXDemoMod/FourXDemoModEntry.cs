using FourXDemoMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FourXDemoMod
{
    public sealed class FourXDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[FourXDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallFourXDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new FourXSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

