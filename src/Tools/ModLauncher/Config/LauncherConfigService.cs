using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.ModLauncher.Config
{
    public sealed class LauncherConfigService
    {
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ConfigPath { get; }

        public LauncherConfigService(string? configPath = null)
        {
            ConfigPath = string.IsNullOrWhiteSpace(configPath) ? GetDefaultConfigPath() : Path.GetFullPath(configPath);
        }

        public ModLauncherConfig LoadOrDefault()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new ModLauncherConfig();
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<ModLauncherConfig>(json, _options);
                return cfg ?? new ModLauncherConfig();
            }
            catch
            {
                return new ModLauncherConfig();
            }
        }

        public void Save(ModLauncherConfig config)
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(config ?? new ModLauncherConfig(), _options);
            File.WriteAllText(ConfigPath, json);
        }

        private static string GetDefaultConfigPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Ludots", "ModLauncher", "config.json");
        }
    }
}

