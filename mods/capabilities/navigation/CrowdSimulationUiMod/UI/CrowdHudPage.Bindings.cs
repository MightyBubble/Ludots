using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>hud.html 里 ui-text / ui-canvas 指向的成员，以及 code-behind 要切 class 的节点。容量按最长文案预留，稳态改写不再扩容。</summary>
internal sealed partial class CrowdHudPage
{
    private const int Short = 24;
    private const int Line = 96;
    private const int Long = 320;

    internal readonly UiTextBinding Title = new(Line);
    internal readonly UiTextBinding PathDetail = new(Long);
    internal readonly UiTextBinding PathKeys = new(Long);
    internal readonly UiTextBinding OrderMode = new(Short);
    internal readonly UiTextBinding InspectColor = new(Short);
    internal readonly UiTextBinding InspectTerrain = new(Short);
    internal readonly UiTextBinding InspectBelief = new(Short);
    internal readonly UiTextBinding TileCacheHead = new(Short);
    internal readonly UiTextBinding TileCacheDetail = new(Line);
    internal readonly UiTextBinding FlowCacheHead = new(Short);
    internal readonly UiTextBinding FlowCacheDetail = new(Line);
    internal readonly UiTextBinding NavResident = new(Short);
    internal readonly UiTextBinding NavRequests = new(Short);
    internal readonly UiTextBinding NavRoundTrip = new(Short);
    internal readonly UiTextBinding NavWaits = new(Short);
    internal readonly UiTextBinding NavTick = new(Short);
    internal readonly UiTextBinding Selected = new(Short);
    internal readonly UiTextBinding Hint = new(Long);
    internal readonly UiTextBinding Fault = new(Long);

    internal readonly UiTextBinding DeployCount = new(Short);
    internal readonly UiTextBinding StressLabel = new(Short);
    internal readonly UiTextBinding SpawnHead = new(Line);
    internal readonly UiTextBinding SpawnCount = new(Short);
    internal readonly UiTextBinding SpawnTileLabel = new(Short);
    internal readonly UiTextBinding ToolHint = new(Long);
    internal readonly UiTextBinding RebakeEmpty = new(Line);
    internal readonly UiTextBinding RebakeTiles = new(Line);
    internal readonly UiTextBinding RebakeCost = new(Line);
    internal readonly UiTextBinding RebakeCache = new(Line);
    internal readonly UiTextBinding RebakeOrders = new(Line);
    internal readonly UiTextBinding RebakeStuck = new(Short);
    internal readonly UiTextBinding RebakeMs = new(Short);
    internal readonly UiTextBinding Validation = new(Long);
    internal readonly UiTextBinding FuzzLine = new(Long);
    internal readonly UiTextBinding Replay = new(Long);
    internal readonly UiTextBinding Counters = new(Long);
    internal readonly UiTextBinding CounterDetail = new(Long);
    internal readonly UiTextBinding TickRate = new(Short);
    internal readonly UiTextBinding AvoidRate = new(Short);
    internal readonly UiTextBinding Speed = new(Short);

    internal readonly UiTextBinding Fps = new(Short);
    internal readonly UiTextBinding FpsLow = new(Short);
    internal readonly UiTextBinding FrameMs = new(Short);
    internal readonly UiTextBinding Units = new(Short);
    internal readonly UiTextBinding SimMs = new(Short);
    internal readonly UiTextBinding SimMsAlert = new(Short);
    internal readonly UiTextBinding AvoidMs = new(Short);
    internal readonly UiTextBinding AvoidMsAlert = new(Short);
    internal readonly UiTextBinding HashMs = new(Short);
    internal readonly UiTextBinding RenderMs = new(Short);
    internal readonly UiTextBinding UiMs = new(Short);
    internal readonly UiTextBinding HeapRate = new(Short);
    internal readonly UiTextBinding HeapUnit = new(Short);
    internal readonly UiTextBinding Heading = new(Short);
    internal readonly UiTextBinding HeadingAlert = new(Short);
    internal readonly UiTextBinding Flip = new(Short);
    internal readonly UiTextBinding FlipAlert = new(Short);
    internal readonly UiTextBinding FixedHz = new(Short);
    internal readonly UiTextBinding FixedUnit = new(Short);
    internal readonly UiTextBinding NavMeshMs = new(Short);
    internal readonly UiTextBinding HpaMs = new(Short);
    internal readonly UiTextBinding FlowMs = new(Short);
    internal readonly UiTextBinding Extend = new(Short);
    internal readonly UiTextBinding PlanMs = new(Short);
    internal readonly UiTextBinding NavBakeMs = new(Short);
    internal readonly UiTextBinding RebakeTelMs = new(Short);
    internal readonly UiTextBinding RebakeTelUnit = new(Line);
    internal readonly UiTextBinding AgentsMoving = new(Short);
    internal readonly UiTextBinding AgentsArrived = new(Short);
    internal readonly UiTextBinding AgentsUnreachable = new(Short);
    internal readonly UiTextBinding AgentsIdle = new(Short);
    internal readonly UiTextBinding NavCell = new(Short);
    internal readonly UiTextBinding Clusters = new(Short);
    internal readonly UiTextBinding WorldGen = new(Short);
    internal readonly UiTextBinding WorldGenUnit = new(Short);

