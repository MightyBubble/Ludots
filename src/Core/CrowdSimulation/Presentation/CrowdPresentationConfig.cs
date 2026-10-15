using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

internal sealed class CrowdPresentationConfig
{
    public const string KindPath = "path";
    public const string KindDeploy = "deploy";

    [JsonRequired] public string Kind { get; set; } = string.Empty;
    [JsonRequired] public string Title { get; set; } = string.Empty;
    [JsonRequired] public string[] ViewLabels { get; set; } = Array.Empty<string>();
    [JsonRequired] public CrowdHudConfig Hud { get; set; } = new();
    public CrowdPathPresentation? Path { get; set; }
    public CrowdDeployPresentation? Deploy { get; set; }

    public static CrowdPresentationConfig? ParseOptional(JsonObject debug)
    {
        if (debug["presentation"] is not JsonNode node) return null;
        CrowdPresentationConfig cfg;
        try
        {
            cfg = node.Deserialize<CrowdPresentationConfig>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException("CrowdSimulationDebug.json presentation 反序列化为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation 严格反序列化失败: {ex.Message}", ex);
        }

        Validate(cfg);
        return cfg;
    }

    internal static void Validate(CrowdPresentationConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Title))
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.title 必填。");
        if (cfg.ViewLabels == null || cfg.ViewLabels.Length == 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.viewLabels 至少一条。");
        for (int i = 0; i < cfg.ViewLabels.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(cfg.ViewLabels[i]))
                throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.viewLabels[{i}] 必填。");
        }

        if (cfg.Hud == null)
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.hud 必填。");
        if (string.IsNullOrWhiteSpace(cfg.Hud.Keys))
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.hud.keys 必填。");

        if (cfg.Kind == KindPath)
        {
            if (cfg.Path == null)
                throw new InvalidOperationException("CrowdSimulationDebug.json presentation.path 必填(kind=path)。");
            if (cfg.Deploy != null)
                throw new InvalidOperationException("CrowdSimulationDebug.json presentation.kind=path 不能带 deploy。");
            if (string.IsNullOrWhiteSpace(cfg.Path.TruthAsset))
                throw new InvalidOperationException("CrowdSimulationDebug.json presentation.path.truthAsset 必填。");
            RequireActions(cfg.Path.Actions);
        }
        else if (cfg.Kind == KindDeploy)
        {
            if (cfg.Deploy == null)
                throw new InvalidOperationException("CrowdSimulationDebug.json presentation.deploy 必填(kind=deploy)。");
            if (cfg.Path != null)
                throw new InvalidOperationException("CrowdSimulationDebug.json presentation.kind=deploy 不能带 path。");
            RequireDeployCopy(cfg.Deploy);
            if (cfg.Deploy.Structures == null || cfg.Deploy.Structures.Length == 0)
                throw new InvalidOperationException("CrowdSimulationDebug.json presentation.deploy.structures 至少一条。");
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < cfg.Deploy.Structures.Length; i++)
            {
                CrowdStructureTemplateDto row = cfg.Deploy.Structures[i]
                    ?? throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.deploy.structures[{i}] 为空。");
                ValidateStructure(row, i);
                if (!seen.Add(row.SelectAction))
                {
                    throw new InvalidOperationException(
                        $"CrowdSimulationDebug.json presentation.deploy.structures[{i}].selectAction 重复。");
                }
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"CrowdSimulationDebug.json presentation.kind \"{cfg.Kind}\" 需为 {KindPath} / {KindDeploy}。");
        }
    }

    private static void RequireActions(CrowdPathActions? actions)
    {
        if (actions == null)
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.path.actions 必填。");
        RequireText(actions.SetStart, "path.actions.setStart");
        RequireText(actions.SetGoal, "path.actions.setGoal");
        RequireText(actions.CycleContext, "path.actions.cycleContext");
        RequireText(actions.ToggleThreads, "path.actions.toggleThreads");
        RequireText(actions.ToggleSlow, "path.actions.toggleSlow");
        RequireText(actions.CycleView, "path.actions.cycleView");
    }

    private static void RequireDeployCopy(CrowdDeployPresentation deploy)
    {
        if (deploy.Actions == null)
            throw new InvalidOperationException("CrowdSimulationDebug.json presentation.deploy.actions 必填。");
        RequireText(deploy.Actions.CycleView, "deploy.actions.cycleView");
        RequireText(deploy.Actions.ToggleBuild, "deploy.actions.toggleBuild");
        RequireText(deploy.Actions.Remove, "deploy.actions.remove");
        RequireText(deploy.Actions.Place, "deploy.actions.place");
        RequireText(deploy.Actions.Validate, "deploy.actions.validate");
        RequireText(deploy.Actions.ToolOff, "deploy.actions.toolOff");
        RequireText(deploy.Actions.ToolBoom, "deploy.actions.toolBoom");
        RequireText(deploy.Actions.ToolSpawn, "deploy.actions.toolSpawn");
        RequireText(deploy.Actions.ToolStructure, "deploy.actions.toolStructure");
        RequireText(deploy.Actions.ToolDemolish, "deploy.actions.toolDemolish");
        RequireText(deploy.Actions.Deploy, "deploy.actions.deploy");
        RequireText(deploy.Actions.Scatter, "deploy.actions.scatter");
        RequireText(deploy.Actions.Stress, "deploy.actions.stress");
        RequireText(deploy.Actions.ClearCaches, "deploy.actions.clearCaches");
        RequireText(deploy.Actions.AvoidDown, "deploy.actions.avoidDown");
        RequireText(deploy.Actions.AvoidUp, "deploy.actions.avoidUp");
        RequireText(deploy.Actions.SliceDown, "deploy.actions.sliceDown");
        RequireText(deploy.Actions.SliceUp, "deploy.actions.sliceUp");
        RequireText(deploy.Actions.Pause, "deploy.actions.pause");
        RequireText(deploy.Actions.SpeedDown, "deploy.actions.speedDown");
        RequireText(deploy.Actions.SpeedUp, "deploy.actions.speedUp");
        RequireText(deploy.Actions.Fuzz, "deploy.actions.fuzz");
        RequireText(deploy.BuildActivity, "deploy.buildActivity");
        RequireText(deploy.ObserveActivity, "deploy.observeActivity");
        RequireText(deploy.PlaceOk, "deploy.placeOk");
        RequireText(deploy.PlaceBlocked, "deploy.placeBlocked");
        RequireText(deploy.PlaceOffMap, "deploy.placeOffMap");
        RequireText(deploy.PlaceOutOfBounds, "deploy.placeOutOfBounds");
        RequireText(deploy.PlaceOverlapsStructure, "deploy.placeOverlapsStructure");
        RequireText(deploy.PlaceOverlapsBlocked, "deploy.placeOverlapsBlocked");
        RequireText(deploy.PlaceSpanTerrain, "deploy.placeSpanTerrain");
        RequireText(deploy.PlaceAnchorTerrain, "deploy.placeAnchorTerrain");
        RequireText(deploy.PendingNav, "deploy.pendingNav");
        RequireColor(deploy.PlaceLegal, "deploy.placeLegal");
        RequireColor(deploy.PlaceIllegal, "deploy.placeIllegal");
        RequireColor(deploy.PlaceAnchor, "deploy.placeAnchor");
        RequireColor(deploy.PlacePortal, "deploy.placePortal");
    }

    private static void ValidateStructure(CrowdStructureTemplateDto row, int index)
    {
        string at = $"presentation.deploy.structures[{index}]";
        RequireText(row.Id, $"{at}.id");
        RequireText(row.Label, $"{at}.label");
        RequireText(row.SelectAction, $"{at}.selectAction");
        if (row.SizeCm <= 0)
            throw new InvalidOperationException($"CrowdSimulationDebug.json {at}.sizeCm 必须 > 0。");
        if (row.Shape == S4DeployDemoRuntime.ShapePoint)
        {
            if (row.RoadLengthCm != null)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationDebug.json {at}.shape=point 不能带 roadLengthCm。");
            }
        }
        else if (row.Shape == S4DeployDemoRuntime.ShapeRoad)
        {
            if (row.RoadLengthCm is not > 0)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationDebug.json {at}.shape=road 需要 roadLengthCm > 0。");
            }
        }
        else if (row.Shape == S4DeployDemoRuntime.ShapeDisc || row.Shape == S4DeployDemoRuntime.ShapeSpan)
        {
            if (row.RoadLengthCm != null)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationDebug.json {at}.shape={row.Shape} 不能带 roadLengthCm。");
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"CrowdSimulationDebug.json {at}.shape \"{row.Shape}\" 需为 {S4DeployDemoRuntime.ShapePoint} / {S4DeployDemoRuntime.ShapeRoad} / {S4DeployDemoRuntime.ShapeDisc} / {S4DeployDemoRuntime.ShapeSpan}。");
        }
    }

    internal static Vector4 Rgba(int[]? rgba, string path)
    {
        RequireColor(rgba, path);
        return new Vector4(rgba![0] / 255f, rgba[1] / 255f, rgba[2] / 255f, rgba[3] / 255f);
    }

    private static void RequireColor(int[]? rgba, string path)
    {
        if (rgba == null || rgba.Length != 4)
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.{path} 需要 4 个 0..255 的整数。");
        for (int i = 0; i < 4; i++)
        {
            if ((uint)rgba[i] > 255)
                throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.{path}[{i}] 超出 0..255。");
        }
    }

    private static void RequireText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.{path} 必填。");
    }
}

