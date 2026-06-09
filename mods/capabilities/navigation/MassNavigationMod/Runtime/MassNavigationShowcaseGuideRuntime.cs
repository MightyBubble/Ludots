using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;

namespace MassNavigationMod.Runtime;

public enum MassNavigationShowcaseStepId
{
    VisualHeightmapBake = 0,
    LogicHeightmapBake = 1,
    LayerAreaEditor = 2,
    NavMeshBake = 3,
    PathOnly = 4,
    WorldHpa = 5,
    StrategySwitch = 6,
    OrderReuse = 7,
    TargetAllocation = 8,
    LayerCosts = 9,
    WaypointAuthoring = 10,
    LargeWorldStreaming = 11,
    TenKFlow = 12,
    StaticObstacleWorld = 13,
    PerformanceDebug = 14,
    DebugVisualBudget = 15,
    BakeToolQuery = 16,
}

public readonly record struct MassNavigationShowcaseStep(
    MassNavigationShowcaseStepId Id,
    string Title,
    string Who,
    string What,
    string When,
    string Where,
    string Why,
    string How,
    string PlayerInput,
    string PlayerExpected,
    string ReadablePassSignal,
    string DebugLegend,
    string ExpectedOutput,
    string ProductionGate);

public readonly record struct MassNavigationGuideSegment(
    int Axcm,
    int Aycm,
    int Bxcm,
    int Bycm,
    string Kind,
    int ClearanceCm,
    int AreaId);

public readonly record struct MassNavigationNavMeshGuideSample(
    bool Available,
    string Source,
    string LogicHeightmapSource,
    int Layer,
    string ProfileId,
    int ChunkX,
    int ChunkY,
    int TriangleCount,
    int PortalCount,
    int FirstAreaId,
    int MinPortalClearanceCm,
    int AgentRadiusCm,
    int BlockedCellCount,
    int HighCostCellCount,
    int WaterCellCount,
    int RampCellCount,
    string AreaLegend,
    string LayerLegend,
    string BlockedSource,
    string OffMeshLinkSource,
    MassNavigationGuideSegment[] TriangleEdges,
    MassNavigationGuideSegment[] Portals);

public readonly record struct MassNavigationNavMeshCoverageGuide(
    bool Available,
    bool IsPartialCoverage,
    int TargetChunkCount,
    int WorldChunkCount,
    int ActiveWindowMinChunkX,
    int ActiveWindowMinChunkY,
    int ActiveWindowMaxChunkX,
    int ActiveWindowMaxChunkY,
    int ActiveWindowChunkCount,
    int LayerCount,
    int ProfileCount,
    int TotalExpectedTileBakes,
    int TotalBakedTiles)
{
    public static MassNavigationNavMeshCoverageGuide Unavailable { get; } = new(
        Available: false,
        IsPartialCoverage: false,
        TargetChunkCount: 0,
        WorldChunkCount: 0,
        ActiveWindowMinChunkX: -1,
        ActiveWindowMinChunkY: -1,
        ActiveWindowMaxChunkX: -1,
        ActiveWindowMaxChunkY: -1,
        ActiveWindowChunkCount: 0,
        LayerCount: 0,
        ProfileCount: 0,
        TotalExpectedTileBakes: 0,
        TotalBakedTiles: 0);

    public int NotLoadedWorldChunkCount => Math.Max(0, WorldChunkCount - TargetChunkCount);
}

public sealed class MassNavigationShowcaseGuideRuntime
{
    private const int MaxNavMeshOverviewTiles = 256;
    private const int MaxRuntimeNavMeshTiles = 256;
    private const int MaxActiveWindowNavMeshEdges = 48_000;
    private const int MaxNavMeshOverviewTrianglesPerTile = 20;
    private const int MaxRuntimeNavMeshTrianglesPerTile = 96;

