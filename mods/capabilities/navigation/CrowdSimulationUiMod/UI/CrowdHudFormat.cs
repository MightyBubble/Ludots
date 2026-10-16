using System.Globalization;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// HUD 文案排版。只在采样且读数变化时调用；每个函数先清空目标缓冲再整段写入，数值直接格式化进缓冲，不产生中间字符串。
/// 数字格式照原型 format.js：缺值写 “—”，整数千分位，字节按 MB / KB。
/// </summary>
internal static class CrowdHudFormat
{
    public const string NoSessionValidation = "校验:无会话";
    public const string Missing = "—";

    public const string ResultIdle = "nf-result";
    public const string ResultOk = "nf-result nf-result-ok";
    public const string ResultBad = "nf-result nf-result-bad";

    private const string BoomHint = "左键点击地图：半径内单位全部移除，外圈被冲击波推开";
    private const string DemolishHint = "左键点击地图实体：移除最上层的一个（含地图自带建筑 / 桥）→ dirty 重烘焙";

    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static HudF1 F1(double value) => new(value);

    public static HudF2 F2(double value) => new(value);

    public static HudInt Int(long value) => new(value, true);

    public static HudInt Int(double value) => double.IsFinite(value) ? new HudInt((long)Math.Round(value), true) : new HudInt(0, false);

    public static HudBytes Bytes(double value) => new(value);

    public static HudRate Pct(int hits, int misses) => new(hits, misses);

    public static void PathDetail(UiTextBuffer b, in CrowdPathHud hud)
    {
        b.Clear();
        string branch = !hud.HasCurrent
            ? "-"
            : hud.Branch switch
            {
                CorridorQuery.Branch.Tile => "NavMesh A*+漏斗",
                CorridorQuery.Branch.Hpa => "HPA* 走廊",
                _ => "不可达",
            };
        b.Append(Inv, $"请求帧 {hud.RequestTick}；生效帧 {hud.DueTick}；当前帧 {hud.Tick}；等待次数 {hud.Waits}");
        b.Append(Inv, $"；体型 {hud.ContextLabel}；后台线程 {hud.Threads}；分支 {branch}");
        if (hud.HasCurrent) b.Append(Inv, $"；可达 {(hud.Reachable ? "是" : "否")}；流场到达 {hud.FlowReached} 格");
        if (hud.Slow) b.Append("；后台慢注入中——仿真暂停等待,绝不使用半成品");
        if (hud.Fault != null) b.Append(Inv, $"；服务故障 {hud.Fault}");
    }

    public static void PathKeys(UiTextBuffer b, CrowdHudText text, in CrowdPathHud hud)
    {
        b.Clear();
        ExpandTemplate(b, text.KeysTemplate, [new TemplateArg("{view}", hud.ViewLabel)]);
    }

    /// <summary>玩家条下的鼠标操作说明，取演示配置的 hud.keys。</summary>
    public static void MouseHint(UiTextBuffer b, CrowdHudText text, in CrowdDeployHud hud)
    {
        b.Clear();
        ExpandTemplate(b, text.KeysTemplate, [new TemplateArg("{view}", hud.ViewLabel)]);
    }

    /// <summary>
    /// 点击工具页的说明行。关闭和建造取演示配置的 observeActivity / buildActivity，
    /// 生成要报当前所选单位，没有玩家视图时退到 <paramref name="spawnUnit"/> 为空的写法。
    /// </summary>
    public static void ToolHint(UiTextBuffer b, CrowdHudText text, in CrowdDeployHud hud, int spawnCount, string? spawnUnit, int spawnRadiusCm)
    {
        b.Clear();
        switch (hud.Tool)
        {
            case CrowdClickTool.Off:
                b.Append(text.ObserveActivity);
                break;
            case CrowdClickTool.Boom:
                b.Append(BoomHint);
                break;
            case CrowdClickTool.Spawn:
                if (spawnUnit == null) b.Append("左键点击地图：生成所选单位（在「部署」页选择单位）");
                else b.Append(Inv, $"左键点击地图：生成 {spawnCount} 个 {spawnUnit} · {spawnRadiusCm} cm（在「部署」页选择单位）");
                break;
            case CrowdClickTool.Structure:
                string placeHint = !hud.PreviewKnown
                    ? ""
                    : hud.PreviewShown
                        ? PlaceHint(text, hud.PreviewReason)
                        : text.PlaceOffMap;
                ExpandTemplate(b, text.BuildActivity, [new TemplateArg("{label}", hud.TemplateLabel), new TemplateArg("{placeHint}", placeHint)]);
                break;
            case CrowdClickTool.Demolish:
                b.Append(DemolishHint);
                break;
            default:
                throw new InvalidOperationException($"未知点击工具 {hud.Tool}。");
        }

        if (hud.PendingNav) b.Append(text.PendingNav);
    }