    private readonly UiTextBinding[] _noticeTitle = Make(CrowdPlayerHud.MaxNotices, Line);
    private readonly UiTextBinding[] _noticeStatus = Make(CrowdPlayerHud.MaxNotices, Short);
    private readonly UiTextBinding[] _noticeMuted = Make(CrowdPlayerHud.MaxNotices, Short);
    private readonly UiTextBinding[] _noticeBad = Make(CrowdPlayerHud.MaxNotices, Short);
    private readonly UiTextBinding[] _noticeGood = Make(CrowdPlayerHud.MaxNotices, Short);
    private readonly UiTextBinding[] _noticeDetail = Make(CrowdPlayerHud.MaxNotices, Line);
    private readonly UiTextBinding[] _playerCounts = Make(MaxPlayers + 1, Short);
    private readonly UiTextBinding[] _typeCounts = Make(MaxUnitTypes, Short);

    private UiTextBinding Notice0Title => _noticeTitle[0];
    private UiTextBinding Notice0Status => _noticeStatus[0];
    private UiTextBinding Notice0StatusMuted => _noticeMuted[0];
    private UiTextBinding Notice0StatusBad => _noticeBad[0];
    private UiTextBinding Notice0StatusGood => _noticeGood[0];
    private UiTextBinding Notice0Detail => _noticeDetail[0];
    private UiTextBinding Notice1Title => _noticeTitle[1];
    private UiTextBinding Notice1Status => _noticeStatus[1];
    private UiTextBinding Notice1StatusMuted => _noticeMuted[1];
    private UiTextBinding Notice1StatusBad => _noticeBad[1];
    private UiTextBinding Notice1StatusGood => _noticeGood[1];
    private UiTextBinding Notice1Detail => _noticeDetail[1];
    private UiTextBinding Notice2Title => _noticeTitle[2];
    private UiTextBinding Notice2Status => _noticeStatus[2];
    private UiTextBinding Notice2StatusMuted => _noticeMuted[2];
    private UiTextBinding Notice2StatusBad => _noticeBad[2];
    private UiTextBinding Notice2StatusGood => _noticeGood[2];
    private UiTextBinding Notice2Detail => _noticeDetail[2];

    private readonly UiVectorDrawList _tileCacheBar = CrowdHudCanvas.Bar(1);
    private readonly UiVectorDrawList _flowCacheBar = CrowdHudCanvas.Bar(1);
    private readonly UiVectorDrawList _deployCountSlider = CrowdHudCanvas.Slider();
    private readonly UiVectorDrawList _spawnCountSlider = CrowdHudCanvas.Slider();
    private readonly UiVectorDrawList _tickSlider = CrowdHudCanvas.Slider();
    private readonly UiVectorDrawList _avoidSlider = CrowdHudCanvas.Slider();
    private readonly UiVectorDrawList _agentsBar = CrowdHudCanvas.Bar(4);
    private UiVectorDrawList _fpsSpark = null!;
    private UiVectorDrawList _simSpark = null!;
    private UiVectorDrawList _avoidSpark = null!;
    private UiVectorDrawList _headingSpark = null!;
    private UiVectorDrawList _flipSpark = null!;
    private UiVectorDrawList _hpaSpark = null!;

    private UiVectorDrawList TileCacheBar() => _tileCacheBar;
    private UiVectorDrawList FlowCacheBar() => _flowCacheBar;
    private UiVectorDrawList DeployCountSlider() => _deployCountSlider;
    private UiVectorDrawList SpawnCountSlider() => _spawnCountSlider;
    private UiVectorDrawList TickSlider() => _tickSlider;
    private UiVectorDrawList AvoidSlider() => _avoidSlider;
    private UiVectorDrawList AgentsBar() => _agentsBar;
    private UiVectorDrawList FpsSpark() => _fpsSpark;
    private UiVectorDrawList SimSpark() => _simSpark;
    private UiVectorDrawList AvoidSpark() => _avoidSpark;
    private UiVectorDrawList HeadingSpark() => _headingSpark;
    private UiVectorDrawList FlipSpark() => _flipSpark;
    private UiVectorDrawList HpaSpark() => _hpaSpark;

