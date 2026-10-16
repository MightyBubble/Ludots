using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体演示 HUD 的只读数据口。界面在自己的节奏上调 Read*，这里只拷贝现成状态，不排版、不缓存文本。
/// 仿真 tick 不经过这里；没人读就没有开销。
/// </summary>
public sealed class CrowdHudFeed
{
    private readonly S3PathDemoRuntime? _path;
    private readonly S4DeployDemoRuntime? _deploy;
    private readonly Func<CrowdSimSession?> _session;
    private readonly Func<CrowdSimulationRuntime?> _runtime;
    private readonly Func<int>? _localSeatScope;
    private CrowdDebugViewFrame? _debugView;
    private CrowdPlayerViewHost? _playerView;
    private bool _previewKnown;
    private bool _previewShown;
    private bool _previewValid;
    private CrowdPlacementReject _previewReason;
    private CrowdSimSession? _catalogSession;
    private CrowdHudCatalog? _catalog;

    internal CrowdHudFeed(
        S3PathDemoRuntime path,
        CrowdPresentationConfig presentation,
        Func<CrowdSimSession?> session,
        Func<CrowdSimulationRuntime?> runtime)
    {
        Kind = CrowdHudKind.Path;
        _path = path;
        _session = session;
        _runtime = runtime;
        Text = new CrowdHudText(presentation);
    }

    internal CrowdHudFeed(
        S4DeployDemoRuntime deploy,
        CrowdPresentationConfig presentation,
        Func<CrowdSimSession?> session,
        Func<CrowdSimulationRuntime?> runtime,
        Func<int>? localSeatScope)
    {
        Kind = CrowdHudKind.Deploy;
        _deploy = deploy;
        _session = session;
        _runtime = runtime;
        _localSeatScope = localSeatScope;
        Text = new CrowdHudText(presentation);
    }

    public CrowdHudKind Kind { get; }
    public CrowdHudText Text { get; }
    public CrowdPlayerViewLabels? PlayerLabels => _playerView?.Labels;
    public bool HasDebugView => _debugView != null;
    public bool HasPlayerView => _playerView != null;
    public bool NavPanelOpen => _debugView is { NavPanel: true };
    public bool TelemetryPanelOpen => _debugView is { TelemetryPanel: true };

    public int PathReads { get; private set; }
    public int DeployReads { get; private set; }
    public int PlayerReads { get; private set; }
    public int InspectReads { get; private set; }
    public int NavReads { get; private set; }
    public int TelemetryReads { get; private set; }
    public int FaultReads { get; private set; }

    /// <summary>部署演示控件的动作号；路径演示为 null。</summary>
    public CrowdHudActions? Actions { get; private set; }

    /// <summary>当前会话的选项名单；没有会话或不是部署演示时为 null。会话不换就返回同一个实例。</summary>
    public CrowdHudCatalog? Catalog
    {
        get
        {
            if (_deploy == null || _session() is not { } session) return null;
            if (!ReferenceEquals(session, _catalogSession))
            {
                _catalog = new CrowdHudCatalog(session, _deploy.Choices, _playerView?.ViewConfig, _deploy.Rates);
                _catalogSession = session;
            }

            return _catalog;
        }
    }

    internal void UseActions(CrowdHudActions actions) => Actions = actions;

    internal void UseDebugView(CrowdDebugViewFrame frame) => _debugView = frame;

    internal void UsePlayerView(CrowdPlayerViewHost host) => _playerView = host;

    internal void NotePreview(bool known, bool shown, bool valid, CrowdPlacementReject reason)
    {
        _previewKnown = known;
        _previewShown = shown;
        _previewValid = valid;
        _previewReason = reason;
    }

    public bool ReadPath(ref CrowdPathHud hud)
    {
        if (_path == null) return false;
        PathReads++;
        var current = _path.Current;
        hud.RequestTick = _path.RequestTick;
        hud.DueTick = _path.DueTick;
        hud.Tick = _path.TickCounter;
        hud.Waits = _path.TotalWaits;
        hud.Threads = _path.ThreadCount;
        hud.ContextLabel = _path.ContextLabel;
        hud.ViewLabel = _path.ViewLabel;
        hud.Slow = _path.SlowInjected;
        hud.Fault = _path.Fault;
        hud.HasCurrent = current != null;
        hud.Reachable = current?.Reachable ?? false;
        hud.Branch = current?.Branch ?? default;
        hud.FlowReached = current?.Flow?.Reached ?? 0;
        return true;
    }

