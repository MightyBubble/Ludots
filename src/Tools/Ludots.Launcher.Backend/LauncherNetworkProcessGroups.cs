using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Launcher.Backend;

public static class LauncherProcessGroupTopologies
{
    public const string LocalAuthoritative = "localAuthoritative";
    public const string ExternalJoin = "externalJoin";
}

public static class LauncherProcessHostKinds
{
    public const string DedicatedServer = "dedicatedServer";
    public const string Raylib = "raylib";
}

public sealed record LauncherResolvedProcessApplication(
    string Id,
    string HostKind,
    string ProjectPath,
    string AssemblyPath,
    string WorkingDirectory);

public sealed record LauncherResolvedNetworkProcess(
    string Id,
    LauncherResolvedProcessApplication Application,
    NetworkHostBootstrapConfig NetworkHost,
    string CredentialPath,
    string ReadinessArtifactFileName,
    int MinimumReplicatedMirrorCount,
    int MinimumRenderableMirrorCount);

public sealed record LauncherResolvedProcessGroup(
    string PresetId,
    string Topology,
    string ArtifactDirectory,
    int ClientCount,
    int ReadinessTimeoutMilliseconds,
    int ReadinessPollIntervalMilliseconds,
    LauncherLaunchPlan LaunchPlan,
    IReadOnlyList<LauncherResolvedProcessApplication> Applications,
    IReadOnlyList<LauncherResolvedNetworkProcess> Processes);

public sealed record LauncherNetworkRoleArtifact(
    string ProcessId,
    string ProcessRole,
    string ApplicationId,
    string ApplicationProjectPath,
    string ApplicationAssemblyPath,
    string WorkingDirectory,
    string GraphPath,
    string BootstrapPath,
    string CredentialPath,
    string ReadinessArtifactPath,
    int MinimumReplicatedMirrorCount,
    int MinimumRenderableMirrorCount);

