using Ludots.Core.Modding;

namespace MultiLayerFogFieldShowcaseMod;

public sealed class MultiLayerFogFieldShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MultiLayerFogFieldShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
