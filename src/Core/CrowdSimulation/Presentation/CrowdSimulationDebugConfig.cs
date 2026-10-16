using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Mathematics.FixedPoint;

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
    public CrowdDebugFuzz? Fuzz { get; init; }
    public CrowdPresentationConfig? Presentation { get; init; }

    public static CrowdSimulationDebugConfig Load(JsonObject debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
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

        if (cfg.Session?.ScriptAsset is { } scriptAsset && string.IsNullOrWhiteSpace(scriptAsset))
            throw new InvalidOperationException("CrowdSimulationDebug.json: session.scriptAsset 不能为空串。");
        if (cfg.Tools != null) ValidateTools(cfg.Tools);
        if (cfg.Rates != null) ValidateRates(cfg.Rates);
        if (cfg.Fuzz != null) ValidateFuzz(cfg.Fuzz);
        if (cfg.Presentation?.Kind == CrowdPresentationConfig.KindDeploy && (cfg.Tools == null || cfg.Rates == null || cfg.Fuzz == null))
        {
            throw new InvalidOperationException("CrowdSimulationDebug.json: 部署演示必须带 tools、rates 与 fuzz。");
        }

        if (cfg.Tools != null && cfg.Presentation?.Deploy != null)
        {
            RequirePlaceEntry(cfg.Tools, cfg.Presentation.Deploy);
        }

        return cfg;
    }

    /// <summary>
    /// 会话脚本：先是 <c>session.scriptAsset</c> 指向文档根 <c>script</c> 的行(与回归场景同形)，再接 <c>session.script</c>。
    /// 资产按 mod 内相对路径经 <paramref name="openAsset"/> 打开。
    /// </summary>
    public static CrowdDebugScriptEntry[]? SessionScript(CrowdDebugSession? session, Func<string, Stream> openAsset)
    {
        ArgumentNullException.ThrowIfNull(openAsset);
        if (session?.ScriptAsset == null) return session?.Script;

        JsonObject document;
        using (Stream stream = openAsset(session.ScriptAsset))
        {
            document = JsonNode.Parse(stream) as JsonObject
                ?? throw new InvalidOperationException($"{session.ScriptAsset}: 根必须是对象。");
        }

        CrowdDebugScriptEntry[] assetRows;
        try
        {
            assetRows = CrowdSimRegressCatalog.Script(document).Deserialize<CrowdDebugScriptEntry[]>(StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{session.ScriptAsset}: script 反序列化为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{session.ScriptAsset}: {ex.Message}", ex);
        }

        if (session.Script is not { Length: > 0 } inline) return assetRows;
        var rows = new CrowdDebugScriptEntry[assetRows.Length + inline.Length];
        assetRows.CopyTo(rows, 0);
        inline.CopyTo(rows, assetRows.Length);
        return rows;
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
        if (string.IsNullOrWhiteSpace(tools.PlaceEntry))
            throw new InvalidOperationException("CrowdSimulationDebug.json: tools.placeEntry 必填。");
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

    private static void ValidateFuzz(CrowdDebugFuzz fuzz)
    {
        if (fuzz.SliceMs < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.sliceMs 必须 ≥ 1。");
        if (fuzz.OpCount < 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.opCount 必须 ≥ 0。");
        if (fuzz.StepsPerOp < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.stepsPerOp 必须 ≥ 1。");
        if (fuzz.ProbeHandles < 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.probeHandles 必须 ≥ 0。");
        if (fuzz.SpawnCount < 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.spawnCount 必须 ≥ 0。");
        if (string.IsNullOrWhiteSpace(fuzz.PlaceTemplate))
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.placeTemplate 必填。");
        if (fuzz.PlaceSizeCm < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.placeSizeCm 必须 ≥ 1。");
        if (string.IsNullOrWhiteSpace(fuzz.OrderShape))
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.orderShape 必填。");
        if (fuzz.OrderMemberCap < 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.orderMemberCap 必须 ≥ 0。");
        if (fuzz.StallGuard < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.stallGuard 必须 ≥ 1。");
        if (fuzz.Ops == null || fuzz.Ops.Length == 0)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.ops 至少一条。");
        bool placePath = false;
        for (int i = 0; i < fuzz.Ops.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(fuzz.Ops[i].Id))
                throw new InvalidOperationException($"CrowdSimulationDebug.json: fuzz.ops[{i}].id 必填。");
            if (fuzz.Ops[i].Id == "placePath") placePath = true;
        }

        if (!placePath) return;
        if (string.IsNullOrWhiteSpace(fuzz.PathTemplate))
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.pathTemplate 必填。");
        if (fuzz.PathWidthCm < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.pathWidthCm 必须 ≥ 1。");
        if (fuzz.PathLengthCm < 1)
            throw new InvalidOperationException("CrowdSimulationDebug.json: fuzz.pathLengthCm 必须 ≥ 1。");
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

    private static void RequirePlaceEntry(CrowdDebugTools tools, CrowdDeployPresentation deploy)
    {
        for (int i = 0; i < deploy.Palette.Length; i++)
        {
            if (string.Equals(deploy.Palette[i].Entry, tools.PlaceEntry, StringComparison.Ordinal)) return;
        }

        throw new InvalidOperationException(
            $"CrowdSimulationDebug.json: tools.placeEntry \"{tools.PlaceEntry}\" 不在 presentation.deploy.palette。");
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
    [JsonRequired] public string PlaceEntry { get; init; } = string.Empty;
    [JsonRequired] public int[] SpeedStepsPermille { get; init; } = Array.Empty<int>();
}

internal sealed class CrowdDebugFuzz
{
    [JsonRequired] public int SliceMs { get; init; }
    [JsonRequired] public int OpCount { get; init; }
    [JsonRequired] public long Seed { get; init; }
    [JsonRequired] public int StepsPerOp { get; init; }
    [JsonRequired] public int ProbeHandles { get; init; }
    [JsonRequired] public int SpawnCount { get; init; }
    [JsonRequired] public string PlaceTemplate { get; init; } = string.Empty;
    [JsonRequired] public int PlaceSizeCm { get; init; }
    [JsonRequired] public string OrderShape { get; init; } = string.Empty;
    [JsonRequired] public int OrderMemberCap { get; init; }
    [JsonRequired] public int StallGuard { get; init; }
    public string PathTemplate { get; init; } = "";
    public int PathWidthCm { get; init; }
    public int PathLengthCm { get; init; }
    [JsonRequired] public CrowdDebugFuzzOp[] Ops { get; init; } = Array.Empty<CrowdDebugFuzzOp>();

    public CrowdFuzzPlan ToPlan(CrowdDebugTools tools)
    {
        var ops = new CrowdFuzzOp[Ops.Length];
        for (int i = 0; i < Ops.Length; i++)
        {
            CrowdDebugFuzzOp row = Ops[i];
            ops[i] = new CrowdFuzzOp
            {
                Id = row.Id,
                Structure = row.Structure,
                ProbeHandles = row.ProbeHandles,
                Reserved = row.Reserved,
            };
        }

        return new CrowdFuzzPlan
        {
            Seed = Seed,
            OpCount = OpCount,
            StepsPerOp = StepsPerOp,
            ProbeHandleCount = ProbeHandles,
            SpawnCount = SpawnCount,
            PlaceTemplate = PlaceTemplate,
            PlaceSizeCm = PlaceSizeCm,
            OrderShape = OrderShape,
            OrderMemberCap = OrderMemberCap,
            StallGuard = StallGuard,
            PathTemplate = PathTemplate,
            PathWidthCm = PathWidthCm,
            PathLengthCm = PathLengthCm,
            BoomRadiusCm = tools.BoomRadiusCm,
            RingScale = Fix64.FromDouble(tools.RingScale),
            Impulse = Fix64.FromDouble(tools.Impulse),
            Ops = ops,
        };
    }
}

internal sealed class CrowdDebugFuzzOp
{
    [JsonRequired] public string Id { get; init; } = string.Empty;
    [JsonRequired] public bool Structure { get; init; }
    [JsonRequired] public bool ProbeHandles { get; init; }
    [JsonRequired] public bool Reserved { get; init; }
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
    public string? ScriptAsset { get; init; }
    public int? AutoReplayAtTick { get; init; }
    public string? CameraId { get; init; }
    public bool IncrementalNavVerify { get; init; }
}

internal sealed class CrowdDebugScriptEntry
{
    [JsonRequired] public int Tick { get; init; }
    [JsonRequired] public JsonObject Cmd { get; init; } = new();
}
