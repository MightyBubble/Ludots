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
    public CrowdDebugSession? Session { get; init; }
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

        return cfg;
    }
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