/// <summary>键位说明模板。HUD 面板的布局与样式属于群体界面 mod。</summary>
internal sealed class CrowdHudConfig
{
    [JsonRequired] public string Keys { get; set; } = string.Empty;
}

internal sealed class CrowdPathPresentation
{
    [JsonRequired] public string TruthAsset { get; set; } = string.Empty;
    [JsonRequired] public CrowdPathActions Actions { get; set; } = new();
}

internal sealed class CrowdPathActions
{
    [JsonRequired] public string SetStart { get; set; } = string.Empty;
    [JsonRequired] public string SetGoal { get; set; } = string.Empty;
    [JsonRequired] public string CycleContext { get; set; } = string.Empty;
    [JsonRequired] public string ToggleThreads { get; set; } = string.Empty;
    [JsonRequired] public string ToggleSlow { get; set; } = string.Empty;
    [JsonRequired] public string CycleView { get; set; } = string.Empty;
}

internal sealed class CrowdDeployPresentation
{
    [JsonRequired] public CrowdDeployActions Actions { get; set; } = new();
    [JsonRequired] public string BuildActivity { get; set; } = string.Empty;
    [JsonRequired] public string ObserveActivity { get; set; } = string.Empty;
    [JsonRequired] public string PlaceOk { get; set; } = string.Empty;
    [JsonRequired] public string PlaceBlocked { get; set; } = string.Empty;
    [JsonRequired] public string PlaceOffMap { get; set; } = string.Empty;
    [JsonRequired] public string PlaceOutOfBounds { get; set; } = string.Empty;
    [JsonRequired] public string PlaceOverlapsStructure { get; set; } = string.Empty;
    [JsonRequired] public string PlaceOverlapsBlocked { get; set; } = string.Empty;
    [JsonRequired] public string PlaceSpanTerrain { get; set; } = string.Empty;
    [JsonRequired] public string PlaceAnchorTerrain { get; set; } = string.Empty;
    [JsonRequired] public int[] PlaceLegal { get; set; } = Array.Empty<int>();
    [JsonRequired] public int[] PlaceIllegal { get; set; } = Array.Empty<int>();
    [JsonRequired] public int[] PlaceAnchor { get; set; } = Array.Empty<int>();
    [JsonRequired] public int[] PlacePortal { get; set; } = Array.Empty<int>();
    [JsonRequired] public string PendingNav { get; set; } = string.Empty;
    [JsonRequired] public CrowdStructureTemplateDto[] Structures { get; set; } = Array.Empty<CrowdStructureTemplateDto>();
}

