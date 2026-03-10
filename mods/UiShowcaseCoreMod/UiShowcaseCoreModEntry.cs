using Ludots.Core.Modding;

namespace UiShowcaseCoreMod;

public sealed class UiShowcaseCoreModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiShowcaseCoreMod] Shared UI showcase assets loaded.");
    }

    public void OnUnload()
    {
    }
}
