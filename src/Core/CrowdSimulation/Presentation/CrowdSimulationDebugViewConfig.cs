using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Numerics;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// CrowdSimulationDebugView.json。叠加层默认开关、检视组合、箭头像素阈值和颜色都在这里。
/// 缺字段、未知键、越界在装载时失败，不补值。
/// </summary>
internal sealed class CrowdSimulationDebugViewConfig
{
    public const int ColorOff = 0;
    public const int ColorTeam = 1;
    public const int ColorTemplate = 2;
    public const int ColorPriority = 3;

    [JsonRequired] public int Version { get; init; }
    [JsonRequired] public int ArrowMinScreenPx { get; init; }
    [JsonRequired] public int LineThicknessPx { get; init; }
    [JsonRequired] public int UnitMarkPx { get; init; }
    [JsonRequired] public int MaxScreenSegments { get; init; }
    [JsonRequired] public int MaxFieldCells { get; init; }
    [JsonRequired] public int MaxWorldSegments { get; init; }
    [JsonRequired] public int DensityMax { get; init; }
    [JsonRequired] public int OverlayReserve { get; init; }
    [JsonRequired] public int DirtyPulsePercent { get; init; }
    [JsonRequired] public CrowdDebugViewInspect Inspect { get; init; } = null!;
    [JsonRequired] public int BeliefGroup { get; init; }
    [JsonRequired] public string UnitColor { get; init; } = "";
    [JsonRequired] public CrowdDebugViewOverlays Overlays { get; init; } = null!;
    [JsonRequired] public CrowdDebugViewColors Colors { get; init; } = null!;
    [JsonRequired] public CrowdDebugViewLayers Layers { get; init; } = null!;
    [JsonRequired] public CrowdDebugViewActions Actions { get; init; } = null!;
    [JsonRequired] public CrowdDebugViewPanel Panel { get; init; } = null!;

    [JsonIgnore] public string SourcePath { get; private set; } = "";
    [JsonIgnore] public int UnitColorMode { get; private set; }

    public static CrowdSimulationDebugViewConfig Load(JsonObject node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrEmpty(path);
        CrowdSimulationDebugViewConfig cfg;
        try
        {
            cfg = node.Deserialize<CrowdSimulationDebugViewConfig>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{path}: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path}: {ex.Message}", ex);
        }

        cfg.SourcePath = path;
        cfg.Validate(path);
        return cfg;
    }

    private void Validate(string path)
    {
        if (Version != 1)
            throw new InvalidOperationException($"{path}: version 需为 1。");
        AtLeast(path, "arrowMinScreenPx", ArrowMinScreenPx, 1);
        AtLeast(path, "lineThicknessPx", LineThicknessPx, 1);
        AtLeast(path, "unitMarkPx", UnitMarkPx, 1);
        AtLeast(path, "maxScreenSegments", MaxScreenSegments, 1);
        AtLeast(path, "maxFieldCells", MaxFieldCells, 1);
        AtLeast(path, "maxWorldSegments", MaxWorldSegments, 1);
        AtLeast(path, "densityMax", DensityMax, 1);
        if (OverlayReserve < 0)
            throw new InvalidOperationException($"{path}: overlayReserve 需为 ≥ 0 的整数。");
        if (MaxScreenSegments + OverlayReserve > ScreenOverlayBuffer.MaxItems)
            throw new InvalidOperationException($"{path}: maxScreenSegments 与 overlayReserve 之和超过屏幕叠加容量 {ScreenOverlayBuffer.MaxItems}。");
        if (DirtyPulsePercent < 1 || DirtyPulsePercent > 100)
            throw new InvalidOperationException($"{path}: dirtyPulsePercent 需为 1..100 的整数。");
        if (Inspect.LayerIndex < 0)
            throw new InvalidOperationException($"{path}: inspect.layerIndex 需为 ≥ 0 的整数。");
        if (Inspect.RadiusIndex < 0)
            throw new InvalidOperationException($"{path}: inspect.radiusIndex 需为 ≥ 0 的整数。");
        if (BeliefGroup < 0)
            throw new InvalidOperationException($"{path}: beliefGroup 需为 ≥ 0 的整数。");
        UnitColorMode = UnitColor switch
        {
            "off" => ColorOff,
            "team" => ColorTeam,
            "template" => ColorTemplate,
            "priority" => ColorPriority,
            _ => throw new InvalidOperationException($"{path}: unitColor 只能是 off、team、template、priority。"),
        };
        Colors.Validate(path);
        Layers.Validate(path);
        Actions.Validate(path);
        if (Panel.FontSize < 1)
            throw new InvalidOperationException($"{path}: panel.fontSize 需为 ≥ 1 的整数。");
        if (Panel.X < 0)
            throw new InvalidOperationException($"{path}: panel.x 需为 ≥ 0 的整数。");
        if (Panel.Y < 0)
            throw new InvalidOperationException($"{path}: panel.y 需为 ≥ 0 的整数。");
    }

    internal static void AtLeast(string path, string field, int value, int min)
    {
        if (value < min)
            throw new InvalidOperationException($"{path}: {field} 需为 ≥ {min} 的整数。");
    }
}

