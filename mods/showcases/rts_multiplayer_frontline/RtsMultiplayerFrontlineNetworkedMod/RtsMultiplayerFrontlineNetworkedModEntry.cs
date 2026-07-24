using Ludots.Core.Modding;

namespace RtsMultiplayerFrontlineNetworkedMod;

public sealed class RtsMultiplayerFrontlineNetworkedModEntry : IMod
{
    public void OnLoad(IModContext context) =>
        context.Log("[RtsMultiplayerFrontlineNetworkedMod] Network profile loaded");

    public void OnUnload()
    {
    }
}