    public bool ReadDeploy(ref CrowdDeployHud hud)
    {
        S4DeployDemoRuntime? demo = _deploy;
        if (demo == null) return false;
        DeployReads++;
        CrowdSimSession? session = _session();
        CrowdSimulationRuntime? runtime = _runtime();
        hud.Tool = demo.Tool;
        hud.ToolLabel = demo.ToolLabel;
        hud.BuildMode = demo.BuildMode;
        hud.PlaybackHeld = demo.PlaybackHeld;
        hud.SpeedPermille = demo.SpeedPermille;
        hud.HasSpanAnchor = demo.HasSpanAnchor;
        hud.TickHzWindow = demo.TickHzWindow;
        hud.ViewMode = demo.ViewMode;
        hud.ViewLabel = demo.ViewLabel;
        hud.TemplateIndex = demo.TemplateIndex;
        hud.TemplateLabel = demo.Choice.Label;
        hud.Interactive = demo.InteractiveMode;
        hud.PreviewKnown = session != null && _previewKnown;
        hud.PreviewShown = hud.PreviewKnown && _previewShown;
        hud.PreviewValid = hud.PreviewKnown && _previewValid;
        hud.PreviewReason = hud.PreviewKnown ? _previewReason : CrowdPlacementReject.None;

        hud.SessionActive = session != null;
        hud.Expanded = session != null && demo.TelemetryExpanded;
        hud.PendingNav = session != null && S4DeployDemoPresentationSystem.NavVariantPending(session, demo.ViewMode);
        if (session != null)
        {
            var telemetry = session.Telemetry;
            hud.Tick = session.TickCount;
            hud.Units = session.Units.Count;
            hud.Idle = telemetry.Idle;
            hud.Moving = telemetry.Moving;
            hud.Arrived = telemetry.Arrived;
            hud.Unreachable = telemetry.Unreachable;
            hud.Traversing = telemetry.Traversing;
            hud.Selected = runtime != null && runtime.TryLocalSeatPlayerId(out int seatPlayer)
                ? session.SelectedCollectionCount(seatPlayer)
                : -1;
            hud.FixedHz = session.Config.FixedHz;
            hud.AvoidHz = session.AvoidHz.ToDouble();
            hud.RebakeSlices = session.RebakeSliceCount;
            hud.SubStepClamped = telemetry.SubStepClamped;
            hud.EvictStuck = telemetry.EvictStuck;
            hud.Plans = telemetry.Plans;
            hud.Rebakes = telemetry.Rebakes;
            hud.FogRefreshes = telemetry.FogRefreshes;
            hud.Contacts = hud.Expanded ? telemetry.Contacts : 0;
            hud.PathWaits = hud.Expanded ? telemetry.PathWaits : 0;
            ReadLocalFog(hud.Expanded ? session : null, out hud.FogVisible, out hud.FogExplored, out hud.FogGhosts);
            WriteRebake(ref hud, session.LastRebakeReport);
        }
        else
        {
            hud.Tick = hud.Units = hud.Idle = hud.Moving = hud.Arrived = hud.Unreachable = hud.Traversing = 0;
            hud.Selected = -1;
            hud.FixedHz = 0;
            hud.AvoidHz = 0;
            hud.RebakeSlices = -1;
            hud.SubStepClamped = hud.EvictStuck = hud.Plans = hud.Rebakes = hud.FogRefreshes = 0;
            hud.Contacts = hud.PathWaits = 0;
            hud.FogVisible = hud.FogExplored = hud.FogGhosts = 0;
            WriteRebake(ref hud, null);
        }

        hud.RuntimePresent = runtime != null;
        if (runtime != null)
        {
            hud.ReplayStatus = runtime.ReplayStatus;
            hud.ReplayDivergenceTick = runtime.ReplayDivergenceTick;
            hud.ReplayDivergenceSection = runtime.ReplayDivergenceSection;
            hud.ReplayDiagnostic = runtime.ReplayDivergenceDiagnostic;
            hud.ReplayDiagnosticTick = runtime.ReplayDiagnosticTick;
            hud.ValidationLine = runtime.DebugValidationLine;
            hud.ValidationFailed = runtime.DebugValidationFailed;
            hud.ValidationRan = runtime.DebugValidationRan;
            hud.FuzzLine = runtime.DebugFuzzLine;
            hud.FuzzFailed = runtime.DebugFuzzFailed;
            hud.FuzzRan = runtime.DebugFuzzRan;
        }
        else
        {
            hud.ReplayStatus = 0;
            hud.ReplayDivergenceTick = -1;
            hud.ReplayDivergenceSection = string.Empty;
            hud.ReplayDiagnostic = CrowdSimDiagnosticSection.None;
            hud.ReplayDiagnosticTick = -1;
            hud.ValidationLine = string.Empty;
            hud.ValidationFailed = hud.ValidationRan = false;
            hud.FuzzLine = string.Empty;
            hud.FuzzFailed = hud.FuzzRan = false;
        }

        return true;
    }

