using Ludots.Core.Modding;

namespace VisionConeHighGroundShowcaseMod;

public sealed class VisionConeHighGroundShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[VisionConeHighGroundShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
