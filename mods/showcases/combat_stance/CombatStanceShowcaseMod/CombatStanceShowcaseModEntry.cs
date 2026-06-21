using CombatStanceShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CombatStanceShowcaseMod;

public sealed class CombatStanceShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.MapLoaded, new InstallCombatStanceShowcaseOrdersTrigger(context).ExecuteAsync);
        context.Log("[CombatStanceShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
