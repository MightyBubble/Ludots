using Ludots.Core.Modding;

namespace RtsEmpireLikeShowcaseMod;

public sealed class RtsEmpireLikeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RtsEmpireLikeShowcaseMod] Loaded - Empire style production showcase root.");
    }

    public void OnUnload()
    {
    }
}
