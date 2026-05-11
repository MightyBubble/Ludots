using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace MassNavigationTotalWarEntryMod.Runtime;

internal sealed class TotalWarShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public string RuntimeSpawnReceiptChannelKey { get; set; } = string.Empty;
    public string FormationAnchorTemplateId { get; set; } = string.Empty;
    public string InitialSelectionFormationId { get; set; } = string.Empty;
    public TotalWarFormationSyncConfig FormationSync { get; set; } = new();
    public TotalWarFormationConfig[] Formations { get; set; } = Array.Empty<TotalWarFormationConfig>();

    public static TotalWarShowcaseConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        TotalWarShowcaseConfig? config = document.RootElement.Deserialize<TotalWarShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize Total War showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "runtimeSpawnReceiptChannelKey");
        RequireProperty(root, "formationAnchorTemplateId");
        RequireProperty(root, "initialSelectionFormationId");
        JsonElement formationSync = RequireProperty(root, "formationSync");
        RequireProperty(formationSync, "facingVelocityEpsilonCmPerSecond");
        JsonElement formations = RequireProperty(root, "formations");
        if (formations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Total War showcase config requires formations as an array.");
        }

        int index = 0;
        foreach (JsonElement formation in formations.EnumerateArray())
        {
            RequireProperty(formation, "id");
            RequireProperty(formation, "label");
            RequireProperty(formation, "teamId");
            RequireProperty(formation, "templateId");
            RequireProperty(formation, "heavy");
            RequireProperty(formation, "navMass");
            RequireProperty(formation, "visualScale");
            RequireProperty(formation, "centerXCm");
            RequireProperty(formation, "centerYCm");
            RequireProperty(formation, "facingDeg");
            JsonElement slots = RequireProperty(formation, "slots");
            string slotLayout = RequireString(slots, "layout");
            if (string.Equals(slotLayout, TotalWarFormationSlotLayoutNames.Grid, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(slots, "grid"), "columns", "rows", "spacingXCm", "spacingYCm");
            }
            else if (string.Equals(slotLayout, TotalWarFormationSlotLayoutNames.Disc, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(slots, "disc"), "count", "ringSpacingCm");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Total War showcase config formations[{index}].slots.layout must be '{TotalWarFormationSlotLayoutNames.Grid}' or '{TotalWarFormationSlotLayoutNames.Disc}', got '{slotLayout}'.");
            }

            JsonElement outline = RequireProperty(formation, "outline");
            string outlineShape = RequireString(outline, "shape");
            if (string.Equals(outlineShape, TotalWarFormationOutlineShapeNames.Rectangle, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(outline, "rectangle"), "widthCm", "depthCm", "edgeLineWidthCm");
            }
            else if (string.Equals(outlineShape, TotalWarFormationOutlineShapeNames.Circle, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(outline, "circle"), "radiusCm", "ringWidthCm");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Total War showcase config formations[{index}].outline.shape must be '{TotalWarFormationOutlineShapeNames.Rectangle}' or '{TotalWarFormationOutlineShapeNames.Circle}', got '{outlineShape}'.");
            }

            RequireProperty(outline, "heightOffsetM");
            RequireProperty(outline, "frontIndicatorLengthCm");
            RequireProperty(outline, "frontIndicatorLineWidthCm");
            RequireProperty(outline, "fillColor");
            RequireProperty(outline, "borderColor");
            index++;
        }

        if (index <= 0)
        {
            throw new InvalidOperationException("Total War showcase config requires at least one formation.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Total War showcase config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireProperties(JsonElement root, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            RequireProperty(root, propertyNames[i]);
        }
    }

    private static string RequireString(JsonElement root, string propertyName)
    {
        JsonElement value = RequireProperty(root, propertyName);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new InvalidOperationException($"Total War showcase config requires '{propertyName}' as a string.");
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(RuntimeSpawnReceiptChannelKey, nameof(RuntimeSpawnReceiptChannelKey));
        RequireNonEmpty(FormationAnchorTemplateId, nameof(FormationAnchorTemplateId));
        RequireNonEmpty(InitialSelectionFormationId, nameof(InitialSelectionFormationId));
        FormationSync.Validate();
        if (Formations.Length <= 0)
        {
            throw new InvalidOperationException("Total War showcase config requires at least one formation.");
        }

        var formationIds = new HashSet<string>(StringComparer.Ordinal);
        bool foundInitialSelection = false;
        for (int i = 0; i < Formations.Length; i++)
        {
            Formations[i].Validate(i);
            if (!formationIds.Add(Formations[i].Id))
            {
                throw new InvalidOperationException($"Total War showcase config contains duplicate formation id '{Formations[i].Id}'.");
            }

            foundInitialSelection |= string.Equals(Formations[i].Id, InitialSelectionFormationId, StringComparison.Ordinal);
        }

        if (!foundInitialSelection)
        {
            throw new InvalidOperationException(
                $"Total War showcase initial selection formation '{InitialSelectionFormationId}' is not configured.");
        }
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Total War showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class TotalWarFormationSyncConfig
{
    public float FacingVelocityEpsilonCmPerSecond { get; set; }

    public void Validate()
    {
        if (!(FacingVelocityEpsilonCmPerSecond > 0f))
        {
            throw new InvalidOperationException("Total War showcase formationSync requires FacingVelocityEpsilonCmPerSecond > 0.");
        }
    }
}

internal sealed class TotalWarFormationConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public bool Heavy { get; set; }
    public float NavMass { get; set; }
    public float VisualScale { get; set; }
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public float FacingDeg { get; set; }
    public TotalWarFormationSlotConfig Slots { get; set; } = new();
    public TotalWarFormationOutlineConfig Outline { get; set; } = new();

    public int SoldierCount => Slots.SoldierCount;

    public void Validate(int index)
    {
        RequireNonEmpty(Id, $"formations[{index}].id");
        RequireNonEmpty(Label, $"formations[{index}].label");
        RequireNonEmpty(TemplateId, $"formations[{index}].templateId");
        if (TeamId <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' requires TeamId > 0.");
        }

        if (!(NavMass > 0f))
        {
            throw new InvalidOperationException($"Total War formation '{Id}' requires NavMass > 0.");
        }

        if (!(VisualScale > 0f))
        {
            throw new InvalidOperationException($"Total War formation '{Id}' requires VisualScale > 0.");
        }

        Slots.Validate(Id);
        Outline.Validate(Id);
        ValidateSlotOutlineContract();
    }

    private void ValidateSlotOutlineContract()
    {
        TotalWarFormationSlotLayout slotLayout = Slots.LayoutKind;
        if (slotLayout == TotalWarFormationSlotLayout.Grid &&
            Outline.ResolvedShape != TotalWarFormationOutlineShape.Rectangle)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' grid slots require Rectangle outline.");
        }

        if (slotLayout == TotalWarFormationSlotLayout.Disc &&
            Outline.ResolvedShape != TotalWarFormationOutlineShape.Circle)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' disc slots require Circle outline.");
        }

        if (slotLayout == TotalWarFormationSlotLayout.Grid)
        {
            TotalWarFormationRectangleOutlineConfig rectangle = Outline.RequiredRectangle;
            if (rectangle.WidthCm < Slots.GridWidthCm || rectangle.DepthCm < Slots.GridDepthCm)
            {
                throw new InvalidOperationException(
                    $"Total War formation '{Id}' Rectangle outline must cover its grid slots.");
            }
        }
        else
        {
            float requiredRadiusCm = Slots.DiscRadiusCm;
            if (Outline.RequiredCircle.RadiusCm < requiredRadiusCm)
            {
                throw new InvalidOperationException(
                    $"Total War formation '{Id}' Circle outline must cover its disc slots.");
            }
        }
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Total War showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class TotalWarFormationSlotConfig
{
    public string Layout { get; set; } = string.Empty;
    public TotalWarFormationGridSlotConfig? Grid { get; set; }
    public TotalWarFormationDiscSlotConfig? Disc { get; set; }

    public int SoldierCount => LayoutKind switch
    {
        TotalWarFormationSlotLayout.Grid => RequiredGrid.Columns * RequiredGrid.Rows,
        TotalWarFormationSlotLayout.Disc => RequiredDisc.Count,
        _ => throw new InvalidOperationException($"Total War formation slots.layout '{Layout}' was not validated."),
    };
    public TotalWarFormationSlotLayout ResolvedLayout => ResolveLayout(Layout, "slots.layout");
    public TotalWarFormationSlotLayout LayoutKind => ResolvedLayout;
    public TotalWarFormationGridSlotConfig RequiredGrid => Grid
        ?? throw new InvalidOperationException("Total War grid formation requires slots.grid.");
    public TotalWarFormationDiscSlotConfig RequiredDisc => Disc
        ?? throw new InvalidOperationException("Total War disc formation requires slots.disc.");
    public float GridWidthCm => (RequiredGrid.Columns - 1) * RequiredGrid.SpacingXCm;
    public float GridDepthCm => (RequiredGrid.Rows - 1) * RequiredGrid.SpacingYCm;
    public float DiscRadiusCm => RequiredDisc.Count <= 1
        ? 0f
        : MathF.Sqrt(RequiredDisc.Count - 1) * RequiredDisc.RingSpacingCm;

    public void Validate(string formationId)
    {
        RequireNonEmpty(Layout, $"formations[{formationId}].slots.layout");
        TotalWarFormationSlotLayout layout = ResolveLayout(Layout, formationId);
        if (layout == TotalWarFormationSlotLayout.Grid)
        {
            RequiredGrid.Validate(formationId);
            if (Disc != null)
            {
                throw new InvalidOperationException($"Total War formation '{formationId}' grid slots must not author slots.disc.");
            }
        }
        else
        {
            RequiredDisc.Validate(formationId);
            if (Grid != null)
            {
                throw new InvalidOperationException($"Total War formation '{formationId}' disc slots must not author slots.grid.");
            }
        }
    }

    private static TotalWarFormationSlotLayout ResolveLayout(string layout, string formationId)
    {
        return layout switch
        {
            TotalWarFormationSlotLayoutNames.Grid => TotalWarFormationSlotLayout.Grid,
            TotalWarFormationSlotLayoutNames.Disc => TotalWarFormationSlotLayout.Disc,
            _ => throw new InvalidOperationException(
                $"Total War formation '{formationId}' slots.layout must be '{TotalWarFormationSlotLayoutNames.Grid}' or '{TotalWarFormationSlotLayoutNames.Disc}', got '{layout}'."),
        };
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Total War showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class TotalWarFormationGridSlotConfig
{
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int SpacingXCm { get; set; }
    public int SpacingYCm { get; set; }

    public void Validate(string formationId)
    {
        if (Columns <= 0 || Rows <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires positive slots.grid columns and rows.");
        }

        if (SpacingXCm <= 0 || SpacingYCm <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires positive slots.grid spacingXCm and spacingYCm.");
        }
    }
}

internal sealed class TotalWarFormationDiscSlotConfig
{
    public int Count { get; set; }
    public int RingSpacingCm { get; set; }

    public void Validate(string formationId)
    {
        if (Count <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires slots.disc.count > 0.");
        }

        if (RingSpacingCm <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires slots.disc.ringSpacingCm > 0.");
        }
    }
}

internal sealed class TotalWarFormationOutlineConfig
{
    public string Shape { get; set; } = string.Empty;
    public TotalWarFormationRectangleOutlineConfig? Rectangle { get; set; }
    public TotalWarFormationCircleOutlineConfig? Circle { get; set; }
    public float HeightOffsetM { get; set; }
    public float FrontIndicatorLengthCm { get; set; }
    public float FrontIndicatorLineWidthCm { get; set; }
    public float[] FillColor { get; set; } = Array.Empty<float>();
    public float[] BorderColor { get; set; } = Array.Empty<float>();
    public TotalWarFormationOutlineShape ResolvedShape => ResolveShape(Shape, "outline.shape");
    public TotalWarFormationRectangleOutlineConfig RequiredRectangle => Rectangle
        ?? throw new InvalidOperationException("Total War rectangle formation requires outline.rectangle.");
    public TotalWarFormationCircleOutlineConfig RequiredCircle => Circle
        ?? throw new InvalidOperationException("Total War circle formation requires outline.circle.");

    public void Validate(string formationId)
    {
        RequireNonEmpty(Shape, $"formations[{formationId}].outline.shape");
        TotalWarFormationOutlineShape shape = ResolveShape(Shape, formationId);
        if (shape == TotalWarFormationOutlineShape.Rectangle)
        {
            RequiredRectangle.Validate(formationId);
            if (Circle != null)
            {
                throw new InvalidOperationException($"Total War formation '{formationId}' rectangle outline must not author outline.circle.");
            }
        }
        else if (shape == TotalWarFormationOutlineShape.Circle)
        {
            RequiredCircle.Validate(formationId);
            if (Rectangle != null)
            {
                throw new InvalidOperationException($"Total War formation '{formationId}' circle outline must not author outline.rectangle.");
            }
        }
        else
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' has unsupported outline shape '{Shape}'.");
        }

        RequirePositive(FrontIndicatorLineWidthCm, formationId, nameof(FrontIndicatorLineWidthCm));
        if (FrontIndicatorLengthCm < 0f)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.FrontIndicatorLengthCm >= 0.");
        }

        ValidateColor(FillColor, formationId, nameof(FillColor));
        ValidateColor(BorderColor, formationId, nameof(BorderColor));
    }

    public TotalWarFormationOutline ToComponent(string formationId)
    {
        return new TotalWarFormationOutline
        {
            Shape = ResolveShape(Shape, formationId),
            WidthCm = Rectangle?.WidthCm ?? 0f,
            DepthCm = Rectangle?.DepthCm ?? 0f,
            RadiusCm = Circle?.RadiusCm ?? 0f,
            HeightOffsetM = HeightOffsetM,
            EdgeLineWidthCm = Rectangle?.EdgeLineWidthCm ?? 0f,
            CircleRingWidthCm = Circle?.RingWidthCm ?? 0f,
            FrontIndicatorLengthCm = FrontIndicatorLengthCm,
            FrontIndicatorLineWidthCm = FrontIndicatorLineWidthCm,
            FillColor = ToVector4(FillColor),
            BorderColor = ToVector4(BorderColor),
        };
    }

    private static TotalWarFormationOutlineShape ResolveShape(string shape, string formationId)
    {
        return shape switch
        {
            TotalWarFormationOutlineShapeNames.Rectangle => TotalWarFormationOutlineShape.Rectangle,
            TotalWarFormationOutlineShapeNames.Circle => TotalWarFormationOutlineShape.Circle,
            _ => throw new InvalidOperationException(
                $"Total War formation '{formationId}' outline.shape must be '{TotalWarFormationOutlineShapeNames.Rectangle}' or '{TotalWarFormationOutlineShapeNames.Circle}', got '{shape}'."),
        };
    }

    private static void RequirePositive(float value, string formationId, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.{fieldName} > 0.");
        }
    }

    private static void ValidateColor(float[] values, string formationId, string fieldName)
    {
        if (values.Length != 4)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.{fieldName} as [r,g,b,a].");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < 0f || values[i] > 1f)
            {
                throw new InvalidOperationException(
                    $"Total War formation '{formationId}' outline.{fieldName}[{i}] must be between 0 and 1.");
            }
        }
    }

    private static Vector4 ToVector4(float[] values)
    {
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Total War showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class TotalWarFormationRectangleOutlineConfig
{
    public float WidthCm { get; set; }
    public float DepthCm { get; set; }
    public float EdgeLineWidthCm { get; set; }

    public void Validate(string formationId)
    {
        RequirePositive(WidthCm, formationId, nameof(WidthCm));
        RequirePositive(DepthCm, formationId, nameof(DepthCm));
        RequirePositive(EdgeLineWidthCm, formationId, nameof(EdgeLineWidthCm));
    }

    private static void RequirePositive(float value, string formationId, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.rectangle.{fieldName} > 0.");
        }
    }
}

internal sealed class TotalWarFormationCircleOutlineConfig
{
    public float RadiusCm { get; set; }
    public float RingWidthCm { get; set; }

    public void Validate(string formationId)
    {
        RequirePositive(RadiusCm, formationId, nameof(RadiusCm));
        RequirePositive(RingWidthCm, formationId, nameof(RingWidthCm));
    }

    private static void RequirePositive(float value, string formationId, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.circle.{fieldName} > 0.");
        }
    }
}

internal sealed class TotalWarShowcaseConfigLoader
{
    public const string RelativePath = "TotalWarShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public TotalWarShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public TotalWarShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Total War showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Total War showcase config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Total War showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return TotalWarShowcaseConfig.Load(merged);
    }
}