internal sealed class CrowdDeployActions
{
    [JsonRequired] public string CycleView { get; set; } = string.Empty;
    [JsonRequired] public string ToggleBuild { get; set; } = string.Empty;
    [JsonRequired] public string Remove { get; set; } = string.Empty;
    [JsonRequired] public string Place { get; set; } = string.Empty;
    [JsonRequired] public string Validate { get; set; } = string.Empty;
    [JsonRequired] public string ToolOff { get; set; } = string.Empty;
    [JsonRequired] public string ToolBoom { get; set; } = string.Empty;
    [JsonRequired] public string ToolSpawn { get; set; } = string.Empty;
    [JsonRequired] public string ToolStructure { get; set; } = string.Empty;
    [JsonRequired] public string ToolDemolish { get; set; } = string.Empty;
    [JsonRequired] public string Deploy { get; set; } = string.Empty;
    [JsonRequired] public string Scatter { get; set; } = string.Empty;
    [JsonRequired] public string Stress { get; set; } = string.Empty;
    [JsonRequired] public string ClearCaches { get; set; } = string.Empty;
    [JsonRequired] public string AvoidDown { get; set; } = string.Empty;
    [JsonRequired] public string AvoidUp { get; set; } = string.Empty;
    [JsonRequired] public string SliceDown { get; set; } = string.Empty;
    [JsonRequired] public string SliceUp { get; set; } = string.Empty;
    [JsonRequired] public string Pause { get; set; } = string.Empty;
    [JsonRequired] public string SpeedDown { get; set; } = string.Empty;
    [JsonRequired] public string SpeedUp { get; set; } = string.Empty;
    [JsonRequired] public string Fuzz { get; set; } = string.Empty;
}

internal sealed class CrowdStructureTemplateDto
{
    [JsonRequired] public string Id { get; set; } = string.Empty;
    [JsonRequired] public string Label { get; set; } = string.Empty;
    [JsonRequired] public string Shape { get; set; } = string.Empty;
    [JsonRequired] public int SizeCm { get; set; }
    public int? RoadLengthCm { get; set; }
    [JsonRequired] public string SelectAction { get; set; } = string.Empty;
}