    private static readonly MassNavigationShowcaseStep[] StepCatalog =
    {
        new(
            MassNavigationShowcaseStepId.VisualHeightmapBake,
            "U1 VisualHeightmap bake",
            "Map tool user and mod author",
            "Bake a visual heightmap into LogicHeightmap, then into active-window NavMesh tiles.",
            "Before trusting any path on a terrain-authored map.",
            "Visual source, LogicHeightmap contract, loaded .ntil tile, walkable mesh sample.",
            "Visual terrain must produce the same nav truth as authored logic data.",
            "Click U1 VHTM, then inspect the logic source and loaded tile sample.",
            "Click U1 VHTM.",
            "You see vhtm -> LogicHeightmap -> .ntil, plus walkable/blocked/high-cost evidence.",
            "Logic source ends in .lhtm, tile has triangles and portals, active-window status is visible.",
            "Green/cyan=walkable mesh; red=blocked source; gold=high-cost; orange=portal/link.",
            "Bake source, normalized logic data, NavMesh tile, and validator artifacts are visible.",
            "PASS: VHTM normalizes to LogicHeightmap, active-window .ntil tiles are loaded, and validator evidence is linked."),
        new(
            MassNavigationShowcaseStepId.LogicHeightmapBake,
            "U2 LogicHeightmap unification",
            "Engine/tooling developer",
            "Verify vtxm, vhtm, quad grid, and hex vertex inputs converge to LogicHeightmap.",
            "Before adding more source-specific bake behavior.",
            "LogicHeightmap source, grid/chunk contract, nav tile sample, and validator lane.",
            "One bake substrate prevents semantic drift between editor, runtime, and mods.",
            "Click U2 Logic and read the source/origin contract before inspecting the mesh.",
            "Click U2 Logic.",
            "The guide explains that all terrain sources normalize before NavMesh generation.",
            "SourceKind=lhtm semantics and active-window .ntil sample are visible together.",
            "LogicHeightmap is the contract; NavMesh/graph/flow bake from it.",
            "Source convergence, chunk scale, and current active-window bake proof are visible.",
            "PASS: vtxm, vhtm and lhtm sources converge into one LogicHeightmap bake contract."),
        new(
            MassNavigationShowcaseStepId.LayerAreaEditor,
            "U3 Mountain river layer editor",
            "Map editor user and movement designer",
            "Inspect mountain, river, NoFly/high-cost, and walkable area semantics.",
            "Before shipping a map with multiple movement layers.",
            "Layer regions, water/mountain/high-cost/blocked masks, and cost labels.",
            "Layer editing is only trustworthy when the data that will be baked is visible.",
            "Click U3 Areas and compare colored regions with the profile rows.",
            "Click U3 Areas.",
            "Mountains, rivers, blocked zones, high-cost areas, and active NavMesh sample appear together.",
            "Layer count, profile count, area-cost rows, and blocked/high-cost counts are non-zero.",
            "Green=ground; blue=water; gold=mountain/high-cost; red=blocked/NoFly.",
            "Layer/area semantics are visible before route strategy is judged.",
            "PASS: layer/cost semantics and active-window multi-layer query rows are visible."),
        new(
            MassNavigationShowcaseStepId.PathOnly,
            "U4 Path-only point query",
            "Designer or player testing whether a route exists",
            "Pick a start point and a goal point to run a path preview without submitting a move order.",
            "Before sending units or allocating formation slots.",
            "Start/goal world points, pathpoints, corridor, portals, and editable waypoint seed.",
            "Pathfinding and movement are different products; this step proves the query without side effects.",
            "Click Pick Path Preview, then left-click the start on the ground and right-click the goal.",
            "Left-click start, right-click goal.",
            "The picked route is highlighted, units do not receive an order, and the visual separates waypoints from pathpoints.",
            "NoOrderSubmitted=true, orderDelta=0, pathpoints>0, corridor portals visible.",
            "Cyan strip=corridor; bright cyan dots=immutable pathpoints; yellow handles=editable waypoint intent; orange dots=portal crossings.",
            "Immutable pathpoints, corridor strip, portal crossings, editable waypoint handles, and NoOrderSubmitted=true.",
            "PASS: route preview is highlighted, pathpoints are immutable, waypoints are editable, and no unit order is submitted."),
        new(
            MassNavigationShowcaseStepId.NavMeshBake,
            "U1/U16 NavMesh bake workbench",
            "Navigation tool author and mod author",
            "Inspect what is walkable, blocked, high-cost, near an edge, or connected by a border portal; authored off-mesh links are called out as absent in smoke.",
            "After LogicHeightmap bake and before route strategy selection.",
            "Loaded active-window .ntil tile, triangle edges, portals, clearance, and agent radius.",
            "A Unity/Unreal-like bake tool must make mesh truth visible, not only export a file.",
            "Press NavMesh View and compare triangle mesh, border portals, clearance band, authored off-mesh-link status, and agent radius.",
            "Click NavMesh View.",
            "The scene shows walkable triangles, blocked/high-cost source, portal clearance, border portals, authored off-mesh-link status, and current agent radius.",
            "TriangleCount>0, PortalCount>0, clearance>0, LogicHeightmap source and blocked/high-cost counts are visible.",
            "Green/cyan lines=walkable triangle edges; red label=blocked source; yellow/orange=high-cost/portal; yellow circle=agent radius.",
            "Walkable triangle edges, blocked-source label, high-cost samples, border portal endpoints, off-mesh-link status, and radius are visible.",
            "PASS: walkable mesh, blocked/high-cost source, portal clearance, mesh-link status and agent radius are visible."),
        new(
            MassNavigationShowcaseStepId.WorldHpa,
            "U5 World and HPA route",
            "Player, UAT reviewer, and mod author",
            "Verify the 64km world, active data window, macro chunks, and HPA route chunks.",
            "Before judging any unit motion or FPS.",
            "256x256 macro grid, active window, start chunk, goal chunk, and crossed chunks.",
            "A large RTS path is only trustworthy when the reviewer can see which chunks and portals the route touches.",
            "Click Pick HPA Route, then optionally left-click a start chunk and right-click a far goal chunk.",
            "Left-click start and right-click goal to refresh the route, or inspect the seeded long route.",
            "You see the 64km world concept, active window, start chunk, goal chunk, numbered route chunks, and portal crossings.",
            "Route chunks are numbered, portal ids are visible when graph data exists, and routeChunks/portals are non-zero.",
            "Purple cells=HPA route chunks; green box=loaded active window; orange nodes=portal crossings.",
            "Route chunks, active-window bounds, HPA portals, and streaming counters are visible.",
            "PASS: numbered HPA route chunks, portal crossings and active-window graph route are visible."),
        new(
            MassNavigationShowcaseStepId.StrategySwitch,
            "U6 Road/NavMesh/Hybrid switch",
            "Gameplay designer and mod author",
            "Compare the same query across road graph, NavMesh, and hybrid selection.",
            "After path-only query and before mass movement.",
            "The same start/goal route, with graph, mesh, and hybrid choices drawn separately.",
            "Different unit classes need different strategies without changing the player order model.",
            "Click Pick Strategy Route, then left-click one start and right-click one goal so all candidates use the same query.",
            "Left-click start and right-click goal.",
            "Road, NavMesh, and Hybrid candidates are visually different and the current profile rows explain selected strategy/cost.",
            "Strategy rows list graph/mesh status, route id, touched tiles, selected strategy, and the shared start/goal.",
            "Blue=road graph candidate; green=NavMesh candidate; gold=hybrid selected candidate.",
            "Road, NavMesh, and hybrid candidates show point counts, costs, touched tiles, and selected strategy.",
            "PASS: graph, NavMesh and hybrid evidence are available for the same query."),
        new(
            MassNavigationShowcaseStepId.LayerCosts,
            "U9 Layer and cost profiles",
            "Mod author configuring ground, water, air, and mountain movement",
            "Verify that layer, area cost, forbidden area, and high-cost regions are visible.",
            "Before shipping a map with air/water/mountain/naval units.",
            "Ground, water, air, mountain bands, NoFly/high-cost areas, and cost labels.",
            "A unit layer is not a cosmetic label; it changes reachability and route cost.",
            "Switch this step and compare each profile row with the colored world regions.",
            "Click Layer/Cost.",
            "You can tell why infantry, large vehicles, naval, air, and mountain units prefer or avoid different regions.",
            "Layer count, profile count, area-cost samples, blocked mask, and active-window mesh status are visible.",
            "Green=ground; blue=water; gold=mountain/high-cost; red=blocked/NoFly; mesh lines show active profile tile.",
            "Profiles, layers, costs, blocked masks, and active-window mesh query status are visible.",
            "PASS: ground, water, air and mountain profiles expose layer costs and active-window mesh status."),
        new(
            MassNavigationShowcaseStepId.OrderReuse,
            "U7 Same/near order reuse",
            "RTS player repeatedly issuing the same or almost same order",
            "Select a squad, right-click the same destination twice, then a nearby destination; verify route bucket reuse.",
            "When many units receive simultaneous or near-identical orders.",
            "Shared goal bucket around the target, route id/cached signature, and formal order fanout.",
            "The engine must not recalculate identical large-army paths for every unit.",
            "Click Select Reuse Squad, right-click one destination twice, then right-click a nearby point.",
            "Select Reuse Squad, then right-click same point twice and a near point once.",
            "The same route id is reused for identical or nearby orders instead of generating a fresh path per unit.",
            "cacheHit=true, reused route id is non-zero, and scope is same_point_order_bucket or near_point_order_bucket.",
            "Gold ring=normalized goal bucket; small gold dot=near-order click; cyan route=reused path.",
            "same_point_order_bucket or near_point_order_bucket is visible with a reused route id.",
            "PASS: same/near commands reuse a normalized route bucket and expose route signatures."),
        new(
            MassNavigationShowcaseStepId.TargetAllocation,
            "U8 Large-selection target allocation",
            "Player box-selecting a large army and clicking one destination",
            "Select a 10k army and right-click one destination; verify the order expands into reachable group/unit target slots.",
            "After route reuse and before flow-field movement.",
            "Destination footprint, slot cloud, reachable/blocked/fallback counters, and route reuse key.",
            "Large RTS orders are not one point; they are a placement problem plus a route problem.",
            "Click Select 10k Army, then right-click one destination in the world.",
            "Select 10k Army, then right-click destination.",
            "The actual RTS order expands into 10k logical target slots while the debug view samples the cloud so it remains readable.",
            "selected=10000, slots>=10000, reachable>=10000, route reuse key present, visible markers are capped.",
            "Gold dots=sampled target slots; gold ring=goal footprint; counts in panel prove the full 10k logical allocation.",
            "10k logical slots, visible sampled slots, reachability source, blocked/fallback counts, and route id.",
            "PASS: 10k reachable slots are allocated with blocked=0, fallback=0 and shared route id."),
        new(
            MassNavigationShowcaseStepId.WaypointAuthoring,
            "U10 Waypoint vs pathpoint authoring",
            "Designer building a trade route or planned move chain",
            "Separate editable waypoints from immutable pathpoints returned by one query.",
            "When path preview is copied into gameplay authoring data.",
            "Yellow waypoint plan, green current pathpoints, and faded old pathpoints after an edit.",
            "Business routes are authored intent; pathpoints are disposable query output.",
            "Click Edit Waypoint Plan, left-click start, right-click goal, then click Edit Waypoint Plan again to move the authored midpoint.",
            "Pick start/goal, then click Edit Waypoint Plan.",
            "The editable waypoint chain moves, old pathpoints fade as invalidated output, and regenerated pathpoints remain query-owned.",
            "WaypointsEditable=true, PathPointsImmutable=true, pathpoints>0, and the action text says old pathpoints invalidated.",
            "Yellow handles=editable waypoints; cyan dots=current pathpoints; red faded line=old invalidated path result.",
            "Waypoints remain editable; old pathpoints are invalidated and new pathpoints appear.",
            "PASS: editable waypoint intent stays separate from immutable query-owned pathpoints."),
        new(
            MassNavigationShowcaseStepId.LargeWorldStreaming,
            "U11 64km active-window world",
            "Large-world reviewer",
            "Verify 64km world scale, 256x256 chunks, loaded active window, and not-loaded gap.",
            "Before judging large-world streaming readiness.",
            "Full macro grid concept, active data window, HPA graph sample, NavMesh loaded/notLoaded counts.",
            "Large-world streaming is trustworthy when hot data and streamed-out data are both visible.",
            "Click U11 World and read the loaded vs notLoaded counters.",
            "Click U11 World.",
            "The screen distinguishes active-window loaded data from streamed-out world data.",
            "World=64km, macro=256x256, active window visible, notLoaded count stays explicit.",
            "Green=loaded active window; purple=sample route; gray/notLoaded means streamed-out by design.",
            "World scale, active-window contract, and streamed working-set counters are visible.",
            "PASS: 64km, 256x256 chunks, active-window tiles, HPA route and notLoaded=total-baked are all visible."),
        new(
            MassNavigationShowcaseStepId.TenKFlow,
            "U12 10k commanded flow",
            "RTS player and performance reviewer",
            "Verify a large selection receives a shared command, slots, and flow movement smoke.",
            "After target allocation and before judging FPS budget.",
            "Commanded/moving/settled counts, shared route id, sampled target slots, flow state.",
            "The player order is one click, but the system must prove routing and movement separately.",
            "Click Select 10k Army, then right-click one destination and inspect commanded/moving and slot counts.",
            "Select 10k Army, then right-click destination.",
            "10k units are commanded through shared order data and sampled flow/slot visuals remain readable.",
            "commanded is 10000, flow is enabled, and FPS budget is reported by the Raylib gate.",
            "Cyan=route/flow; gold=sampled slots; counters prove the full logical load.",
            "10k command, slot allocation and movement/flow evidence stay visible together.",
            "PASS: commanded reaches 10k, movement buckets account for the commanded units, and flow is enabled."),
        new(
            MassNavigationShowcaseStepId.StaticObstacleWorld,
            "U13 40k static obstacle world",
            "Map performance reviewer",
            "Verify authored, baked, loaded, and solver-active obstacle counts are separate.",
            "Before testing avoidance or FPS with heavy map data.",
            "40k macro obstacle distribution, active-window subset, solver capacity, activation strategy.",
            "A map can author 40k obstacles while the solver only activates an active-window subset.",
            "Click U13 Obstacles and compare world asset counts with solver-active counts.",
            "Click U13 Obstacles.",
            "The obstacle world asset is visible as sampled buckets and the active solver subset is explicit.",
            "planned/authored/baked/loaded are 40000 while solver-active/capacity is separately reported.",
            "Yellow bucket strokes=sampled authored obstacles; green box=active solver window; bright green crosses=solver-active subset.",
            "40k data chain and active-window runtime activation are visible.",
            "PASS: 40k planned/authored/baked/loaded counts are present and solver-active subset stays within capacity."),
        new(
            MassNavigationShowcaseStepId.PerformanceDebug,
            "U14 80/100 FPS scope",
            "Performance reviewer and gameplay engineer",
            "Verify the current timing evidence scope and production FPS/debug budget.",
            "After all visual layers are understandable.",
            "Frame timing scope, Raylib benchmark status, loaded-data flag, and production thresholds.",
            "FPS is credible when the measured renderer scope and thresholds are visible.",
            "Click U14 FPS and read scope, p95, fullLoadedData, and production status.",
            "Click U14 FPS.",
            "The guide shows Raylib framebuffer benchmark results and threshold status.",
            "rendererScope is raylib_framebuffer_micro_benchmark and production FPS gate is true when thresholds pass.",
            "Timing panel exposes scope; debug visuals stay sampled.",
            "FPS/debug-budget evidence is visible with loaded summary data.",
            "PASS: p95, p99, overlay draw and loaded-data flags pass the Raylib benchmark gate."),
        new(
            MassNavigationShowcaseStepId.DebugVisualBudget,
            "U15 Low-cost debug visuals",
            "Performance reviewer and gameplay engineer",
            "Verify debug visuals are sampled and bounded, not a hidden frame-time tax.",
            "Before leaving debug presentation enabled during UAT.",
            "Overlay counts, visible slot cap, active-window gate, FPS scope, and production status.",
            "Debug is useful only when it explains the system without becoming the system.",
            "Use the debug toggle rows and compare overlay counts with the raylib micro-benchmark evidence.",
            "Click U15 Debug and toggle debug layers.",
            "The overlay explains its own cost and stays sampled, so the tool can be left on during UAT without pretending production FPS is solved.",
            "Overlay item counts stay bounded; FPS line reports the Raylib production budget scope.",
            "Green box=active window; sampled slots prove bounded debug; panel exposes overlay/fps scope.",
            "Overlay item counts stay bounded and the panel exposes the measured renderer budget.",
            "PASS: runtime overlay writes stay zero and Raylib overlay budget passes."),
        new(
            MassNavigationShowcaseStepId.BakeToolQuery,
            "U16 Runtime bake/query update",
            "NavMesh tool user, mod author, and UAT reviewer",
            "In the running game, pick path endpoints, draw a polygon obstacle, bake dirty runtime NavData, and re-query the NavMesh path.",
            "Before trusting runtime map edits or streamed navdata updates.",
            "Full map/minimap picking, full-world NavMesh coverage, authored polygon, dirty chunks, runtime Recast bake revision, and path query.",
            "A reliable bake view must prove the live runtime data changed, not only that an offline tool exported files.",
            "Left/right-click route endpoints, click Draw Poly, left-click obstacle vertices, right-click or Close Poly, then Update NavData.",
            "Pick route endpoints, draw and close a polygon obstacle, then update runtime NavData.",
            "The authored polygon, dirty chunks, changed NavMesh overlay, and refreshed path query are visible without unrelated debug-label clutter.",
            "authoredPolygons>0, dirtyChunks>0, bakedTiles>0, changedTiles>0, navDataRevision increments, and query runs after update.",
            "Green/cyan=NavMesh and route; red=authored runtime obstacle; yellow=dirty chunks.",
            "Runtime polygon authoring, dirty chunk set, Recast tile bake, changed NavMesh triangles/checksum, and refreshed query are visible.",
            "PASS: runtime authored polygon dirties chunks, Recast bakes replacement NavTiles, NavData revision updates, and the visible mesh/query refreshes.")
    };

