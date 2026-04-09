using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using VisualTerrainEditorMod.Runtime;
using VisualTerrainEditorMod.Triggers;

namespace VisualTerrainEditorMod;

public sealed class VisualTerrainEditorModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[VisualTerrainEditorMod] Loaded.");

        var runtime = new VisualTerrainEditorRuntime();
        var installTrigger = new InstallVisualTerrainEditorOnGameStartTrigger(runtime);

        context.OnEvent(GameEvents.GameStart, installTrigger.ExecuteAsync);
        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
