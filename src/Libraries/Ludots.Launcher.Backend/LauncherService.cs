using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;

namespace Ludots.Launcher.Backend;

public sealed class LauncherService
{
    private const int LaunchGraphSchemaVersion = 1;
    private const string RuntimeTargetFramework = "net9.0";
    private static readonly JsonSerializerOptions BootstrapJsonWriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions GraphJsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record ActiveLaunchProcessRecord(
        int Pid,
        long StartedAtUtcTicks,
        string AdapterId,
        string AppAssemblyPath,
        string BootstrapPath);

    private readonly string _repoRoot;
    private readonly LauncherConfigService _configService;

    public string RepoRoot => _repoRoot;

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
                .Select(binding => new LauncherBindingInfo(binding.Name, binding.Target.Type, binding.Target.Value, binding.Target.ProjectPath))
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
        var existing = config.Bindings.Find(binding => string.Equals(binding.Name, name, StringComparison.OrdinalIgnoreCase));
        config.Bindings.RemoveAll(binding => string.Equals(binding.Name, name, StringComparison.OrdinalIgnoreCase));
        config.Bindings.Add(new LauncherBinding
        {
            Name = name.Trim(),
            Target = new LauncherBindingTarget
            {
                Type = targetType.Trim(),
                Value = targetValue.Trim(),
                ProjectPath = string.IsNullOrWhiteSpace(projectPath) ? null : projectPath.Trim(),
                Args = existing?.Target.Args
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
        var selectors = activeModIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => $"mod:{id}");
        return SavePresetSelectors(presetId, name, selectors, adapterId: null, LauncherBuildMode.Auto, selectAfterSave);
    }

    public LauncherPreset SavePresetSelectors(
        string? presetId,
        string name,
        IEnumerable<string> selectors,
        string? adapterId,
        LauncherBuildMode buildMode,
        bool selectAfterSave)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preset name is required.", nameof(name));
        }

        var presetDocument = LoadPresets();
        var resolvedPresetId = string.IsNullOrWhiteSpace(presetId) ? CreateStableId("preset", name) : presetId.Trim();
        var selectorList = selectors
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .Select(selector => selector.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(selector => selector, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectorList.Count == 0)
        {
            throw new InvalidOperationException("At least one selector is required to save a preset.");
        }

        presetDocument.Presets.RemoveAll(item => string.Equals(item.Id, resolvedPresetId, StringComparison.OrdinalIgnoreCase));
        presetDocument.Presets.Add(new LauncherPresetDefinition
        {
            Id = resolvedPresetId,
            Name = name.Trim(),
            Selectors = selectorList,
            AdapterId = string.IsNullOrWhiteSpace(adapterId) ? null : adapterId.Trim().ToLowerInvariant(),
            BuildMode = buildMode.ToString().ToLowerInvariant()
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
        return await BuildAsync(selectors, null, LauncherBuildMode.Always, CancellationToken.None);
    }

    public async Task<IReadOnlyList<LauncherBuildResult>> BuildAsync(
        IEnumerable<string> selectors,
        string? adapterId = null,
        LauncherBuildMode buildMode = LauncherBuildMode.Always,
        CancellationToken ct = default,
        string? browserProviderOverride = null)
    {
        var resolvedSelectors = selectors
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(
            resolvedSelectors,
            adapterId,
            buildMode,
            config,
            BuildCatalog(config),
            LoadPresets(),
            browserProviderOverride);
        WriteLaunchGraphDocument(resolveResult.Plan);
        return await BuildPlanRuntimeAsync(resolveResult.Plan, config, ct);
    }

    /// <summary>
    /// 与 BuildExecutableTargetAsync 的 Never 语义对齐：预构建布局（玩家发行包）跳过 app 编译，
    /// 避免玩家机需要 .NET SDK。
    /// </summary>
    private static bool ShouldSkipAppBuild(LauncherLaunchPlan plan)
    {
        return plan.BuildMode == LauncherBuildMode.Never.ToString().ToLowerInvariant() &&
               !string.IsNullOrWhiteSpace(plan.AppAssemblyPath) &&
               File.Exists(plan.AppAssemblyPath);
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

        var dotnetBuild = await RunDotnetAsync(
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
        return WriteBootstrap(selectors, platformId);
    }

    public string WriteLaunchGraph(
        IEnumerable<string> selectors,
        string? adapterId = null,
        LauncherBuildMode buildMode = LauncherBuildMode.Never)
    {
        var resolvedSelectors = selectors
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(resolvedSelectors, adapterId, buildMode, config, BuildCatalog(config), LoadPresets());
        return WriteLaunchGraphDocument(resolveResult.Plan);
    }

    public async Task<LauncherLaunchResult> LaunchAsync(string platformId, IEnumerable<string> modIds)
    {
        var selectors = modIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => $"mod:{id}")
            .ToList();
        return await LaunchAsync(selectors, platformId, LauncherBuildMode.Auto);
    }

    public string WriteBootstrap(
        IEnumerable<string> selectors,
        string? adapterId = null,
        LauncherBuildMode buildMode = LauncherBuildMode.Never)
    {
        var resolvedSelectors = selectors
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(resolvedSelectors, adapterId, buildMode, config, BuildCatalog(config), LoadPresets());
        return WriteBootstrap(resolveResult.Plan);
    }

    public string WriteBootstrap(LauncherLaunchPlan plan)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (plan.IsExecutableTarget)
        {
            throw new InvalidOperationException("Executable target plans do not write a runtime bootstrap.");
        }

        return WriteRuntimeBootstrap(plan);
    }

    /// <param name="buildApp">
    /// false = 跳过平台 app 构建（进程内 shell 会话必须如此：运行中的进程锁着自己的 bin，
    /// 自建必然 MSB3027 死锁，且新程序集也要经会话中继才生效）。
    /// </param>
    public async Task<LauncherPrepareResult> PrepareLaunchAsync(
        IEnumerable<string> selectors,
        string? adapterId = null,
        LauncherBuildMode buildMode = LauncherBuildMode.Auto,
        bool buildApp = true)
    {
        var resolvedSelectors = selectors
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .ToList();
        var config = LoadConfig();
        var resolveResult = ResolvePlan(resolvedSelectors, adapterId, buildMode, config, BuildCatalog(config), LoadPresets());
        if (resolveResult.Plan.IsExecutableTarget)
        {
            return new LauncherPrepareResult(
                false,
                "Executable targets run in an external process; the in-app shell only prepares mod plans.",
                string.Empty,
                null);
        }

        var buildResults = await BuildPlanRuntimeAsync(resolveResult.Plan, config, CancellationToken.None);
        var failedModBuild = buildResults.FirstOrDefault(result => !result.Ok);
        if (failedModBuild != null)
        {
            return new LauncherPrepareResult(false, failedModBuild.Output, string.Empty, resolveResult.Plan);
        }

        if (buildApp)
        {
            var appBuild = await BuildAppAsync(resolveResult.Plan.AdapterId);
            if (!appBuild.Ok)
            {
                return new LauncherPrepareResult(false, appBuild.Output, string.Empty, resolveResult.Plan);
            }
        }

        var bootstrapPath = WriteRuntimeBootstrap(resolveResult.Plan);
        return new LauncherPrepareResult(true, string.Empty, bootstrapPath, resolveResult.Plan);
    }

    public async Task<LauncherLaunchResult> LaunchAsync(
        IEnumerable<string> selectors,
        string? adapterId = null,
        LauncherBuildMode buildMode = LauncherBuildMode.Auto)
    {
        var config = LoadConfig();
        var resolveResult = ResolvePlan(
            selectors.Where(selector => !string.IsNullOrWhiteSpace(selector)).ToList(),
            adapterId,
            buildMode,
            config,
            BuildCatalog(config),
            LoadPresets());
        if (resolveResult.Plan.IsExecutableTarget)
        {
            return await LaunchExecutableTargetAsync(resolveResult.Plan, config);
        }

        var prepared = await PrepareLaunchAsync(selectors, adapterId, buildMode);
        if (!prepared.Ok || prepared.Plan is null)
        {
            return new LauncherLaunchResult(false, prepared.Error, -1, string.Empty, string.Empty, resolveResult.Plan);
        }

        var plan = prepared.Plan;
        ReplacePreviousActiveProcess(plan);
        var startInfo = new ProcessStartInfo(
            ResolveDotnetCommand(),
            $"exec --roll-forward Major \"{plan.AppAssemblyPath}\" \"{prepared.BootstrapPath}\"")
        {
            WorkingDirectory = plan.AppOutputDirectory,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            return new LauncherLaunchResult(false, "Failed to start platform process.", -1, string.Empty, prepared.BootstrapPath, plan);
        }

        PersistActiveProcess(plan, prepared.BootstrapPath, process);
        return new LauncherLaunchResult(true, string.Empty, process.Id, plan.LaunchUrl, prepared.BootstrapPath, plan);
    }

    private async Task<LauncherLaunchResult> LaunchExecutableTargetAsync(LauncherLaunchPlan plan, LauncherConfig config)
    {
        var buildResults = await BuildPlanRuntimeAsync(plan, config, CancellationToken.None);
        var failedBuild = buildResults.FirstOrDefault(result => !result.Ok);
        if (failedBuild != null)
        {
            return new LauncherLaunchResult(false, failedBuild.Output, -1, string.Empty, string.Empty, plan);
        }

        ReplacePreviousActiveProcess(plan);
        Process process;
        try
        {
            process = Process.Start(CreateAppStartInfo(
                plan.AppAssemblyPath,
                plan.AppOutputDirectory,
                BuildExecutableTargetArguments(plan)));
        }
        catch (InvalidOperationException ex)
        {
            return new LauncherLaunchResult(false, ex.Message, -1, string.Empty, string.Empty, plan);
        }

        if (process == null)
        {
            return new LauncherLaunchResult(false, "Failed to start executable target process.", -1, string.Empty, string.Empty, plan);
        }

        PersistActiveProcess(plan, string.Empty, process);
        return new LauncherLaunchResult(true, string.Empty, process.Id, string.Empty, string.Empty, plan);
    }

    public async Task<LauncherExecutableTargetRun> ExecuteExecutableTargetAsync(LauncherLaunchPlan plan, CancellationToken ct = default)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (!plan.IsExecutableTarget)
        {
            throw new InvalidOperationException("Plan is not an executable target plan.");
        }

        ct.ThrowIfCancellationRequested();
        var (runnerFileName, runnerArguments) = BuildAppRunnerCommand(
            plan.AppAssemblyPath,
            BuildExecutableTargetArguments(plan));
        var run = await RunProcessAsync(
            runnerFileName,
            runnerArguments,
            plan.AppOutputDirectory,
            timeoutMs: 300_000);
        return new LauncherExecutableTargetRun($"{runnerFileName} {runnerArguments}", run.ExitCode, run.Output);
    }