    private readonly MassNavigationShowcaseConfig _config;
    private readonly MassNavigationShowcaseStep[] _activeSteps;
    private int _currentStepIndex;
    private int _actionRevision;
    private int _waypointEditRevision;
    private int _pathPreviewSessionRevision;
    private MassNavigationGuideSegment[] _activeWindowNavMeshEdges = Array.Empty<MassNavigationGuideSegment>();
    private MassNavigationNavMeshRuntimeCoordinateMapper _navMeshCoordinateMapper;
    private RuntimeWorldPathEndpointCacheKey _runtimeWorldPathEndpointCacheKey;
    private MassNavigationRuntimeWorldPathEndpointResult _runtimeWorldPathEndpoint;
    private bool _hasRuntimeWorldPathEndpoint;

    public MassNavigationShowcaseGuideRuntime()
        : this(new MassNavigationShowcaseConfig())
    {
    }

    public MassNavigationShowcaseGuideRuntime(MassNavigationShowcaseConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _activeSteps = ResolveActiveSteps(_config);
        _currentStepIndex = ResolveInitialStepIndex(_config, _activeSteps);
        LastActionText = _config.FocusedPanel
            ? $"Focused showcase ready: {_config.Title}. Run the visible objective and verify the pass signal."
            : "Guided showcase ready. Start with U1/U2 bake data, then U4 path preview before orders.";
        NavMeshSample = CreateUnavailableNavMeshSample("navmesh_sample_not_bound");
        NavMeshCoverage = MassNavigationNavMeshCoverageGuide.Unavailable;
        ApplyDebugPreset(CurrentStepId);
    }

    public string ShowcaseId => _config.Id;
    public string ShowcaseTitle => _config.Title;
    public string PlayerPerspective => _config.PlayerPerspective;
    public string ModAuthorPerspective => _config.ModAuthorPerspective;
    public bool FocusedPanel => _config.FocusedPanel;
    public string PrimaryActionLabel => ResolvePrimaryActionLabel(CurrentStepId);
    public string OperationMode => ResolveOperationMode(CurrentStepId);
    public string OperationContract => ResolveOperationContract(CurrentStepId);
    public int ActionRevision => _actionRevision;
    public int StepCount => _activeSteps.Length;
    public int CurrentStepIndex => _currentStepIndex;
    public MassNavigationShowcaseStep CurrentStep => _activeSteps[_currentStepIndex];
    public MassNavigationShowcaseStepId CurrentStepId => CurrentStep.Id;
    public string LastActionText { get; private set; }
    public int LastActionOrderDelta { get; private set; }
    public int WaypointEditRevision => _waypointEditRevision;
    public int PathPreviewSessionRevision => _pathPreviewSessionRevision;
    public bool HasPathPreviewStart { get; private set; }
    public bool HasPathPreviewGoal { get; private set; }
    public Vector2 PathPreviewStartWorldCm { get; private set; }
    public Vector2 PathPreviewGoalWorldCm { get; private set; }
    public bool DebugNavMeshEnabled { get; private set; } = true;
    public bool DebugHpaEnabled { get; private set; } = true;
    public bool DebugPathEnabled { get; private set; } = true;
    public bool DebugLayerCostEnabled { get; private set; } = true;
    public bool DebugSlotsEnabled { get; private set; } = true;
    public MassNavigationNavMeshGuideSample NavMeshSample { get; private set; }
    public MassNavigationNavMeshCoverageGuide NavMeshCoverage { get; private set; }
    public MassNavigationRuntimeBakeAuthoringRuntime RuntimeBakeAuthoring { get; } = new();
    public ReadOnlySpan<MassNavigationGuideSegment> ActiveWindowNavMeshEdges => _activeWindowNavMeshEdges;

    public ReadOnlySpan<MassNavigationShowcaseStep> Steps => _activeSteps;

    public bool TryResolveRuntimeBakeWorldPathEndpoints(
        MassNavigationBakeDataDiagnostics? diagnostics,
        NavQueryServiceRegistry? navRegistry,
        NavMeshProfileRegistry? navProfiles,
        out MassNavigationRuntimeWorldPathEndpointResult endpoints)
    {
        RuntimeWorldPathEndpointCacheKey key = RuntimeWorldPathEndpointCacheKey.Create(diagnostics, navRegistry);
        if (_hasRuntimeWorldPathEndpoint && key.Equals(_runtimeWorldPathEndpointCacheKey))
        {
            endpoints = _runtimeWorldPathEndpoint;
            return true;
        }

        if (_hasRuntimeWorldPathEndpoint &&
            key.MatchesWorldAndWindow(_runtimeWorldPathEndpointCacheKey) &&
            MassNavigationRuntimeWorldPathEndpointResolver.TryRevalidate(
                diagnostics,
                navRegistry,
                navProfiles,
                _runtimeWorldPathEndpoint,
                out endpoints))
        {
            _runtimeWorldPathEndpointCacheKey = key;
            _runtimeWorldPathEndpoint = endpoints;
            Bump($"World Path endpoints revalidated from cached live NavMesh pair: {endpoints.StartChunkX},{endpoints.StartChunkY}->{endpoints.GoalChunkX},{endpoints.GoalChunkY}; routeChunks={endpoints.MacroRouteChunkCount}; revision={key.NavDataRevision}.");
            return true;
        }

        if (!MassNavigationRuntimeWorldPathEndpointResolver.TryResolve(
                diagnostics,
                navRegistry,
                navProfiles,
                out endpoints))
        {
            _hasRuntimeWorldPathEndpoint = false;
            _runtimeWorldPathEndpointCacheKey = default;
            _runtimeWorldPathEndpoint = default;
            return false;
        }

        _runtimeWorldPathEndpointCacheKey = key;
        _runtimeWorldPathEndpoint = endpoints;
        _hasRuntimeWorldPathEndpoint = true;
        Bump($"World Path endpoints resolved from live NavMesh: {endpoints.StartChunkX},{endpoints.StartChunkY}->{endpoints.GoalChunkX},{endpoints.GoalChunkY}; routeChunks={endpoints.MacroRouteChunkCount}; componentTiles={endpoints.ComponentTileCount}.");
        return true;
    }

    private readonly record struct RuntimeWorldPathEndpointCacheKey(
        int WorldMinXcm,
        int WorldMinYcm,
        int Columns,
        int Rows,
        int ActiveMinX,
        int ActiveMinY,
        int ActiveMaxX,
        int ActiveMaxY,
        int NavDataRevision)
    {
        public static RuntimeWorldPathEndpointCacheKey Create(
            MassNavigationBakeDataDiagnostics? diagnostics,
            NavQueryServiceRegistry? navRegistry)
        {
            if (diagnostics == null)
            {
                return default;
            }

            return new RuntimeWorldPathEndpointCacheKey(
                diagnostics.WorldMinXCm,
                diagnostics.WorldMinYCm,
                diagnostics.MacroChunkColumns,
                diagnostics.MacroChunkRows,
                diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMinChunkX : 0,
                diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMinChunkY : 0,
                diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMaxChunkX : diagnostics.MacroChunkColumns - 1,
                diagnostics.HasActiveNavMeshWindow ? diagnostics.ActiveNavMeshMaxChunkY : diagnostics.MacroChunkRows - 1,
                navRegistry?.DataRevision ?? 0);
        }

        public bool MatchesWorldAndWindow(RuntimeWorldPathEndpointCacheKey other)
        {
            return WorldMinXcm == other.WorldMinXcm &&
                WorldMinYcm == other.WorldMinYcm &&
                Columns == other.Columns &&
                Rows == other.Rows &&
                ActiveMinX == other.ActiveMinX &&
                ActiveMinY == other.ActiveMinY &&
                ActiveMaxX == other.ActiveMaxX &&
                ActiveMaxY == other.ActiveMaxY;
        }
    }

    public void SetStep(MassNavigationShowcaseStepId stepId)
    {
        for (int i = 0; i < _activeSteps.Length; i++)
        {
            if (_activeSteps[i].Id == stepId)
            {
                _currentStepIndex = i;
                ApplyDebugPreset(stepId);
                Bump($"Step changed to {_activeSteps[i].Title}.");
                return;
            }
        }
    }

    public void NextStep()
    {
        _currentStepIndex = (_currentStepIndex + 1) % _activeSteps.Length;
        ApplyDebugPreset(CurrentStepId);
        Bump($"Step changed to {CurrentStep.Title}.");
    }

