using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace RtsStarCraftLikeShowcaseMod;

public sealed class RtsStarCraftLikeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.MapLoaded, RtsStarCraftLikeShowcaseRuntime.InstallOnMapLoadedAsync);
        context.Log("[RtsStarCraftLikeShowcaseMod] Loaded - StarCraft style production showcase root.");
    }

    public void OnUnload()
    {
    }
}
