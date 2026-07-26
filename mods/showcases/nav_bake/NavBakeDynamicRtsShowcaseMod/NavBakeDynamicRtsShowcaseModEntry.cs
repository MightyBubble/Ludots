using Ludots.Core.Modding;

namespace NavBakeDynamicRtsShowcaseMod;

public sealed class NavBakeDynamicRtsShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[NavBakeDynamicRtsShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
