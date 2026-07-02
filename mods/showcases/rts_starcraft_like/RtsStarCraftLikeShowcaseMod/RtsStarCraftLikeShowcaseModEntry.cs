using Ludots.Core.Modding;

namespace RtsStarCraftLikeShowcaseMod;

public sealed class RtsStarCraftLikeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RtsStarCraftLikeShowcaseMod] Loaded - StarCraft style production showcase root.");
    }

    public void OnUnload()
    {
    }
}