    public void PreviousStep()
    {
        _currentStepIndex = (_currentStepIndex + _activeSteps.Length - 1) % _activeSteps.Length;
        ApplyDebugPreset(CurrentStepId);
        Bump($"Step changed to {CurrentStep.Title}.");
    }

    public bool AllowsStep(MassNavigationShowcaseStepId stepId)
    {
        for (int i = 0; i < _activeSteps.Length; i++)
        {
            if (_activeSteps[i].Id == stepId)
            {
                return true;
            }
        }

        return false;
    }

    private static MassNavigationShowcaseStep[] ResolveActiveSteps(MassNavigationShowcaseConfig config)
    {
        if (config.VisibleStepIds.Length == 0)
        {
            return StepCatalog;
        }

        var steps = new List<MassNavigationShowcaseStep>(config.VisibleStepIds.Length);
        var seen = new HashSet<MassNavigationShowcaseStepId>();
        for (int i = 0; i < config.VisibleStepIds.Length; i++)
        {
            string raw = config.VisibleStepIds[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            MassNavigationShowcaseStepId stepId = ParseStepId(raw, config.Id);
            if (!seen.Add(stepId))
            {
                continue;
            }

            steps.Add(FindCatalogStep(stepId));
        }

        if (steps.Count == 0)
        {
            throw new InvalidOperationException($"Mass-nav showcase '{config.Id}' must expose at least one valid VisibleStepIds entry.");
        }

        return steps.ToArray();
    }

    private static int ResolveInitialStepIndex(MassNavigationShowcaseConfig config, MassNavigationShowcaseStep[] activeSteps)
    {
        if (string.IsNullOrWhiteSpace(config.InitialStepId))
        {
            return 0;
        }

        MassNavigationShowcaseStepId initial = ParseStepId(config.InitialStepId, config.Id);
        for (int i = 0; i < activeSteps.Length; i++)
        {
            if (activeSteps[i].Id == initial)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"Mass-nav showcase '{config.Id}' initial step '{config.InitialStepId}' is not included in VisibleStepIds.");
    }

    private static MassNavigationShowcaseStepId ParseStepId(string raw, string showcaseId)
    {
        if (Enum.TryParse(raw, ignoreCase: true, out MassNavigationShowcaseStepId stepId))
        {
            return stepId;
        }

        throw new InvalidOperationException($"Mass-nav showcase '{showcaseId}' references unknown step id '{raw}'.");
    }

    private static MassNavigationShowcaseStep FindCatalogStep(MassNavigationShowcaseStepId stepId)
    {
        for (int i = 0; i < StepCatalog.Length; i++)
        {
            if (StepCatalog[i].Id == stepId)
            {
                return StepCatalog[i];
            }
        }

        throw new InvalidOperationException($"MassNavigationShowcaseStepId '{stepId}' is not present in the catalog.");
    }

    public void RunCurrentStep(MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        MassNavigationShowcaseStepId requestedStep = CurrentStepId;
        switch (requestedStep)
        {
            case MassNavigationShowcaseStepId.PathOnly:
                ArmPathDrivenOperation(MassNavigationShowcaseStepId.PathOnly);
                return;
            case MassNavigationShowcaseStepId.StrategySwitch:
                ArmPathDrivenOperation(MassNavigationShowcaseStepId.StrategySwitch);
                Bump("Strategy comparison armed: left-click one start and right-click one goal. The same query will drive RoadGraph, NavMesh, and Hybrid overlays.");
                return;
            case MassNavigationShowcaseStepId.OrderReuse:
                SetStep(MassNavigationShowcaseStepId.OrderReuse);
                Bump("Order reuse mode armed: click Select Reuse Squad, right-click the same destination twice, then right-click a nearby point to prove cache hit and route id reuse through the formal order chain.");
                return;
            case MassNavigationShowcaseStepId.TargetAllocation:
                SetStep(MassNavigationShowcaseStepId.TargetAllocation);
                Bump("Target allocation mode armed: select or click Select 10k Army, then right-click one destination to run the formal RTS order chain.");
                return;
            case MassNavigationShowcaseStepId.TenKFlow:
                SetStep(MassNavigationShowcaseStepId.TenKFlow);
                Bump("10k flow mode armed: select or click Select 10k Army, then right-click one destination to command movement through slots and flow.");
                return;
            case MassNavigationShowcaseStepId.WaypointAuthoring:
                ArmPathDrivenOperation(MassNavigationShowcaseStepId.WaypointAuthoring);
                return;
            case MassNavigationShowcaseStepId.BakeToolQuery:
                SetStep(MassNavigationShowcaseStepId.BakeToolQuery);
                Bump("Runtime bake/query update surface opened: pick a route, draw a polygon obstacle, close it, then Update NavData to bake dirty NavMesh tiles and re-run the NavMesh query.");
                return;
            default:
                SetStep(requestedStep);
                Bump($"{ResolveOperationMode(requestedStep)} operation prepared: {CurrentStep.PlayerInput} Verify the highlighted live debug layers and pass signal.");
                return;
        }
    }

    public static string ResolvePrimaryActionLabel(MassNavigationShowcaseStepId stepId)
    {
        return stepId switch
        {
            MassNavigationShowcaseStepId.VisualHeightmapBake => "Bake VHTM Window",
            MassNavigationShowcaseStepId.LogicHeightmapBake => "Inspect Logic Bake",
            MassNavigationShowcaseStepId.LayerAreaEditor => "Open Layer Tool",
            MassNavigationShowcaseStepId.NavMeshBake => "Inspect NavMesh Tile",
            MassNavigationShowcaseStepId.PathOnly => "Pick Path Preview",
            MassNavigationShowcaseStepId.WorldHpa => "Pick HPA Route",
            MassNavigationShowcaseStepId.StrategySwitch => "Pick Strategy Route",
            MassNavigationShowcaseStepId.OrderReuse => "Select Reuse Squad",
            MassNavigationShowcaseStepId.TargetAllocation => "Select 10k Army",
            MassNavigationShowcaseStepId.LayerCosts => "Compare Layer Costs",
            MassNavigationShowcaseStepId.WaypointAuthoring => "Edit Waypoint Plan",
            MassNavigationShowcaseStepId.LargeWorldStreaming => "Show Active Window",
            MassNavigationShowcaseStepId.TenKFlow => "Select 10k Army",
            MassNavigationShowcaseStepId.StaticObstacleWorld => "Inspect 40k Obstacles",
            MassNavigationShowcaseStepId.PerformanceDebug => "Open FPS Budget",
            MassNavigationShowcaseStepId.DebugVisualBudget => "Toggle Debug Budget",
            MassNavigationShowcaseStepId.BakeToolQuery => "Update NavData",
            _ => "Run Operation"
        };
    }

    public static string ResolveOperationMode(MassNavigationShowcaseStepId stepId)
    {
        return stepId switch
        {
            MassNavigationShowcaseStepId.VisualHeightmapBake or
            MassNavigationShowcaseStepId.LogicHeightmapBake or
            MassNavigationShowcaseStepId.LayerAreaEditor or
            MassNavigationShowcaseStepId.NavMeshBake or
            MassNavigationShowcaseStepId.LayerCosts => "Editor tool",
            MassNavigationShowcaseStepId.BakeToolQuery => "Runtime NavData tool",
            MassNavigationShowcaseStepId.PerformanceDebug or
            MassNavigationShowcaseStepId.DebugVisualBudget => "Diagnostics tool",
            _ => "Playable RTS"
        };
    }

    public static string ResolveOperationContract(MassNavigationShowcaseStepId stepId)
    {
        return stepId switch
        {
            MassNavigationShowcaseStepId.VisualHeightmapBake =>
                "Input: visual heightmap asset. Output: LogicHeightmap sample, .ntil tile, active-window bake diagnostics.",
            MassNavigationShowcaseStepId.LogicHeightmapBake =>
                "Input: vtxm/vhtm/quad/hex sources. Output: one LogicHeightmap contract before NavMesh bake.",
            MassNavigationShowcaseStepId.LayerAreaEditor =>
                "Input: layer/area/cost authoring. Output: colored mountain, river, NoFly, blocked and high-cost regions.",
            MassNavigationShowcaseStepId.NavMeshBake =>
                "Input: loaded NavTile. Output: walkable triangles, blocked source, high-cost source, portal clearance, agent radius.",
            MassNavigationShowcaseStepId.PathOnly =>
                "Input: left-click start and right-click goal. Output: highlighted path/corridor/pathpoints with no move order submitted.",
            MassNavigationShowcaseStepId.WorldHpa =>
                "Input: long-distance route query. Output: numbered macro chunks, active-window HPA graph route, portal crossings.",
            MassNavigationShowcaseStepId.StrategySwitch =>
                "Input: one start/goal query. Output: RoadGraph, NavMesh, and Hybrid candidates with selected profile costs.",
            MassNavigationShowcaseStepId.OrderReuse =>
                "Input: same and near destination orders. Output: normalized route bucket, cache hit, reused route id.",
            MassNavigationShowcaseStepId.TargetAllocation =>
                "Input: selected 10k army plus one right-click destination. Output: 10k reachable formation slots and sampled slot cloud.",
            MassNavigationShowcaseStepId.LayerCosts =>
                "Input: unit movement profiles. Output: ground, water, air, mountain layer reachability and area costs.",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                "Input: editable waypoint chain. Output: regenerated immutable pathpoints while waypoints remain authoring data.",
            MassNavigationShowcaseStepId.LargeWorldStreaming =>
                "Input: 64km world. Output: 256x256 macro grid, loaded active window, explicit streamed-out counts.",
            MassNavigationShowcaseStepId.TenKFlow =>
                "Input: selected 10k army plus one right-click order. Output: shared command, slots, flow movement counters and sampled debug visuals.",
            MassNavigationShowcaseStepId.StaticObstacleWorld =>
                "Input: 40k static obstacle data. Output: authored/baked/loaded counts and active solver subset.",
            MassNavigationShowcaseStepId.PerformanceDebug =>
                "Input: playable runtime frame timing. Output: FPS scope, p95/p99, loaded-data flag, production threshold.",
            MassNavigationShowcaseStepId.DebugVisualBudget =>
                "Input: debug layer toggles. Output: sampled overlays with bounded draw counts and budget text.",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                "Input: runtime route picks plus authored obstacle polygon. Output: dirty chunks, Recast baked NavTiles, refreshed mesh/path overlays, and triangle/checksum change evidence.",
            _ => "Input: runtime operation. Output: visible pass signal and diagnostics."
        };
    }

    public void ToggleNavMesh()
    {
        DebugNavMeshEnabled = !DebugNavMeshEnabled;
        Bump($"NavMesh debug layer {(DebugNavMeshEnabled ? "on" : "off")}.");
    }

    public void ToggleHpa()
    {
        DebugHpaEnabled = !DebugHpaEnabled;
        Bump($"HPA debug layer {(DebugHpaEnabled ? "on" : "off")}.");
    }

    public void TogglePath()
    {
        DebugPathEnabled = !DebugPathEnabled;
        Bump($"Path/corridor debug layer {(DebugPathEnabled ? "on" : "off")}.");
    }

    public void ToggleLayerCost()
    {
        DebugLayerCostEnabled = !DebugLayerCostEnabled;
        Bump($"Layer/cost debug layer {(DebugLayerCostEnabled ? "on" : "off")}.");
    }

    public void ToggleSlots()
    {
        DebugSlotsEnabled = !DebugSlotsEnabled;
        Bump($"Target-slot debug layer {(DebugSlotsEnabled ? "on" : "off")}.");
    }

    private void ApplyDebugPreset(MassNavigationShowcaseStepId stepId)
    {
        DebugNavMeshEnabled = false;
        DebugHpaEnabled = false;
        DebugPathEnabled = false;
        DebugLayerCostEnabled = false;
        DebugSlotsEnabled = false;

        switch (stepId)
        {
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
                DebugNavMeshEnabled = true;
                DebugLayerCostEnabled = true;
                return;
            case MassNavigationShowcaseStepId.LayerAreaEditor:
            case MassNavigationShowcaseStepId.LayerCosts:
                DebugNavMeshEnabled = true;
                DebugLayerCostEnabled = true;
                return;
            case MassNavigationShowcaseStepId.NavMeshBake:
                DebugNavMeshEnabled = true;
                DebugPathEnabled = true;
                DebugLayerCostEnabled = true;
                return;
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                DebugHpaEnabled = true;
                DebugPathEnabled = true;
                return;
            case MassNavigationShowcaseStepId.PathOnly:
            case MassNavigationShowcaseStepId.StrategySwitch:
            case MassNavigationShowcaseStepId.OrderReuse:
            case MassNavigationShowcaseStepId.WaypointAuthoring:
                DebugPathEnabled = true;
                return;
            case MassNavigationShowcaseStepId.TargetAllocation:
            case MassNavigationShowcaseStepId.TenKFlow:
                DebugPathEnabled = true;
                DebugSlotsEnabled = true;
                return;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
                DebugHpaEnabled = true;
                return;
            case MassNavigationShowcaseStepId.PerformanceDebug:
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                DebugSlotsEnabled = true;
                return;
            case MassNavigationShowcaseStepId.BakeToolQuery:
                DebugNavMeshEnabled = true;
                DebugPathEnabled = true;
                return;
        }
    }

    public void RunPathPreview(MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        int before = simulation.CommandCountFrame + simulation.PendingCommandCount;
        LastActionOrderDelta = 0;
        ClearPathPreviewPicks();
        SetStep(MassNavigationShowcaseStepId.PathOnly);
        int after = simulation.CommandCountFrame + simulation.PendingCommandCount;
        LastActionOrderDelta = after - before;
        Bump("Path preview mode armed: left-click a start point on the ground or minimap, then right-click a goal point. This will query PathService/NavMesh and submit no unit order.");
    }

    public void ArmPathDrivenOperation(MassNavigationShowcaseStepId stepId)
    {
        if (!IsPathDrivenStep(stepId))
        {
            throw new InvalidOperationException($"Step '{stepId}' is not a path-driven operation.");
        }

        LastActionOrderDelta = 0;
        ClearPathPreviewPicks();
        SetStep(stepId);
        Bump(stepId switch
        {
            MassNavigationShowcaseStepId.WorldHpa =>
                "HPA route picking armed: left-click a start chunk on the ground or minimap and right-click a far goal. The same PathService query updates route chunks, portal labels, and active-window proof.",
            MassNavigationShowcaseStepId.StrategySwitch =>
                "Strategy route picking armed: left-click start and right-click goal on the ground or minimap. RoadGraph, NavMesh, and Hybrid overlays will compare the same route intent.",
            MassNavigationShowcaseStepId.WaypointAuthoring =>
                "Waypoint authoring armed: left-click start and right-click goal on the ground or minimap, then click Edit Waypoint Plan to move the editable midpoint while pathpoints remain query output.",
            MassNavigationShowcaseStepId.BakeToolQuery =>
                "Runtime NavData route picking armed: left-click start and right-click goal on the ground or minimap, then draw a polygon obstacle and Update NavData.",
            _ =>
                "Path preview mode armed: left-click a start point on the ground or minimap, then right-click a goal point. This will query PathService/NavMesh and submit no unit order."
        });
    }

    public bool IsRuntimeObstacleAuthoringActive()
    {
        return RuntimeBakeAuthoring.ObstacleAuthoringArmed &&
            (CurrentStepId == MassNavigationShowcaseStepId.BakeToolQuery ||
                CurrentStepId == MassNavigationShowcaseStepId.StaticObstacleWorld);
    }

    public void ArmRuntimeObstacleAuthoring()
    {
        SetStep(MassNavigationShowcaseStepId.BakeToolQuery);
        RuntimeBakeAuthoring.ArmObstaclePolygon();
        DebugNavMeshEnabled = true;
        DebugPathEnabled = true;
        DebugHpaEnabled = false;
        DebugLayerCostEnabled = false;
        DebugSlotsEnabled = false;
        Bump("Runtime obstacle polygon armed: left-click vertices on the ground or minimap; right-click or Close Poly to finish. Route picking is paused while polygon drawing is armed.");
    }

    public void CancelRuntimeObstacleAuthoring()
    {
        RuntimeBakeAuthoring.CancelObstaclePolygonDraft();
        Bump("Runtime obstacle polygon drawing cancelled; route picking is active again.");
    }

    public void RecordRuntimeObstaclePoint(Vector2 worldCm, MassNavigationRuntimeDirtyChunk dirtyChunk)
    {
        SetStep(MassNavigationShowcaseStepId.BakeToolQuery);
        Bump($"Runtime obstacle point added at ({worldCm.X:0},{worldCm.Y:0}) cm; draftPoints={RuntimeBakeAuthoring.DraftPointCount}; dirtyChunk={dirtyChunk.X},{dirtyChunk.Y}.");
    }

    public void RecordRuntimeObstacleAuthoringFailure(Vector2 worldCm, string reason)
    {
        SetStep(MassNavigationShowcaseStepId.BakeToolQuery);
        Bump($"Runtime obstacle authoring rejected at ({worldCm.X:0},{worldCm.Y:0}) cm: {reason}.");
    }

    public void RecordRuntimeObstacleClosed()
    {
        SetStep(MassNavigationShowcaseStepId.BakeToolQuery);
        Bump($"Runtime obstacle polygon closed: authoredPolygons={RuntimeBakeAuthoring.AuthoredPolygonCount}; dirtyChunks={RuntimeBakeAuthoring.DirtyChunkCount}. Click Update NavData to bake dirty runtime tiles and refresh the path query.");
    }

    public void RecordRuntimeNavDataUpdateResult(MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics)
    {
        SetStep(MassNavigationShowcaseStepId.BakeToolQuery);
        RefreshRuntimeBakedNavMeshEdges(diagnostics);
        Bump($"Runtime NavData update {diagnostics.Status}: revision={diagnostics.NavDataRevision}; dirtyChunks={diagnostics.DirtyChunkCount}; bakedTiles={diagnostics.BakedTileCount}; changedTiles={diagnostics.ChangedTileCount}; triangles={diagnostics.BeforeTriangleCount}->{diagnostics.AfterTriangleCount}; query={diagnostics.QueryStatusAfterUpdate}/{diagnostics.QueryPathPointCount}; source={diagnostics.UpdateSource}.");
    }

    public void RecordPathPreviewPick(string pickKind, Vector2 worldCm, bool otherEndpointReady)
    {
        LastActionOrderDelta = 0;
        if (string.Equals(pickKind, "start", StringComparison.OrdinalIgnoreCase))
        {
            PathPreviewStartWorldCm = worldCm;
            HasPathPreviewStart = true;
        }
        else if (string.Equals(pickKind, "goal", StringComparison.OrdinalIgnoreCase))
        {
            PathPreviewGoalWorldCm = worldCm;
            HasPathPreviewGoal = true;
        }

        MassNavigationShowcaseStepId stepId = CurrentStepId;
        if (!IsPathDrivenStep(stepId))
        {
            stepId = MassNavigationShowcaseStepId.PathOnly;
        }

        SetStep(stepId);
        string next = otherEndpointReady
            ? "running query"
            : (string.Equals(pickKind, "start", StringComparison.OrdinalIgnoreCase)
                ? "right-click a goal point on the ground or minimap"
                : "left-click a start point on the ground or minimap");
        Bump($"Path preview {pickKind} picked at ({worldCm.X:0}, {worldCm.Y:0}) cm; {next}.");
    }

    public void RecordPathPreviewPickFailure(Vector2 worldCm, string reason)
    {
        LastActionOrderDelta = 0;
        MassNavigationShowcaseStepId stepId = CurrentStepId;
        if (!IsPathDrivenStep(stepId))
        {
            stepId = MassNavigationShowcaseStepId.PathOnly;
        }

        SetStep(stepId);
        Bump($"Path preview pick rejected at ({worldCm.X:0}, {worldCm.Y:0}) cm: {reason}.");
    }

    public void RecordPathPreviewQueryResult(
        Vector2 startWorldCm,
        Vector2 goalWorldCm,
        int orderDelta,
        MassNavigationPathOnlyQueryDiagnostics query)
    {
        LastActionOrderDelta = orderDelta;
        PathPreviewStartWorldCm = startWorldCm;
        PathPreviewGoalWorldCm = goalWorldCm;
        HasPathPreviewStart = true;
        HasPathPreviewGoal = true;
        MassNavigationShowcaseStepId stepId = CurrentStepId;
        if (!IsPathDrivenStep(stepId))
        {
            stepId = MassNavigationShowcaseStepId.PathOnly;
        }

        SetStep(stepId);
        Bump($"{ResolveOperationMode(stepId)} query {query.Status}: start=({startWorldCm.X:0},{startWorldCm.Y:0}) goal=({goalWorldCm.X:0},{goalWorldCm.Y:0}) pathpoints={query.PathPointCount} orderDelta={LastActionOrderDelta}.");
    }

    private void ClearPathPreviewPicks()
    {
        PathPreviewStartWorldCm = Vector2.Zero;
        PathPreviewGoalWorldCm = Vector2.Zero;
        HasPathPreviewStart = false;
        HasPathPreviewGoal = false;
        _pathPreviewSessionRevision++;
    }

    public void RecordWaypointEditResult(
        Vector2 authoredMidpointWorldCm,
        int orderDelta,
        MassNavigationWaypointPathDiagnostics waypointPath)
    {
        LastActionOrderDelta = orderDelta;
        _waypointEditRevision = Math.Max(_waypointEditRevision + 1, waypointPath.EditRevision);
        SetStep(MassNavigationShowcaseStepId.WaypointAuthoring);
        Bump(
            $"Waypoint edited from user click at ({authoredMidpointWorldCm.X:0},{authoredMidpointWorldCm.Y:0}) cm: waypoints={waypointPath.WaypointCount} pathpoints={waypointPath.PathPointCount} oldPathpointsInvalidated={waypointPath.InvalidatedPathPointCount} editRevision={waypointPath.EditRevision} orderDelta={LastActionOrderDelta}.");
    }

    public void RecordWaypointEditFailure(Vector2 authoredMidpointWorldCm, string reason)
    {
        LastActionOrderDelta = 0;
        SetStep(MassNavigationShowcaseStepId.WaypointAuthoring);
        Bump($"Waypoint edit rejected at ({authoredMidpointWorldCm.X:0},{authoredMidpointWorldCm.Y:0}) cm: {reason}. Pick start/goal first, then left-click the editable midpoint.");
    }

    public void RunTargetAllocationProbe(MassNavigationSimulationRuntime simulation, int requestedSlots)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        SetStep(MassNavigationShowcaseStepId.TargetAllocation);
        Bump($"Target allocation armed for {Math.Max(1, requestedSlots)} units: select or click Select 10k Army, then right-click one destination. Allocation proof must come from OrderBuffer and MassNavigationOrderBridgeSystem.");
    }