    public static void Replay(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        if (hud.Interactive)
        {
            b.Append("回放已停用(交互模式)");
            return;
        }

        if (!hud.RuntimePresent)
        {
            b.Append("回放:-");
            return;
        }

        switch (hud.ReplayStatus)
        {
            case 1:
                b.Append("回放:逐位一致");
                break;
            case 2:
                b.Append(Inv, $"回放:分歧 @tick {hud.ReplayDivergenceTick}");
                if (!string.IsNullOrEmpty(hud.ReplayDivergenceSection)) b.Append(Inv, $" {hud.ReplayDivergenceSection}");
                break;
            default:
                b.Append("回放:待触发");
                return;
        }

        string diag = CrowdSimChecksum.FormatDiagnostic(hud.ReplayDiagnostic);
        if (diag.Length != 0) b.Append(Inv, $" diag differs: {diag} @tick {hud.ReplayDiagnosticTick}");
    }

    public static void Counters(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        if (!hud.SessionActive) return;
        b.Append(Inv, $"subStepClamped {hud.SubStepClamped} evictStuck {hud.EvictStuck} plans {hud.Plans} rebake {hud.Rebakes} fog {hud.FogRefreshes}");
    }

    public static void CounterDetail(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        if (!hud.Expanded) return;
        b.Append(Inv, $"contacts {hud.Contacts} pathWait {hud.PathWaits} fogCells vis {hud.FogVisible} explored {hud.FogExplored} ghost {hud.FogGhosts}");
    }

    public static void Validation(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        b.Append(hud.RuntimePresent ? hud.ValidationLine : NoSessionValidation);
    }

    public static void Fuzz(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        b.Append(hud.RuntimePresent ? hud.FuzzLine : CrowdSimulationRuntime.DebugFuzzNoSessionLine);
    }

    /// <summary>结果框的类名，取本类的字面量，调用方按引用比较。</summary>
    public static string ResultClass(bool ran, bool failed) => failed ? ResultBad : ran ? ResultOk : ResultIdle;

    public static string ColorLabel(CrowdUnitColorMode mode) => mode switch
    {
        CrowdUnitColorMode.Team => "队伍",
        CrowdUnitColorMode.Template => "兵种",
        CrowdUnitColorMode.Priority => "推挤优先级",
        _ => "关",
    };

    public static void Fault(UiTextBuffer b, string firstLine, double timeoutMs)
    {
        b.Clear();
        b.Append(firstLine.Length == 0 ? "路径服务故障(原因未记录)" : firstLine);
        b.Append(Inv, $" · 超时 {F1(timeoutMs)} ms · 仿真停摆，不在主线程补算");
    }

    private static string PlaceHint(CrowdHudText text, CrowdPlacementReject reason) => reason switch
    {
        CrowdPlacementReject.None => text.PlaceOk,
        CrowdPlacementReject.OutOfBounds => Required(text.PlaceOutOfBounds, "placeOutOfBounds"),
        CrowdPlacementReject.OverlapsStructure => Required(text.PlaceOverlapsStructure, "placeOverlapsStructure"),
        CrowdPlacementReject.OverlapsBlocked => Required(text.PlaceOverlapsBlocked, "placeOverlapsBlocked"),
        CrowdPlacementReject.InteriorArea => Required(text.PlaceInteriorArea, "placeInteriorArea"),
        CrowdPlacementReject.EndpointArea => Required(text.PlaceEndpointArea, "placeEndpointArea"),
        _ => throw new InvalidOperationException($"未知放置拒绝原因 {reason}。"),
    };

    private static string Required(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.deploy.{key} 必填。");
        return value;
    }

