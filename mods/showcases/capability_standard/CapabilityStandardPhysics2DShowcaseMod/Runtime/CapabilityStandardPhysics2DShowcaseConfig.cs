using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal sealed class CapabilityStandardPhysics2DShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public string RuntimeSpawnReceiptChannelKey { get; set; } = string.Empty;
    public string StaticObstacleTemplateId { get; set; } = string.Empty;
    public int DynamicSpawnBatch { get; set; } = 256;
    public int StaticObstacleSpawnBatch { get; set; } = 128;
    public int DynamicSpawnStep { get; set; } = 128;
    public int StaticObstacleSpawnStep { get; set; } = 64;
    public int MaxDynamicEntities { get; set; } = 30_000;
    public int MaxStaticObstacles { get; set; } = 100_000;
    public int SpawnAreaHalfWidthCm { get; set; } = 24_000;
    public int SpawnAreaHalfHeightCm { get; set; } = 14_000;
    public int DynamicRadiusCm { get; set; } = 18;
    public int StaticObstacleSpacingCm { get; set; } = 220;
    public int PhysicsHzStep { get; set; } = 5;
    public int PhysicsHzMin { get; set; } = 0;
    public int PhysicsHzMax { get; set; } = 120;
    public int MaxStepsMin { get; set; } = 1;
    public int MaxStepsMax { get; set; } = 16;
    public int BroadphaseCellSizeStepCm { get; set; } = 64;
    public int BroadphaseCellSizeMinCm { get; set; } = 64;
    public int BroadphaseCellSizeMaxCm { get; set; } = 2048;
    public float DampingStep { get; set; } = 0.02f;
    public float DampingMin { get; set; } = 0.50f;
    public float DampingMax { get; set; } = 1.00f;
    public float RestitutionStep { get; set; } = 0.05f;
    public float RestitutionMin { get; set; } = 0.00f;
    public float RestitutionMax { get; set; } = 1.00f;
    public float FrictionStep { get; set; } = 0.05f;
    public float FrictionMin { get; set; } = 0.00f;
    public float FrictionMax { get; set; } = 2.00f;

    public static CapabilityStandardPhysics2DShowcaseConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        CapabilityStandardPhysics2DShowcaseConfig? config = document.RootElement.Deserialize<CapabilityStandardPhysics2DShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize capability-standard Physics2D showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "runtimeSpawnReceiptChannelKey");
        RequireProperty(root, "staticObstacleTemplateId");
        RequireProperty(root, "dynamicSpawnBatch");
        RequireProperty(root, "staticObstacleSpawnBatch");
        RequireProperty(root, "maxDynamicEntities");
        RequireProperty(root, "maxStaticObstacles");
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(RuntimeSpawnReceiptChannelKey, nameof(RuntimeSpawnReceiptChannelKey));
        RequireNonEmpty(StaticObstacleTemplateId, nameof(StaticObstacleTemplateId));
        RequirePositive(DynamicSpawnBatch, nameof(DynamicSpawnBatch));
        RequirePositive(StaticObstacleSpawnBatch, nameof(StaticObstacleSpawnBatch));
        RequirePositive(DynamicSpawnStep, nameof(DynamicSpawnStep));
        RequirePositive(StaticObstacleSpawnStep, nameof(StaticObstacleSpawnStep));
        RequirePositive(MaxDynamicEntities, nameof(MaxDynamicEntities));
        RequirePositive(MaxStaticObstacles, nameof(MaxStaticObstacles));
        RequirePositive(SpawnAreaHalfWidthCm, nameof(SpawnAreaHalfWidthCm));
        RequirePositive(SpawnAreaHalfHeightCm, nameof(SpawnAreaHalfHeightCm));
        RequirePositive(DynamicRadiusCm, nameof(DynamicRadiusCm));
        RequirePositive(StaticObstacleSpacingCm, nameof(StaticObstacleSpacingCm));
        if (PhysicsHzMin < 0 || PhysicsHzMax < PhysicsHzMin)
        {
            throw new InvalidOperationException("Physics2D showcase requires valid physics Hz bounds.");
        }

        if (BroadphaseCellSizeMinCm < 1 || BroadphaseCellSizeMaxCm < BroadphaseCellSizeMinCm)
        {
            throw new InvalidOperationException("Physics2D showcase requires valid broadphase cell-size bounds.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D showcase config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D showcase config requires non-empty {fieldName}.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D showcase config requires {fieldName} > 0.");
        }
    }
}

internal sealed class CapabilityStandardPhysics2DShowcaseConfigLoader
{
    public const string RelativePath = "CapabilityStandardPhysics2DShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardPhysics2DShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardPhysics2DShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D showcase config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return CapabilityStandardPhysics2DShowcaseConfig.Load(merged);
    }
}
