using System.Globalization;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// HUD 文案排版。只在采样且读数变化时调用；每个函数先清空目标缓冲再整行写入，数值直接格式化进缓冲，不产生中间字符串。
/// </summary>
internal static class CrowdHudFormat
{
    public const string NoSessionValidation = "校验:无会话";
    public const string AlertLabel = "告警";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static HudMs Ms(double value) => new(value);

    public static HudRate HitRate(int hits, int misses) => new(hits, misses);

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

    public static void Tools(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        b.Append(Inv, $"工具 {hud.ToolLabel} · 步进 ");
        if (hud.SessionActive) b.Append(Inv, $"{hud.FixedHz}");
        else b.Append('-');
        b.Append(Inv, $" Hz（窗 {(string.IsNullOrEmpty(hud.TickHzWindow) ? "-" : hud.TickHzWindow)}）· 避让 ");
        if (hud.SessionActive) b.Append(Inv, $"{hud.AvoidHz:0.###}");
        else b.Append('-');
        b.Append(" Hz · ");
        if (hud.PlaybackHeld) b.Append("暂停");
        else b.Append(Inv, $"×{hud.SpeedPermille / 1000d:0.###}");
        b.Append(" · 切片 ");
        if (hud.SessionActive) b.Append(Inv, $"{hud.RebakeSlices}");
        else b.Append('-');
        if (hud.HasSpanAnchor) b.Append(" · 连线起点已定");
    }

    public static void Status(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        if (!hud.SessionActive)
        {
            b.Append("会话未激活");
            return;
        }

        b.Append(Inv, $"tick {hud.Tick};单位 {hud.Units}(闲 {hud.Idle}/行 {hud.Moving}/达 {hud.Arrived}");
        if (hud.Unreachable + hud.Traversing > 0) b.Append(Inv, $"/不可达 {hud.Unreachable}/穿越 {hud.Traversing}");
        b.Append(");选中 ");
        if (hud.Selected < 0) b.Append('-');
        else b.Append(Inv, $"{hud.Selected}");
    }

    public static void Rebake(UiTextBuffer b, in CrowdDeployHud hud)
    {
        b.Clear();
        if (!hud.SessionActive)
        {
            b.Append("最近重烘:-");
            return;
        }

        if (!hud.HasRebake)
        {
            b.Append("最近重烘:无结构变更");
            return;
        }

        b.Append(Inv, $"最近重烘:tick {hud.RebakeExecTick}→{hud.RebakeReportTick} {(hud.RebakeIsPlace ? "建造" : "拆除")}");
        b.Append(Inv, $" · 脏 tile {hud.RebakeTiles} · 仅代价 {hud.RebakeCostOnly} · 可通行 {hud.RebakeContexts - hud.RebakeCostOnly}");
        b.Append(Inv, $" · 缓存命中 {hud.RebakeHits}/{hud.RebakeHits + hud.RebakeMisses}");
        b.Append(Inv, $" · 重规划 {hud.RebakeOrders} · 挤离 {hud.RebakeEvicted} · 无处安放 {hud.RebakeStuck} · 耗时 {hud.RebakeMs:0.##} ms");
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

    public static string Tone(bool ran, bool failed) => failed ? "crowd-bad" : ran ? "crowd-ok" : "crowd-dim";

    public static void DeployKeys(UiTextBuffer b, CrowdHudText text, in CrowdDeployHud hud)
    {
        b.Clear();
        string placeHint = !hud.PreviewKnown
            ? ""
            : hud.PreviewShown
                ? PlaceHint(text, hud.PreviewReason)
                : text.PlaceOffMap;
        ExpandTemplate(b, text.KeysTemplate,
        [
            new TemplateArg("{view}", hud.ViewLabel),
            new TemplateArg("{tool}", hud.ToolLabel),
            new TemplateArg("{templateIndex}", hud.TemplateIndex + 1),
            hud.BuildMode
                ? new TemplateArg("{activity}", text.BuildActivity, hud.TemplateLabel, placeHint)
                : new TemplateArg("{activity}", text.ObserveActivity),
            new TemplateArg("{pending}", hud.PendingNav ? text.PendingNav : ""),
        ]);
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

            args[matched].WriteValue(b);
            rest = rest[args[matched].Key.Length..];
        }
    }

