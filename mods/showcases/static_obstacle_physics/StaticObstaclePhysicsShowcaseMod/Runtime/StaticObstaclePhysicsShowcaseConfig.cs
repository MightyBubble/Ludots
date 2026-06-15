using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace StaticObstaclePhysicsShowcaseMod.Runtime;

internal sealed class StaticObstaclePhysicsShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public string ObstacleTemplateId { get; set; } = string.Empty;
    public int SpawnScratchCapacity { get; set; }
    public StaticObstaclePhysicsRegionConfig[] Regions { get; set; } = Array.Empty<StaticObstaclePhysicsRegionConfig>();

    public int TotalObstacleCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Regions.Length; i++)
            {
                count = checked(count + Regions[i].ObstacleCount);
            }

            return count;
        }
    }

    public static StaticObstaclePhysicsShowcaseConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        StaticObstaclePhysicsShowcaseConfig? config = document.RootElement.Deserialize<StaticObstaclePhysicsShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize static obstacle physics showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "obstacleTemplateId");
        RequireProperty(root, "spawnScratchCapacity");
        JsonElement regions = RequireProperty(root, "regions");
        if (regions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Static obstacle physics showcase config requires regions as an array.");
        }

        int index = 0;
        foreach (JsonElement region in regions.EnumerateArray())
        {
            RequireProperty(region, "id");
            RequireProperty(region, "centerXCm");
            RequireProperty(region, "centerYCm");
            RequireProperty(region, "columns");
            RequireProperty(region, "rows");
            RequireProperty(region, "spacingXCm");
            RequireProperty(region, "spacingYCm");
            RequireProperty(region, "staggerXCm");
            RequireProperty(region, "staggerYCm");
            RequireProperty(region, "facingDeg");
            index++;
        }

        if (index <= 0)
        {
            throw new InvalidOperationException("Static obstacle physics showcase config requires at least one region.");
        }
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(ObstacleTemplateId, nameof(ObstacleTemplateId));
        if (SpawnScratchCapacity <= 0)
        {
            throw new InvalidOperationException("Static obstacle physics showcase config requires spawnScratchCapacity > 0.");
        }

        if (Regions.Length <= 0)
        {
            throw new InvalidOperationException("Static obstacle physics showcase config requires at least one region.");
        }

        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Regions.Length; i++)
        {
            Regions[i].Validate(i);
            if (!regionIds.Add(Regions[i].Id))
            {
                throw new InvalidOperationException($"Static obstacle physics showcase config contains duplicate region id '{Regions[i].Id}'.");
            }
        }

        int totalObstacleCount = TotalObstacleCount;
        if (SpawnScratchCapacity < totalObstacleCount)
        {
            throw new InvalidOperationException(
                $"Static obstacle physics showcase config requires spawnScratchCapacity >= total obstacle count {totalObstacleCount}.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Static obstacle physics showcase config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Static obstacle physics showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class StaticObstaclePhysicsRegionConfig
{
    public string Id { get; set; } = string.Empty;
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int SpacingXCm { get; set; }
    public int SpacingYCm { get; set; }
    public int StaggerXCm { get; set; }
    public int StaggerYCm { get; set; }
    public float FacingDeg { get; set; }

    public int ObstacleCount => checked(Columns * Rows);

    public void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"Static obstacle physics showcase regions[{index}] requires non-empty id.");
        }

        if (Columns <= 0 || Rows <= 0)
        {
            throw new InvalidOperationException($"Static obstacle physics showcase region '{Id}' requires columns and rows > 0.");
        }

        if (SpacingXCm <= 0 || SpacingYCm <= 0)
        {
            throw new InvalidOperationException($"Static obstacle physics showcase region '{Id}' requires spacingXCm and spacingYCm > 0.");
        }

        if (!float.IsFinite(FacingDeg))
        {
            throw new InvalidOperationException($"Static obstacle physics showcase region '{Id}' requires finite facingDeg.");
        }

        _ = ObstacleCount;
    }
}

internal sealed class StaticObstaclePhysicsShowcaseConfigLoader
{
    public const string RelativePath = "StaticObstaclePhysicsShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public StaticObstaclePhysicsShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public StaticObstaclePhysicsShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Static obstacle physics showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Static obstacle physics showcase config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Static obstacle physics showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return StaticObstaclePhysicsShowcaseConfig.Load(merged);
    }
}
