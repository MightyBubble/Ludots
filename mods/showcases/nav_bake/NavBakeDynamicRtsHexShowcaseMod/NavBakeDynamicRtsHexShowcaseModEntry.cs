using Ludots.Core.Modding;

namespace NavBakeDynamicRtsHexShowcaseMod;

public sealed class NavBakeDynamicRtsHexShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[NavBakeDynamicRtsHexShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