    private CrowdHudClassSwitch _wide = null!;
    private CrowdHudClassSwitch _pathSection = null!;
    private CrowdHudClassSwitch _prGroup = null!;
    private CrowdHudClassSwitch _playerbar = null!;
    private CrowdHudClassSwitch _noticeRow = null!;
    private CrowdHudClassSwitch _hint = null!;
    private CrowdHudClassSwitch _fault = null!;
    private CrowdHudClassSwitch _computing = null!;
    private CrowdHudClassSwitch _dv = null!;
    private CrowdHudClassSwitch _dock = null!;
    private CrowdHudClassSwitch _dockBody = null!;
    private CrowdHudClassSwitch _dockDown = null!;
    private CrowdHudClassSwitch _dockUp = null!;
    private CrowdHudClassSwitch _telemetry = null!;
    private CrowdHudClassSwitch _deployParams = null!;
    private CrowdHudClassSwitch _spawnSection = null!;
    private CrowdHudClassSwitch[] _dvTabs = null!;
    private CrowdHudClassSwitch[] _dvPages = null!;
    private CrowdHudClassSwitch[] _dockTabs = null!;
    private CrowdHudClassSwitch[] _dockPages = null!;
    private CrowdHudClassSwitch _magicSwitch = null!;
    private CrowdHudClassSwitch _losSwitch = null!;
    private CrowdHudClassSwitch _optimisticSwitch = null!;
    private CrowdHudClassSwitch _routeSwitch = null!;
    private CrowdHudClassSwitch _fogSwitch = null!;
    private CrowdHudClassSwitch[] _overlaySwitches = null!;
    private CrowdHudClassSwitch[] _notices = null!;
    private CrowdHudClassSwitch[] _tools = null!;
    private CrowdHudClassSwitch _spawnTile = null!;
    private CrowdHudClassSwitch _structures = null!;
    private CrowdHudClassSwitch _rebake = null!;
    private CrowdHudClassSwitch _rebakeEmpty = null!;
    private CrowdHudClassSwitch _rebakeRows = null!;
    private CrowdHudClassSwitch _validation = null!;
    private CrowdHudClassSwitch _fuzz = null!;
    private CrowdHudClassSwitch _counterDetail = null!;
    private CrowdHudClassSwitch[] _slices = null!;
    private CrowdHudClassSwitch _playIcon = null!;
    private CrowdHudClassSwitch _pauseIcon = null!;

    private static readonly string[] OverlayIds =
    [
        "byplayer", "pushprio", "path", "density", "corridor", "hpa", "navmesh", "tiles", "flow", "areas", "belief", "links",
    ];

    private void InitBindings(int history)
    {
        _fpsSpark = CrowdHudCanvas.Spark(history);
        _simSpark = CrowdHudCanvas.Spark(history);
        _avoidSpark = CrowdHudCanvas.Spark(history);
        _headingSpark = CrowdHudCanvas.Spark(history);
        _flipSpark = CrowdHudCanvas.Spark(history);
        _hpaSpark = CrowdHudCanvas.Spark(history);
        _rings = new double[SparkSeries][];
        for (int i = 0; i < _rings.Length; i++) _rings[i] = new double[history];
    }

