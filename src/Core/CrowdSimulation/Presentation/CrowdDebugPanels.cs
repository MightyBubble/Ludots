using System;
using System.Globalization;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.CrowdSimulation.Presentation;

internal struct CrowdNavReadout
{
    public const int Lines = 8;

    public bool HasTileCache;
    public int TileCapacity;
    public int TileCount;
    public int TileHits;
    public int TileMisses;
    public int TileEvicts;
    public int FlowCapacity;
    public int ResidentNavs;
    public bool HasPath;
    public long PlanRequests;
    public int PendingReplies;
    public double RoundTripLastMs;
    public double RoundTripMeanMs;
    public double WorkerLastMs;
    public double WorkerMeanMs;
    public long WaitTicks;
    public int Tick;
    public int CommandLog;
    public double PlanTimeoutMs;

    public readonly bool Same(in CrowdNavReadout o) =>
        HasTileCache == o.HasTileCache
        && TileCapacity == o.TileCapacity
        && TileCount == o.TileCount
        && TileHits == o.TileHits
        && TileMisses == o.TileMisses
        && TileEvicts == o.TileEvicts
        && FlowCapacity == o.FlowCapacity
        && ResidentNavs == o.ResidentNavs
        && HasPath == o.HasPath
        && PlanRequests == o.PlanRequests
        && PendingReplies == o.PendingReplies
        && CrowdDebugText.Tenth(RoundTripLastMs) == CrowdDebugText.Tenth(o.RoundTripLastMs)
        && CrowdDebugText.Tenth(RoundTripMeanMs) == CrowdDebugText.Tenth(o.RoundTripMeanMs)
        && CrowdDebugText.Tenth(WorkerLastMs) == CrowdDebugText.Tenth(o.WorkerLastMs)
        && CrowdDebugText.Tenth(WorkerMeanMs) == CrowdDebugText.Tenth(o.WorkerMeanMs)
        && WaitTicks == o.WaitTicks
        && Tick == o.Tick
        && CommandLog == o.CommandLog
        && CrowdDebugText.Tenth(PlanTimeoutMs) == CrowdDebugText.Tenth(o.PlanTimeoutMs);

    public static CrowdNavReadout From(CrowdSimSession session)
    {
        var cache = session.NavTileCache;
        var service = session.Planner?.Service;
        PathServiceStats stats = default;
        bool hasPath = service != null;
        if (hasPath) stats = service!.CopyStats();
        int resident = session.Navs.Count;
        if (session.Beliefs != null) resident += session.Beliefs.VariantCount();
        return new CrowdNavReadout
        {
            HasTileCache = cache != null,
            TileCapacity = cache?.Capacity ?? 0,
            TileCount = cache?.Count ?? 0,
            TileHits = cache?.Hits ?? 0,
            TileMisses = cache?.Misses ?? 0,
            TileEvicts = cache?.Evictions ?? 0,
            FlowCapacity = session.Config.Flowfield.CacheCapacity,
            ResidentNavs = resident,
            HasPath = hasPath,
            PlanRequests = hasPath ? stats.Requests : 0,
            PendingReplies = hasPath ? stats.Pending : 0,
            RoundTripLastMs = hasPath ? stats.RoundTripLastMs : double.NaN,
            RoundTripMeanMs = hasPath ? stats.RoundTripMeanMs : double.NaN,
            WorkerLastMs = hasPath ? stats.WorkerLastMs : double.NaN,
            WorkerMeanMs = hasPath ? stats.WorkerMeanMs : double.NaN,
            WaitTicks = session.Telemetry.StallTicks,
            Tick = session.TickCount,
            CommandLog = session.Commands.Log.Count,
            PlanTimeoutMs = hasPath ? stats.PlanTimeoutMs : double.NaN,
        };
    }

