using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

public sealed class UiPlayerAggregateGraphMvpConfig
{
    public string MapId { get; set; } = UiPlayerAggregateGraphMvpIds.MapId;
    public string GraphId { get; set; } = string.Empty;
    public int PlayerTeamId { get; set; }
    public string FactionOwnerName { get; set; } = string.Empty;
    public string ShutDownBuildingName { get; set; } = string.Empty;
    public UiPlayerAggregateAttributeNames Attributes { get; set; } = new();
    public UiPlayerAggregateSummaryKeys SummaryKeys { get; set; } = new();
    public UiPlayerAggregateBuildingSeed[] Buildings { get; set; } = Array.Empty<UiPlayerAggregateBuildingSeed>();
    public UiPlayerAggregatePresentation Presentation { get; set; } = new();

    public static UiPlayerAggregateGraphMvpConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        UiPlayerAggregateGraphMvpConfig? config = document.RootElement.Deserialize<UiPlayerAggregateGraphMvpConfig>(
            StrictJsonOptions.CreateCamelCase());
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize player aggregate graph MVP showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "graphId");
        RequireProperty(root, "playerTeamId");
        RequireProperty(root, "factionOwnerName");
        RequireProperty(root, "shutDownBuildingName");
        JsonElement attributes = RequireProperty(root, "attributes");
        RequireProperty(attributes, "ore");
        RequireProperty(attributes, "crystal");
        JsonElement summaryKeys = RequireProperty(root, "summaryKeys");
        RequireProperty(summaryKeys, "oreTotal");
        RequireProperty(summaryKeys, "crystalTotal");
        JsonElement buildings = RequireProperty(root, "buildings");
        if (buildings.ValueKind != JsonValueKind.Array || buildings.GetArrayLength() < 2)
        {
            throw new InvalidOperationException("Player aggregate graph MVP config requires at least two buildings.");
        }

        foreach (JsonElement building in buildings.EnumerateArray())
        {
            RequireProperty(building, "name");
            RequireProperty(building, "ore");
            RequireProperty(building, "crystal");
        }

        JsonElement presentation = RequireProperty(root, "presentation");
        RequireProperty(presentation, "title");
        RequireProperty(presentation, "copy");
        RequireProperty(presentation, "controls");
    }

    private void Validate()
    {
        if (!string.Equals(MapId, UiPlayerAggregateGraphMvpIds.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Player aggregate graph MVP config mapId must be '{UiPlayerAggregateGraphMvpIds.MapId}'.");
        }

        if (string.IsNullOrWhiteSpace(GraphId) ||
            string.IsNullOrWhiteSpace(FactionOwnerName) ||
            string.IsNullOrWhiteSpace(ShutDownBuildingName) ||
            string.IsNullOrWhiteSpace(Attributes.Ore) ||
            string.IsNullOrWhiteSpace(Attributes.Crystal) ||
            string.IsNullOrWhiteSpace(SummaryKeys.OreTotal) ||
            string.IsNullOrWhiteSpace(SummaryKeys.CrystalTotal) ||
            string.IsNullOrWhiteSpace(Presentation.Title) ||
            string.IsNullOrWhiteSpace(Presentation.Copy))
        {
            throw new InvalidOperationException("Player aggregate graph MVP config is missing required string fields.");
        }

        if (PlayerTeamId <= 0)
        {
            throw new InvalidOperationException("Player aggregate graph MVP config requires a positive playerTeamId.");
        }

        if (Buildings.Length < 2)
        {
            throw new InvalidOperationException("Player aggregate graph MVP config requires at least two buildings.");
        }

        bool foundShutDown = false;
        for (int i = 0; i < Buildings.Length; i++)
        {
            UiPlayerAggregateBuildingSeed building = Buildings[i];
            if (string.IsNullOrWhiteSpace(building.Name) || building.Ore < 0f || building.Crystal < 0f)
            {
                throw new InvalidOperationException("Player aggregate graph MVP buildings require name and non-negative resource values.");
            }

            if (string.Equals(building.Name, ShutDownBuildingName, StringComparison.Ordinal))
            {
                foundShutDown = true;
            }
        }

        if (!foundShutDown)
        {
            throw new InvalidOperationException($"Player aggregate graph MVP shutDownBuildingName '{ShutDownBuildingName}' is not present in buildings.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Player aggregate graph MVP config requires explicit '{propertyName}' property.");
        }

        return value;
    }
}

public sealed class UiPlayerAggregateAttributeNames
{
    public string Ore { get; set; } = string.Empty;
    public string Crystal { get; set; } = string.Empty;
}

public sealed class UiPlayerAggregateSummaryKeys
{
    public string OreTotal { get; set; } = string.Empty;
    public string CrystalTotal { get; set; } = string.Empty;
}

public sealed class UiPlayerAggregateBuildingSeed
{
    public string Name { get; set; } = string.Empty;
    public float Ore { get; set; }
    public float Crystal { get; set; }
}

public sealed class UiPlayerAggregatePresentation
{
    public string Title { get; set; } = string.Empty;
    public string Copy { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
}

internal sealed class UiPlayerAggregateGraphMvpConfigLoader
{
    public const string RelativePath = "UiPlayerAggregateGraphMvpShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public UiPlayerAggregateGraphMvpConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public UiPlayerAggregateGraphMvpConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Player aggregate graph MVP config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Player aggregate graph MVP config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Player aggregate graph MVP requires config '{RelativePath}' through ConfigPipeline.");
        }

        return UiPlayerAggregateGraphMvpConfig.Load(merged);
    }
}