    private void InitSwitches()
    {
        _wide = CrowdHudClassSwitch.Toggle(Find(RootId), "wide");
        _pathSection = Visibility("crowd-path");
        _prGroup = Visibility("crowd-pr");
        _playerbar = Visibility("crowd-playerbar");
        _noticeRow = Visibility("crowd-notice-row");
        _hint = Visibility("crowd-hint");
        _fault = Visibility("crowd-fault");
        _computing = Visibility("crowd-computing");
        _dv = Visibility("crowd-dv");
        _dock = Visibility("crowd-dock");
        _dockBody = Visibility("crowd-dock-body");
        _dockDown = Visibility("crowd-dock-down");
        _dockUp = Visibility("crowd-dock-up");
        _telemetry = Visibility("crowd-telemetry");
        _deployParams = Visibility("crowd-deploy-params");
        _spawnSection = Visibility("crowd-spawn-section");
        _dvTabs = Toggles("nf-tab-on", "crowd-dv-tab-player", "crowd-dv-tab-layers", "crowd-dv-tab-inspect", "crowd-dv-tab-overlays", "crowd-dv-tab-cache");
        _dvPages = Visibilities("crowd-dv-player", "crowd-dv-layers", "crowd-dv-inspect", "crowd-dv-overlays", "crowd-dv-cache");
        _dockTabs = Toggles("nf-tab-on", "crowd-dock-tab-deploy", "crowd-dock-tab-tools", "crowd-dock-tab-tests", "crowd-dock-tab-prefs");
        _dockPages = Visibilities("crowd-dock-deploy", "crowd-dock-tools", "crowd-dock-tests", "crowd-dock-prefs");
        _magicSwitch = Toggle("crowd-magic-switch", "nf-switch-on");
        _losSwitch = Toggle("crowd-los-switch", "nf-switch-on");
        _optimisticSwitch = Toggle("crowd-optimistic-switch", "nf-switch-on");
        _routeSwitch = Toggle("crowd-pv-route-switch", "nf-switch-on");
        _fogSwitch = Toggle("crowd-pv-fog-switch", "nf-switch-on");
        _overlaySwitches = new CrowdHudClassSwitch[OverlayIds.Length];
        for (int i = 0; i < OverlayIds.Length; i++) _overlaySwitches[i] = Toggle($"crowd-ov-{OverlayIds[i]}-switch", "nf-switch-on");
        _notices = new CrowdHudClassSwitch[CrowdPlayerHud.MaxNotices];
        for (int i = 0; i < _notices.Length; i++)
            _notices[i] = CrowdHudClassSwitch.Options(Find($"crowd-notice-{i}"), "nf-hidden", "", "nf-notice-gone");
        _tools = Toggles("nf-btn-on", "crowd-tool-off", "crowd-tool-boom", "crowd-tool-spawn", "crowd-tool-place", "crowd-tool-remove");
        _spawnTile = Toggle("crowd-spawn-tile", "nf-btn-primary");
        _structures = Visibility("crowd-structures");
        _rebake = Visibility("crowd-rebake");
        _rebakeEmpty = Visibility("crowd-rebake-empty");
        _rebakeRows = Visibility("crowd-rebake-rows");
        _validation = CrowdHudClassSwitch.Options(Find("crowd-validation"), "", "nf-result-ok", "nf-result-bad");
        _fuzz = CrowdHudClassSwitch.Options(Find("crowd-fuzz"), "", "nf-result-ok", "nf-result-bad");
        _counterDetail = Visibility("crowd-counter-detail");
        _slices = Toggles("nf-chip-on", "crowd-slice-1", "crowd-slice-2", "crowd-slice-3");
        _playIcon = Visibility("crowd-play-play");
        _pauseIcon = Visibility("crowd-play-pause");
    }

    private void InitStatic()
    {
        Title.Set(_feed.Text.Title);
        RebakeEmpty.Set("尚未发生 — 点击地图放置 / 移除实体");
        FpsLow.Set("");
        Fps.Set(CrowdHudFormat.Missing);
    }

    private UiElement Find(string id) =>
        _document.FindById(id) ?? throw new InvalidOperationException($"hud.html 里没有 #{id}。");

    private CrowdHudClassSwitch Visibility(string id) => CrowdHudClassSwitch.Visibility(Find(id));

    private CrowdHudClassSwitch Toggle(string id, string token) => CrowdHudClassSwitch.Toggle(Find(id), token);

    private CrowdHudClassSwitch[] Toggles(string token, params string[] ids)
    {
        var switches = new CrowdHudClassSwitch[ids.Length];
        for (int i = 0; i < ids.Length; i++) switches[i] = Toggle(ids[i], token);
        return switches;
    }

    private CrowdHudClassSwitch[] Visibilities(params string[] ids)
    {
        var switches = new CrowdHudClassSwitch[ids.Length];
        for (int i = 0; i < ids.Length; i++) switches[i] = Visibility(ids[i]);
        return switches;
    }

    private static UiTextBinding[] Make(int count, int capacity)
    {
        var bindings = new UiTextBinding[count];
        for (int i = 0; i < count; i++) bindings[i] = new UiTextBinding(capacity);
        return bindings;
    }

    /// <summary>把 <see cref="W"/> 写进绑定后清空，内容没变就不动绑定。</summary>
    private void Flush(UiTextBinding binding)
    {
        binding.Set(W);
        W.Clear();
    }

    private void Put<T>(UiTextBinding binding, T value) where T : ISpanFormattable
    {
        W.Clear();
        W.Append(value, default, CrowdHudFormat.Inv);
        Flush(binding);
    }

    private static void Put(UiTextBinding binding, string text) => binding.Set(text);
}
