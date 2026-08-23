using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NavDomainShowcaseMod.Runtime;
using NavDomainShowcaseMod.Triggers;

namespace NavDomainShowcaseMod;

public sealed class NavDomainShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[NavDomainShowcaseMod] Loaded.");

        var runtime = new NavDomainAuthoringRuntime(
            new LogicTerrainDocument(
                widthCells: 256,
                heightCells: 256,
                cellSizeCm: 100,
                heightScaleMeters: 1f));
        var installTrigger = new InstallNavDomainOnGameStartTrigger(runtime);

        context.OnEvent(GameEvents.GameStart, installTrigger.ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
