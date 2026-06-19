using Ludots.Core.Modding;

namespace CapabilityStandardParticipantViewsMod;

public sealed class CapabilityStandardParticipantViewsModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardParticipantViewsMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
