using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Presentation;

public enum CrowdHudKind : byte
{
    Path = 1,
    Deploy = 2,
}

public enum CrowdUnitColorMode : byte
{
    Off = CrowdSimulationDebugViewConfig.ColorOff,
    Team = CrowdSimulationDebugViewConfig.ColorTeam,
    Template = CrowdSimulationDebugViewConfig.ColorTemplate,
    Priority = CrowdSimulationDebugViewConfig.ColorPriority,
}

/// <summary>演示配置里的标题、键位模板和放置提示。整个呈现期内不变。</summary>
public sealed class CrowdHudText
{
    internal CrowdHudText(CrowdPresentationConfig presentation)
    {
        Title = presentation.Title;
        KeysTemplate = presentation.Hud.Keys;
        CrowdDeployPresentation? deploy = presentation.Deploy;
        if (deploy == null) return;
        BuildActivity = deploy.BuildActivity;
        ObserveActivity = deploy.ObserveActivity;
        PlaceOk = deploy.PlaceOk;
        PlaceOffMap = deploy.PlaceOffMap;
        PlaceOutOfBounds = deploy.PlaceOutOfBounds;
        PlaceOverlapsStructure = deploy.PlaceOverlapsStructure;
        PlaceOverlapsBlocked = deploy.PlaceOverlapsBlocked;
        PlaceInteriorArea = deploy.PlaceInteriorArea;
        PlaceEndpointArea = deploy.PlaceEndpointArea;
        PendingNav = deploy.PendingNav;
    }

    public string Title { get; }
    public string KeysTemplate { get; }
    public string BuildActivity { get; } = string.Empty;
    public string ObserveActivity { get; } = string.Empty;
    public string PlaceOk { get; } = string.Empty;
    public string PlaceOffMap { get; } = string.Empty;
    public string PlaceOutOfBounds { get; } = string.Empty;
    public string PlaceOverlapsStructure { get; } = string.Empty;
    public string PlaceOverlapsBlocked { get; } = string.Empty;
    public string PlaceInteriorArea { get; } = string.Empty;
    public string PlaceEndpointArea { get; } = string.Empty;
    public string PendingNav { get; } = string.Empty;
}

/// <summary>S3 两点路径演示的一次读数。</summary>
public record struct CrowdPathHud
{
    public int RequestTick;
    public int DueTick;
    public int Tick;
    public int Waits;
    public int Threads;
    public string ContextLabel;
    public string ViewLabel;
    public bool HasCurrent;
    public bool Reachable;
    public CorridorQuery.Branch Branch;
    public int FlowReached;
    public bool Slow;
    public string? Fault;
}

/// <summary>S4 部署演示的一次读数：会话计数、工具态、最近重烘、回放、校验与模糊结论。</summary>
public record struct CrowdDeployHud
{
    public bool SessionActive;
    public int Tick;
    public int Units;
    public int Idle;
    public int Moving;
    public int Arrived;
    public int Unreachable;
    public int Traversing;
    /// <summary>本机座位玩家的选中数；没有座位时为 -1。</summary>
    public int Selected;
    public int FixedHz;
    public double AvoidHz;
    public int RebakeSlices;

    public CrowdClickTool Tool;
    public bool PlaybackHeld;
    public int SpeedPermille;
    /// <summary>当前模板要点两下（起点、终点）。</summary>
    public bool TemplateTwoPoint;
    public bool HasSpanAnchor;
    public int ViewMode;
    public string ViewLabel;
    public int TemplateIndex;
    public string TemplateLabel;
    public bool PendingNav;

    /// <summary>光标不在地表上时为 false；此时没有预览结论。</summary>
    public bool PreviewKnown;
    public bool PreviewShown;
    public bool PreviewValid;
    public CrowdPlacementReject PreviewReason;

    public bool HasRebake;
    public int RebakeTiles;
    public int RebakeCostOnly;
    public int RebakeContexts;
    public int RebakeHits;
    public int RebakeMisses;
    public int RebakeOrders;
    public int RebakeEvicted;
    public int RebakeStuck;
    public double RebakeMs;

