using System.Text.Json;

namespace Ludots.Launcher.Backend;

public sealed class LauncherConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public LauncherConfigService(string? configPath = null)
    {
        ConfigPath = string.IsNullOrWhiteSpace(configPath) ? GetDefaultConfigPath() : Path.GetFullPath(configPath);
    }

    public string ConfigPath { get; }

    public LauncherConfig LoadOrDefault()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new LauncherConfig();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<LauncherConfig>(json, SerializerOptions) ?? new LauncherConfig();
        }
        catch
        {
            return new LauncherConfig();
        }
    }

    public void Save(LauncherConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config ?? new LauncherConfig(), SerializerOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Ludots", "Launcher", "config.json");
    }
}
