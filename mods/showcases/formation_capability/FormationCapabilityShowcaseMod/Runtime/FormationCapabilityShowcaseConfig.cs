using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace FormationCapabilityShowcaseMod.Runtime;

internal sealed class FormationCapabilityShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public FormationCapabilityShowcaseTemplateAuthoringConfig FormationAnchor { get; set; } = new();
    public string InitialCommandSourceFormationId { get; set; } = string.Empty;
    public int InitialCommandSourceEntityCapacity { get; set; }
    public int OrderBatchCapacity { get; set; }
    public FormationCapabilityShowcaseObstacleOverlayConfig ObstacleOverlay { get; set; } = new();
    public FormationCapabilityShowcaseFormationConfig[] Formations { get; set; } = Array.Empty<FormationCapabilityShowcaseFormationConfig>();
    public int FormationOutlineOwnerCapacity => Formations.Length;
    public int FormationOutlineSplineCapacity
    {
        get
        {
            int capacity = 0;
            for (int i = 0; i < Formations.Length; i++)
            {
                capacity += FormationCapabilityShowcaseFormationOutlineSegments.CountSplineSegments(
                    Formations[i].Outline.ResolvedShape,
                    Formations[i].Outline.FrontIndicatorLengthCm > 0f,
                    Formations[i].Outline.CurveSampleCount);
            }

            return capacity;
        }
    }

    public static FormationCapabilityShowcaseConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        FormationCapabilityShowcaseConfig? config = document.RootElement.Deserialize<FormationCapabilityShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize Formation Capability showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(RequireProperty(root, "formationAnchor"), "templateId");
        RequireProperty(root, "initialCommandSourceFormationId");
        RequireProperty(root, "initialCommandSourceEntityCapacity");
        RequireProperty(root, "orderBatchCapacity");
        JsonElement obstacleOverlay = RequireProperty(root, "obstacleOverlay");
        RequireProperties(obstacleOverlay, "templateId", "heightOffsetM", "borderWidthCm", "fillColor", "borderColor");
        JsonElement formations = RequireProperty(root, "formations");
        if (formations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Formation Capability showcase config requires formations as an array.");
        }

        int index = 0;
        foreach (JsonElement formation in formations.EnumerateArray())
        {
            RequireProperty(formation, "id");
            RequireProperty(formation, "label");
            RequireProperty(formation, "teamId");
            RequireProperty(formation, "ownerPlayerId");
            RequireAgentAuthoring(RequireProperty(formation, "soldierAgent"), $"formations[{index}].soldierAgent");
            RequireProperty(formation, "centerXCm");
            RequireProperty(formation, "centerYCm");
            RequireProperty(formation, "facingDeg");
            JsonElement slots = RequireProperty(formation, "slots");
            string slotLayout = RequireString(slots, "layout");
            if (string.Equals(slotLayout, FormationCapabilityShowcaseFormationSlotLayoutNames.Grid, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(slots, "grid"), "columns", "rows", "spacingXCm", "spacingYCm");
            }
            else if (string.Equals(slotLayout, FormationCapabilityShowcaseFormationSlotLayoutNames.Disc, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(slots, "disc"), "count", "ringSpacingCm");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Formation Capability showcase config formations[{index}].slots.layout must be '{FormationCapabilityShowcaseFormationSlotLayoutNames.Grid}' or '{FormationCapabilityShowcaseFormationSlotLayoutNames.Disc}', got '{slotLayout}'.");
            }

            JsonElement outline = RequireProperty(formation, "outline");
            string outlineShape = RequireString(outline, "shape");
            if (string.Equals(outlineShape, FormationCapabilityShowcaseFormationOutlineShapeNames.Rectangle, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(outline, "rectangle"), "widthCm", "depthCm", "edgeLineWidthCm");
            }
            else if (string.Equals(outlineShape, FormationCapabilityShowcaseFormationOutlineShapeNames.Circle, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(outline, "circle"), "radiusCm", "ringWidthCm", "footprintVertexCount");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Formation Capability showcase config formations[{index}].outline.shape must be '{FormationCapabilityShowcaseFormationOutlineShapeNames.Rectangle}' or '{FormationCapabilityShowcaseFormationOutlineShapeNames.Circle}', got '{outlineShape}'.");
            }

            RequireProperty(outline, "heightOffsetM");
            RequireProperty(outline, "curveSampleCount");
            RequireProperty(outline, "emissionPositionEpsilonM");
            RequireProperty(outline, "emissionFacingEpsilonRadians");
            RequireProperty(outline, "frontIndicatorLengthCm");
            RequireProperty(outline, "frontIndicatorLineWidthCm");
            RequireProperty(outline, "fillColor");
            RequireProperty(outline, "borderColor");
            index++;
        }

        if (index <= 0)
        {
            throw new InvalidOperationException("Formation Capability showcase config requires at least one formation.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Formation Capability showcase config requires explicit '{propertyName}' property.");
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

    private static void RequireAgentAuthoring(JsonElement root, string label)
    {
        RequireProperty(root, "templateId");
        RequireProperty(root, "profileId");
    }

    private static string RequireString(JsonElement root, string propertyName)
    {
        JsonElement value = RequireProperty(root, propertyName);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Formation Capability showcase config requires '{propertyName}' as a string.");
        }

        return value.GetString()
            ?? throw new InvalidOperationException($"Formation Capability showcase config requires non-null '{propertyName}' string.");
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        FormationAnchor.Validate(nameof(FormationAnchor));
        RequireNonEmpty(InitialCommandSourceFormationId, nameof(InitialCommandSourceFormationId));
        if (InitialCommandSourceEntityCapacity <= 0)
        {
            throw new InvalidOperationException("Formation Capability showcase config requires initialCommandSourceEntityCapacity > 0.");
        }

        if (OrderBatchCapacity <= 0)
        {
            throw new InvalidOperationException("Formation Capability showcase config requires orderBatchCapacity > 0.");
        }

        ObstacleOverlay.Validate();
        if (Formations.Length <= 0)
        {
            throw new InvalidOperationException("Formation Capability showcase config requires at least one formation.");
        }

        var formationIds = new HashSet<string>(StringComparer.Ordinal);
        bool foundInitialCommandSource = false;
        for (int i = 0; i < Formations.Length; i++)
        {
            Formations[i].Validate(i);
            if (!formationIds.Add(Formations[i].Id))
            {
                throw new InvalidOperationException($"Formation Capability showcase config contains duplicate formation id '{Formations[i].Id}'.");
            }

            foundInitialCommandSource |= string.Equals(Formations[i].Id, InitialCommandSourceFormationId, StringComparison.Ordinal);
        }

        if (!foundInitialCommandSource)
        {
            throw new InvalidOperationException(
                $"Formation Capability showcase initial command-source formation '{InitialCommandSourceFormationId}' is not configured.");
        }
    }

    public void ValidateAgentProfileReferences(MassNavigationAgentProfileSetConfig profileSet, AgentProfileRegistry geometryProfiles)
    {
        for (int i = 0; i < Formations.Length; i++)
        {
            FormationCapabilityShowcaseFormationConfig formation = Formations[i];
            _ = ResolveSoldierAgentProfile(profileSet, i);
            geometryProfiles.Require(formation.SoldierAgent.ProfileId, $"formations[{i}].soldierAgent.profileId");
        }
    }

    public MassNavigationAgentProfileConfig ResolveSoldierAgentProfile(MassNavigationAgentProfileSetConfig profileSet, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)Formations.Length)
        {
            throw new InvalidOperationException(
                $"Formation Capability formation index {formationIndex} exceeds configured formations length {Formations.Length}.");
        }

        return ResolveAgentProfile(
            profileSet,
            Formations[formationIndex].SoldierAgent.ProfileId,
            $"formations[{formationIndex}].soldierAgent.profileId");
    }

    private static MassNavigationAgentProfileConfig ResolveAgentProfile(
        MassNavigationAgentProfileSetConfig profileSet,
        string profileId,
        string label)
    {
        if (profileSet == null)
        {
            throw new ArgumentNullException(nameof(profileSet));
        }

        RequireNonEmpty(profileId, label);
        for (int i = 0; i < profileSet.Profiles.Length; i++)
        {
            MassNavigationAgentProfileConfig profile = profileSet.Profiles[i];
            if (string.Equals(profile.Id, profileId, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        throw new InvalidOperationException(
            $"Formation Capability showcase config {label} references MassNavigation agent profile '{profileId}', but that profile is not configured.");
    }

    internal static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Formation Capability showcase config requires non-empty {fieldName}.");
        }
    }

}