    public bool ReadPlayer(ref CrowdPlayerHud hud, Span<int> unitsByPlayer)
    {
        if (_playerView == null) return false;
        PlayerReads++;
        _playerView.ReadPanel(ref hud, unitsByPlayer);
        return true;
    }

    public bool ReadInspect(ref CrowdInspectReadout readout)
    {
        if (_debugView == null) return false;
        InspectReads++;
        _debugView.ReadInspect(ref readout);
        return true;
    }

    public bool ReadNav(ref CrowdNavReadout readout)
    {
        if (_debugView == null || _session() is not { } session) return false;
        NavReads++;
        readout = CrowdNavReadout.From(session);
        return true;
    }

    public bool ReadTelemetry(ref CrowdTelemetryReadout readout)
    {
        if (_debugView == null || _session() is not { } session) return false;
        TelemetryReads++;
        readout = CrowdTelemetryReadout.From(session, _runtime());
        return true;
    }

    /// <summary>路径服务故障时返回首行原因与规划超时；服务正常或没有会话时返回 false。</summary>
    public bool ReadPathFault(out string firstLine, out double planTimeoutMs)
    {
        firstLine = string.Empty;
        planTimeoutMs = double.NaN;
        if (_debugView == null || _session() is not { } session) return false;
        FaultReads++;
        var service = session.Planner?.Service;
        if (service is not { Faulted: true }) return false;
        firstLine = FirstLine(service.FaultMessage);
        planTimeoutMs = service.PlanTimeout.TotalMilliseconds;
        return true;
    }

    private static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        int split = message.IndexOf('\n');
        return split < 0 ? message : message.Substring(0, split);
    }

    private static void WriteRebake(ref CrowdDeployHud hud, CrowdRebakeReport? report)
    {
        hud.HasRebake = report != null;
        if (report == null)
        {
            hud.RebakeExecTick = hud.RebakeReportTick = 0;
            hud.RebakeIsPlace = false;
            hud.RebakeTiles = hud.RebakeCostOnly = hud.RebakeContexts = 0;
            hud.RebakeHits = hud.RebakeMisses = hud.RebakeOrders = hud.RebakeEvicted = hud.RebakeStuck = 0;
            hud.RebakeMs = 0;
            return;
        }

        hud.RebakeExecTick = report.ExecTick;
        hud.RebakeReportTick = report.ReportTick;
        hud.RebakeIsPlace = report.Kind == CrowdRebakeReport.KindPlace;
        hud.RebakeTiles = report.Tiles;
        hud.RebakeCostOnly = report.CostOnly;
        hud.RebakeContexts = report.Contexts;
        hud.RebakeHits = report.Hits;
        hud.RebakeMisses = report.Misses;
        hud.RebakeOrders = report.Orders;
        hud.RebakeEvicted = report.Evicted;
        hud.RebakeStuck = report.Stuck;
        hud.RebakeMs = CrowdSimulationTelemetry.TicksToMs(report.ElapsedTicks);
    }

    /// <summary>可见数、已探索数取本机座位的视野组。没有座位时保持 0，不改用第一组。</summary>
    private void ReadLocalFog(CrowdSimSession? session, out int visible, out int explored, out int ghosts)
    {
        visible = explored = ghosts = 0;
        CrowdFog? fog = session?.Fog;
        if (fog == null || _localSeatScope == null) return;
        int scope = _localSeatScope();
        if (scope <= 0) return;
        int group = scope - 1;
        if ((uint)group >= (uint)fog.VisionGroupCount) return;
        visible = fog.VisibleCountOf(group);
        explored = fog.ExploredCountOf(group);
        ghosts = fog.GhostCount[group];
    }
}
