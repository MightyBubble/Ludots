using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using PerformerBlacksmithShowcaseMod;

namespace PerformerBlacksmithLargeWorldEntryMod;

public sealed class PerformerBlacksmithLargeWorldEntryModEntry : IMod
{
    private const string MinimapPresetEnvKey = "LUDOTS_MINIMAP_PRESET";
    private const string MinimapVisibleEnvKey = "LUDOTS_MINIMAP_VISIBLE";
    private const string MinimapZoomNormalizedEnvKey = "LUDOTS_MINIMAP_ZOOM_NORMALIZED";
    private const string MinimapRotateWithCameraEnvKey = "LUDOTS_MINIMAP_ROTATE_WITH_CAMERA";
    private const string ChunkDebugVisibleEnvKey = "LUDOTS_CHUNK_DEBUG_VISIBLE";

    public void OnLoad(IModContext context)
    {
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
        if (engine == null ||
            !string.Equals(
                engine.CurrentMapSession?.MapId.Value,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                System.StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime runtime)
        {
            return Task.CompletedTask;
        }

        runtime.Visible = ReadEnvBoolOrDefault(MinimapVisibleEnvKey, defaultValue: true);
        runtime.SetRotateWithCamera(ReadEnvBoolOrDefault(MinimapRotateWithCameraEnvKey, defaultValue: false));
        ConfigureMinimapPreset(runtime);
        ApplyMinimapZoom(runtime);
        if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
        {
            renderDebug.DrawTerrain = true;
        }

        if (engine.GetService(CoreServiceKeys.ChunkDebugPanelRuntime) is ChunkDebugPanelRuntime chunkDebug)
        {
            chunkDebug.Visible = ReadEnvBoolOrDefault(ChunkDebugVisibleEnvKey, defaultValue: true);
        }

        return Task.CompletedTask;
    }

    private static void ConfigureMinimapPreset(MinimapRuntime runtime)
    {
        string? raw = System.Environment.GetEnvironmentVariable(MinimapPresetEnvKey);
        if (string.Equals(raw, "follow-camera", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "camera", System.StringComparison.OrdinalIgnoreCase))
        {
            runtime.UseFollowCameraPreset(
                runtime.HalfExtentCm,
                ReadEnvBoolOrDefault(MinimapRotateWithCameraEnvKey, defaultValue: true));
            return;
        }

        runtime.UseRtsFullMapPreset();
    }

    private static void ApplyMinimapZoom(MinimapRuntime runtime)
    {
        if (!TryReadEnvNormalized(MinimapZoomNormalizedEnvKey, out float normalized))
        {
            return;
        }

        runtime.SetZoomNormalized(normalized);
    }

    private static bool ReadEnvBoolOrDefault(string key, bool defaultValue)
    {
        string? raw = System.Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return string.Equals(raw, "1", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "true", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "yes", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "on", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadEnvNormalized(string key, out float value)
    {
        string? raw = System.Environment.GetEnvironmentVariable(key);
        if (float.TryParse(raw, out value) && float.IsFinite(value) && value >= 0f && value <= 1f)
        {
            return true;
        }

        value = 0f;
        return false;
    }
}
