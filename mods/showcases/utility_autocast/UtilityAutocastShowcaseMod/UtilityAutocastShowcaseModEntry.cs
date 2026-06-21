using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UtilityAutocastShowcaseMod.Triggers;

namespace UtilityAutocastShowcaseMod;

public sealed class UtilityAutocastShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.MapLoaded, new PrintUtilityAutocastTraceOnMapLoadedTrigger(context).ExecuteAsync);
        context.Log("[UtilityAutocastShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
