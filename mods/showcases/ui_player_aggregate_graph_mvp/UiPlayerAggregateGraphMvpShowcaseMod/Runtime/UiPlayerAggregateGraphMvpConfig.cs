using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

public sealed class UiPlayerAggregateGraphMvpConfig
{
    public const string OreBindingVariableId = "oreTotal";
    public const string CrystalBindingVariableId = "crystalTotal";

    public string MapId { get; set; } = UiPlayerAggregateGraphMvpIds.MapId;
    public string GraphId { get; set; } = string.Empty;
    public int PlayerTeamId { get; set; }
    public string FactionOwnerName { get; set; } = string.Empty;
    public string ShutDownBuildingName { get; set; } = string.Empty;
    public UiPlayerAggregateAttributeNames Attributes { get; set; } = new();
    public UiPlayerAggregatePanelBinding[] PanelBindings { get; set; } = Array.Empty<UiPlayerAggregatePanelBinding>();
    public UiPlayerAggregateBuildingSeed[] Buildings { get; set; } = Array.Empty<UiPlayerAggregateBuildingSeed>();
    public UiPlayerAggregatePresentation Presentation { get; set; } = new();

    public UiPlayerAggregateSummaryKeys SummaryKeys => new()
    {
        OreTotal = RequireBinding(OreBindingVariableId).GraphOutputKey,
        CrystalTotal = RequireBinding(CrystalBindingVariableId).GraphOutputKey,
    };

    public UiPlayerAggregatePanelBinding OreBinding => RequireBinding(OreBindingVariableId);

    public UiPlayerAggregatePanelBinding CrystalBinding => RequireBinding(CrystalBindingVariableId);

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

