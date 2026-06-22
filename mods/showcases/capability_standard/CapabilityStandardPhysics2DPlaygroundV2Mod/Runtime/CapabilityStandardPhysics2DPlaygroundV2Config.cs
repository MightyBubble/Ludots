using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

public sealed class CapabilityStandardPhysics2DPlaygroundV2Config
{
    public string MapId { get; set; } = string.Empty;
    public int SpawnScratchCapacity { get; set; }
    public CapabilityStandardPhysics2DPlaygroundV2SpawnConfig[] Spawns { get; set; } = Array.Empty<CapabilityStandardPhysics2DPlaygroundV2SpawnConfig>();
    public string PrimaryPhysicsTemplateId { get; set; } = string.Empty;
    public string PrimaryNavTemplateId { get; set; } = string.Empty;
    public string PrimaryObstacleTemplateId { get; set; } = string.Empty;
    public int NavTargetXCm { get; set; }
    public int NavTargetYCm { get; set; }
    public int PhysicsImpulseXCmPerSec { get; set; }
    public int PhysicsImpulseYCmPerSec { get; set; }
    public int DisplacementDistanceCm { get; set; }
    public int DisplacementTicks { get; set; }
    public string BenchmarkBodyTemplateId { get; set; } = string.Empty;
    public int BenchmarkDefaultSpawnCount { get; set; }
    public int BenchmarkSpawnRadiusCm { get; set; }
    public int BenchmarkInitialSpeedCmPerSec { get; set; }
    public int BenchmarkForceXCmPerSec2 { get; set; }
    public int BenchmarkForceYCmPerSec2 { get; set; }
    public string StaticPolygonTemplateId { get; set; } = string.Empty;
    public string FrictionZoneLowTemplateId { get; set; } = string.Empty;
    public string FrictionZoneMediumTemplateId { get; set; } = string.Empty;
    public string FrictionZoneHighTemplateId { get; set; } = string.Empty;
    public int FrictionZoneSpacingCm { get; set; }
    public int ExplosionRadiusCm { get; set; }
    public int ExplosionForceCmPerSec2 { get; set; }
    public int ExplosionQueryCapacity { get; set; }