    /// <summary>模板参数：字符串、整数，或者带 {label}/{placeHint} 的二级模板。</summary>
    private readonly struct TemplateArg
    {
        private readonly string? _text;
        private readonly int _number;
        private readonly bool _isNumber;
        private readonly string? _label;
        private readonly string? _placeHint;

        public TemplateArg(string key, string? text)
        {
            Key = key;
            _text = text;
            _number = 0;
            _isNumber = false;
            _label = null;
            _placeHint = null;
        }

        public TemplateArg(string key, int number)
        {
            Key = key;
            _text = null;
            _number = number;
            _isNumber = true;
            _label = null;
            _placeHint = null;
        }

        public TemplateArg(string key, string activityTemplate, string? label, string placeHint)
        {
            Key = key;
            _text = activityTemplate;
            _number = 0;
            _isNumber = false;
            _label = label ?? "";
            _placeHint = placeHint;
        }

        public string Key { get; }

        public void WriteValue(UiTextBuffer b)
        {
            if (_isNumber)
            {
                b.Append(_number, default, Inv);
                return;
            }

            if (_label == null)
            {
                b.Append(_text);
                return;
            }

            ExpandTemplate(b, _text ?? "", [new TemplateArg("{label}", _label), new TemplateArg("{placeHint}", _placeHint)]);
        }
    }

    /// <summary>只开信念层时没有检视行，缓冲留空。</summary>
    public static void Inspect(UiTextBuffer b, in CrowdInspectReadout readout)
    {
        b.Clear();
        if (!readout.NavOverlays && readout.UnitColor == CrowdUnitColorMode.Off) return;
        b.Append(Inv, $"检视 移动 {readout.LayerIndex} 半径 {readout.RadiusIndex}");
        if (readout.UnitColor != CrowdUnitColorMode.Off) b.Append(Inv, $" 着色 {ColorLabel(readout.UnitColor)}");
        if (readout.TerrainTypes) b.Append(" 地形类型");
    }

    public static void Belief(UiTextBuffer b, in CrowdInspectReadout readout)
    {
        b.Clear();
        if (!readout.Belief) return;
        b.Append(Inv, $"视野组 {readout.BeliefGroup} 分歧 {readout.DivergentCells} 残影 {readout.Ghosts}");
    }

    private static string ColorLabel(CrowdUnitColorMode mode) => mode switch
    {
        CrowdUnitColorMode.Team => "队伍",
        CrowdUnitColorMode.Template => "模板",
        CrowdUnitColorMode.Priority => "优先级",
        _ => "关",
    };

    public static void Fault(UiTextBuffer b, string firstLine, double timeoutMs)
    {
        b.Clear();
        b.Append(firstLine.Length == 0 ? "路径服务故障(原因未记录)" : firstLine);
        b.Append(Inv, $" · 超时 {Ms(timeoutMs)} ms · 仿真停摆，不在主线程补算");
    }

    public static void WriteNav(in CrowdNavReadout r, HudLine[] lines)
    {
        lines[0].Set("导航缓存 / 路径服务");

        UiTextBuffer b = lines[1].Buffer;
        b.Clear();
        if (r.HasTileCache)
            b.Append(Inv, $"tile 容量 {r.TileCapacity} 在册 {r.TileCount} · 命中率 {HitRate(r.TileHits, r.TileMisses)} · hit {r.TileHits} miss {r.TileMisses} evict {r.TileEvicts}");
        else
            b.Append("tile 容量 N/A 在册 N/A · 命中率 N/A · hit/miss/evict N/A");

        b = lines[2].Buffer;
        b.Clear();
        if (r.HasFlowCache)
            b.Append(Inv, $"流场容量 {r.FlowCapacity} 在册 {r.FlowCount} · 命中率 {HitRate(r.FlowHits, r.FlowMisses)} · hit {r.FlowHits} miss {r.FlowMisses} evict {r.FlowEvicts}");
        else
            b.Append(Inv, $"流场容量 {r.FlowCapacity} · 命中率 N/A · hit/miss/evict N/A · 无结果缓存");

        b = lines[3].Buffer;
        b.Clear();
        b.Append(Inv, $"常驻导航 {r.ResidentNavs}");

        b = lines[4].Buffer;
        b.Clear();
        if (r.HasPath)
            b.Append(Inv, $"规划请求 {r.PlanRequests} 待回 {r.PendingReplies} · 超时 {Ms(r.PlanTimeoutMs)} ms");
        else
            b.Append("规划请求 N/A 待回 N/A · 超时 N/A");

        b = lines[5].Buffer;
        b.Clear();
        b.Append(Inv, $"往返 {Ms(r.RoundTripLastMs)}/{Ms(r.RoundTripMeanMs)} ms · 工人 {Ms(r.WorkerLastMs)}/{Ms(r.WorkerMeanMs)} ms");

        b = lines[6].Buffer;
        b.Clear();
        b.Append(Inv, $"等待 worker {r.WaitTicks} tick");

        b = lines[7].Buffer;
        b.Clear();
        b.Append(Inv, $"tick {r.Tick} · 命令日志 {r.CommandLog}");
    }

