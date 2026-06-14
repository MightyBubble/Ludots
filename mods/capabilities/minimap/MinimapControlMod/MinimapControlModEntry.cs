using Ludots.Core.Modding;

namespace MinimapControlMod;

public sealed class MinimapControlModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[MinimapControlMod] Deprecated: minimap runtime is Core infrastructure. No mod runtime installed.");
    }

    public void OnUnload()
    {
    }
}