internal sealed class FormationCapabilityShowcaseObstacleOverlayConfig
{
    public string TemplateId { get; set; } = string.Empty;
    public float HeightOffsetM { get; set; }
    public float BorderWidthCm { get; set; }
    public float[] FillColor { get; set; } = Array.Empty<float>();
    public float[] BorderColor { get; set; } = Array.Empty<float>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TemplateId))
        {
            throw new InvalidOperationException("Formation Capability obstacleOverlay requires non-empty templateId.");
        }

        if (!(BorderWidthCm > 0f))
        {
            throw new InvalidOperationException("Formation Capability obstacleOverlay requires BorderWidthCm > 0.");
        }

        ValidateColor(FillColor, nameof(FillColor));
        ValidateColor(BorderColor, nameof(BorderColor));
    }

    public FormationCapabilityShowcaseObstacleOverlay ToComponent(float radiusCm)
    {
        if (!(radiusCm > 0f))
        {
            throw new InvalidOperationException("Formation Capability obstacle overlay requires obstacle radiusCm > 0.");
        }

        return new FormationCapabilityShowcaseObstacleOverlay
        {
            RadiusCm = radiusCm,
            HeightOffsetM = HeightOffsetM,
            BorderWidthCm = BorderWidthCm,
            FillColor = ToVector4(FillColor),
            BorderColor = ToVector4(BorderColor),
        };
    }

    private static void ValidateColor(float[] values, string fieldName)
    {
        if (values.Length != 4)
        {
            throw new InvalidOperationException($"Formation Capability obstacleOverlay requires {fieldName} as [r,g,b,a].");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < 0f || values[i] > 1f)
            {
                throw new InvalidOperationException(
                    $"Formation Capability obstacleOverlay.{fieldName}[{i}] must be between 0 and 1.");
            }
        }
    }

    private static Vector4 ToVector4(float[] values)
    {
        return new Vector4(values[0], values[1], values[2], values[3]);
    }
}

