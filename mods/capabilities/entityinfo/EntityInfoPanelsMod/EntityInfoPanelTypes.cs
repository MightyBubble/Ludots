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
}

public readonly record struct EntityInfoPanelRequest(
    EntityInfoPanelKind Kind,
    EntityInfoPanelSurface Surface,
    EntityInfoPanelTarget Target,
    EntityInfoPanelLayout Layout,
    EntityInfoGasDetailFlags GasDetailFlags,
    bool Visible);
