using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshBakeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AgentProfileRegistry _agentProfiles;

        public NavMeshBakeConfigLoader(ConfigPipeline pipeline, AgentProfileRegistry agentProfiles)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
        }

        public NavMeshBakeConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig not found in any source for '{relativePath}'.");
            }

            ValidateRaw(mergedObject, relativePath);
            var options = StrictJsonOptions.CreateCamelCase();

            var config = mergedObject.Deserialize<NavMeshBakeConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException($"Failed to deserialize NavMeshBakeConfig from '{relativePath}'.");
            }

            _ = config.ParsedMode;
            _ = config.ParsedAlgorithm;

            if (config.ParsedMode == NavBakeMode.RuntimeIncremental &&
                config.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt &&
                config.ParsedAlgorithm != NavBakeAlgorithmKind.Recast)
            {
                throw new InvalidOperationException("NavMeshBakeConfig runtime-incremental mode must use algorithm 'cdt' or 'recast'.");
            }

            if (config.Profiles == null || config.Profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            }

            if (config.Layers == null || config.Layers.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layers is empty.");
            }

            if (config.Areas == null)
            {
                config.Areas = new();
            }

            return config;
        }

        public static NavMeshBakeConfig LoadFromRepoRoot(string repoRoot, string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            return LoadContextFromRepoRoot(repoRoot, targetModId: null, relativePath: relativePath).Config;
        }

        public static NavMeshBakeConfigContext LoadContextFromRepoRoot(
            string repoRoot,
            string? targetModId = null,
            string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new ArgumentException("repoRoot is required.", nameof(repoRoot));
            }

            string assetsRoot = Path.Combine(Path.GetFullPath(repoRoot), "assets");
            if (!Directory.Exists(assetsRoot))
            {
                throw new DirectoryNotFoundException($"Repo root is missing assets/: {repoRoot}");
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);

            ModLoader? modLoader = null;
            if (!string.IsNullOrWhiteSpace(targetModId))
            {
                modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                modLoader.LoadResolvedPlan(ResolveModLoadPlan(repoRoot, targetModId));
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var agentProfiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
            var config = new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog, relativePath: relativePath);
            return new NavMeshBakeConfigContext(config, agentProfiles);
        }

        private static IReadOnlyList<ResolvedModLoadEntry> ResolveModLoadPlan(string repoRoot, string targetModId)
        {
            string modsRoot = Path.Combine(Path.GetFullPath(repoRoot), "mods");
            if (!Directory.Exists(modsRoot))
            {
                throw new DirectoryNotFoundException($"Repo root is missing mods/: {repoRoot}");
            }

            List<DiscoveredMod> discovered = ModDiscovery.DiscoverMods(new[] { modsRoot });
            var byId = new Dictionary<string, DiscoveredMod>(StringComparer.Ordinal);
            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredMod mod = discovered[i];
                if (!byId.TryAdd(mod.Manifest.Name, mod))
                {
                    throw new InvalidOperationException($"Duplicate mod id '{mod.Manifest.Name}' in repo mods/.");
                }
            }

            if (!byId.ContainsKey(targetModId))
            {
                throw new InvalidOperationException($"Unknown mod '{targetModId}'.");
            }

            var required = new HashSet<string>(StringComparer.Ordinal);
            void AddRequired(string modId)
            {
                if (!required.Add(modId))
                {
                    return;
                }

                if (!byId.TryGetValue(modId, out DiscoveredMod mod))
                {
                    throw new InvalidOperationException($"Missing mod dependency '{modId}'.");
                }

                foreach (string dependencyId in mod.Manifest.Dependencies.Keys)
                {
                    AddRequired(dependencyId);
                }
            }

            AddRequired(targetModId);

            var nodes = new List<DependencyResolver.ModNode>(required.Count);
            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredMod mod = discovered[i];
                if (!required.Contains(mod.Manifest.Name))
                {
                    continue;
                }

                nodes.Add(new DependencyResolver.ModNode
                {
                    Manifest = mod.Manifest,
                    CreationIndex = i
                });
            }

            List<ModManifest> sorted = new DependencyResolver().Resolve(nodes);
            var result = new List<ResolvedModLoadEntry>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                ModManifest manifest = sorted[i];
                result.Add(new ResolvedModLoadEntry(manifest.Name, byId[manifest.Name].DirectoryPath));
            }

            return result;
        }

        private void ValidateRaw(JsonObject root, string relativePath)
        {
            RequireOnlyProperties(root, "NavMeshBakeConfig", "mode", "algorithm", "profiles", "layers", "areas", "runtimeIncremental");
            string mode = RequireString(root, "mode", "NavMeshBakeConfig");
            string algorithm = RequireString(root, "algorithm", "NavMeshBakeConfig");
            _ = NavBakeNames.ParseMode(mode, "NavMeshBakeConfig.mode");
            _ = NavBakeNames.ParseAlgorithm(algorithm, "NavMeshBakeConfig.algorithm");

            if (root["profiles"] is not JsonArray profiles || profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles must be a non-empty explicit array.");
            }

            var seenProfiles = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] is not JsonObject profile)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles[{i}] must be an object.");
                }

                string path = $"NavMeshBakeConfig.profiles[{i}]";
                RequireOnlyProperties(profile, path, "id", "maxClimbCm", "maxSlopeDeg");
                string id = RequireString(profile, "id", path);
                if (!seenProfiles.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles contains duplicate id '{id}'.");
                }

                _agentProfiles.Require(id, $"{relativePath}.profiles[{i}]");
                RequireNumber(profile, "maxClimbCm", path);
                RequireNumber(profile, "maxSlopeDeg", path);
            }

            if (root["layers"] is not JsonArray layers || layers.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layers must be a non-empty explicit array.");
            }

            var seenLayers = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] is not JsonObject layer)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers[{i}] must be an object.");
                }

                string path = $"NavMeshBakeConfig.layers[{i}]";
                RequireOnlyProperties(layer, path, "id", "layer");
                string id = RequireString(layer, "id", path);
                if (!seenLayers.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers contains duplicate id '{id}'.");
                }

                RequireNumber(layer, "layer", path);
            }

            if (root["areas"] is not JsonArray areas)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.areas must be an explicit array.");
            }

            for (int i = 0; i < areas.Count; i++)
            {
                if (areas[i] is not JsonObject area)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas[{i}] must be an object.");
                }

                string path = $"NavMeshBakeConfig.areas[{i}]";
                RequireOnlyProperties(area, path, "id", "areaId", "cost");
                RequireString(area, "id", path);
                RequireNumber(area, "areaId", path);
                RequireNumber(area, "cost", path);
            }

            if (root["runtimeIncremental"] is not JsonObject runtimeIncremental)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental must be an explicit object.");
            }

            RequireOnlyProperties(
                runtimeIncremental,
                "NavMeshBakeConfig.runtimeIncremental",
                "tileBudgetPerFixedTick",
                "includeNeighborTiles",
                "heightScaleMeters",
                "minWalkableUpDot",
                "cliffHeightThreshold");
            int tileBudget = RequireInt(runtimeIncremental, "tileBudgetPerFixedTick", "NavMeshBakeConfig.runtimeIncremental");
            if (tileBudget <= 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.tileBudgetPerFixedTick must be > 0.");
            }

            RequireBool(runtimeIncremental, "includeNeighborTiles", "NavMeshBakeConfig.runtimeIncremental");
            float heightScale = RequireFloat(runtimeIncremental, "heightScaleMeters", "NavMeshBakeConfig.runtimeIncremental");
            if (heightScale <= 0f || float.IsNaN(heightScale))
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.heightScaleMeters must be > 0.");
            }

            float minUpDot = RequireFloat(runtimeIncremental, "minWalkableUpDot", "NavMeshBakeConfig.runtimeIncremental");
            if (minUpDot < -1f || minUpDot > 1f || float.IsNaN(minUpDot))
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.minWalkableUpDot must be between -1 and 1.");
            }

            int cliff = RequireInt(runtimeIncremental, "cliffHeightThreshold", "NavMeshBakeConfig.runtimeIncremental");
            if (cliff < 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.cliffHeightThreshold must be >= 0.");
            }
        }

        private static void RequireOnlyProperties(JsonObject obj, string path, params string[] allowed)
        {
            foreach (var property in obj)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(property.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException($"{path} contains unknown property '{property.Key}'.");
                }
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (!obj.ContainsKey(allowed[i]))
                {
                    throw new InvalidOperationException($"{path} must explicitly define '{allowed[i]}'.");
                }
            }
        }

        private static string RequireString(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{path}.{key} must be a non-empty string.");
            }

            if (!string.Equals(text.Trim(), text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.{key} must not contain leading or trailing whitespace.");
            }

            return text;
        }

        private static void RequireNumber(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value ||
                (!value.TryGetValue<int>(out _) &&
                 !value.TryGetValue<float>(out _) &&
                 !value.TryGetValue<double>(out _)))
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }
        }

        private static int RequireInt(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<int>(out int number))
            {
                throw new InvalidOperationException($"{path}.{key} must be an integer.");
            }

            return number;
        }

        private static float RequireFloat(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value)
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }

            if (value.TryGetValue<float>(out float number))
            {
                return number;
            }

            if (value.TryGetValue<double>(out double numberDouble))
            {
                return (float)numberDouble;
            }

            throw new InvalidOperationException($"{path}.{key} must be a number.");
        }

        private static bool RequireBool(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
            {
                throw new InvalidOperationException($"{path}.{key} must be a boolean.");
            }

            return flag;
        }
    }

    public sealed record NavMeshBakeConfigContext(
        NavMeshBakeConfig Config,
        AgentProfileRegistry AgentProfiles);
}
