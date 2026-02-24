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
            context.TriggerManager.RegisterTrigger(new InstallMobaDemoOnGameStartTrigger(context));
            context.TriggerManager.RegisterTrigger(new MobaCameraOnEntryMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}
