using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace CrowdSimulationUiMod.UI;

/// <summary>assets/CrowdSimulationUi/hud.json：各面板采样间隔、遥测曲线长度、告警阈值。缺字段或越界即拒绝装载。</summary>
internal sealed class CrowdHudSettings
{
    public const int MaxHistory = 120;
    public const string RelativePath = "assets/CrowdSimulationUi/hud.json";

    [JsonRequired] public int Version { get; init; }
    [JsonRequired] public CrowdHudSampleRates SampleMs { get; init; } = null!;
    [JsonRequired] public int History { get; init; }
    [JsonRequired] public CrowdHudAlerts Alerts { get; init; } = null!;

    public static CrowdHudSettings Parse(string json, string path)
    {
        CrowdHudSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<CrowdHudSettings>(json, StrictJsonOptions.CreateCamelCase())
                ?? throw new InvalidOperationException($"{path}: 反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path}: {ex.Message}", ex);
        }

        settings.Validate(path);
        return settings;
    }

    private void Validate(string path)
    {
        if (Version != 1) throw new InvalidOperationException($"{path}: version 需为 1。");
        SampleMs.Validate(path);
        if (History < 2 || History > MaxHistory)
            throw new InvalidOperationException($"{path}: history 需为 2..{MaxHistory} 的整数。");
        Alerts.Validate(path);
    }

}

internal sealed class CrowdHudSampleRates
{
    [JsonRequired] public int Status { get; init; }
    [JsonRequired] public int Player { get; init; }
    [JsonRequired] public int Inspect { get; init; }
    [JsonRequired] public int Nav { get; init; }
    [JsonRequired] public int Telemetry { get; init; }

    public void Validate(string path)
    {
        Positive(path, "sampleMs.status", Status);
        Positive(path, "sampleMs.player", Player);
        Positive(path, "sampleMs.inspect", Inspect);
        Positive(path, "sampleMs.nav", Nav);
        Positive(path, "sampleMs.telemetry", Telemetry);
    }

    private static void Positive(string path, string field, int value)
    {
        if (value < 1) throw new InvalidOperationException($"{path}: {field} 需为 ≥ 1 的整数。");
    }
}

internal sealed class CrowdHudAlerts
{
    [JsonRequired] public int LowFps { get; init; }
    [JsonRequired] public double SimMs { get; init; }
    [JsonRequired] public double AvoidMs { get; init; }
    [JsonRequired] public double HeadingDeg { get; init; }
    [JsonRequired] public double FlipPct { get; init; }

    public void Validate(string path)
    {
        if (LowFps < 1) throw new InvalidOperationException($"{path}: alerts.lowFps 需为 ≥ 1 的整数。");
        Positive(path, "alerts.simMs", SimMs, double.PositiveInfinity);
        Positive(path, "alerts.avoidMs", AvoidMs, double.PositiveInfinity);
        Positive(path, "alerts.headingDeg", HeadingDeg, 180d);
        Positive(path, "alerts.flipPct", FlipPct, 100d);
    }

    private static void Positive(string path, string field, double value, double max)
    {
        if (!double.IsFinite(value) || value <= 0 || value > max)
            throw new InvalidOperationException($"{path}: {field} 需为 > 0 且 ≤ {max} 的有限数。");
    }
}
