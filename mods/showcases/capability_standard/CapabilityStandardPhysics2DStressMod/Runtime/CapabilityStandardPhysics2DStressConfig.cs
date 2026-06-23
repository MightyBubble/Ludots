using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace CapabilityStandardPhysics2DStressMod.Runtime;

internal sealed class CapabilityStandardPhysics2DStressConfig
{
    public string MapId { get; set; } = string.Empty;
    public int DynamicBodies { get; set; }
    public int StaticColumns { get; set; }
    public int GridColumns { get; set; }
    public int StartXCm { get; set; }
    public int StartYCm { get; set; }
    public int SpacingCm { get; set; }
    public int ContactClusterBodies { get; set; }
    public int ContactClusterColumns { get; set; }
    public int ContactClusterStartXCm { get; set; }
    public int ContactClusterStartYCm { get; set; }
    public int ContactClusterSpacingCm { get; set; }
    public int StaticStartXCm { get; set; }
    public int StaticStartYCm { get; set; }
    public int StaticSpacingCm { get; set; }
    public string DynamicTemplateId { get; set; } = string.Empty;
    public string StaticTemplateId { get; set; } = string.Empty;
    public int SpawnScratchCapacity { get; set; }
    public int AcceptanceWarmupFrames { get; set; }
    public int AcceptanceMeasuredFrames { get; set; }
    public double AcceptanceAvgStepBudgetMs { get; set; }
    public long AcceptanceSteadyStateAllocationBudgetBytes { get; set; }

    public int SpawnCount => DynamicBodies + StaticColumns;

    public static CapabilityStandardPhysics2DStressConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        CapabilityStandardPhysics2DStressConfig? config = document.RootElement.Deserialize<CapabilityStandardPhysics2DStressConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize capability-standard Physics2D stress config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        string[] required =
        {
            "mapId",
            "dynamicBodies",
            "staticColumns",
            "gridColumns",
            "startXCm",
            "startYCm",
            "spacingCm",
            "contactClusterBodies",
            "contactClusterColumns",
            "contactClusterStartXCm",
            "contactClusterStartYCm",
            "contactClusterSpacingCm",
            "staticStartXCm",
            "staticStartYCm",
            "staticSpacingCm",
            "dynamicTemplateId",
            "staticTemplateId",
            "spawnScratchCapacity",
            "acceptanceWarmupFrames",
            "acceptanceMeasuredFrames",
            "acceptanceAvgStepBudgetMs",
            "acceptanceSteadyStateAllocationBudgetBytes"
        };

        for (int i = 0; i < required.Length; i++)
        {
            RequireProperty(root, required[i]);
        }
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(DynamicTemplateId, nameof(DynamicTemplateId));
        RequireNonEmpty(StaticTemplateId, nameof(StaticTemplateId));
        if (DynamicBodies <= 0 || StaticColumns <= 0 || GridColumns <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress config requires positive body and grid counts.");
        }

        if (ContactClusterBodies <= 0 ||
            ContactClusterBodies > DynamicBodies ||
            ContactClusterColumns <= 0 ||
            ContactClusterSpacingCm <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress config requires a positive contact cluster inside dynamicBodies.");
        }

        if (SpawnScratchCapacity < SpawnCount)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress config requires spawnScratchCapacity >= dynamicBodies + staticColumns.");
        }

        if (SpacingCm <= 0 || StaticSpacingCm <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress config requires positive spacing.");
        }

        if (AcceptanceWarmupFrames <= 0 || AcceptanceMeasuredFrames <= 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress acceptance frames must be positive.");
        }

        if (AcceptanceAvgStepBudgetMs <= 0d || AcceptanceSteadyStateAllocationBudgetBytes < 0)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress acceptance budgets must be non-negative.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D stress config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D stress config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class CapabilityStandardPhysics2DStressConfigLoader
{
    public const string RelativePath = "CapabilityStandardPhysics2DStressConfig.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardPhysics2DStressConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardPhysics2DStressConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Capability-standard Physics2D stress config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D stress config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D stress requires config '{RelativePath}' through ConfigPipeline.");
        }

        return CapabilityStandardPhysics2DStressConfig.Load(merged);
    }
}
