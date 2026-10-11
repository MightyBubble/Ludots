using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ParticipantViewCapabilityMod.Runtime;

namespace ParticipantViewCapabilityMod;

public sealed class ParticipantViewCapabilityModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[ParticipantViewCapabilityMod] Loaded");
        var runtime = new ParticipantViewCapabilityRuntime();
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
