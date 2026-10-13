using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// CrowdSimulationPlayerView.json。缺字段、未知键、越界在装载时失败，不补值。
/// 颜色只出现在这份配置里。
/// </summary>
internal sealed class CrowdPlayerViewConfig
{
    [JsonRequired] public int Version { get; init; }
    [JsonRequired] public bool MagicBox { get; init; }
    [JsonRequired] public bool FogHide { get; init; }
    [JsonRequired] public bool PlayerRoute { get; init; }
    [JsonRequired] public bool LineOfSight { get; init; }
    [JsonRequired] public bool LayersStartEnabled { get; init; }
    [JsonRequired] public int DeployCount { get; init; }
    [JsonRequired] public int DeployCountStep { get; init; }
    [JsonRequired] public int DeployCountMin { get; init; }
    [JsonRequired] public int DeployCountMax { get; init; }
    [JsonRequired] public int ClickCount { get; init; }
    [JsonRequired] public int ClickCountStep { get; init; }
    [JsonRequired] public int ClickCountMin { get; init; }
    [JsonRequired] public int ClickCountMax { get; init; }
    [JsonRequired] public int ClickUnitType { get; init; }
    [JsonRequired] public int ClickRadiusClass { get; init; }
    [JsonRequired] public int RadiusMinIndex { get; init; }
    [JsonRequired] public int RadiusMaxIndex { get; init; }
    [JsonRequired] public int RippleMs { get; init; }
    [JsonRequired] public int RippleRadiusCm { get; init; }
    [JsonRequired] public int DashCm { get; init; }
    [JsonRequired] public int GapCm { get; init; }
    [JsonRequired] public int MaxDashes { get; init; }
    [JsonRequired] public int RouteThicknessCm { get; init; }
    [JsonRequired] public int MarkerRadiusCm { get; init; }
    [JsonRequired] public int CrossArmCm { get; init; }
    [JsonRequired] public int StatusCardX { get; init; }
    [JsonRequired] public int StatusCardY { get; init; }
    [JsonRequired] public int StatusCardW { get; init; }
    [JsonRequired] public int StatusCardH { get; init; }
    [JsonRequired] public int TeamBarX { get; init; }
    [JsonRequired] public int TeamBarY { get; init; }
    [JsonRequired] public int FontSize { get; init; }
    [JsonRequired] public int LinePx { get; init; }
    [JsonRequired] public int KillRadiusCm { get; init; }
    [JsonRequired] public double KillRingScale { get; init; }
    [JsonRequired] public double KillImpulse { get; init; }
    [JsonRequired] public int KillXCm { get; init; }
    [JsonRequired] public int KillYCm { get; init; }
    [JsonRequired] public int EnemyMarkRadiusCm { get; init; }
    [JsonRequired] public float HillshadeStrength { get; init; }
    [JsonRequired] public int MaxBeliefMarkers { get; init; }
    [JsonRequired] public string CameraId { get; init; } = "";
    [JsonRequired] public string SeatId { get; init; } = "";
    [JsonRequired] public string BoxingContextId { get; init; } = "";
    [JsonRequired] public string FogSceneUri { get; init; } = "";
    [JsonRequired] public string DefaultShapeId { get; init; } = "";
    [JsonRequired] public CrowdPlayerViewActions Actions { get; init; } = null!;
    [JsonRequired] public CrowdPlayerViewLabels Labels { get; init; } = null!;
    [JsonRequired] public CrowdPlayerViewColors Colors { get; init; } = null!;

    public static CrowdPlayerViewConfig Load(JsonObject node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrEmpty(path);
        CrowdPlayerViewConfig cfg;
        try
        {
            cfg = node.Deserialize<CrowdPlayerViewConfig>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{path}: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path}: {ex.Message}", ex);
        }

