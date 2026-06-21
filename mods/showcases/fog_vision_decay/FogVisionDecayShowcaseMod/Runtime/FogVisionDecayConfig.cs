using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Knowledge;

namespace FogVisionDecayShowcaseMod.Runtime;

internal sealed class FogVisionDecayConfig
{
    public string MapId { get; set; } = FogVisionDecayIds.MapId;
    public string Header { get; set; } = "Fog Vision Decay";
    public string Summary { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
    public int TargetCount { get; set; }
    public int LiveWindowCount { get; set; }
    public int PatrolStride { get; set; }
    public int KnownTtlTicks { get; set; }
    public int LiveExpiryOffsetTicks { get; set; }
    public int ConfidencePermille { get; set; }
    public int CapacityCeiling { get; set; }
    public int MarkerColumns { get; set; }
    public int MarkerSpacingCm { get; set; }
    public int OriginXCm { get; set; }
    public int OriginYCm { get; set; }
    public FogVisionMaintenanceConfig Maintenance { get; set; } = new();

    public KnowledgeProjectionMaintenancePolicy CreateMaintenancePolicy()
    {
        return new KnowledgeProjectionMaintenancePolicy(
            Maintenance.ExpirePeriodTicks,
            Maintenance.CompactPeriodTicks,
            Maintenance.CompactInactivePermilleThreshold,
            Maintenance.CompactInactiveCountThreshold);
    }

    public static FogVisionDecayConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        FogVisionDecayConfig? config = document.RootElement.Deserialize<FogVisionDecayConfig>(
            StrictJsonOptions.CreateCamelCase());
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize fog vision decay showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "targetCount");
        RequireProperty(root, "liveWindowCount");
        RequireProperty(root, "patrolStride");
        RequireProperty(root, "knownTtlTicks");
        RequireProperty(root, "liveExpiryOffsetTicks");
        RequireProperty(root, "confidencePermille");
        RequireProperty(root, "capacityCeiling");
        RequireProperty(root, "markerColumns");
        RequireProperty(root, "markerSpacingCm");
        RequireProperty(root, "originXCm");
        RequireProperty(root, "originYCm");
        JsonElement maintenance = RequireProperty(root, "maintenance");
        RequireProperty(maintenance, "expirePeriodTicks");
        RequireProperty(maintenance, "compactPeriodTicks");
        RequireProperty(maintenance, "compactInactivePermilleThreshold");
        RequireProperty(maintenance, "compactInactiveCountThreshold");
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(MapId))
        {
            throw new InvalidOperationException("Fog vision decay showcase config requires mapId.");
        }

        if (TargetCount <= 0 || LiveWindowCount <= 0 || LiveWindowCount > TargetCount)
        {
            throw new InvalidOperationException("Fog vision decay showcase config requires 0 < liveWindowCount <= targetCount.");
        }

        if (PatrolStride <= 0 || KnownTtlTicks <= 0 || LiveExpiryOffsetTicks <= 0)
        {
            throw new InvalidOperationException("Fog vision decay showcase config requires positive patrol stride and TTL values.");
        }

        if ((uint)ConfidencePermille > 1000u)
        {
            throw new InvalidOperationException("Fog vision decay showcase confidencePermille must be within 0..1000.");
        }

        if (CapacityCeiling <= 0 || MarkerColumns <= 0 || MarkerSpacingCm <= 0)
        {
            throw new InvalidOperationException("Fog vision decay showcase config requires positive capacity and marker layout values.");
        }

        _ = CreateMaintenancePolicy();
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Fog vision decay showcase config requires explicit '{propertyName}' property.");
        }

        return value;
    }
}

internal sealed class FogVisionMaintenanceConfig
{
    public int ExpirePeriodTicks { get; set; }
    public int CompactPeriodTicks { get; set; }
    public int CompactInactivePermilleThreshold { get; set; }
    public int CompactInactiveCountThreshold { get; set; }
}

internal sealed class FogVisionDecayConfigLoader
{
    public const string RelativePath = "FogVisionDecayShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public FogVisionDecayConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public FogVisionDecayConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Fog vision decay showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Fog vision decay showcase config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Fog vision decay showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return FogVisionDecayConfig.Load(merged);
    }
}
