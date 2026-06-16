using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using MassNavigationMod.Runtime;

namespace CapabilityStandardTotalWarLikeMod.Runtime;

internal sealed class CapabilityStandardTotalWarLikeConfig
{
    public string MapId { get; set; } = string.Empty;
    public string RuntimeSpawnReceiptChannelKey { get; set; } = string.Empty;
    public CapabilityStandardTotalWarLikeAgentAuthoringConfig FormationAgent { get; set; } = new();
    public string InitialSelectionFormationId { get; set; } = string.Empty;
    public int InitialSelectionEntityCapacity { get; set; }
    public CapabilityStandardTotalWarLikeSoldierTargetSyncConfig SoldierTargetSync { get; set; } = new();
    public CapabilityStandardTotalWarLikeObstacleOverlayConfig ObstacleOverlay { get; set; } = new();
    public CapabilityStandardTotalWarLikeFormationConfig[] Formations { get; set; } = Array.Empty<CapabilityStandardTotalWarLikeFormationConfig>();
    public int FormationOutlineOwnerCapacity => Formations.Length;
    public int FormationOutlineSplineCapacity
    {
        get
        {
            int capacity = 0;
            for (int i = 0; i < Formations.Length; i++)
            {
                capacity += CapabilityStandardTotalWarLikeFormationOutlineSegments.CountSplineSegments(
                    Formations[i].Outline.ResolvedShape,
                    Formations[i].Outline.FrontIndicatorLengthCm > 0f,
                    Formations[i].Outline.CurveSampleCount);
            }

            return capacity;
        }
    }

    public static CapabilityStandardTotalWarLikeConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        CapabilityStandardTotalWarLikeConfig? config = document.RootElement.Deserialize<CapabilityStandardTotalWarLikeConfig>(options);
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
        RequireAgentAuthoring(RequireProperty(root, "formationAgent"), "formationAgent");
        RequireProperty(root, "initialSelectionFormationId");
        RequireProperty(root, "initialSelectionEntityCapacity");
        JsonElement soldierTargetSync = RequireProperty(root, "soldierTargetSync");
        RequireProperties(
            soldierTargetSync,
            "targetChangeEpsilonCm",
            "facingChangeEpsilonRadians");
        JsonElement obstacleOverlay = RequireProperty(root, "obstacleOverlay");
        RequireProperties(obstacleOverlay, "templateId", "heightOffsetM", "borderWidthCm", "fillColor", "borderColor");
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
            RequireProperty(formation, "ownerPlayerId");
            RequireAgentAuthoring(RequireProperty(formation, "soldierAgent"), $"formations[{index}].soldierAgent");
            RequireProperty(formation, "centerXCm");
            RequireProperty(formation, "centerYCm");
            RequireProperty(formation, "facingDeg");
            JsonElement slots = RequireProperty(formation, "slots");
            string slotLayout = RequireString(slots, "layout");
            if (string.Equals(slotLayout, CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Grid, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(slots, "grid"), "columns", "rows", "spacingXCm", "spacingYCm");
            }
            else if (string.Equals(slotLayout, CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Disc, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(slots, "disc"), "count", "ringSpacingCm");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Total War showcase config formations[{index}].slots.layout must be '{CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Grid}' or '{CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Disc}', got '{slotLayout}'.");
            }

            JsonElement outline = RequireProperty(formation, "outline");
            string outlineShape = RequireString(outline, "shape");
            if (string.Equals(outlineShape, CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Rectangle, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(outline, "rectangle"), "widthCm", "depthCm", "edgeLineWidthCm");
            }
            else if (string.Equals(outlineShape, CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Circle, StringComparison.Ordinal))
            {
                RequireProperties(RequireProperty(outline, "circle"), "radiusCm", "ringWidthCm", "footprintVertexCount");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Total War showcase config formations[{index}].outline.shape must be '{CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Rectangle}' or '{CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Circle}', got '{outlineShape}'.");
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
            throw new InvalidOperationException($"Total War showcase config requires '{propertyName}' as a string.");
        }