public sealed record LauncherProcessGroupArtifacts(
    string PresetId,
    string Topology,
    string ArtifactDirectory,
    int ClientCount,
    int ReadinessTimeoutMilliseconds,
    int ReadinessPollIntervalMilliseconds,
    IReadOnlyList<LauncherNetworkRoleArtifact> Processes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LauncherNetworkProcessReadinessArtifact
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("processRole")]
    public string ProcessRole { get; set; } = string.Empty;

    [JsonPropertyName("runtimeReady")]
    public bool RuntimeReady { get; set; }

    [JsonPropertyName("sessionEstablished")]
    public bool SessionEstablished { get; set; }

    [JsonPropertyName("sessionEpoch")]
    public ulong SessionEpoch { get; set; }

    [JsonPropertyName("connectedSeatCount")]
    public int ConnectedSeatCount { get; set; }

    [JsonPropertyName("replicatedMirrorCount")]
    public int ReplicatedMirrorCount { get; set; }

    [JsonPropertyName("renderableMirrorCount")]
    public int RenderableMirrorCount { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Resolves and validates a data-authored network process topology without starting processes.
/// The resolved values are also the public SSOT used by acceptance tooling.
/// </summary>
public static class LauncherNetworkProcessGroupResolver
{
    public static LauncherResolvedProcessGroup Resolve(
        string repoRoot,
        string presetId,
        LauncherProcessGroupDefinition definition,
        LauncherLaunchPlan launchPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(launchPlan);

        string root = Path.GetFullPath(repoRoot);
        string topology = RequireText(definition.Topology, presetId, "topology");
        if (topology != LauncherProcessGroupTopologies.LocalAuthoritative &&
            topology != LauncherProcessGroupTopologies.ExternalJoin)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' has unknown topology '{topology}'. " +
                $"Expected {LauncherProcessGroupTopologies.LocalAuthoritative} or {LauncherProcessGroupTopologies.ExternalJoin}.");
        }

        string artifactDirectory = ResolveOwnedPath(
            root,
            RequireText(definition.ArtifactDirectory, presetId, "artifactDirectory"),
            root,
            $"Process group preset '{presetId}' artifactDirectory");
        string host = RequireText(definition.Host, presetId, "host");
        if ((uint)(definition.Port - 1) >= ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' port must be between 1 and {ushort.MaxValue}; got {definition.Port}.");
        }

        string connectionKey = RequireText(definition.ConnectionKey, presetId, "connectionKey");
        if (definition.ClientCount <= 0)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' clientCount must be positive.");
        }

        if (definition.Readiness == null || definition.Readiness.TimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' readiness.timeoutMilliseconds must be positive.");
        }

        if (definition.Readiness.PollIntervalMilliseconds <= 0 ||
            definition.Readiness.PollIntervalMilliseconds > definition.Readiness.TimeoutMilliseconds)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' readiness.pollIntervalMilliseconds must be positive and no greater than its timeout.");
        }

        if (definition.Applications == null || definition.Applications.Count == 0)
        {
            throw new InvalidOperationException($"Process group preset '{presetId}' must declare applications.");
        }

        var applications = new List<LauncherResolvedProcessApplication>(definition.Applications.Count);
        var applicationsById = new Dictionary<string, LauncherResolvedProcessApplication>(StringComparer.OrdinalIgnoreCase);
        var applicationProjects = new HashSet<string>(PathComparer);
        var applicationAssemblies = new HashSet<string>(PathComparer);
        foreach (LauncherProcessApplicationDefinition application in definition.Applications)
        {
            if (application == null)
            {
                throw new InvalidOperationException($"Process group preset '{presetId}' contains a null application.");
            }

            string applicationId = RequireIdentifier(application.Id, presetId, "applications[].id");
            string hostKind = RequireText(application.HostKind, presetId, $"application '{applicationId}' hostKind");
            if (hostKind != LauncherProcessHostKinds.DedicatedServer && hostKind != LauncherProcessHostKinds.Raylib)
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' application '{applicationId}' has unknown hostKind '{hostKind}'.");
            }

            string projectPath = ResolveOwnedPath(
                root,
                RequireText(application.ProjectPath, presetId, $"application '{applicationId}' projectPath"),
                root,
                $"Process group preset '{presetId}' application '{applicationId}' projectPath");
            if (!string.Equals(Path.GetExtension(projectPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' application '{applicationId}' projectPath must reference a .csproj file.");
            }

            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException(
                    $"Process group preset '{presetId}' application project was not found: {projectPath}",
                    projectPath);
            }

            string assemblyPath = ResolveOwnedPath(
                root,
                RequireText(application.AssemblyPath, presetId, $"application '{applicationId}' assemblyPath"),
                root,
                $"Process group preset '{presetId}' application '{applicationId}' assemblyPath");
            if (!string.Equals(Path.GetExtension(assemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' application '{applicationId}' assemblyPath must reference a .dll file.");
            }

            if (!applicationProjects.Add(projectPath) || !applicationAssemblies.Add(assemblyPath))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' application '{applicationId}' duplicates another application's projectPath or assemblyPath.");
            }

            string workingDirectory = Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException(
                    $"Process group preset '{presetId}' application '{applicationId}' assemblyPath has no parent directory.");
            var resolved = new LauncherResolvedProcessApplication(
                applicationId,
                hostKind,
                projectPath,
                assemblyPath,
                workingDirectory);
            if (!applicationsById.TryAdd(applicationId, resolved))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' declares duplicate application id '{applicationId}'.");
            }

            applications.Add(resolved);
        }

        if (definition.Processes == null || definition.Processes.Count == 0)
        {
            throw new InvalidOperationException($"Process group preset '{presetId}' must declare processes.");
        }

        var processIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clientInstanceIds = new HashSet<int>();
        var faultSeeds = new HashSet<int>();
        var credentialPaths = new HashSet<string>(PathComparer);
        var usedApplicationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processes = new List<LauncherResolvedNetworkProcess>(definition.Processes.Count);
        int serverCount = 0;
        int clientCount = 0;
        foreach (LauncherNetworkProcessDefinition process in definition.Processes)
        {
            if (process == null)
            {
                throw new InvalidOperationException($"Process group preset '{presetId}' contains a null process.");
            }

            string processId = RequireIdentifier(process.Id, presetId, "processes[].id");
            if (!processIds.Add(processId))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' declares duplicate process id '{processId}'.");
            }

            string applicationId = RequireText(process.ApplicationId, presetId, $"process '{processId}' applicationId");
            if (!applicationsById.TryGetValue(applicationId, out LauncherResolvedProcessApplication? application))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' process '{processId}' references unknown application '{applicationId}'.");
            }

            usedApplicationIds.Add(applicationId);

            string processRole = RequireText(process.ProcessRole, presetId, $"process '{processId}' processRole");
            string faultProfile = RequireText(process.FaultProfile, presetId, $"process '{processId}' faultProfile");
            if (process.Readiness == null)
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' process '{processId}' readiness is required.");
            }

            string readinessArtifactFileName = RequireFileName(
                process.Readiness.ArtifactFileName,
                presetId,
                $"process '{processId}' readiness.artifactFileName");
            if (process.Readiness.MinimumReplicatedMirrorCount < 0 ||
                process.Readiness.MinimumRenderableMirrorCount < 0)
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' process '{processId}' readiness mirror thresholds must not be negative.");
            }

            string credentialPath = string.Empty;
            var networkHost = new NetworkHostBootstrapConfig
            {
                ProcessRole = processRole,
                Host = processRole == "replicatedClient" ? host : string.Empty,
                Port = definition.Port,
                ConnectionKey = connectionKey,
                ClientInstanceId = process.ClientInstanceId,
                CredentialPath = string.Empty,
                ReadinessArtifactPath = readinessArtifactFileName,
                FaultProfile = faultProfile,
                FaultSeed = process.FaultSeed
            };

            NetworkProcessRole role = networkHost.ResolveRole();
            if (!faultSeeds.Add(process.FaultSeed))
            {
                throw new InvalidOperationException(
                    $"Process group preset '{presetId}' declares duplicate faultSeed {process.FaultSeed}.");
            }

            if (role == NetworkProcessRole.AuthoritativeServer)
            {
                serverCount++;
                if (application.HostKind != LauncherProcessHostKinds.DedicatedServer)
                {
                    throw new InvalidOperationException(
                        $"Process group preset '{presetId}' authoritative process '{processId}' must use a dedicatedServer application.");
                }

                if (!string.IsNullOrEmpty(process.CredentialPath))
                {
                    throw new InvalidOperationException(
                        $"Process group preset '{presetId}' authoritative process '{processId}' must not declare credentialPath.");
                }

                if (process.Readiness.MinimumReplicatedMirrorCount != 0 ||
                    process.Readiness.MinimumRenderableMirrorCount != 0)
                {
                    throw new InvalidOperationException(
                        $"Process group preset '{presetId}' authoritative process '{processId}' must use zero client mirror readiness thresholds.");
                }
            }
            else
            {
                clientCount++;
                if (application.HostKind != LauncherProcessHostKinds.Raylib)
                {
                    throw new InvalidOperationException(
                        $"Process group preset '{presetId}' replicated client '{processId}' must use a raylib application.");
                }

                if (!clientInstanceIds.Add(process.ClientInstanceId))
                {
                    throw new InvalidOperationException(
                        $"Process group preset '{presetId}' declares duplicate clientInstanceId {process.ClientInstanceId}.");
                }

                credentialPath = ResolveOwnedPath(
                    artifactDirectory,
                    RequireText(process.CredentialPath, presetId, $"process '{processId}' credentialPath"),
                    artifactDirectory,
                    $"Process group preset '{presetId}' process '{processId}' credentialPath");
                if (!credentialPaths.Add(credentialPath))
                {
                    throw new InvalidOperationException(
                        $"Process group preset '{presetId}' declares duplicate credentialPath '{credentialPath}'.");
                }

                networkHost.CredentialPath = credentialPath;
            }

            networkHost.Validate();
            processes.Add(new LauncherResolvedNetworkProcess(
                processId,
                application,
                networkHost,
                credentialPath,
                readinessArtifactFileName,
                process.Readiness.MinimumReplicatedMirrorCount,
                process.Readiness.MinimumRenderableMirrorCount));
        }

        if (topology == LauncherProcessGroupTopologies.LocalAuthoritative && (serverCount != 1 || clientCount == 0))
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' localAuthoritative topology requires exactly one authoritative server and at least one replicated client; " +
                $"got servers={serverCount}, clients={clientCount}.");
        }

        if (topology == LauncherProcessGroupTopologies.LocalAuthoritative &&
            processes[0].NetworkHost.ResolveRole() != NetworkProcessRole.AuthoritativeServer)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' localAuthoritative topology must list its authoritative server first.");
        }

        if (topology == LauncherProcessGroupTopologies.ExternalJoin && (serverCount != 0 || clientCount != 1))
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' externalJoin topology requires exactly one replicated client and no authoritative server; " +
                $"got servers={serverCount}, clients={clientCount}.");
        }

        if (clientCount != definition.ClientCount)
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' declares clientCount={definition.ClientCount}, but resolves {clientCount} replicated clients.");
        }

        if (usedApplicationIds.Count != applications.Count)
        {
            string unused = string.Join(", ", applications
                .Where(application => !usedApplicationIds.Contains(application.Id))
                .Select(application => application.Id));
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' declares unused applications: {unused}.");
        }

        return new LauncherResolvedProcessGroup(
            presetId,
            topology,
            artifactDirectory,
            definition.ClientCount,
            definition.Readiness.TimeoutMilliseconds,
            definition.Readiness.PollIntervalMilliseconds,
            launchPlan,
            applications,
            processes);
    }

    private static string RequireText(string? value, string presetId, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Process group preset '{presetId}' {field} is required.");
        }

        return value.Trim();
    }

    private static string RequireIdentifier(string? value, string presetId, string field)
    {
        string identifier = RequireText(value, presetId, field);
        if (identifier.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' {field} must contain only ASCII letters, digits, '-' or '_'.");
        }

        return identifier;
    }

    private static string RequireFileName(string? value, string presetId, string field)
    {
        string fileName = RequireText(value, presetId, field);
        if (Path.IsPathRooted(fileName) ||
            fileName is "." or ".." ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Process group preset '{presetId}' {field} must be a single relative file name.");
        }

        return fileName;
    }

    private static string ResolveOwnedPath(string baseDirectory, string path, string ownerDirectory, string description)
    {
        string resolved = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
        string owner = Path.GetFullPath(ownerDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = owner + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(prefix, comparison) && !string.Equals(resolved, owner, comparison))
        {
            throw new InvalidOperationException($"{description} must stay within '{owner}'; got '{resolved}'.");
        }

        return resolved;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

/// <summary>
/// Materializes one strict launcher graph/bootstrap pair per resolved network role.
/// </summary>
public sealed class LauncherNetworkRoleArtifactGenerator
{
    private static readonly JsonSerializerOptions BootstrapJsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions GraphJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void PrepareCredentialsForLaunch(LauncherResolvedProcessGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Topology == LauncherProcessGroupTopologies.ExternalJoin)
        {
            return;
        }

        if (group.Topology != LauncherProcessGroupTopologies.LocalAuthoritative)
        {
            throw new InvalidOperationException(
                $"Process group '{group.PresetId}' has unsupported credential lifecycle topology '{group.Topology}'.");
        }

        string artifactRoot = Path.GetFullPath(group.ArtifactDirectory);
        (string ProcessId, string CredentialPath)[] clientCredentials = group.Processes
            .Where(process => process.NetworkHost.ResolveRole() == NetworkProcessRole.ReplicatedClient)
            .Select(process => (
                process.Id,
                ResolveOwnedFile(artifactRoot, process.CredentialPath, $"client '{process.Id}' credential")))
            .ToArray();

        foreach ((string processId, string credentialPath) in clientCredentials)
        {
            try
            {
                File.Delete(credentialPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Failed to remove stale credential for process-group client '{processId}'; launch was aborted.",
                    exception);
            }
        }
    }

    public LauncherProcessGroupArtifacts Generate(LauncherResolvedProcessGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Directory.CreateDirectory(group.ArtifactDirectory);
        var artifacts = new List<LauncherNetworkRoleArtifact>(group.Processes.Count);
        var roleDirectories = new List<string>(group.Processes.Count);
        try
        {
            foreach (LauncherResolvedNetworkProcess process in group.Processes)
            {
                string roleDirectory = ResolveRoleDirectory(group.ArtifactDirectory, process.Id);
                roleDirectories.Add(roleDirectory);
                if (Directory.Exists(roleDirectory))
                {
                    Directory.Delete(roleDirectory, recursive: true);
                }

                Directory.CreateDirectory(roleDirectory);
                string graphPath = Path.Combine(roleDirectory, "launcher.graph.json");
                string bootstrapPath = Path.Combine(roleDirectory, "launcher.runtime.json");
                string readinessArtifactPath = ResolveOwnedFile(
                    roleDirectory,
                    Path.Combine(roleDirectory, process.ReadinessArtifactFileName),
                    "readiness artifact");
                LauncherLaunchPlan plan = group.LaunchPlan;
                var graph = new LauncherGraphDocument(
                    plan.SchemaVersion,
                    plan.GeneratedAtUtc,
                    plan.PlanFingerprint,
                    plan.Adapter,
                    plan.BuildMode,
                    plan.Selectors,
                    plan.RootModIds,
                    plan.OrderedModIds,
                    plan.Mods,
                    new LauncherRuntimeArtifacts(
                        "file",
                        bootstrapPath,
                        graphPath,
                        process.Application.WorkingDirectory,
                        process.Application.AssemblyPath,
                        string.Empty),
                    plan.BrowserRuntime,
                    plan.Diagnostics);
                File.WriteAllText(graphPath, JsonSerializer.Serialize(graph, GraphJsonOptions));

                var bootstrapConfig = new AppBootstrapConfig
                {
                    LaunchGraphPath = "launcher.graph.json",
                    LaunchGraphFullPath = graphPath,
                    PlanSelectors = plan.Selectors,
                    PlanRootModIds = plan.RootModIds,
                    PlanOrderedModIds = plan.OrderedModIds,
                    PlanFingerprint = plan.PlanFingerprint,
                    PlanSchemaVersion = plan.SchemaVersion,
                    PlanGeneratedAtUtc = plan.GeneratedAtUtc,
                    BrowserRuntime = plan.BrowserRuntime,
                    NetworkHost = process.NetworkHost
                };
                JsonObject bootstrap = JsonSerializer.SerializeToNode(bootstrapConfig, BootstrapJsonOptions)?.AsObject()
                    ?? throw new InvalidOperationException("Failed to serialize process-group bootstrap metadata.");
                File.WriteAllText(bootstrapPath, bootstrap.ToJsonString(BootstrapJsonOptions));
                artifacts.Add(new LauncherNetworkRoleArtifact(
                    process.Id,
                    process.NetworkHost.ProcessRole,
                    process.Application.Id,
                    process.Application.ProjectPath,
                    process.Application.AssemblyPath,
                    process.Application.WorkingDirectory,
                    graphPath,
                    bootstrapPath,
                    process.CredentialPath,
                    readinessArtifactPath,
                    process.MinimumReplicatedMirrorCount,
                    process.MinimumRenderableMirrorCount));
            }
        }
        catch (Exception generationException)
        {
            var cleanupFailures = new List<Exception>();
            foreach (string roleDirectory in roleDirectories)
            {
                try
                {
                    string validated = ResolveRoleDirectory(
                        group.ArtifactDirectory,
                        Path.GetFileName(roleDirectory));
                    if (Directory.Exists(validated))
                    {
                        Directory.Delete(validated, recursive: true);
                    }
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            if (cleanupFailures.Count != 0)
            {
                cleanupFailures.Insert(0, generationException);
                throw new AggregateException(
                    "Process-group artifact generation failed and generated role directories were not fully cleaned.",
                    cleanupFailures);
            }

            throw;
        }

        return new LauncherProcessGroupArtifacts(
            group.PresetId,
            group.Topology,
            group.ArtifactDirectory,
            group.ClientCount,
            group.ReadinessTimeoutMilliseconds,
            group.ReadinessPollIntervalMilliseconds,
            artifacts);
    }

    public void DeleteSensitiveBootstrapArtifacts(LauncherProcessGroupArtifacts artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        string artifactRoot = Path.GetFullPath(artifacts.ArtifactDirectory);
        foreach (LauncherNetworkRoleArtifact process in artifacts.Processes)
        {
            string roleDirectory = ResolveRoleDirectory(artifactRoot, process.ProcessId);
            string bootstrapPath = ResolveOwnedFile(roleDirectory, process.BootstrapPath, "bootstrap artifact");
            if (File.Exists(bootstrapPath))
            {
                File.Delete(bootstrapPath);
            }
        }
    }

    private static string ResolveRoleDirectory(string artifactDirectory, string processId)
    {
        if (string.IsNullOrWhiteSpace(processId) ||
            Path.IsPathRooted(processId) ||
            processId is "." or ".." ||
            processId.Contains(Path.DirectorySeparatorChar) ||
            processId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException(
                $"Process id '{processId}' cannot be used as a role artifact directory.");
        }

        string root = Path.GetFullPath(artifactDirectory);
        string resolved = Path.GetFullPath(Path.Combine(root, processId));
        EnsureOwnedPath(root, resolved, "role artifact directory");
        return resolved;
    }

    private static string ResolveOwnedFile(string ownerDirectory, string path, string description)
    {
        string resolved = Path.GetFullPath(path);
        EnsureOwnedPath(Path.GetFullPath(ownerDirectory), resolved, description);
        return resolved;
    }

    private static void EnsureOwnedPath(string ownerDirectory, string resolvedPath, string description)
    {
        string owner = ownerDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = owner + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolvedPath.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException(
                $"Resolved {description} '{resolvedPath}' must stay within '{owner}'.");
        }
    }
}

public sealed class LauncherNetworkProcessReadinessReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public bool TryRead(string path, out LauncherNetworkProcessReadinessArtifact artifact)
    {
        return TryRead(path, DateTime.MinValue, out artifact);
    }

    public bool TryRead(
        string path,
        DateTime minimumUpdatedAtUtc,
        out LauncherNetworkProcessReadinessArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (minimumUpdatedAtUtc != DateTime.MinValue && minimumUpdatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Readiness freshness boundary must be expressed in UTC.", nameof(minimumUpdatedAtUtc));
        }
        artifact = new LauncherNetworkProcessReadinessArtifact();
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            artifact = JsonSerializer.Deserialize<LauncherNetworkProcessReadinessArtifact>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"Readiness artifact '{path}' deserialized to null.");
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Readiness artifact '{path}' is invalid: {exception.Message}",
                exception);
        }

        if (artifact.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Readiness artifact '{path}' has unsupported schemaVersion {artifact.SchemaVersion}; expected 1.");
        }

        if (artifact.ProcessRole is not "authoritativeServer" and not "replicatedClient")
        {
            throw new InvalidOperationException(
                $"Readiness artifact '{path}' has unknown processRole '{artifact.ProcessRole}'.");
        }

        if (artifact.ReplicatedMirrorCount < 0 ||
            artifact.RenderableMirrorCount < 0 ||
            artifact.RenderableMirrorCount > artifact.ReplicatedMirrorCount ||
            artifact.ConnectedSeatCount < 0)
        {
            throw new InvalidOperationException(
                $"Readiness artifact '{path}' contains invalid mirror or seat counts.");
        }

        if (artifact.UpdatedAtUtc == default || artifact.UpdatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"Readiness artifact '{path}' requires an explicit UTC updatedAtUtc timestamp.");
        }

        if ((!artifact.RuntimeReady &&
             (artifact.SessionEstablished || artifact.SessionEpoch != 0 ||
              artifact.ReplicatedMirrorCount != 0 || artifact.RenderableMirrorCount != 0 ||
              artifact.ConnectedSeatCount != 0)) ||
            (artifact.ProcessRole == "authoritativeServer" &&
             (!artifact.RuntimeReady || artifact.SessionEstablished || artifact.SessionEpoch == 0 ||
              artifact.ReplicatedMirrorCount != 0 || artifact.RenderableMirrorCount != 0)) ||
            (artifact.ProcessRole == "replicatedClient" &&
             artifact.SessionEstablished && artifact.SessionEpoch == 0))
        {
            throw new InvalidOperationException(
                $"Readiness artifact '{path}' contains contradictory process state.");
        }

        if (minimumUpdatedAtUtc != DateTime.MinValue && artifact.UpdatedAtUtc < minimumUpdatedAtUtc)
        {
            artifact = new LauncherNetworkProcessReadinessArtifact();
            return false;
        }

        return true;
    }
}