    public static UiPlayerAggregateGraphMvpConfig Load(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
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

    public UiPlayerAggregatePanelBinding RequireBinding(string variableId)
    {
        for (int i = 0; i < PanelBindings.Length; i++)
        {
            if (string.Equals(PanelBindings[i].VariableId, variableId, StringComparison.Ordinal))
            {
                return PanelBindings[i];
            }
        }

        throw new InvalidOperationException(
            $"Player aggregate graph MVP config is missing panelBindings entry '{variableId}'.");
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
        JsonElement panelBindings = RequireProperty(root, "panelBindings");
        if (panelBindings.ValueKind != JsonValueKind.Array || panelBindings.GetArrayLength() < 2)
        {
            throw new InvalidOperationException("Player aggregate graph MVP config requires at least two panelBindings.");
        }

        foreach (JsonElement binding in panelBindings.EnumerateArray())
        {
            RequireProperty(binding, "variableId");
            RequireProperty(binding, "label");
            RequireProperty(binding, "graphOutputKey");
            RequireProperty(binding, "accent");
        }

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
        JsonElement panel = RequireProperty(presentation, "panel");
        RequireProperty(panel, "width");
        RequireProperty(panel, "padding");
        RequireProperty(panel, "gap");
        RequireProperty(panel, "radius");
        RequireProperty(panel, "background");
        RequireProperty(panel, "borderColor");
        RequireProperty(panel, "borderThickness");
        RequireProperty(panel, "titleColor");
        RequireProperty(panel, "copyColor");
        RequireProperty(panel, "graphMetaColor");
        RequireProperty(panel, "statusColor");
        RequireProperty(panel, "controlsColor");
        RequireProperty(panel, "chipValueColor");
        RequireProperty(panel, "chipGap");
        RequireProperty(panel, "buttonPaddingX");
        RequireProperty(panel, "buttonPaddingY");
        RequireProperty(panel, "buttonRadius");
        RequireProperty(panel, "buttonBackground");
        RequireProperty(panel, "buttonOfflineBackground");
        RequireProperty(panel, "buttonColor");
        JsonElement markers = RequireProperty(presentation, "markers");
        RequireColor(RequireProperty(markers, "onlineColor"));
        RequireColor(RequireProperty(markers, "offlineColor"));
        RequireColor(RequireProperty(markers, "onlineDotColor"));
        RequireColor(RequireProperty(markers, "offlineDotColor"));
        RequireProperty(markers, "halfSizeMeters");
        RequireProperty(markers, "innerScale");
        RequireProperty(markers, "outerThickness");
        RequireProperty(markers, "innerThickness");
        RequireProperty(markers, "onlineDotRadius");
        RequireProperty(markers, "offlineDotRadius");
        RequireProperty(markers, "dotThickness");
        RequireProperty(markers, "offlineStockEpsilon");
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
            string.IsNullOrWhiteSpace(Presentation.Title) ||
            string.IsNullOrWhiteSpace(Presentation.Copy))
        {
            throw new InvalidOperationException("Player aggregate graph MVP config is missing required string fields.");
        }

        if (PlayerTeamId <= 0)
        {
            throw new InvalidOperationException("Player aggregate graph MVP config requires a positive playerTeamId.");
        }

        if (PanelBindings.Length < 2)
        {
            throw new InvalidOperationException("Player aggregate graph MVP config requires at least two panelBindings.");
        }

        RequireBinding(OreBindingVariableId);
        RequireBinding(CrystalBindingVariableId);
        for (int i = 0; i < PanelBindings.Length; i++)
        {
            UiPlayerAggregatePanelBinding binding = PanelBindings[i];
            if (string.IsNullOrWhiteSpace(binding.VariableId) ||
                string.IsNullOrWhiteSpace(binding.Label) ||
                string.IsNullOrWhiteSpace(binding.GraphOutputKey) ||
                string.IsNullOrWhiteSpace(binding.Accent))
            {
                throw new InvalidOperationException("Player aggregate graph MVP panelBindings require variableId, label, graphOutputKey, and accent.");
            }
        }

        Presentation.Panel.Validate();
        Presentation.Markers.Validate();

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

    private static void RequireColor(JsonElement color)
    {
        RequireProperty(color, "r");
        RequireProperty(color, "g");
        RequireProperty(color, "b");
        RequireProperty(color, "a");
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

public sealed class UiPlayerAggregatePanelBinding
{
    public string VariableId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string GraphOutputKey { get; set; } = string.Empty;
    public string Accent { get; set; } = string.Empty;
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
    public UiPlayerAggregatePanelStyle Panel { get; set; } = new();
    public UiPlayerAggregateMarkerStyle Markers { get; set; } = new();
}

public sealed class UiPlayerAggregatePanelStyle
{
    public float Width { get; set; }
    public float Padding { get; set; }
    public float Gap { get; set; }
    public float Radius { get; set; }
    public string Background { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
    public float BorderThickness { get; set; }
    public string TitleColor { get; set; } = string.Empty;
    public string CopyColor { get; set; } = string.Empty;
    public string GraphMetaColor { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public string ControlsColor { get; set; } = string.Empty;
    public string ChipValueColor { get; set; } = string.Empty;
    public float ChipGap { get; set; }
    public float ButtonPaddingX { get; set; }
    public float ButtonPaddingY { get; set; }
    public float ButtonRadius { get; set; }
    public string ButtonBackground { get; set; } = string.Empty;
    public string ButtonOfflineBackground { get; set; } = string.Empty;
    public string ButtonColor { get; set; } = string.Empty;

    public void Validate()
    {
        if (Width <= 0f ||
            Padding < 0f ||
            Gap < 0f ||
            Radius < 0f ||
            BorderThickness < 0f ||
            ChipGap < 0f ||
            ButtonPaddingX < 0f ||
            ButtonPaddingY < 0f ||
            ButtonRadius < 0f ||
            string.IsNullOrWhiteSpace(Background) ||
            string.IsNullOrWhiteSpace(BorderColor) ||
            string.IsNullOrWhiteSpace(TitleColor) ||
            string.IsNullOrWhiteSpace(CopyColor) ||
            string.IsNullOrWhiteSpace(GraphMetaColor) ||
            string.IsNullOrWhiteSpace(StatusColor) ||
            string.IsNullOrWhiteSpace(ControlsColor) ||
            string.IsNullOrWhiteSpace(ChipValueColor) ||
            string.IsNullOrWhiteSpace(ButtonBackground) ||
            string.IsNullOrWhiteSpace(ButtonOfflineBackground) ||
            string.IsNullOrWhiteSpace(ButtonColor))
        {
            throw new InvalidOperationException("Player aggregate graph MVP presentation.panel requires positive layout values and explicit colors.");
        }
    }
}

public sealed class UiPlayerAggregateMarkerStyle
{
    public UiPlayerAggregateRgbaColor OnlineColor { get; set; } = new();
    public UiPlayerAggregateRgbaColor OfflineColor { get; set; } = new();
    public UiPlayerAggregateRgbaColor OnlineDotColor { get; set; } = new();
    public UiPlayerAggregateRgbaColor OfflineDotColor { get; set; } = new();
    public float HalfSizeMeters { get; set; }
    public float InnerScale { get; set; }
    public float OuterThickness { get; set; }
    public float InnerThickness { get; set; }
    public float OnlineDotRadius { get; set; }
    public float OfflineDotRadius { get; set; }
    public float DotThickness { get; set; }
    public float OfflineStockEpsilon { get; set; }

    public void Validate()
    {
        if (HalfSizeMeters <= 0f ||
            InnerScale <= 0f ||
            OuterThickness <= 0f ||
            InnerThickness <= 0f ||
            OnlineDotRadius <= 0f ||
            OfflineDotRadius <= 0f ||
            DotThickness <= 0f ||
            OfflineStockEpsilon < 0f)
        {
            throw new InvalidOperationException(
                "Player aggregate graph MVP presentation.markers requires positive sizes and non-negative offlineStockEpsilon.");
        }

        OnlineColor.Validate("onlineColor");
        OfflineColor.Validate("offlineColor");
        OnlineDotColor.Validate("onlineDotColor");
        OfflineDotColor.Validate("offlineDotColor");
    }
}

public sealed class UiPlayerAggregateRgbaColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; } = 255;

    public void Validate(string name)
    {
        if (A == 0)
        {
            throw new InvalidOperationException($"Player aggregate graph MVP presentation.markers.{name} requires non-zero alpha.");
        }
    }
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