        return value.GetString()
            ?? throw new InvalidOperationException($"Total War showcase config requires non-null '{propertyName}' string.");
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(RuntimeSpawnReceiptChannelKey, nameof(RuntimeSpawnReceiptChannelKey));
        FormationAgent.Validate(nameof(FormationAgent));
        RequireNonEmpty(InitialSelectionFormationId, nameof(InitialSelectionFormationId));
        if (InitialSelectionEntityCapacity <= 0)
        {
            throw new InvalidOperationException("Total War showcase config requires initialSelectionEntityCapacity > 0.");
        }

        SoldierTargetSync.Validate();
        ObstacleOverlay.Validate();
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

    public void ValidateAgentProfileReferences(MassNavigationAgentProfileSetConfig profileSet)
    {
        MassNavigationAgentProfileConfig formationProfile = ResolveFormationAgentProfile(profileSet);
        for (int i = 0; i < Formations.Length; i++)
        {
            CapabilityStandardTotalWarLikeFormationConfig formation = Formations[i];
            MassNavigationAgentProfileConfig soldierProfile = ResolveSoldierAgentProfile(profileSet, i);
            if (!(soldierProfile.SpeedCmPerSecond > formationProfile.SpeedCmPerSecond))
            {
                throw new InvalidOperationException(
                    $"Total War formation '{formation.Id}' at formations[{i}] requires soldierAgent.profileId '{formation.SoldierAgent.ProfileId}' speedCmPerSecond ({soldierProfile.SpeedCmPerSecond}) > formationAgent.profileId '{FormationAgent.ProfileId}' speedCmPerSecond ({formationProfile.SpeedCmPerSecond}).");
            }
        }
    }

    public MassNavigationAgentProfileConfig ResolveFormationAgentProfile(MassNavigationAgentProfileSetConfig profileSet)
    {
        return ResolveAgentProfile(profileSet, FormationAgent.ProfileId, "formationAgent.profileId");
    }

    public MassNavigationAgentProfileConfig ResolveSoldierAgentProfile(MassNavigationAgentProfileSetConfig profileSet, int formationIndex)
    {
        if ((uint)formationIndex >= (uint)Formations.Length)
        {
            throw new InvalidOperationException(
                $"Total War formation index {formationIndex} exceeds configured formations length {Formations.Length}.");
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
            $"Total War showcase config {label} references MassNavigation agent profile '{profileId}', but that profile is not configured.");
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Total War showcase config requires non-empty {fieldName}.");
        }
    }

}

internal sealed class CapabilityStandardTotalWarLikeSoldierTargetSyncConfig
{
    public float TargetChangeEpsilonCm { get; set; }
    public float FacingChangeEpsilonRadians { get; set; }

    public void Validate()
    {
        if (!(TargetChangeEpsilonCm > 0f))
        {
            throw new InvalidOperationException("Total War showcase soldierTargetSync requires TargetChangeEpsilonCm > 0.");
        }

        if (!(FacingChangeEpsilonRadians > 0f))
        {
            throw new InvalidOperationException("Total War showcase soldierTargetSync requires FacingChangeEpsilonRadians > 0.");
        }
    }
}

internal sealed class CapabilityStandardTotalWarLikeObstacleOverlayConfig
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
            throw new InvalidOperationException("Total War obstacleOverlay requires non-empty templateId.");
        }

        if (!(BorderWidthCm > 0f))
        {
            throw new InvalidOperationException("Total War obstacleOverlay requires BorderWidthCm > 0.");
        }

        ValidateColor(FillColor, nameof(FillColor));
        ValidateColor(BorderColor, nameof(BorderColor));
    }

    public CapabilityStandardTotalWarLikeObstacleOverlay ToComponent(float radiusCm)
    {
        if (!(radiusCm > 0f))
        {
            throw new InvalidOperationException("Total War obstacle overlay requires obstacle radiusCm > 0.");
        }

        return new CapabilityStandardTotalWarLikeObstacleOverlay
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
            throw new InvalidOperationException($"Total War obstacleOverlay requires {fieldName} as [r,g,b,a].");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < 0f || values[i] > 1f)
            {
                throw new InvalidOperationException(
                    $"Total War obstacleOverlay.{fieldName}[{i}] must be between 0 and 1.");
            }
        }
    }

    private static Vector4 ToVector4(float[] values)
    {
        return new Vector4(values[0], values[1], values[2], values[3]);
    }
}

