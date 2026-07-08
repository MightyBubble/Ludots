using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavigationConfigAuthoringSaveResult
    {
        public NavigationConfigAuthoringSaveResult(
            string modId,
            string agentProfilesPath,
            string navMeshPath,
            int agentProfileCount,
            int bakeProfileCount,
            int layerCount,
            int areaCount)
        {
            ModId = modId ?? throw new ArgumentNullException(nameof(modId));
            AgentProfilesPath = agentProfilesPath ?? throw new ArgumentNullException(nameof(agentProfilesPath));
            NavMeshPath = navMeshPath ?? throw new ArgumentNullException(nameof(navMeshPath));
            AgentProfileCount = agentProfileCount;
            BakeProfileCount = bakeProfileCount;
            LayerCount = layerCount;
            AreaCount = areaCount;
        }

        public string ModId { get; }

        public string AgentProfilesPath { get; }

        public string NavMeshPath { get; }

        public int AgentProfileCount { get; }

        public int BakeProfileCount { get; }

        public int LayerCount { get; }

        public int AreaCount { get; }
    }

    public sealed class NavigationConfigAuthoringWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly IVirtualFileSystem _vfs;

        public NavigationConfigAuthoringWriter(IVirtualFileSystem vfs)
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        }

        public NavigationConfigAuthoringSaveResult Save(
            string targetModId,
            IReadOnlyList<AgentProfileConfig> agentProfiles,
            NavMeshBakeConfig navMeshConfig)
        {
            if (string.IsNullOrWhiteSpace(targetModId))
            {
                throw new ArgumentException("Navigation config save requires a target mod id.", nameof(targetModId));
            }

            Validate(agentProfiles, navMeshConfig);

            string agentProfilesPath = ResolveWritablePath(targetModId, AgentProfileConfigLoader.RelativePath);
            string navMeshPath = ResolveWritablePath(targetModId, NavMeshConfigPaths.BakeConfigPath);
            Directory.CreateDirectory(Path.GetDirectoryName(agentProfilesPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(navMeshPath)!);
            File.WriteAllText(agentProfilesPath, JsonSerializer.Serialize(agentProfiles, JsonOptions));
            File.WriteAllText(navMeshPath, JsonSerializer.Serialize(navMeshConfig, JsonOptions));

            return new NavigationConfigAuthoringSaveResult(
                targetModId,
                agentProfilesPath,
                navMeshPath,
                agentProfiles.Count,
                navMeshConfig.Profiles?.Count ?? 0,
                navMeshConfig.Layers?.Count ?? 0,
                navMeshConfig.Areas?.Count ?? 0);
        }

        public static void Validate(IReadOnlyList<AgentProfileConfig> agentProfiles, NavMeshBakeConfig navMeshConfig)
        {
            if (agentProfiles == null || agentProfiles.Count == 0)
            {
                throw new InvalidOperationException("Navigation/agent_profiles.json must define at least one profile.");
            }

            if (navMeshConfig == null)
            {
                throw new ArgumentNullException(nameof(navMeshConfig));
            }

            AgentProfileRegistry agentRegistry = new(agentProfiles);
            _ = navMeshConfig.ParsedMode;
            _ = navMeshConfig.ParsedAlgorithm;
            if (navMeshConfig.ParsedMode == NavBakeMode.RuntimeIncremental &&
                navMeshConfig.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt)
            {
                throw new InvalidOperationException("NavMeshBakeConfig runtime-incremental mode must use algorithm 'cdt'.");
            }

            if (navMeshConfig.Profiles == null || navMeshConfig.Profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            }

            if (navMeshConfig.Layers == null || navMeshConfig.Layers.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layers is empty.");
            }

            ValidateBakeProfiles(navMeshConfig.Profiles);
            ValidateLayers(navMeshConfig.Layers);
            ValidateAreas(navMeshConfig.Areas);
            ValidateRuntimeIncremental(navMeshConfig.RuntimeIncremental);
            _ = new NavMeshProfileRegistry(navMeshConfig, agentRegistry);
        }

        private string ResolveWritablePath(string targetModId, string relativePath)
        {
            string uri = $"{targetModId}:assets/Configs/{relativePath}";
            if (!_vfs.TryResolveFullPath(uri, out string path))
            {
                throw new InvalidOperationException($"Cannot resolve writable navigation config path '{uri}'.");
            }

            return path;
        }

        private static void ValidateBakeProfiles(IReadOnlyList<NavMeshAgentProfileConfig> profiles)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profiles.Count; i++)
            {
                NavMeshAgentProfileConfig profile = profiles[i]
                    ?? throw new InvalidOperationException($"NavMeshBakeConfig.profiles[{i}] must be an object.");
                if (string.IsNullOrWhiteSpace(profile.Id) ||
                    !string.Equals(profile.Id.Trim(), profile.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles[{i}].id must be a non-empty canonical string.");
                }

                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles contains duplicate id '{profile.Id}'.");
                }

                if (profile.MaxClimbCm < 0)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profile '{profile.Id}' requires maxClimbCm >= 0.");
                }

                if (profile.MaxSlopeDeg < 0f || profile.MaxSlopeDeg >= 90f || float.IsNaN(profile.MaxSlopeDeg))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profile '{profile.Id}' requires maxSlopeDeg >= 0 and < 90.");
                }
            }
        }

        private static void ValidateLayers(IReadOnlyList<NavLayerConfig> layers)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                NavLayerConfig layer = layers[i]
                    ?? throw new InvalidOperationException($"NavMeshBakeConfig.layers[{i}] must be an object.");
                if (string.IsNullOrWhiteSpace(layer.Id) ||
                    !string.Equals(layer.Id.Trim(), layer.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers[{i}].id must be a non-empty canonical string.");
                }

                if (!ids.Add(layer.Id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers contains duplicate id '{layer.Id}'.");
                }

                if (layer.Layer < 0)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layer '{layer.Id}' requires layer >= 0.");
                }
            }
        }

        private static void ValidateAreas(IReadOnlyList<NavAreaCostConfig>? areas)
        {
            if (areas == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.areas must be an explicit array.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var areaIds = new HashSet<int>();
            for (int i = 0; i < areas.Count; i++)
            {
                NavAreaCostConfig area = areas[i] ?? throw new InvalidOperationException($"NavMeshBakeConfig.areas[{i}] must be an object.");
                if (string.IsNullOrWhiteSpace(area.Id) || !string.Equals(area.Id.Trim(), area.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas[{i}].id must be a non-empty canonical string.");
                }

                if (!ids.Add(area.Id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas contains duplicate id '{area.Id}'.");
                }

                if (area.AreaId < 0 || area.AreaId > 255 || !areaIds.Add(area.AreaId))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas has invalid or duplicate areaId {area.AreaId}.");
                }

                if (area.Cost <= 0f || float.IsNaN(area.Cost))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas has invalid cost for areaId={area.AreaId}.");
                }
            }
        }

        private static void ValidateRuntimeIncremental(NavRuntimeIncrementalConfig runtime)
        {
            if (runtime == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental must be an explicit object.");
            }

            if (runtime.TileBudgetPerFixedTick <= 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.tileBudgetPerFixedTick must be > 0.");
            }

            if (runtime.HeightScaleMeters <= 0f || float.IsNaN(runtime.HeightScaleMeters))
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.heightScaleMeters must be > 0.");
            }

            if (runtime.MinWalkableUpDot < -1f ||
                runtime.MinWalkableUpDot > 1f ||
                float.IsNaN(runtime.MinWalkableUpDot))
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.minWalkableUpDot must be between -1 and 1.");
            }

            if (runtime.CliffHeightThreshold < 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.cliffHeightThreshold must be >= 0.");
            }
        }
    }
}
