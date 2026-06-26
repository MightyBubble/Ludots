using Ludots.Core.Modding;

namespace MassNavigationMod;

public sealed class MassNavigationModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MassNavigationMod] Loaded data-only MassNavigation assets.");
    }

    public void OnUnload()
    {
    }
}
