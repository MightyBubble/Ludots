using Ludots.Core.Modding;

namespace GapGeneratorShowcaseMod;

public sealed class GapGeneratorShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[GapGeneratorShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
