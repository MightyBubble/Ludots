using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Modding.Workspace
{
    /// <summary>
    /// A game preset is a named game.*.json file that defines a mod combination and launch config.
    /// Discovered from the Raylib app directory or any configured directory.
    /// </summary>
    public sealed class GamePreset
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("windowTitle")]
        public string WindowTitle { get; set; } = "";

        [JsonPropertyName("modPaths")]
        public List<string> ModPaths { get; set; } = new();

        /// <summary>
        /// Discover all game.*.json files in a directory.
        /// Returns presets sorted by id.
        /// </summary>
        public static List<GamePreset> DiscoverPresets(string directory)
        {
            var presets = new List<GamePreset>();
            if (!Directory.Exists(directory)) return presets;

            foreach (var file in Directory.GetFiles(directory, "game.*.json"))
            {
                var preset = TryLoad(file);
                if (preset != null) presets.Add(preset);
            }

            var defaultFile = Path.Combine(directory, "game.json");
            if (File.Exists(defaultFile))
            {
                var preset = TryLoad(defaultFile);
                if (preset != null)
                {
                    preset.Id = "default";
                    presets.Insert(0, preset);
                }
            }

            presets.Sort((a, b) => string.Compare(a.Id, b.Id, System.StringComparison.OrdinalIgnoreCase));
            return presets;
        }

        private static GamePreset? TryLoad(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var preset = new GamePreset
                {
                    FilePath = Path.GetFullPath(filePath),
                    Id = ExtractPresetId(filePath)
                };

                if (root.TryGetProperty("WindowTitle", out var title) && title.ValueKind == JsonValueKind.String)
                    preset.WindowTitle = title.GetString() ?? "";

                if (root.TryGetProperty("ModPaths", out var paths) && paths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in paths.EnumerateArray())
                    {
                        if (p.ValueKind == JsonValueKind.String)
                            preset.ModPaths.Add(p.GetString() ?? "");
                    }
                }

                return preset;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractPresetId(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.StartsWith("game.", System.StringComparison.OrdinalIgnoreCase) && fileName.Length > 5)
                return fileName.Substring(5);
            if (string.Equals(fileName, "game", System.StringComparison.OrdinalIgnoreCase))
                return "default";
            return fileName;
        }
    }
}