    public bool Interactive;
    public bool RuntimePresent;
    public int ReplayStatus;
    public int ReplayDivergenceTick;
    public string ReplayDivergenceSection;
    public CrowdSimDiagnosticSection ReplayDiagnostic;
    public int ReplayDiagnosticTick;

    public long SubStepClamped;
    public long EvictStuck;
    public long Plans;
    public long Rebakes;
    public long FogRefreshes;

    public bool Expanded;
    public long Contacts;
    public long PathWaits;
    public int FogVisible;
    public int FogExplored;
    public int FogGhosts;

    public string ValidationLine;
    public bool ValidationFailed;
    public bool ValidationRan;
    public string FuzzLine;
    public bool FuzzFailed;
    public bool FuzzRan;
}

/// <summary>调试视图检视行：当前检视的移动层、半径级、着色方式与视野组分歧。</summary>
public record struct CrowdInspectReadout
{
    public bool NavOverlays;
    public string AgentTypeId;
    public string ProfileId;
    public CrowdUnitColorMode UnitColor;
    public bool TerrainTypes;
    public bool Belief;
    public int BeliefPlayer;
    public int DivergentCells;
    public int Ghosts;
    public bool Funnel;
    public bool FormationSlots;
    public bool Corridor;
    public bool HpaGraph;
    public bool NavPolygons;
    public bool TileGrid;
    public bool Flow;
    public bool Density;
    public bool Blocked;
    public bool Links;

    public readonly bool Visible => NavOverlays || Belief || UnitColor != CrowdUnitColorMode.Off;
}

/// <summary>玩家视图状态卡：最新指令的计数与玩家偏好。各玩家单位数单独写入调用方缓冲。</summary>
public record struct CrowdPlayerHud
{
    public int PlayerCount;
    public int LocalPlayer;
    public CrowdCommandCardState Card;
    public int Planning;
    public int Moving;
    public int Arrived;
    public int Other;
    public string ShapeLabel;
    /// <summary>最新指令的模式；没有指令时为 null。</summary>
    public CrowdOrderMode? Mode;
    public bool MagicBox;
    public bool FogHide;
    public bool Route;
    public bool LineOfSight;
    public string ClickTemplate;
    public int ClickCount;
    public int DeployCount;
    public string WindowFrom;
    public string WindowTo;
    public string LayerId;
    public int SceneKind;
    /// <summary>当前阵型在 <see cref="CrowdHudCatalog.Formations"/> 里的下标；配置里找不到时为 -1。</summary>
    public int ShapeIndex;
    /// <summary>按兵种下标的启用位。</summary>
    public int LayerMask;
    /// <summary>本方最近的指令，按指令号从新到旧；只填前 <see cref="NoticeCount"/> 条。</summary>
    public int NoticeCount;
    public CrowdOrderNotice Notice0;
    public CrowdOrderNotice Notice1;
    public CrowdOrderNotice Notice2;

    public const int MaxNotices = 3;

