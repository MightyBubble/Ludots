using System.Text.Json.Serialization;

namespace Ludots.Launcher.Backend;

public sealed class LauncherConfig
{
    [JsonPropertyName("workspaceSources")]
    public List<string> WorkspaceSources { get; set; } = new();

    [JsonPropertyName("selectedPresetId")]
    public string? SelectedPresetId { get; set; }

    [JsonPropertyName("selectedPlatformId")]
    public string SelectedPlatformId { get; set; } = LauncherPlatformIds.Raylib;

    [JsonPropertyName("presets")]
    public Dictionary<string, LauncherPreset> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LauncherPreset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("activeModIds")]
    public List<string> ActiveModIds { get; set; } = new();

    [JsonPropertyName("includeDependencies")]
    public bool IncludeDependencies { get; set; } = true;
}