    public readonly void Write(string[] lines)
    {
        string tile = HasTileCache
            ? $"tile 容量 {TileCapacity} 在册 {TileCount} 命中率 {CrowdDebugText.HitRate(TileHits, TileMisses)} hit {TileHits} miss {TileMisses} evict {TileEvicts}"
            : "tile 容量 N/A 在册 N/A 命中率 N/A hit/miss/evict N/A";
        lines[0] = "导航缓存 / 路径服务";
        lines[1] = tile;
        lines[2] = $"流场容量 {FlowCapacity} 命中率 N/A hit/miss/evict N/A（无结果缓存）";
        lines[3] = $"常驻导航 {ResidentNavs}";
        lines[4] = HasPath
            ? $"规划请求 {PlanRequests} 待回 {PendingReplies} 超时 {CrowdDebugText.Ms(PlanTimeoutMs)} ms"
            : "规划请求 N/A 待回 N/A 超时 N/A";
        lines[5] = $"往返 {CrowdDebugText.Ms(RoundTripLastMs)}/{CrowdDebugText.Ms(RoundTripMeanMs)} ms 工人 {CrowdDebugText.Ms(WorkerLastMs)}/{CrowdDebugText.Ms(WorkerMeanMs)} ms";
        lines[6] = $"等待 worker {WaitTicks} tick";
        lines[7] = $"tick {Tick} 命令日志 {CommandLog}";
    }
}

internal struct CrowdTelemetryReadout
{
    public const int Lines = 16;
    public const int FpsLine = 1;
    public const int SimLine = 2;
    public const int AvoidLine = 3;
    public const int HeadingLine = 7;
    public const int FlipLine = 8;

    public double Fps;
    public double SimMs;
    public double AvoidMs;
    public double HashMs;
    public double RenderMs;
    public double HeapBytesPerSec;
    public double HeadingDeg;
    public bool HasHeading;
    public int FlipPct;
    public bool HasFlip;
    public int FixedHz;
    public int SampleMs;
    public int History;
    public double NavMeshMs;
    public double HpaMs;
    public double FlowMs;
    public double PlanMs;
    public double NavBakeMs;
    public double RebakeMs;
    public int Idle, Moving, Arrived, Unreachable, Jumping;
    public int ViewWidth, ViewHeight;
    public int NavCellCm;
    public int TileCount;
    public double WorldBakeMs;
    public int WorldCacheHits, WorldCacheMisses;
    public bool WorldBakeRecorded;

    public readonly bool Same(in CrowdTelemetryReadout o) =>
        CrowdDebugText.Whole(Fps) == CrowdDebugText.Whole(o.Fps)
        && CrowdDebugText.Tenth(SimMs) == CrowdDebugText.Tenth(o.SimMs)
        && CrowdDebugText.Tenth(AvoidMs) == CrowdDebugText.Tenth(o.AvoidMs)
        && CrowdDebugText.Tenth(HashMs) == CrowdDebugText.Tenth(o.HashMs)
        && CrowdDebugText.Tenth(RenderMs) == CrowdDebugText.Tenth(o.RenderMs)
        && CrowdDebugText.Tenth(HeapBytesPerSec) == CrowdDebugText.Tenth(o.HeapBytesPerSec)
        && CrowdDebugText.Tenth(HeadingDeg) == CrowdDebugText.Tenth(o.HeadingDeg)
        && HasHeading == o.HasHeading
        && FlipPct == o.FlipPct
        && HasFlip == o.HasFlip
        && FixedHz == o.FixedHz
        && SampleMs == o.SampleMs
        && History == o.History
        && CrowdDebugText.Tenth(NavMeshMs) == CrowdDebugText.Tenth(o.NavMeshMs)
        && CrowdDebugText.Tenth(HpaMs) == CrowdDebugText.Tenth(o.HpaMs)
        && CrowdDebugText.Tenth(FlowMs) == CrowdDebugText.Tenth(o.FlowMs)
        && CrowdDebugText.Tenth(PlanMs) == CrowdDebugText.Tenth(o.PlanMs)
        && CrowdDebugText.Tenth(NavBakeMs) == CrowdDebugText.Tenth(o.NavBakeMs)
        && CrowdDebugText.Tenth(RebakeMs) == CrowdDebugText.Tenth(o.RebakeMs)
        && Idle == o.Idle && Moving == o.Moving && Arrived == o.Arrived
        && Unreachable == o.Unreachable && Jumping == o.Jumping
        && ViewWidth == o.ViewWidth && ViewHeight == o.ViewHeight
        && NavCellCm == o.NavCellCm && TileCount == o.TileCount
        && WorldBakeRecorded == o.WorldBakeRecorded
        && CrowdDebugText.Tenth(WorldBakeMs) == CrowdDebugText.Tenth(o.WorldBakeMs)
        && WorldCacheHits == o.WorldCacheHits && WorldCacheMisses == o.WorldCacheMisses;

