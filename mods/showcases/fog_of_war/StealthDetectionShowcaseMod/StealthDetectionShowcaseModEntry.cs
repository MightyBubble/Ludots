using Ludots.Core.Modding;

namespace StealthDetectionShowcaseMod;

public sealed class StealthDetectionShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[StealthDetectionShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
