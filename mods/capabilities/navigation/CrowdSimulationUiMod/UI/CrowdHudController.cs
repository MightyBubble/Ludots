using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 群体 HUD 控制器。每帧只做时间戳比较和开关比较；面板到点才读 <see cref="CrowdHudFeed"/>，
/// 读数变了才排版并重组一次页面。关着的面板既不读也不排版。
/// </summary>
internal sealed class CrowdHudController
{
    public const string OwnerId = "CrowdSimulation.Hud";
    private const int MaxPlayers = 8;
    private static readonly Action<CrowdHudState> Touch = static _ => { };

    private readonly CrowdHudSettings _settings;
    private readonly UiStyleSheet _styleSheet;
    private readonly CrowdHudState _state;
    private readonly long _statusPeriod;
    private readonly long _playerPeriod;
    private readonly long _inspectPeriod;
    private readonly long _navPeriod;
    private readonly long _telemetryPeriod;
    private readonly int[] _units = new int[MaxPlayers + 1];
    private readonly int[] _unitsShown = new int[MaxPlayers + 1];
    private readonly double[][] _rings;
    private readonly CrowdSparkCanvas[] _sparks;

    private ReactivePage<CrowdHudState>? _page;
    private IUiSurfaceHost? _host;
    private UiSurfaceLeaseHandle _lease;
    private CrowdHudFeed? _feed;

    private long _statusDue;
    private long _playerDue;
    private long _inspectDue;
    private long _navDue;
    private long _telemetryDue;

    private CrowdPathHud _path;
    private CrowdDeployHud _deploy;
    private CrowdPlayerHud _player;
    private CrowdInspectReadout _inspect;
    private CrowdNavReadout _nav;
    private bool _pathReady;
    private bool _deployReady;
    private bool _playerReady;
    private bool _inspectReady;
    private bool _navReady;
    private string _faultLine = "";
    private double _faultTimeoutMs = double.NaN;

    private int _histCount;
    private int _histHead;
    private bool _heapSeen;
    private long _heapBytes;
    private long _heapAt;
    private float _uiRedrawPeakMs;
    private double _composeMs = double.NaN;

