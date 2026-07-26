using Ludots.Core.Modding;

namespace NavBakeOpenWorld64x64ShowcaseMod;

public sealed class NavBakeOpenWorld64x64ShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[NavBakeOpenWorld64x64ShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
