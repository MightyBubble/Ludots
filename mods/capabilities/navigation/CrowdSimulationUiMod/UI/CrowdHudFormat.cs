using System.Globalization;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;

namespace CrowdSimulationUiMod.UI;

/// <summary>HUD 文案排版。只在采样且读数变化时调用。</summary>
internal static class CrowdHudFormat
{
    public const string NoSessionValidation = "校验:无会话";
    public const string AlertPrefix = "告警 ";

    public static string Ms(double value) =>
        double.IsNaN(value) ? "N/A" : value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string HitRate(int hits, int misses)
    {
        int denom = hits + misses;
        if (denom <= 0) return "N/A";
        return (hits * 100 / denom).ToString(CultureInfo.InvariantCulture) + "%";
    }

    public static string PathDetail(in CrowdPathHud hud)
    {
        string branch = !hud.HasCurrent
            ? "-"
            : hud.Branch switch
            {
                CorridorQuery.Branch.Tile => "NavMesh A*+漏斗",
                CorridorQuery.Branch.Hpa => "HPA* 走廊",
                _ => "不可达",
            };
        return $"请求帧 {hud.RequestTick}；生效帧 {hud.DueTick}；当前帧 {hud.Tick}；等待次数 {hud.Waits}" +
               $"；体型 {hud.ContextLabel}；后台线程 {hud.Threads}；分支 {branch}" +
               (hud.HasCurrent ? $"；可达 {(hud.Reachable ? "是" : "否")}；流场到达 {hud.FlowReached} 格" : "") +
               (hud.Slow ? "；后台慢注入中——仿真暂停等待,绝不使用半成品" : "") +
               (hud.Fault != null ? $"；服务故障 {hud.Fault}" : "");
    }

    public static string PathKeys(CrowdHudText text, in CrowdPathHud hud) =>
        text.KeysTemplate.Replace("{view}", hud.ViewLabel, StringComparison.Ordinal);

