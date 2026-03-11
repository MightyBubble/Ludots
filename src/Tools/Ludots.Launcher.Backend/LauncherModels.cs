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
    string LaunchUrl,
    string RuntimeBootstrapFileName);

public enum LauncherBuildState
{
    NoProject,
    Idle,
    Outdated,
    Building,
    Succeeded,
    Failed
}

public enum LauncherModKind
{
    ResourceOnly,
    BinaryOnly,
    BuildableSource
}

public sealed class LauncherModInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int Priority { get; init; }
    public Dictionary<string, string> Dependencies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string RootPath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string LayerPath { get; init; } = "root";
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public string ChangelogFile { get; init; } = string.Empty;
    public bool HasThumbnail { get; init; }
    public bool HasReadme { get; init; }
    public string MainAssemblyPath { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public bool HasProject { get; init; }
    public LauncherBuildState BuildState { get; init; }
    public string LastBuildMessage { get; init; } = string.Empty;
    public LauncherModKind Kind { get; init; }
    public bool IsAmbiguous { get; init; }
    public List<string> BindingNames { get; init; } = new();
}

public sealed class LauncherPreset
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string> Selectors { get; init; } = new();
    public string AdapterId { get; init; } = LauncherPlatformIds.Raylib;
    public string BuildMode { get; init; } = LauncherBuildMode.Auto.ToString().ToLowerInvariant();
    public List<string> ActiveModIds { get; init; } = new();
    public bool IncludeDependencies { get; init; } = true;
}

public sealed record LauncherBindingInfo(
    string Name,
    string TargetType,
    string TargetValue,
    string? ProjectPath);

public sealed record LauncherStateSnapshot(
    IReadOnlyList<LauncherPlatformProfile> Platforms,
    string SelectedPlatformId,
    IReadOnlyList<LauncherPreset> Presets,
    string? SelectedPresetId,
    IReadOnlyList<string> WorkspaceSources,
    IReadOnlyList<LauncherBindingInfo> Bindings);

public sealed record LauncherBuildResult(
    string Id,
    bool Ok,
    int ExitCode,
    string Output);

public sealed record LauncherPlannedMod(
    string Id,
    string RootPath,
    string ProjectPath,
    string MainAssemblyPath,
    LauncherModKind Kind,
    LauncherBuildState BuildState,
    IReadOnlyList<string> BindingNames);

public sealed record LauncherLaunchPlan(
    string AdapterId,
    string BuildMode,
    IReadOnlyList<string> Selectors,
    IReadOnlyList<string> RootModIds,
    IReadOnlyList<string> OrderedModIds,
    IReadOnlyList<LauncherPlannedMod> Mods,
    string BootstrapArtifactStrategy,
    string BootstrapArtifactPath,
    string AppOutputDirectory,
    string AppAssemblyPath,
    string LaunchUrl);

public sealed record LauncherResolveResult(
    LauncherLaunchPlan Plan,
    IReadOnlyList<LauncherModInfo> Catalog);

public sealed record LauncherLaunchResult(
    bool Ok,
    string Error,
    int Pid,
    string Url,
    string BootstrapPath,
    LauncherLaunchPlan? Plan);
