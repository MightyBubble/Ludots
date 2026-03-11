using System.Text.Json;

namespace Ludots.Launcher.Backend;

public sealed class LauncherConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public LauncherConfigService(
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

        RepoRoot = Path.GetFullPath(repoRoot);
        RepoConfigPath = ResolveRepoPath(configPath, "launcher.config.json");
        RepoPresetsPath = ResolveRepoPath(presetsPath, "launcher.presets.json");
        UserConfigPath = string.IsNullOrWhiteSpace(userConfigPath)
            ? Path.Combine(GetUserLauncherDirectory(), "config.overlay.json")
            : Path.GetFullPath(userConfigPath);
        PreferencesPath = string.IsNullOrWhiteSpace(preferencesPath)
            ? Path.Combine(GetUserLauncherDirectory(), "preferences.json")
            : Path.GetFullPath(preferencesPath);
    }

    public string RepoRoot { get; }
    public string RepoConfigPath { get; }
    public string RepoPresetsPath { get; }
    public string UserConfigPath { get; }
    public string PreferencesPath { get; }

    public LauncherConfig LoadOrDefault()
    {
        return LoadMergedConfig();
    }

    public LauncherConfig LoadMergedConfig()
    {
        var repoConfig = EnsureWorkspaceDefaults(ReadJsonFile<LauncherConfig>(RepoConfigPath));
        var userOverlay = EnsureWorkspaceDefaults(ReadJsonFile<LauncherConfig>(UserConfigPath), injectDefaults: false);
        return MergeWorkspaceConfig(repoConfig, userOverlay);
    }

    public LauncherConfig LoadRepoConfig()
    {
        return EnsureWorkspaceDefaults(ReadJsonFile<LauncherConfig>(RepoConfigPath));
    }

    public LauncherPresetDocument LoadPresets()
    {
        var document = ReadJsonFile<LauncherPresetDocument>(RepoPresetsPath);
        document.Presets ??= new List<LauncherPresetDefinition>();
        return document;
    }

    public LauncherPreferences LoadPreferences()
    {
        return ReadJsonFile<LauncherPreferences>(PreferencesPath);
    }

    public void SaveRepoConfig(LauncherConfig config)
    {
        WriteJsonFile(RepoConfigPath, EnsureWorkspaceDefaults(config));
    }

    public void SavePresets(LauncherPresetDocument presets)
    {
        presets ??= new LauncherPresetDocument();
        presets.Presets ??= new List<LauncherPresetDefinition>();
        WriteJsonFile(RepoPresetsPath, presets);
    }

    public void SavePreferences(LauncherPreferences preferences)
    {
        WriteJsonFile(PreferencesPath, preferences ?? new LauncherPreferences());
    }

    public void SaveUserConfig(LauncherConfig config)
    {
        WriteJsonFile(UserConfigPath, EnsureWorkspaceDefaults(config, injectDefaults: false));
    }

    private string ResolveRepoPath(string? path, string defaultFileName)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.Combine(RepoRoot, defaultFileName);
    }

    private static string GetUserLauncherDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Ludots", "Launcher");
    }

    private static T ReadJsonFile<T>(string path)
        where T : new()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new T();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    private static void WriteJsonFile<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(value, SerializerOptions));
    }

    private LauncherConfig EnsureWorkspaceDefaults(LauncherConfig? config, bool injectDefaults = true)
    {
        config ??= new LauncherConfig();
        config.ScanRoots ??= new List<LauncherScanRoot>();
        config.Bindings ??= new List<LauncherBinding>();
        config.Adapters ??= new LauncherAdapterDefaults();
        config.ProjectHints ??= new List<LauncherProjectHint>();

        if (injectDefaults && !config.ScanRoots.Any(root => string.Equals(root.Id, "repo_mods", StringComparison.OrdinalIgnoreCase)))
        {
            config.ScanRoots.Insert(0, new LauncherScanRoot
            {
                Id = "repo_mods",
                Path = "mods",
                ScanMode = "recursive",
                Enabled = true
            });
        }

        if (string.IsNullOrWhiteSpace(config.Adapters.Default))
        {
            config.Adapters.Default = LauncherPlatformIds.Raylib;
        }

        return config;
    }

    private static LauncherConfig MergeWorkspaceConfig(LauncherConfig repoConfig, LauncherConfig userOverlay)
    {
        var merged = new LauncherConfig
        {
            SchemaVersion = Math.Max(repoConfig.SchemaVersion, userOverlay.SchemaVersion),
            Adapters = new LauncherAdapterDefaults
            {
                Default = string.IsNullOrWhiteSpace(userOverlay.Adapters?.Default)
                    ? repoConfig.Adapters?.Default ?? LauncherPlatformIds.Raylib
                    : userOverlay.Adapters.Default
            }
        };

        foreach (var root in MergeByKey(
                     repoConfig.ScanRoots,
                     userOverlay.ScanRoots,
                     root => string.IsNullOrWhiteSpace(root.Id) ? root.Path : root.Id))
        {
            merged.ScanRoots.Add(root);
        }

        foreach (var binding in MergeByKey(
                     repoConfig.Bindings,
                     userOverlay.Bindings,
                     binding => binding.Name))
        {
            merged.Bindings.Add(binding);
        }

        foreach (var hint in MergeByKey(
                     repoConfig.ProjectHints,
                     userOverlay.ProjectHints,
                     BuildProjectHintKey))
        {
            merged.ProjectHints.Add(hint);
        }

        return merged;
    }

    private static IEnumerable<T> MergeByKey<T>(
        IEnumerable<T>? baseItems,
        IEnumerable<T>? overlayItems,
        Func<T, string> keySelector)
    {
        var merged = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in baseItems ?? Array.Empty<T>())
        {
            var key = keySelector(item);
            if (!string.IsNullOrWhiteSpace(key))
            {
                merged[key] = item;
            }
        }

        foreach (var item in overlayItems ?? Array.Empty<T>())
        {
            var key = keySelector(item);
            if (!string.IsNullOrWhiteSpace(key))
            {
                merged[key] = item;
            }
        }

        return merged.Values;
    }

    private static string BuildProjectHintKey(LauncherProjectHint hint)
    {
        if (!string.IsNullOrWhiteSpace(hint.ModId))
        {
            return $"mod:{hint.ModId}";
        }

        if (!string.IsNullOrWhiteSpace(hint.RootPath))
        {
            return $"path:{hint.RootPath}";
        }

        return $"project:{hint.ProjectPath}";
    }
}
