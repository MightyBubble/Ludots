using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;

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

            var pipeline = new ConfigPipeline(vfs, modLoader: null);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var agentProfiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
            return new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog, relativePath: relativePath);
        }

        private void ValidateRaw(JsonObject root, string relativePath)
        {
            RequireOnlyProperties(root, "NavMeshBakeConfig", "profiles", "layers", "areas");
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
    }
}
