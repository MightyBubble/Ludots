using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// CrowdSimulationDebug.json 的严格形状。未知键在装载时拒绝。
/// 指令载荷留在 <see cref="CrowdDebugScriptEntry.Cmd"/>，由 <c>CrowdSimCommand.Parse</c> 校验。
/// </summary>
internal sealed class CrowdSimulationDebugConfig
{
    public CrowdDebugWorker? Worker { get; init; }
    public CrowdDebugSession? Session { get; init; }
    public CrowdPresentationConfig? Presentation { get; init; }

    public TimeSpan? PlanTimeout =>
        Worker == null ? null : TimeSpan.FromMilliseconds(Worker.PlanTimeoutMs);

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

        if (cfg.Worker != null && cfg.Worker.PlanTimeoutMs <= 0)
        {
            throw new InvalidOperationException("CrowdSimulationDebug.json: worker.planTimeoutMs 需为 > 0 的整数毫秒。");
        }

        if (cfg.Presentation != null)
        {
            CrowdPresentationConfig.Validate(cfg.Presentation);
        }

        if (cfg.Session is { ValidateEveryNTicks: < 0 })
        {
            throw new InvalidOperationException(
                "CrowdSimulationDebug.json: session.validateEveryNTicks 需为 ≥ 0 的整数。");
        }

        return cfg;
    }
}

internal sealed class CrowdDebugWorker
{
    [JsonRequired] public int PlanTimeoutMs { get; init; }
}

internal sealed class CrowdDebugSession
{
    public bool Autostart { get; init; }
    public CrowdDebugScriptEntry[]? Script { get; init; }
    public int? AutoReplayAtTick { get; init; }
    public string? CameraId { get; init; }
    public bool IncrementalNavVerify { get; init; }
    /// <summary>0 = 关闭。正整数 = 每这么多 tick 跑仿真与导航不变量。</summary>
    public int ValidateEveryNTicks { get; init; }
}

internal sealed class CrowdDebugScriptEntry
{
    [JsonRequired] public int Tick { get; init; }
    [JsonRequired] public JsonObject Cmd { get; init; } = new();
}