internal sealed class FormationCapabilityShowcaseAgentAuthoringConfig
{
    public string TemplateId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;

    public void Validate(string label)
    {
        RequireNonEmpty(TemplateId, $"{label}.templateId");
        RequireNonEmpty(ProfileId, $"{label}.profileId");
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Formation Capability showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseTemplateAuthoringConfig
{
    public string TemplateId { get; set; } = string.Empty;

    public void Validate(string label)
    {
        FormationCapabilityShowcaseConfig.RequireNonEmpty(TemplateId, $"{label}.templateId");
    }
}

internal sealed class FormationCapabilityShowcaseFormationConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public int OwnerPlayerId { get; set; }
    public FormationCapabilityShowcaseAgentAuthoringConfig SoldierAgent { get; set; } = new();
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public float FacingDeg { get; set; }
    public FormationCapabilityShowcaseFormationSlotConfig Slots { get; set; } = new();
    public FormationCapabilityShowcaseFormationOutlineConfig Outline { get; set; } = new();

    public int SoldierCount => Slots.SoldierCount;

    public void Validate(int index)
    {
        RequireNonEmpty(Id, $"formations[{index}].id");
        RequireNonEmpty(Label, $"formations[{index}].label");
        SoldierAgent.Validate($"formations[{index}].soldierAgent");
        if (TeamId <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{Id}' requires TeamId > 0.");
        }

        if (OwnerPlayerId <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{Id}' requires OwnerPlayerId > 0.");
        }

        Slots.Validate(Id);
        Outline.Validate(Id);
        ValidateSlotOutlineContract();
    }

    private void ValidateSlotOutlineContract()
    {
        FormationCapabilityShowcaseFormationSlotLayout slotLayout = Slots.LayoutKind;
        if (slotLayout == FormationCapabilityShowcaseFormationSlotLayout.Grid &&
            Outline.ResolvedShape != FormationCapabilityShowcaseFormationOutlineShape.Rectangle)
        {
            throw new InvalidOperationException($"Formation Capability formation '{Id}' grid slots require Rectangle outline.");
        }

        if (slotLayout == FormationCapabilityShowcaseFormationSlotLayout.Disc &&
            Outline.ResolvedShape != FormationCapabilityShowcaseFormationOutlineShape.Circle)
        {
            throw new InvalidOperationException($"Formation Capability formation '{Id}' disc slots require Circle outline.");
        }

        if (slotLayout == FormationCapabilityShowcaseFormationSlotLayout.Grid)
        {
            FormationCapabilityShowcaseFormationRectangleOutlineConfig rectangle = Outline.RequiredRectangle;
            if (rectangle.WidthCm < Slots.GridWidthCm || rectangle.DepthCm < Slots.GridDepthCm)
            {
                throw new InvalidOperationException(
                    $"Formation Capability formation '{Id}' Rectangle outline must cover its grid slots.");
            }
        }
        else
        {
            float requiredRadiusCm = Slots.DiscRadiusCm;
            if (Outline.RequiredCircle.RadiusCm < requiredRadiusCm)
            {
                throw new InvalidOperationException(
                    $"Formation Capability formation '{Id}' Circle outline must cover its disc slots.");
            }
        }
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Formation Capability showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseFormationSlotConfig
{
    public string Layout { get; set; } = string.Empty;
    public FormationCapabilityShowcaseFormationGridSlotConfig? Grid { get; set; }
    public FormationCapabilityShowcaseFormationDiscSlotConfig? Disc { get; set; }

    public int SoldierCount => LayoutKind switch
    {
        FormationCapabilityShowcaseFormationSlotLayout.Grid => RequiredGrid.Columns * RequiredGrid.Rows,
        FormationCapabilityShowcaseFormationSlotLayout.Disc => RequiredDisc.Count,
        _ => throw new InvalidOperationException($"Formation Capability formation slots.layout '{Layout}' was not validated."),
    };
    public FormationCapabilityShowcaseFormationSlotLayout ResolvedLayout => ResolveLayout(Layout, "slots.layout");
    public FormationCapabilityShowcaseFormationSlotLayout LayoutKind => ResolvedLayout;
    public FormationCapabilityShowcaseFormationGridSlotConfig RequiredGrid => Grid
        ?? throw new InvalidOperationException("Formation Capability grid formation requires slots.grid.");
    public FormationCapabilityShowcaseFormationDiscSlotConfig RequiredDisc => Disc
        ?? throw new InvalidOperationException("Formation Capability disc formation requires slots.disc.");
    public float GridWidthCm => (RequiredGrid.Columns - 1) * RequiredGrid.SpacingXCm;
    public float GridDepthCm => (RequiredGrid.Rows - 1) * RequiredGrid.SpacingYCm;
    public float DiscRadiusCm => RequiredDisc.Count <= 1
        ? 0f
        : MathF.Sqrt(RequiredDisc.Count - 1) * RequiredDisc.RingSpacingCm;

    public void Validate(string formationId)
    {
        RequireNonEmpty(Layout, $"formations[{formationId}].slots.layout");
        FormationCapabilityShowcaseFormationSlotLayout layout = ResolveLayout(Layout, formationId);
        if (layout == FormationCapabilityShowcaseFormationSlotLayout.Grid)
        {
            RequiredGrid.Validate(formationId);
            if (Disc != null)
            {
                throw new InvalidOperationException($"Formation Capability formation '{formationId}' grid slots must not author slots.disc.");
            }
        }
        else
        {
            RequiredDisc.Validate(formationId);
            if (Grid != null)
            {
                throw new InvalidOperationException($"Formation Capability formation '{formationId}' disc slots must not author slots.grid.");
            }
        }
    }

    private static FormationCapabilityShowcaseFormationSlotLayout ResolveLayout(string layout, string formationId)
    {
        return layout switch
        {
            FormationCapabilityShowcaseFormationSlotLayoutNames.Grid => FormationCapabilityShowcaseFormationSlotLayout.Grid,
            FormationCapabilityShowcaseFormationSlotLayoutNames.Disc => FormationCapabilityShowcaseFormationSlotLayout.Disc,
            _ => throw new InvalidOperationException(
                $"Formation Capability formation '{formationId}' slots.layout must be '{FormationCapabilityShowcaseFormationSlotLayoutNames.Grid}' or '{FormationCapabilityShowcaseFormationSlotLayoutNames.Disc}', got '{layout}'."),
        };
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Formation Capability showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseFormationGridSlotConfig
{
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int SpacingXCm { get; set; }
    public int SpacingYCm { get; set; }

    public void Validate(string formationId)
    {
        if (Columns <= 0 || Rows <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires positive slots.grid columns and rows.");
        }

        if (SpacingXCm <= 0 || SpacingYCm <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires positive slots.grid spacingXCm and spacingYCm.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseFormationDiscSlotConfig
{
    public int Count { get; set; }
    public int RingSpacingCm { get; set; }

    public void Validate(string formationId)
    {
        if (Count <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires slots.disc.count > 0.");
        }

        if (RingSpacingCm <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires slots.disc.ringSpacingCm > 0.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseFormationOutlineConfig
{
    public string Shape { get; set; } = string.Empty;
    public FormationCapabilityShowcaseFormationRectangleOutlineConfig? Rectangle { get; set; }
    public FormationCapabilityShowcaseFormationCircleOutlineConfig? Circle { get; set; }
    public float HeightOffsetM { get; set; }
    public int CurveSampleCount { get; set; }
    public float EmissionPositionEpsilonM { get; set; }
    public float EmissionFacingEpsilonRadians { get; set; }
    public float FrontIndicatorLengthCm { get; set; }
    public float FrontIndicatorLineWidthCm { get; set; }
    public float[] FillColor { get; set; } = Array.Empty<float>();
    public float[] BorderColor { get; set; } = Array.Empty<float>();
    public FormationCapabilityShowcaseFormationOutlineShape ResolvedShape => ResolveShape(Shape, "outline.shape");
    public FormationCapabilityShowcaseFormationRectangleOutlineConfig RequiredRectangle => Rectangle
        ?? throw new InvalidOperationException("Formation Capability rectangle formation requires outline.rectangle.");
    public FormationCapabilityShowcaseFormationCircleOutlineConfig RequiredCircle => Circle
        ?? throw new InvalidOperationException("Formation Capability circle formation requires outline.circle.");

    public void Validate(string formationId)
    {
        RequireNonEmpty(Shape, $"formations[{formationId}].outline.shape");
        FormationCapabilityShowcaseFormationOutlineShape shape = ResolveShape(Shape, formationId);
        if (shape == FormationCapabilityShowcaseFormationOutlineShape.Rectangle)
        {
            RequiredRectangle.Validate(formationId);
            if (Circle != null)
            {
                throw new InvalidOperationException($"Formation Capability formation '{formationId}' rectangle outline must not author outline.circle.");
            }
        }
        else if (shape == FormationCapabilityShowcaseFormationOutlineShape.Circle)
        {
            RequiredCircle.Validate(formationId);
            if (Rectangle != null)
            {
                throw new InvalidOperationException($"Formation Capability formation '{formationId}' circle outline must not author outline.rectangle.");
            }
        }
        else
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' has unsupported outline shape '{Shape}'.");
        }

        RequirePositive(FrontIndicatorLineWidthCm, formationId, nameof(FrontIndicatorLineWidthCm));
        if (CurveSampleCount <= 0)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires outline.CurveSampleCount > 0.");
        }

        RequirePositive(EmissionPositionEpsilonM, formationId, nameof(EmissionPositionEpsilonM));
        RequirePositive(EmissionFacingEpsilonRadians, formationId, nameof(EmissionFacingEpsilonRadians));
        if (FrontIndicatorLengthCm < 0f)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires outline.FrontIndicatorLengthCm >= 0.");
        }

        ValidateColor(FillColor, formationId, nameof(FillColor));
        ValidateColor(BorderColor, formationId, nameof(BorderColor));
    }

    public FormationCapabilityShowcaseFormationOutline ToComponent(string formationId)
    {
        FormationCapabilityShowcaseFormationOutlineShape shape = ResolveShape(Shape, formationId);
        if (shape == FormationCapabilityShowcaseFormationOutlineShape.Rectangle)
        {
            FormationCapabilityShowcaseFormationRectangleOutlineConfig rectangle = RequiredRectangle;
            return new FormationCapabilityShowcaseFormationOutline
            {
                Shape = shape,
                WidthCm = rectangle.WidthCm,
                DepthCm = rectangle.DepthCm,
                RadiusCm = 0f,
                HeightOffsetM = HeightOffsetM,
                CurveSampleCount = CurveSampleCount,
                EmissionPositionEpsilonM = EmissionPositionEpsilonM,
                EmissionFacingEpsilonRadians = EmissionFacingEpsilonRadians,
                EdgeLineWidthCm = rectangle.EdgeLineWidthCm,
                CircleRingWidthCm = 0f,
                FrontIndicatorLengthCm = FrontIndicatorLengthCm,
                FrontIndicatorLineWidthCm = FrontIndicatorLineWidthCm,
                FillColor = ToVector4(FillColor),
                BorderColor = ToVector4(BorderColor),
            };
        }

        if (shape == FormationCapabilityShowcaseFormationOutlineShape.Circle)
        {
            FormationCapabilityShowcaseFormationCircleOutlineConfig circle = RequiredCircle;
            return new FormationCapabilityShowcaseFormationOutline
            {
                Shape = shape,
                WidthCm = 0f,
                DepthCm = 0f,
                RadiusCm = circle.RadiusCm,
                HeightOffsetM = HeightOffsetM,
                CurveSampleCount = CurveSampleCount,
                EmissionPositionEpsilonM = EmissionPositionEpsilonM,
                EmissionFacingEpsilonRadians = EmissionFacingEpsilonRadians,
                EdgeLineWidthCm = 0f,
                CircleRingWidthCm = circle.RingWidthCm,
                FrontIndicatorLengthCm = FrontIndicatorLengthCm,
                FrontIndicatorLineWidthCm = FrontIndicatorLineWidthCm,
                FillColor = ToVector4(FillColor),
                BorderColor = ToVector4(BorderColor),
            };
        }

        throw new InvalidOperationException($"Formation Capability formation '{formationId}' has unsupported outline shape '{Shape}'.");
    }

    public SpatialBounds ToSpatialBounds()
    {
        return new SpatialBounds
        {
            Kind = SpatialBoundsKind.Footprint2D,
            LocalCenterXCm = 0,
            LocalCenterYCm = 0,
            LocalCenterZCm = 0,
        };
    }

    public SpatialFootprint2D ToSpatialFootprint(string formationId)
    {
        FormationCapabilityShowcaseFormationOutlineShape shape = ResolveShape(Shape, formationId);
        var footprint = new SpatialFootprint2D();
        if (shape == FormationCapabilityShowcaseFormationOutlineShape.Rectangle)
        {
            FormationCapabilityShowcaseFormationRectangleOutlineConfig rectangle = RequiredRectangle;
            int halfWidthCm = CheckedRoundToInt(rectangle.WidthCm * 0.5f, formationId, "outline.rectangle.widthCm");
            int halfDepthCm = CheckedRoundToInt(rectangle.DepthCm * 0.5f, formationId, "outline.rectangle.depthCm");
            footprint.SetPolygonVertexCount(0, 4);
            footprint.SetVertex(0, 0, new WorldCmInt2(-halfWidthCm, -halfDepthCm));
            footprint.SetVertex(0, 1, new WorldCmInt2(halfWidthCm, -halfDepthCm));
            footprint.SetVertex(0, 2, new WorldCmInt2(halfWidthCm, halfDepthCm));
            footprint.SetVertex(0, 3, new WorldCmInt2(-halfWidthCm, halfDepthCm));
            return footprint;
        }

        if (shape == FormationCapabilityShowcaseFormationOutlineShape.Circle)
        {
            FormationCapabilityShowcaseFormationCircleOutlineConfig circle = RequiredCircle;
            int vertexCount = circle.FootprintVertexCount;
            int radiusCm = CheckedRoundToInt(circle.RadiusCm, formationId, "outline.circle.radiusCm");
            footprint.SetPolygonVertexCount(0, vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                float angle = (MathF.Tau * i) / vertexCount;
                int xCm = CheckedRoundToInt(MathF.Cos(angle) * radiusCm, formationId, "outline.circle.radiusCm");
                int zCm = CheckedRoundToInt(MathF.Sin(angle) * radiusCm, formationId, "outline.circle.radiusCm");
                footprint.SetVertex(0, i, new WorldCmInt2(xCm, zCm));
            }

            return footprint;
        }

        throw new InvalidOperationException($"Formation Capability formation '{formationId}' has unsupported outline shape '{Shape}'.");
    }

    private static int CheckedRoundToInt(float value, string formationId, string fieldName)
    {
        if (!float.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' {fieldName} cannot be represented as integer cm.");
        }

        return (int)MathF.Round(value);
    }

    private static FormationCapabilityShowcaseFormationOutlineShape ResolveShape(string shape, string formationId)
    {
        return shape switch
        {
            FormationCapabilityShowcaseFormationOutlineShapeNames.Rectangle => FormationCapabilityShowcaseFormationOutlineShape.Rectangle,
            FormationCapabilityShowcaseFormationOutlineShapeNames.Circle => FormationCapabilityShowcaseFormationOutlineShape.Circle,
            _ => throw new InvalidOperationException(
                $"Formation Capability formation '{formationId}' outline.shape must be '{FormationCapabilityShowcaseFormationOutlineShapeNames.Rectangle}' or '{FormationCapabilityShowcaseFormationOutlineShapeNames.Circle}', got '{shape}'."),
        };
    }

    private static void RequirePositive(float value, string formationId, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires outline.{fieldName} > 0.");
        }
    }

    private static void ValidateColor(float[] values, string formationId, string fieldName)
    {
        if (values.Length != 4)
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires outline.{fieldName} as [r,g,b,a].");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < 0f || values[i] > 1f)
            {
                throw new InvalidOperationException(
                    $"Formation Capability formation '{formationId}' outline.{fieldName}[{i}] must be between 0 and 1.");
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
            throw new InvalidOperationException($"Formation Capability showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseFormationRectangleOutlineConfig
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
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires outline.rectangle.{fieldName} > 0.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseFormationCircleOutlineConfig
{
    public float RadiusCm { get; set; }
    public float RingWidthCm { get; set; }
    public int FootprintVertexCount { get; set; }

    public void Validate(string formationId)
    {
        RequirePositive(RadiusCm, formationId, nameof(RadiusCm));
        RequirePositive(RingWidthCm, formationId, nameof(RingWidthCm));
        if (FootprintVertexCount < 3 || FootprintVertexCount > SpatialFootprint2D.MaxVerticesPerPolygon)
        {
            throw new InvalidOperationException(
                $"Formation Capability formation '{formationId}' requires outline.circle.FootprintVertexCount between 3 and {SpatialFootprint2D.MaxVerticesPerPolygon}.");
        }
    }

    private static void RequirePositive(float value, string formationId, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Formation Capability formation '{formationId}' requires outline.circle.{fieldName} > 0.");
        }
    }
}

internal sealed class FormationCapabilityShowcaseConfigLoader
{
    public const string RelativePath = "FormationCapabilityShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public FormationCapabilityShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public FormationCapabilityShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Formation Capability showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Formation Capability showcase config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Formation Capability showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return FormationCapabilityShowcaseConfig.Load(merged);
    }
}
