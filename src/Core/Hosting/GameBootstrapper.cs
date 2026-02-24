using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;

namespace Ludots.Core.Hosting
{
    public readonly record struct GameBootstrapResult(GameEngine Engine, GameConfig Config, string AssetsRoot);

    /// <summary>
    /// App-level game.json - only contains ModPaths for bootstrap.
    /// All actual game configuration comes from ConfigPipeline merge.
    /// </summary>
    public class AppBootstrapConfig
    {
        public List<string> ModPaths { get; set; } = new List<string>();
    }

    public static class GameBootstrapper
    {
        public static GameBootstrapResult InitializeFromBaseDirectory(string baseDirectory)
        {
            return InitializeFromBaseDirectory(baseDirectory, "game.json");
        }

        /// <summary>
        /// New initialization flow using ConfigPipeline for game.json merge:
        /// 1. Read App's game.json for ModPaths only
        /// 2. Initialize VFS and ModLoader
        /// 3. Use ConfigPipeline to merge all game.json files (Core -> Mods)
        /// 4. Pass merged config to GameEngine
        /// </summary>
        public static GameBootstrapResult InitializeFromBaseDirectory(string baseDirectory, string gameConfigFile)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory is required.", nameof(baseDirectory));

            var baseDir = Path.GetFullPath(baseDirectory);
            var assetsRoot = FindAssetsRootStrict(baseDir);

            // Step 1: Read App's game.json for ModPaths only
            string gameJsonPath = Path.IsPathRooted(gameConfigFile)
                ? Path.GetFullPath(gameConfigFile)
                : Path.Combine(baseDir, gameConfigFile);
            if (!File.Exists(gameJsonPath))
                throw new FileNotFoundException($"Missing game.json next to executable: {gameJsonPath}");

            AppBootstrapConfig bootstrapConfig;
            try
            {
                var json = File.ReadAllText(gameJsonPath);
                bootstrapConfig = JsonSerializer.Deserialize<AppBootstrapConfig>(json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse game.json: {ex.Message}", ex);
            }

            if (bootstrapConfig == null)
                throw new Exception("Failed to parse game.json: deserialized config is null.");

            if (bootstrapConfig.ModPaths == null)
                bootstrapConfig.ModPaths = new List<string>();

            // Resolve mod paths
            var resolvedModPaths = new List<string>();
            for (int i = 0; i < bootstrapConfig.ModPaths.Count; i++)
            {
                var raw = bootstrapConfig.ModPaths[i];
                if (string.IsNullOrWhiteSpace(raw))
                    throw new Exception($"Invalid game.json: ModPaths[{i}] is empty.");

                var resolved = Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw);
                resolved = Path.GetFullPath(resolved);

                if (!Directory.Exists(resolved))
                    throw new DirectoryNotFoundException($"Mod directory not found: {resolved}");

                var manifestPath = Path.Combine(resolved, "mod.json");
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException($"mod.json not found in mod directory: {resolved}");

                resolvedModPaths.Add(resolved);
            }

            // Step 2 & 3: Initialize engine with resolved mod paths
            // Engine will internally use ConfigPipeline to merge game.json
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(resolvedModPaths, assetsRoot);

            // Get the merged config from engine
            var mergedConfig = engine.MergedConfig;

            return new GameBootstrapResult(engine, mergedConfig, assetsRoot);
        }

        private static string FindAssetsRootStrict(string startPath)
        {
            var current = Path.GetFullPath(startPath);
            while (!Directory.Exists(Path.Combine(current, "assets")))
            {
                var parent = Directory.GetParent(current);
                if (parent == null)
                    throw new DirectoryNotFoundException($"Could not locate 'assets' directory starting from: {startPath}");
                current = parent.FullName;
            }
            return Path.Combine(current, "assets");
        }
    }
}
