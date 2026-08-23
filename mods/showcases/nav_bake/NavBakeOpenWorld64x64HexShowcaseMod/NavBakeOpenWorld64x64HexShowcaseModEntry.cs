using Ludots.Core.Modding;

namespace NavBakeOpenWorld64x64HexShowcaseMod;

public sealed class NavBakeOpenWorld64x64HexShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[NavBakeOpenWorld64x64HexShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