    public readonly CrowdOrderNotice Notice(int index) => index switch
    {
        0 => Notice0,
        1 => Notice1,
        2 => Notice2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

/// <summary>一条指令按单位状态的计数。规划中的组里未到达的单位都算规划中。</summary>
public record struct CrowdOrderNotice
{
    public int Id;
    public CrowdOrderMode Mode;
    public int Units;
    public int Planning;
    public int Moving;
    public int Arrived;
    public int Unreachable;
}

/// <summary>导航缓存与路径服务的一次读数。读不到的项为 NaN 或对应 Has* 为 false。</summary>
public record struct CrowdNavReadout
{
    public bool HasTileCache;
    public int TileCapacity;
    public int TileCount;
    public int TileHits;
    public int TileMisses;
    public int TileEvicts;
    public int FlowCapacity;
    public bool HasFlowCache;
    public int FlowCount;
    public int FlowHits;
    public int FlowMisses;
    public int FlowEvicts;
    public int ResidentNavs;
    public bool HasPath;
    public long PlanRequests;
    public int PendingReplies;
    public double RoundTripLastMs;
    public double WorkerLastMs;
    public long WaitTicks;
    public int Tick;
    public int CommandLog;
    public double PlanTimeoutMs;

    internal static CrowdNavReadout From(CrowdSimSession session)
    {
        var cache = session.NavTileCache;
        var planner = session.Planner;
        var service = planner?.Service;
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
            HasFlowCache = planner != null,
            FlowCount = planner?.FlowCacheCount ?? 0,
            FlowHits = planner?.FlowCacheHits ?? 0,
            FlowMisses = planner?.FlowCacheMisses ?? 0,
            FlowEvicts = planner?.FlowCacheEvicts ?? 0,
            ResidentNavs = resident,
            HasPath = hasPath,
            PlanRequests = hasPath ? stats.Requests : 0,
            PendingReplies = hasPath ? stats.Pending : 0,
            RoundTripLastMs = hasPath ? stats.RoundTripLastMs : double.NaN,
            WorkerLastMs = hasPath ? stats.WorkerLastMs : double.NaN,
            WaitTicks = session.Telemetry.StallTicks,
            Tick = session.TickCount,
            CommandLog = session.Commands.Log.Count,
            PlanTimeoutMs = hasPath ? stats.PlanTimeoutMs : double.NaN,
        };
    }
}

/// <summary>
/// 仿真侧遥测的一次读数：分段耗时、路径服务耗时、单位状态分布和世界生成。
/// 帧率、堆增长、界面重绘这些宿主侧数值不在这里，由显示它的一方自己读。
/// </summary>
public record struct CrowdTelemetryReadout
{
    public int FixedHz;
    public double SimMs;
    public double AvoidMs;
    public double HashMs;
    public double HeadingDeg;
    public bool HasHeading;
    public int FlipPct;
    public bool HasFlip;
    public double NavMeshMs;
    public double HpaMs;
    public double FlowMs;
    public double PlanMs;
    public double NavBakeMs;
    public double RebakeMs;
    public int Idle;
    public int Moving;
    public int Arrived;
    public int Unreachable;
    public int Traversing;
    public int NavCellCm;
    public int TileCount;
    public bool WorldBakeRecorded;
    public double WorldBakeMs;
    public int WorldCacheHits;
    public int WorldCacheMisses;
    public bool HasExtend;
    public int ExtendCount;

    internal static CrowdTelemetryReadout From(CrowdSimSession session, CrowdSimulationRuntime? runtime)
    {
        var telemetry = session.Telemetry;
        var phases = telemetry.LastPhaseTicks;
        var service = session.Planner?.Service;
        PathServiceStats stats = default;
        bool hasPath = service != null;
        if (hasPath) stats = service!.CopyStats();
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
            FixedHz = session.Config.FixedHz,
            SimMs = telemetry.LastTickMs,
            AvoidMs = CrowdSimulationTelemetry.TicksToMs(phases.Separation),
            HashMs = CrowdSimulationTelemetry.TicksToMs(phases.Hash),
            HeadingDeg = telemetry.HeadingDeg,
            HasHeading = telemetry.MotionSamples > 0,
            FlipPct = telemetry.FlipPct,
            HasFlip = telemetry.SteerPairs > 0,
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
            Traversing = telemetry.Traversing,
            NavCellCm = session.Config.NavCellSizeCm,
            TileCount = tiles,
            WorldBakeRecorded = baked,
            WorldBakeMs = baked ? runtime!.WorldBakeMs : double.NaN,
            WorldCacheHits = baked ? runtime!.WorldBakeCacheHits : 0,
            WorldCacheMisses = baked ? runtime!.WorldBakeCacheMisses : 0,
            HasExtend = session.Planner != null,
            ExtendCount = session.Planner?.ExtendSubmits ?? 0,
        };
    }
}
