using System.Text.Json.Serialization;

namespace Ludots.Launcher.Backend;

public sealed class LauncherConfig
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("scanRoots")]
    public List<LauncherScanRoot> ScanRoots { get; set; } = new();

    [JsonPropertyName("bindings")]
    public List<LauncherBinding> Bindings { get; set; } = new();

    [JsonPropertyName("adapters")]
    public LauncherAdapterDefaults Adapters { get; set; } = new();

    [JsonPropertyName("projectHints")]
    public List<LauncherProjectHint> ProjectHints { get; set; } = new();
}

public sealed class LauncherScanRoot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("scanMode")]
    public string ScanMode { get; set; } = "recursive";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class LauncherBinding
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public LauncherBindingTarget Target { get; set; } = new();
}

public sealed class LauncherBindingTarget
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "path";

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; set; }
}

public sealed class LauncherProjectHint
{
    [JsonPropertyName("modId")]
    public string? ModId { get; set; }

    [JsonPropertyName("rootPath")]
    public string? RootPath { get; set; }

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;
}

public sealed class LauncherAdapterDefaults
{
    [JsonPropertyName("default")]
    public string Default { get; set; } = LauncherPlatformIds.Raylib;
}

public sealed class LauncherPresetDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("presets")]
    public List<LauncherPresetDefinition> Presets { get; set; } = new();
}

public sealed class LauncherPresetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("selectors")]
    public List<string> Selectors { get; set; } = new();

    [JsonPropertyName("adapterId")]
    public string? AdapterId { get; set; }

    [JsonPropertyName("buildMode")]
    public string BuildMode { get; set; } = LauncherBuildMode.Auto.ToString().ToLowerInvariant();
}

public sealed class LauncherPreferences
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("lastPresetId")]
    public string? LastPresetId { get; set; }

    [JsonPropertyName("lastAdapterId")]
    public string? LastAdapterId { get; set; }

    [JsonPropertyName("viewMode")]
    public string ViewMode { get; set; } = "card";
}

public enum LauncherBuildMode
{
    Auto,
    Always,
    Never
}
