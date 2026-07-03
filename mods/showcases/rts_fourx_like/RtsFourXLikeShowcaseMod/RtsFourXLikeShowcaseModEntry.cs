using Ludots.Core.Modding;

namespace RtsFourXLikeShowcaseMod;

public sealed class RtsFourXLikeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RtsFourXLikeShowcaseMod] Loaded - 4X style production showcase root.");
    }

    public void OnUnload()
    {
    }
}
