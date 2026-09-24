using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ludots.Core.Hosting;

namespace Ludots.Launcher.Backend;

public sealed partial class LauncherService
{
    private sealed record ActiveLaunchProcessGroupRecord(
        string GroupKey,
        string ArtifactDirectory,
        string ConnectionKey,
        IReadOnlyList<ActiveLaunchProcessRecord> Processes);

    public static string GenerateNetworkConnectionKey()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    public LauncherProcessGroupDefinition? TryResolveProcessGroup(IEnumerable<string> selectors)
    {
        var resolvedSelectors = selectors
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .Select(selector => selector.Trim())
            .ToList();
        return TryResolveProcessGroup(resolvedSelectors, LoadPresets());
    }

    public LauncherProcessGroupArtifacts MaterializeProcessGroupArtifacts(
        LauncherLaunchPlan contentPlan,
        LauncherProcessGroupDefinition processGroup,
        string artifactDirectory,
        string? connectionKey = null)
    {
        ArgumentNullException.ThrowIfNull(contentPlan);
        ArgumentNullException.ThrowIfNull(processGroup);
        if (string.IsNullOrWhiteSpace(artifactDirectory))
        {
            throw new ArgumentException("Artifact directory is required.", nameof(artifactDirectory));
        }

        LauncherProcessGroupValidation.Validate(processGroup, IsKnownAdapterId);
        var resolvedConnectionKey = string.IsNullOrWhiteSpace(connectionKey)
            ? GenerateNetworkConnectionKey()
            : connectionKey.Trim();
        if (string.IsNullOrWhiteSpace(resolvedConnectionKey))
        {
            throw new InvalidOperationException("Generated connection key must be non-empty.");
        }

        var groupDirectory = Path.GetFullPath(artifactDirectory);
        Directory.CreateDirectory(groupDirectory);
        foreach (var process in processGroup.Processes)
        {
            var roleDirectory = Path.Combine(groupDirectory, process.Name.Trim());
            if (Directory.Exists(roleDirectory))
            {
                Directory.Delete(roleDirectory, recursive: true);
            }
        }

        var sourceGraphPath = File.Exists(contentPlan.GraphArtifactPath)
            ? contentPlan.GraphArtifactPath
            : WriteLaunchGraphDocument(contentPlan);
        var sourceGraphJson = File.ReadAllText(sourceGraphPath);
        var sourceGraph = JsonSerializer.Deserialize<LauncherGraphDocument>(sourceGraphJson, GraphJsonReadOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize launch graph: {sourceGraphPath}");

        if (!string.Equals(sourceGraph.PlanFingerprint, contentPlan.PlanFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Content launch graph fingerprint does not match the resolved content plan fingerprint.");
        }

        var roles = new List<LauncherRoleArtifactPaths>(processGroup.Processes.Count);
        foreach (var process in processGroup.Processes)
        {
            roles.Add(MaterializeRoleArtifacts(
                contentPlan,
                sourceGraph,
                process,
                processGroup,
                groupDirectory,
                resolvedConnectionKey));
        }

        return new LauncherProcessGroupArtifacts(
            groupDirectory,
            resolvedConnectionKey,
            contentPlan.PlanFingerprint,
            contentPlan.OrderedModIds.ToList(),
            roles);
    }

    public LauncherRoleArtifactPaths MaterializeRoleArtifacts(
        LauncherLaunchPlan contentPlan,
        LauncherProcessGroupMemberDefinition process,
        LauncherProcessGroupDefinition processGroup,
        string roleDirectory,
        string connectionKey)
    {
        ArgumentNullException.ThrowIfNull(contentPlan);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(processGroup);
        if (string.IsNullOrWhiteSpace(roleDirectory))
        {
            throw new ArgumentException("Role directory is required.", nameof(roleDirectory));
        }

        if (string.IsNullOrWhiteSpace(connectionKey))
        {
            throw new ArgumentException("Connection key is required.", nameof(connectionKey));
        }

        LauncherProcessGroupValidation.Validate(processGroup, IsKnownAdapterId);
        var sourceGraphPath = File.Exists(contentPlan.GraphArtifactPath)
            ? contentPlan.GraphArtifactPath
            : WriteLaunchGraphDocument(contentPlan);
        var sourceGraph = JsonSerializer.Deserialize<LauncherGraphDocument>(
                File.ReadAllText(sourceGraphPath),
                GraphJsonReadOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize launch graph: {sourceGraphPath}");

        return MaterializeRoleArtifacts(
            contentPlan,
            sourceGraph,
            process,
            processGroup,
            Path.GetDirectoryName(Path.GetFullPath(roleDirectory)) ?? Path.GetFullPath(roleDirectory),
            connectionKey.Trim(),
            Path.GetFullPath(roleDirectory));
    }

    private LauncherRoleArtifactPaths MaterializeRoleArtifacts(
        LauncherLaunchPlan contentPlan,
        LauncherGraphDocument sourceGraph,
        LauncherProcessGroupMemberDefinition process,
        LauncherProcessGroupDefinition processGroup,
        string groupDirectory,
        string connectionKey,
        string? explicitRoleDirectory = null)
    {
        var processName = process.Name.Trim();
        var adapterId = process.AdapterId.Trim().ToLowerInvariant();
        var processRole = process.ProcessRole.Trim();
        var roleDirectory = explicitRoleDirectory ?? Path.Combine(groupDirectory, processName);
        Directory.CreateDirectory(roleDirectory);

        var graphPath = Path.Combine(roleDirectory, "launcher.graph.json");
        var bootstrapPath = Path.Combine(roleDirectory, "launcher.runtime.json");
        var profile = GetPlatformProfile(adapterId);
        var credentialPath = processRole == "replicatedClient"
            ? Path.Combine(roleDirectory, "session.credential")
            : string.Empty;

        var roleGraph = sourceGraph with
        {
            RuntimeArtifacts = new LauncherRuntimeArtifacts(
                sourceGraph.RuntimeArtifacts.BootstrapArtifactStrategy,
                bootstrapPath,
                graphPath,
                profile.OutputDirectory,
                ResolveAppAssemblyPath(profile),
                profile.LaunchUrl)
        };

        File.WriteAllText(graphPath, JsonSerializer.Serialize(roleGraph, GraphJsonWriteOptions));

        var networkHost = new NetworkHostBootstrapConfig
        {
            ProcessRole = processRole,
            Host = processRole == "replicatedClient" ? processGroup.Host.Trim() : string.Empty,
            Port = processGroup.Port,
            ConnectionKey = connectionKey,
            ClientInstanceId = process.ClientInstanceId,
            CredentialPath = credentialPath,
            FaultProfile = processGroup.FaultProfile.Trim(),
            FaultSeed = process.FaultSeed
        };
        networkHost.Validate();

        WriteRuntimeBootstrapDocument(contentPlan, graphPath, bootstrapPath, networkHost);

        return new LauncherRoleArtifactPaths(
            processName,
            processRole,
            adapterId,
            roleDirectory,
            graphPath,
            bootstrapPath,
            credentialPath,
            networkHost);
    }

    private async Task<LauncherLaunchResult> LaunchProcessGroupAsync(
        IReadOnlyList<string> selectors,
        LauncherProcessGroupDefinition processGroup,
        string? adapterId,
        LauncherBuildMode buildMode,
        LauncherConfig config,
        LauncherPresetDocument presets,
        string? groupKey)
    {
        var validatedGroup = LauncherProcessGroupValidation.Clone(processGroup);
        LauncherProcessGroupValidation.Validate(validatedGroup, IsKnownAdapterId);

        var contentAdapterId = string.IsNullOrWhiteSpace(adapterId)
            ? ResolveContentAdapterId(selectors, presets, config)
            : adapterId.Trim().ToLowerInvariant();
        var resolveResult = ResolvePlan(selectors, contentAdapterId, buildMode, config, BuildCatalog(config), presets);
        var buildResults = await BuildPlanRuntimeAsync(resolveResult.Plan, config, CancellationToken.None);
        var failedModBuild = buildResults.FirstOrDefault(result => !result.Ok);
        if (failedModBuild != null)
        {
            return new LauncherLaunchResult(false, failedModBuild.Output, -1, string.Empty, string.Empty, resolveResult.Plan);
        }

        var requiredAdapters = validatedGroup.Processes
            .Select(process => process.AdapterId.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var requiredAdapter in requiredAdapters)
        {
            var appBuild = await BuildAppAsync(requiredAdapter);
            if (!appBuild.Ok)
            {
                return new LauncherLaunchResult(false, appBuild.Output, -1, string.Empty, string.Empty, resolveResult.Plan);
            }
        }

        WriteLaunchGraphDocument(resolveResult.Plan);
        var resolvedGroupKey = string.IsNullOrWhiteSpace(groupKey)
            ? BuildProcessGroupKey(selectors, validatedGroup)
            : groupKey.Trim();
        ReplacePreviousActiveProcessGroup(resolvedGroupKey);
        foreach (var requiredAdapter in requiredAdapters)
        {
            ReplacePreviousActiveProcess(GetPlatformProfile(requiredAdapter).Id, ResolveAppAssemblyPath(GetPlatformProfile(requiredAdapter)));
        }

        var artifactDirectory = Path.Combine(
            _repoRoot,
            "artifacts",
            "launcher",
            "process-groups",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        var artifacts = MaterializeProcessGroupArtifacts(
            resolveResult.Plan,
            validatedGroup,
            artifactDirectory);

        var started = new List<(LauncherRoleArtifactPaths Role, Process Process)>();
        try
        {
            foreach (var role in OrderProcessGroupRolesForLaunch(artifacts.Roles))
            {
                var startInfo = new ProcessStartInfo(
                    ResolveDotnetCommand(),
                    $"exec --roll-forward Major \"{ResolveAppAssemblyPath(GetPlatformProfile(role.AdapterId))}\" \"{role.BootstrapPath}\"")
                {
                    WorkingDirectory = role.RoleDirectory,
                    UseShellExecute = false
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException($"Failed to start process group member '{role.ProcessName}'.");
                }

                started.Add((role, process));
            }
        }
        catch (Exception ex)
        {
            foreach (var entry in started)
            {
                TryKillProcessTree(entry.Process);
            }

            return new LauncherLaunchResult(
                false,
                ex.Message,
                -1,
                string.Empty,
                artifacts.ArtifactDirectory,
                resolveResult.Plan);
        }

        var launched = started
            .Select(entry => new LauncherLaunchedProcessInfo(
                entry.Role.ProcessName,
                entry.Role.ProcessRole,
                entry.Role.AdapterId,
                entry.Process.Id,
                entry.Role.BootstrapPath))
            .ToList();
        PersistActiveProcessGroup(
            resolvedGroupKey,
            artifacts.ArtifactDirectory,
            artifacts.ConnectionKey,
            started.Select(entry => new ActiveLaunchProcessRecord(
                entry.Process.Id,
                entry.Process.StartTime.ToUniversalTime().Ticks,
                entry.Role.AdapterId,
                Path.GetFullPath(ResolveAppAssemblyPath(GetPlatformProfile(entry.Role.AdapterId))),
                Path.GetFullPath(entry.Role.BootstrapPath))).ToList());

        var server = launched.First(process => process.ProcessRole == "authoritativeServer");
        return new LauncherLaunchResult(
            true,
            string.Empty,
            server.Pid,
            string.Empty,
            artifacts.ArtifactDirectory,
            resolveResult.Plan,
            new LauncherProcessGroupLaunchResult(
                artifacts.ArtifactDirectory,
                artifacts.ConnectionKey,
                launched));
    }

    private LauncherProcessGroupDefinition? TryResolveProcessGroup(
        IReadOnlyList<string> selectors,
        LauncherPresetDocument presets)
    {
        LauncherProcessGroupDefinition? resolved = null;
        string? sourcePresetId = null;
        foreach (var selector in selectors)
        {
            if (!selector.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var presetId = selector["preset:".Length..].Trim();
            var preset = presets.Presets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (preset?.ProcessGroup == null)
            {
                continue;
            }

            if (resolved != null &&
                !string.Equals(sourcePresetId, preset.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Selectors resolve multiple processGroup presets ('{sourcePresetId}' and '{preset.Id}').");
            }

            resolved = preset.ProcessGroup;
            sourcePresetId = preset.Id;
        }

        return resolved == null ? null : LauncherProcessGroupValidation.Clone(resolved);
    }

    private string ResolveContentAdapterId(
        IReadOnlyList<string> selectors,
        LauncherPresetDocument presets,
        LauncherConfig config)
    {
        foreach (var selector in selectors)
        {
            if (!selector.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var presetId = selector["preset:".Length..].Trim();
            var preset = presets.Presets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preset?.AdapterId))
            {
                return preset!.AdapterId!.Trim().ToLowerInvariant();
            }
        }

        return ResolveSelectedAdapterId(config, LoadPreferences());
    }

    private static IEnumerable<LauncherRoleArtifactPaths> OrderProcessGroupRolesForLaunch(
        IReadOnlyList<LauncherRoleArtifactPaths> roles)
    {
        return roles
            .OrderBy(role => role.ProcessRole == "authoritativeServer" ? 0 : 1)
            .ThenBy(role => role.ProcessName, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildProcessGroupKey(
        IReadOnlyList<string> selectors,
        LauncherProcessGroupDefinition processGroup)
    {
        var presetSelector = selectors.FirstOrDefault(selector =>
            selector.StartsWith("preset:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(presetSelector))
        {
            return presetSelector!["preset:".Length..].Trim();
        }

        return $"endpoint-{processGroup.Host.Trim()}-{processGroup.Port}";
    }

    private void ReplacePreviousActiveProcessGroup(string groupKey)
    {
        var recordPath = GetActiveProcessGroupRecordPath(groupKey);
        var record = ReadActiveProcessGroupRecord(recordPath);
        if (record == null)
        {
            return;
        }

        foreach (var processRecord in record.Processes)
        {
            TryKillRecordedProcess(processRecord);
        }

        DeleteActiveProcessRecord(recordPath);
    }

    private void PersistActiveProcessGroup(
        string groupKey,
        string artifactDirectory,
        string connectionKey,
        IReadOnlyList<ActiveLaunchProcessRecord> processes)
    {
        var record = new ActiveLaunchProcessGroupRecord(
            groupKey,
            Path.GetFullPath(artifactDirectory),
            connectionKey,
            processes);
        var recordPath = GetActiveProcessGroupRecordPath(groupKey);
        var directory = Path.GetDirectoryName(recordPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            recordPath,
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ActiveLaunchProcessGroupRecord? ReadActiveProcessGroupRecord(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ActiveLaunchProcessGroupRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static string GetActiveProcessGroupRecordPath(string groupKey)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var safeGroupKey = string.IsNullOrWhiteSpace(groupKey)
            ? "default"
            : string.Concat(groupKey.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeGroupKey))
        {
            safeGroupKey = "default";
        }

        return Path.Combine(appData, "Ludots", "Launcher", "active-process-groups", $"{safeGroupKey}.json");
    }

    private void ReplacePreviousActiveProcess(string adapterId, string appAssemblyPath)
    {
        var recordPath = GetActiveProcessRecordPath(adapterId);
        var record = ReadActiveProcessRecord(recordPath);
        if (record == null)
        {
            return;
        }

        if (!PathsEqual(record.AppAssemblyPath, appAssemblyPath))
        {
            DeleteActiveProcessRecord(recordPath);
            return;
        }

        TryKillRecordedProcess(record);
        DeleteActiveProcessRecord(recordPath);
    }

    private static void TryKillRecordedProcess(ActiveLaunchProcessRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.Pid);
            if (process.HasExited || !StartTimeMatches(process, record.StartedAtUtcTicks))
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private bool IsKnownAdapterId(string adapterId)
    {
        return GetPlatformProfiles().Any(profile =>
            string.Equals(profile.Id, adapterId, StringComparison.OrdinalIgnoreCase));
    }
}