    /// <summary>遥测行。<paramref name="host"/> 是呈现端自己量的帧率、UI 重绘、编排、堆增长、分辨率，不来自仿真。</summary>
    public static void WriteTelemetry(in CrowdTelemetryReadout r, in CrowdHostReadout host, int sampleMs, int history, HudLine[] lines)
    {
        Begin(lines, 0).Append(Inv, $"遥测 采样 {sampleMs} ms · 曲线 {history} · 固定 {r.FixedHz} Hz");
        Begin(lines, 1).Append(Inv, $"FPS {Ms(host.Fps)}");
        Begin(lines, 2).Append(Inv, $"Sim {Ms(r.SimMs)} ms");
        Begin(lines, 3).Append(Inv, $"避让 {Ms(r.AvoidMs)} ms");
        Begin(lines, 4).Append(Inv, $"空间哈希 {Ms(r.HashMs)} ms");
        Begin(lines, 5).Append(Inv, $"UI 重绘峰值 {Ms(host.UiRedrawPeakMs)} ms · 面板编排 {Ms(host.ComposeMs)} ms");

        UiTextBuffer b = Begin(lines, 6);
        if (double.IsNaN(host.HeapBytesPerSec)) b.Append("堆增长 N/A");
        else b.Append(Inv, $"堆增长 {host.HeapBytesPerSec / (1024d * 1024d):0.00} MB/s");

        b = Begin(lines, 7);
        if (r.HasHeading) b.Append(Inv, $"航向 {Ms(r.HeadingDeg)}°");
        else b.Append("航向 N/A");

        b = Begin(lines, 8);
        if (r.HasFlip) b.Append(Inv, $"反复 {r.FlipPct}%");
        else b.Append("反复 N/A");

        Begin(lines, 9).Append(Inv, $"NavMesh {Ms(r.NavMeshMs)} ms · HPA* {Ms(r.HpaMs)} ms · 流场 {Ms(r.FlowMs)} ms");

        b = Begin(lines, 10);
        if (r.HasExtend) b.Append(Inv, $"走廊扩展 {r.ExtendCount} 次 {Ms(r.ExtendMs)} ms");
        else b.Append("走廊扩展 N/A · 端口未做走廊扩展");

        Begin(lines, 11).Append(Inv, $"规划 {Ms(r.PlanMs)} ms · 烘焙 {Ms(r.NavBakeMs)} ms · 重烘 {Ms(r.RebakeMs)} ms");
        Begin(lines, 12).Append(Inv, $"状态 闲 {r.Idle} 行 {r.Moving} 达 {r.Arrived} · 不可达 {r.Unreachable} 穿越 {r.Traversing}");

        b = Begin(lines, 13);
        b.Append("分辨率 ");
        if (host.Width > 0 && host.Height > 0) b.Append(Inv, $"{host.Width}x{host.Height}");
        else b.Append("N/A");
        b.Append(Inv, $" · nav格 {r.NavCellCm} cm · tile {r.TileCount}");

        b = Begin(lines, 14);
        b.Append("世界生成 ");
        if (r.WorldBakeRecorded) b.Append(Inv, $"{Ms(r.WorldBakeMs)} ms 缓存 {r.WorldCacheHits}/{r.WorldCacheHits + r.WorldCacheMisses}");
        else b.Append("N/A");

        Begin(lines, 15).Append("烘焙超时 N/A · 仿真Worker/宿主RPC N/A · 同进程，烘焙在激活线程");
    }

    private static UiTextBuffer Begin(HudLine[] lines, int index)
    {
        UiTextBuffer b = lines[index].Buffer;
        b.Clear();
        return b;
    }

    /// <summary>读数缺失（NaN 或没有样本）不告警。</summary>
    public static void MarkAlerts(in CrowdTelemetryReadout r, double fps, CrowdHudAlerts alerts, bool[] hot)
    {
        Array.Clear(hot);
        hot[CrowdHudState.FpsLine] = !double.IsNaN(fps) && fps < alerts.LowFps;
        hot[CrowdHudState.SimLine] = !double.IsNaN(r.SimMs) && r.SimMs > alerts.SimMs;
        hot[CrowdHudState.AvoidLine] = !double.IsNaN(r.AvoidMs) && r.AvoidMs > alerts.AvoidMs;
        hot[CrowdHudState.HeadingLine] = r.HasHeading && r.HeadingDeg > alerts.HeadingDeg;
        hot[CrowdHudState.FlipLine] = r.HasFlip && r.FlipPct > alerts.FlipPct;
    }