public static class LauncherNetworkProcessReadinessEvaluator
{
    public static bool IsServerRuntimeReady(
        LauncherNetworkRoleArtifact process,
        LauncherNetworkProcessReadinessArtifact readiness)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(readiness);
        RequireMatchingRole(process, readiness);
        return process.ProcessRole == "authoritativeServer" && readiness.RuntimeReady;
    }

    public static bool IsGroupReady(
        LauncherNetworkRoleArtifact process,
        LauncherNetworkProcessReadinessArtifact readiness,
        int requiredConnectedSeatCount)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(readiness);
        if (requiredConnectedSeatCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredConnectedSeatCount));
        }

        RequireMatchingRole(process, readiness);
        if (!readiness.RuntimeReady)
        {
            return false;
        }

        if (process.ProcessRole == "authoritativeServer")
        {
            return readiness.ConnectedSeatCount >= requiredConnectedSeatCount;
        }

        return readiness.SessionEstablished &&
            readiness.ReplicatedMirrorCount >= process.MinimumReplicatedMirrorCount &&
            readiness.RenderableMirrorCount >= process.MinimumRenderableMirrorCount &&
            HasCredential(process.CredentialPath);
    }

    private static void RequireMatchingRole(
        LauncherNetworkRoleArtifact process,
        LauncherNetworkProcessReadinessArtifact readiness)
    {
        if (!string.Equals(process.ProcessRole, readiness.ProcessRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Readiness processRole '{readiness.ProcessRole}' does not match process '{process.ProcessId}' role '{process.ProcessRole}'.");
        }
    }

    private static bool HasCredential(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return new FileInfo(path).Length > 0;
    }
}
