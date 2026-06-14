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
    /// App-level launcher bootstrap.
    /// Launch graph metadata is the runtime SSOT.
    /// All actual game configuration comes from ConfigPipeline merge.
    /// </summary>
    public class AppBootstrapConfig
    {
        public string? LaunchGraphPath { get; set; }
        public string? LaunchGraphFullPath { get; set; }
        public string? PlanFingerprint { get; set; }
        public int? PlanSchemaVersion { get; set; }
        public string? PlanGeneratedAtUtc { get; set; }
    }

    internal sealed class AppLaunchGraphConfig
    {
        public int SchemaVersion { get; set; }
        public string? GeneratedAtUtc { get; set; }
        public string? PlanFingerprint { get; set; }
        public List<string> OrderedModIds { get; set; } = new List<string>();
        public List<AppLaunchGraphMod> PlannedMods { get; set; } = new List<AppLaunchGraphMod>();
    }

    internal sealed class AppLaunchGraphMod
    {
        public string? Id { get; set; }
        public string? RootPath { get; set; }
    }

    public static class GameBootstrapper
    {
        private static readonly JsonSerializerOptions BootstrapJsonOptions = StrictJsonOptions.CreateExact();
        private static readonly JsonSerializerOptions LaunchGraphJsonOptions = StrictJsonOptions.CreateCamelCase();

        public static GameBootstrapResult InitializeFromBaseDirectory(string baseDirectory)
        {
            return InitializeFromBaseDirectory(baseDirectory, "launcher.runtime.json");
        }

        /// <summary>
        /// New initialization flow using ConfigPipeline for game.json merge:
        /// 1. Read app bootstrap for launch graph metadata
        /// 2. Initialize VFS and ModLoader from the resolved launch plan
        /// 3. Use ConfigPipeline to merge all game.json files (Core -> Mods)
        /// 4. Pass merged config to GameEngine
        /// </summary>
        public static GameBootstrapResult InitializeFromBaseDirectory(string baseDirectory, string gameConfigFile)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory is required.", nameof(baseDirectory));

            var baseDir = Path.GetFullPath(baseDirectory);
            var assetsRoot = FindAssetsRootStrict(baseDir);

            // Step 1: Read the launcher bootstrap and resolve the launcher graph.
            string gameJsonPath = Path.IsPathRooted(gameConfigFile)
                ? Path.GetFullPath(gameConfigFile)
                : Path.Combine(baseDir, gameConfigFile);
            if (!File.Exists(gameJsonPath))
                throw new FileNotFoundException($"Missing launcher bootstrap next to executable: {gameJsonPath}");

            AppBootstrapConfig bootstrapConfig;
            try
            {
                var json = File.ReadAllText(gameJsonPath);
                bootstrapConfig = JsonSerializer.Deserialize<AppBootstrapConfig>(json, BootstrapJsonOptions);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse launcher bootstrap: {ex.Message}", ex);
            }

            if (bootstrapConfig == null)
                throw new Exception("Failed to parse launcher bootstrap: deserialized config is null.");

            string graphPath = ResolveRequiredGraphPath(baseDir, gameJsonPath, bootstrapConfig);
            var resolvedPlan = ResolveGraphPlan(graphPath, bootstrapConfig);

            // Step 2 & 3: Initialize engine with launcher-resolved plan
            // Engine will internally use ConfigPipeline to merge game.json
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(resolvedPlan, assetsRoot);

            // Get the merged config from engine
            var mergedConfig = engine.MergedConfig;

            return new GameBootstrapResult(engine, mergedConfig, assetsRoot);
        }

        private static string ResolveRequiredGraphPath(string baseDir, string bootstrapPath, AppBootstrapConfig bootstrapConfig)
        {
            var candidate = !string.IsNullOrWhiteSpace(bootstrapConfig.LaunchGraphFullPath)
                ? bootstrapConfig.LaunchGraphFullPath
                : bootstrapConfig.LaunchGraphPath;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new InvalidOperationException(
                    $"Launcher bootstrap '{bootstrapPath}' is missing launch graph metadata. " +
                    "Product runtime bootstrap must point to a launcher-resolved graph artifact.");
            }

            var resolved = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bootstrapPath) ?? baseDir, candidate));
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Launch graph not found: {resolved}");
            }

            return resolved;
        }

        private static ResolvedModLoadPlan ResolveGraphPlan(string graphPath, AppBootstrapConfig bootstrapConfig)
        {
            AppLaunchGraphConfig? graphConfig;
            try
            {
                var json = File.ReadAllText(graphPath);
                graphConfig = JsonSerializer.Deserialize<AppLaunchGraphConfig>(json, LaunchGraphJsonOptions);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse launch graph: {ex.Message}", ex);
            }

            if (graphConfig == null)
            {
                throw new Exception("Failed to parse launch graph: deserialized graph is null.");
            }

            if (bootstrapConfig.PlanSchemaVersion.HasValue && graphConfig.SchemaVersion != bootstrapConfig.PlanSchemaVersion.Value)
            {
                throw new Exception(
                    $"Launch graph schema mismatch: bootstrap expected {bootstrapConfig.PlanSchemaVersion.Value}, graph was {graphConfig.SchemaVersion}.");
            }

            if (!string.IsNullOrWhiteSpace(bootstrapConfig.PlanFingerprint) &&
                !string.Equals(bootstrapConfig.PlanFingerprint, graphConfig.PlanFingerprint, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"Launch graph fingerprint mismatch: bootstrap expected {bootstrapConfig.PlanFingerprint}, graph was {graphConfig.PlanFingerprint ?? "<null>"}.");
            }

            if (graphConfig.PlannedMods == null || graphConfig.PlannedMods.Count == 0)
            {
                throw new Exception("Launch graph does not contain planned mods.");
            }

            if (graphConfig.OrderedModIds == null || graphConfig.OrderedModIds.Count == 0)
            {
                throw new Exception("Launch graph does not contain ordered mod ids.");
            }

            if (graphConfig.OrderedModIds.Count != graphConfig.PlannedMods.Count)
            {
                throw new Exception(
                    $"Launch graph is invalid: orderedModIds count ({graphConfig.OrderedModIds.Count}) does not match plannedMods count ({graphConfig.PlannedMods.Count}).");
            }

            var orderedMods = new List<ResolvedModLoadEntry>();
            var seenOrderedIds = new HashSet<string>(StringComparer.Ordinal);
            var seenRoots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < graphConfig.PlannedMods.Count; i++)
            {
                var mod = graphConfig.PlannedMods[i];
                if (string.IsNullOrWhiteSpace(mod.Id))
                    throw new Exception($"Invalid launch graph: plannedMods[{i}].id is empty.");

                if (!seenOrderedIds.Add(graphConfig.OrderedModIds[i]))
                    throw new Exception($"Duplicate launch graph ordered mod id detected: {graphConfig.OrderedModIds[i]}");

                if (!string.Equals(graphConfig.OrderedModIds[i], mod.Id, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Launch graph is invalid: orderedModIds[{i}]='{graphConfig.OrderedModIds[i]}' does not match plannedMods[{i}].id='{mod.Id}'.");
                }

                var raw = graphConfig.PlannedMods[i].RootPath;
                if (string.IsNullOrWhiteSpace(raw))
                    throw new Exception($"Invalid launch graph: plannedMods[{i}].rootPath is empty.");

                var resolved = Path.IsPathRooted(raw)
                    ? raw
                    : Path.Combine(Path.GetDirectoryName(graphPath) ?? AppContext.BaseDirectory, raw);
                resolved = Path.GetFullPath(resolved);

                if (!Directory.Exists(resolved))
                    throw new DirectoryNotFoundException($"Mod directory not found: {resolved}");

                var manifestPath = Path.Combine(resolved, "mod.json");
                if (!File.Exists(manifestPath))
                    throw new FileNotFoundException($"mod.json not found in mod directory: {resolved}");

                if (!seenRoots.Add(resolved))
                    throw new Exception($"Duplicate launch graph mod root detected: {resolved}");

                orderedMods.Add(new ResolvedModLoadEntry(mod.Id, resolved));
            }

            return new ResolvedModLoadPlan(
                orderedMods,
                graphConfig.SchemaVersion,
                graphConfig.PlanFingerprint,
                graphConfig.GeneratedAtUtc ?? bootstrapConfig.PlanGeneratedAtUtc,
                graphPath);
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
