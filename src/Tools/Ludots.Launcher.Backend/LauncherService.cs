using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Modding;

namespace Ludots.Launcher.Backend;

public sealed class LauncherService
{
    private readonly string _repoRoot;
    private readonly LauncherConfigService _configService;

    public LauncherService(
        string repoRoot,
        string? configPath = null,
        string? presetsPath = null,
        string? preferencesPath = null,
        string? userConfigPath = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repoRoot));
        }

        _repoRoot = Path.GetFullPath(repoRoot);
        _configService = new LauncherConfigService(_repoRoot, configPath, presetsPath, preferencesPath, userConfigPath);
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
        var preferences = LoadPreferences();
        var presetDocument = LoadPresets();
        var catalog = BuildCatalog(config);
        var selectedAdapterId = ResolveSelectedAdapterId(config, preferences);
        var selectedPresetId = ResolveSelectedPresetId(preferences, presetDocument);

        return new LauncherStateSnapshot(
            GetPlatformProfiles(),
            selectedAdapterId,
            BuildPresetViews(presetDocument, catalog),
            selectedPresetId,
            LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, config),
            config.Bindings
                .OrderBy(binding => binding.Name, StringComparer.OrdinalIgnoreCase)
                .Select(binding => new LauncherBindingInfo(binding.Name, binding.Target.Type, binding.Target.Value))
                .ToList());
    }

    public IReadOnlyList<LauncherModInfo> DiscoverMods()
    {
        return BuildCatalog(LoadConfig()).Entries.Select(entry => entry.Info).ToList();
    }

    public IReadOnlyList<string> GetWorkspaceSources()
    {
        return LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, LoadConfig());
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

        var config = LoadRepoConfig();
        if (!config.ScanRoots.Any(root => PathsEqual(LauncherWorkspaceSourceResolver.ResolvePath(_repoRoot, root.Path), fullPath)))
        {
            config.ScanRoots.Add(new LauncherScanRoot
            {
                Id = CreateStableId("root", Path.GetFileName(fullPath)),
                Path = GetPortablePath(fullPath),
                ScanMode = "recursive",
                Enabled = true
            });
            SaveRepoConfig(config);
        }

        return GetState();
    }

    public LauncherStateSnapshot UpsertBinding(string name, string targetType, string targetValue, string? projectPath = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Binding name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(targetType))
        {
            throw new ArgumentException("Binding target type is required.", nameof(targetType));
        }

        if (string.IsNullOrWhiteSpace(targetValue))
        {
            throw new ArgumentException("Binding target value is required.", nameof(targetValue));
        }

        var config = LoadRepoConfig();
        config.Bindings.RemoveAll(binding => string.Equals(binding.Name, name, StringComparison.OrdinalIgnoreCase));
        config.Bindings.Add(new LauncherBinding
        {
            Name = name.Trim(),
            Target = new LauncherBindingTarget
            {
                Type = targetType.Trim(),
                Value = targetValue.Trim(),
                ProjectPath = string.IsNullOrWhiteSpace(projectPath) ? null : projectPath.Trim()
            }
        });
        SaveRepoConfig(config);
        return GetState();
    }

    public LauncherStateSnapshot DeleteBinding(string name)
    {
        var config = LoadRepoConfig();
        config.Bindings.RemoveAll(binding => string.Equals(binding.Name, name, StringComparison.OrdinalIgnoreCase));
        SaveRepoConfig(config);
        return GetState();
    }

    public LauncherStateSnapshot SelectPlatform(string platformId)
    {
        _ = GetPlatformProfile(platformId);
        var preferences = LoadPreferences();
        preferences.LastAdapterId = platformId;
        SavePreferences(preferences);
        return GetState();
    }

    public LauncherStateSnapshot SelectPreset(string? presetId)
    {
        var preferences = LoadPreferences();
        preferences.LastPresetId = string.IsNullOrWhiteSpace(presetId) ? null : presetId.Trim();
        SavePreferences(preferences);
        return GetState();
    }

    public LauncherPreset SavePreset(string? presetId, string name, IEnumerable<string> activeModIds, bool includeDependencies, bool selectAfterSave)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name is required.", nameof(name));
        }

        var presetDocument = LoadPresets();
        var resolvedPresetId = string.IsNullOrWhiteSpace(presetId) ? CreateStableId("preset", name) : presetId.Trim();
        presetDocument.Presets.RemoveAll(item => string.Equals(item.Id, resolvedPresetId, StringComparison.OrdinalIgnoreCase));
        presetDocument.Presets.Add(new LauncherPresetDefinition
        {
            Id = resolvedPresetId,
            Name = name.Trim(),
            Selectors = activeModIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Select(id => $"mod:{id}")
                .ToList(),
            BuildMode = LauncherBuildMode.Auto.ToString().ToLowerInvariant()
        });
        SavePresets(presetDocument);

        if (selectAfterSave)
        {
            var preferences = LoadPreferences();
            preferences.LastPresetId = resolvedPresetId;
            SavePreferences(preferences);
        }

        return BuildPresetViews(presetDocument, BuildCatalog(LoadConfig()))
            .First(item => string.Equals(item.Id, resolvedPresetId, StringComparison.OrdinalIgnoreCase));
    }

    public void DeletePreset(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            throw new ArgumentException("Preset id is required.", nameof(presetId));
        }

        var presetDocument = LoadPresets();
        presetDocument.Presets.RemoveAll(item => string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
        SavePresets(presetDocument);

        var preferences = LoadPreferences();
        if (string.Equals(preferences.LastPresetId, presetId, StringComparison.OrdinalIgnoreCase))
        {
            preferences.LastPresetId = presetDocument.Presets
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Id)
                .FirstOrDefault();
            SavePreferences(preferences);
        }
    }

    public string FixModProject(string modId)
    {
        var config = LoadConfig();
        var catalog = BuildCatalog(config);
        var entry = ResolveUniqueModEntry(modId, catalog.ById);
        return EnsureProjectFile(entry, config);
    }

    public async Task<LauncherBuildResult> BuildModAsync(string modId)
    {
        var results = await BuildModsAsync(new[] { modId });
        return results.Single();
    }

    public async Task<IReadOnlyList<LauncherBuildResult>> BuildModsAsync(IEnumerable<string> modIds)
    {
        var selectors = modIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => $"mod:{id}")
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(selectors, null, LauncherBuildMode.Always, config, BuildCatalog(config), LoadPresets());
        return await BuildPlannedModsAsync(resolveResult.Plan, config, CancellationToken.None);
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
                var install = await RunNodePackageCommandAsync("ci", profile.ClientProjectDirectory, timeoutMs: 300_000);
                output.AppendLine(install.Output);
                if (install.ExitCode != 0)
                {
                    return new LauncherBuildResult(platformId, false, install.ExitCode, output.ToString());
                }
            }

            var clientBuild = await RunNodePackageCommandAsync("run build", profile.ClientProjectDirectory, timeoutMs: 300_000);
            output.AppendLine(clientBuild.Output);
            if (clientBuild.ExitCode != 0)
            {
                return new LauncherBuildResult(platformId, false, clientBuild.ExitCode, output.ToString());
            }
        }

        var dotnetBuild = await RunProcessAsync(
            "dotnet",
            $"build \"{profile.AppProjectPath}\" -c Release",
            _repoRoot,
            timeoutMs: 300_000);
        output.AppendLine(dotnetBuild.Output);
        return new LauncherBuildResult(platformId, dotnetBuild.ExitCode == 0, dotnetBuild.ExitCode, output.ToString());
    }

    public string WriteGameJson(string platformId, IEnumerable<string> modIds)
    {
        var selectors = modIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => $"mod:{id}")
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(selectors, platformId, LauncherBuildMode.Never, config, BuildCatalog(config), LoadPresets());
        return WriteRuntimeBootstrap(resolveResult.Plan);
    }

    public async Task<LauncherLaunchResult> LaunchAsync(string platformId, IEnumerable<string> modIds)
    {
        var selectors = modIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => $"mod:{id}")
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(selectors, platformId, LauncherBuildMode.Auto, config, BuildCatalog(config), LoadPresets());
        var buildResults = await BuildPlannedModsAsync(resolveResult.Plan, config, CancellationToken.None);
        var failedModBuild = buildResults.FirstOrDefault(result => !result.Ok);
        if (failedModBuild != null)
        {
            return new LauncherLaunchResult(false, failedModBuild.Output, -1, string.Empty, string.Empty, resolveResult.Plan);
        }

        var appBuild = await BuildAppAsync(resolveResult.Plan.AdapterId);
        if (!appBuild.Ok)
        {
            return new LauncherLaunchResult(false, appBuild.Output, -1, string.Empty, string.Empty, resolveResult.Plan);
        }

        var bootstrapPath = WriteRuntimeBootstrap(resolveResult.Plan);
        var startInfo = new ProcessStartInfo("dotnet", $"\"{resolveResult.Plan.AppAssemblyPath}\" \"{bootstrapPath}\"")
        {
            WorkingDirectory = resolveResult.Plan.AppOutputDirectory,
            UseShellExecute = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            return new LauncherLaunchResult(false, "Failed to start platform process.", -1, string.Empty, bootstrapPath, resolveResult.Plan);
        }

        return new LauncherLaunchResult(true, string.Empty, process.Id, resolveResult.Plan.LaunchUrl, bootstrapPath, resolveResult.Plan);
    }

    public LauncherResolveResult Resolve(IEnumerable<string> selectors, string? adapterId = null, LauncherBuildMode buildMode = LauncherBuildMode.Auto)
    {
        var config = LoadConfig();
        var catalog = BuildCatalog(config);
        return ResolvePlan(selectors.Where(selector => !string.IsNullOrWhiteSpace(selector)).ToList(), adapterId, buildMode, config, catalog, LoadPresets());
    }

    public Task<string> ExportSdkAsync(CancellationToken ct = default)
    {
        return LauncherModSdkExporter.ExportAsync(_repoRoot, ct);
    }

    public async Task<string> GenerateSolutionAsync(string modId)
    {
        var config = LoadConfig();
        var catalog = BuildCatalog(config);
        var entry = ResolveUniqueModEntry(modId, catalog.ById);
        var solutionPath = Path.Combine(entry.Info.RootPath, $"{entry.Info.Id}.sln");

        var create = await RunProcessAsync("dotnet", $"new sln -n {entry.Info.Id} --force", entry.Info.RootPath, timeoutMs: 30_000);
        if (create.ExitCode != 0)
        {
            throw new InvalidOperationException(create.Output);
        }

        var projectPath = EnsureProjectFile(entry, config);
        if (File.Exists(projectPath))
        {
            await RunProcessAsync("dotnet", $"sln \"{solutionPath}\" add \"{projectPath}\"", entry.Info.RootPath, timeoutMs: 30_000);
        }

        foreach (var dependencyId in entry.Manifest.Dependencies.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var dependency = ResolveUniqueModEntry(dependencyId, catalog.ById);
            var dependencyProjectPath = ResolveBuildProjectPath(config, dependency.Info.RootPath, dependency.Info.Id, dependency.Info.ProjectPath);
            if (!string.IsNullOrWhiteSpace(dependencyProjectPath) && File.Exists(dependencyProjectPath))
            {
                await RunProcessAsync("dotnet", $"sln \"{solutionPath}\" add \"{dependencyProjectPath}\"", entry.Info.RootPath, timeoutMs: 30_000);
            }
        }

        var coreProjectPath = Path.Combine(_repoRoot, "src", "Core", "Ludots.Core.csproj");
        if (File.Exists(coreProjectPath))
        {
            await RunProcessAsync("dotnet", $"sln \"{solutionPath}\" add \"{coreProjectPath}\"", entry.Info.RootPath, timeoutMs: 30_000);
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

    private LauncherConfig LoadConfig() => _configService.LoadMergedConfig();
    private LauncherConfig LoadRepoConfig() => _configService.LoadRepoConfig();
    private LauncherPresetDocument LoadPresets() => _configService.LoadPresets();
    private LauncherPreferences LoadPreferences() => _configService.LoadPreferences();
    private void SaveRepoConfig(LauncherConfig config) => _configService.SaveRepoConfig(config);
    private void SavePresets(LauncherPresetDocument presets) => _configService.SavePresets(presets);
    private void SavePreferences(LauncherPreferences preferences) => _configService.SavePreferences(preferences);

    private LauncherResolveResult ResolvePlan(
        IReadOnlyList<string> selectors,
        string? adapterId,
        LauncherBuildMode buildMode,
        LauncherConfig config,
        CatalogIndex catalog,
        LauncherPresetDocument presetDocument)
    {
        if (selectors.Count == 0)
        {
            throw new InvalidOperationException("At least one selector is required.");
        }

        var localByRootPath = new Dictionary<string, CatalogEntry>(catalog.ByRootPath, StringComparer.OrdinalIgnoreCase);
        var localById = catalog.ById.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
        var presetStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<CatalogEntry>();

        foreach (var selector in selectors)
        {
            roots.AddRange(ResolveSelector(selector, config, presetDocument, catalog, localByRootPath, localById, presetStack));
        }

        var ordered = ResolveDependencyClosure(roots, localById);
        var resolvedAdapterId = string.IsNullOrWhiteSpace(adapterId)
            ? ResolveSelectedAdapterId(config, LoadPreferences())
            : adapterId!.Trim().ToLowerInvariant();
        var profile = GetPlatformProfile(resolvedAdapterId);
        var plannedMods = ordered
            .Select(entry => new LauncherPlannedMod(
                entry.Info.Id,
                entry.Info.RootPath,
                entry.Info.ProjectPath,
                entry.Info.MainAssemblyPath,
                entry.Info.Kind,
                entry.Info.BuildState,
                entry.Info.BindingNames))
            .ToList();

        var plan = new LauncherLaunchPlan(
            profile.Id,
            buildMode.ToString().ToLowerInvariant(),
            selectors,
            roots.Select(entry => entry.Info.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ordered.Select(entry => entry.Info.Id).ToList(),
            plannedMods,
            "file",
            Path.Combine(profile.OutputDirectory, profile.RuntimeBootstrapFileName),
            profile.OutputDirectory,
            ResolveAppAssemblyPath(profile),
            profile.LaunchUrl);

        return new LauncherResolveResult(plan, catalog.Entries.Select(entry => entry.Info).ToList());
    }

    private IReadOnlyList<CatalogEntry> ResolveSelector(
        string selector,
        LauncherConfig config,
        LauncherPresetDocument presetDocument,
        CatalogIndex catalog,
        Dictionary<string, CatalogEntry> localByRootPath,
        Dictionary<string, List<CatalogEntry>> localById,
        HashSet<string> presetStack)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return Array.Empty<CatalogEntry>();
        }

        if (selector.StartsWith('$'))
        {
            var alias = selector[1..];
            if (!catalog.BindingsByName.TryGetValue(alias, out var binding))
            {
                throw new InvalidOperationException($"Binding not found: {selector}");
            }

            return ResolveBinding(binding, config, catalog, localByRootPath, localById, presetDocument, presetStack);
        }

        if (selector.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
        {
            var presetId = selector["preset:".Length..];
            if (!presetStack.Add(presetId))
            {
                throw new InvalidOperationException($"Preset cycle detected at '{presetId}'.");
            }

            try
            {
                var preset = presetDocument.Presets.FirstOrDefault(item => string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
                if (preset == null)
                {
                    throw new InvalidOperationException($"Preset not found: {presetId}");
                }

                var resolved = new List<CatalogEntry>();
                foreach (var nestedSelector in preset.Selectors)
                {
                    resolved.AddRange(ResolveSelector(nestedSelector, config, presetDocument, catalog, localByRootPath, localById, presetStack));
                }

                return resolved;
            }
            finally
            {
                presetStack.Remove(presetId);
            }
        }

        if (selector.StartsWith("mod:", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { ResolveUniqueModEntry(selector["mod:".Length..], localById) };
        }

        if (selector.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = LauncherWorkspaceSourceResolver.ResolvePath(_repoRoot, selector["path:".Length..]);
            if (localByRootPath.TryGetValue(fullPath, out var existing))
            {
                return new[] { existing };
            }

            var manifestPath = Path.Combine(fullPath, "mod.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException($"Mod path not found: {fullPath}");
            }

            var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
            var created = CreateCatalogEntry(config, localByRootPath.Keys.ToList(), fullPath, manifest);
            localByRootPath[fullPath] = created;
            if (!localById.TryGetValue(created.Info.Id, out var matches))
            {
                matches = new List<CatalogEntry>();
                localById[created.Info.Id] = matches;
            }

            matches.Add(created);
            return new[] { created };
        }

        return new[] { ResolveUniqueModEntry(selector, localById) };
    }

    private IReadOnlyList<CatalogEntry> ResolveBinding(
        LauncherBinding binding,
        LauncherConfig config,
        CatalogIndex catalog,
        Dictionary<string, CatalogEntry> localByRootPath,
        Dictionary<string, List<CatalogEntry>> localById,
        LauncherPresetDocument presetDocument,
        HashSet<string> presetStack)
    {
        return binding.Target.Type.Trim().ToLowerInvariant() switch
        {
            "path" => ResolveSelector($"path:{binding.Target.Value}", config, presetDocument, catalog, localByRootPath, localById, presetStack),
            "modid" => ResolveSelector($"mod:{binding.Target.Value}", config, presetDocument, catalog, localByRootPath, localById, presetStack),
            _ => throw new InvalidOperationException($"Unsupported binding target type: {binding.Target.Type}")
        };
    }

    private static CatalogEntry ResolveUniqueModEntry(string modId, IReadOnlyDictionary<string, List<CatalogEntry>> byId)
    {
        if (!byId.TryGetValue(modId, out var matches) || matches.Count == 0)
        {
            throw new InvalidOperationException($"Mod not found: {modId}");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"Ambiguous mod selector '{modId}'. Use a binding or path selector.");
        }

        return matches[0];
    }

    private static IReadOnlyList<CatalogEntry> ResolveDependencyClosure(
        IEnumerable<CatalogEntry> roots,
        IReadOnlyDictionary<string, List<CatalogEntry>> byId)
    {
        var order = new List<CatalogEntry>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(CatalogEntry entry)
        {
            var visitKey = entry.Info.RootPath;
            if (visited.Contains(visitKey))
            {
                return;
            }

            if (!visiting.Add(visitKey))
            {
                throw new InvalidOperationException($"Dependency cycle detected at '{entry.Info.Id}'.");
            }

            foreach (var dependencyId in entry.Manifest.Dependencies.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (!byId.TryGetValue(dependencyId, out var matches) || matches.Count == 0)
                {
                    throw new InvalidOperationException($"Missing dependency '{dependencyId}' required by '{entry.Info.Id}'.");
                }

                if (matches.Count > 1)
                {
                    throw new InvalidOperationException($"Ambiguous dependency '{dependencyId}' required by '{entry.Info.Id}'.");
                }

                Visit(matches[0]);
            }

            visiting.Remove(visitKey);
            visited.Add(visitKey);
            order.Add(entry);
        }

        foreach (var root in roots.GroupBy(entry => entry.Info.RootPath, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            Visit(root);
        }

        return order;
    }

    private string ResolveSelectedAdapterId(LauncherConfig config, LauncherPreferences preferences)
    {
        var preferred = string.IsNullOrWhiteSpace(preferences.LastAdapterId) ? config.Adapters.Default : preferences.LastAdapterId;
        return GetPlatformProfiles().Any(profile => string.Equals(profile.Id, preferred, StringComparison.OrdinalIgnoreCase))
            ? preferred!
            : LauncherPlatformIds.Raylib;
    }

    private static string? ResolveSelectedPresetId(LauncherPreferences preferences, LauncherPresetDocument presets)
    {
        if (!string.IsNullOrWhiteSpace(preferences.LastPresetId) &&
            presets.Presets.Any(item => string.Equals(item.Id, preferences.LastPresetId, StringComparison.OrdinalIgnoreCase)))
        {
            return preferences.LastPresetId;
        }

        return presets.Presets
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Id)
            .FirstOrDefault();
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
                string.Empty,
                "launcher.runtime.json"),
            new LauncherPlatformProfile(
                LauncherPlatformIds.Web,
                "Web",
                Path.Combine(_repoRoot, "src", "Apps", "Web", "Ludots.App.Web", "Ludots.App.Web.csproj"),
                Path.Combine(_repoRoot, "src", "Apps", "Web", "Ludots.App.Web", "bin", "Release", "net8.0"),
                Path.Combine(_repoRoot, "src", "Client", "Web"),
                Path.Combine(_repoRoot, "src", "Client", "Web", "dist"),
                "http://localhost:5200",
                "launcher.runtime.json")
        };
    }

    private LauncherPlatformProfile GetPlatformProfile(string platformId)
    {
        var match = GetPlatformProfiles().FirstOrDefault(profile => string.Equals(profile.Id, platformId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            throw new InvalidOperationException($"Unknown adapter: {platformId}");
        }

        return match;
    }

    private static string ResolveAppAssemblyPath(LauncherPlatformProfile profile)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(profile.AppProjectPath) + ".dll";
        return Path.Combine(profile.OutputDirectory, assemblyName);
    }

    private CatalogIndex BuildCatalog(LauncherConfig config)
    {
        var sources = LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, config);
        var discovered = ModDiscovery.DiscoverMods(sources);
        var entries = new List<CatalogEntry>(discovered.Count);
        foreach (var mod in discovered)
        {
            entries.Add(CreateCatalogEntry(config, sources, mod.DirectoryPath, mod.Manifest));
        }

        var byId = entries
            .GroupBy(entry => entry.Info.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var bindingMap = BuildBindingMap(config, byId);
        var finalizedEntries = new List<CatalogEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var bindingNames = bindingMap.TryGetValue(entry.Info.RootPath, out var names)
                ? names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
            var isAmbiguous = byId.TryGetValue(entry.Info.Id, out var matches) && matches.Count > 1;
            finalizedEntries.Add(new CatalogEntry(
                CloneModInfo(entry.Info, bindingNames, isAmbiguous),
                entry.Manifest));
        }

        return new CatalogIndex(
            finalizedEntries
                .OrderBy(entry => entry.Info.Priority)
                .ThenBy(entry => entry.Info.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Info.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            finalizedEntries.ToDictionary(entry => entry.Info.RootPath, StringComparer.OrdinalIgnoreCase),
            finalizedEntries.GroupBy(entry => entry.Info.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase),
            config.Bindings.ToDictionary(binding => binding.Name, StringComparer.OrdinalIgnoreCase));
    }

    private LauncherModInfo CloneModInfo(LauncherModInfo source, IReadOnlyList<string> bindingNames, bool isAmbiguous)
    {
        return new LauncherModInfo
        {
            Id = source.Id,
            Name = source.Name,
            Version = source.Version,
            Priority = source.Priority,
            Dependencies = new Dictionary<string, string>(source.Dependencies, StringComparer.OrdinalIgnoreCase),
            RootPath = source.RootPath,
            RelativePath = source.RelativePath,
            LayerPath = source.LayerPath,
            Description = source.Description,
            Author = source.Author,
            Tags = source.Tags.ToList(),
            ChangelogFile = source.ChangelogFile,
            HasThumbnail = source.HasThumbnail,
            HasReadme = source.HasReadme,
            MainAssemblyPath = source.MainAssemblyPath,
            ProjectPath = source.ProjectPath,
            HasProject = source.HasProject,
            BuildState = source.BuildState,
            LastBuildMessage = source.LastBuildMessage,
            Kind = source.Kind,
            BindingNames = bindingNames.ToList(),
            IsAmbiguous = isAmbiguous
        };
    }

    private CatalogEntry CreateCatalogEntry(LauncherConfig config, IReadOnlyList<string> sources, string rootPath, ModManifest manifest)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var relativePath = GetPortablePath(fullRootPath);
        var sourceRoot = ResolveSourceRoot(fullRootPath, sources);
        var layerPath = ResolveLayerPath(fullRootPath, sourceRoot);
        var projectPath = ResolveBuildProjectPath(config, fullRootPath, manifest.Name, preferredProjectPath: string.Empty);
        var mainAssemblyPath = ResolveMainAssemblyPath(fullRootPath, manifest.Main);
        var kind = ResolveModKind(fullRootPath, manifest.Main, projectPath, mainAssemblyPath);
        var (buildState, lastBuildMessage) = ResolveBuildState(fullRootPath, manifest.Main, projectPath, kind, mainAssemblyPath);

        return new CatalogEntry(
            new LauncherModInfo
            {
                Id = manifest.Name,
                Name = manifest.Name,
                Version = manifest.Version,
                Priority = manifest.Priority,
                Dependencies = new Dictionary<string, string>(manifest.Dependencies, StringComparer.OrdinalIgnoreCase),
                RootPath = fullRootPath,
                RelativePath = relativePath,
                LayerPath = layerPath,
                Description = manifest.Description ?? string.Empty,
                Author = manifest.Author ?? string.Empty,
                Tags = manifest.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList() ?? new List<string>(),
                ChangelogFile = manifest.Changelog ?? string.Empty,
                HasThumbnail = HasLauncherThumbnail(fullRootPath),
                HasReadme = File.Exists(Path.Combine(fullRootPath, "README.md")),
                MainAssemblyPath = mainAssemblyPath,
                ProjectPath = projectPath ?? string.Empty,
                HasProject = !string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath),
                BuildState = buildState,
                LastBuildMessage = lastBuildMessage,
                Kind = kind
            },
            manifest);
    }

    private Dictionary<string, List<string>> BuildBindingMap(
        LauncherConfig config,
        IReadOnlyDictionary<string, List<CatalogEntry>> byId)
    {
        var bindingMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in config.Bindings)
        {
            if (binding?.Target == null || string.IsNullOrWhiteSpace(binding.Name))
            {
                continue;
            }

            switch (binding.Target.Type.Trim().ToLowerInvariant())
            {
                case "path":
                {
                    var fullPath = LauncherWorkspaceSourceResolver.ResolvePath(_repoRoot, binding.Target.Value);
                    if (!bindingMap.TryGetValue(fullPath, out var names))
                    {
                        names = new List<string>();
                        bindingMap[fullPath] = names;
                    }

                    names.Add(binding.Name);
                    break;
                }
                case "modid":
                {
                    if (!byId.TryGetValue(binding.Target.Value, out var matches) || matches.Count != 1)
                    {
                        break;
                    }

                    var rootPath = matches[0].Info.RootPath;
                    if (!bindingMap.TryGetValue(rootPath, out var names))
                    {
                        names = new List<string>();
                        bindingMap[rootPath] = names;
                    }

                    names.Add(binding.Name);
                    break;
                }
            }
        }

        return bindingMap;
    }

    private IReadOnlyList<LauncherPreset> BuildPresetViews(LauncherPresetDocument presetDocument, CatalogIndex catalog)
    {
        var config = LoadConfig();
        var output = new List<LauncherPreset>(presetDocument.Presets.Count);
        foreach (var preset in presetDocument.Presets.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var activeModIds = new List<string>();
            try
            {
                var resolved = ResolvePlan(
                    new[] { $"preset:{preset.Id}" },
                    preset.AdapterId,
                    ParseBuildMode(preset.BuildMode),
                    config,
                    catalog,
                    presetDocument);
                activeModIds.AddRange(resolved.Plan.OrderedModIds);
            }
            catch
            {
            }

            output.Add(new LauncherPreset
            {
                Id = preset.Id,
                Name = preset.Name,
                Selectors = preset.Selectors.ToList(),
                AdapterId = string.IsNullOrWhiteSpace(preset.AdapterId) ? ResolveSelectedAdapterId(config, LoadPreferences()) : preset.AdapterId!,
                BuildMode = NormalizeBuildMode(preset.BuildMode),
                ActiveModIds = activeModIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                IncludeDependencies = true
            });
        }

        return output;
    }

    private async Task<IReadOnlyList<LauncherBuildResult>> BuildPlannedModsAsync(
        LauncherLaunchPlan plan,
        LauncherConfig config,
        CancellationToken ct)
    {
        var catalog = BuildCatalog(config);
        var plannedEntries = plan.Mods
            .Select(mod => catalog.ByRootPath.TryGetValue(mod.RootPath, out var entry)
                ? entry
                : throw new InvalidOperationException($"Catalog entry not found for {mod.RootPath}"))
            .ToList();

        if (plannedEntries.Any(entry => entry.Info.Kind == LauncherModKind.BuildableSource))
        {
            await ExportSdkAsync(ct);
        }

        var results = new List<LauncherBuildResult>(plannedEntries.Count);
        foreach (var entry in plannedEntries)
        {
            results.Add(await BuildPlannedModAsync(entry, config, plan));
        }

        return results;
    }

    private async Task<LauncherBuildResult> BuildPlannedModAsync(
        CatalogEntry entry,
        LauncherConfig config,
        LauncherLaunchPlan plan)
    {
        if (entry.Info.Kind == LauncherModKind.ResourceOnly)
        {
            return new LauncherBuildResult(entry.Info.Id, true, 0, "Resource-only mod requires no build.");
        }

        if (entry.Info.Kind == LauncherModKind.BinaryOnly)
        {
            if (File.Exists(entry.Info.MainAssemblyPath))
            {
                return new LauncherBuildResult(entry.Info.Id, true, 0, "Binary-only mod already has a main assembly.");
            }

            return new LauncherBuildResult(entry.Info.Id, false, 1, $"Missing main assembly for {entry.Info.Id}: {entry.Info.MainAssemblyPath}");
        }

        if (plan.BuildMode == LauncherBuildMode.Never.ToString().ToLowerInvariant() &&
            entry.Info.BuildState == LauncherBuildState.Succeeded)
        {
            return new LauncherBuildResult(entry.Info.Id, true, 0, "Build skipped by request.");
        }

        var projectPath = EnsureProjectFile(entry, config);
        var output = new StringBuilder();
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? entry.Info.RootPath;
        var build = await RunProcessAsync(
            "dotnet",
            $"build \"{projectPath}\" /p:ProduceReferenceAssembly=true -c Release",
            projectDirectory,
            timeoutMs: 300_000);
        output.AppendLine(build.Output);
        if (build.ExitCode != 0)
        {
            return new LauncherBuildResult(entry.Info.Id, false, build.ExitCode, output.ToString());
        }

        var referenceExportPath = ExportReferenceAssembly(entry.Info, projectDirectory);
        output.AppendLine($"Exported ref: {referenceExportPath}");

        var graphCompile = await RunProcessAsync(
            "dotnet",
            $"run --project \"{Path.Combine(_repoRoot, "src", "Tools", "Ludots.Tool", "Ludots.Tool.csproj")}\" -- graph compile --modPath \"{entry.Info.RootPath}\" --assetsRoot \"{_repoRoot}\"",
            _repoRoot,
            timeoutMs: 300_000);
        output.AppendLine(graphCompile.Output);
        if (graphCompile.ExitCode != 0)
        {
            return new LauncherBuildResult(entry.Info.Id, false, graphCompile.ExitCode, output.ToString());
        }

        var mainAssemblyPath = ResolveMainAssemblyPath(entry.Info.RootPath, entry.Manifest.Main);
        if (!string.IsNullOrWhiteSpace(entry.Manifest.Main) && !File.Exists(mainAssemblyPath))
        {
            return new LauncherBuildResult(entry.Info.Id, false, 1, $"Main assembly missing after build: {entry.Manifest.Main}");
        }

        return new LauncherBuildResult(entry.Info.Id, true, 0, output.ToString());
    }

    private string EnsureProjectFile(CatalogEntry entry, LauncherConfig config)
    {
        var existingProjectPath = ResolveBuildProjectPath(config, entry.Info.RootPath, entry.Info.Id, entry.Info.ProjectPath);
        if (!string.IsNullOrWhiteSpace(existingProjectPath) && File.Exists(existingProjectPath))
        {
            return existingProjectPath;
        }

        var sdkPropsPath = Path.Combine(_repoRoot, "assets", "ModSdk", "ModSdk.props");
        if (!File.Exists(sdkPropsPath))
        {
            throw new FileNotFoundException($"Mod SDK props not found: {sdkPropsPath}");
        }

        var projectPath = Path.Combine(entry.Info.RootPath, $"{entry.Info.Name}.csproj");
        var relativeSdkPropsPath = Path.GetRelativePath(entry.Info.RootPath, sdkPropsPath);
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

    private string WriteRuntimeBootstrap(LauncherLaunchPlan plan)
    {
        Directory.CreateDirectory(plan.AppOutputDirectory);
        var modPaths = plan.Mods
            .Select(mod => Path.GetRelativePath(plan.AppOutputDirectory, mod.RootPath).Replace('\\', '/'))
            .ToArray();
        var json = JsonSerializer.Serialize(new { ModPaths = modPaths }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(plan.BootstrapArtifactPath, json);
        return plan.BootstrapArtifactPath;
    }

    private string? ResolveBuildProjectPath(LauncherConfig config, string rootPath, string modId, string preferredProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredProjectPath))
        {
            var resolvedPreferred = ResolveProjectPath(rootPath, preferredProjectPath);
            if (File.Exists(resolvedPreferred))
            {
                return resolvedPreferred;
            }
        }

        var bindingHint = config.Bindings
            .FirstOrDefault(binding =>
                string.Equals(binding.Target.Type, "path", StringComparison.OrdinalIgnoreCase) &&
                PathsEqual(LauncherWorkspaceSourceResolver.ResolvePath(_repoRoot, binding.Target.Value), rootPath) &&
                !string.IsNullOrWhiteSpace(binding.Target.ProjectPath));
        if (!string.IsNullOrWhiteSpace(bindingHint?.Target.ProjectPath))
        {
            var resolvedBindingProject = ResolveProjectPath(rootPath, bindingHint.Target.ProjectPath!);
            if (File.Exists(resolvedBindingProject))
            {
                return resolvedBindingProject;
            }
        }

        var projectHint = config.ProjectHints.FirstOrDefault(hint =>
            (!string.IsNullOrWhiteSpace(hint.ModId) && string.Equals(hint.ModId, modId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(hint.RootPath) && PathsEqual(LauncherWorkspaceSourceResolver.ResolvePath(_repoRoot, hint.RootPath!), rootPath)));
        if (!string.IsNullOrWhiteSpace(projectHint?.ProjectPath))
        {
            var resolvedProjectHint = ResolveProjectPath(rootPath, projectHint.ProjectPath);
            if (File.Exists(resolvedProjectHint))
            {
                return resolvedProjectHint;
            }
        }

        return Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string ResolveProjectPath(string modRootPath, string projectPath)
    {
        if (Path.IsPathRooted(projectPath))
        {
            return Path.GetFullPath(projectPath);
        }

        return Path.GetFullPath(Path.Combine(modRootPath, projectPath));
    }

    private static LauncherModKind ResolveModKind(string rootPath, string? manifestMain, string? projectPath, string mainAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(manifestMain))
        {
            return LauncherModKind.ResourceOnly;
        }

        if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
        {
            return LauncherModKind.BuildableSource;
        }

        return HasUserSourceFiles(rootPath) ? LauncherModKind.BuildableSource : LauncherModKind.BinaryOnly;
    }

    private static (LauncherBuildState State, string Message) ResolveBuildState(
        string rootPath,
        string? manifestMain,
        string? projectPath,
        LauncherModKind kind,
        string mainAssemblyPath)
    {
        return kind switch
        {
            LauncherModKind.ResourceOnly => (LauncherBuildState.Succeeded, "Resource only"),
            LauncherModKind.BinaryOnly => File.Exists(mainAssemblyPath)
                ? (LauncherBuildState.Succeeded, "Binary ready")
                : (LauncherBuildState.Failed, "Missing main assembly"),
            LauncherModKind.BuildableSource => ResolveProjectBuildState(rootPath, manifestMain, projectPath, mainAssemblyPath),
            _ => (LauncherBuildState.Failed, "Unknown build state")
        };
    }

    private static (LauncherBuildState State, string Message) ResolveProjectBuildState(
        string rootPath,
        string? manifestMain,
        string? projectPath,
        string mainAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return (LauncherBuildState.NoProject, "Project missing");
        }

        if (string.IsNullOrWhiteSpace(manifestMain))
        {
            return (LauncherBuildState.Failed, "Invalid main");
        }

        if (!File.Exists(mainAssemblyPath))
        {
            return (LauncherBuildState.Idle, "Not built");
        }

        var assemblyWriteUtc = File.GetLastWriteTimeUtc(mainAssemblyPath);
        if (GetLatestSourceWriteUtc(rootPath) > assemblyWriteUtc)
        {
            return (LauncherBuildState.Outdated, "Outdated");
        }

        return (LauncherBuildState.Succeeded, "OK");
    }

    private static string ResolveMainAssemblyPath(string rootPath, string? manifestMain)
    {
        if (string.IsNullOrWhiteSpace(manifestMain))
        {
            return string.Empty;
        }

        return Path.GetFullPath(Path.Combine(rootPath, manifestMain.Replace('/', Path.DirectorySeparatorChar)));
    }

    private string ResolveSourceRoot(string rootPath, IReadOnlyList<string> sources)
    {
        return sources
            .Where(source => rootPath.StartsWith(source, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(source => source.Length)
            .FirstOrDefault() ?? _repoRoot;
    }

    private static string ResolveLayerPath(string rootPath, string sourceRoot)
    {
        var relative = Path.GetRelativePath(sourceRoot, rootPath).Replace('\\', '/');
        var lastSlash = relative.LastIndexOf('/');
        return lastSlash <= 0 ? "root" : relative[..lastSlash];
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

    private static bool HasUserSourceFiles(string rootPath)
    {
        foreach (var sourceFilePath in Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories))
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

    private string GetPortablePath(string fullPath)
    {
        if (fullPath.StartsWith(_repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(_repoRoot, fullPath).Replace('\\', '/');
        }

        return fullPath.Replace('\\', '/');
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStableId(string prefix, string raw)
    {
        var cleaned = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = Guid.NewGuid().ToString("N");
        }

        return $"{prefix}_{cleaned}".ToLowerInvariant();
    }

    private static string NormalizeBuildMode(string? buildMode)
    {
        return ParseBuildMode(buildMode).ToString().ToLowerInvariant();
    }

    private static LauncherBuildMode ParseBuildMode(string? buildMode)
    {
        return Enum.TryParse<LauncherBuildMode>(buildMode, true, out var parsed)
            ? parsed
            : LauncherBuildMode.Auto;
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

    private sealed record CatalogEntry(LauncherModInfo Info, ModManifest Manifest);
    private sealed record CatalogIndex(
        IReadOnlyList<CatalogEntry> Entries,
        IReadOnlyDictionary<string, CatalogEntry> ByRootPath,
        IReadOnlyDictionary<string, List<CatalogEntry>> ById,
        IReadOnlyDictionary<string, LauncherBinding> BindingsByName);
}