    public static CrowdTelemetryReadout From(
        CrowdSimSession session,
        CrowdSimulationRuntime? runtime,
        PresentationTimingDiagnostics? timing,
        int viewWidth,
        int viewHeight,
        double heapBytesPerSec,
        int sampleMs,
        int history)
    {
        var telemetry = session.Telemetry;
        var phases = telemetry.LastPhaseTicks;
        var service = session.Planner?.Service;
        PathServiceStats stats = default;
        bool hasPath = service != null;
        if (hasPath) stats = service!.CopyStats();
        double fps = double.NaN;
        double render = double.NaN;
        if (timing != null)
        {
            float frame = timing.LastWallFrameMs > 0f ? timing.LastWallFrameMs : timing.LastFrameMs;
            if (frame > 0f) fps = 1000d / frame;
            render = timing.LastUiRenderMs;
        }

        var report = session.LastRebakeReport;
        double rebake = report != null
            ? CrowdSimulationTelemetry.TicksToMs(report.ElapsedTicks)
            : CrowdSimulationTelemetry.TicksToMs(phases.Rebake);
        int side = session.Config.NavCellsPerSide;
        int perTile = session.Config.Hpa.NavCellsPerTile;
        int tiles = perTile > 0 ? (side / perTile) * (side / perTile) : 0;
        bool baked = runtime is { WorldBakeRecorded: true };
        return new CrowdTelemetryReadout
        {
            Fps = fps,
            SimMs = telemetry.LastTickMs,
            AvoidMs = CrowdSimulationTelemetry.TicksToMs(phases.Separation),
            HashMs = CrowdSimulationTelemetry.TicksToMs(phases.Hash),
            RenderMs = render,
            HeapBytesPerSec = heapBytesPerSec,
            HeadingDeg = telemetry.HeadingDeg,
            HasHeading = telemetry.MotionSamples > 0,
            FlipPct = telemetry.FlipPct,
            HasFlip = telemetry.SteerPairs > 0,
            FixedHz = session.Config.FixedHz,
            SampleMs = sampleMs,
            History = history,
            NavMeshMs = hasPath ? stats.NavMeshMs : double.NaN,
            HpaMs = hasPath ? stats.HpaMs : double.NaN,
            FlowMs = hasPath ? stats.FlowMs : double.NaN,
            PlanMs = CrowdSimulationTelemetry.TicksToMs(phases.Plan),
            NavBakeMs = session.NavTileCache?.BakeMs ?? double.NaN,
            RebakeMs = rebake,
            Idle = telemetry.Idle,
            Moving = telemetry.Moving,
            Arrived = telemetry.Arrived,
            Unreachable = telemetry.Unreachable,
            Jumping = telemetry.Jumping,
            ViewWidth = viewWidth,
            ViewHeight = viewHeight,
            NavCellCm = session.Config.NavCellSizeCm,
            TileCount = tiles,
            WorldBakeMs = baked ? runtime!.WorldBakeMs : double.NaN,
            WorldCacheHits = baked ? runtime!.WorldBakeCacheHits : 0,
            WorldCacheMisses = baked ? runtime!.WorldBakeCacheMisses : 0,
            WorldBakeRecorded = baked,
        };
    }

