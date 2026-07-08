using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using FormationCapabilityShowcaseMod.Runtime;

namespace FormationCapabilityShowcaseMod;

public sealed class FormationCapabilityShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[FormationCapabilityShowcaseMod] Loaded");
        FormationCapabilityShowcaseComponentAuthoring.Register(context.ModId);
        var runtime = new FormationCapabilityShowcaseRuntime(context);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