    public void RecordLargeSelectionPrepared(MassNavigationShowcaseStepId stepId, int selectedCount)
    {
        SetStep(stepId);
        Bump($"10k army selected through SelectionRuntime: selected={selectedCount}. Right-click one destination to submit the formal move order and inspect slots/flow.");
    }

    public void RecordOrderReuseSelectionPrepared(int selectedCount)
    {
        SetStep(MassNavigationShowcaseStepId.OrderReuse);
        Bump($"Reuse squad selected through SelectionRuntime: selected={selectedCount}. Right-click one destination twice, then a nearby point; cache hit and reused route id should update from real orders.");
    }

    public void RecordLargeSelectionPreparationFailed(MassNavigationShowcaseStepId stepId, string reason)
    {
        SetStep(stepId);
        Bump($"10k army selection could not be prepared: {reason}.");
    }

    public void RunWaypointEditProbe(MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.AcceptanceDiagnostics.PathOnlyQuery.Available)
        {
            SetStep(MassNavigationShowcaseStepId.WaypointAuthoring);
            Bump("Waypoint edit armed: left-click a new midpoint handle in the world. This edits waypoint intent and re-queries immutable pathpoints.");
            return;
        }

        SetStep(MassNavigationShowcaseStepId.WaypointAuthoring);
        Bump("Waypoint edit needs a route first: left-click start, right-click goal, then left-click the authored midpoint to regenerate pathpoints.");
    }

    public static bool IsPathDrivenStep(MassNavigationShowcaseStepId stepId)
    {
        return stepId == MassNavigationShowcaseStepId.PathOnly ||
            stepId == MassNavigationShowcaseStepId.WorldHpa ||
            stepId == MassNavigationShowcaseStepId.StrategySwitch ||
            stepId == MassNavigationShowcaseStepId.WaypointAuthoring ||
            stepId == MassNavigationShowcaseStepId.BakeToolQuery;
    }

    private void RefreshRuntimeBakedNavMeshEdges(MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics)
    {
        IReadOnlyList<NavTile> tiles = RuntimeBakeAuthoring.LastVisibleNavMeshTiles.Count > 0
            ? RuntimeBakeAuthoring.LastVisibleNavMeshTiles
            : RuntimeBakeAuthoring.LastBakedTiles;
        if (tiles.Count == 0)
        {
            return;
        }

        var activeEdges = new List<MassNavigationGuideSegment>(Math.Min(
            MaxActiveWindowNavMeshEdges,
            Math.Max(0, tiles.Count) * MaxRuntimeNavMeshTrianglesPerTile * 3));
        if (!_navMeshCoordinateMapper.Available)
        {
            _activeWindowNavMeshEdges = Array.Empty<MassNavigationGuideSegment>();
            return;
        }

        int loadedTiles = 0;
        for (int i = 0;
             i < tiles.Count && loadedTiles < MaxRuntimeNavMeshTiles && activeEdges.Count < MaxActiveWindowNavMeshEdges;
             i++)
        {
            AppendTriangleEdges(
                tiles[i],
                activeEdges,
                MaxActiveWindowNavMeshEdges,
                _navMeshCoordinateMapper,
                MaxRuntimeNavMeshTrianglesPerTile);
            loadedTiles++;
        }

        _activeWindowNavMeshEdges = activeEdges.ToArray();

        NavTile primary = tiles[0];
        for (int i = 0; i < tiles.Count; i++)
        {
            NavTile tile = tiles[i];
            if (tile.TileId.ChunkX == NavMeshSample.ChunkX &&
                tile.TileId.ChunkY == NavMeshSample.ChunkY &&
                tile.TileId.Layer == NavMeshSample.Layer)
            {
                primary = tile;
                break;
            }
        }

        if (NavMeshSample.Available)
        {
            NavMeshSample = NavMeshSample with
            {
                Source = diagnostics.UpdateSource,
                Layer = primary.TileId.Layer,
                ChunkX = primary.TileId.ChunkX,
                ChunkY = primary.TileId.ChunkY,
                TriangleCount = primary.TriangleCount,
                PortalCount = primary.Portals.Length,
                FirstAreaId = primary.TriAreaIds.Length > 0 ? primary.TriAreaIds[0] : 0,
                TriangleEdges = BuildTriangleEdges(primary, maxTriangles: 28, _navMeshCoordinateMapper),
                Portals = BuildPortalSegments(primary, _navMeshCoordinateMapper)
            };
        }
    }

    public void BindNavMeshSample(
        MassNavigationBakeDataDiagnostics? diagnostics,
        NavBakeDiagnosticsDocument? navBakeDiagnostics,
        IVirtualFileSystem? vfs,
        IEnumerable<string>? loadedModIds,
        string mapId)
    {
        if (diagnostics == null || navBakeDiagnostics == null || vfs == null)
        {
            NavMeshSample = CreateUnavailableNavMeshSample("navmesh_diagnostics_or_vfs_missing");
            NavMeshCoverage = MassNavigationNavMeshCoverageGuide.Unavailable;
            _activeWindowNavMeshEdges = Array.Empty<MassNavigationGuideSegment>();
            _navMeshCoordinateMapper = default;
            return;
        }

        _navMeshCoordinateMapper = default;
        NavMeshCoverage = CreateNavMeshCoverage(navBakeDiagnostics);

        NavBakeLayerProfileSummary? profile = ResolveGroundLightProfile(navBakeDiagnostics);
        if (profile == null)
        {
            NavMeshSample = CreateUnavailableNavMeshSample("ground_light_profile_not_baked");
            _activeWindowNavMeshEdges = Array.Empty<MassNavigationGuideSegment>();
            _navMeshCoordinateMapper = default;
            return;
        }

        int chunkX = ResolveSampleChunk(
            navBakeDiagnostics.ActiveWindowMinChunkX,
            navBakeDiagnostics.ActiveWindowMaxChunkX,
            diagnostics.MacroChunkColumns);
        int chunkY = ResolveSampleChunk(
            navBakeDiagnostics.ActiveWindowMinChunkY,
            navBakeDiagnostics.ActiveWindowMaxChunkY,
            diagnostics.MacroChunkRows);

        if (!TryLoadTile(vfs, loadedModIds, mapId, profile.Layer, profile.ProfileId, chunkX, chunkY, out NavTile? tile, out string source))
        {
            NavMeshSample = CreateUnavailableNavMeshSample($"navtile_missing:{chunkX},{chunkY},layer={profile.Layer},profile={profile.ProfileId}");
            _activeWindowNavMeshEdges = Array.Empty<MassNavigationGuideSegment>();
            _navMeshCoordinateMapper = default;
            return;
        }

        _navMeshCoordinateMapper = CreateNavMeshCoordinateMapper(diagnostics, tile!);
        MassNavigationGuideSegment[] triangleEdges = BuildTriangleEdges(
            tile!,
            maxTriangles: 28,
            _navMeshCoordinateMapper);
        MassNavigationGuideSegment[] portals = BuildPortalSegments(tile!, _navMeshCoordinateMapper);
        LogicCellCounts logicCounts = CountLogicCells(navBakeDiagnostics.SourceMapPath, chunkX, chunkY);
        _activeWindowNavMeshEdges = BuildActiveWindowTriangleEdges(
            vfs,
            loadedModIds,
            mapId,
            navBakeDiagnostics,
            profile,
            _navMeshCoordinateMapper);
        int minClearance = int.MaxValue;
        for (int i = 0; i < portals.Length; i++)
        {
            if (portals[i].ClearanceCm > 0)
            {
                minClearance = Math.Min(minClearance, portals[i].ClearanceCm);
            }
        }

        NavMeshSample = new MassNavigationNavMeshGuideSample(
            Available: true,
            Source: source,
            LogicHeightmapSource: navBakeDiagnostics.SourceMapPath,
            Layer: profile.Layer,
            ProfileId: profile.ProfileId,
            ChunkX: chunkX,
            ChunkY: chunkY,
            TriangleCount: tile!.TriangleCount,
            PortalCount: tile.Portals.Length,
            FirstAreaId: tile.TriAreaIds.Length > 0 ? tile.TriAreaIds[0] : 0,
            MinPortalClearanceCm: minClearance == int.MaxValue ? 0 : minClearance,
            AgentRadiusCm: ResolveAgentRadiusCm(navBakeDiagnostics, profile.ProfileId),
            BlockedCellCount: logicCounts.Blocked,
            HighCostCellCount: logicCounts.HighCost,
            WaterCellCount: logicCounts.Water,
            RampCellCount: logicCounts.Ramp,
            AreaLegend: "0 Default, 1 Road, 2 Forest, 3 MountainSlope, 4 ShallowWater, 5 DeepWater, 6 NoFlyZone",
            LayerLegend: BuildLayerLegend(navBakeDiagnostics),
            BlockedSource: "LogicHeightmap blocked flags + recast walkability; active tile sample renders walkable triangles and sampled blocked/high-cost source counts",
            OffMeshLinkSource: "border portals loaded; authored off-mesh links not present in this smoke",
            TriangleEdges: triangleEdges,
            Portals: portals);
        Bump($"NavMesh sample bound: tile={chunkX},{chunkY} layer={profile.Layer} profile={profile.ProfileId} triangles={tile.TriangleCount} portals={tile.Portals.Length}.");
    }

    private static MassNavigationNavMeshRuntimeCoordinateMapper CreateNavMeshCoordinateMapper(
        MassNavigationBakeDataDiagnostics diagnostics,
        NavTile sampleTile)
    {
        if (!string.IsNullOrWhiteSpace(diagnostics.LogicHeightmapSource) &&
            File.Exists(diagnostics.LogicHeightmapSource))
        {
            try
            {
                using LogicHeightmapFileReader reader = LogicHeightmapFileReader.Open(diagnostics.LogicHeightmapSource);
                if (MassNavigationNavMeshRuntimeCoordinateMapper.TryCreate(
                        diagnostics,
                        reader,
                        out MassNavigationNavMeshRuntimeCoordinateMapper mapper))
                {
                    return mapper;
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(diagnostics, sampleTile);
    }

    private static MassNavigationNavMeshCoverageGuide CreateNavMeshCoverage(NavBakeDiagnosticsDocument document)
    {
        int worldChunkCount = document.WorldChunkCount > 0
            ? document.WorldChunkCount
            : document.TargetChunkCount;
        int activeWindowChunkCount = document.ActiveWindowChunkCount > 0
            ? document.ActiveWindowChunkCount
            : CountActiveWindowChunks(document);
        return new MassNavigationNavMeshCoverageGuide(
            Available: true,
            IsPartialCoverage: document.IsPartialCoverage || document.TargetChunkCount < worldChunkCount,
            TargetChunkCount: Math.Max(0, document.TargetChunkCount),
            WorldChunkCount: Math.Max(0, worldChunkCount),
            ActiveWindowMinChunkX: document.ActiveWindowMinChunkX,
            ActiveWindowMinChunkY: document.ActiveWindowMinChunkY,
            ActiveWindowMaxChunkX: document.ActiveWindowMaxChunkX,
            ActiveWindowMaxChunkY: document.ActiveWindowMaxChunkY,
            ActiveWindowChunkCount: activeWindowChunkCount,
            LayerCount: Math.Max(0, document.LayerCount),
            ProfileCount: Math.Max(0, document.ProfileCount),
            TotalExpectedTileBakes: Math.Max(0, document.TotalExpectedTileBakes),
            TotalBakedTiles: Math.Max(0, document.TotalBakedTiles));
    }

    private static int CountActiveWindowChunks(NavBakeDiagnosticsDocument document)
    {
        if (document.ActiveWindowMinChunkX < 0 ||
            document.ActiveWindowMinChunkY < 0 ||
            document.ActiveWindowMaxChunkX < document.ActiveWindowMinChunkX ||
            document.ActiveWindowMaxChunkY < document.ActiveWindowMinChunkY)
        {
            return 0;
        }

        int width = checked(document.ActiveWindowMaxChunkX - document.ActiveWindowMinChunkX + 1);
        int height = checked(document.ActiveWindowMaxChunkY - document.ActiveWindowMinChunkY + 1);
        return checked(width * height);
    }

    private static int ResolveSampleChunk(int windowMin, int windowMax, int chunkCount)
    {
        int maxChunk = Math.Max(0, chunkCount - 1);
        if (windowMin >= 0 && windowMax >= windowMin)
        {
            return Math.Clamp(windowMin + ((windowMax - windowMin) / 2), 0, maxChunk);
        }

        return Math.Clamp(chunkCount / 2, 0, maxChunk);
    }

    private static EntitySelectionScratch BuildSelectionScratch(MassNavigationSimulationRuntime simulation, int count)
    {
        if (count <= 0)
        {
            return new EntitySelectionScratch(Array.Empty<Arch.Core.Entity>(), 0);
        }

        var entities = new Arch.Core.Entity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = simulation.AgentState.ControllableAgents[i];
        }

        return new EntitySelectionScratch(entities, count);
    }

    private static Vector2 ResolveDefaultOrderDestination(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (query.GoalWorldCm != Vector2.Zero)
        {
            return query.GoalWorldCm;
        }

        return new Vector2(
            simulation.SolverWindowCenterXCm + MathF.Max(2_000f, simulation.SolverWindowWidthCm * 0.35f),
            simulation.SolverWindowCenterYCm + MathF.Max(2_000f, simulation.SolverWindowHeightCm * 0.25f));
    }

    private static NavBakeLayerProfileSummary? ResolveGroundLightProfile(NavBakeDiagnosticsDocument document)
    {
        for (int i = 0; i < document.LayerProfiles.Count; i++)
        {
            NavBakeLayerProfileSummary profile = document.LayerProfiles[i];
            if (profile.BakedTiles > 0 &&
                string.Equals(profile.ProfileId, "GroundLight", StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        for (int i = 0; i < document.LayerProfiles.Count; i++)
        {
            if (document.LayerProfiles[i].BakedTiles > 0)
            {
                return document.LayerProfiles[i];
            }
        }

        return null;
    }

    private static bool TryLoadTile(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string mapId,
        int layer,
        string profileId,
        int chunkX,
        int chunkY,
        out NavTile? tile,
        out string source)
    {
        tile = null;
        source = string.Empty;
        string relative = NavAssetPaths.GetNavTileRelativePath(mapId, layer, profileId, chunkX, chunkY);
        if (TryLoadTileUri(vfs, $"Core:{relative}", out tile))
        {
            source = $"Core:{relative}";
            return true;
        }

        if (loadedModIds != null)
        {
            foreach (string modId in loadedModIds)
            {
                if (string.IsNullOrWhiteSpace(modId))
                {
                    continue;
                }

                string uri = $"{modId}:{relative}";
                if (TryLoadTileUri(vfs, uri, out tile))
                {
                    source = uri;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryLoadTileUri(IVirtualFileSystem vfs, string uri, out NavTile? tile)
    {
        tile = null;
        if (!vfs.TryResolveFullPath(uri, out string fullPath) || !File.Exists(fullPath))
        {
            return false;
        }

        using Stream stream = vfs.GetStream(uri);
        tile = NavTileBinary.Read(stream);
        return true;
    }

    private static MassNavigationGuideSegment[] BuildActiveWindowTriangleEdges(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string mapId,
        NavBakeDiagnosticsDocument diagnostics,
        NavBakeLayerProfileSummary profile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        if (!mapper.Available)
        {
            return Array.Empty<MassNavigationGuideSegment>();
        }

        int minX = diagnostics.ActiveWindowMinChunkX >= 0 ? diagnostics.ActiveWindowMinChunkX : 0;
        int minY = diagnostics.ActiveWindowMinChunkY >= 0 ? diagnostics.ActiveWindowMinChunkY : 0;
        int maxX = diagnostics.ActiveWindowMaxChunkX >= minX ? diagnostics.ActiveWindowMaxChunkX : minX;
        int maxY = diagnostics.ActiveWindowMaxChunkY >= minY ? diagnostics.ActiveWindowMaxChunkY : minY;
        (int X, int Y)[] sampleTiles = BuildNavMeshOverviewTileCoordinates(
            minX,
            minY,
            maxX,
            maxY,
            MaxNavMeshOverviewTiles);
        var edges = new List<MassNavigationGuideSegment>(Math.Min(
            MaxActiveWindowNavMeshEdges,
            sampleTiles.Length * MaxNavMeshOverviewTrianglesPerTile * 3));
        int loadedTiles = 0;
        for (int i = 0; i < sampleTiles.Length && edges.Count < MaxActiveWindowNavMeshEdges; i++)
        {
            (int x, int y) = sampleTiles[i];
            if (!TryLoadTile(vfs, loadedModIds, mapId, profile.Layer, profile.ProfileId, x, y, out NavTile? tile, out _))
            {
                continue;
            }

            AppendTriangleEdges(
                tile!,
                edges,
                MaxActiveWindowNavMeshEdges,
                mapper,
                MaxNavMeshOverviewTrianglesPerTile);
            loadedTiles++;
        }

        return edges.ToArray();
    }

    private static (int X, int Y)[] BuildNavMeshOverviewTileCoordinates(
        int minX,
        int minY,
        int maxX,
        int maxY,
        int maxTiles)
    {
        if (maxTiles <= 0 || maxX < minX || maxY < minY)
        {
            return Array.Empty<(int X, int Y)>();
        }

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0)
        {
            return Array.Empty<(int X, int Y)>();
        }

        long total = (long)width * height;
        var result = new List<(int X, int Y)>(Math.Min(maxTiles, (int)Math.Min(total, int.MaxValue)));
        var keys = new HashSet<long>();
        if (total <= maxTiles)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    AddOverviewTile(result, keys, x, y, maxTiles);
                }
            }

            return result.ToArray();
        }

        int axisSamples = Math.Max(1, (int)MathF.Floor(MathF.Sqrt(maxTiles)));
        int xSamples = Math.Clamp(axisSamples, 1, width);
        int ySamples = Math.Clamp(Math.Max(1, maxTiles / xSamples), 1, height);
        while (xSamples * ySamples > maxTiles && ySamples > 1)
        {
            ySamples--;
        }

        for (int yIndex = 0; yIndex < ySamples; yIndex++)
        {
            int y = SampleAxis(minY, maxY, ySamples, yIndex);
            for (int xIndex = 0; xIndex < xSamples; xIndex++)
            {
                int x = SampleAxis(minX, maxX, xSamples, xIndex);
                AddOverviewTile(result, keys, x, y, maxTiles);
            }
        }

        return result.ToArray();
    }

    private static int SampleAxis(int min, int max, int sampleCount, int sampleIndex)
    {
        if (sampleCount <= 1 || max <= min)
        {
            return min + ((max - min) / 2);
        }

        float t = sampleIndex / (float)(sampleCount - 1);
        return Math.Clamp(min + (int)MathF.Round((max - min) * t), min, max);
    }

    private static void AddOverviewTile(
        List<(int X, int Y)> result,
        HashSet<long> keys,
        int x,
        int y,
        int maxTiles)
    {
        if (result.Count >= maxTiles)
        {
            return;
        }

        long key = (((long)x) << 32) ^ (uint)y;
        if (keys.Add(key))
        {
            result.Add((x, y));
        }
    }

    private static MassNavigationGuideSegment[] BuildTriangleEdges(
        NavTile tile,
        int maxTriangles,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        if (!mapper.Available)
        {
            return Array.Empty<MassNavigationGuideSegment>();
        }

        int maxEdges = Math.Max(0, maxTriangles) * 3;
        var edges = new List<MassNavigationGuideSegment>(Math.Min(maxEdges, Math.Max(0, tile.TriangleCount) * 3));
        AppendNavMeshTriangleWireEdges(tile, edges, maxEdges, mapper);
        return edges.ToArray();
    }

    private static void AppendTriangleEdges(
        NavTile tile,
        List<MassNavigationGuideSegment> edges,
        int maxEdges,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        int maxTriangles = int.MaxValue)
    {
        if (tile == null || maxEdges <= 0 || maxTriangles <= 0 || !mapper.Available)
        {
            return;
        }

        int edgeBudget = Math.Min(maxEdges, edges.Count + (Math.Max(0, maxTriangles) * 3));
        AppendNavMeshTriangleWireEdges(tile, edges, edgeBudget, mapper);
    }

    private static void AppendNavMeshTriangleWireEdges(
        NavTile tile,
        List<MassNavigationGuideSegment> edges,
        int maxEdges,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        if (tile == null || maxEdges <= 0 || !mapper.Available)
        {
            return;
        }

        for (int i = 0; i < tile.TriangleCount && edges.Count < maxEdges; i++)
        {
            int a = tile.TriA[i];
            int b = tile.TriB[i];
            int c = tile.TriC[i];
            int areaId = tile.TriAreaIds.Length > i ? tile.TriAreaIds[i] : 0;
            AddEdge(tile, edges, a, b, areaId, mapper, "walkable_triangle_edge");
            if (edges.Count >= maxEdges) break;
            AddEdge(tile, edges, b, c, areaId, mapper, "walkable_triangle_edge");
            if (edges.Count >= maxEdges) break;
            AddEdge(tile, edges, c, a, areaId, mapper, "walkable_triangle_edge");
        }
    }

    private static void AddEdge(
        NavTile tile,
        List<MassNavigationGuideSegment> edges,
        int a,
        int b,
        int areaId,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        string kind)
    {
        Vector2 aworld = mapper.BakedTileLocalToWorldCm(tile, tile.VertexXcm[a], tile.VertexZcm[a]);
        Vector2 bworld = mapper.BakedTileLocalToWorldCm(tile, tile.VertexXcm[b], tile.VertexZcm[b]);
        edges.Add(new MassNavigationGuideSegment(
            (int)MathF.Round(aworld.X),
            (int)MathF.Round(aworld.Y),
            (int)MathF.Round(bworld.X),
            (int)MathF.Round(bworld.Y),
            kind,
            ClearanceCm: 0,
            AreaId: areaId));
    }

    private static MassNavigationGuideSegment[] BuildPortalSegments(
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        if (!mapper.Available)
        {
            return Array.Empty<MassNavigationGuideSegment>();
        }

        var segments = new MassNavigationGuideSegment[tile.Portals.Length];
        for (int i = 0; i < tile.Portals.Length; i++)
        {
            NavBorderPortal portal = tile.Portals[i];
            Vector2 left = mapper.BakedTileLocalToWorldCm(tile, portal.LeftXcm, portal.LeftZcm);
            Vector2 right = mapper.BakedTileLocalToWorldCm(tile, portal.RightXcm, portal.RightZcm);
            segments[i] = new MassNavigationGuideSegment(
                (int)MathF.Round(left.X),
                (int)MathF.Round(left.Y),
                (int)MathF.Round(right.X),
                (int)MathF.Round(right.Y),
                $"portal_{portal.Side}",
                portal.ClearanceCm,
                AreaId: -1);
        }

        return segments;
    }

    private static MassNavigationNavMeshGuideSample CreateUnavailableNavMeshSample(string source)
    {
        return new MassNavigationNavMeshGuideSample(
            Available: false,
            Source: source,
            LogicHeightmapSource: string.Empty,
            Layer: 0,
            ProfileId: string.Empty,
            ChunkX: -1,
            ChunkY: -1,
            TriangleCount: 0,
            PortalCount: 0,
            FirstAreaId: 0,
            MinPortalClearanceCm: 0,
            AgentRadiusCm: 0,
            BlockedCellCount: 0,
            HighCostCellCount: 0,
            WaterCellCount: 0,
            RampCellCount: 0,
            AreaLegend: string.Empty,
            LayerLegend: string.Empty,
            BlockedSource: "not_available",
            OffMeshLinkSource: "not_available",
            TriangleEdges: Array.Empty<MassNavigationGuideSegment>(),
            Portals: Array.Empty<MassNavigationGuideSegment>());
    }

    private static int ResolveAgentRadiusCm(NavBakeDiagnosticsDocument document, string profileId)
    {
        return profileId switch
        {
            "GroundLarge" => 80,
            "Mountain" => 35,
            "Naval" => 140,
            "Air" => 60,
            "GroundLight" => 30,
            _ => document.LayerProfiles.Any(profile =>
                string.Equals(profile.ProfileId, "GroundLight", StringComparison.OrdinalIgnoreCase))
                    ? 30
                    : 0
        };
    }

    private static string BuildLayerLegend(NavBakeDiagnosticsDocument document)
    {
        var layers = document.LayerProfiles
            .GroupBy(profile => profile.Layer)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}:{group.First().LayerId}")
            .ToArray();
        return layers.Length == 0 ? "not_available" : string.Join(", ", layers);
    }

    private readonly record struct LogicCellCounts(int Blocked, int HighCost, int Water, int Ramp);

    private static LogicCellCounts CountLogicCells(string lhtmPath, int chunkX, int chunkY)
    {
        if (string.IsNullOrWhiteSpace(lhtmPath) || !File.Exists(lhtmPath) || chunkX < 0 || chunkY < 0)
        {
            return default;
        }

        try
        {
            using LogicHeightmapFileReader reader = LogicHeightmapFileReader.Open(lhtmPath);
            if (chunkX >= reader.WidthInChunks || chunkY >= reader.HeightInChunks)
            {
                return default;
            }

            LogicHeightmap map = reader.ReadTileWindow(chunkX, chunkY, radiusChunks: 0);
            LogicHeightmapChunk? chunk = map.GetChunk(
                chunkX * LogicHeightmapChunk.ChunkSize,
                chunkY * LogicHeightmapChunk.ChunkSize);
            if (chunk == null)
            {
                return default;
            }

            int blocked = 0;
            int highCost = 0;
            int water = 0;
            int ramp = 0;
            for (int y = 0; y < LogicHeightmapChunk.ChunkSize; y++)
            {
                for (int x = 0; x < LogicHeightmapChunk.ChunkSize; x++)
                {
                    if (MatchesLogicCell(chunk, x, y, LogicCellPredicate.Blocked))
                    {
                        blocked++;
                    }

                    if (MatchesLogicCell(chunk, x, y, LogicCellPredicate.HighCostArea))
                    {
                        highCost++;
                    }

                    if (MatchesLogicCell(chunk, x, y, LogicCellPredicate.Water))
                    {
                        water++;
                    }

                    if (MatchesLogicCell(chunk, x, y, LogicCellPredicate.Ramp))
                    {
                        ramp++;
                    }
                }
            }

            return new LogicCellCounts(blocked, highCost, water, ramp);
        }
        catch (IOException)
        {
            return default;
        }
        catch (InvalidDataException)
        {
            return default;
        }
    }

    private static bool MatchesLogicCell(LogicHeightmapChunk chunk, int x, int y, LogicCellPredicate predicate)
    {
        return predicate switch
        {
            LogicCellPredicate.Blocked => chunk.IsBlocked(x, y),
            LogicCellPredicate.Water => chunk.GetWaterHeightCm(x, y) > chunk.GetHeightCm(x, y),
            LogicCellPredicate.Ramp => chunk.IsRamp(x, y),
            LogicCellPredicate.HighCostArea => chunk.GetAreaId(x, y) is 2 or 3 or 5 or 6,
            _ => false
        };
    }

    private enum LogicCellPredicate
    {
        Blocked,
        Water,
        Ramp,
        HighCostArea,
    }

    private void Bump(string text)
    {
        LastActionText = text;
        _actionRevision++;
    }

    private readonly struct EntitySelectionScratch : IDisposable
    {
        public readonly Arch.Core.Entity[] Entities;
        public readonly int Count;

        public EntitySelectionScratch(Arch.Core.Entity[] entities, int count)
        {
            Entities = entities;
            Count = count;
        }

        public void Dispose()
        {
        }
    }
}