    public static string Tools(in CrowdDeployHud hud)
    {
        string avoid = hud.SessionActive ? hud.AvoidHz.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        string hz = hud.SessionActive ? hud.FixedHz.ToString(CultureInfo.InvariantCulture) : "-";
        string slices = hud.SessionActive ? hud.RebakeSlices.ToString(CultureInfo.InvariantCulture) : "-";
        string window = string.IsNullOrEmpty(hud.TickHzWindow) ? "-" : hud.TickHzWindow;
        string speed = hud.PlaybackHeld
            ? "暂停"
            : "×" + (hud.SpeedPermille / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
        string span = hud.HasSpanAnchor ? " · 连线起点已定" : "";
        return $"工具 {hud.ToolLabel} · 步进 {hz} Hz（窗 {window}）· 避让 {avoid} Hz · {speed} · 切片 {slices}{span}";
    }

    public static string Status(in CrowdDeployHud hud)
    {
        if (!hud.SessionActive) return "会话未激活";
        string selected = hud.Selected < 0 ? "-" : hud.Selected.ToString(CultureInfo.InvariantCulture);
        return $"tick {hud.Tick};单位 {hud.Units}(闲 {hud.Idle}/行 {hud.Moving}/达 {hud.Arrived}" +
               (hud.Unreachable + hud.Jumping > 0 ? $"/不可达 {hud.Unreachable}/跳跃 {hud.Jumping}" : "") +
               $");选中 {selected}";
    }

    public static string Rebake(in CrowdDeployHud hud)
    {
        if (!hud.SessionActive) return "最近重烘:-";
        if (!hud.HasRebake) return "最近重烘:无结构变更";
        string ms = hud.RebakeMs.ToString("0.##", CultureInfo.InvariantCulture);
        return $"最近重烘:tick {hud.RebakeExecTick}→{hud.RebakeReportTick} {(hud.RebakeIsPlace ? "建造" : "拆除")}" +
               $" · 脏 tile {hud.RebakeTiles} · 仅代价 {hud.RebakeCostOnly} · 可通行 {hud.RebakeContexts - hud.RebakeCostOnly}" +
               $" · 缓存命中 {hud.RebakeHits}/{hud.RebakeHits + hud.RebakeMisses}" +
               $" · 重规划 {hud.RebakeOrders} · 挤离 {hud.RebakeEvicted} · 无处安放 {hud.RebakeStuck} · 耗时 {ms} ms";
    }

    public static string Replay(in CrowdDeployHud hud)
    {
        if (hud.Interactive) return "回放已停用(交互模式)";
        if (!hud.RuntimePresent) return "回放:-";
        return hud.ReplayStatus switch
        {
            1 => WithDiagnostic("回放:逐位一致", hud),
            2 => WithDiagnostic(
                string.IsNullOrEmpty(hud.ReplayDivergenceSection)
                    ? $"回放:分歧 @tick {hud.ReplayDivergenceTick}"
                    : $"回放:分歧 @tick {hud.ReplayDivergenceTick} {hud.ReplayDivergenceSection}",
                hud),
            _ => "回放:待触发",
        };
    }

    private static string WithDiagnostic(string head, in CrowdDeployHud hud)
    {
        string diag = CrowdSimChecksum.FormatDiagnostic(hud.ReplayDiagnostic);
        return diag.Length == 0 ? head : $"{head} diag differs: {diag} @tick {hud.ReplayDiagnosticTick}";
    }

    public static string Counters(in CrowdDeployHud hud) => !hud.SessionActive
        ? ""
        : $"subStepClamped {hud.SubStepClamped} evictStuck {hud.EvictStuck} plans {hud.Plans} rebake {hud.Rebakes} fog {hud.FogRefreshes}";

    public static string CounterDetail(in CrowdDeployHud hud) => !hud.Expanded
        ? ""
        : $"contacts {hud.Contacts} pathWait {hud.PathWaits} fogCells vis {hud.FogVisible} explored {hud.FogExplored} ghost {hud.FogGhosts}";

    public static string Validation(in CrowdDeployHud hud) => hud.RuntimePresent ? hud.ValidationLine : NoSessionValidation;

    public static string Fuzz(in CrowdDeployHud hud) => hud.RuntimePresent ? hud.FuzzLine : CrowdSimulationRuntime.DebugFuzzNoSessionLine;

    public static string Tone(bool ran, bool failed) => failed ? "crowd-bad" : ran ? "crowd-ok" : "crowd-dim";

    public static string DeployKeys(CrowdHudText text, in CrowdDeployHud hud)
    {
        string placeHint = !hud.PreviewKnown
            ? ""
            : hud.PreviewShown
                ? PlaceHint(text, hud.PreviewReason)
                : text.PlaceOffMap;
        string activity = hud.BuildMode
            ? text.BuildActivity
                .Replace("{label}", hud.TemplateLabel, StringComparison.Ordinal)
                .Replace("{placeHint}", placeHint, StringComparison.Ordinal)
            : text.ObserveActivity;
        return text.KeysTemplate
            .Replace("{view}", hud.ViewLabel, StringComparison.Ordinal)
            .Replace("{tool}", hud.ToolLabel, StringComparison.Ordinal)
            .Replace("{templateIndex}", (hud.TemplateIndex + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{activity}", activity, StringComparison.Ordinal)
            .Replace("{pending}", hud.PendingNav ? text.PendingNav : "", StringComparison.Ordinal);
    }

    private static string PlaceHint(CrowdHudText text, CrowdPlacementReject reason) => reason switch
    {
        CrowdPlacementReject.None => text.PlaceOk,
        CrowdPlacementReject.OutOfBounds => Required(text.PlaceOutOfBounds, "placeOutOfBounds"),
        CrowdPlacementReject.OverlapsStructure => Required(text.PlaceOverlapsStructure, "placeOverlapsStructure"),
        CrowdPlacementReject.OverlapsBlocked => Required(text.PlaceOverlapsBlocked, "placeOverlapsBlocked"),
        CrowdPlacementReject.SpanTerrain => Required(text.PlaceSpanTerrain, "placeSpanTerrain"),
        CrowdPlacementReject.AnchorTerrain => Required(text.PlaceAnchorTerrain, "placeAnchorTerrain"),
        _ => throw new InvalidOperationException($"未知放置拒绝原因 {reason}。"),
    };

    private static string Required(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"CrowdSimulationDebug.json presentation.deploy.{key} 必填。");
        return value;
    }

    /// <summary>只开信念层时没有检视行，返回空串。</summary>
    public static string Inspect(in CrowdInspectReadout readout)
    {
        if (!readout.NavOverlays && readout.UnitColor == CrowdUnitColorMode.Off) return "";
        string line = readout.UnitColor == CrowdUnitColorMode.Off
            ? $"检视 移动 {readout.LayerIndex} 半径 {readout.RadiusIndex}"
            : $"检视 移动 {readout.LayerIndex} 半径 {readout.RadiusIndex} 着色 {ColorLabel(readout.UnitColor)}";
        return readout.TerrainTypes ? line + " 地形类型" : line;
    }

    public static string Belief(in CrowdInspectReadout readout) => readout.Belief
        ? $"视野组 {readout.BeliefGroup} 分歧 {readout.DivergentCells} 残影 {readout.Ghosts}"
        : "";

    private static string ColorLabel(CrowdUnitColorMode mode) => mode switch
    {
        CrowdUnitColorMode.Team => "队伍",
        CrowdUnitColorMode.Template => "模板",
        CrowdUnitColorMode.Priority => "优先级",
        _ => "关",
    };

    public static string Fault(string firstLine, double timeoutMs)
    {
        string line = firstLine.Length == 0 ? "路径服务故障(原因未记录)" : firstLine;
        return $"{line} · 超时 {Ms(timeoutMs)} ms · 仿真停摆，不在主线程补算";
    }

    public static void WriteNav(in CrowdNavReadout r, string[] lines)
    {
        lines[0] = "导航缓存 / 路径服务";
        lines[1] = r.HasTileCache
            ? $"tile 容量 {r.TileCapacity} 在册 {r.TileCount} · 命中率 {HitRate(r.TileHits, r.TileMisses)} · hit {r.TileHits} miss {r.TileMisses} evict {r.TileEvicts}"
            : "tile 容量 N/A 在册 N/A · 命中率 N/A · hit/miss/evict N/A";
        lines[2] = r.HasFlowCache
            ? $"流场容量 {r.FlowCapacity} 在册 {r.FlowCount} · 命中率 {HitRate(r.FlowHits, r.FlowMisses)} · hit {r.FlowHits} miss {r.FlowMisses} evict {r.FlowEvicts}"
            : $"流场容量 {r.FlowCapacity} · 命中率 N/A · hit/miss/evict N/A · 无结果缓存";
        lines[3] = $"常驻导航 {r.ResidentNavs}";
        lines[4] = r.HasPath
            ? $"规划请求 {r.PlanRequests} 待回 {r.PendingReplies} · 超时 {Ms(r.PlanTimeoutMs)} ms"
            : "规划请求 N/A 待回 N/A · 超时 N/A";
        lines[5] = $"往返 {Ms(r.RoundTripLastMs)}/{Ms(r.RoundTripMeanMs)} ms · 工人 {Ms(r.WorkerLastMs)}/{Ms(r.WorkerMeanMs)} ms";
        lines[6] = $"等待 worker {r.WaitTicks} tick";
        lines[7] = $"tick {r.Tick} · 命令日志 {r.CommandLog}";
    }

    /// <summary>遥测行。<paramref name="host"/> 是呈现端自己量的帧率、UI 重绘、编排、堆增长、分辨率，不来自仿真。</summary>
    public static void WriteTelemetry(in CrowdTelemetryReadout r, in CrowdHostReadout host, int sampleMs, int history, string[] lines)
    {
        string resolution = host.Width > 0 && host.Height > 0 ? $"{host.Width}x{host.Height}" : "N/A";
        string heap = double.IsNaN(host.HeapBytesPerSec)
            ? "N/A"
            : (host.HeapBytesPerSec / (1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture) + " MB/s";
        string world = r.WorldBakeRecorded
            ? $"{Ms(r.WorldBakeMs)} ms 缓存 {r.WorldCacheHits}/{r.WorldCacheHits + r.WorldCacheMisses}"
            : "N/A";
        lines[0] = $"遥测 采样 {sampleMs} ms · 曲线 {history} · 固定 {r.FixedHz} Hz";
        lines[1] = $"FPS {Ms(host.Fps)}";
        lines[2] = $"Sim {Ms(r.SimMs)} ms";
        lines[3] = $"避让 {Ms(r.AvoidMs)} ms";
        lines[4] = $"空间哈希 {Ms(r.HashMs)} ms";
        lines[5] = $"UI 重绘峰值 {Ms(host.UiRedrawPeakMs)} ms · 面板编排 {Ms(host.ComposeMs)} ms";
        lines[6] = $"堆增长 {heap}";
        lines[7] = r.HasHeading ? $"航向 {Ms(r.HeadingDeg)}°" : "航向 N/A";
        lines[8] = r.HasFlip ? $"反复 {r.FlipPct}%" : "反复 N/A";
        lines[9] = $"NavMesh {Ms(r.NavMeshMs)} ms · HPA* {Ms(r.HpaMs)} ms · 流场 {Ms(r.FlowMs)} ms";
        lines[10] = r.HasExtend
            ? $"走廊扩展 {r.ExtendCount} 次 {Ms(r.ExtendMs)} ms"
            : "走廊扩展 N/A · 端口未做走廊扩展";
        lines[11] = $"规划 {Ms(r.PlanMs)} ms · 烘焙 {Ms(r.NavBakeMs)} ms · 重烘 {Ms(r.RebakeMs)} ms";
        lines[12] = $"状态 闲 {r.Idle} 行 {r.Moving} 达 {r.Arrived} · 不可达 {r.Unreachable} 跳跃 {r.Jumping}";
        lines[13] = $"分辨率 {resolution} · nav格 {r.NavCellCm} cm · tile {r.TileCount}";
        lines[14] = $"世界生成 {world}";
        lines[15] = "烘焙超时 N/A · 仿真Worker/宿主RPC N/A · 同进程，烘焙在激活线程";
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

    public static void PrefixAlerts(string[] lines, bool[] hot)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (hot[i]) lines[i] = AlertPrefix + lines[i];
        }
    }

    public static string TeamLine(in CrowdPlayerHud hud, ReadOnlySpan<int> units, CrowdPlayerViewLabels labels)
    {
        var sb = new System.Text.StringBuilder(64);
        for (int p = 1; p <= hud.PlayerCount && p < units.Length; p++)
        {
            if (p > 1) sb.Append(' ');
            sb.Append('P').Append(p).Append(' ').Append(units[p]);
            if (p == hud.LocalPlayer) sb.Append(' ').Append(labels.Self);
        }

        return sb.ToString();
    }

    public static string CardLine(in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        string card = hud.Card switch
        {
            CrowdCommandCardState.Planning => labels.Planning,
            CrowdCommandCardState.Moving => labels.Moving,
            CrowdCommandCardState.Arrived => labels.Arrived,
            CrowdCommandCardState.Partial => labels.Partial,
            _ => labels.Idle,
        };
        return $"{card} · {labels.CountPlanning} {hud.Planning} · {labels.CountMoving} {hud.Moving} · {labels.Arrived} {hud.Arrived} · {labels.CountOther} {hud.Other}";
    }

    public static string ModeLine(in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        string mode = hud.Mode switch
        {
            CrowdOrderMode.Formation => labels.Formation,
            CrowdOrderMode.Converge => labels.Converge,
            CrowdOrderMode.Preserve => labels.Preserve,
            _ => labels.Idle,
        };
        return $"{hud.ShapeLabel} {mode} {(hud.MagicBox ? labels.MagicOn : labels.MagicOff)} {(hud.FogHide ? labels.FogOn : labels.FogOff)} " +
               $"{(hud.Route ? labels.RouteOn : labels.RouteOff)} {(hud.LineOfSight ? labels.LosOn : labels.LosOff)}";
    }

    public static string ClickLine(in CrowdPlayerHud hud, CrowdPlayerViewLabels labels) =>
        $"{labels.ClickSpawn} {labels.UnitType} {hud.ClickUnitType} · {labels.Radius} {hud.ClickRadius} · {labels.Count} {hud.ClickCount}";

    public static string DeployLine(in CrowdPlayerHud hud, CrowdPlayerViewLabels labels)
    {
        string scene = hud.SceneKind switch
        {
            1 => $" · {labels.SceneOutside}",
            2 => $" · {labels.SceneBehind}",
            _ => "",
        };
        return $"{labels.Deploy} {hud.DeployCount} · {labels.Radius} {hud.RadiusMin}-{hud.RadiusMax} · {labels.Layer} {hud.Layer}{scene}";
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
