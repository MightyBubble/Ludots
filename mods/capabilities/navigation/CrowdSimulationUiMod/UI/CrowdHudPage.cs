using System.Diagnostics;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// hud.html 的 code-behind。每帧只比较时间戳和开关；面板可见且到点才读 <see cref="CrowdHudFeed"/>，
/// 读数格式化进复用缓冲再写进文字绑定（内容不变不写）。改 class、重生成名单这类结构变化只记一个标记，
/// 由控制器在帧末统一让租约重建。点控件注入的是演示已有的按键动作。
/// </summary>
internal sealed partial class CrowdHudPage
{
    public const string RootId = "crowd-hud";
    public const int MaxPlayers = 8;
    public const int WideViewportPx = 1700;
    private const int MaxUnitTypes = 32;
    private const double LingerMs = 4000d;
    private const double BurstMs = 600d;

    private static readonly long LingerTicks = (long)(LingerMs * Stopwatch.Frequency / 1000d);
    private static readonly long BurstTicks = (long)(BurstMs * Stopwatch.Frequency / 1000d);

    private readonly UiDocument _document;
    private readonly CrowdHudFeed _feed;
    private readonly CrowdHudSettings _settings;
    private readonly CrowdHudActionSink _sink;
    private readonly CrowdHudStepper _stepper;
    private readonly string?[] _swatches;
    private readonly UiTextBuffer W = new(512);

    private readonly long _statusPeriod;
    private readonly long _playerPeriod;
    private readonly long _inspectPeriod;
    private readonly long _navPeriod;
    private readonly long _telemetryPeriod;
    private long _statusDue;
    private long _playerDue;
    private long _inspectDue;
    private long _faultDue;
    private long _navDue;
    private long _telemetryDue;
    private long _burstUntil;
    private long _now;

    private bool _structureDirty;
    private DvTab _dvTab = DvTab.Layers;
    private DockTab _dockTab = DockTab.Deploy;
    private bool _dockCollapsed;
    private bool _navPanelSeen;
    private bool _telemetrySeen;

    public CrowdHudPage(string html, string css, CrowdHudFeed feed, CrowdHudSettings settings, CrowdHudActionSink sink, string?[] swatches)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _swatches = swatches ?? throw new ArgumentNullException(nameof(swatches));
        _stepper = new CrowdHudStepper(sink);
        _statusPeriod = Ticks(settings.SampleMs.Status);
        _playerPeriod = Ticks(settings.SampleMs.Player);
        _inspectPeriod = Ticks(settings.SampleMs.Inspect);
        _navPeriod = Ticks(settings.SampleMs.Nav);
        _telemetryPeriod = Ticks(settings.SampleMs.Telemetry);
        InitBindings(settings.History);
        InitObservers();