    private static string BuildExecutableTargetArguments(LauncherLaunchPlan plan)
    {
        var arguments = new StringBuilder();
        foreach (var argument in plan.ExecutableArgs ?? Array.Empty<string>())
        {
            if (arguments.Length > 0)
            {
                arguments.Append(' ');
            }

            arguments.Append(QuoteProcessArgument(argument));
        }

        return arguments.ToString();
    }

    /// <summary>
    /// 自包含发布布局下 apphost 与应用 DLL 同目录：Windows 为 .exe，Unix 为无扩展名同名文件。
    /// 存在则直启（玩家机无需安装 .NET 运行时）；否则要求可用的 dotnet（开发机布局），缺则显式失败。
    /// </summary>
    private static (string FileName, string Arguments) BuildAppRunnerCommand(string appAssemblyPath, string arguments)
    {
        var appHostPath = ResolveAppHostPath(appAssemblyPath);
        if (appHostPath != null)
        {
            return (appHostPath, arguments);
        }

        var dotnet = ResolveDotnetCommand();
        if (!IsUsableDotnetCommand(dotnet))
        {
            throw new InvalidOperationException(
                $"App host executable not found next to '{appAssemblyPath}', and no usable dotnet is available. " +
                "Prebuilt/player packages must ship the self-contained apphost next to the app DLL " +
                "(Windows: *.exe; Linux/macOS: extensionless sibling); " +
                "dev layouts require a runnable dotnet (launcher must run under dotnet, or dotnet must be on PATH).");
        }

        var dotnetArguments = string.IsNullOrWhiteSpace(arguments)
            ? $"exec --roll-forward Major \"{appAssemblyPath}\""
            : $"exec --roll-forward Major \"{appAssemblyPath}\" {arguments}";
        return (dotnet, dotnetArguments);
    }

    private static string? ResolveAppHostPath(string appAssemblyPath)
    {
        var directory = Path.GetDirectoryName(appAssemblyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(appAssemblyPath);
        if (OperatingSystem.IsWindows())
        {
            var windowsHost = Path.Combine(directory, baseName + ".exe");
            return File.Exists(windowsHost) ? windowsHost : null;
        }

        // linux-x64 / osx-* self-contained：apphost 与 DLL 同名、无扩展名
        var unixHost = Path.Combine(directory, baseName);
        if (!File.Exists(unixHost))
        {
            return null;
        }

        if (string.Equals(Path.GetFullPath(unixHost), Path.GetFullPath(appAssemblyPath), StringComparison.Ordinal))
        {
            return null;
        }

        return unixHost;
    }

    private static bool IsUsableDotnetCommand(string command)
    {
        if (!string.Equals(Path.GetFileName(command), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(command), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(command);
        }

        // 裸 "dotnet"：必须真的能在 PATH 上找到
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        return pathVariable.Split(Path.PathSeparator).Any(directory =>
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            return extensions.Any(extension =>
                File.Exists(Path.Combine(directory.Trim(), $"dotnet{extension}")));
        });
    }

    private static ProcessStartInfo CreateAppStartInfo(string appAssemblyPath, string workingDirectory, string arguments)
    {
        var (fileName, fullArguments) = BuildAppRunnerCommand(appAssemblyPath, arguments);
        return new ProcessStartInfo(fileName, fullArguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
    }

    private static string QuoteProcessArgument(string argument)
    {
        if (!argument.Contains(' ') && !argument.Contains('"'))
        {
            return argument;
        }

        return $"\"{argument.Replace("\"", "\\\"")}\"";
    }

    public LauncherResolveResult Resolve(
        IEnumerable<string> selectors,
        string? adapterId = null,
        LauncherBuildMode buildMode = LauncherBuildMode.Auto,
        string? browserProviderOverride = null)
    {
        var config = LoadConfig();
        var catalog = BuildCatalog(config);
        var result = ResolvePlan(
            selectors.Where(selector => !string.IsNullOrWhiteSpace(selector)).ToList(),
            adapterId,
            buildMode,
            config,
            catalog,
            LoadPresets(),
            browserProviderOverride);
        WriteLaunchGraphDocument(result.Plan);
        return result;
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

        var create = await RunDotnetAsync($"new sln -n {entry.Info.Id} --force", entry.Info.RootPath, timeoutMs: 30_000);
        if (create.ExitCode != 0)
        {
            throw new InvalidOperationException(create.Output);
        }

        var projectPath = EnsureProjectFile(entry, config);
        if (File.Exists(projectPath))
        {
            await RunDotnetAsync($"sln \"{solutionPath}\" add \"{projectPath}\"", entry.Info.RootPath, timeoutMs: 30_000);
        }

        foreach (var dependencyId in entry.Manifest.Dependencies.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var dependency = ResolveUniqueModEntry(dependencyId, catalog.ById);
            var dependencyProjectPath = ResolveBuildProjectPath(config, dependency.Info.RootPath, dependency.Info.Id, dependency.Info.ProjectPath);
            if (!string.IsNullOrWhiteSpace(dependencyProjectPath) && File.Exists(dependencyProjectPath))
            {
                await RunDotnetAsync($"sln \"{solutionPath}\" add \"{dependencyProjectPath}\"", entry.Info.RootPath, timeoutMs: 30_000);
            }
        }

        var coreProjectPath = Path.Combine(_repoRoot, "src", "Core", "Ludots.Core.csproj");
        if (File.Exists(coreProjectPath))
        {
            await RunDotnetAsync($"sln \"{solutionPath}\" add \"{coreProjectPath}\"", entry.Info.RootPath, timeoutMs: 30_000);
        }

        return solutionPath;
    }

