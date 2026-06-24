using Ludots.Core.Modding;

namespace LineOfSightBrushShowcaseMod;

public sealed class LineOfSightBrushShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[LineOfSightBrushShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
