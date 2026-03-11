using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Modding;

namespace Ludots.Launcher.Backend;

public sealed class LauncherService
{
    private const string DefaultPresetId = "default";
    private readonly string _repoRoot;
    private readonly LauncherConfigService _configService;

    public LauncherService(string repoRoot, string? configPath = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repoRoot));
        }

        _repoRoot = Path.GetFullPath(repoRoot);
        _configService = new LauncherConfigService(configPath);
    }

    public static string FindRepoRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current != null && !Directory.Exists(Path.Combine(current.FullName, "assets")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            throw new DirectoryNotFoundException($"Could not locate Ludots repository root from '{startDirectory}'.");
        }

        return current.FullName;
    }

    public LauncherStateSnapshot GetState()
    {
        var config = LoadConfig();
        return new LauncherStateSnapshot(
            GetPlatformProfiles(),
            config.SelectedPlatformId,
            config.Presets.Values.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            config.SelectedPresetId,
            LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, config));
    }

    public IReadOnlyList<LauncherModInfo> DiscoverMods()
    {
        var config = LoadConfig();
        return DiscoverMods(config);
    }

    public IReadOnlyList<string> GetWorkspaceSources()
    {
        var config = LoadConfig();
        return LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, config);
    }

    public LauncherStateSnapshot AddWorkspaceSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Workspace source path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Workspace source not found: {fullPath}");
        }

        var config = LoadConfig();
        if (!config.WorkspaceSources.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            config.WorkspaceSources.Add(fullPath);
            SaveConfig(config);
        }

        return GetState();
    }

    public LauncherStateSnapshot SelectPlatform(string platformId)
    {
        _ = GetPlatformProfile(platformId);
        var config = LoadConfig();
        config.SelectedPlatformId = platformId;
        SaveConfig(config);
        return GetState();
    }

    public LauncherStateSnapshot SelectPreset(string? presetId)
    {
        var config = LoadConfig();
        config.SelectedPresetId = string.IsNullOrWhiteSpace(presetId) ? null : presetId;
        SaveConfig(config);
        return GetState();
    }

    public LauncherPreset SavePreset(string? presetId, string name, IEnumerable<string> activeModIds, bool includeDependencies, bool selectAfterSave)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name is required.", nameof(name));
        }

        var config = LoadConfig();
        var resolvedPresetId = string.IsNullOrWhiteSpace(presetId) ? Guid.NewGuid().ToString("N") : presetId.Trim();
        var preset = new LauncherPreset
        {
            Id = resolvedPresetId,
            Name = name.Trim(),
            ActiveModIds = activeModIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IncludeDependencies = includeDependencies
        };

        config.Presets[resolvedPresetId] = preset;
        if (selectAfterSave)
        {
            config.SelectedPresetId = resolvedPresetId;
        }

        SaveConfig(config);
        return preset;
    }

    public void DeletePreset(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            throw new ArgumentException("Preset id is required.", nameof(presetId));
        }

        var config = LoadConfig();
        config.Presets.Remove(presetId);
        if (string.Equals(config.SelectedPresetId, presetId, StringComparison.OrdinalIgnoreCase))
        {
            config.SelectedPresetId = DefaultPresetId;
        }

        EnsureDefaults(config, persistChanges: false);
        SaveConfig(config);
    }

    public string FixModProject(string modId)
    {
        var mod = FindMod(modId);
        var existingProjectPath = ResolveBuildProjectPath(mod);
        if (!string.IsNullOrWhiteSpace(existingProjectPath) && File.Exists(existingProjectPath))
        {
            return existingProjectPath;
        }

        var sdkPropsPath = Path.Combine(_repoRoot, "assets", "ModSdk", "ModSdk.props");
        if (!File.Exists(sdkPropsPath))
        {
            throw new FileNotFoundException($"Mod SDK props not found: {sdkPropsPath}");
        }

        var projectPath = Path.Combine(mod.RootPath, $"{mod.Name}.csproj");
        var relativeSdkPropsPath = Path.GetRelativePath(mod.RootPath, sdkPropsPath);
        var projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputPath>bin</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <Import Project=""{relativeSdkPropsPath}"" />

</Project>";

        File.WriteAllText(projectPath, projectContent);
        return projectPath;
    }

    public async Task<LauncherBuildResult> BuildModAsync(string modId)
    {
        var results = await BuildModsAsync(new[] { modId });
        return results.Single();
    }

    public async Task<IReadOnlyList<LauncherBuildResult>> BuildModsAsync(IEnumerable<string> modIds)
    {
        var allMods = DiscoverMods().ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var orderedMods = ResolveModClosure(modIds, allMods, includeDependencies: true);
        var results = new List<LauncherBuildResult>(orderedMods.Count);
        foreach (var mod in orderedMods)
        {
            results.Add(await BuildModInternalAsync(mod));
        }

        return results;
    }

    public async Task<LauncherBuildResult> BuildAppAsync(string platformId)
    {
        var profile = GetPlatformProfile(platformId);
        var output = new StringBuilder();

        if (string.Equals(profile.Id, LauncherPlatformIds.Web, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(profile.ClientProjectDirectory))
        {
            if (!Directory.Exists(Path.Combine(profile.ClientProjectDirectory, "node_modules")))
            {
                var installResult = await RunNodePackageCommandAsync("ci", profile.ClientProjectDirectory, timeoutMs: 300_000);
                output.AppendLine(installResult.Output);
                if (installResult.ExitCode != 0)
                {
                    return new LauncherBuildResult(platformId, false, installResult.ExitCode, output.ToString());
                }
            }

            var clientBuildResult = await RunNodePackageCommandAsync("run build", profile.ClientProjectDirectory, timeoutMs: 300_000);
            output.AppendLine(clientBuildResult.Output);
            if (clientBuildResult.ExitCode != 0)
            {
                return new LauncherBuildResult(platformId, false, clientBuildResult.ExitCode, output.ToString());
            }
        }

        var dotnetBuildResult = await RunProcessAsync(
            "dotnet",
            $"build \"{profile.AppProjectPath}\" -c Release",
            _repoRoot,
            timeoutMs: 300_000);
        output.AppendLine(dotnetBuildResult.Output);
        return new LauncherBuildResult(platformId, dotnetBuildResult.ExitCode == 0, dotnetBuildResult.ExitCode, output.ToString());
    }

    public string WriteGameJson(string platformId, IEnumerable<string> modIds)
    {
        var profile = GetPlatformProfile(platformId);
        var allMods = DiscoverMods().ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var orderedMods = ResolveModClosure(modIds, allMods, includeDependencies: true);
        Directory.CreateDirectory(profile.OutputDirectory);

        var modPaths = orderedMods
            .Select(mod => Path.GetRelativePath(profile.OutputDirectory, mod.RootPath).Replace('\\', '/'))
            .ToArray();

        var gameJsonPath = Path.Combine(profile.OutputDirectory, "game.json");
        var json = JsonSerializer.Serialize(new { ModPaths = modPaths }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(gameJsonPath, json);
        return gameJsonPath;
    }

    public async Task<LauncherLaunchResult> LaunchAsync(string platformId, IEnumerable<string> modIds)
    {
        var profile = GetPlatformProfile(platformId);
        var allMods = DiscoverMods().ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var orderedMods = ResolveModClosure(modIds, allMods, includeDependencies: true);
        var invalidBuilds = orderedMods.Where(mod => mod.BuildState != LauncherBuildState.Succeeded).ToList();
        if (invalidBuilds.Count > 0)
        {
            var message = string.Join(", ", invalidBuilds.Select(mod => $"{mod.Id}({mod.BuildState})"));
            return new LauncherLaunchResult(false, $"Launch blocked. Build required: {message}", -1, string.Empty);
        }

        if (string.Equals(profile.Id, LauncherPlatformIds.Web, StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(profile.ClientDistributionDirectory))
        {
            return new LauncherLaunchResult(false, "Web client dist is missing. Build the web platform first.", -1, string.Empty);
        }

        WriteGameJson(platformId, orderedMods.Select(mod => mod.Id));

        var appAssemblyPath = ResolveAppAssemblyPath(profile);
        if (!File.Exists(appAssemblyPath))
        {
            return new LauncherLaunchResult(false, $"Platform build output missing: {appAssemblyPath}", -1, string.Empty);
        }

        var startInfo = new ProcessStartInfo("dotnet", $"\"{appAssemblyPath}\" game.json")
        {
            WorkingDirectory = profile.OutputDirectory,
            UseShellExecute = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            return new LauncherLaunchResult(false, "Failed to start platform process.", -1, string.Empty);
        }

        await Task.CompletedTask;
        return new LauncherLaunchResult(true, string.Empty, process.Id, profile.LaunchUrl);
    }

    public async Task<string> GenerateSolutionAsync(string modId)
    {
        var mod = FindMod(modId);
        var solutionPath = Path.Combine(mod.RootPath, $"{mod.Id}.sln");

        var createResult = await RunProcessAsync("dotnet", $"new sln -n {mod.Id} --force", mod.RootPath, timeoutMs: 30_000);
        if (createResult.ExitCode != 0)
        {
            throw new InvalidOperationException(createResult.Output);
        }

        var projectPath = ResolveBuildProjectPath(mod);
        if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
        {
            await RunProcessAsync("dotnet", $"sln \"{solutionPath}\" add \"{projectPath}\"", mod.RootPath, timeoutMs: 30_000);
        }

        var allMods = DiscoverMods().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var dependencyId in mod.Dependencies.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!allMods.TryGetValue(dependencyId, out var dependency))
            {
                continue;
            }

            var dependencyProjectPath = ResolveBuildProjectPath(dependency);
            if (!string.IsNullOrWhiteSpace(dependencyProjectPath) && File.Exists(dependencyProjectPath))
            {
                await RunProcessAsync("dotnet", $"sln \"{solutionPath}\" add \"{dependencyProjectPath}\"", mod.RootPath, timeoutMs: 30_000);
            }
        }

        var coreProjectPath = Path.Combine(_repoRoot, "src", "Core", "Ludots.Core.csproj");
        if (File.Exists(coreProjectPath))
        {
            await RunProcessAsync("dotnet", $"sln \"{solutionPath}\" add \"{coreProjectPath}\"", mod.RootPath, timeoutMs: 30_000);
        }

        return solutionPath;
    }

    public async Task<string> CreateModAsync(string modId, string template, string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("Mod id is required.", nameof(modId));
        }

        var toolProjectPath = Path.Combine(_repoRoot, "src", "Tools", "Ludots.Tool", "Ludots.Tool.csproj");
        var args = new StringBuilder($"run --project \"{toolProjectPath}\" -- mod init --id {modId} --template {template}");
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            args.Append($" --dir \"{targetDirectory}\"");
        }

        var result = await RunProcessAsync("dotnet", args.ToString(), _repoRoot, timeoutMs: 120_000);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return result.Output;
    }

    private LauncherConfig LoadConfig()
    {
        var config = _configService.LoadOrDefault();
        EnsureDefaults(config, persistChanges: true);
        return config;
    }

    private void SaveConfig(LauncherConfig config)
    {
        _configService.Save(config);
    }

    private void EnsureDefaults(LauncherConfig config, bool persistChanges)
    {
        var changed = false;
        var defaultModsDirectory = Path.Combine(_repoRoot, "mods");
        if (!config.WorkspaceSources.Contains(defaultModsDirectory, StringComparer.OrdinalIgnoreCase))
        {
            config.WorkspaceSources.Add(defaultModsDirectory);
            changed = true;
        }

        if (config.Presets.Count == 0)
        {
            config.Presets[DefaultPresetId] = new LauncherPreset
            {
                Id = DefaultPresetId,
                Name = "Default",
                ActiveModIds = ReadDefaultPresetModIds(),
                IncludeDependencies = true
            };
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.SelectedPresetId) || !config.Presets.ContainsKey(config.SelectedPresetId))
        {
            config.SelectedPresetId = config.Presets.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).First();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.SelectedPlatformId) ||
            GetPlatformProfiles().All(profile => !string.Equals(profile.Id, config.SelectedPlatformId, StringComparison.OrdinalIgnoreCase)))
        {
            config.SelectedPlatformId = LauncherPlatformIds.Raylib;
            changed = true;
        }

        if (changed && persistChanges)
        {
            SaveConfig(config);
        }
    }

    private List<string> ReadDefaultPresetModIds()
    {
        var gameJsonPath = Path.Combine(_repoRoot, "src", "Apps", "Raylib", "Ludots.App.Raylib", "game.json");
        if (!File.Exists(gameJsonPath))
        {
            return new List<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(gameJsonPath));
            if (!document.RootElement.TryGetProperty("ModPaths", out var modPathsElement) || modPathsElement.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            var appDirectory = Path.GetDirectoryName(gameJsonPath) ?? _repoRoot;
            var modIds = new List<string>();
            foreach (var modPathElement in modPathsElement.EnumerateArray())
            {
                var rawPath = modPathElement.GetString();
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                var resolvedPath = Path.IsPathRooted(rawPath)
                    ? Path.GetFullPath(rawPath)
                    : Path.GetFullPath(Path.Combine(appDirectory, rawPath));
                modIds.Add(Path.GetFileName(resolvedPath));
            }

            return modIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private IReadOnlyList<LauncherPlatformProfile> GetPlatformProfiles()
    {
        return new[]
        {
            new LauncherPlatformProfile(
                LauncherPlatformIds.Raylib,
                "Raylib",
                Path.Combine(_repoRoot, "src", "Apps", "Raylib", "Ludots.App.Raylib", "Ludots.App.Raylib.csproj"),
                Path.Combine(_repoRoot, "src", "Apps", "Raylib", "Ludots.App.Raylib", "bin", "Release", "net8.0"),
                string.Empty,
                string.Empty,
                string.Empty),
            new LauncherPlatformProfile(
                LauncherPlatformIds.Web,
                "Web",
                Path.Combine(_repoRoot, "src", "Apps", "Web", "Ludots.App.Web", "Ludots.App.Web.csproj"),
                Path.Combine(_repoRoot, "src", "Apps", "Web", "Ludots.App.Web", "bin", "Release", "net8.0"),
                Path.Combine(_repoRoot, "src", "Client", "Web"),
                Path.Combine(_repoRoot, "src", "Client", "Web", "dist"),
                "http://localhost:5200")
        };
    }

    private LauncherPlatformProfile GetPlatformProfile(string platformId)
    {
        var match = GetPlatformProfiles().FirstOrDefault(profile => string.Equals(profile.Id, platformId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            throw new InvalidOperationException($"Unknown platform: {platformId}");
        }

        return match;
    }

    private static string ResolveAppAssemblyPath(LauncherPlatformProfile profile)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(profile.AppProjectPath) + ".dll";
        return Path.Combine(profile.OutputDirectory, assemblyName);
    }

    private IReadOnlyList<LauncherModInfo> DiscoverMods(LauncherConfig config)
    {
        var results = new List<LauncherModInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, config))
        {
            foreach (var directory in Directory.GetDirectories(source))
            {
                var manifestPath = Path.Combine(directory, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                if (!seen.Add(manifest.Name))
                {
                    continue;
                }

                var fullRootPath = Path.GetFullPath(directory);
                var buildProjectPath = ResolveBuildProjectPath(fullRootPath, manifest.Name);
                var hasProject = !string.IsNullOrWhiteSpace(buildProjectPath) && File.Exists(buildProjectPath);
                var (buildState, lastBuildMessage) = ResolveBuildState(fullRootPath, manifest.Main, hasProject);
                results.Add(new LauncherModInfo
                {
                    Id = manifest.Name,
                    Name = manifest.Name,
                    Version = manifest.Version,
                    Priority = manifest.Priority,
                    Dependencies = new Dictionary<string, string>(manifest.Dependencies, StringComparer.OrdinalIgnoreCase),
                    RootPath = fullRootPath,
                    Description = manifest.Description ?? string.Empty,
                    Author = manifest.Author ?? string.Empty,
                    Tags = manifest.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList() ?? new List<string>(),
                    ChangelogFile = manifest.Changelog ?? string.Empty,
                    HasThumbnail = HasLauncherThumbnail(fullRootPath),
                    HasReadme = File.Exists(Path.Combine(fullRootPath, "README.md")),
                    MainAssemblyPath = manifest.Main ?? string.Empty,
                    HasProject = hasProject,
                    BuildState = buildState,
                    LastBuildMessage = lastBuildMessage
                });
            }
        }

        return results
            .OrderBy(mod => mod.Priority)
            .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private LauncherModInfo FindMod(string modId)
    {
        var mod = DiscoverMods().FirstOrDefault(item => string.Equals(item.Id, modId, StringComparison.OrdinalIgnoreCase));
        if (mod == null)
        {
            throw new InvalidOperationException($"Mod not found: {modId}");
        }

        return mod;
    }

    private List<LauncherModInfo> ResolveModClosure(IEnumerable<string> requestedModIds, Dictionary<string, LauncherModInfo> allMods, bool includeDependencies)
    {
        var requested = requestedModIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
        {
            throw new InvalidOperationException("At least one mod must be selected.");
        }

        if (!includeDependencies)
        {
            return requested.Select(id => allMods[id]).ToList();
        }

        var order = new List<LauncherModInfo>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string modId)
        {
            if (visited.Contains(modId))
            {
                return;
            }

            if (!allMods.TryGetValue(modId, out var mod))
            {
                throw new InvalidOperationException($"Unknown mod: {modId}");
            }

            if (!visiting.Add(modId))
            {
                throw new InvalidOperationException($"Dependency cycle detected at '{modId}'.");
            }

            foreach (var dependencyId in mod.Dependencies.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                Visit(dependencyId);
            }

            visiting.Remove(modId);
            visited.Add(modId);
            order.Add(mod);
        }

        foreach (var requestedId in requested.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            Visit(requestedId);
        }

        return order;
    }

    private async Task<LauncherBuildResult> BuildModInternalAsync(LauncherModInfo mod)
    {
        var buildProjectPath = ResolveBuildProjectPath(mod);
        if (string.IsNullOrWhiteSpace(buildProjectPath) || !File.Exists(buildProjectPath))
        {
            return new LauncherBuildResult(mod.Id, false, 1, $"No build project found for {mod.Id}.");
        }

        var output = new StringBuilder();
        var projectDirectory = Path.GetDirectoryName(buildProjectPath) ?? mod.RootPath;
        var buildResult = await RunProcessAsync(
            "dotnet",
            $"build \"{buildProjectPath}\" /p:ProduceReferenceAssembly=true -c Release",
            projectDirectory,
            timeoutMs: 300_000);
        output.AppendLine(buildResult.Output);
        if (buildResult.ExitCode != 0)
        {
            return new LauncherBuildResult(mod.Id, false, buildResult.ExitCode, output.ToString());
        }

        var referenceExportPath = ExportReferenceAssembly(mod, projectDirectory);
        output.AppendLine($"Exported ref: {referenceExportPath}");

        var graphCompileResult = await RunProcessAsync(
            "dotnet",
            $"run --project \"{Path.Combine(_repoRoot, "src", "Tools", "Ludots.Tool", "Ludots.Tool.csproj")}\" -- graph compile --mod \"{mod.Id}\" --assetsRoot \"{_repoRoot}\"",
            _repoRoot,
            timeoutMs: 300_000);
        output.AppendLine(graphCompileResult.Output);
        if (graphCompileResult.ExitCode != 0)
        {
            return new LauncherBuildResult(mod.Id, false, graphCompileResult.ExitCode, output.ToString());
        }

        return new LauncherBuildResult(mod.Id, true, 0, output.ToString());
    }

    private string ExportReferenceAssembly(LauncherModInfo mod, string projectDirectory)
    {
        if (!TryFindReferenceAssembly(projectDirectory, mod.Name, out var referenceAssemblyPath))
        {
            throw new InvalidOperationException($"Reference assembly not found for {mod.Id}.");
        }

        var referenceDirectory = Path.Combine(mod.RootPath, "ref");
        Directory.CreateDirectory(referenceDirectory);
        var targetPath = Path.Combine(referenceDirectory, $"{mod.Name}.dll");
        File.Copy(referenceAssemblyPath, targetPath, overwrite: true);
        return targetPath;
    }

    private static bool TryFindReferenceAssembly(string projectDirectory, string assemblyName, out string path)
    {
        path = string.Empty;
        var objectDirectory = Path.Combine(projectDirectory, "obj");
        if (!Directory.Exists(objectDirectory))
        {
            return false;
        }

        var candidates = Directory.EnumerateFiles(objectDirectory, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Where(candidate =>
            {
                var normalized = candidate.Replace('\\', '/');
                return normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("/refint/", StringComparison.OrdinalIgnoreCase)
                    && normalized.Contains("/release/", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        path = candidates[0];
        return true;
    }

    private static bool HasLauncherThumbnail(string rootPath)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            if (File.Exists(Path.Combine(rootPath, "assets", "Launcher", "thumbnail" + extension)))
            {
                return true;
            }
        }

        return false;
    }

    private string? ResolveBuildProjectPath(LauncherModInfo mod)
    {
        return ResolveBuildProjectPath(mod.RootPath, mod.Name);
    }

    private string? ResolveBuildProjectPath(string rootPath, string modName)
    {
        var rootProject = Directory.GetFiles(rootPath, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rootProject) && ProjectHasUserSourceFiles(rootProject))
        {
            return rootProject;
        }

        var repositoryProject = Path.Combine(_repoRoot, "mods", modName, $"{modName}.csproj");
        if (File.Exists(repositoryProject))
        {
            return repositoryProject;
        }

        return rootProject;
    }

    private static bool ProjectHasUserSourceFiles(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return false;
        }

        foreach (var sourceFilePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = sourceFilePath.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static (LauncherBuildState State, string Message) ResolveBuildState(string rootPath, string? mainAssemblyPath, bool hasProject)
    {
        if (!hasProject)
        {
            return (LauncherBuildState.NoProject, "No project");
        }

        if (string.IsNullOrWhiteSpace(mainAssemblyPath))
        {
            return (LauncherBuildState.Failed, "Invalid main");
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(rootPath, mainAssemblyPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(assemblyPath))
        {
            return (LauncherBuildState.Idle, "Not built");
        }

        var assemblyWriteUtc = File.GetLastWriteTimeUtc(assemblyPath);
        if (GetLatestSourceWriteUtc(rootPath) > assemblyWriteUtc)
        {
            return (LauncherBuildState.Outdated, "Outdated");
        }

        return (LauncherBuildState.Succeeded, "OK");
    }

    private static DateTime GetLatestSourceWriteUtc(string rootPath)
    {
        var latest = DateTime.MinValue;
        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
        {
            var normalized = filePath.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var writeUtc = File.GetLastWriteTimeUtc(filePath);
            if (writeUtc > latest)
            {
                latest = writeUtc;
            }
        }

        return latest;
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            return (-1, $"Process timed out after {timeoutMs} ms.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return (process.ExitCode, output);
    }

    private static Task<(int ExitCode, string Output)> RunNodePackageCommandAsync(string arguments, string workingDirectory, int timeoutMs)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunProcessAsync("cmd.exe", $"/c npm {arguments}", workingDirectory, timeoutMs);
        }

        return RunProcessAsync("npm", arguments, workingDirectory, timeoutMs);
    }
}
