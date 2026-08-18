using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

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
        public IReadOnlyList<string>? PlanSelectors { get; set; }
        public IReadOnlyList<string>? PlanRootModIds { get; set; }
        public IReadOnlyList<string>? PlanOrderedModIds { get; set; }
        public string? PlanFingerprint { get; set; }
        public int? PlanSchemaVersion { get; set; }
        public string? PlanGeneratedAtUtc { get; set; }
        public BrowserRuntimeConfig? BrowserRuntime { get; set; }
    }

    public static class GameBootstrapper
    {
        private static readonly JsonSerializerOptions BootstrapJsonOptions = StrictJsonOptions.CreateExact();
        private static readonly JsonSerializerOptions LaunchGraphJsonOptions = StrictJsonOptions.CreateCamelCase();

        private sealed record ResolvedBootstrapPlan(
            ResolvedModLoadPlan ModLoadPlan,
            BrowserRuntimeConfig? BrowserRuntime);

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
            var resolvedPlan = ResolveGraphPlan(graphPath, gameJsonPath, bootstrapConfig);

            // Step 2 & 3: Initialize engine with launcher-resolved plan
            // Engine will internally use ConfigPipeline to merge game.json
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(resolvedPlan.ModLoadPlan, assetsRoot);

            // Get the merged config from engine
            var mergedConfig = engine.MergedConfig;
            ApplyHostBrowserRuntimeConfig(engine, mergedConfig, resolvedPlan.BrowserRuntime);

            return new GameBootstrapResult(engine, mergedConfig, assetsRoot);
        }

        private static string ResolveRequiredGraphPath(string baseDir, string bootstrapPath, AppBootstrapConfig bootstrapConfig)
        {
            string? graphPath = null;
            if (!string.IsNullOrWhiteSpace(bootstrapConfig.LaunchGraphPath))
            {
                graphPath = ResolveBootstrapRelativePath(baseDir, bootstrapPath, bootstrapConfig.LaunchGraphPath);
            }

            string? fullGraphPath = null;
            if (!string.IsNullOrWhiteSpace(bootstrapConfig.LaunchGraphFullPath))
            {
                fullGraphPath = ResolveBootstrapRelativePath(baseDir, bootstrapPath, bootstrapConfig.LaunchGraphFullPath);
            }

            if (graphPath != null &&
                fullGraphPath != null &&
                !PathsEqual(graphPath, fullGraphPath))
            {
                throw new InvalidOperationException(
                    $"Launcher bootstrap '{bootstrapPath}' has conflicting launch graph pointers: " +
                    $"LaunchGraphPath resolved to '{graphPath}', LaunchGraphFullPath resolved to '{fullGraphPath}'.");
            }

            var resolved = fullGraphPath ?? graphPath;
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new InvalidOperationException(
                    $"Launcher bootstrap '{bootstrapPath}' is missing launch graph metadata. " +
                    "Product runtime bootstrap must point to a launcher-resolved graph artifact.");
            }

            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Launch graph not found: {resolved}");
            }

            return resolved;
        }

        private static string ResolveBootstrapRelativePath(string baseDir, string bootstrapPath, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new InvalidOperationException(
                    $"Launcher bootstrap '{bootstrapPath}' is missing launch graph metadata. " +
                    "Product runtime bootstrap must point to a launcher-resolved graph artifact.");
            }

            return Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bootstrapPath) ?? baseDir, candidate));
        }

        private static ResolvedBootstrapPlan ResolveGraphPlan(string graphPath, string bootstrapPath, AppBootstrapConfig bootstrapConfig)
        {
            LauncherGraphDocument? graphConfig;
            try
            {
                var json = File.ReadAllText(graphPath);
                graphConfig = JsonSerializer.Deserialize<LauncherGraphDocument>(json, LaunchGraphJsonOptions);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse launch graph: {ex.Message}", ex);
            }

            if (graphConfig == null)
            {
                throw new Exception("Failed to parse launch graph: deserialized graph is null.");
            }

            ValidateGraphArtifactFreshness(graphPath, bootstrapPath, graphConfig);

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

            ValidateBootstrapPlanFreshness(
                bootstrapConfig,
                graphConfig,
                requiresPlanMetadata: graphConfig.RuntimeArtifacts != null);

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

            LaunchModInjection.Apply(orderedMods, graphPath);

            BrowserRuntimeConfig? browserRuntime = ResolveBrowserRuntimeConfig(bootstrapConfig, graphConfig);
            var modLoadPlan = new ResolvedModLoadPlan(
                orderedMods,
                graphConfig.SchemaVersion,
                graphConfig.PlanFingerprint,
                graphConfig.GeneratedAtUtc ?? bootstrapConfig.PlanGeneratedAtUtc,
                graphPath);
            return new ResolvedBootstrapPlan(modLoadPlan, browserRuntime);
        }

        private static void ApplyHostBrowserRuntimeConfig(
            GameEngine engine,
            GameConfig mergedConfig,
            BrowserRuntimeConfig? browserRuntime)
        {
            if (browserRuntime == null)
            {
                return;
            }

            mergedConfig.BrowserRuntime = browserRuntime;
            engine.SetService(CoreServiceKeys.GameConfig, mergedConfig);
        }

        private static BrowserRuntimeConfig? ResolveBrowserRuntimeConfig(
            AppBootstrapConfig bootstrapConfig,
            LauncherGraphDocument graphConfig)
        {
            BrowserRuntimeConfig? bootstrapRuntime = bootstrapConfig.BrowserRuntime;
            BrowserRuntimeConfig? graphRuntime = graphConfig.BrowserRuntime;
            if (bootstrapRuntime != null && graphRuntime != null && !BrowserRuntimeConfigsEqual(bootstrapRuntime, graphRuntime))
            {
                throw new InvalidOperationException(
                    "Launcher bootstrap browserRuntime does not match the selected launch graph browserRuntime.");
            }

            return graphRuntime ?? bootstrapRuntime;
        }

        private static bool BrowserRuntimeConfigsEqual(BrowserRuntimeConfig left, BrowserRuntimeConfig right)
        {
            return left.Enabled == right.Enabled &&
                left.Required == right.Required &&
                string.Equals(left.Provider, right.Provider, StringComparison.Ordinal) &&
                string.Equals(left.ProviderAssemblyPath, right.ProviderAssemblyPath, StringComparison.Ordinal) &&
                string.Equals(left.ProviderHostTypeName, right.ProviderHostTypeName, StringComparison.Ordinal) &&
                string.Equals(left.ProviderProjectPath, right.ProviderProjectPath, StringComparison.Ordinal) &&
                string.Equals(left.RuntimeRootPath, right.RuntimeRootPath, StringComparison.Ordinal) &&
                string.Equals(left.CacheRootPath, right.CacheRootPath, StringComparison.Ordinal) &&
                left.UseCollectibleLoadContext == right.UseCollectibleLoadContext &&
                BrowserRuntimeStringArraysEqual(left.ProcessSharedAssemblyNamePrefixes, right.ProcessSharedAssemblyNamePrefixes);
        }

        private static bool BrowserRuntimeStringArraysEqual(string[]? left, string[]? right)
        {
            left ??= Array.Empty<string>();
            right ??= Array.Empty<string>();
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateGraphArtifactFreshness(string graphPath, string bootstrapPath, LauncherGraphDocument graphConfig)
        {
            if (graphConfig.RuntimeArtifacts == null)
            {
                return;
            }

            ValidateRuntimeArtifactPath(
                "graphArtifactPath",
                graphConfig.RuntimeArtifacts.GraphArtifactPath,
                graphPath,
                graphPath);
            ValidateRuntimeArtifactPath(
                "bootstrapArtifactPath",
                graphConfig.RuntimeArtifacts.BootstrapArtifactPath,
                graphPath,
                bootstrapPath);
        }

        private static void ValidateRuntimeArtifactPath(
            string fieldName,
            string artifactPath,
            string graphPath,
            string actualPath)
        {
            if (string.IsNullOrWhiteSpace(artifactPath))
            {
                throw new InvalidOperationException(
                    $"Launch graph runtimeArtifacts.{fieldName} is required for freshness validation.");
            }

            string resolvedArtifactPath = Path.IsPathRooted(artifactPath)
                ? Path.GetFullPath(artifactPath)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(graphPath) ?? AppContext.BaseDirectory, artifactPath));
            if (!PathsEqual(resolvedArtifactPath, actualPath))
            {
                throw new InvalidOperationException(
                    $"Stale launch graph artifact rejected: runtimeArtifacts.{fieldName}='{resolvedArtifactPath}' " +
                    $"does not match the selected runtime artifact '{Path.GetFullPath(actualPath)}'.");
            }
        }

        private static void ValidateBootstrapPlanFreshness(
            AppBootstrapConfig bootstrapConfig,
            LauncherGraphDocument graphConfig,
            bool requiresPlanMetadata)
        {
            ValidatePlanList("selectors", bootstrapConfig.PlanSelectors, graphConfig.Selectors, requiresPlanMetadata);
            ValidatePlanList("rootModIds", bootstrapConfig.PlanRootModIds, graphConfig.RootModIds, requiresPlanMetadata);
            ValidatePlanList("orderedModIds", bootstrapConfig.PlanOrderedModIds, graphConfig.OrderedModIds, requiresPlanMetadata);
        }

        private static void ValidatePlanList(
            string fieldName,
            IReadOnlyList<string>? expected,
            IReadOnlyList<string>? actual,
            bool required)
        {
            if (expected == null)
            {
                if (required)
                {
                    throw new InvalidOperationException(
                        $"Launcher bootstrap is missing plan freshness metadata: Plan{ToPascalCase(fieldName)} is required when the launch graph declares runtimeArtifacts.");
                }

                return;
            }

            if (actual == null)
            {
                throw new InvalidOperationException(
                    $"Stale launch graph rejected: bootstrap expected plan {fieldName}, but the launch graph does not declare {fieldName}.");
            }

            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    $"Stale launch graph rejected: bootstrap plan {fieldName} count {expected.Count} does not match graph count {actual.Count}.");
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Stale launch graph rejected: bootstrap plan {fieldName}[{i}]='{expected[i]}' does not match graph {fieldName}[{i}]='{actual[i]}'.");
                }
            }
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return char.ToUpperInvariant(value[0]) + value[1..];
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
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
