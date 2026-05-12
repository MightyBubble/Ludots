using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

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
            if (string.IsNullOrWhiteSpace(directory))
                throw new System.ArgumentException("Preset directory is required.", nameof(directory));
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Preset directory not found: {Path.GetFullPath(directory)}");

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith("game.", System.StringComparison.Ordinal) &&
                    fileName.EndsWith(".json", System.StringComparison.Ordinal) &&
                    fileName.Length > "game..json".Length)
                {
                    presets.Add(Load(file, ExtractPresetId(file)));
                }
            }

            if (TryGetExactChildFile(directory, "game.json", out var defaultFile))
            {
                presets.Insert(0, Load(defaultFile, "default"));
            }

            presets.Sort((a, b) => string.Compare(a.Id, b.Id, System.StringComparison.Ordinal));
            return presets;
        }

        private static GamePreset Load(string filePath, string id)
        {
            var fullPath = Path.GetFullPath(filePath);
            var json = File.ReadAllText(fullPath);
            var contract = JsonSerializer.Deserialize<PresetContract>(json, SerializerOptions)
                ?? throw new InvalidDataException($"Failed to deserialize game preset: {fullPath}");

            if (contract.ModPaths == null)
            {
                throw new InvalidDataException($"Game preset must declare ModPaths: {fullPath}");
            }

            return new GamePreset
            {
                FilePath = fullPath,
                Id = id,
                WindowTitle = contract.WindowTitle ?? "",
                ModPaths = contract.ModPaths
            };
        }

        private static string ExtractPresetId(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.StartsWith("game.", System.StringComparison.Ordinal) && fileName.Length > 5)
                return fileName.Substring(5);
            if (string.Equals(fileName, "game", System.StringComparison.Ordinal))
                return "default";
            return fileName;
        }

        private static bool TryGetExactChildFile(string directory, string fileName, out string fullPath)
        {
            foreach (var candidate in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(candidate), fileName, System.StringComparison.Ordinal))
                {
                    fullPath = Path.GetFullPath(candidate);
                    return true;
                }
            }

            fullPath = string.Empty;
            return false;
        }

        private sealed class PresetContract
        {
            public string? WindowTitle { get; set; }
            public List<string>? ModPaths { get; set; }
        }

        private static readonly JsonSerializerOptions SerializerOptions = StrictJsonOptions.CreateExact();
    }
}
