using System.Diagnostics;
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
/// 读数变了才排版。排版写进绑定，只重量变了尺寸的文字叶子；开关、段数、配色类变了才重组页面。
/// 关着的面板既不读也不排版。
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
    internal const string SparkBlocks = "▁▂▃▄▅▆▇█";
    private readonly double[][] _rings;
    private readonly char[] _sparkChars;

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
    private bool _reshape;

    public CrowdHudController(CrowdHudSettings settings, UiStyleSheet styleSheet)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _styleSheet = styleSheet ?? throw new ArgumentNullException(nameof(styleSheet));
        _state = new CrowdHudState(MaxPlayers, settings.History);
        _sparkChars = new char[settings.History];
        _statusPeriod = Ticks(settings.SampleMs.Status);
        _playerPeriod = Ticks(settings.SampleMs.Player);
        _inspectPeriod = Ticks(settings.SampleMs.Inspect);
        _navPeriod = Ticks(settings.SampleMs.Nav);
        _telemetryPeriod = Ticks(settings.SampleMs.Telemetry);
        _rings = new double[CrowdHudState.SparkSeries][];
        for (int i = 0; i < _rings.Length; i++) _rings[i] = new double[settings.History];
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
        _reshape = false;
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
            _reshape = true;
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
            _reshape = true;
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
        if (_reshape)
        {
            _page!.Mutate(Touch);
            ComposeCount++;
        }

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
            context => CrowdHudView.Build(context),
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
        _state.Tools.Set("");
        _state.Tools.Commit();
        _state.Keys.Set("");
        _state.Keys.Commit();
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
        SetFlag(ref _state.ShowStatus, false);
        CrowdHudFormat.PathDetail(_state.Tools.Buffer, in next);
        Commit(_state.Tools);
        CrowdHudFormat.PathKeys(_state.Keys.Buffer, feed.Text, in next);
        Commit(_state.Keys);
        return true;
    }

    private bool SampleDeploy(CrowdHudFeed feed)
    {
        var next = _deploy;
        if (!feed.ReadDeploy(ref next)) return false;
        if (_deployReady && next == _deploy) return false;
        _deploy = next;
        _deployReady = true;
        SetFlag(ref _state.ShowStatus, true);
        CrowdHudFormat.Tools(_state.Tools.Buffer, in next);
        Commit(_state.Tools);
        CrowdHudFormat.Status(_state.Status.Buffer, in next);
        Commit(_state.Status);
        CrowdHudFormat.Rebake(_state.Rebake.Buffer, in next);
        Commit(_state.Rebake);
        CrowdHudFormat.Replay(_state.Replay.Buffer, in next);
        Commit(_state.Replay);
        SetFlag(ref _state.ReplayWarn, next.Interactive);
        CrowdHudFormat.Counters(_state.Counters.Buffer, in next);
        Commit(_state.Counters);
        CrowdHudFormat.CounterDetail(_state.CounterDetail.Buffer, in next);
        Commit(_state.CounterDetail);
        CrowdHudFormat.Validation(_state.Validation.Buffer, in next);
        Commit(_state.Validation);
        SetTone(ref _state.ValidationTone, CrowdHudFormat.Tone(next.RuntimePresent && next.ValidationRan, next.RuntimePresent && next.ValidationFailed));
        CrowdHudFormat.Fuzz(_state.Fuzz.Buffer, in next);
        Commit(_state.Fuzz);
        SetTone(ref _state.FuzzTone, CrowdHudFormat.Tone(next.RuntimePresent && next.FuzzRan, next.RuntimePresent && next.FuzzFailed));
        CrowdHudFormat.DeployKeys(_state.Keys.Buffer, feed.Text, in next);
        Commit(_state.Keys);
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
        SetFlag(ref _state.ShowPlayer, true);
        if (_state.PlayerCount != next.PlayerCount)
        {
            _state.PlayerCount = next.PlayerCount;
            _reshape = true;
        }

        CrowdHudFormat.TeamLine(_state.TeamLine.Buffer, in next, _unitsShown, labels);
        Commit(_state.TeamLine);
        CrowdHudFormat.CardLine(_state.CardLine.Buffer, in next, labels);
        Commit(_state.CardLine);
        CrowdHudFormat.ModeLine(_state.ModeLine.Buffer, in next, labels);
        Commit(_state.ModeLine);
        CrowdHudFormat.ClickLine(_state.ClickLine.Buffer, in next, labels);
        Commit(_state.ClickLine);
        CrowdHudFormat.DeployLine(_state.DeployLine.Buffer, in next, labels);
        Commit(_state.DeployLine);
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
            SetFlag(ref _state.ShowInspect, next.Visible);
            CrowdHudFormat.Inspect(_state.Inspect.Buffer, in next);
            Commit(_state.Inspect);
            CrowdHudFormat.Belief(_state.Belief.Buffer, in next);
            Commit(_state.Belief);
            changed = true;
        }

        bool faulted = feed.ReadPathFault(out string line, out double timeoutMs);
        if (faulted != _state.ShowFault
            || (faulted && (!string.Equals(line, _faultLine, StringComparison.Ordinal) || !timeoutMs.Equals(_faultTimeoutMs))))
        {
            SetFlag(ref _state.ShowFault, faulted);
            _faultLine = line;
            _faultTimeoutMs = timeoutMs;
            if (faulted) CrowdHudFormat.Fault(_state.Fault.Buffer, line, timeoutMs);
            else _state.Fault.Buffer.Clear();
            Commit(_state.Fault);
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
        for (int i = 0; i < _state.NavLines.Length; i++) Commit(_state.NavLines[i]);
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
        for (int i = 0; i < _state.TelemetryLines.Length; i++) Commit(_state.TelemetryLines[i]);
        CrowdHudFormat.MarkAlerts(in r, fps, _settings.Alerts, _state.TelemetryHot);
        WriteAlerts();
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
            for (int age = 0; age < _histCount; age++)
            {
                double t = Math.Clamp((ring[(start + age) % n] - min) / span, 0d, 1d);
                _sparkChars[age] = SparkBlocks[(int)Math.Round(t * (SparkBlocks.Length - 1))];
            }

            _state.SparkText[s].Set(_sparkChars.AsSpan(0, _histCount));
        }

        if ((_state.SparkCount >= 2) != (_histCount >= 2)) _reshape = true;
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
        WriteAlerts();
    }

    private void WriteAlerts()
    {
        for (int i = 0; i < _state.TelemetryAlert.Length; i++)
            _state.TelemetryAlert[i].Set(_state.TelemetryHot[i] ? CrowdHudFormat.AlertLabel : "");
    }

    private void Commit(HudLine line) => _reshape |= line.Commit();

    private void SetFlag(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        _reshape = true;
    }

    /// <summary>配色类只取 <see cref="CrowdHudFormat.Tone"/> 的字面量，按引用比较即可。</summary>
    private void SetTone(ref string field, string value)
    {
        if (ReferenceEquals(field, value)) return;
        field = value;
        _reshape = true;
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

    private static UiColor Rgba(Vector4 c) => new(Channel(c.X), Channel(c.Y), Channel(c.Z), Channel(c.W));

    private static byte Channel(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
