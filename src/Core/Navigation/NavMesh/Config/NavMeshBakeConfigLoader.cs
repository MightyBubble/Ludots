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

            if (config.LayeredSpan == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layeredSpan is required.");
            }

            if (config.TriangleSurface == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.triangleSurface is required.");
            }

            if (config.Recast == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.recast is required.");
            }

            config.LayeredSpan.Validate();
            config.TriangleSurface.Validate(layeredSpan: config.LayeredSpan);
            config.Recast.Validate();
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
            RequireOnlyProperties(root, "NavMeshBakeConfig", "mode", "algorithm", "profiles", "layers", "areas", "runtimeIncremental", "layeredSpan", "triangleSurface", "recast");
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

            const string runtimePath = "NavMeshBakeConfig.runtimeIncremental";
            RequireOnlyProperties(
                runtimeIncremental,
                runtimePath,
                "tileBudgetPerFixedTick",
                "includeNeighborTiles",
                "heightScaleMeters",
                "minWalkableUpDot",
                "cliffHeightThreshold",
                "trackedStructuralEntityCapacity",
                "obstaclePrimitiveCapacity",
                "polygonVertexCapacity",
                "dirtyTileCapacity",
                "stagedEntryCapacity",
                "publishedTileCapacity",
                "storeGroupCapacity",
                "residentTileCapacity",
                "outputVertexCapacity",
                "outputTriangleCapacity",
                "outputPortalCapacity",
                "initialResidentChunkX",
                "initialResidentChunkZ",
                "initialResidentWidthChunks",
                "initialResidentHeightChunks");
            int tileBudget = RequireInt(runtimeIncremental, "tileBudgetPerFixedTick", runtimePath);
            if (tileBudget <= 0)
            {
                throw new InvalidOperationException($"{runtimePath}.tileBudgetPerFixedTick must be > 0.");
            }

            RequireBool(runtimeIncremental, "includeNeighborTiles", runtimePath);
            float heightScale = RequireFloat(runtimeIncremental, "heightScaleMeters", runtimePath);
            if (heightScale <= 0f || float.IsNaN(heightScale))
            {
                throw new InvalidOperationException($"{runtimePath}.heightScaleMeters must be > 0.");
            }

            float minUpDot = RequireFloat(runtimeIncremental, "minWalkableUpDot", runtimePath);
            if (minUpDot < -1f || minUpDot > 1f || float.IsNaN(minUpDot))
            {
                throw new InvalidOperationException($"{runtimePath}.minWalkableUpDot must be between -1 and 1.");
            }

            int cliff = RequireInt(runtimeIncremental, "cliffHeightThreshold", runtimePath);
            if (cliff < 0)
            {
                throw new InvalidOperationException($"{runtimePath}.cliffHeightThreshold must be >= 0.");
            }

            RequirePositiveInt(runtimeIncremental, "trackedStructuralEntityCapacity", runtimePath);
            RequirePositiveInt(runtimeIncremental, "obstaclePrimitiveCapacity", runtimePath);
            RequirePositiveInt(runtimeIncremental, "polygonVertexCapacity", runtimePath);
            int dirtyTileCapacity = RequirePositiveInt(runtimeIncremental, "dirtyTileCapacity", runtimePath);
            int stagedEntryCapacity = RequirePositiveInt(runtimeIncremental, "stagedEntryCapacity", runtimePath);
            int publishedTileCapacity = RequirePositiveInt(runtimeIncremental, "publishedTileCapacity", runtimePath);
            RequirePositiveInt(runtimeIncremental, "storeGroupCapacity", runtimePath);
            int residentTileCapacity = RequirePositiveInt(runtimeIncremental, "residentTileCapacity", runtimePath);
            RequirePositiveInt(runtimeIncremental, "outputVertexCapacity", runtimePath);
            RequirePositiveInt(runtimeIncremental, "outputTriangleCapacity", runtimePath);
            RequirePositiveInt(runtimeIncremental, "outputPortalCapacity", runtimePath);

            int initialResidentChunkX = RequireInt(runtimeIncremental, "initialResidentChunkX", runtimePath);
            int initialResidentChunkZ = RequireInt(runtimeIncremental, "initialResidentChunkZ", runtimePath);
            if (initialResidentChunkX < 0)
            {
                throw new InvalidOperationException($"{runtimePath}.initialResidentChunkX must be >= 0.");
            }

            if (initialResidentChunkZ < 0)
            {
                throw new InvalidOperationException($"{runtimePath}.initialResidentChunkZ must be >= 0.");
            }

            int initialResidentWidthChunks = RequirePositiveInt(runtimeIncremental, "initialResidentWidthChunks", runtimePath);
            int initialResidentHeightChunks = RequirePositiveInt(runtimeIncremental, "initialResidentHeightChunks", runtimePath);
            int initialResidentTiles = checked(initialResidentWidthChunks * initialResidentHeightChunks);
            if (initialResidentTiles > dirtyTileCapacity)
            {
                throw new InvalidOperationException(
                    $"{runtimePath}.initialResident window ({initialResidentWidthChunks}x{initialResidentHeightChunks}={initialResidentTiles}) " +
                    $"exceeds dirtyTileCapacity ({dirtyTileCapacity}).");
            }

            if (initialResidentTiles > residentTileCapacity)
            {
                throw new InvalidOperationException(
                    $"{runtimePath}.initialResident window ({initialResidentWidthChunks}x{initialResidentHeightChunks}={initialResidentTiles}) " +
                    $"exceeds residentTileCapacity ({residentTileCapacity}).");
            }

            if (initialResidentTiles > stagedEntryCapacity)
            {
                throw new InvalidOperationException(
                    $"{runtimePath}.initialResident window ({initialResidentWidthChunks}x{initialResidentHeightChunks}={initialResidentTiles}) " +
                    $"exceeds stagedEntryCapacity ({stagedEntryCapacity}).");
            }

            if (initialResidentTiles > publishedTileCapacity)
            {
                throw new InvalidOperationException(
                    $"{runtimePath}.initialResident window ({initialResidentWidthChunks}x{initialResidentHeightChunks}={initialResidentTiles}) " +
                    $"exceeds publishedTileCapacity ({publishedTileCapacity}).");
            }

            ValidateLayeredSpanRaw(root);
            ValidateTriangleSurfaceRaw(root);
            ValidateRecastRaw(root);
        }

        private static void ValidateRecastRaw(JsonObject root)
        {
            if (root["recast"] is not JsonObject recast)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.recast must be an explicit object.");
            }

            const string path = "NavMeshBakeConfig.recast";
            RequireOnlyProperties(recast, path, "rasterCellSizeCm", "rasterCellHeightCm");
            RequirePositiveInt(recast, "rasterCellSizeCm", path);
            RequirePositiveInt(recast, "rasterCellHeightCm", path);
        }

        private static void ValidateTriangleSurfaceRaw(JsonObject root)
        {
            if (root["triangleSurface"] is not JsonObject triangleSurface)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.triangleSurface must be an explicit object.");
            }

            const string path = "NavMeshBakeConfig.triangleSurface";
            RequireOnlyProperties(triangleSurface, path, "haloPaddingCm");
            RequireNonNegativeInt(triangleSurface, "haloPaddingCm", path);

            if (root["layeredSpan"] is JsonObject layeredSpan)
            {
                int rasterCellSizeCm = RequirePositiveInt(layeredSpan, "rasterCellSizeCm", "NavMeshBakeConfig.layeredSpan");
                int rasterHaloCells = RequireNonNegativeInt(layeredSpan, "rasterHaloCells", "NavMeshBakeConfig.layeredSpan");
                int requiredHalo = checked(rasterHaloCells * rasterCellSizeCm);
                int haloPaddingCm = RequireNonNegativeInt(triangleSurface, "haloPaddingCm", path);
                if (haloPaddingCm < requiredHalo)
                {
                    throw new InvalidOperationException(
                        $"{path}.haloPaddingCm ({haloPaddingCm}) must be >= " +
                        $"layeredSpan.rasterHaloCells * rasterCellSizeCm ({requiredHalo}).");
                }
            }
        }

        private static void ValidateLayeredSpanRaw(JsonObject root)
        {
            if (root["layeredSpan"] is not JsonObject layeredSpan)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layeredSpan must be an explicit object.");
            }

            const string path = "NavMeshBakeConfig.layeredSpan";
            RequireOnlyProperties(
                layeredSpan,
                path,
                "scratchSlotCount",
                "rasterCellSizeCm",
                "rasterHaloCells",
                "sameSurfaceToleranceCm",
                "maxSimplificationErrorCm",
                "heightRounding",
                "maxLawsonFlipCount",
                "columnCapacity",
                "spanCapacity",
                "classifiedSpanCapacity",
                "walkableSpanCapacity",
                "linkCapacity",
                "sheetCapacity",
                "portalIntervalCapacity",
                "regionCapacity",
                "chartCapacity",
                "ringCapacity",
                "contourVertexCapacity",
                "contourEdgeCapacity",
                "seamCapacity",
                "canonicalLinkCapacity",
                "splitPointCapacity",
                "triangulationVertexCapacity",
                "triangulationTriangleCapacity",
                "constrainedEdgeCapacity",
                "borderPortalCapacity",
                "polygonVertexCapacity",
                "adjacencyEdgeCapacity",
                "bridgeCandidateCapacity",
                "ringWorkCapacity",
                "temporaryConstraintFlagCapacity");

            RequirePositiveInt(layeredSpan, "scratchSlotCount", path);
            RequirePositiveInt(layeredSpan, "rasterCellSizeCm", path);
            RequireNonNegativeInt(layeredSpan, "rasterHaloCells", path);
            RequireNonNegativeInt(layeredSpan, "sameSurfaceToleranceCm", path);
            RequireNonNegativeInt(layeredSpan, "maxSimplificationErrorCm", path);
            string heightRounding = RequireString(layeredSpan, "heightRounding", path);
            _ = NavLayeredSpanConfig.ParseHeightRounding(heightRounding, $"{path}.heightRounding");
            RequireNonNegativeInt(layeredSpan, "maxLawsonFlipCount", path);

            RequirePositiveInt(layeredSpan, "columnCapacity", path);
            RequirePositiveInt(layeredSpan, "spanCapacity", path);
            RequirePositiveInt(layeredSpan, "classifiedSpanCapacity", path);
            RequirePositiveInt(layeredSpan, "walkableSpanCapacity", path);
            RequirePositiveInt(layeredSpan, "linkCapacity", path);
            RequirePositiveInt(layeredSpan, "sheetCapacity", path);
            RequirePositiveInt(layeredSpan, "portalIntervalCapacity", path);
            RequirePositiveInt(layeredSpan, "regionCapacity", path);
            RequirePositiveInt(layeredSpan, "chartCapacity", path);
            RequirePositiveInt(layeredSpan, "ringCapacity", path);
            RequirePositiveInt(layeredSpan, "contourVertexCapacity", path);
            RequirePositiveInt(layeredSpan, "contourEdgeCapacity", path);
            RequirePositiveInt(layeredSpan, "seamCapacity", path);
            RequirePositiveInt(layeredSpan, "canonicalLinkCapacity", path);
            RequirePositiveInt(layeredSpan, "splitPointCapacity", path);
            RequirePositiveInt(layeredSpan, "triangulationVertexCapacity", path);
            RequirePositiveInt(layeredSpan, "triangulationTriangleCapacity", path);
            RequirePositiveInt(layeredSpan, "constrainedEdgeCapacity", path);
            RequirePositiveInt(layeredSpan, "borderPortalCapacity", path);
            RequirePositiveInt(layeredSpan, "polygonVertexCapacity", path);
            RequirePositiveInt(layeredSpan, "adjacencyEdgeCapacity", path);
            RequirePositiveInt(layeredSpan, "bridgeCandidateCapacity", path);
            RequirePositiveInt(layeredSpan, "ringWorkCapacity", path);
            RequirePositiveInt(layeredSpan, "temporaryConstraintFlagCapacity", path);
        }

        private static int RequirePositiveInt(JsonObject obj, string key, string path)
        {
            int value = RequireInt(obj, key, path);
            if (value <= 0)
            {
                throw new InvalidOperationException($"{path}.{key} must be > 0.");
            }

            return value;
        }

        private static int RequireNonNegativeInt(JsonObject obj, string key, string path)
        {
            int value = RequireInt(obj, key, path);
            if (value < 0)
            {
                throw new InvalidOperationException($"{path}.{key} must be >= 0.");
            }

            return value;
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