    public static void TeamLine(UiTextBuffer b, in CrowdPlayerHud hud, ReadOnlySpan<int> units, CrowdPlayerViewLabels labels)
    {
        b.Clear();
        for (int p = 1; p <= hud.PlayerCount && p < units.Length; p++)
        {
            if (p > 1) b.Append(' ');
            b.Append(Inv, $"P{p} {units[p]}");
            if (p == hud.LocalPlayer) b.Append(Inv, $" {labels.Self}");
        }
    }

    public static void CardLine(UiTextBuffer b, in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        b.Clear();
        string card = hud.Card switch
        {
            CrowdCommandCardState.Planning => labels.Planning,
            CrowdCommandCardState.Moving => labels.Moving,
            CrowdCommandCardState.Arrived => labels.Arrived,
            CrowdCommandCardState.Partial => labels.Partial,
            _ => labels.Idle,
        };
        b.Append(Inv, $"{card} · {labels.CountPlanning} {hud.Planning} · {labels.CountMoving} {hud.Moving} · {labels.Arrived} {hud.Arrived} · {labels.CountOther} {hud.Other}");
    }

    public static void ModeLine(UiTextBuffer b, in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        b.Clear();
        string mode = hud.Mode switch
        {
            CrowdOrderMode.Formation => labels.Formation,
            CrowdOrderMode.Converge => labels.Converge,
            CrowdOrderMode.Preserve => labels.Preserve,
            _ => labels.Idle,
        };
        b.Append(Inv, $"{hud.ShapeLabel} {mode} {(hud.MagicBox ? labels.MagicOn : labels.MagicOff)} {(hud.FogHide ? labels.FogOn : labels.FogOff)} ");
        b.Append(Inv, $"{(hud.Route ? labels.RouteOn : labels.RouteOff)} {(hud.LineOfSight ? labels.LosOn : labels.LosOff)}");
    }

    public static void ClickLine(UiTextBuffer b, in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        b.Clear();
        b.Append(Inv, $"{labels.ClickSpawn} {labels.UnitType} {hud.ClickUnitType} · {labels.Radius} {hud.ClickRadius} · {labels.Count} {hud.ClickCount}");
    }

    public static void DeployLine(UiTextBuffer b, in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        b.Clear();
        b.Append(Inv, $"{labels.Deploy} {hud.DeployCount} · {labels.Radius} {hud.RadiusMin}-{hud.RadiusMax} · {labels.Layer} {hud.Layer}");
        if (hud.SceneKind == 1) b.Append(Inv, $" · {labels.SceneOutside}");
        else if (hud.SceneKind == 2) b.Append(Inv, $" · {labels.SceneBehind}");
    }
}

/// <summary>毫秒读数：NaN 写 “N/A”，否则保留一位小数。</summary>
internal readonly struct HudMs(double value) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (!double.IsNaN(value)) return value.TryFormat(destination, out charsWritten, "0.0", CultureInfo.InvariantCulture);
        charsWritten = 0;
        if (!"N/A".AsSpan().TryCopyTo(destination)) return false;
        charsWritten = 3;
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString() => double.IsNaN(value) ? "N/A" : value.ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>命中率百分比：没有样本写 “N/A”。</summary>
internal readonly struct HudRate(int hits, int misses) : ISpanFormattable
{
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        int denom = hits + misses;
        if (denom <= 0)
        {
            charsWritten = 0;
            if (!"N/A".AsSpan().TryCopyTo(destination)) return false;
            charsWritten = 3;
            return true;
        }

        if (!(hits * 100 / denom).TryFormat(destination, out int digits, default, CultureInfo.InvariantCulture) || digits >= destination.Length)
        {
            charsWritten = 0;
            return false;
        }

        destination[digits] = '%';
        charsWritten = digits + 1;
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public override string ToString()
    {
        int denom = hits + misses;
        return denom <= 0 ? "N/A" : (hits * 100 / denom).ToString(CultureInfo.InvariantCulture) + "%";
    }
}

/// <summary>呈现端自己量的读数，NaN 表示宿主没有提供。</summary>
internal readonly record struct CrowdHostReadout(
    double Fps,
    double UiRedrawPeakMs,
    double ComposeMs,
    double HeapBytesPerSec,
    int Width,
    int Height);
