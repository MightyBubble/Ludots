using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MobaDemoMod.Triggers;

namespace MobaDemoMod
{
    public sealed class MobaDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[MobaDemoMod] Loaded");
            context.OnEvent(GameEvents.GameStart, new InstallMobaDemoOnGameStartTrigger(context).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, new MobaCameraOnEntryMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