    public readonly void Write(string[] lines)
    {
        string resolution = ViewWidth > 0 && ViewHeight > 0 ? $"{ViewWidth}x{ViewHeight}" : "N/A";
        string heap = double.IsNaN(HeapBytesPerSec)
            ? "N/A"
            : (HeapBytesPerSec / (1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture) + " MB/s";
        string world = WorldBakeRecorded
            ? $"{CrowdDebugText.Ms(WorldBakeMs)} ms 缓存 {WorldCacheHits}/{WorldCacheHits + WorldCacheMisses}"
            : "N/A";
        lines[0] = $"遥测 采样 {SampleMs} ms 曲线 {History} 固定 {FixedHz} Hz";
        lines[1] = $"FPS {CrowdDebugText.Ms(Fps)}";
        lines[2] = $"Sim {CrowdDebugText.Ms(SimMs)} ms";
        lines[3] = $"避让 {CrowdDebugText.Ms(AvoidMs)} ms";
        lines[4] = $"空间哈希 {CrowdDebugText.Ms(HashMs)} ms";
        lines[5] = $"渲染 {CrowdDebugText.Ms(RenderMs)} ms";
        lines[6] = $"堆增长 {heap}";
        lines[7] = HasHeading ? $"航向 {CrowdDebugText.Ms(HeadingDeg)}°" : "航向 N/A";
        lines[8] = HasFlip ? $"反复 {FlipPct}%" : "反复 N/A";
        lines[9] = $"NavMesh {CrowdDebugText.Ms(NavMeshMs)} ms  HPA* {CrowdDebugText.Ms(HpaMs)} ms  流场 {CrowdDebugText.Ms(FlowMs)} ms";
        lines[10] = "走廊扩展 N/A（端口未做走廊扩展）";
        lines[11] = $"规划 {CrowdDebugText.Ms(PlanMs)} ms  烘焙 {CrowdDebugText.Ms(NavBakeMs)} ms  重烘 {CrowdDebugText.Ms(RebakeMs)} ms";
        lines[12] = $"状态 闲 {Idle} 行 {Moving} 达 {Arrived} 不可达 {Unreachable} 跳跃 {Jumping}";
        lines[13] = $"分辨率 {resolution}  nav格 {NavCellCm} cm  tile {TileCount}";
        lines[14] = $"世界生成 {world}";
        lines[15] = "烘焙超时 N/A  仿真Worker/宿主RPC N/A（同进程，烘焙在激活线程）";
    }
}

internal readonly struct CrowdTelemetryAlertState
{
    public bool Fps { get; init; }
    public bool Sim { get; init; }
    public bool Avoid { get; init; }
    public bool Heading { get; init; }
    public bool Flip { get; init; }

    public static CrowdTelemetryAlertState Evaluate(in CrowdTelemetryReadout reading, CrowdDebugViewAlerts alerts)
    {
        return new CrowdTelemetryAlertState
        {
            Fps = !double.IsNaN(reading.Fps) && reading.Fps < alerts.LowFps,
            Sim = !double.IsNaN(reading.SimMs) && reading.SimMs > alerts.SimMs,
            Avoid = !double.IsNaN(reading.AvoidMs) && reading.AvoidMs > alerts.AvoidMs,
            Heading = reading.HasHeading && reading.HeadingDeg > alerts.HeadingDeg,
            Flip = reading.HasFlip && reading.FlipPct > alerts.FlipPct,
        };
    }

    public readonly bool Same(in CrowdTelemetryAlertState o) =>
        Fps == o.Fps && Sim == o.Sim && Avoid == o.Avoid && Heading == o.Heading && Flip == o.Flip;

    public readonly bool At(int line) => line switch
    {
        CrowdTelemetryReadout.FpsLine => Fps,
        CrowdTelemetryReadout.SimLine => Sim,
        CrowdTelemetryReadout.AvoidLine => Avoid,
        CrowdTelemetryReadout.HeadingLine => Heading,
        CrowdTelemetryReadout.FlipLine => Flip,
        _ => false,
    };
}

internal static class CrowdDebugText
{
    public static int Tenth(double value) => double.IsNaN(value) ? int.MinValue : (int)Math.Round(value * 10d);

    public static int Whole(double value) => double.IsNaN(value) ? int.MinValue : (int)Math.Round(value);

    public static string Ms(double value) =>
        double.IsNaN(value) ? "N/A" : value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string HitRate(int hits, int misses)
    {
        int denom = hits + misses;
        if (denom <= 0) return "N/A";
        return (hits * CrowdSimulationLimits.FullPercent / denom).ToString(CultureInfo.InvariantCulture) + "%";
    }

    public static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "路径服务故障(原因未记录)";
        int split = message.IndexOf('\n');
        return split < 0 ? message : message.Substring(0, split);
    }
}