        _document = new UiMarkupLoader().LoadDocument(html, css);
        MarkupBinder.Bind(_document, this);
        InitSwitches();
        InitStatic();
        ApplyKind();
        ApplyTabs();
    }

    public UiDocument Document => _document;

    public UiElement Root => _document.Root;

    /// <summary>上一帧以来有没有改过 class 或名单；读一次就清掉。</summary>
    public bool TakeStructureDirty()
    {
        bool dirty = _structureDirty;
        _structureDirty = false;
        return dirty;
    }

    /// <param name="now"><see cref="Stopwatch.GetTimestamp"/> 刻度。</param>
    public void Frame(long now, PresentationTimingDiagnostics? timing, float viewportWidth, double composeMs)
    {
        _now = now;
        _sink.Update();
        bool burst = _stepper.Active || now < _burstUntil;
        Mark(_wide.Set(viewportWidth >= WideViewportPx));
        TrackPanelKeys();

        if (_feed.Kind == CrowdHudKind.Path)
        {
            if (Due(ref _statusDue, _statusPeriod, burst)) SamplePath();
            return;
        }

        if (Due(ref _statusDue, _statusPeriod, burst)) SampleDeploy();
        if (_feed.HasPlayerView && Due(ref _playerDue, _playerPeriod, burst)) SamplePlayer();
        if (_feed.HasDebugView)
        {
            if (Due(ref _faultDue, _inspectPeriod, burst)) SampleFault();
            if ((_dvTab == DvTab.Inspect || _dvTab == DvTab.Overlays) && Due(ref _inspectDue, _inspectPeriod, burst)) SampleInspect();
            if (_dvTab == DvTab.Cache && Due(ref _navDue, _navPeriod, burst)) SampleNav();
        }

        if (_telemetrySeen)
        {
            if (timing != null && timing.LastUiRenderMs > _uiRedrawPeakMs) _uiRedrawPeakMs = timing.LastUiRenderMs;
            if (Due(ref _telemetryDue, _telemetryPeriod, burst)) SampleTelemetry(timing, composeMs);
        }

        _stepper.Step(now);
    }

    private bool Due(ref long due, long period, bool burst)
    {
        if (!burst && _now < due) return false;
        due = _now + period;
        return true;
    }

    /// <summary>
    /// F9 / F10 仍按键位开关：导航面板键把调试视图切到“缓存”页（再按切回“图层”），遥测键开关右栏。
    /// 只在开关翻转的那一帧改页面。
    /// </summary>
    private void TrackPanelKeys()
    {
        bool nav = _feed.NavPanelOpen;
        if (nav != _navPanelSeen)
        {
            _navPanelSeen = nav;
            if (nav) _dvTab = DvTab.Cache;
            else if (_dvTab == DvTab.Cache) _dvTab = DvTab.Layers;
            ApplyTabs();
        }

        bool telemetry = _feed.TelemetryPanelOpen;
        if (telemetry != _telemetrySeen)
        {
            _telemetrySeen = telemetry;
            ResetTelemetry();
            _telemetryDue = _now;
            Mark(_telemetry.Show(telemetry));
        }
    }

    private void ApplyKind()
    {
        bool path = _feed.Kind == CrowdHudKind.Path;
        bool player = !path && _feed.HasPlayerView;
        bool debug = !path && _feed.HasDebugView;
        Mark(_pathSection.Show(path));
        Mark(_prGroup.Show(player));
        Mark(_playerbar.Show(player));
        Mark(_noticeRow.Show(player));
        Mark(_dv.Show(debug));
        Mark(_dock.Show(!path));
        Mark(_hint.Show(!path));
        Mark(_deployParams.Show(player));
        Mark(_spawnSection.Show(player));
        Mark(_telemetry.Show(false));
    }

    private void ApplyTabs()
    {
        for (int i = 0; i < _dvTabs.Length; i++)
        {
            Mark(_dvTabs[i].Set(i == (int)_dvTab));
            Mark(_dvPages[i].Show(i == (int)_dvTab));
        }

        for (int i = 0; i < _dockTabs.Length; i++)
        {
            Mark(_dockTabs[i].Set(!_dockCollapsed && i == (int)_dockTab));
            Mark(_dockPages[i].Show(i == (int)_dockTab));
        }

        Mark(_dockBody.Show(!_dockCollapsed));
        Mark(_dockDown.Show(!_dockCollapsed));
        Mark(_dockUp.Show(_dockCollapsed));
    }

    private void Mark(bool changed) => _structureDirty |= changed;

    /// <summary>点过控件后的一小段时间里各可见面板每帧都读，按下的效果下一帧就能看到。</summary>
    private void Burst() => _burstUntil = _now + BurstTicks;

    private static long Ticks(int ms) => ms * Stopwatch.Frequency / 1000;

    private enum DvTab
    {
        Player,
        Layers,
        Inspect,
        Overlays,
        Cache,
    }

    private enum DockTab
    {
        Deploy,
        Tools,
        Tests,
        Prefs,
    }
}
