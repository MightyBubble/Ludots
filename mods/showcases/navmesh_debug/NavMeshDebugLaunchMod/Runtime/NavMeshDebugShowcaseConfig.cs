using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace NavMeshDebugLaunchMod.Runtime
{
    internal sealed class NavMeshDebugShowcaseConfig
    {
        public string AutoObstacleEnvironmentVariable { get; set; } = string.Empty;
        public int AutoObstacleDelayFrames { get; set; }
        public int DefaultObstacleRadiusCm { get; set; }
        public NavMeshDebugSpawnPointConfig[] SpawnPoints { get; set; } = Array.Empty<NavMeshDebugSpawnPointConfig>();

        public static NavMeshDebugShowcaseConfig Load(JsonObject configObject)
        {
            using var document = JsonDocument.Parse(configObject.ToJsonString());
            JsonElement root = document.RootElement;
            RequireProperty(root, "autoObstacleEnvironmentVariable");
            RequireProperty(root, "autoObstacleDelayFrames");
            RequireProperty(root, "defaultObstacleRadiusCm");
            RequireProperty(root, "spawnPoints");

            var options = StrictJsonOptions.CreateCamelCase();
            NavMeshDebugShowcaseConfig? config = root.Deserialize<NavMeshDebugShowcaseConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize NavMesh Debug showcase config.");
            }

            config.Validate();
            return config;
        }

        public NavMeshDebugSpawnPointConfig RequireSpawnPoint(string mapId)
        {
            for (int i = 0; i < SpawnPoints.Length; i++)
            {
                if (string.Equals(SpawnPoints[i].MapId, mapId, StringComparison.Ordinal))
                {
                    return SpawnPoints[i];
                }
            }

            throw new InvalidOperationException($"NavMesh Debug showcase config has no spawn point for map '{mapId}'.");
        }

        private void Validate()
        {
            RequireTrimmed(AutoObstacleEnvironmentVariable, "autoObstacleEnvironmentVariable");
            RequirePositive(AutoObstacleDelayFrames, "autoObstacleDelayFrames");
            RequirePositive(DefaultObstacleRadiusCm, "defaultObstacleRadiusCm");

            if (SpawnPoints.Length == 0)
            {
                throw new InvalidOperationException("NavMesh Debug showcase config requires at least one spawn point.");
            }

            for (int i = 0; i < SpawnPoints.Length; i++)
            {
                SpawnPoints[i].Validate(i);
            }
        }

        private static void RequireProperty(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out _))
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config requires explicit '{propertyName}' property.");
            }
        }

        private static void RequireTrimmed(string value, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !string.Equals(value.Trim(), value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config requires non-empty trimmed '{propertyName}'.");
            }
        }

        private static void RequirePositive(int value, string propertyName)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config requires '{propertyName}' > 0.");
            }
        }
    }

    internal sealed class NavMeshDebugSpawnPointConfig
    {
        public string MapId { get; set; } = string.Empty;
        public int XCm { get; set; }
        public int YCm { get; set; }
        public int ObstacleRadiusCm { get; set; }

        public void Validate(int index)
        {
            if (string.IsNullOrWhiteSpace(MapId) ||
                !string.Equals(MapId.Trim(), MapId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config spawnPoints[{index}] requires a non-empty trimmed mapId.");
            }

            if (XCm < 0 || YCm < 0)
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config spawnPoints[{index}] requires non-negative coordinates.");
            }

            if (ObstacleRadiusCm <= 0)
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config spawnPoints[{index}] requires obstacleRadiusCm > 0.");
            }
        }
    }

    internal sealed class NavMeshDebugShowcaseConfigLoader
    {
        public const string RelativePath = "NavMeshDebugShowcaseConfig.json";

        private readonly ConfigPipeline _pipeline;

        public NavMeshDebugShowcaseConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public NavMeshDebugShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config '{RelativePath}' must be registered in config_catalog.json.");
            }

            if (entry.MergePolicy != ConfigMergePolicy.Replace)
            {
                throw new InvalidOperationException($"NavMesh Debug showcase config '{RelativePath}' must use Replace merge policy.");
            }

            JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
            if (merged == null)
            {
                throw new InvalidOperationException($"NavMesh Debug showcase requires config '{RelativePath}' through ConfigPipeline.");
            }

            return NavMeshDebugShowcaseConfig.Load(merged);
        }
    }
}