        cfg.Validate(path);
        return cfg;
    }

    private void Validate(string path)
    {
        if (Version != 1)
            throw new InvalidOperationException($"{path}: version 需为 1。");
        RequireRange(path, "deployCount", DeployCount, DeployCountMin, DeployCountMax);
        if (DeployCountStep < 1)
            throw new InvalidOperationException($"{path}: deployCountStep 需为 ≥ 1 的整数。");
        if (DeployCountMin < 1 || DeployCountMax < DeployCountMin)
            throw new InvalidOperationException($"{path}: deployCountMin/Max 非法。");
        RequireRange(path, "clickCount", ClickCount, ClickCountMin, ClickCountMax);
        if (ClickCountStep < 1)
            throw new InvalidOperationException($"{path}: clickCountStep 需为 ≥ 1 的整数。");
        if (ClickCountMin < 1 || ClickCountMax < ClickCountMin)
            throw new InvalidOperationException($"{path}: clickCountMin/Max 非法。");
        if (ClickUnitType < 0 || ClickRadiusClass < 0)
            throw new InvalidOperationException($"{path}: clickUnitType 与 clickRadiusClass 需为 ≥ 0 的整数。");
        if (RadiusMinIndex < 0)
            throw new InvalidOperationException($"{path}: radiusMinIndex 需为 ≥ 0 的整数。");
        if (RadiusMaxIndex < -1)
            throw new InvalidOperationException($"{path}: radiusMaxIndex 需为 ≥ -1 的整数，-1 表示绑到半径级表的最后一档。");
        if (RadiusMaxIndex >= 0 && RadiusMaxIndex < RadiusMinIndex)
            throw new InvalidOperationException($"{path}: radiusMaxIndex 不能小于 radiusMinIndex。");
        if (RippleMs < 1)
            throw new InvalidOperationException($"{path}: rippleMs 需为 ≥ 1 的整数。");
        if (RippleRadiusCm < 1)
            throw new InvalidOperationException($"{path}: rippleRadiusCm 需为 ≥ 1 的整数。");
        if (DashCm < 1 || GapCm < 1)
            throw new InvalidOperationException($"{path}: dashCm 与 gapCm 需为 ≥ 1 的整数。");
        if (MaxDashes < 1 || MaxDashes > (1 << 8))
            throw new InvalidOperationException($"{path}: maxDashes 需为 1..256 的整数。");
        if (RouteThicknessCm < 1 || MarkerRadiusCm < 1 || CrossArmCm < 1 || EnemyMarkRadiusCm < 1)
            throw new InvalidOperationException($"{path}: routeThicknessCm、markerRadiusCm、crossArmCm 与 enemyMarkRadiusCm 需为 ≥ 1 的整数。");
        if (StatusCardW < 1 || StatusCardH < 1 || FontSize < 1 || LinePx < 1)
            throw new InvalidOperationException($"{path}: 状态卡尺寸、字号和线宽需为 ≥ 1 的整数。");
        if (KillRadiusCm < 1)
            throw new InvalidOperationException($"{path}: killRadiusCm 需为 ≥ 1 的整数。");
        if (!(KillRingScale > 1d) || !double.IsFinite(KillRingScale))
            throw new InvalidOperationException($"{path}: killRingScale 必须 > 1。");
        if (KillImpulse < 0d || !double.IsFinite(KillImpulse))
            throw new InvalidOperationException($"{path}: killImpulse 必须 ≥ 0。");
        RequirePositive(path, "hillshadeStrength", HillshadeStrength);
        if (MaxBeliefMarkers < 1 || MaxBeliefMarkers > 4096)
            throw new InvalidOperationException($"{path}: maxBeliefMarkers 需为 1..4096 的整数。");
        RequireText(path, "cameraId", CameraId);
        RequireText(path, "seatId", SeatId);
        RequireText(path, "boxingContextId", BoxingContextId);
        RequireText(path, "fogSceneUri", FogSceneUri);
        RequireText(path, "defaultShapeId", DefaultShapeId);
        Actions.Validate(path);
        Labels.Validate(path);
        Colors.Validate(path);
    }

    private static void RequireRange(string path, string field, int value, int min, int max)
    {
        if (value < min || value > max)
            throw new InvalidOperationException($"{path}: {field} 需落在 {min}..{max}。");
    }

    private static void RequirePositive(string path, string field, float value)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new InvalidOperationException($"{path}: {field} 必须是 > 0 的有限数。");
    }

    internal static void RequireText(string path, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{path}: {field} 不能为空。");
    }
}

