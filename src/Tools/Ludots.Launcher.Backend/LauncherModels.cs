namespace Ludots.Launcher.Backend;

public static class LauncherPlatformIds
{
    public const string Raylib = "raylib";
    public const string Web = "web";
}

public sealed record LauncherPlatformProfile(
    string Id,
    string Name,
    string AppProjectPath,
    string OutputDirectory,
    string ClientProjectDirectory,
    string ClientDistributionDirectory,
    string LaunchUrl);

public enum LauncherBuildState
{
    NoProject,
    Idle,
    Outdated,
    Building,
    Succeeded,
    Failed
}

public sealed class LauncherModInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int Priority { get; init; }
    public Dictionary<string, string> Dependencies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string RootPath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public string ChangelogFile { get; init; } = string.Empty;
    public bool HasThumbnail { get; init; }
    public bool HasReadme { get; init; }
    public string MainAssemblyPath { get; init; } = string.Empty;
    public bool HasProject { get; init; }
    public LauncherBuildState BuildState { get; init; }
    public string LastBuildMessage { get; init; } = string.Empty;
}

public sealed record LauncherStateSnapshot(
    IReadOnlyList<LauncherPlatformProfile> Platforms,
    string SelectedPlatformId,
    IReadOnlyList<LauncherPreset> Presets,
    string? SelectedPresetId,
    IReadOnlyList<string> WorkspaceSources);

public sealed record LauncherBuildResult(
    string Id,
    bool Ok,
    int ExitCode,
    string Output);

public sealed record LauncherLaunchResult(
    bool Ok,
    string Error,
    int Pid,
    string Url);
