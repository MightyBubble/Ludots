using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;

namespace CapabilityStandardMassNavigationLargeWorld10kMod;

public sealed class CapabilityStandardMassNavigationLargeWorld10kModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardMassNavigationLargeWorld10kMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ConfigureLargeWorldUatAsync);
        context.OnEvent(GameEvents.MapLoaded, ConfigureLargeWorldUatAsync);
        context.OnEvent(GameEvents.MapResumed, ConfigureLargeWorldUatAsync);
    }

    public void OnUnload()
    {
    }

    private static Task ConfigureLargeWorldUatAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || !IsStartupMapFocused(engine))
        {
            return Task.CompletedTask;
        }

        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime runtime)
        {
            return Task.CompletedTask;
        }

        runtime.Visible = true;
        runtime.SetRotateWithCamera(false);
        runtime.UseRtsFullMapPreset();
        return Task.CompletedTask;
    }

    private static bool IsStartupMapFocused(GameEngine engine)
    {
        string? startupMapId = engine.MergedConfig?.StartupMapId;
        return !string.IsNullOrWhiteSpace(startupMapId) &&
               string.Equals(
                   engine.CurrentMapSession?.MapId.Value,
                   startupMapId,
                   StringComparison.Ordinal);
    }
}
