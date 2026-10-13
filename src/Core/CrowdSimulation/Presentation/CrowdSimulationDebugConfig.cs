using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Engine.TimeFlow;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// CrowdSimulationDebug.json 的严格形状。未知键在装载时拒绝。
/// 指令载荷留在 <see cref="CrowdDebugScriptEntry.Cmd"/>，由 <c>CrowdSimCommand.Parse</c> 校验。
/// </summary>
internal sealed class CrowdSimulationDebugConfig
{
    public CrowdDebugSession? Session { get; init; }
    public CrowdDebugTools? Tools { get; init; }
    public CrowdDebugRates? Rates { get; init; }
    public CrowdPresentationConfig? Presentation { get; init; }

    public static CrowdSimulationDebugConfig Load(JsonObject debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        if (debug["session"] is JsonObject session && session.ContainsKey("autostart"))
        {
            throw new InvalidOperationException(
                "CrowdSimulationDebug.json: 已删除 session.autostart。群体会话随地图焦点创建，挂起时释放。哪张图跑群体模拟，在 Navigation/navmesh.json 的 maps.<mapId>.solver 写 crowd-simulation。");
        }

        if (debug.ContainsKey("worker"))
        {
            throw new InvalidOperationException(
                "CrowdSimulationDebug.json: 已删除 worker.planTimeoutMs。路径规划超时写在 CrowdSimulationConfig.json 的 worker.planTimeoutMs。");
        }

        CrowdSimulationDebugConfig cfg;
        try
        {
            cfg = debug.Deserialize<CrowdSimulationDebugConfig>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException("CrowdSimulationDebug.json: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"CrowdSimulationDebug.json: {ex.Message}", ex);
        }

        if (cfg.Presentation != null)
        {
            CrowdPresentationConfig.Validate(cfg.Presentation);
        }

        if (cfg.Tools != null) ValidateTools(cfg.Tools);
        if (cfg.Rates != null) ValidateRates(cfg.Rates);
        if (cfg.Presentation?.Kind == CrowdPresentationConfig.KindDeploy && (cfg.Tools == null || cfg.Rates == null))
        {
            throw new InvalidOperationException("CrowdSimulationDebug.json: 部署演示必须带 tools 与 rates。");
        }

        if (cfg.Tools != null && cfg.Presentation?.Deploy != null)
        {
            RequirePlaceTemplate(cfg.Tools, cfg.Presentation.Deploy);
        }

        return cfg;
    }

    private static void ValidateTools(CrowdDebugTools tools)
    {
        if (tools.BoomRadiusCm <= 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.boomRadiusCm 必须 > 0。");
        if (!(tools.RingScale > 1d))
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.ringScale 必须 > 1。");
        if (tools.Impulse < 0d || double.IsNaN(tools.Impulse) || double.IsInfinity(tools.Impulse))
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.impulse 必须 ≥ 0。");
        if (double.IsNaN(tools.RingScale) || double.IsInfinity(tools.RingScale))
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.ringScale 必须 > 1。");
        if (tools.SpawnCount < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.spawnCount 必须 ≥ 1。");
        if (tools.SpawnUnitType < 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.spawnUnitType 必须 ≥ 0。");
        if (tools.SpawnRadiusClass < 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.spawnRadiusClass 必须 ≥ 0。");
        if (string.IsNullOrWhiteSpace(tools.PlaceTemplate))
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.placeTemplate 必填。");
        if (tools.RectSizeCm <= 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.rectSizeCm 必须 > 0。");
        if (tools.DiscRadiusCm <= 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.discRadiusCm 必须 > 0。");
        if (tools.PathWidthCm <= 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.pathWidthCm 必须 > 0。");
        if (tools.SpeedStepsPermille == null || tools.SpeedStepsPermille.Length == 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.speedStepsPermille 至少一档。");
        bool hasUnity = false;
        int previous = 0;
        for (int i = 0; i < tools.SpeedStepsPermille.Length; i++)
        {
            int step = tools.SpeedStepsPermille[i];
            if (step < 1 || step > TimeFlowService.MaxScalePermille)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationDebug.json: tools.speedStepsPermille[{i}] = {step}，需在 1..{TimeFlowService.MaxScalePermille}。");
            }

            if (i > 0 && step <= previous)
            {
                throw new InvalidOperationException("CrowdSimulationDebug.json: tools.speedStepsPermille 必须严格递增。");
            }

            if (step == TimeFlowService.DefaultScalePermille) hasUnity = true;
            previous = step;
        }

        if (!hasUnity)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationDebug.json: tools.speedStepsPermille 必须包含 {TimeFlowService.DefaultScalePermille}。");
        }
    }

    private static void ValidateRates(CrowdDebugRates rates)
    {
        if (rates.TickHzMin < 1 || rates.TickHzMax < rates.TickHzMin)
        {
            throw new InvalidOperationException(
                "CrowdSimulationDebug.json: rates.tickHzMin / tickHzMax 需满足 1 ≤ min ≤ max。");
        }

        if (!(rates.AvoidHzMin > 0d) || rates.AvoidHzMax < rates.AvoidHzMin
            || double.IsNaN(rates.AvoidHzMin) || double.IsNaN(rates.AvoidHzMax)
            || double.IsInfinity(rates.AvoidHzMin) || double.IsInfinity(rates.AvoidHzMax))
        {
            throw new InvalidOperationException(
                "CrowdSimulationDebug.json: rates.avoidHzMin / avoidHzMax 需满足 0 < min ≤ max。");
        }

        if (!(rates.AvoidStep > 0d) || double.IsNaN(rates.AvoidStep) || double.IsInfinity(rates.AvoidStep))
        {
            throw new InvalidOperationException("CrowdSimulationDebug.json: rates.avoidStep 必须 > 0。");
        }
    }

    private static void RequirePlaceTemplate(CrowdDebugTools tools, CrowdDeployPresentation deploy)
    {
        CrowdStructureTemplateDto? matched = null;
        for (int i = 0; i < deploy.Structures.Length; i++)
        {
            CrowdStructureTemplateDto row = deploy.Structures[i];
            if (!string.Equals(row.Id, tools.PlaceTemplate, StringComparison.Ordinal)) continue;
            matched = row;
            break;
        }

        if (matched == null)
        {
            throw new InvalidOperationException(
                $"CrowdSimulationDebug.json: tools.placeTemplate \"{tools.PlaceTemplate}\" 不在 presentation.deploy.structures。");
        }

        if (matched.Shape == S4DeployDemoRuntime.ShapePoint && matched.SizeCm != tools.RectSizeCm)
        {
            throw new InvalidOperationException(
                "CrowdSimulationDebug.json: tools.placeTemplate 是方形时，其 sizeCm 必须等于 tools.rectSizeCm。");
        }

        for (int i = 0; i < deploy.Structures.Length; i++)
        {
            CrowdStructureTemplateDto row = deploy.Structures[i];
            if (row.Shape == S4DeployDemoRuntime.ShapeDisc && row.SizeCm != tools.DiscRadiusCm)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationDebug.json: presentation.deploy.structures[{i}] 是圆时，sizeCm 必须等于 tools.discRadiusCm。");
            }

            if (row.Shape == S4DeployDemoRuntime.ShapeSpan && row.SizeCm != tools.PathWidthCm)
            {
                throw new InvalidOperationException(
                    $"CrowdSimulationDebug.json: presentation.deploy.structures[{i}] 是连线时，sizeCm 必须等于 tools.pathWidthCm。");
            }
        }
    }
}

internal sealed class CrowdDebugTools
{
    [JsonRequired] public int BoomRadiusCm { get; init; }
    [JsonRequired] public double RingScale { get; init; }
    [JsonRequired] public double Impulse { get; init; }
    [JsonRequired] public int SpawnCount { get; init; }
    [JsonRequired] public int SpawnUnitType { get; init; }
    [JsonRequired] public int SpawnRadiusClass { get; init; }
    [JsonRequired] public string PlaceTemplate { get; init; } = string.Empty;
    [JsonRequired] public int RectSizeCm { get; init; }
    [JsonRequired] public int DiscRadiusCm { get; init; }
    [JsonRequired] public int PathWidthCm { get; init; }
    [JsonRequired] public int[] SpeedStepsPermille { get; init; } = Array.Empty<int>();
}

internal sealed class CrowdDebugRates
{
    [JsonRequired] public int TickHzMin { get; init; }
    [JsonRequired] public int TickHzMax { get; init; }
    [JsonRequired] public double AvoidHzMin { get; init; }
    [JsonRequired] public double AvoidHzMax { get; init; }
    [JsonRequired] public double AvoidStep { get; init; }

    public string TickWindowText =>
        string.Create(CultureInfo.InvariantCulture, $"{TickHzMin}–{TickHzMax}");
}

internal sealed class CrowdDebugSession
{
    public CrowdDebugScriptEntry[]? Script { get; init; }
    public int? AutoReplayAtTick { get; init; }
    public string? CameraId { get; init; }
    public bool IncrementalNavVerify { get; init; }
}

internal sealed class CrowdDebugScriptEntry
{
    [JsonRequired] public int Tick { get; init; }
    [JsonRequired] public JsonObject Cmd { get; init; } = new();
}