internal sealed class CrowdPlayerViewActions
{
    [JsonRequired] public string NextTeam { get; init; } = "";
    [JsonRequired] public string SelectAll { get; init; } = "";
    [JsonRequired] public string ClearSelection { get; init; } = "";
    [JsonRequired] public string ToggleLayer { get; init; } = "";
    [JsonRequired] public string CycleLayer { get; init; } = "";
    [JsonRequired] public string ToggleFog { get; init; } = "";
    [JsonRequired] public string SceneOutside { get; init; } = "";
    [JsonRequired] public string SceneBehind { get; init; } = "";
    [JsonRequired] public string ToggleLos { get; init; } = "";
    [JsonRequired] public string ToggleRoute { get; init; } = "";
    [JsonRequired] public string DeployCountDown { get; init; } = "";
    [JsonRequired] public string DeployCountUp { get; init; } = "";
    [JsonRequired] public string CycleClickTemplate { get; init; } = "";
    [JsonRequired] public string CycleClickRadius { get; init; } = "";
    [JsonRequired] public string ToggleMagicBox { get; init; } = "";
    [JsonRequired] public string CycleShape { get; init; } = "";
    [JsonRequired] public string ClickCountDown { get; init; } = "";
    [JsonRequired] public string ClickCountUp { get; init; } = "";
    [JsonRequired] public string CycleRadiusRange { get; init; } = "";
    [JsonRequired] public string BoxSelectBegin { get; init; } = "";
    [JsonRequired] public string SecondaryHold { get; init; } = "";
    [JsonRequired] public string FormationCommit { get; init; } = "";
    [JsonRequired] public string PrimaryTap { get; init; } = "";

    public void Validate(string path)
    {
        Require(path, "nextTeam", NextTeam);
        Require(path, "selectAll", SelectAll);
        Require(path, "clearSelection", ClearSelection);
        Require(path, "toggleLayer", ToggleLayer);
        Require(path, "cycleLayer", CycleLayer);
        Require(path, "toggleFog", ToggleFog);
        Require(path, "sceneOutside", SceneOutside);
        Require(path, "sceneBehind", SceneBehind);
        Require(path, "toggleLos", ToggleLos);
        Require(path, "toggleRoute", ToggleRoute);
        Require(path, "deployCountDown", DeployCountDown);
        Require(path, "deployCountUp", DeployCountUp);
        Require(path, "cycleClickTemplate", CycleClickTemplate);
        Require(path, "cycleClickRadius", CycleClickRadius);
        Require(path, "toggleMagicBox", ToggleMagicBox);
        Require(path, "cycleShape", CycleShape);
        Require(path, "clickCountDown", ClickCountDown);
        Require(path, "clickCountUp", ClickCountUp);
        Require(path, "cycleRadiusRange", CycleRadiusRange);
        Require(path, "boxSelectBegin", BoxSelectBegin);
        Require(path, "secondaryHold", SecondaryHold);
        Require(path, "formationCommit", FormationCommit);
        Require(path, "primaryTap", PrimaryTap);
    }

    private static void Require(string path, string field, string value)
        => CrowdPlayerViewConfig.RequireText($"{path}.actions", field, value);
}

internal sealed class CrowdPlayerViewLabels
{
    [JsonRequired] public string Planning { get; init; } = "";
    [JsonRequired] public string Moving { get; init; } = "";
    [JsonRequired] public string Arrived { get; init; } = "";
    [JsonRequired] public string Partial { get; init; } = "";
    [JsonRequired] public string Idle { get; init; } = "";
    [JsonRequired] public string MagicOn { get; init; } = "";
    [JsonRequired] public string MagicOff { get; init; } = "";
    [JsonRequired] public string Formation { get; init; } = "";
    [JsonRequired] public string Converge { get; init; } = "";
    [JsonRequired] public string Preserve { get; init; } = "";
    [JsonRequired] public string FogOn { get; init; } = "";
    [JsonRequired] public string FogOff { get; init; } = "";
    [JsonRequired] public string RouteOn { get; init; } = "";
    [JsonRequired] public string RouteOff { get; init; } = "";
    [JsonRequired] public string LosOn { get; init; } = "";
    [JsonRequired] public string LosOff { get; init; } = "";
    [JsonRequired] public string ClickSpawn { get; init; } = "";
    [JsonRequired] public string SceneOutside { get; init; } = "";
    [JsonRequired] public string SceneBehind { get; init; } = "";
    [JsonRequired] public string CountPlanning { get; init; } = "";
    [JsonRequired] public string CountMoving { get; init; } = "";
    [JsonRequired] public string CountOther { get; init; } = "";
    [JsonRequired] public string UnitType { get; init; } = "";
    [JsonRequired] public string Radius { get; init; } = "";
    [JsonRequired] public string Count { get; init; } = "";
    [JsonRequired] public string Deploy { get; init; } = "";
    [JsonRequired] public string Layer { get; init; } = "";
    [JsonRequired] public string Self { get; init; } = "";

