using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Hosting
{
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

    public sealed record LauncherAdapterDescriptor(
        string Id,
        string Name,
        string HostKind,
        string BuildPipeline,
        string RuntimeBootstrapSchema,
        string AppProjectPath,
        string OutputDirectory,
        string ClientProjectDirectory,
        string ClientDistributionDirectory,
        string LaunchUrl,
        string RuntimeBootstrapFileName);

    public sealed record LauncherPlannedMod(
        string Id,
        string RootPath,
        string ProjectPath,
        string MainAssemblyPath,
        LauncherModKind Kind,
        LauncherBuildState BuildState,
        IReadOnlyList<string> BindingNames);

    public sealed record LauncherSettingContribution(
        string Source,
        string? OwnerModId,
        bool IsRootSelection,
        JsonNode? Value);

    public sealed record LauncherResolvedSetting(
        string Key,
        JsonNode? EffectiveValue,
        string? EffectiveSource,
        IReadOnlyList<LauncherSettingContribution> Contributions);

    public sealed record LauncherPlanDiagnostics(
        IReadOnlyList<LauncherResolvedSetting> Settings,
        IReadOnlyList<string> Warnings);

    public sealed record LauncherRuntimeArtifacts(
        string BootstrapArtifactStrategy,
        string BootstrapArtifactPath,
        string GraphArtifactPath,
        string AppOutputDirectory,
        string AppAssemblyPath,
        string LaunchUrl);

    public sealed record LauncherGraphDocument(
        int SchemaVersion,
        string GeneratedAtUtc,
        string PlanFingerprint,
        LauncherAdapterDescriptor Adapter,
        string BuildMode,
        IReadOnlyList<string> Selectors,
        IReadOnlyList<string> RootModIds,
        IReadOnlyList<string> OrderedModIds,
        IReadOnlyList<LauncherPlannedMod> PlannedMods,
        LauncherRuntimeArtifacts RuntimeArtifacts,
        BrowserRuntimeConfig? BrowserRuntime,
        LauncherPlanDiagnostics Diagnostics);
}
