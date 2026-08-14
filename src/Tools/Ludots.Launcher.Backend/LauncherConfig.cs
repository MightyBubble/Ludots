using System.Text.Json.Serialization;
using Ludots.Core.Config;

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

    [JsonPropertyName("browserRuntimeProviders")]
    public List<LauncherBrowserRuntimeProvider> BrowserRuntimeProviders { get; set; } = new();
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

public sealed class LauncherBrowserRuntimeProvider
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonPropertyName("packageRootPath")]
    public string PackageRootPath { get; set; } = string.Empty;

    [JsonPropertyName("assemblyPath")]
    public string AssemblyPath { get; set; } = string.Empty;

    [JsonPropertyName("hostTypeName")]
    public string HostTypeName { get; set; } = string.Empty;

    [JsonPropertyName("useCollectibleLoadContext")]
    public bool UseCollectibleLoadContext { get; set; } = true;

    [JsonPropertyName("processSharedAssemblyNamePrefixes")]
    public List<string> ProcessSharedAssemblyNamePrefixes { get; set; } = new();
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

    [JsonPropertyName("browserRuntime")]
    public BrowserRuntimeConfig? BrowserRuntime { get; set; }

    [JsonPropertyName("processGroup")]
    public LauncherProcessGroupDefinition? ProcessGroup { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LauncherProcessGroupDefinition
{
    [JsonPropertyName("topology")]
    public string Topology { get; set; } = string.Empty;

    [JsonPropertyName("artifactDirectory")]
    public string ArtifactDirectory { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("connectionKey")]
    public string ConnectionKey { get; set; } = string.Empty;

    [JsonPropertyName("clientCount")]
    public int ClientCount { get; set; }

    [JsonPropertyName("readiness")]
    public LauncherProcessGroupReadinessDefinition Readiness { get; set; } = new();

    [JsonPropertyName("applications")]
    public List<LauncherProcessApplicationDefinition> Applications { get; set; } = new();

    [JsonPropertyName("processes")]
    public List<LauncherNetworkProcessDefinition> Processes { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LauncherProcessApplicationDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("hostKind")]
    public string HostKind { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonPropertyName("assemblyPath")]
    public string AssemblyPath { get; set; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LauncherNetworkProcessDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("applicationId")]
    public string ApplicationId { get; set; } = string.Empty;

    [JsonPropertyName("processRole")]
    public string ProcessRole { get; set; } = string.Empty;

    [JsonPropertyName("clientInstanceId")]
    public int ClientInstanceId { get; set; }

    [JsonPropertyName("credentialPath")]
    public string CredentialPath { get; set; } = string.Empty;

    [JsonPropertyName("faultProfile")]
    public string FaultProfile { get; set; } = string.Empty;

    [JsonPropertyName("faultSeed")]
    public int FaultSeed { get; set; }

    [JsonPropertyName("readiness")]
    public LauncherNetworkProcessReadinessDefinition Readiness { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LauncherProcessGroupReadinessDefinition
{
    [JsonPropertyName("timeoutMilliseconds")]
    public int TimeoutMilliseconds { get; set; }

    [JsonPropertyName("pollIntervalMilliseconds")]
    public int PollIntervalMilliseconds { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LauncherNetworkProcessReadinessDefinition
{
    [JsonPropertyName("artifactFileName")]
    public string ArtifactFileName { get; set; } = string.Empty;

    [JsonPropertyName("minimumReplicatedMirrorCount")]
    public int MinimumReplicatedMirrorCount { get; set; }

    [JsonPropertyName("minimumRenderableMirrorCount")]
    public int MinimumRenderableMirrorCount { get; set; }
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