/// <summary>
/// 导航面板、遥测面板和路径故障浮层。字符串只在量化值变化时重建。
/// 堆读本进程：仿真与呈现同进程，没有单独的仿真 Worker 堆接口。读不到就显示 N/A。
/// </summary>
internal sealed class CrowdDebugPanels
{
    private readonly CrowdSimulationDebugViewConfig _config;
    private readonly string[] _navLines = new string[CrowdNavReadout.Lines];
    private readonly string[] _telLines = new string[CrowdTelemetryReadout.Lines];
    private readonly double[] _fps;
    private readonly double[] _sim;
    private readonly double[] _avoid;
    private readonly double[] _heading;
    private readonly double[] _flip;
    private int _histCount;
    private int _histHead;
    private readonly System.Diagnostics.Stopwatch _sample = new();
    private bool _sampled;
    private bool _heapSeen;
    private long _heapBytes;
    private long _heapAtMs;
    private double _heapPerSec = double.NaN;
    private CrowdNavReadout _navSnap;
    private bool _navReady;
    private CrowdTelemetryReadout _telSnap;
    private CrowdTelemetryAlertState _alerts;
    private bool _telReady;
    private string _fault = "";
    private bool _faultKnown;

    public CrowdDebugPanels(CrowdSimulationDebugViewConfig config)
    {
        _config = config;
        int n = config.Telemetry.History;
        _fps = new double[n];
        _sim = new double[n];
        _avoid = new double[n];
        _heading = new double[n];
        _flip = new double[n];
    }

    public void Draw(
        CrowdSimSession session,
        CrowdSimulationRuntime? runtime,
        PresentationTimingDiagnostics? timing,
        int viewWidth,
        int viewHeight,
        bool navOpen,
        bool telemetryOpen,
        ScreenOverlayBuffer overlay)
    {
        var service = session.Planner?.Service;
        if (service is { Faulted: true })
        {
            string line = CrowdDebugText.FirstLine(service.FaultMessage);
            string timeout = CrowdDebugText.Ms(service.PlanTimeout.TotalMilliseconds);
            string text = $"{line}  超时 {timeout} ms  仿真停摆，不在主线程补算";
            if (!_faultKnown || !string.Equals(_fault, text, StringComparison.Ordinal))
            {
                _fault = text;
                _faultKnown = true;
            }

            var panels = _config.Panels;
            var alert = _config.Colors.Alert.ToVector4();
            overlay.AddRect(panels.FaultOriginXPx, panels.FaultOriginYPx, panels.WidthPx, panels.LinePx + 8, _config.Colors.Ink.ToVector4(), alert);
            overlay.AddText(panels.FaultOriginXPx + 8, panels.FaultOriginYPx + 4, _fault, _config.Panel.FontSize, alert);
        }

        if (navOpen)
        {
            var nav = CrowdNavReadout.From(session);
            if (!_navReady || !_navSnap.Same(in nav))
            {
                nav.Write(_navLines);
                _navSnap = nav;
                _navReady = true;
            }

            Paint(_config.Panels.NavOriginXPx, _config.Panels.NavOriginYPx, _navLines, null, overlay);
        }

        if (!telemetryOpen) return;
        if (!_sample.IsRunning) _sample.Start();
        bool due = !_sampled || _sample.ElapsedMilliseconds >= _config.Telemetry.SampleMs;
        if (due)
        {
            _sample.Restart();
            _sampled = true;
            NoteHeap();
        }

        var reading = CrowdTelemetryReadout.From(
            session, runtime, timing, viewWidth, viewHeight, _heapPerSec,
            _config.Telemetry.SampleMs, _config.Telemetry.History);
        var alerts = CrowdTelemetryAlertState.Evaluate(in reading, _config.Telemetry.Alerts);
        if (due)
        {
            Push(_fps, Series(reading.Fps));
            Push(_sim, Series(reading.SimMs));
            Push(_avoid, Series(reading.AvoidMs));
            Push(_heading, reading.HasHeading ? reading.HeadingDeg : 0);
            Push(_flip, reading.HasFlip ? reading.FlipPct : 0);
            _histHead = (_histHead + 1) % _fps.Length;
            if (_histCount < _fps.Length) _histCount++;
        }

        if (!_telReady || !_telSnap.Same(in reading) || !_alerts.Same(in alerts))
        {
            reading.Write(_telLines);
            for (int i = 0; i < _telLines.Length; i++)
            {
                if (alerts.At(i)) _telLines[i] = "告警 " + _telLines[i];
            }

            _telSnap = reading;
            _alerts = alerts;
            _telReady = true;
        }

        Paint(_config.Panels.TelemetryOriginXPx, _config.Panels.TelemetryOriginYPx, _telLines, alerts, overlay);
    }