    public static CapabilityStandardPhysics2DPlaygroundV2Config Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        CapabilityStandardPhysics2DPlaygroundV2Config? config = document.RootElement.Deserialize<CapabilityStandardPhysics2DPlaygroundV2Config>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize capability-standard Physics2D Playground v2 config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "spawnScratchCapacity");
        RequireProperty(root, "primaryPhysicsTemplateId");
        RequireProperty(root, "primaryNavTemplateId");
        RequireProperty(root, "primaryObstacleTemplateId");
        RequireProperty(root, "navTargetXCm");
        RequireProperty(root, "navTargetYCm");
        RequireProperty(root, "physicsImpulseXCmPerSec");
        RequireProperty(root, "physicsImpulseYCmPerSec");
        RequireProperty(root, "displacementDistanceCm");
        RequireProperty(root, "displacementTicks");
        RequireProperty(root, "benchmarkBodyTemplateId");
        RequireProperty(root, "benchmarkDefaultSpawnCount");
        RequireProperty(root, "benchmarkSpawnRadiusCm");
        RequireProperty(root, "benchmarkInitialSpeedCmPerSec");
        RequireProperty(root, "benchmarkForceXCmPerSec2");
        RequireProperty(root, "benchmarkForceYCmPerSec2");
        RequireProperty(root, "staticPolygonTemplateId");
        RequireProperty(root, "frictionZoneLowTemplateId");
        RequireProperty(root, "frictionZoneMediumTemplateId");
        RequireProperty(root, "frictionZoneHighTemplateId");
        RequireProperty(root, "frictionZoneSpacingCm");
        RequireProperty(root, "explosionRadiusCm");
        RequireProperty(root, "explosionForceCmPerSec2");
        RequireProperty(root, "explosionQueryCapacity");

        JsonElement spawns = RequireProperty(root, "spawns");
        if (spawns.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 config requires spawns as an array.");
        }

        int index = 0;
        foreach (JsonElement spawn in spawns.EnumerateArray())
        {
            RequireProperty(spawn, "id");
            RequireProperty(spawn, "templateId");
            RequireProperty(spawn, "worldXCm");
            RequireProperty(spawn, "worldYCm");
            RequireProperty(spawn, "facingRad");
            index++;
        }

        if (index <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 config requires at least one spawn.");
        }
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(PrimaryPhysicsTemplateId, nameof(PrimaryPhysicsTemplateId));
        RequireNonEmpty(PrimaryNavTemplateId, nameof(PrimaryNavTemplateId));
        RequireNonEmpty(PrimaryObstacleTemplateId, nameof(PrimaryObstacleTemplateId));
        RequireNonEmpty(BenchmarkBodyTemplateId, nameof(BenchmarkBodyTemplateId));
        RequireNonEmpty(StaticPolygonTemplateId, nameof(StaticPolygonTemplateId));
        RequireNonEmpty(FrictionZoneLowTemplateId, nameof(FrictionZoneLowTemplateId));
        RequireNonEmpty(FrictionZoneMediumTemplateId, nameof(FrictionZoneMediumTemplateId));
        RequireNonEmpty(FrictionZoneHighTemplateId, nameof(FrictionZoneHighTemplateId));

        if (SpawnScratchCapacity < Spawns.Length)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 config requires spawnScratchCapacity >= spawns length.");
        }

        if (DisplacementDistanceCm <= 0 || DisplacementTicks <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 displacement acceptance values must be positive.");
        }

        if (BenchmarkDefaultSpawnCount <= 0 || BenchmarkDefaultSpawnCount > SpawnScratchCapacity)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 benchmarkDefaultSpawnCount must fit spawnScratchCapacity.");
        }

        if (BenchmarkSpawnRadiusCm <= 0 || BenchmarkInitialSpeedCmPerSec <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 benchmark spawn radius and initial speed must be positive.");
        }

        if (FrictionZoneSpacingCm <= 0 || ExplosionRadiusCm <= 0 || ExplosionForceCmPerSec2 <= 0 || ExplosionQueryCapacity <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 friction spacing and explosion force/query settings must be positive.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        bool hasPrimaryPhysics = false;
        bool hasPrimaryNav = false;
        bool hasPrimaryObstacle = false;
        for (int i = 0; i < Spawns.Length; i++)
        {
            Spawns[i].Validate(i);
            if (!ids.Add(Spawns[i].Id))
            {
                throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 config contains duplicate spawn id '{Spawns[i].Id}'.");
            }

            hasPrimaryPhysics |= string.Equals(Spawns[i].TemplateId, PrimaryPhysicsTemplateId, StringComparison.Ordinal);
            hasPrimaryNav |= string.Equals(Spawns[i].TemplateId, PrimaryNavTemplateId, StringComparison.Ordinal);
            hasPrimaryObstacle |= string.Equals(Spawns[i].TemplateId, PrimaryObstacleTemplateId, StringComparison.Ordinal);
        }

        if (!hasPrimaryPhysics || !hasPrimaryNav || !hasPrimaryObstacle)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 primary templates must be present in spawns.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 config requires non-empty {fieldName}.");
        }
    }
}

public sealed class CapabilityStandardPhysics2DPlaygroundV2SpawnConfig
{
    public string Id { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int WorldXCm { get; set; }
    public int WorldYCm { get; set; }
    public float FacingRad { get; set; }

    public void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 spawns[{index}] requires non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(TemplateId))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 spawn '{Id}' requires non-empty templateId.");
        }

        if (!float.IsFinite(FacingRad))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 spawn '{Id}' requires finite facingRad.");
        }
    }
}

internal sealed class CapabilityStandardPhysics2DPlaygroundV2ConfigLoader
{
    public const string RelativePath = "CapabilityStandardPhysics2DPlaygroundV2Config.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardPhysics2DPlaygroundV2ConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardPhysics2DPlaygroundV2Config Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 requires config '{RelativePath}' through ConfigPipeline.");
        }

        return CapabilityStandardPhysics2DPlaygroundV2Config.Load(merged);
    }
}
