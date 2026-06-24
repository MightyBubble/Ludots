using Ludots.Core.Modding;

namespace ExploredMemoryShowcaseMod;

public sealed class ExploredMemoryShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[ExploredMemoryShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