    public void Validate(string path)
    {
        string prefix = $"{path}.labels";
        CrowdPlayerViewConfig.RequireText(prefix, "planning", Planning);
        CrowdPlayerViewConfig.RequireText(prefix, "moving", Moving);
        CrowdPlayerViewConfig.RequireText(prefix, "arrived", Arrived);
        CrowdPlayerViewConfig.RequireText(prefix, "partial", Partial);
        CrowdPlayerViewConfig.RequireText(prefix, "idle", Idle);
        CrowdPlayerViewConfig.RequireText(prefix, "magicOn", MagicOn);
        CrowdPlayerViewConfig.RequireText(prefix, "magicOff", MagicOff);
        CrowdPlayerViewConfig.RequireText(prefix, "formation", Formation);
        CrowdPlayerViewConfig.RequireText(prefix, "converge", Converge);
        CrowdPlayerViewConfig.RequireText(prefix, "preserve", Preserve);
        CrowdPlayerViewConfig.RequireText(prefix, "fogOn", FogOn);
        CrowdPlayerViewConfig.RequireText(prefix, "fogOff", FogOff);
        CrowdPlayerViewConfig.RequireText(prefix, "routeOn", RouteOn);
        CrowdPlayerViewConfig.RequireText(prefix, "routeOff", RouteOff);
        CrowdPlayerViewConfig.RequireText(prefix, "losOn", LosOn);
        CrowdPlayerViewConfig.RequireText(prefix, "losOff", LosOff);
        CrowdPlayerViewConfig.RequireText(prefix, "clickSpawn", ClickSpawn);
        CrowdPlayerViewConfig.RequireText(prefix, "sceneOutside", SceneOutside);
        CrowdPlayerViewConfig.RequireText(prefix, "sceneBehind", SceneBehind);
        CrowdPlayerViewConfig.RequireText(prefix, "countPlanning", CountPlanning);
        CrowdPlayerViewConfig.RequireText(prefix, "countMoving", CountMoving);
        CrowdPlayerViewConfig.RequireText(prefix, "countOther", CountOther);
        CrowdPlayerViewConfig.RequireText(prefix, "unitType", UnitType);
        CrowdPlayerViewConfig.RequireText(prefix, "radius", Radius);
        CrowdPlayerViewConfig.RequireText(prefix, "count", Count);
        CrowdPlayerViewConfig.RequireText(prefix, "deploy", Deploy);
        CrowdPlayerViewConfig.RequireText(prefix, "layer", Layer);
        CrowdPlayerViewConfig.RequireText(prefix, "self", Self);
    }
}

internal sealed class CrowdPlayerViewColors
{
    [JsonRequired] public CrowdDebugRgba Plan { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Ink { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Paper { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Selected { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba TerrainLow { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba TerrainHigh { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba TerrainShade { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Goal { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Ripple { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Card { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba Text { get; init; } = null!;
    [JsonRequired] public CrowdDebugRgba EnemyMark { get; init; } = null!;

    public void Validate(string path)
    {
        Plan.Validate(path, "colors.plan");
        Ink.Validate(path, "colors.ink");
        Paper.Validate(path, "colors.paper");
        Selected.Validate(path, "colors.selected");
        TerrainLow.Validate(path, "colors.terrainLow");
        TerrainHigh.Validate(path, "colors.terrainHigh");
        TerrainShade.Validate(path, "colors.terrainShade");
        Goal.Validate(path, "colors.goal");
        Ripple.Validate(path, "colors.ripple");
        Card.Validate(path, "colors.card");
        Text.Validate(path, "colors.text");
        EnemyMark.Validate(path, "colors.enemyMark");
    }
}