internal sealed class CrowdDebugViewInspect
{
    [JsonRequired] public int LayerIndex { get; init; }
    [JsonRequired] public int RadiusIndex { get; init; }
}

internal sealed class CrowdDebugViewOverlays
{
    [JsonRequired] public bool Funnel { get; init; }
    [JsonRequired] public bool Corridor { get; init; }
    [JsonRequired] public bool HpaGraph { get; init; }
    [JsonRequired] public bool NavPolygons { get; init; }
    [JsonRequired] public bool TileGrid { get; init; }
    [JsonRequired] public bool Flow { get; init; }
    [JsonRequired] public bool Density { get; init; }
    [JsonRequired] public bool Obstacles { get; init; }
    [JsonRequired] public bool JumpLinks { get; init; }
    [JsonRequired] public bool Belief { get; init; }
}

internal sealed class CrowdDebugRgba
{
    [JsonRequired] public int R { get; init; }
    [JsonRequired] public int G { get; init; }
    [JsonRequired] public int B { get; init; }
    [JsonRequired] public int A { get; init; }

    public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    public void Validate(string path, string field)
    {
        Channel(path, field, R);
        Channel(path, field, G);
        Channel(path, field, B);
        Channel(path, field, A);
    }

    private static void Channel(string path, string field, int value)
    {
        if (value < 0 || value > 255)
            throw new InvalidOperationException($"{path}: {field} 需为 0..255 的整数。");
    }
}