    public async Task<string> CreateModAsync(string modId, string template, string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("Mod id is required.", nameof(modId));
        }

        var args = new StringBuilder($"mod init --id \"{modId}\" --template \"{template}\"");
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            args.Append($" --dir \"{targetDirectory}\"");
        }

        var result = await RunLudotsToolAsync(args.ToString(), timeoutMs: 120_000);
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
        LauncherPresetDocument presetDocument,
        string? browserProviderOverride = null)
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
        var resolutionState = new PlanResolutionState();
        var roots = new List<CatalogEntry>();

        foreach (var selector in selectors)
        {
            roots.AddRange(ResolveSelector(selector, config, presetDocument, catalog, localByRootPath, localById, presetStack, resolutionState));
        }

        if (resolutionState.ExecutableTargets.Count > 0)
        {
            if (roots.Count > 0)
            {
                throw new InvalidOperationException(
                    "Executable project bindings cannot be combined with mod selectors in one launch plan.");
            }

            var executablePlan = BuildExecutableLaunchPlan(selectors, adapterId, buildMode, config, resolutionState);
            return new LauncherResolveResult(executablePlan, catalog.Entries.Select(entry => entry.Info).ToList());
        }

        var ordered = ResolveDependencyClosure(roots, localById);
        var resolvedAdapterId = string.IsNullOrWhiteSpace(adapterId)
            ? ResolveSelectedAdapterId(config, LoadPreferences())
            : adapterId!.Trim().ToLowerInvariant();
        var profile = GetPlatformProfile(resolvedAdapterId);
        var buildModeText = buildMode.ToString().ToLowerInvariant();
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
        var rootModIds = roots
            .Select(entry => entry.Info.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedModIds = ordered
            .Select(entry => entry.Info.Id)
            .ToList();
        var diagnostics = BuildPlanDiagnostics(roots, ordered);
        var browserRuntime = ResolveBrowserRuntimeConfig(
            selectors,
            presetDocument,
            diagnostics,
            config,
            browserProviderOverride);
        var adapterDescriptor = BuildAdapterDescriptor(profile);
        var bootstrapArtifactPath = Path.Combine(profile.OutputDirectory, profile.RuntimeBootstrapFileName);
        var appAssemblyPath = ResolveAppAssemblyPath(profile);
        var graphArtifactPath = ResolveGraphArtifactPath(profile);
        var generatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var planFingerprint = ComputePlanFingerprint(
            adapterDescriptor,
            buildModeText,
            selectors,
            rootModIds,
            orderedModIds,
            plannedMods,
            "file",
            bootstrapArtifactPath,
            graphArtifactPath,
            profile.OutputDirectory,
            appAssemblyPath,
            profile.LaunchUrl,
            browserRuntime);

        var plan = new LauncherLaunchPlan(
            profile.Id,
            buildModeText,
            selectors,
            rootModIds,
            orderedModIds,
            plannedMods,
            "file",
            bootstrapArtifactPath,
            profile.OutputDirectory,
            appAssemblyPath,
            profile.LaunchUrl,
            browserRuntime,
            diagnostics,
            adapterDescriptor,
            LaunchGraphSchemaVersion,
            generatedAtUtc,
            planFingerprint,
            graphArtifactPath);

        return new LauncherResolveResult(plan, catalog.Entries.Select(entry => entry.Info).ToList());
    }

    private IReadOnlyList<CatalogEntry> ResolveSelector(
        string selector,
        LauncherConfig config,
        LauncherPresetDocument presetDocument,
        CatalogIndex catalog,
        Dictionary<string, CatalogEntry> localByRootPath,
        Dictionary<string, List<CatalogEntry>> localById,
        HashSet<string> presetStack,
        PlanResolutionState resolutionState)
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

            return ResolveBinding(binding, config, catalog, localByRootPath, localById, presetDocument, presetStack, resolutionState);
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

                ApplyPresetArgs(preset, resolutionState);
                var resolved = new List<CatalogEntry>();
                foreach (var nestedSelector in preset.Selectors)
                {
                    resolved.AddRange(ResolveSelector(nestedSelector, config, presetDocument, catalog, localByRootPath, localById, presetStack, resolutionState));
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
        HashSet<string> presetStack,
        PlanResolutionState resolutionState)
    {
        return binding.Target.Type.Trim().ToLowerInvariant() switch
        {
            "path" => ResolveSelector($"path:{binding.Target.Value}", config, presetDocument, catalog, localByRootPath, localById, presetStack, resolutionState),
            "modid" => ResolveSelector($"mod:{binding.Target.Value}", config, presetDocument, catalog, localByRootPath, localById, presetStack, resolutionState),
            "project" => ResolveProjectBinding(binding, resolutionState),
            _ => throw new InvalidOperationException($"Unsupported binding target type: {binding.Target.Type}")
        };
    }

    private IReadOnlyList<CatalogEntry> ResolveProjectBinding(LauncherBinding binding, PlanResolutionState resolutionState)
    {
        var projectPath = ResolveRepoRelativePath(binding.Target.Value);
        if (!File.Exists(projectPath))
        {
            throw new InvalidOperationException($"Executable project target not found: {projectPath}");
        }

        resolutionState.ExecutableTargets.Add(new ExecutableTargetCandidate(projectPath, binding.Target.Args));
        return Array.Empty<CatalogEntry>();
    }

    private static void ApplyPresetArgs(LauncherPresetDefinition preset, PlanResolutionState resolutionState)
    {
        if (preset.Args == null)
        {
            return;
        }

        if (resolutionState.PresetArgs != null &&
            !resolutionState.PresetArgs.SequenceEqual(preset.Args, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Conflicting preset args: [{string.Join(" ", resolutionState.PresetArgs)}] vs [{string.Join(" ", preset.Args)}].");
        }

        resolutionState.PresetArgs = preset.Args.ToList();
    }

    private LauncherLaunchPlan BuildExecutableLaunchPlan(
        IReadOnlyList<string> selectors,
        string? adapterId,
        LauncherBuildMode buildMode,
        LauncherConfig config,
        PlanResolutionState resolutionState)
    {
        var distinctTargets = resolutionState.ExecutableTargets
            .GroupBy(target => target.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctTargets.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple executable project targets selected: {string.Join(", ", distinctTargets.Select(group => group.Key))}.");
        }

        var target = distinctTargets[0].First();
        var resolvedAdapterId = string.IsNullOrWhiteSpace(adapterId)
            ? ResolveSelectedAdapterId(config, LoadPreferences())
            : adapterId!.Trim().ToLowerInvariant();
        var profile = GetPlatformProfile(resolvedAdapterId);
        var buildModeText = buildMode.ToString().ToLowerInvariant();
        var executableArgs = NormalizeExecutableArgs(resolutionState.PresetArgs ?? target.BindingArgs);
        var outputDirectory = ResolveExecutableOutputDirectory(target.ProjectPath);
        var appAssemblyPath = ResolveExecutableAssemblyPath(target.ProjectPath, outputDirectory);
        var adapterDescriptor = new LauncherAdapterDescriptor(
            profile.Id,
            profile.Name,
            string.Equals(profile.Id, LauncherPlatformIds.Web, StringComparison.OrdinalIgnoreCase) ? "web" : "desktop",
            "dotnet",
            "none",
            target.ProjectPath,
            outputDirectory,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
        var graphArtifactPath = ResolveGraphArtifactPath(profile);
        var generatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var planFingerprint = ComputePlanFingerprint(
            adapterDescriptor,
            buildModeText,
            selectors,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<LauncherPlannedMod>(),
            "none",
            string.Empty,
            graphArtifactPath,
            outputDirectory,
            appAssemblyPath,
            string.Empty,
            browserRuntime: null,
            isExecutableTarget: true,
            executableProjectPath: target.ProjectPath,
            executableArgs: executableArgs);

        return new LauncherLaunchPlan(
            profile.Id,
            buildModeText,
            selectors,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<LauncherPlannedMod>(),
            "none",
            string.Empty,
            outputDirectory,
            appAssemblyPath,
            string.Empty,
            null,
            new LauncherPlanDiagnostics(Array.Empty<LauncherResolvedSetting>(), Array.Empty<string>()),
            adapterDescriptor,
            LaunchGraphSchemaVersion,
            generatedAtUtc,
            planFingerprint,
            graphArtifactPath,
            IsExecutableTarget: true,
            ExecutableProjectPath: target.ProjectPath,
            ExecutableArgs: executableArgs);
    }

    private static IReadOnlyList<string> NormalizeExecutableArgs(List<string>? args)
    {
        return args == null
            ? Array.Empty<string>()
            : args.Where(arg => !string.IsNullOrWhiteSpace(arg)).Select(arg => arg.Trim()).ToList();
    }

    private static string ResolveExecutableOutputDirectory(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new InvalidOperationException($"Executable project path has no directory: {projectPath}");
        }

        return Path.Combine(projectDirectory, "bin", "Release", RuntimeTargetFramework);
    }

    private static string ResolveExecutableAssemblyPath(string projectPath, string outputDirectory)
    {
        return Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(projectPath) + ".dll");
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

    private LauncherPlanDiagnostics BuildPlanDiagnostics(
        IReadOnlyList<CatalogEntry> roots,
        IReadOnlyList<CatalogEntry> ordered)
    {
        var fragments = CollectGameConfigFragments(roots, ordered);
        var settings = new List<LauncherResolvedSetting>
        {
            ResolveGameJsonSetting("defaultCoreMod", fragments),
            ResolveGameJsonSetting("startupMapId", fragments),
            ResolveGameJsonSetting("startupInputContexts", fragments),
            ResolveGameJsonSetting("browserRuntime", fragments)
        };
        var warnings = BuildPlanWarnings(roots, settings);
        return new LauncherPlanDiagnostics(settings, warnings);
    }

    private BrowserRuntimeConfig? ResolveBrowserRuntimeConfig(
        IReadOnlyList<string> selectors,
        LauncherPresetDocument presetDocument,
        LauncherPlanDiagnostics diagnostics,
        LauncherConfig config,
        string? browserProviderOverride = null)
    {
        BrowserRuntimeConfig? gameConfig = ResolveBrowserRuntimeFromDiagnostics(diagnostics);
        BrowserRuntimeConfig? presetConfig = ResolveBrowserRuntimeFromSelectors(selectors, presetDocument);
        BrowserRuntimeConfig? effective = presetConfig ?? gameConfig;
        if (effective == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(browserProviderOverride))
        {
            effective = CloneBrowserRuntimeConfig(effective);
            effective.Provider = browserProviderOverride.Trim();
            // Force host paths to be re-derived from the selected provider registration.
            effective.ProviderAssemblyPath = string.Empty;
            effective.ProviderHostTypeName = string.Empty;
            effective.ProviderProjectPath = string.Empty;
            effective.RuntimeRootPath = string.Empty;
            effective.UseCollectibleLoadContext = null;
            effective.ProcessSharedAssemblyNamePrefixes = Array.Empty<string>();
        }

        return CompleteHostBrowserRuntimeConfig(effective, config);
    }

    private static BrowserRuntimeConfig? ResolveBrowserRuntimeFromDiagnostics(LauncherPlanDiagnostics diagnostics)
    {
        LauncherResolvedSetting? setting = diagnostics.Settings.FirstOrDefault(item =>
            string.Equals(item.Key, "browserRuntime", StringComparison.OrdinalIgnoreCase));
        if (setting?.EffectiveValue == null)
        {
            return null;
        }

        var options = StrictJsonOptions.CreateCamelCase();
        return JsonSerializer.Deserialize<BrowserRuntimeConfig>(
            setting.EffectiveValue.ToJsonString(),
            options);
    }

    private static BrowserRuntimeConfig? ResolveBrowserRuntimeFromSelectors(
        IReadOnlyList<string> selectors,
        LauncherPresetDocument presetDocument)
    {
        var presetStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BrowserRuntimeConfig? runtime = null;
        foreach (var selector in selectors)
        {
            BrowserRuntimeConfig? candidate = ResolveBrowserRuntimeFromSelector(selector, presetDocument, presetStack);
            if (candidate != null)
            {
                runtime = candidate;
            }
        }

        return runtime;
    }

    private static BrowserRuntimeConfig? ResolveBrowserRuntimeFromSelector(
        string selector,
        LauncherPresetDocument presetDocument,
        HashSet<string> presetStack)
    {
        if (!selector.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string presetId = selector["preset:".Length..];
        if (!presetStack.Add(presetId))
        {
            throw new InvalidOperationException($"Preset cycle detected at '{presetId}'.");
        }

        try
        {
            LauncherPresetDefinition? preset = presetDocument.Presets.FirstOrDefault(item =>
                string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
            {
                throw new InvalidOperationException($"Preset not found: {presetId}");
            }

            BrowserRuntimeConfig? runtime = null;
            foreach (string nestedSelector in preset.Selectors)
            {
                BrowserRuntimeConfig? nestedRuntime = ResolveBrowserRuntimeFromSelector(nestedSelector, presetDocument, presetStack);
                if (nestedRuntime != null)
                {
                    runtime = nestedRuntime;
                }
            }

            return preset.BrowserRuntime ?? runtime;
        }
        finally
        {
            presetStack.Remove(presetId);
        }
    }

    private BrowserRuntimeConfig CompleteHostBrowserRuntimeConfig(BrowserRuntimeConfig source, LauncherConfig config)
    {
        BrowserRuntimeConfig runtime = CloneBrowserRuntimeConfig(source);
        if (!runtime.Enabled && !runtime.Required)
        {
            return runtime;
        }

        if (string.IsNullOrWhiteSpace(runtime.Provider))
        {
            return runtime;
        }

        LauncherBrowserRuntimeProvider? provider = config.BrowserRuntimeProviders.FirstOrDefault(item =>
            string.Equals(item.Id, runtime.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
        {
            throw new InvalidOperationException(
                $"browserRuntime provider '{runtime.Provider}' is not registered in launcher.config.json browserRuntimeProviders.");
        }

        if (string.Equals(runtime.Provider, "cef", StringComparison.OrdinalIgnoreCase) &&
            !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "browserRuntime provider 'cef' requires Windows (CefSharp.OffScreen.NETCore win-x64). " +
                $"Current OS '{System.Runtime.InteropServices.RuntimeInformation.OSDescription}' is unsupported. " +
                "Disable browserRuntime on this host, or register a Linux-capable provider such as Ultralight.");
        }

        if (!string.IsNullOrWhiteSpace(provider.ProjectPath))
        {
            runtime.ProviderProjectPath = ResolveRepoRelativePath(provider.ProjectPath);
        }

        string packageRootPath = ResolveProviderPackageRootPath(provider);
        string providerAssemblyPath = ResolveProviderAssemblyPath(provider, packageRootPath);

        if (string.IsNullOrWhiteSpace(runtime.ProviderAssemblyPath))
        {
            runtime.ProviderAssemblyPath = providerAssemblyPath;
        }
        else
        {
            EnsureSamePath(
                ResolveRepoRelativePath(runtime.ProviderAssemblyPath),
                providerAssemblyPath,
                "browserRuntime.providerAssemblyPath");
        }

        if (string.IsNullOrWhiteSpace(runtime.RuntimeRootPath))
        {
            runtime.RuntimeRootPath = packageRootPath;
        }
        else
        {
            EnsureSamePath(
                ResolveRepoRelativePath(runtime.RuntimeRootPath),
                packageRootPath,
                "browserRuntime.runtimeRootPath");
        }

        if (!string.IsNullOrWhiteSpace(provider.HostTypeName))
        {
            runtime.ProviderHostTypeName = provider.HostTypeName.Trim();
        }

        if (string.IsNullOrWhiteSpace(runtime.ProviderAssemblyPath))
        {
            throw new InvalidOperationException(
                $"browserRuntime provider '{runtime.Provider}' is registered without an assemblyPath.");
        }

        if (string.IsNullOrWhiteSpace(runtime.ProviderHostTypeName))
        {
            throw new InvalidOperationException(
                $"browserRuntime provider '{runtime.Provider}' is registered without a hostTypeName.");
        }

        runtime.UseCollectibleLoadContext = provider.UseCollectibleLoadContext;
        runtime.ProcessSharedAssemblyNamePrefixes = provider.ProcessSharedAssemblyNamePrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .ToArray();

        return runtime;
    }

    private string ResolveProviderPackageRootPath(LauncherBrowserRuntimeProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.PackageRootPath))
        {
            throw new InvalidOperationException(
                $"browserRuntime provider '{provider.Id}' must declare packageRootPath in launcher.config.json.");
        }

        return ResolveRepoRelativePath(provider.PackageRootPath);
    }

    private string ResolveProviderAssemblyPath(
        LauncherBrowserRuntimeProvider provider,
        string packageRootPath)
    {
        if (string.IsNullOrWhiteSpace(provider.AssemblyPath))
        {
            throw new InvalidOperationException(
                $"browserRuntime provider '{provider.Id}' must declare assemblyPath in launcher.config.json.");
        }

        string providerAssemblyPath = ResolveRepoRelativePath(provider.AssemblyPath);
        if (!IsSameOrChildPath(packageRootPath, providerAssemblyPath))
        {
            throw new InvalidOperationException(
                $"browserRuntime provider '{provider.Id}' assemblyPath must be inside packageRootPath.");
        }

        return providerAssemblyPath;
    }

    private void EnsureSamePath(string configuredPath, string expectedPath, string configKey)
    {
        if (!PathsEqual(configuredPath, expectedPath))
        {
            throw new InvalidOperationException(
                $"{configKey} must be derived from the selected browserRuntime provider package root.");
        }
    }

    private static BrowserRuntimeConfig CloneBrowserRuntimeConfig(BrowserRuntimeConfig source)
    {
        return new BrowserRuntimeConfig
        {
            Enabled = source.Enabled,
            Required = source.Required,
            Provider = source.Provider,
            ProviderAssemblyPath = source.ProviderAssemblyPath,
            ProviderHostTypeName = source.ProviderHostTypeName,
            ProviderProjectPath = source.ProviderProjectPath,
            RuntimeRootPath = source.RuntimeRootPath,
            CacheRootPath = source.CacheRootPath,
            UseCollectibleLoadContext = source.UseCollectibleLoadContext,
            ProcessSharedAssemblyNamePrefixes = source.ProcessSharedAssemblyNamePrefixes?.ToArray() ?? Array.Empty<string>()
        };
    }

    private string ResolveRepoRelativePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_repoRoot, path));
    }

    private List<GameConfigFragment> CollectGameConfigFragments(
        IReadOnlyList<CatalogEntry> roots,
        IReadOnlyList<CatalogEntry> ordered)
    {
        var fragments = new List<GameConfigFragment>();
        AppendGameConfigFragment(fragments, Path.Combine(_repoRoot, "assets", "Configs", "game.json"), ownerModId: null, isRootSelection: false);
        AppendGameConfigFragment(fragments, Path.Combine(_repoRoot, "assets", "game.json"), ownerModId: null, isRootSelection: false);

        var rootPaths = new HashSet<string>(
            roots.Select(entry => entry.Info.RootPath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ordered)
        {
            bool isRootSelection = rootPaths.Contains(entry.Info.RootPath);
            AppendGameConfigFragment(
                fragments,
                Path.Combine(entry.Info.RootPath, "assets", "game.json"),
                entry.Info.Id,
                isRootSelection);
            AppendGameConfigFragment(
                fragments,
                Path.Combine(entry.Info.RootPath, "assets", "Configs", "game.json"),
                entry.Info.Id,
                isRootSelection);
        }

        return fragments;
    }

    private void AppendGameConfigFragment(
        List<GameConfigFragment> fragments,
        string fullPath,
        string? ownerModId,
        bool isRootSelection)
    {
        if (!File.Exists(fullPath))
        {
            return;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(fullPath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse launcher startup config fragment '{fullPath}': {ex.Message}", ex);
        }

        if (parsed is not JsonObject obj)
        {
            throw new InvalidOperationException($"Launcher startup config fragment must be a JSON object: {fullPath}");
        }

        fragments.Add(new GameConfigFragment(GetPortablePath(fullPath), ownerModId, isRootSelection, obj));
    }

    private static LauncherResolvedSetting ResolveGameJsonSetting(
        string key,
        IReadOnlyList<GameConfigFragment> fragments)
    {
        var contributions = new List<LauncherSettingContribution>();
        JsonNode? effectiveValue = null;
        string? effectiveSource = null;

        foreach (var fragment in fragments)
        {
            if (!fragment.Content.TryGetPropertyValue(key, out var value))
            {
                continue;
            }

            var clonedValue = value?.DeepClone();
            contributions.Add(new LauncherSettingContribution(
                fragment.Source,
                fragment.OwnerModId,
                fragment.IsRootSelection,
                clonedValue));
            effectiveValue = clonedValue?.DeepClone();
            effectiveSource = fragment.Source;
        }

        return new LauncherResolvedSetting(key, effectiveValue, effectiveSource, contributions);
    }

    private static IReadOnlyList<string> BuildPlanWarnings(
        IReadOnlyList<CatalogEntry> roots,
        IReadOnlyList<LauncherResolvedSetting> settings)
    {
        var warnings = new List<string>();
        var distinctRoots = roots
            .GroupBy(entry => entry.Info.RootPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Info.Id)
            .ToList();
        if (distinctRoots.Count > 1)
        {
            warnings.Add(
                $"Selected {distinctRoots.Count} root mods ({string.Join(", ", distinctRoots)}). Runtime still boots a single startupMapId; inspect the effective startup settings below.");
        }

        foreach (var setting in settings)
        {
            int rootContributionCount = setting.Contributions.Count(contribution => contribution.IsRootSelection);
            if (rootContributionCount > 1)
            {
                warnings.Add(
                    $"'{setting.Key}' is written by multiple selected mods; final winner is {setting.EffectiveSource}.");
            }
        }

        if (settings.FirstOrDefault(setting => string.Equals(setting.Key, "startupMapId", StringComparison.OrdinalIgnoreCase))?.EffectiveValue == null)
        {
            warnings.Add("No startupMapId was found in merged game.json fragments.");
        }

        return warnings;
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
                Path.Combine(_repoRoot, "src", "Apps", "Raylib", "Ludots.App.Raylib", "bin", "Release", RuntimeTargetFramework),
                string.Empty,
                string.Empty,
                string.Empty,
                "launcher.runtime.json"),
            new LauncherPlatformProfile(
                LauncherPlatformIds.Web,
                "Web",
                Path.Combine(_repoRoot, "src", "Apps", "Web", "Ludots.App.Web", "Ludots.App.Web.csproj"),
                Path.Combine(_repoRoot, "src", "Apps", "Web", "Ludots.App.Web", "bin", "Release", RuntimeTargetFramework),
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

    private static LauncherAdapterDescriptor BuildAdapterDescriptor(LauncherPlatformProfile profile)
    {
        var isWeb = string.Equals(profile.Id, LauncherPlatformIds.Web, StringComparison.OrdinalIgnoreCase);
        return new LauncherAdapterDescriptor(
            profile.Id,
            profile.Name,
            isWeb ? "web" : "desktop",
            isWeb ? "dotnet+npm" : "dotnet",
            "launcher.runtime.v1",
            profile.AppProjectPath,
            profile.OutputDirectory,
            profile.ClientProjectDirectory,
            profile.ClientDistributionDirectory,
            profile.LaunchUrl,
            profile.RuntimeBootstrapFileName);
    }

    private string ResolveGraphArtifactPath(LauncherPlatformProfile profile)
    {
        var fileName = $"{profile.Id}.launch.graph.json";
        return Path.Combine(_repoRoot, "artifacts", "launcher", fileName);
    }

    private static string ComputePlanFingerprint(
        LauncherAdapterDescriptor adapter,
        string buildMode,
        IReadOnlyList<string> selectors,
        IReadOnlyList<string> rootModIds,
        IReadOnlyList<string> orderedModIds,
        IReadOnlyList<LauncherPlannedMod> plannedMods,
        string bootstrapArtifactStrategy,
        string bootstrapArtifactPath,
        string graphArtifactPath,
        string appOutputDirectory,
        string appAssemblyPath,
        string launchUrl,
        BrowserRuntimeConfig? browserRuntime,
        bool isExecutableTarget = false,
        string executableProjectPath = "",
        IReadOnlyList<string>? executableArgs = null)
    {
        var payload = new PlanFingerprintPayload(
            LaunchGraphSchemaVersion,
            adapter,
            buildMode,
            selectors.ToList(),
            rootModIds.ToList(),
            orderedModIds.ToList(),
            plannedMods
                .Select(mod => new PlanFingerprintModPayload(
                    mod.Id,
                    mod.RootPath,
                    mod.ProjectPath,
                    mod.MainAssemblyPath,
                    mod.Kind.ToString(),
                    mod.BindingNames.ToList()))
                .ToList(),
            bootstrapArtifactStrategy,
            bootstrapArtifactPath,
            graphArtifactPath,
            appOutputDirectory,
            appAssemblyPath,
            launchUrl,
            browserRuntime,
            isExecutableTarget,
            executableProjectPath,
            executableArgs?.ToList());
        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
                case "project":
                {
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
                BrowserRuntime = preset.BrowserRuntime,
                ActiveModIds = activeModIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                IncludeDependencies = true
            });
        }

        return output;
    }

    private async Task<IReadOnlyList<LauncherBuildResult>> BuildPlanRuntimeAsync(
        LauncherLaunchPlan plan,
        LauncherConfig config,
        CancellationToken ct)
    {
        var results = new List<LauncherBuildResult>();
        if (plan.IsExecutableTarget)
        {
            results.Add(await BuildExecutableTargetAsync(plan, ct));
            return results;
        }

        results.AddRange(await BuildPlannedModsAsync(plan, config, ct));
        results.AddRange(await BuildHostBrowserRuntimeAsync(plan, ct));
        return results;
    }

    private async Task<LauncherBuildResult> BuildExecutableTargetAsync(LauncherLaunchPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var resultId = $"project:{plan.ExecutableProjectPath}";
        if (plan.BuildMode == LauncherBuildMode.Never.ToString().ToLowerInvariant() && File.Exists(plan.AppAssemblyPath))
        {
            return new LauncherBuildResult(resultId, true, 0, "Executable target build skipped by request.");
        }

        var projectDirectory = Path.GetDirectoryName(plan.ExecutableProjectPath) ?? _repoRoot;
        var build = await RunDotnetAsync(
            $"build \"{plan.ExecutableProjectPath}\" -c Release",
            projectDirectory,
            timeoutMs: 300_000);
        if (build.ExitCode != 0)
        {
            return new LauncherBuildResult(resultId, false, build.ExitCode, build.Output);
        }

        if (!File.Exists(plan.AppAssemblyPath))
        {
            return new LauncherBuildResult(
                resultId,
                false,
                1,
                $"{build.Output}{Environment.NewLine}Executable assembly missing after build: {plan.AppAssemblyPath}");
        }

        return new LauncherBuildResult(resultId, true, 0, build.Output);
    }

    private async Task<IReadOnlyList<LauncherBuildResult>> BuildHostBrowserRuntimeAsync(
        LauncherLaunchPlan plan,
        CancellationToken ct)
    {
        BrowserRuntimeConfig? browserRuntime = plan.BrowserRuntime;
        if (browserRuntime == null || !browserRuntime.Enabled)
        {
            return Array.Empty<LauncherBuildResult>();
        }

        string resultId = $"browserRuntime:{browserRuntime.Provider}";
        if (string.IsNullOrWhiteSpace(browserRuntime.ProviderAssemblyPath))
        {
            return new[]
            {
                new LauncherBuildResult(
                    resultId,
                    false,
                    1,
                    "browserRuntime.providerAssemblyPath is required for a host-owned browser runtime provider.")
            };
        }

        if (string.IsNullOrWhiteSpace(browserRuntime.RuntimeRootPath))
        {
            return new[]
            {
                new LauncherBuildResult(
                    resultId,
                    false,
                    1,
                    "browserRuntime.runtimeRootPath is required for a host-owned browser runtime provider.")
            };
        }

        if (string.IsNullOrWhiteSpace(browserRuntime.ProviderProjectPath))
        {
            bool exists = ValidateBrowserRuntimePackage(browserRuntime, out string validationMessage);
            return new[]
            {
                new LauncherBuildResult(
                    resultId,
                    exists,
                    exists ? 0 : 1,
                    exists
                        ? $"Host browser runtime provider package already exists: {browserRuntime.RuntimeRootPath}"
                        : validationMessage)
            };
        }

        ct.ThrowIfCancellationRequested();
        if (plan.BuildMode == LauncherBuildMode.Never.ToString().ToLowerInvariant() &&
            ValidateBrowserRuntimePackage(browserRuntime, out _))
        {
            return new[]
            {
                new LauncherBuildResult(
                    resultId,
                    true,
                    0,
                    "Host browser runtime provider build skipped by request.")
            };
        }

        string projectDirectory = Path.GetDirectoryName(browserRuntime.ProviderProjectPath) ?? _repoRoot;
        var output = new StringBuilder();
        Directory.CreateDirectory(browserRuntime.RuntimeRootPath);
        var publish = await RunDotnetAsync(
            $"publish \"{browserRuntime.ProviderProjectPath}\" -c Release -o \"{browserRuntime.RuntimeRootPath}\" --self-contained false /p:GenerateRuntimeConfigurationFiles=false -nologo -v:m",
            projectDirectory,
            timeoutMs: 300_000);
        output.AppendLine(publish.Output);
        if (publish.ExitCode != 0)
        {
            return new[] { new LauncherBuildResult(resultId, false, publish.ExitCode, output.ToString()) };
        }

        if (!ValidateBrowserRuntimePackage(browserRuntime, out string packageValidationMessage))
        {
            output.AppendLine(packageValidationMessage);
            return new[] { new LauncherBuildResult(resultId, false, 1, output.ToString()) };
        }

        return new[] { new LauncherBuildResult(resultId, true, 0, output.ToString()) };
    }

    private static bool ValidateBrowserRuntimePackage(BrowserRuntimeConfig browserRuntime, out string message)
    {
        if (!File.Exists(browserRuntime.ProviderAssemblyPath))
        {
            message = $"Host browser runtime provider assembly is missing: {browserRuntime.ProviderAssemblyPath}";
            return false;
        }

        if (!Directory.Exists(browserRuntime.RuntimeRootPath))
        {
            message = $"Host browser runtime package root is missing: {browserRuntime.RuntimeRootPath}";
            return false;
        }

        if (string.Equals(browserRuntime.Provider, "cef", StringComparison.OrdinalIgnoreCase))
        {
            string[] requiredFiles =
            {
                "Ludots.UI.Browser.Cef.deps.json",
                "CefSharp.Core.Runtime.dll",
                "libcef.dll",
                "resources.pak",
                "icudtl.dat",
                Path.Combine("locales", "en-US.pak")
            };

            foreach (string file in requiredFiles)
            {
                string path = Path.Combine(browserRuntime.RuntimeRootPath, file);
                if (!File.Exists(path))
                {
                    message = $"CEF browser runtime package is incomplete. Missing: {path}";
                    return false;
                }
            }
        }
        else if (string.Equals(browserRuntime.Provider, "ultralight", StringComparison.OrdinalIgnoreCase))
        {
            string[] requiredFiles =
            {
                "Ludots.UI.Browser.Ultralight.deps.json",
                "Ludots.UI.Browser.Ultralight.dll",
                "UltralightNet.dll",
                "UltralightNet.Binaries.dll",
                "UltralightNet.AppCore.dll",
                "UltralightNet.AppCore.Binaries.dll"
            };

            foreach (string file in requiredFiles)
            {
                string path = Path.Combine(browserRuntime.RuntimeRootPath, file);
                if (!File.Exists(path))
                {
                    message = $"Ultralight browser runtime package is incomplete. Missing: {path}";
                    return false;
                }
            }

            bool hasLinux = File.Exists(Path.Combine(browserRuntime.RuntimeRootPath, "libUltralight.so")) ||
                            File.Exists(Path.Combine(browserRuntime.RuntimeRootPath, "runtimes", "linux-x64", "native", "libUltralight.so"));
            bool hasWindows = File.Exists(Path.Combine(browserRuntime.RuntimeRootPath, "Ultralight.dll")) ||
                              File.Exists(Path.Combine(browserRuntime.RuntimeRootPath, "runtimes", "win-x64", "native", "Ultralight.dll"));
            bool hasMac = File.Exists(Path.Combine(browserRuntime.RuntimeRootPath, "libUltralight.dylib")) ||
                          File.Exists(Path.Combine(browserRuntime.RuntimeRootPath, "runtimes", "osx-x64", "native", "libUltralight.dylib"));
            if (!hasLinux && !hasWindows && !hasMac)
            {
                message =
                    $"Ultralight browser runtime package is incomplete. Missing native Ultralight libraries under '{browserRuntime.RuntimeRootPath}'.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private async Task<IReadOnlyList<LauncherBuildResult>> BuildPlannedModsAsync(
        LauncherLaunchPlan plan,
        LauncherConfig config,
        CancellationToken ct)
    {
        var catalog = BuildCatalog(config);
        var sources = LauncherWorkspaceSourceResolver.ResolveSources(_repoRoot, config);
        var plannedEntries = plan.Mods
            .Select(mod =>
            {
                if (catalog.ByRootPath.TryGetValue(mod.RootPath, out var entry))
                {
                    return entry;
                }

                var manifestPath = Path.Combine(mod.RootPath, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    throw new InvalidOperationException($"Catalog entry not found for {mod.RootPath}");
                }

                var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                return CreateCatalogEntry(config, sources, mod.RootPath, manifest);
            })
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
        var build = await RunDotnetAsync(
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
    <TargetFramework>{RuntimeTargetFramework}</TargetFramework>
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
        var graphPath = WriteLaunchGraphDocument(plan);
        Directory.CreateDirectory(plan.AppOutputDirectory);
        var graphRelativePath = Path.GetRelativePath(plan.AppOutputDirectory, graphPath).Replace('\\', '/');
        var json = JsonSerializer.Serialize(new
        {
            LaunchGraphPath = graphRelativePath,
            LaunchGraphFullPath = graphPath,
            PlanSelectors = plan.Selectors,
            PlanRootModIds = plan.RootModIds,
            PlanOrderedModIds = plan.OrderedModIds,
            PlanFingerprint = plan.PlanFingerprint,
            PlanSchemaVersion = plan.SchemaVersion,
            PlanGeneratedAtUtc = plan.GeneratedAtUtc,
            BrowserRuntime = plan.BrowserRuntime
        }, BootstrapJsonWriteOptions);
        File.WriteAllText(plan.BootstrapArtifactPath, json);
        return plan.BootstrapArtifactPath;
    }

    private string WriteLaunchGraphDocument(LauncherLaunchPlan plan)
    {
        var document = new LauncherGraphDocument(
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
                plan.BootstrapArtifactStrategy,
                plan.BootstrapArtifactPath,
                plan.GraphArtifactPath,
                plan.AppOutputDirectory,
                plan.AppAssemblyPath,
                plan.LaunchUrl),
            plan.BrowserRuntime,
            plan.Diagnostics);
        var directory = Path.GetDirectoryName(plan.GraphArtifactPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(plan.GraphArtifactPath, JsonSerializer.Serialize(document, GraphJsonWriteOptions));
        return plan.GraphArtifactPath;
    }

    private void ReplacePreviousActiveProcess(LauncherLaunchPlan plan)
    {
        var recordPath = GetActiveProcessRecordPath(plan.AdapterId);
        var record = ReadActiveProcessRecord(recordPath);
        if (record == null)
        {
            return;
        }

        if (!PathsEqual(record.AppAssemblyPath, plan.AppAssemblyPath))
        {
            DeleteActiveProcessRecord(recordPath);
            return;
        }

        try
        {
            using var process = Process.GetProcessById(record.Pid);
            if (process.HasExited || !StartTimeMatches(process, record.StartedAtUtcTicks))
            {
                DeleteActiveProcessRecord(recordPath);
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
        finally
        {
            DeleteActiveProcessRecord(recordPath);
        }
    }

    private void PersistActiveProcess(LauncherLaunchPlan plan, string bootstrapPath, Process process)
    {
        var record = new ActiveLaunchProcessRecord(
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            plan.AdapterId,
            Path.GetFullPath(plan.AppAssemblyPath),
            Path.GetFullPath(bootstrapPath));
        var recordPath = GetActiveProcessRecordPath(plan.AdapterId);
        var directory = Path.GetDirectoryName(recordPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(recordPath, json);
    }

    private static ActiveLaunchProcessRecord? ReadActiveProcessRecord(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ActiveLaunchProcessRecord>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteActiveProcessRecord(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static bool StartTimeMatches(Process process, long startedAtUtcTicks)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks == startedAtUtcTicks;
        }
        catch
        {
            return false;
        }
    }

    private static string GetActiveProcessRecordPath(string adapterId)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var safeAdapterId = string.IsNullOrWhiteSpace(adapterId)
            ? "default"
            : string.Concat(adapterId.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeAdapterId))
        {
            safeAdapterId = "default";
        }

        return Path.Combine(appData, "Ludots", "Launcher", "active-processes", $"{safeAdapterId}.json");
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

    private static bool IsSameOrChildPath(string parentPath, string candidatePath)
    {
        string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        return string.Equals(normalizedParent, normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
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

    internal static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutMs,
        int outputDrainTimeoutMs = 5_000)
    {
        if (outputDrainTimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputDrainTimeoutMs), outputDrainTimeoutMs, "Output drain timeout must be positive.");
        }

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
        var processExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => processExited.TrySetResult(true);
        process.EnableRaisingEvents = true;
        if (process.HasExited)
        {
            processExited.TrySetResult(true);
        }

        var outputGate = new object();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                stdoutClosed.TrySetResult(true);
                return;
            }

            lock (outputGate)
            {
                stdout.AppendLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                stderrClosed.TrySetResult(true);
                return;
            }

            lock (outputGate)
            {
                stderr.AppendLine(eventArgs.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = new CancellationTokenSource(timeoutMs);
        bool timedOut = false;
        bool timedOutProcessTerminated = false;
        var cleanupFailures = new List<string>();
        try
        {
            await processExited.Task.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await processExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
                timedOutProcessTerminated = process.HasExited;
            }
            catch (Exception ex)
            {
                if (process.HasExited)
                {
                    timedOutProcessTerminated = true;
                }
                else
                {
                    cleanupFailures.Add(
                        $"[launcher] Timed-out process could not be confirmed stopped: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        bool outputClosed = false;
        try
        {
            await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task).WaitAsync(TimeSpan.FromMilliseconds(outputDrainTimeoutMs));
            outputClosed = true;
        }
        catch (TimeoutException)
        {
            try
            {
                process.CancelOutputRead();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(
                    $"[launcher] Failed to cancel stdout capture after drain timeout: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                process.CancelErrorRead();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(
                    $"[launcher] Failed to cancel stderr capture after drain timeout: {ex.GetType().Name}: {ex.Message}");
            }
        }

        string capturedStdout;
        string capturedStderr;
        lock (outputGate)
        {
            capturedStdout = stdout.ToString().TrimEnd();
            capturedStderr = stderr.ToString().TrimEnd();
        }

        var outputParts = new List<string>(3);
        if (timedOut)
        {
            outputParts.Add($"Process timed out after {timeoutMs} ms.");
            if (!timedOutProcessTerminated)
            {
                outputParts.Add("[launcher] Timed-out process did not confirm termination; build outputs may still be changing.");
            }
        }

        if (!string.IsNullOrWhiteSpace(capturedStdout))
        {
            outputParts.Add(capturedStdout);
        }

        if (!string.IsNullOrWhiteSpace(capturedStderr))
        {
            outputParts.Add(capturedStderr);
        }

        if (!outputClosed)
        {
            outputParts.Add($"[launcher] Redirected output remained open for {outputDrainTimeoutMs} ms after process exit; capture was stopped explicitly.");
        }

        if (cleanupFailures.Count > 0)
        {
            outputParts.AddRange(cleanupFailures);
        }

        int exitCode = timedOut
            ? (timedOutProcessTerminated && cleanupFailures.Count == 0 ? -1 : -2)
            : process.ExitCode;
        return (exitCode, string.Join(Environment.NewLine, outputParts));
    }

    private static string ResolveDotnetCommand()
    {
        if (IsDotnetExecutable(Environment.ProcessPath))
        {
            return Environment.ProcessPath!;
        }

        try
        {
            var currentProcessPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (IsDotnetExecutable(currentProcessPath))
            {
                return currentProcessPath!;
            }
        }
        catch
        {
        }

        return "dotnet";
    }

    private static bool IsDotnetExecutable(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<(int ExitCode, string Output)> RunDotnetAsync(string arguments, string workingDirectory, int timeoutMs)
    {
        return RunProcessAsync(ResolveDotnetCommand(), arguments, workingDirectory, timeoutMs);
    }

    private string GetLudotsToolProjectPath()
    {
        return Path.Combine(_repoRoot, "src", "Tools", "Ludots.Tool", "Ludots.Tool.csproj");
    }

    private string GetLudotsToolAssemblyPath()
    {
        return Path.Combine(_repoRoot, "src", "Tools", "Ludots.Tool", "bin", "Release", RuntimeTargetFramework, "Ludots.Tool.dll");
    }

    private async Task<(int ExitCode, string Output)> RunLudotsToolAsync(string arguments, int timeoutMs)
    {
        var toolProjectPath = GetLudotsToolProjectPath();
        var output = new StringBuilder();
        var build = await RunDotnetAsync(
            $"build \"{toolProjectPath}\" -c Release -nologo -clp:ErrorsOnly",
            _repoRoot,
            timeoutMs);
        if (!string.IsNullOrWhiteSpace(build.Output))
        {
            output.AppendLine(build.Output);
        }

        if (build.ExitCode != 0)
        {
            return (build.ExitCode, output.ToString());
        }

        var toolAssemblyPath = GetLudotsToolAssemblyPath();
        if (!File.Exists(toolAssemblyPath))
        {
            output.AppendLine($"Ludots.Tool.dll missing after build: {toolAssemblyPath}");
            return (1, output.ToString());
        }

        var run = await RunDotnetAsync(
            $"exec --roll-forward Major \"{toolAssemblyPath}\" {arguments}",
            _repoRoot,
            timeoutMs);
        if (!string.IsNullOrWhiteSpace(run.Output))
        {
            output.AppendLine(run.Output);
        }

        return (run.ExitCode, output.ToString());
    }

    private static Task<(int ExitCode, string Output)> RunNodePackageCommandAsync(string arguments, string workingDirectory, int timeoutMs)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunProcessAsync("cmd.exe", $"/c npm {arguments}", workingDirectory, timeoutMs);
        }

        return RunProcessAsync("npm", arguments, workingDirectory, timeoutMs);
    }

    private sealed record PlanFingerprintModPayload(
        string Id,
        string RootPath,
        string ProjectPath,
        string MainAssemblyPath,
        string Kind,
        IReadOnlyList<string> BindingNames);

    private sealed record PlanFingerprintPayload(
        int SchemaVersion,
        LauncherAdapterDescriptor Adapter,
        string BuildMode,
        IReadOnlyList<string> Selectors,
        IReadOnlyList<string> RootModIds,
        IReadOnlyList<string> OrderedModIds,
        IReadOnlyList<PlanFingerprintModPayload> PlannedMods,
        string BootstrapArtifactStrategy,
        string BootstrapArtifactPath,
        string GraphArtifactPath,
        string AppOutputDirectory,
        string AppAssemblyPath,
        string LaunchUrl,
        BrowserRuntimeConfig? BrowserRuntime,
        bool IsExecutableTarget,
        string ExecutableProjectPath,
        IReadOnlyList<string>? ExecutableArgs);

    private sealed class PlanResolutionState
    {
        public List<ExecutableTargetCandidate> ExecutableTargets { get; } = new();
        public List<string>? PresetArgs { get; set; }
    }

    private sealed record ExecutableTargetCandidate(string ProjectPath, List<string>? BindingArgs);

    private sealed record CatalogEntry(LauncherModInfo Info, ModManifest Manifest);
    private sealed record GameConfigFragment(string Source, string? OwnerModId, bool IsRootSelection, JsonObject Content);
    private sealed record CatalogIndex(
        IReadOnlyList<CatalogEntry> Entries,
        IReadOnlyDictionary<string, CatalogEntry> ByRootPath,
        IReadOnlyDictionary<string, List<CatalogEntry>> ById,
        IReadOnlyDictionary<string, LauncherBinding> BindingsByName);
}
