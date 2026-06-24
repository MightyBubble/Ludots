using Ludots.Core.Modding;

namespace FogOfWarShowcaseMod;

public sealed class FogOfWarShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[FogOfWarShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
