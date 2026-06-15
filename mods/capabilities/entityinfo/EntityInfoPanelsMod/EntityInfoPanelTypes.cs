using System;
using Arch.Core;

namespace EntityInfoPanelsMod;

public readonly record struct EntityInfoPanelHandle(int Slot, int Generation)
{
    public static EntityInfoPanelHandle Invalid => new(-1, 0);
    public bool IsValid => Slot >= 0 && Generation > 0;
}

[Flags]
public enum EntityInfoPanelSurface
{
    None = 0,
    Ui = 1 << 0,
    Overlay = 1 << 1,
}

public enum EntityInfoPanelKind : byte
{
    ComponentInspector = 0,
    GasInspector = 1,
    InsightBrief = 2,
    EntityCollectionInspector = 3,
}

public enum EntityInfoPanelAnchor : byte
{
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3,
    Center = 4,
    TopCenter = 5,
    BottomCenter = 6,
}

public enum EntityInfoPanelTargetKind : byte
{
    FixedEntity = 0,
    GlobalEntityKey = 1,
    CurrentSelectionView = 2,
    EntityCollection = 3,
}

public enum EntityInfoPanelTemplateBindingKind : byte
{
    TargetEntity = 0,
}

public enum EntityInfoPanelTemplateLayoutMode : byte
{
    Compact = 0,
    Full = 1,
}

[Flags]
public enum EntityInfoPanelTemplateSectionFlags : ushort
{
    None = 0,
    Title = 1 << 0,
    Subtitle = 1 << 1,
    Body = 1 << 2,
    Badges = 1 << 3,
    Stats = 1 << 4,
    Tips = 1 << 5,
    Actions = 1 << 6,
    All = Title | Subtitle | Body | Badges | Stats | Tips | Actions,
}

[Flags]
public enum EntityInfoGasDetailFlags
{
    None = 0,
    ShowAttributeAggregateSources = 1 << 0,
    ShowModifierState = 1 << 1,
}

public readonly record struct EntityInfoPanelLayout(
    EntityInfoPanelAnchor Anchor,
    float OffsetX,
    float OffsetY,
    float Width,
    float Height);

public readonly record struct EntityInfoPanelTarget(
    EntityInfoPanelTargetKind Kind,
    Entity FixedEntity,
    string Key)
{
    public static EntityInfoPanelTarget Fixed(Entity entity) =>
        new(EntityInfoPanelTargetKind.FixedEntity, entity, string.Empty);

    public static EntityInfoPanelTarget Global(string key) =>
        new(EntityInfoPanelTargetKind.GlobalEntityKey, Entity.Null, key ?? string.Empty);

    public static EntityInfoPanelTarget CurrentSelectionView() =>
        new(EntityInfoPanelTargetKind.CurrentSelectionView, Entity.Null, string.Empty);

    public static EntityInfoPanelTarget EntityCollection(Entity owner, string collectionKey) =>
        new(EntityInfoPanelTargetKind.EntityCollection, owner, collectionKey ?? string.Empty);
}

public readonly record struct EntityCollectionPanelRow(
    int Index,
    int EntityId,
    string Name,
    string AttributesSummary,
    bool IsPrimary,
    string TemplateId = "",
    string TemplateSubtitle = "",
    string TemplateBody = "",
    string AccentColorHex = "");

public readonly record struct EntityCollectionCategorySummary(
    string Label,
    int Count,
    bool ContainsPrimary);

public readonly record struct EntityInfoPanelRequest
{
    public EntityInfoPanelRequest(
        EntityInfoPanelKind kind,
        EntityInfoPanelSurface surface,
        EntityInfoPanelTarget target,
        EntityInfoPanelLayout layout,
        EntityInfoGasDetailFlags gasDetailFlags,
        bool visible,
        string templateId = "")
    {
        Kind = kind;
        Surface = surface;
        Target = target;
        Layout = layout;
        GasDetailFlags = gasDetailFlags;
        Visible = visible;
        TemplateId = templateId ?? string.Empty;
    }

    public EntityInfoPanelKind Kind { get; init; }
    public EntityInfoPanelSurface Surface { get; init; }
    public EntityInfoPanelTarget Target { get; init; }
    public EntityInfoPanelLayout Layout { get; init; }
    public EntityInfoGasDetailFlags GasDetailFlags { get; init; }
    public bool Visible { get; init; }
    public string TemplateId { get; init; }
}