    private void NoteHeap()
    {
        long now = Environment.TickCount64;
        if (!TryProcessHeap(out long bytes))
        {
            _heapPerSec = double.NaN;
            return;
        }

        if (!_heapSeen)
        {
            _heapSeen = true;
            _heapBytes = bytes;
            _heapAtMs = now;
            _heapPerSec = double.NaN;
            return;
        }

        long dt = now - _heapAtMs;
        _heapPerSec = dt > 0 ? (bytes - _heapBytes) * 1000d / dt : double.NaN;
        _heapBytes = bytes;
        _heapAtMs = now;
    }

    private static bool TryProcessHeap(out long bytes)
    {
        try
        {
            bytes = GC.GetTotalMemory(false);
            return bytes >= 0;
        }
        catch (Exception)
        {
            bytes = 0;
            return false;
        }
    }

    private static double Series(double value) => double.IsNaN(value) ? 0 : value;

    private void Push(double[] ring, double value) => ring[_histHead] = value;

    private void Paint(int x, int y, string[] lines, CrowdTelemetryAlertState? alerts, ScreenOverlayBuffer overlay)
    {
        var panels = _config.Panels;
        int height = lines.Length * panels.LinePx + 8;
        overlay.AddRect(x, y, panels.WidthPx, height, _config.Colors.Ink.ToVector4(), _config.Colors.Paper.ToVector4());
        var text = _config.Colors.PanelText.ToVector4();
        var alert = _config.Colors.Alert.ToVector4();
        int font = _config.Panel.FontSize;
        for (int i = 0; i < lines.Length; i++)
        {
            bool hot = alerts?.At(i) == true;
            overlay.AddText(x + 8, y + 4 + i * panels.LinePx, lines[i], font, hot ? alert : text);
            if (!hot && alerts == null) continue;
            if (i == CrowdTelemetryReadout.FpsLine) Spark(_fps, x, y, i, hot ? alert : text, overlay);
            else if (i == CrowdTelemetryReadout.SimLine) Spark(_sim, x, y, i, hot ? alert : text, overlay);
            else if (i == CrowdTelemetryReadout.AvoidLine) Spark(_avoid, x, y, i, hot ? alert : text, overlay);
            else if (i == CrowdTelemetryReadout.HeadingLine) Spark(_heading, x, y, i, hot ? alert : text, overlay);
            else if (i == CrowdTelemetryReadout.FlipLine) Spark(_flip, x, y, i, hot ? alert : text, overlay);
        }
    }

    private void Spark(double[] ring, int panelX, int panelY, int line, Vector4 color, ScreenOverlayBuffer overlay)
    {
        if (_histCount < 2) return;
        var panels = _config.Panels;
        int width = panels.SparkWidthPx;
        int height = panels.SparkHeightPx;
        int x = panelX + panels.WidthPx - width - 8;
        int y = panelY + 4 + line * panels.LinePx;
        double min = double.MaxValue;
        double max = double.MinValue;
        for (int age = 0; age < _histCount; age++)
        {
            double v = ring[Index(age)];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        double span = max - min;
        int x0 = x;
        int y0 = YAt(ring[Index(0)], min, span, y, height);
        for (int age = 1; age < _histCount; age++)
        {
            int x1 = x + age * (width - 1) / (_histCount - 1);
            int y1 = YAt(ring[Index(age)], min, span, y, height);
            overlay.AddLine(x0, y0, x1, y1, 1, color);
            x0 = x1;
            y0 = y1;
        }
    }

    private int Index(int age)
    {
        int start = _histCount < _fps.Length ? 0 : _histHead;
        return (start + age) % _fps.Length;
    }

    private static int YAt(double value, double min, double span, int top, int height)
    {
        double t = span <= 0 ? 0.5 : (value - min) / span;
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return top + height - 1 - (int)Math.Round(t * (height - 1));
    }
}