    public CrowdHudController(CrowdHudSettings settings, UiStyleSheet styleSheet)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _styleSheet = styleSheet ?? throw new ArgumentNullException(nameof(styleSheet));
        _state = new CrowdHudState(settings.History, MaxPlayers);
        _statusPeriod = Ticks(settings.SampleMs.Status);
        _playerPeriod = Ticks(settings.SampleMs.Player);
        _inspectPeriod = Ticks(settings.SampleMs.Inspect);
        _navPeriod = Ticks(settings.SampleMs.Nav);
        _telemetryPeriod = Ticks(settings.SampleMs.Telemetry);
        _rings = new double[CrowdHudState.SparkSeries][];
        for (int i = 0; i < _rings.Length; i++) _rings[i] = new double[settings.History];
        _sparks = new CrowdSparkCanvas[CrowdHudState.SparkSeries];
        for (int line = 0; line < CrowdHudState.TelemetryLineCount; line++)
        {
            int series = CrowdHudState.SparkSeriesOf(line);
            if (series >= 0) _sparks[series] = new CrowdSparkCanvas(_state, series, line, settings.SparkColor, settings.SparkHotColor);
        }
    }

    public bool Mounted => _lease.IsValid;
    public int ComposeCount { get; private set; }
    public double LastComposeMs => _composeMs;
    internal CrowdHudState State => _state;
    internal ReactivePage<CrowdHudState>? Page => _page;

    public void Tick(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        CrowdHudFeed? feed = engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out var runtime) ? runtime?.HudFeed : null;
        Tick(engine, feed, Stopwatch.GetTimestamp());
    }

    /// <param name="now"><see cref="Stopwatch.GetTimestamp"/> 刻度。</param>
    internal void Tick(GameEngine engine, CrowdHudFeed? feed, long now)
    {
        long started = Stopwatch.GetTimestamp();
        if (!ReferenceEquals(feed, _feed))
        {
            Unmount();
            _feed = feed;
            ResetForFeed(engine, feed);
        }

        if (feed == null) return;
        bool mounting = _page == null;
        if (mounting && !TryResolveHost(engine)) return;

        bool changed = false;
        if (now >= _statusDue)
        {
            _statusDue = now + _statusPeriod;
            changed |= feed.Kind == CrowdHudKind.Path ? SamplePath(feed) : SampleDeploy(feed);
        }

        if (feed.HasPlayerView && now >= _playerDue)
        {
            _playerDue = now + _playerPeriod;
            changed |= SamplePlayer(feed);
        }

        if (feed.HasDebugView && now >= _inspectDue)
        {
            _inspectDue = now + _inspectPeriod;
            changed |= SampleInspect(feed);
        }

        bool navOpen = feed.NavPanelOpen;
        if (navOpen != _state.ShowNav)
        {
            _state.ShowNav = navOpen;
            _navReady = false;
            _navDue = now;
            changed = true;
        }

        if (navOpen && now >= _navDue)
        {
            _navDue = now + _navPeriod;
            changed |= SampleNav(feed);
        }

        bool telemetryOpen = feed.TelemetryPanelOpen;
        if (telemetryOpen != _state.ShowTelemetry)
        {
            _state.ShowTelemetry = telemetryOpen;
            ResetTelemetry();
            _telemetryDue = now;
            changed = true;
        }

        if (telemetryOpen)
        {
            engine.TryGetService(CoreServiceKeys.PresentationTimingDiagnostics, out PresentationTimingDiagnostics? timing);
            if (timing != null && timing.LastUiRenderMs > _uiRedrawPeakMs) _uiRedrawPeakMs = timing.LastUiRenderMs;
            if (now >= _telemetryDue)
            {
                _telemetryDue = now + _telemetryPeriod;
                changed |= SampleTelemetry(engine, feed, timing);
            }
        }

        if (mounting)
        {
            Mount(engine);
            _composeMs = Elapsed(started);
            return;
        }

        if (!changed) return;
        _page!.Mutate(Touch);
        ComposeCount++;
        _composeMs = Elapsed(started);
    }

    public void Unmount()
    {
        if (_host != null && _lease.IsValid) _host.ReleaseLease(ref _lease);
        _lease = default;
        _page = null;
        _host = null;
    }

    private bool TryResolveHost(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.UiSurfaceHost, out object? host) || host is not IUiSurfaceHost surface) return false;
        if (!engine.TryGetService(CoreServiceKeys.UiTextMeasurer, out object? tm) || tm is not IUiTextMeasurer) return false;
        if (!engine.TryGetService(CoreServiceKeys.UiImageSizeProvider, out object? isp) || isp is not IUiImageSizeProvider) return false;
        _host = surface;
        return true;
    }

    private void Mount(GameEngine engine)
    {
        var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
        var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
        _page = new ReactivePage<CrowdHudState>(
            textMeasurer,
            imageSizeProvider,
            _state,
            context => CrowdHudView.Build(context, _sparks),
            null,
            _styleSheet);
        _host!.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest(OwnerId, UiSurfaceSegment.Overlay, priority: 30), _page);
        ComposeCount++;
    }

    private void ResetForFeed(GameEngine engine, CrowdHudFeed? feed)
    {
        _statusDue = _playerDue = _inspectDue = _navDue = _telemetryDue = 0;
        _pathReady = _deployReady = _playerReady = _inspectReady = _navReady = false;
        _faultLine = "";
        _faultTimeoutMs = double.NaN;
        _state.ShowStatus = _state.ShowPlayer = _state.ShowInspect = _state.ShowNav = _state.ShowTelemetry = _state.ShowFault = false;
        _state.Tools = _state.Keys = "";
        ResetTelemetry();
        if (feed == null) return;
        _state.Kind = feed.Kind;
        _state.Title = feed.Text.Title;
        CacheSwatches(engine);
    }

    private void CacheSwatches(GameEngine engine)
    {
        Array.Clear(_state.Swatches);
        if (!engine.TryGetService(CoreServiceKeys.TeamColorPalette, out TeamColorPalette? palette) || palette == null) return;
        for (int p = 1; p < _state.Swatches.Length; p++)
        {
            if (palette.TryGet(p, out Vector4 c)) _state.Swatches[p] = Rgba(c);
        }
    }

    private bool SamplePath(CrowdHudFeed feed)
    {
        var next = _path;
        if (!feed.ReadPath(ref next)) return false;
        if (_pathReady && next == _path) return false;
        _path = next;
        _pathReady = true;
        _state.ShowStatus = false;
        _state.Tools = CrowdHudFormat.PathDetail(in next);
        _state.Keys = CrowdHudFormat.PathKeys(feed.Text, in next);
        return true;
    }

    private bool SampleDeploy(CrowdHudFeed feed)
    {
        var next = _deploy;
        if (!feed.ReadDeploy(ref next)) return false;
        if (_deployReady && next == _deploy) return false;
        _deploy = next;
        _deployReady = true;
        _state.ShowStatus = true;
        _state.Tools = CrowdHudFormat.Tools(in next);
        _state.Status = CrowdHudFormat.Status(in next);
        _state.Rebake = CrowdHudFormat.Rebake(in next);
        _state.Replay = CrowdHudFormat.Replay(in next);
        _state.ReplayWarn = next.Interactive;
        _state.Counters = CrowdHudFormat.Counters(in next);
        _state.CounterDetail = CrowdHudFormat.CounterDetail(in next);
        _state.Validation = CrowdHudFormat.Validation(in next);
        _state.ValidationTone = CrowdHudFormat.Tone(next.RuntimePresent && next.ValidationRan, next.RuntimePresent && next.ValidationFailed);
        _state.Fuzz = CrowdHudFormat.Fuzz(in next);
        _state.FuzzTone = CrowdHudFormat.Tone(next.RuntimePresent && next.FuzzRan, next.RuntimePresent && next.FuzzFailed);
        _state.Keys = CrowdHudFormat.DeployKeys(feed.Text, in next);
        return true;
    }

    private bool SamplePlayer(CrowdHudFeed feed)
    {
        var next = _player;
        if (!feed.ReadPlayer(ref next, _units)) return false;
        if (_playerReady && next == _player && _units.AsSpan().SequenceEqual(_unitsShown)) return false;
        _player = next;
        _units.AsSpan().CopyTo(_unitsShown);
        _playerReady = true;
        CrowdPlayerViewLabels labels = feed.PlayerLabels!;
        _state.ShowPlayer = true;
        _state.PlayerCount = next.PlayerCount;
        _state.TeamLine = CrowdHudFormat.TeamLine(in next, _unitsShown, labels);
        _state.CardLine = CrowdHudFormat.CardLine(in next, labels);
        _state.ModeLine = CrowdHudFormat.ModeLine(in next, labels);
        _state.ClickLine = CrowdHudFormat.ClickLine(in next, labels);
        _state.DeployLine = CrowdHudFormat.DeployLine(in next, labels);
        return true;
    }

    private bool SampleInspect(CrowdHudFeed feed)
    {
        bool changed = false;
        var next = _inspect;
        if (feed.ReadInspect(ref next) && (!_inspectReady || next != _inspect))
        {
            _inspect = next;
            _inspectReady = true;
            _state.ShowInspect = next.Visible;
            _state.Inspect = CrowdHudFormat.Inspect(in next);
            _state.Belief = CrowdHudFormat.Belief(in next);
            changed = true;
        }

        bool faulted = feed.ReadPathFault(out string line, out double timeoutMs);
        if (faulted != _state.ShowFault
            || (faulted && (!string.Equals(line, _faultLine, StringComparison.Ordinal) || !timeoutMs.Equals(_faultTimeoutMs))))
        {
            _state.ShowFault = faulted;
            _faultLine = line;
            _faultTimeoutMs = timeoutMs;
            _state.Fault = faulted ? CrowdHudFormat.Fault(line, timeoutMs) : "";
            changed = true;
        }

        return changed;
    }

    private bool SampleNav(CrowdHudFeed feed)
    {
        var next = _nav;
        if (!feed.ReadNav(ref next)) return false;
        if (_navReady && next == _nav) return false;
        _nav = next;
        _navReady = true;
        CrowdHudFormat.WriteNav(in next, _state.NavLines);
        return true;
    }

    private bool SampleTelemetry(GameEngine engine, CrowdHudFeed feed, PresentationTimingDiagnostics? timing)
    {
        var reading = default(CrowdTelemetryReadout);
        if (!feed.ReadTelemetry(ref reading)) return false;
        double fps = double.NaN;
        if (timing != null)
        {
            float frame = timing.LastWallFrameMs > 0f ? timing.LastWallFrameMs : timing.LastFrameMs;
            if (frame > 0f) fps = 1000d / frame;
        }

        double uiPeak = timing != null ? _uiRedrawPeakMs : double.NaN;
        _uiRedrawPeakMs = 0f;
        double heapPerSec = SampleHeap();
        int width = 0, height = 0;
        if (engine.TryGetService(CoreServiceKeys.ViewController, out IViewController? view) && view != null)
        {
            width = (int)view.Resolution.X;
            height = (int)view.Resolution.Y;
        }

        PushHistory(fps, reading);
        WriteTelemetry(in reading, fps, uiPeak, heapPerSec, width, height);
        return true;
    }

    private void WriteTelemetry(in CrowdTelemetryReadout r, double fps, double uiPeak, double heapPerSec, int width, int height)
    {
        var host = new CrowdHostReadout(fps, uiPeak, _composeMs, heapPerSec, width, height);
        CrowdHudFormat.WriteTelemetry(in r, in host, _settings.SampleMs.Telemetry, _settings.History, _state.TelemetryLines);
        CrowdHudFormat.MarkAlerts(in r, fps, _settings.Alerts, _state.TelemetryHot);
        CrowdHudFormat.PrefixAlerts(_state.TelemetryLines, _state.TelemetryHot);
        WriteSparks();
    }

    private void PushHistory(double fps, in CrowdTelemetryReadout r)
    {
        int n = _settings.History;
        _rings[0][_histHead] = Series(fps);
        _rings[1][_histHead] = Series(r.SimMs);
        _rings[2][_histHead] = Series(r.AvoidMs);
        _rings[3][_histHead] = r.HasHeading ? r.HeadingDeg : 0;
        _rings[4][_histHead] = r.HasFlip ? r.FlipPct : 0;
        _histHead = (_histHead + 1) % n;
        if (_histCount < n) _histCount++;
    }

    private void WriteSparks()
    {
        int n = _settings.History;
        int start = _histCount < n ? 0 : _histHead;
        for (int s = 0; s < _rings.Length; s++)
        {
            double[] ring = _rings[s];
            double min = 0d, max = 1e-6;
            for (int age = 0; age < _histCount; age++)
            {
                double v = ring[(start + age) % n];
                if (v < min) min = v;
                if (v > max) max = v;
            }

            double span = max - min;
            float[] dst = _state.Spark[s];
            for (int age = 0; age < _histCount; age++)
            {
                double v = ring[(start + age) % n];
                double t = (v - min) / span;
                dst[age] = (float)Math.Clamp(t, 0d, 1d);
            }
        }

        _state.SparkCount = _histCount;
    }

    private void ResetTelemetry()
    {
        _histCount = 0;
        _histHead = 0;
        _heapSeen = false;
        _uiRedrawPeakMs = 0f;
        _state.SparkCount = 0;
        Array.Clear(_state.TelemetryHot);
    }

    /// <summary>仿真与呈现同进程，读的是本进程托管堆。</summary>
    private double SampleHeap()
    {
        long now = Environment.TickCount64;
        long bytes = GC.GetTotalMemory(false);
        if (!_heapSeen)
        {
            _heapSeen = true;
            _heapBytes = bytes;
            _heapAt = now;
            return double.NaN;
        }

        long dt = now - _heapAt;
        double perSec = dt > 0 ? (bytes - _heapBytes) * 1000d / dt : double.NaN;
        _heapBytes = bytes;
        _heapAt = now;
        return perSec;
    }

    private static double Series(double value) => double.IsNaN(value) ? 0 : value;

    private static long Ticks(int ms) => ms * Stopwatch.Frequency / 1000;

    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

    private static string Rgba(Vector4 c) => string.Create(
        CultureInfo.InvariantCulture,
        $"rgba({(int)(c.X * 255f)}, {(int)(c.Y * 255f)}, {(int)(c.Z * 255f)}, {c.W:0.###})");
}
