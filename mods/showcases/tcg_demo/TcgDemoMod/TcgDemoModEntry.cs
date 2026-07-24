using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using TcgDemoMod.Triggers;

namespace TcgDemoMod
{
    public sealed class TcgDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[TcgDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallTcgDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new TcgSetupOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