internal sealed class CapabilityStandardTotalWarLikeAgentAuthoringConfig
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
            throw new InvalidOperationException($"Total War showcase config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class CapabilityStandardTotalWarLikeFormationConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public int OwnerPlayerId { get; set; }
    public CapabilityStandardTotalWarLikeAgentAuthoringConfig SoldierAgent { get; set; } = new();
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public float FacingDeg { get; set; }
    public CapabilityStandardTotalWarLikeFormationSlotConfig Slots { get; set; } = new();
    public CapabilityStandardTotalWarLikeFormationOutlineConfig Outline { get; set; } = new();

    public int SoldierCount => Slots.SoldierCount;

    public void Validate(int index)
    {
        RequireNonEmpty(Id, $"formations[{index}].id");
        RequireNonEmpty(Label, $"formations[{index}].label");
        SoldierAgent.Validate($"formations[{index}].soldierAgent");
        if (TeamId <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' requires TeamId > 0.");
        }

        if (OwnerPlayerId <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' requires OwnerPlayerId > 0.");
        }

        Slots.Validate(Id);
        Outline.Validate(Id);
        ValidateSlotOutlineContract();
    }

    private void ValidateSlotOutlineContract()
    {
        CapabilityStandardTotalWarLikeFormationSlotLayout slotLayout = Slots.LayoutKind;
        if (slotLayout == CapabilityStandardTotalWarLikeFormationSlotLayout.Grid &&
            Outline.ResolvedShape != CapabilityStandardTotalWarLikeFormationOutlineShape.Rectangle)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' grid slots require Rectangle outline.");
        }

        if (slotLayout == CapabilityStandardTotalWarLikeFormationSlotLayout.Disc &&
            Outline.ResolvedShape != CapabilityStandardTotalWarLikeFormationOutlineShape.Circle)
        {
            throw new InvalidOperationException($"Total War formation '{Id}' disc slots require Circle outline.");
        }

        if (slotLayout == CapabilityStandardTotalWarLikeFormationSlotLayout.Grid)
        {
            CapabilityStandardTotalWarLikeFormationRectangleOutlineConfig rectangle = Outline.RequiredRectangle;
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

internal sealed class CapabilityStandardTotalWarLikeFormationSlotConfig
{
    public string Layout { get; set; } = string.Empty;
    public CapabilityStandardTotalWarLikeFormationGridSlotConfig? Grid { get; set; }
    public CapabilityStandardTotalWarLikeFormationDiscSlotConfig? Disc { get; set; }

    public int SoldierCount => LayoutKind switch
    {
        CapabilityStandardTotalWarLikeFormationSlotLayout.Grid => RequiredGrid.Columns * RequiredGrid.Rows,
        CapabilityStandardTotalWarLikeFormationSlotLayout.Disc => RequiredDisc.Count,
        _ => throw new InvalidOperationException($"Total War formation slots.layout '{Layout}' was not validated."),
    };
    public CapabilityStandardTotalWarLikeFormationSlotLayout ResolvedLayout => ResolveLayout(Layout, "slots.layout");
    public CapabilityStandardTotalWarLikeFormationSlotLayout LayoutKind => ResolvedLayout;
    public CapabilityStandardTotalWarLikeFormationGridSlotConfig RequiredGrid => Grid
        ?? throw new InvalidOperationException("Total War grid formation requires slots.grid.");
    public CapabilityStandardTotalWarLikeFormationDiscSlotConfig RequiredDisc => Disc
        ?? throw new InvalidOperationException("Total War disc formation requires slots.disc.");
    public float GridWidthCm => (RequiredGrid.Columns - 1) * RequiredGrid.SpacingXCm;
    public float GridDepthCm => (RequiredGrid.Rows - 1) * RequiredGrid.SpacingYCm;
    public float DiscRadiusCm => RequiredDisc.Count <= 1
        ? 0f
        : MathF.Sqrt(RequiredDisc.Count - 1) * RequiredDisc.RingSpacingCm;

    public void Validate(string formationId)
    {
        RequireNonEmpty(Layout, $"formations[{formationId}].slots.layout");
        CapabilityStandardTotalWarLikeFormationSlotLayout layout = ResolveLayout(Layout, formationId);
        if (layout == CapabilityStandardTotalWarLikeFormationSlotLayout.Grid)
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

    private static CapabilityStandardTotalWarLikeFormationSlotLayout ResolveLayout(string layout, string formationId)
    {
        return layout switch
        {
            CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Grid => CapabilityStandardTotalWarLikeFormationSlotLayout.Grid,
            CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Disc => CapabilityStandardTotalWarLikeFormationSlotLayout.Disc,
            _ => throw new InvalidOperationException(
                $"Total War formation '{formationId}' slots.layout must be '{CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Grid}' or '{CapabilityStandardTotalWarLikeFormationSlotLayoutNames.Disc}', got '{layout}'."),
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

internal sealed class CapabilityStandardTotalWarLikeFormationGridSlotConfig
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

internal sealed class CapabilityStandardTotalWarLikeFormationDiscSlotConfig
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

internal sealed class CapabilityStandardTotalWarLikeFormationOutlineConfig
{
    public string Shape { get; set; } = string.Empty;
    public CapabilityStandardTotalWarLikeFormationRectangleOutlineConfig? Rectangle { get; set; }
    public CapabilityStandardTotalWarLikeFormationCircleOutlineConfig? Circle { get; set; }
    public float HeightOffsetM { get; set; }
    public int CurveSampleCount { get; set; }
    public float EmissionPositionEpsilonM { get; set; }
    public float EmissionFacingEpsilonRadians { get; set; }
    public float FrontIndicatorLengthCm { get; set; }
    public float FrontIndicatorLineWidthCm { get; set; }
    public float[] FillColor { get; set; } = Array.Empty<float>();
    public float[] BorderColor { get; set; } = Array.Empty<float>();
    public CapabilityStandardTotalWarLikeFormationOutlineShape ResolvedShape => ResolveShape(Shape, "outline.shape");
    public CapabilityStandardTotalWarLikeFormationRectangleOutlineConfig RequiredRectangle => Rectangle
        ?? throw new InvalidOperationException("Total War rectangle formation requires outline.rectangle.");
    public CapabilityStandardTotalWarLikeFormationCircleOutlineConfig RequiredCircle => Circle
        ?? throw new InvalidOperationException("Total War circle formation requires outline.circle.");

    public void Validate(string formationId)
    {
        RequireNonEmpty(Shape, $"formations[{formationId}].outline.shape");
        CapabilityStandardTotalWarLikeFormationOutlineShape shape = ResolveShape(Shape, formationId);
        if (shape == CapabilityStandardTotalWarLikeFormationOutlineShape.Rectangle)
        {
            RequiredRectangle.Validate(formationId);
            if (Circle != null)
            {
                throw new InvalidOperationException($"Total War formation '{formationId}' rectangle outline must not author outline.circle.");
            }
        }
        else if (shape == CapabilityStandardTotalWarLikeFormationOutlineShape.Circle)
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
        if (CurveSampleCount <= 0)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.CurveSampleCount > 0.");
        }

        RequirePositive(EmissionPositionEpsilonM, formationId, nameof(EmissionPositionEpsilonM));
        RequirePositive(EmissionFacingEpsilonRadians, formationId, nameof(EmissionFacingEpsilonRadians));
        if (FrontIndicatorLengthCm < 0f)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.FrontIndicatorLengthCm >= 0.");
        }

        ValidateColor(FillColor, formationId, nameof(FillColor));
        ValidateColor(BorderColor, formationId, nameof(BorderColor));
    }

    public CapabilityStandardTotalWarLikeFormationOutline ToComponent(string formationId)
    {
        CapabilityStandardTotalWarLikeFormationOutlineShape shape = ResolveShape(Shape, formationId);
        if (shape == CapabilityStandardTotalWarLikeFormationOutlineShape.Rectangle)
        {
            CapabilityStandardTotalWarLikeFormationRectangleOutlineConfig rectangle = RequiredRectangle;
            return new CapabilityStandardTotalWarLikeFormationOutline
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

        if (shape == CapabilityStandardTotalWarLikeFormationOutlineShape.Circle)
        {
            CapabilityStandardTotalWarLikeFormationCircleOutlineConfig circle = RequiredCircle;
            return new CapabilityStandardTotalWarLikeFormationOutline
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

        throw new InvalidOperationException($"Total War formation '{formationId}' has unsupported outline shape '{Shape}'.");
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
        CapabilityStandardTotalWarLikeFormationOutlineShape shape = ResolveShape(Shape, formationId);
        var footprint = new SpatialFootprint2D();
        if (shape == CapabilityStandardTotalWarLikeFormationOutlineShape.Rectangle)
        {
            CapabilityStandardTotalWarLikeFormationRectangleOutlineConfig rectangle = RequiredRectangle;
            int halfWidthCm = CheckedRoundToInt(rectangle.WidthCm * 0.5f, formationId, "outline.rectangle.widthCm");
            int halfDepthCm = CheckedRoundToInt(rectangle.DepthCm * 0.5f, formationId, "outline.rectangle.depthCm");
            footprint.SetPolygonVertexCount(0, 4);
            footprint.SetVertex(0, 0, new WorldCmInt2(-halfWidthCm, -halfDepthCm));
            footprint.SetVertex(0, 1, new WorldCmInt2(halfWidthCm, -halfDepthCm));
            footprint.SetVertex(0, 2, new WorldCmInt2(halfWidthCm, halfDepthCm));
            footprint.SetVertex(0, 3, new WorldCmInt2(-halfWidthCm, halfDepthCm));
            return footprint;
        }

        if (shape == CapabilityStandardTotalWarLikeFormationOutlineShape.Circle)
        {
            CapabilityStandardTotalWarLikeFormationCircleOutlineConfig circle = RequiredCircle;
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

        throw new InvalidOperationException($"Total War formation '{formationId}' has unsupported outline shape '{Shape}'.");
    }

    private static int CheckedRoundToInt(float value, string formationId, string fieldName)
    {
        if (!float.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' {fieldName} cannot be represented as integer cm.");
        }

        return (int)MathF.Round(value);
    }

    private static CapabilityStandardTotalWarLikeFormationOutlineShape ResolveShape(string shape, string formationId)
    {
        return shape switch
        {
            CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Rectangle => CapabilityStandardTotalWarLikeFormationOutlineShape.Rectangle,
            CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Circle => CapabilityStandardTotalWarLikeFormationOutlineShape.Circle,
            _ => throw new InvalidOperationException(
                $"Total War formation '{formationId}' outline.shape must be '{CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Rectangle}' or '{CapabilityStandardTotalWarLikeFormationOutlineShapeNames.Circle}', got '{shape}'."),
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

internal sealed class CapabilityStandardTotalWarLikeFormationRectangleOutlineConfig
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

internal sealed class CapabilityStandardTotalWarLikeFormationCircleOutlineConfig
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
                $"Total War formation '{formationId}' requires outline.circle.FootprintVertexCount between 3 and {SpatialFootprint2D.MaxVerticesPerPolygon}.");
        }
    }

    private static void RequirePositive(float value, string formationId, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Total War formation '{formationId}' requires outline.circle.{fieldName} > 0.");
        }
    }
}

internal sealed class CapabilityStandardTotalWarLikeConfigLoader
{
    public const string RelativePath = "CapabilityStandardTotalWarLikeConfig.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardTotalWarLikeConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardTotalWarLikeConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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

        return CapabilityStandardTotalWarLikeConfig.Load(merged);
    }
}