internal sealed class CrowdDebugViewColors
{
    [JsonRequired] public CrowdDebugRgba Nav { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Plan { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Tile { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba TileDirty { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Ink { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Paper { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba TwoWay { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba OneWay { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba DensityLow { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba DensityHigh { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba BeliefDiff { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba BeliefGhost { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Leader { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Funnel { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba PanelText { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba[] Teams { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba[] Templates { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba[] Priorities { get; init; } = null!;

    public void Validate(string path)
    {
        Nav.Validate(path, "colors.nav");
        Plan.Validate(path, "colors.plan");
        Tile.Validate(path, "colors.tile");
        TileDirty.Validate(path, "colors.tileDirty");
        Ink.Validate(path, "colors.ink");
        Paper.Validate(path, "colors.paper");
        TwoWay.Validate(path, "colors.twoWay");
        OneWay.Validate(path, "colors.oneWay");
        DensityLow.Validate(path, "colors.densityLow");
        DensityHigh.Validate(path, "colors.densityHigh");
        BeliefDiff.Validate(path, "colors.beliefDiff");
        BeliefGhost.Validate(path, "colors.beliefGhost");
        Leader.Validate(path, "colors.leader");
        Funnel.Validate(path, "colors.funnel");
        PanelText.Validate(path, "colors.panelText");
        Palette(path, "colors.teams", Teams);
        Palette(path, "colors.templates", Templates);
        Palette(path, "colors.priorities", Priorities);
    }

    public static Vector4 At(CrowdDebugRgba[] palette, int index, string path, string field)
    {
        if ((uint)index >= (uint)palette.Length)
            throw new InvalidOperationException($"{path}: {field} 下标 {index} 越界（长度 {palette.Length}）。");
        return palette[index].ToVector4();
    }

    private static void Palette(string path, string field, CrowdDebugRgba[] colors)
    {
        if (colors.Length < 1)
            throw new InvalidOperationException($"{path}: {field} 至少一项。");
        for (int i = 0; i < colors.Length; i++)
            colors[i].Validate(path, field);
    }
}

internal sealed class CrowdDebugViewLayers
{
    [JsonRequired] public int Paper { get; init; }
    [JsonRequired] public int Corridor { get; init; }
    [JsonRequired] public int Obstacles { get; init; }
    [JsonRequired] public int Density { get; init; }
    [JsonRequired] public int Belief { get; init; }
    [JsonRequired] public int Flow { get; init; }

    public void Validate(string path)
    {
        int[] ids = { Paper, Corridor, Obstacles, Density, Belief, Flow };
        string[] names = { "layers.paper", "layers.corridor", "layers.obstacles", "layers.density", "layers.belief", "layers.flow" };
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] < 1)
                throw new InvalidOperationException($"{path}: {names[i]} 需为 ≥ 1 的整数。");
            for (int j = 0; j < i; j++)
            {
                if (ids[i] == ids[j])
                    throw new InvalidOperationException($"{path}: {names[i]} 与 {names[j]} 重复。");
            }
        }
    }
}

internal sealed class CrowdDebugViewActions
{
    [JsonRequired] public string ToggleFunnel { get; init; } = "";
    [JsonRequired] public string ToggleCorridor { get; init; } = "";
    [JsonRequired] public string ToggleHpaGraph { get; init; } = "";
    [JsonRequired] public string ToggleNavPolygons { get; init; } = "";
    [JsonRequired] public string ToggleTileGrid { get; init; } = "";
    [JsonRequired] public string ToggleFlow { get; init; } = "";
    [JsonRequired] public string ToggleDensity { get; init; } = "";
    [JsonRequired] public string ToggleObstacles { get; init; } = "";
    [JsonRequired] public string ToggleJumpLinks { get; init; } = "";
    [JsonRequired] public string ToggleBelief { get; init; } = "";
    [JsonRequired] public string CycleUnitColor { get; init; } = "";
    [JsonRequired] public string CycleBeliefGroup { get; init; } = "";
    [JsonRequired] public string CycleInspectLayer { get; init; } = "";
    [JsonRequired] public string CycleInspectRadius { get; init; } = "";

    public void Validate(string path)
    {
        Require(path, "actions.toggleFunnel", ToggleFunnel);
        Require(path, "actions.toggleCorridor", ToggleCorridor);
        Require(path, "actions.toggleHpaGraph", ToggleHpaGraph);
        Require(path, "actions.toggleNavPolygons", ToggleNavPolygons);
        Require(path, "actions.toggleTileGrid", ToggleTileGrid);
        Require(path, "actions.toggleFlow", ToggleFlow);
        Require(path, "actions.toggleDensity", ToggleDensity);
        Require(path, "actions.toggleObstacles", ToggleObstacles);
        Require(path, "actions.toggleJumpLinks", ToggleJumpLinks);
        Require(path, "actions.toggleBelief", ToggleBelief);
        Require(path, "actions.cycleUnitColor", CycleUnitColor);
        Require(path, "actions.cycleBeliefGroup", CycleBeliefGroup);
        Require(path, "actions.cycleInspectLayer", CycleInspectLayer);
        Require(path, "actions.cycleInspectRadius", CycleInspectRadius);
    }

    private static void Require(string path, string field, string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"{path}: {field} 不能为空。");
    }
}

internal sealed class CrowdDebugViewPanel
{
    [JsonRequired] public int X { get; init; }
    [JsonRequired] public int Y { get; init; }
    [JsonRequired] public int FontSize { get; init; }
}