    /// <summary>
    /// 按占位符展开模板，等价于对每个参数依次做一次 <see cref="string.Replace(string, string)"/>，
    /// 但不建中间串；参数值里的占位符不会被再次展开。
    /// </summary>
    private static void ExpandTemplate(UiTextBuffer b, string template, ReadOnlySpan<TemplateArg> args)
    {
        ReadOnlySpan<char> rest = template.AsSpan();
        while (!rest.IsEmpty)
        {
            int open = rest.IndexOf('{');
            if (open < 0)
            {
                b.Append(rest);
                return;
            }

            b.Append(rest[..open]);
            rest = rest[open..];
            int matched = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (rest.StartsWith(args[i].Key.AsSpan(), StringComparison.Ordinal))
                {
                    matched = i;
                    break;
                }
            }

            if (matched < 0)
            {
                b.Append('{');
                rest = rest[1..];
                continue;
            }

            b.Append(args[matched].Text);
            rest = rest[args[matched].Key.Length..];
        }
    }

    private readonly record struct TemplateArg(string Key, string? Text);
}

/// <summary>一位小数；NaN、无穷写 “—”。</summary>
internal readonly struct HudF1(double value) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        HudNumber.Fixed(value, "0.0", destination, out charsWritten);

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString() => double.IsFinite(value) ? value.ToString("0.0", CultureInfo.InvariantCulture) : CrowdHudFormat.Missing;
}

/// <summary>两位小数；NaN、无穷写 “—”。</summary>
internal readonly struct HudF2(double value) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        HudNumber.Fixed(value, "0.00", destination, out charsWritten);

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString() => double.IsFinite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) : CrowdHudFormat.Missing;
}

/// <summary>整数千分位（en-US 写法 “12,345”）；没有值写 “—”。</summary>
internal readonly struct HudInt(long value, bool present) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (!present) return HudNumber.Copy(CrowdHudFormat.Missing, destination, out charsWritten);
        return value.TryFormat(destination, out charsWritten, "#,0", CultureInfo.InvariantCulture);
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString() => present ? value.ToString("#,0", CultureInfo.InvariantCulture) : CrowdHudFormat.Missing;
}

/// <summary>字节量：≥ 1e6 写 “x.x MB”，否则 “x KB”；NaN 写 “N/A”。</summary>
internal readonly struct HudBytes(double value) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        if (!double.IsFinite(value)) return HudNumber.Copy("N/A", destination, out charsWritten);
        bool mega = Math.Abs(value) >= 1e6;
        if (!(mega ? value / 1e6 : value / 1e3).TryFormat(destination, out int digits, mega ? "0.0" : "0", CultureInfo.InvariantCulture)) return false;
        if (!HudNumber.Copy(mega ? " MB" : " KB", destination[digits..], out int unit)) return false;
        charsWritten = digits + unit;
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString()
    {
        if (!double.IsFinite(value)) return "N/A";
        return Math.Abs(value) >= 1e6
            ? (value / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : (value / 1e3).ToString("0", CultureInfo.InvariantCulture) + " KB";
    }
}

/// <summary>命中率百分比：没有样本写 “—”。</summary>
internal readonly struct HudRate(int hits, int misses) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        int denom = hits + misses;
        if (denom <= 0) return HudNumber.Copy(CrowdHudFormat.Missing, destination, out charsWritten);
        charsWritten = 0;
        if (!((int)Math.Round(hits * 100d / denom)).TryFormat(destination, out int digits, default, CultureInfo.InvariantCulture) || digits >= destination.Length)
            return false;
        destination[digits] = '%';
        charsWritten = digits + 1;
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString()
    {
        int denom = hits + misses;
        return denom <= 0 ? CrowdHudFormat.Missing : ((int)Math.Round(hits * 100d / denom)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}

internal static class HudNumber
{
    public static bool Fixed(double value, string format, Span<char> destination, out int charsWritten)
    {
        if (!double.IsFinite(value)) return Copy(CrowdHudFormat.Missing, destination, out charsWritten);
        return value.TryFormat(destination, out charsWritten, format, CultureInfo.InvariantCulture);
    }

    public static bool Copy(string text, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (!text.AsSpan().TryCopyTo(destination)) return false;
        charsWritten = text.Length;
        return true;
    }
}
