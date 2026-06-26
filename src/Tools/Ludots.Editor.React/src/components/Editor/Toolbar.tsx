import React from 'react';
import {
    ArrowDown,
    ArrowUp,
    Ban,
    BoxSelect,
    Circle,
    Crosshair,
    Download,
    Droplets,
    Eye,
    Footprints,
    FolderOpen,
    Grid,
    HardDrive,
    Layers,
    Map as MapIcon,
    MapPin,
    Mountain,
    PaintBucket,
    Play,
    Plus,
    RefreshCw,
    Route,
    Save,
    Settings2,
    Shapes,
    SlidersHorizontal,
    Square,
    TreePine,
    Trash2,
    Type,
    Upload,
} from 'lucide-react';
import { useEditorStore, ToolCategory, ToolMode, type BoardCreateRequest, type BoardUpdateRequest, type BakedNavTilePayload, type EntityTemplatePayload, type JsonRecord } from './EditorStore';
import { Minimap } from './Minimap';
import { readNavTile } from '../../Core/NavMesh/NavTileBinary';
import { cellToWorldCm, worldPointToCell, type BoardTopology } from '../../Core/Map/TopologyMetrics';
import { CHUNK_BYTE_SIZE, REACT_TERRAIN_SPARSE_VERSION, REACT_TERRAIN_STRIDE } from '../../Core/Map/TerrainStore';
import { CellCm, DefaultEditorEagerFullTerrainFileMacroTilesPerAxis, DefaultHexEdgeLengthCm, DefaultNavQueryMaxPortals, DefaultWorldHeightMacroTiles, DefaultWorldWidthMacroTiles, MacroTileCells, TerrainChunkCells } from '../../Core/SpatialScaleDefaults';

const FLAT_GRID_BASELINE_SOURCE = 'flat-grid-baseline-v2';
const LEGACY_FLAT_GRID_BASELINE_SOURCE = 'flat-grid-baseline';

type NavBakeBudgetStatusText = 'ok' | 'large' | 'reject';

type NavBakeProfileEstimate = {
    profileId: string;
    agentRadiusCm: number;
    agentHeightCm: number;
    maxClimbCm: number;
    maxSlopeDeg: number;
    minWalkableUpDot: number;
    recastCellSizeCm: number;
    recastCellHeightCm: number;
    recastColumnsPerAxis: number;
    recastColumnBudgetPerTile: number;
    walkableHeightVoxels: number;
    walkableClimbVoxels: number;
};

type NavBakeEstimateReport = {
    mapId: string;
    sourceUri: string;
    mode: string;
    algorithm: string;
    estimateHash: string;
    terrainWidthCells: number;
    terrainHeightCells: number;
    terrainChunkCells: number;
    tileWorldWidthCm: number;
    tileWorldHeightCm: number;
    fullTileCountX: number;
    fullTileCountY: number;
    fullTileCount: number;
    targetTileCount: number;
    layerCount: number;
    profileCount: number;
    bakeOperationCount: number;
    obstacleCount: number;
    terrainContentHash: string;
    terrainCellSampleCount: number;
    recastColumnBudgetTotal: number;
    budgetWorkUnitCount: number;
    estimatedTileBytesLow: number;
    estimatedTileBytesHigh: number;
    effectiveWorkers: number;
    estimatedSerialSecondsLow: number;
    estimatedSerialSecondsHigh: number;
    estimatedSecondsLow: number;
    estimatedSecondsHigh: number;
    budgetStatus: 0 | 1 | 2;
    budgetStatusText: NavBakeBudgetStatusText;
    budgetMessage: string;
    requiresExplicitLargeBakeApproval: boolean;
    profiles: NavBakeProfileEstimate[];
};

type NavigationConfigPayload = {
    agentProfiles: NavAgentProfilePayload[];
    navmesh: NavMeshConfigPayload;
    sources?: unknown;
    paths?: unknown;
    validated?: JsonRecord;
};

type NavAgentProfilePayload = JsonRecord;
type NavBakeProfilePayload = JsonRecord;
type NavLayerPayload = JsonRecord;
type NavAreaPayload = JsonRecord;
type NavMeshConfigPayload = JsonRecord & {
    profiles?: NavBakeProfilePayload[];
    layers?: NavLayerPayload[];
    areas?: NavAreaPayload[];
    runtimeIncremental?: JsonRecord;
};
type TerrainLayerId = 'Snow' | 'Mud' | 'Ice';

type NavBakePhase = 'idle' | 'estimating' | 'estimated' | 'baking' | 'complete' | 'blocked' | 'error' | 'cancelled';

type NavBakeState = {
    phase: NavBakePhase;
    title: string;
    message: string;
    progress: number;
};

type NavQueryPhase = 'idle' | 'querying' | 'complete' | 'error';

type NavQueryUiState = {
    phase: NavQueryPhase;
    title: string;
    message: string;
};

const panelClass = 'pointer-events-auto rounded-lg border border-slate-700/80 bg-slate-950/90 text-slate-100 shadow-2xl backdrop-blur-md';
const sectionTitleClass = 'text-[10px] font-semibold uppercase tracking-[0.16em] text-slate-500';
const fieldLabelClass = 'text-[10px] text-slate-400';
const inputClass = 'mt-1 w-full rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs text-slate-100 outline-none focus:border-sky-500';
const compactInputClass = 'rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs text-slate-100 outline-none focus:border-sky-500';
const darkButtonClass = 'inline-flex items-center justify-center gap-2 rounded border border-slate-700 bg-slate-900 px-2 py-1.5 text-xs font-medium text-slate-200 transition hover:border-slate-500 hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-45';
const iconToggleClass = 'inline-flex h-9 w-9 items-center justify-center rounded border border-slate-800 bg-slate-900 text-slate-400 transition hover:border-slate-600 hover:bg-slate-800';
const idleNavBakeState: NavBakeState = {
    phase: 'idle',
    title: 'Bake idle',
    message: 'Run Estimate to preview cost, or Bake to estimate and execute in one pass.',
    progress: 0,
};

const idleNavQueryState: NavQueryUiState = {
    phase: 'idle',
    title: 'Path idle',
    message: 'Choose a profile/layer and run a C# query. Grid boards auto-create flat baseline tiles; other topologies require Bake.',
};

function errorMessage(value: unknown): string {
    if (value instanceof Error) return value.message;
    if (value && typeof value === 'object' && 'message' in value) {
        return String((value as { message?: unknown }).message ?? value);
    }
    return String(value ?? 'Unknown error');
}

function textValue(value: unknown, fallback = ''): string {
    return value == null ? fallback : String(value);
}

function numericValue(value: unknown, fallback: number): number {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
}

type BoardAllocationPreview = {
    isValid: boolean;
    withinEditorBudget: boolean;
    exceedsDefaultWorldFootprint: boolean;
    snappedToMacroTile: boolean;
    requestedWidthMeters: number;
    requestedHeightMeters: number;
    cellSizeCm: number;
    macroTileMeters: number;
    terrainChunkMeters: number;
    requestedWidthCells: number;
    requestedHeightCells: number;
    widthMacroTiles: number;
    heightMacroTiles: number;
    allocatedWidthCells: number;
    allocatedHeightCells: number;
    widthTerrainChunks: number;
    heightTerrainChunks: number;
    totalTerrainChunks: number;
    fullTerrainBytes: number;
    allocatedWidthMeters: number;
    allocatedHeightMeters: number;
};

function deriveBoardAllocation(widthMeters: number, heightMeters: number, cellSizeCm: number): BoardAllocationPreview {
    const safeCellSizeCm = Math.max(1, Math.floor(Number(cellSizeCm) || CellCm));
    const requestedWidthMeters = Math.max(0, Number.isFinite(widthMeters) ? widthMeters : 0);
    const requestedHeightMeters = Math.max(0, Number.isFinite(heightMeters) ? heightMeters : 0);
    const requestedWidthCells = requestedWidthMeters > 0
        ? Math.max(1, Math.ceil((requestedWidthMeters * 100) / safeCellSizeCm))
        : 0;
    const requestedHeightCells = requestedHeightMeters > 0
        ? Math.max(1, Math.ceil((requestedHeightMeters * 100) / safeCellSizeCm))
        : 0;
    const widthMacroTiles = requestedWidthCells > 0 ? Math.ceil(requestedWidthCells / MacroTileCells) : 0;
    const heightMacroTiles = requestedHeightCells > 0 ? Math.ceil(requestedHeightCells / MacroTileCells) : 0;
    const allocatedWidthCells = widthMacroTiles * MacroTileCells;
    const allocatedHeightCells = heightMacroTiles * MacroTileCells;
    const chunksPerMacroTile = MacroTileCells / TerrainChunkCells;
    const widthTerrainChunks = widthMacroTiles * chunksPerMacroTile;
    const heightTerrainChunks = heightMacroTiles * chunksPerMacroTile;
    const totalTerrainChunks = widthTerrainChunks * heightTerrainChunks;
    const fullTerrainBytes = totalTerrainChunks * CHUNK_BYTE_SIZE;
    const allocatedWidthMeters = allocatedWidthCells * safeCellSizeCm / 100;
    const allocatedHeightMeters = allocatedHeightCells * safeCellSizeCm / 100;
    const macroTileMeters = MacroTileCells * safeCellSizeCm / 100;
    const terrainChunkMeters = TerrainChunkCells * safeCellSizeCm / 100;
    const snappedToMacroTile =
        Math.abs(allocatedWidthMeters - requestedWidthMeters) > 0.0001 ||
        Math.abs(allocatedHeightMeters - requestedHeightMeters) > 0.0001;

    return {
        isValid: requestedWidthMeters > 0 && requestedHeightMeters > 0,
        withinEditorBudget: widthMacroTiles > 0 && heightMacroTiles > 0,
        exceedsDefaultWorldFootprint:
            widthMacroTiles > DefaultWorldWidthMacroTiles ||
            heightMacroTiles > DefaultWorldHeightMacroTiles,
        snappedToMacroTile,
        requestedWidthMeters,
        requestedHeightMeters,
        cellSizeCm: safeCellSizeCm,
        macroTileMeters,
        terrainChunkMeters,
        requestedWidthCells,
        requestedHeightCells,
        widthMacroTiles,
        heightMacroTiles,
        allocatedWidthCells,
        allocatedHeightCells,
        widthTerrainChunks,
        heightTerrainChunks,
        totalTerrainChunks,
        fullTerrainBytes,
        allocatedWidthMeters,
        allocatedHeightMeters,
    };
}

function formatMeters(value: number): string {
    return value.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function formatBytes(value: number): string {
    if (!Number.isFinite(value) || value <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let size = value;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
        size /= 1024;
        unit++;
    }
    return `${size.toLocaleString(undefined, { maximumFractionDigits: unit === 0 ? 0 : 1 })} ${units[unit]}`;
}

function parseDraftNumber(value: string): number {
    const trimmed = value.trim();
    if (trimmed.length === 0) return Number.NaN;
    const parsed = Number(trimmed);
    return Number.isFinite(parsed) ? parsed : Number.NaN;
}

function isPositiveFinite(value: number): boolean {
    return Number.isFinite(value) && value > 0;
}

export const Toolbar: React.FC = () => {
    const {
        activeCategory, setCategory,
        activeMode, setMode,
        brushSize, setBrushSize,
        brushValue, setBrushValue,
        activeLayer, setActiveLayer,
        terrain, loadMap, initMap,
        bridgeBaseUrl,
        mods, selectedModId, maps, mapInfos, selectedMapId, selectedMapInfo, selectedBoardName, selectedBoardInfo, loadedModId, loadedMapId, loadedBoardName, loadedBoardInfo, canvasSessionKind, canvasSessionLabel, boardMetrics,
        refreshMods, selectMod, selectMap, selectBoard, loadSelectedMap, saveSelectedMap,
        createBoard, updateSelectedBoard, deleteSelectedBoard,
        loadNavigationConfig, saveNavigationConfig, navigationConfig, navigationConfigVersion, setNavigationConfig,
        templates, selectedTemplateId, selectTemplate,
        obstacleTemplateId, setObstacleTemplate, obstacleShape, setObstacleShape, obstacleRadiusCm, setObstacleRadiusCm, obstacleHalfWidthCm, obstacleHalfHeightCm, setObstacleHalfSizeCm,
        spawnEntities, selectedEntityIndex, updateSelectedEntityOverridesJson, deleteSelectedEntityOverride,
        showGrid, toggleGrid,
        showChunkBorders, toggleChunkBorders,
        showNavMesh, toggleNavMesh,
        setBakedNavTiles,
        mergeBakedNavTiles,
        clearBakedNavTiles,
        bakedNavTiles,
        bakedNavTilePayloads,
        navSimulation,
        setNavSimulation,
        clearNavSimulation,
        navPanelTab,
        setNavPanelTab,
        navQueryProfileId,
        setNavQueryProfileId,
        navQueryLayer,
        setNavQueryLayer,
        navQueryStartCell,
        navQueryGoalCell,
        navDirtyChunks,
        clearNavDirty,
        setLoading,
        loadingState,
        cameraRef,
        controlsRef,
    } = useEditorStore();

    const [showNewMap, setShowNewMap] = React.useState(false);
    const [showAddBoard, setShowAddBoard] = React.useState(false);
    const defaultMacroTileMeters = String(MacroTileCells * CellCm / 100);
    const [newMapWidthMeters, setNewMapWidthMeters] = React.useState(defaultMacroTileMeters);
    const [newMapHeightMeters, setNewMapHeightMeters] = React.useState(defaultMacroTileMeters);
    const [newTopology, setNewTopology] = React.useState<BoardTopology>('Grid');
    const [newMapCellSizeCm, setNewMapCellSizeCm] = React.useState(String(CellCm));
    const [newMapHexEdgeLengthCm, setNewMapHexEdgeLengthCm] = React.useState(String(DefaultHexEdgeLengthCm));
    const [newBoardName, setNewBoardName] = React.useState('board_2');
    const [newBoardTopology, setNewBoardTopology] = React.useState<BoardTopology>('Grid');
    const [newBoardWidthMeters, setNewBoardWidthMeters] = React.useState(defaultMacroTileMeters);
    const [newBoardHeightMeters, setNewBoardHeightMeters] = React.useState(defaultMacroTileMeters);
    const [newBoardCellSizeCm, setNewBoardCellSizeCm] = React.useState(String(CellCm));
    const [newBoardHexEdgeLengthCm, setNewBoardHexEdgeLengthCm] = React.useState(String(DefaultHexEdgeLengthCm));
    const [newBoardNavigationEnabled, setNewBoardNavigationEnabled] = React.useState(true);
    const [editBoardCellSizeCm, setEditBoardCellSizeCm] = React.useState(CellCm);
    const [editBoardHexEdgeLengthCm, setEditBoardHexEdgeLengthCm] = React.useState(DefaultHexEdgeLengthCm);
    const [editBoardNavigationEnabled, setEditBoardNavigationEnabled] = React.useState(true);
    const [mapId, setMapId] = React.useState('');
    const [navScope, setNavScope] = React.useState<'dirty' | 'full'>('dirty');
    const [navIncludeNeighbors, setNavIncludeNeighbors] = React.useState(true);
    const [navParallel, setNavParallel] = React.useState(true);
    const [navHeightScale, setNavHeightScale] = React.useState(2.0);
    const [navMinUpDot, setNavMinUpDot] = React.useState(0.6);
    const [navCliffThreshold, setNavCliffThreshold] = React.useState(1);
    const [navMaxDegree, setNavMaxDegree] = React.useState(Math.max(1, navigator.hardwareConcurrency ?? 4));
    const [navEstimate, setNavEstimate] = React.useState<NavBakeEstimateReport | null>(null);
    const [navEstimateError, setNavEstimateError] = React.useState<string | null>(null);
    const [navBakeState, setNavBakeState] = React.useState<NavBakeState>(idleNavBakeState);
    const [navQueryState, setNavQueryState] = React.useState<NavQueryUiState>(idleNavQueryState);
    const [navQueryMaxPortals, setNavQueryMaxPortals] = React.useState(DefaultNavQueryMaxPortals);
    const navAbortRef = React.useRef<AbortController | null>(null);

    const mapInfoById = React.useMemo(() => new Map(mapInfos.map((m) => [m.id, m])), [mapInfos]);
    const selectedBoards = selectedMapInfo?.boards ?? [];
    const boardOptionsLabel = selectedBoards.length > 0 ? `${selectedBoards.length} board${selectedBoards.length === 1 ? '' : 's'}` : 'no boards';
    const canvasHasRepoSession = Boolean(canvasSessionKind === 'repo' && loadedModId && loadedMapId && loadedBoardName);
    const canvasHasLocalSession = canvasSessionKind === 'local';
    const canvasHasAnySession = canvasHasRepoSession || canvasHasLocalSession;
    const canvasMapLoaded = Boolean(canvasHasRepoSession && selectedModId && selectedMapId && selectedBoardName && loadedModId === selectedModId && loadedMapId === selectedMapId && loadedBoardName === selectedBoardName);
    const canvasCanEdit = Boolean(canvasHasLocalSession || (canvasMapLoaded && loadedBoardInfo?.canEditTerrain));
    const brushInspectorLocked = !canvasCanEdit || navPanelTab === 'simulation';
    const selectedDiffersFromCanvas = Boolean(canvasHasRepoSession && !canvasMapLoaded);
    const bakeMapId = canvasMapLoaded ? loadedMapId : null;
    const bakeBoardName = canvasMapLoaded ? loadedBoardName : null;
    const selectedTargetLabel = `${selectedMapId ?? 'no map'} / ${selectedBoardName ?? 'no board'}`;
    const loadedTargetLabel = canvasHasLocalSession
        ? (canvasSessionLabel ?? 'local draft')
        : canvasHasRepoSession
            ? `${loadedMapId} / ${loadedBoardName}${canvasMapLoaded ? '' : ' (different selection)'}`
            : 'not loaded';
    const selectedNavReady = Boolean(canvasMapLoaded && loadedBoardInfo?.canBake);
    const dirtyChunkCount = React.useMemo(() => {
        return navDirtyChunks.size;
    }, [navDirtyChunks, navDirtyChunks.size]);
    const navDisabledReason = !selectedMapId ? 'Select a map first.' :
        !selectedBoardName ? 'Select a board first.' :
        !canvasMapLoaded ? `Open '${selectedMapId}/${selectedBoardName}' from Map And Board before baking.` :
        loadedBoardInfo?.reason ?? 'Loaded board is not bakeable.';
    const estimateStatusLabel = navEstimate?.budgetStatusText === 'ok'
        ? 'Budget OK'
        : navEstimate?.budgetStatusText === 'large'
            ? 'Budget Large'
            : 'Budget Rejected';
    const estimateStatusHint = navEstimate?.budgetStatusText === 'large'
        ? 'This job is above the automatic safe budget. Pressing Bake is the explicit run action; no extra checkbox is required.'
        : navEstimate?.budgetStatusText === 'reject'
            ? 'This bake exceeds the configured hard budget and will not run.'
            : 'This bake is inside the safe auto-run budget.';
    const loadedCanvasLabel = canvasMapLoaded
        ? `${loadedMapId}/${loadedBoardName} / ${boardMetrics.topology} / ${terrain.widthChunks}x${terrain.heightChunks} chunks`
        : canvasHasLocalSession
            ? `${canvasSessionLabel ?? 'local draft'} / ${boardMetrics.topology} / ${terrain.widthChunks}x${terrain.heightChunks} chunks`
            : canvasHasRepoSession
                ? `${loadedMapId}/${loadedBoardName} / locked until selected`
                : 'not loaded';
    const boardSessionTone = canvasMapLoaded || canvasHasLocalSession
        ? 'border-sky-700/60 bg-sky-950/30 text-sky-100'
        : selectedDiffersFromCanvas
            ? 'border-amber-800/70 bg-amber-950/25 text-amber-100'
            : 'border-slate-800 bg-slate-900/60 text-slate-400';
    const boardSessionMessage = canvasHasLocalSession
        ? 'Local terrain draft. It can be edited and exported; repo save requires opening a board.'
        : canvasMapLoaded
            ? 'Selected board is open on the canvas.'
            : selectedDiffersFromCanvas
                ? 'Selected board is only a candidate. Open it before editing, saving, baking, or simulating.'
                : 'No board is open on the canvas.';
    const boardOpenDisabled = !selectedModId || !selectedMapId || !selectedBoardName || !selectedBoardInfo?.canEditTerrain;
    const boardOpenTitle = selectedBoardInfo?.canEditTerrain
        ? 'Open selected map board from repo via Bridge'
        : (selectedBoardInfo?.reason ?? 'Select an editable board first.');
    const deleteBoardDisabled = !selectedMapId || !selectedBoardName || selectedBoards.length <= 1 || canvasMapLoaded;
    const deleteBoardTitle = selectedBoards.length <= 1
        ? 'Cannot delete the last board from a map'
        : canvasMapLoaded
            ? 'Open another board before deleting the loaded board'
            : 'Delete selected board from MapConfig; terrain data file is kept';
    const boardPropertyTopology = canvasMapLoaded
        ? boardMetrics.topology
        : (selectedBoardInfo?.spatialType ?? selectedMapInfo?.spatialType ?? boardMetrics.topology);
    const boardPropertyWidthChunks = canvasMapLoaded
        ? terrain.widthChunks
        : (selectedBoardInfo?.widthChunks ?? selectedMapInfo?.widthChunks ?? terrain.widthChunks);
    const boardPropertyHeightChunks = canvasMapLoaded
        ? terrain.heightChunks
        : (selectedBoardInfo?.heightChunks ?? selectedMapInfo?.heightChunks ?? terrain.heightChunks);
    const boardPropertyChunks = `${boardPropertyWidthChunks} x ${boardPropertyHeightChunks}`;
    const boardPropertyCellSizeCm = canvasMapLoaded
        ? boardMetrics.cellSizeCm
        : (selectedBoardInfo?.cellSizeCm ?? selectedMapInfo?.cellSizeCm ?? boardMetrics.cellSizeCm);
    const boardPropertyHexEdgeLengthCm = canvasMapLoaded
        ? boardMetrics.hexEdgeLengthCm
        : (selectedBoardInfo?.hexEdgeLengthCm ?? selectedMapInfo?.hexEdgeLengthCm ?? boardMetrics.hexEdgeLengthCm);
    const boardPropertyChunkSizeCells = canvasMapLoaded
        ? boardMetrics.chunkSizeCells
        : (selectedBoardInfo?.chunkSizeCells ?? selectedMapInfo?.chunkSizeCells ?? boardMetrics.chunkSizeCells);
    const boardScalePreviewCellSizeCm = Math.max(1, Math.floor(editBoardCellSizeCm || CellCm));
    const boardScalePreviewHexEdgeLengthCm = Math.max(1, Math.floor(editBoardHexEdgeLengthCm || DefaultHexEdgeLengthCm));
    const boardScalePreviewWidthCells = boardPropertyWidthChunks * boardPropertyChunkSizeCells;
    const boardScalePreviewHeightCells = boardPropertyHeightChunks * boardPropertyChunkSizeCells;
    const boardScalePreviewWidthCm = boardScalePreviewWidthCells * boardScalePreviewCellSizeCm;
    const boardScalePreviewHeightCm = boardScalePreviewHeightCells * boardScalePreviewCellSizeCm;
    const boardScalePreviewChunkCm = boardPropertyChunkSizeCells * boardScalePreviewCellSizeCm;
    const boardScaleCellChanged = boardScalePreviewCellSizeCm !== boardPropertyCellSizeCm;
    const boardScaleHexChanged = boardPropertyTopology === 'HexGrid' && boardScalePreviewHexEdgeLengthCm !== boardPropertyHexEdgeLengthCm;
    const boardScaleNavChanged = editBoardNavigationEnabled !== (selectedBoardInfo?.navigationEnabled ?? selectedMapInfo?.navigationEnabled ?? true);
    const boardScaleHasChanges = boardScaleCellChanged || boardScaleHexChanged || boardScaleNavChanged;
    const newMapWidthMetersValue = parseDraftNumber(newMapWidthMeters);
    const newMapHeightMetersValue = parseDraftNumber(newMapHeightMeters);
    const newMapCellSizeCmValue = parseDraftNumber(newMapCellSizeCm);
    const newMapHexEdgeLengthCmValue = parseDraftNumber(newMapHexEdgeLengthCm);
    const newBoardWidthMetersValue = parseDraftNumber(newBoardWidthMeters);
    const newBoardHeightMetersValue = parseDraftNumber(newBoardHeightMeters);
    const newBoardCellSizeCmValue = parseDraftNumber(newBoardCellSizeCm);
    const newBoardHexEdgeLengthCmValue = parseDraftNumber(newBoardHexEdgeLengthCm);
    const newMapAllocation = React.useMemo(
        () => deriveBoardAllocation(newMapWidthMetersValue, newMapHeightMetersValue, newMapCellSizeCmValue),
        [newMapWidthMetersValue, newMapHeightMetersValue, newMapCellSizeCmValue],
    );
    const newBoardAllocation = React.useMemo(
        () => deriveBoardAllocation(newBoardWidthMetersValue, newBoardHeightMetersValue, newBoardCellSizeCmValue),
        [newBoardWidthMetersValue, newBoardHeightMetersValue, newBoardCellSizeCmValue],
    );
    const newBoardWithinFullFileBudget =
        newBoardAllocation.widthMacroTiles > 0 &&
        newBoardAllocation.heightMacroTiles > 0 &&
        newBoardAllocation.widthMacroTiles <= DefaultEditorEagerFullTerrainFileMacroTilesPerAxis &&
        newBoardAllocation.heightMacroTiles <= DefaultEditorEagerFullTerrainFileMacroTilesPerAxis;
    const newMapCreateWarning = newMapAllocation.exceedsDefaultWorldFootprint
        ? `This draft is larger than the default ${DefaultWorldWidthMacroTiles}x${DefaultWorldHeightMacroTiles} MacroTile world footprint. It will still open as sparse terrain; empty chunks are allocated only when painted.`
        : '';
    const newMapCreateDisabledReason = !isPositiveFinite(newMapWidthMetersValue) || !isPositiveFinite(newMapHeightMetersValue)
        ? 'Enter positive map width and height in meters.'
        : !isPositiveFinite(newMapCellSizeCmValue)
            ? 'Enter a positive grid cell size in centimeters.'
            : newTopology === 'HexGrid' && !isPositiveFinite(newMapHexEdgeLengthCmValue)
                ? 'Enter a positive hex edge length in centimeters.'
                : '';
    const newMapCanCreate = newMapCreateDisabledReason.length === 0;
    const newBoardCreateWarning = newBoardWithinFullFileBudget
        ? ''
        : `This board is ${newBoardAllocation.widthMacroTiles}x${newBoardAllocation.heightMacroTiles} MacroTiles. Bridge will create MapConfig first; Save writes sparse terrain instead of the ${formatBytes(newBoardAllocation.fullTerrainBytes)} full-file equivalent.`;
    const newBoardCreateDisabledReason = !newBoardName.trim()
        ? 'Board name is required.'
        : !isPositiveFinite(newBoardWidthMetersValue) || !isPositiveFinite(newBoardHeightMetersValue)
            ? 'Enter positive board width and height in meters.'
            : !isPositiveFinite(newBoardCellSizeCmValue)
                ? 'Enter a positive grid cell size in centimeters.'
                : newBoardTopology === 'HexGrid' && !isPositiveFinite(newBoardHexEdgeLengthCmValue)
                    ? 'Enter a positive hex edge length in centimeters.'
                    : '';
    const newBoardCanCreate = newBoardCreateDisabledReason.length === 0;
    const bakeButtonDisabled = !selectedNavReady || navBakeState.phase === 'estimating' || navBakeState.phase === 'baking';

    const formatEstimateBudgetDetail = (estimate: NavBakeEstimateReport) => {
        const statusDetail = estimate.budgetStatusText === 'ok'
            ? 'This bake can run directly.'
            : estimate.budgetStatusText === 'large'
                ? 'This is a large budget job. Bake will pass the backend large-budget token for this estimate hash.'
                : 'This bake exceeds the hard budget and must use a profiled bake-farm flow.';
        return [
            statusDetail,
            `Operations: ${estimate.bakeOperationCount.toLocaleString()}`,
            `Work units: ${estimate.budgetWorkUnitCount.toLocaleString()}`,
        ].join('\n');
    };

    React.useEffect(() => {
        let cancelled = false;
        const run = async () => {
            try {
                await refreshMods();
                if (cancelled) return;
                const s = useEditorStore.getState();
                if (!s.selectedModId && s.mods.length > 0) {
                    await s.selectMod(s.mods[0].id);
                }
            } catch {
                // Bridge health is surfaced in the dev status strip.
            }
        };
        run();
        return () => { cancelled = true; };
    }, [refreshMods]);

    React.useEffect(() => {
        if (selectedMapId) setMapId(selectedMapId);
    }, [selectedMapId]);

    React.useEffect(() => {
        const source = selectedBoardInfo ?? selectedMapInfo;
        setEditBoardCellSizeCm(source?.cellSizeCm ?? CellCm);
        setEditBoardHexEdgeLengthCm(source?.hexEdgeLengthCm ?? DefaultHexEdgeLengthCm);
        setEditBoardNavigationEnabled(source?.navigationEnabled ?? true);
    }, [selectedBoardInfo, selectedMapInfo, selectedMapId, selectedBoardName]);

    React.useEffect(() => {
        const existing = new Set(selectedBoards.map((board) => board.name));
        let index = selectedBoards.length + 1;
        let candidate = `board_${index}`;
        while (existing.has(candidate)) {
            index++;
            candidate = `board_${index}`;
        }
        setNewBoardName(candidate);
    }, [selectedMapId, selectedBoards.length]);

    React.useEffect(() => {
        setNavEstimate(null);
        setNavEstimateError(null);
        setNavBakeState((state) =>
            state.phase === 'baking' || state.phase === 'estimating' || state.phase === 'complete'
                ? state
                : idleNavBakeState);
    }, [mapId, navScope, navIncludeNeighbors, navParallel, navHeightScale, navMinUpDot, navCliffThreshold, navMaxDegree, terrain, navDirtyChunks.size, selectedModId, loadedMapId, navigationConfigVersion]);

    React.useEffect(() => {
        if (!showNewMap && !showAddBoard) return;

        const onKeyDown = (event: KeyboardEvent) => {
            if (event.key !== 'Escape') return;
            setShowNewMap(false);
            setShowAddBoard(false);
        };

        window.addEventListener('keydown', onKeyDown);
        return () => window.removeEventListener('keydown', onKeyDown);
    }, [showNewMap, showAddBoard]);

    const downloadBlob = (filename: string, blob: Blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    };

    const formatTimestamp = () => {
        const d = new Date();
        const pad = (n: number) => n.toString().padStart(2, '0');
        return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
    };

    const handleNewMap = () => {
        if (!newMapCanCreate) {
            alert(newMapCreateDisabledReason);
            return;
        }
        initMap(newMapAllocation.widthTerrainChunks, newMapAllocation.heightTerrainChunks, {
            topology: newTopology,
            cellSizeCm: Math.max(1, Math.floor(newMapCellSizeCmValue)),
            hexEdgeLengthCm: Math.max(1, Math.floor(newTopology === 'HexGrid' ? newMapHexEdgeLengthCmValue : DefaultHexEdgeLengthCm)),
            chunkSizeCells: TerrainChunkCells,
        });
        setShowNewMap(false);
    };

    const handleCreateBoard = async () => {
        if (!newBoardCanCreate) {
            alert(newBoardCreateDisabledReason);
            return;
        }
        const request: BoardCreateRequest = {
            name: newBoardName.trim(),
            spatialType: newBoardTopology,
            widthInMacroTiles: newBoardAllocation.widthMacroTiles,
            heightInMacroTiles: newBoardAllocation.heightMacroTiles,
            cellSizeCm: Math.max(1, Math.floor(newBoardCellSizeCmValue)),
            navigationEnabled: newBoardNavigationEnabled,
        };
        if (newBoardTopology === 'HexGrid') {
            request.hexEdgeLengthCm = Math.max(1, Math.floor(newBoardHexEdgeLengthCmValue));
        }
        try {
            await createBoard(request);
            setShowAddBoard(false);
        } catch (err: unknown) {
            alert(`Create board failed: ${errorMessage(err)}`);
        }
    };

    const handleUpdateBoard = async () => {
        if (!boardScaleHasChanges) return;

        const request: BoardUpdateRequest = {};
        if (boardScaleCellChanged) {
            request.cellSizeCm = boardScalePreviewCellSizeCm;
        }
        if (boardScaleHexChanged) {
            request.hexEdgeLengthCm = boardScalePreviewHexEdgeLengthCm;
        }
        if (boardScaleNavChanged) {
            request.navigationEnabled = editBoardNavigationEnabled;
        }
        try {
            await updateSelectedBoard(request);
        } catch (err: unknown) {
            alert(`Update board failed: ${errorMessage(err)}`);
        }
    };

    const handleDeleteSelectedBoard = async () => {
        if (!selectedBoardName) return;
        try {
            await deleteSelectedBoard();
        } catch (err: unknown) {
            alert(`Delete board failed: ${errorMessage(err)}`);
        }
    };

    const categories: { id: ToolCategory, icon: React.ReactNode, label: string }[] = [
        { id: 'Height', icon: <Mountain size={16} />, label: 'Height' },
        { id: 'Water', icon: <Droplets size={16} />, label: 'Water' },
        { id: 'Area', icon: <Shapes size={16} />, label: 'Area' },
        { id: 'Blocked', icon: <Ban size={16} />, label: 'Block' },
        { id: 'Biome', icon: <MapIcon size={16} />, label: 'Biome' },
        { id: 'Vegetation', icon: <TreePine size={16} />, label: 'Veg' },
        { id: 'Ramp', icon: <Type size={16} />, label: 'Ramp' },
        { id: 'Layers', icon: <Layers size={16} />, label: 'Layers' },
        { id: 'Entities', icon: <BoxSelect size={16} />, label: 'Ent' },
        { id: 'Obstacle', icon: <Circle size={16} />, label: 'Obs' },
    ];

    const modes: { id: ToolMode, icon: React.ReactNode, label: string }[] = [
        { id: 'Set', icon: <div className="h-3.5 w-3.5 rounded-full bg-current" />, label: 'Set' },
        { id: 'Raise', icon: <ArrowUp size={16} />, label: 'Raise' },
        { id: 'Lower', icon: <ArrowDown size={16} />, label: 'Lower' },
        { id: 'Bucket', icon: <PaintBucket size={16} />, label: 'Bucket' },
    ];

    const buildMapBlob = () => {
        const terrainBinary = terrain.toReactTerrainBinary();
        return new Blob([terrainBinary.header, terrainBinary.body], { type: 'application/octet-stream' });
    };

    const handleDownload = () => {
        if (!canvasHasAnySession) return;
        downloadBlob('map_data.bin', buildMapBlob());
    };

    const handleBakeNavTiles = async () => {
        if (!canvasMapLoaded) {
            alert(navDisabledReason);
            return;
        }
        const ts = formatTimestamp();
        const mapFile = `map_data_${ts}.bin`;
        const dirtyFile = `dirty_chunks_${ts}.json`;

        downloadBlob(mapFile, buildMapBlob());

        const dirtyChunks = Array.from(navDirtyChunks.values());
        downloadBlob(dirtyFile, new Blob([JSON.stringify(dirtyChunks, null, 2)], { type: 'application/json' }));

        const cmd = [
            'dotnet run --project .\\src\\Tools\\Ludots.Tool\\Ludots.Tool.csproj -- nav bake-recast-react',
            `  --mapId ${bakeMapId}`,
            selectedModId ? `  --modId ${selectedModId}` : null,
            bakeBoardName ? `  --boardName ${bakeBoardName}` : null,
            `  --in ${mapFile}`,
            `  --dirty ${dirtyFile}`,
            `  --heightScale ${navHeightScale}`,
            `  --minUpDot ${navMinUpDot}`,
            `  --cliffThreshold ${navCliffThreshold}`,
            `  --maxDegree ${navMaxDegree}`,
            '  --artifact true',
            '  --parallel true',
        ].filter(Boolean).join('\r\n');

        try {
            await navigator.clipboard.writeText(cmd);
            alert('Exported map data and dirty chunks. CLI bake command copied to clipboard.');
        } catch {
            alert(`Exported map data and dirty chunks.\n\nRun from repo root:\n${cmd}`);
        }
    };

    const base64ToArrayBuffer = (b64: string) => {
        const bin = atob(b64);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes.buffer;
    };

    const navPayloadKey = (cx: number, cy: number, layer: number, profileId: string) => `${cx},${cy},${layer},${profileId}`;

    const getQueryablePayloads = (profileId: string, layer: number) => {
        return useEditorStore.getState().bakedNavTilePayloads.filter((tile) =>
            tile.profileId === profileId &&
            tile.layer === layer &&
            tile.base64.length > 0 &&
            !!tile.detourBase64 &&
            tile.detourBase64.length > 0 &&
            tile.source !== LEGACY_FLAT_GRID_BASELINE_SOURCE);
    };

    const collectRouteChunkRequests = (
        startCell: { col: number; row: number },
        goalCell: { col: number; row: number }) => {
        const chunkSize = Math.max(1, boardMetrics.chunkSizeCells);
        const startCx = Math.floor(startCell.col / chunkSize);
        const startCy = Math.floor(startCell.row / chunkSize);
        const goalCx = Math.floor(goalCell.col / chunkSize);
        const goalCy = Math.floor(goalCell.row / chunkSize);
        const dx = Math.abs(goalCx - startCx);
        const dy = Math.abs(goalCy - startCy);
        const steps = Math.max(dx, dy, 1);
        const keys = new Set<string>();
        const add = (cx: number, cy: number) => {
            if (cx < 0 || cy < 0 || cx >= terrain.widthChunks || cy >= terrain.heightChunks) return;
            keys.add(`${cx},${cy}`);
        };

        for (let i = 0; i <= steps; i++) {
            const t = steps === 0 ? 0 : i / steps;
            const cx = Math.round(startCx + (goalCx - startCx) * t);
            const cy = Math.round(startCy + (goalCy - startCy) * t);
            for (let oy = -1; oy <= 1; oy++) {
                for (let ox = -1; ox <= 1; ox++) {
                    add(cx + ox, cy + oy);
                }
            }
        }

        return Array.from(keys, (key) => {
            const [cx, cy] = key.split(',').map(Number);
            return { cx, cy };
        });
    };

    const ensureFlatGridBaselineForRoute = async (
        startCell: { col: number; row: number },
        goalCell: { col: number; row: number }) => {
        const profileId = navQueryProfileId.trim();
        if (!canvasMapLoaded || !loadedMapId || !loadedBoardName || !profileId || boardMetrics.topology !== 'Grid') {
            return getQueryablePayloads(profileId, navQueryLayer);
        }

        const routeChunks = collectRouteChunkRequests(startCell, goalCell);
        const existing = new Set(
            useEditorStore.getState().bakedNavTilePayloads
                .filter((tile) => {
                    const source = tile.source ?? null;
                    return tile.profileId === profileId &&
                        tile.layer === navQueryLayer &&
                        !!tile.detourBase64 &&
                        tile.detourBase64.length > 0 &&
                        source !== LEGACY_FLAT_GRID_BASELINE_SOURCE;
                })
                .map((tile) => tile.key));
        const missingChunks = routeChunks.filter((chunk) => !existing.has(navPayloadKey(chunk.cx, chunk.cy, navQueryLayer, profileId)));
        if (missingChunks.length === 0) {
            return getQueryablePayloads(profileId, navQueryLayer);
        }

        setNavQueryState({
            phase: 'querying',
            title: 'Preparing Grid baseline',
            message: `Requesting ${missingChunks.length} missing flat two-triangle Detour tile(s) before the C# path query.`,
        });

        const res = await fetch(`${bridgeBaseUrl}/api/nav/bootstrap-flat-grid-react`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                modId: selectedModId,
                mapId: loadedMapId,
                boardName: loadedBoardName,
                profileId,
                layer: navQueryLayer,
                chunks: missingChunks,
            }),
        });
        const json = await res.json().catch(() => null);
        if (!res.ok || json?.ok === false) {
            throw new Error(json?.error ?? `Bridge error ${res.status}`);
        }

        const tilesRaw: Array<{ cx?: number; cy?: number; layer?: number; profileId?: string; base64: string; detourBase64?: string; source?: string }> = json.tiles ?? [];
        const tiles = [];
        const payloads: BakedNavTilePayload[] = [];
        for (let i = 0; i < tilesRaw.length; i++) {
            const raw = tilesRaw[i];
            const buf = base64ToArrayBuffer(raw.base64);
            const tile = readNavTile(buf);
            const rawProfileId = raw.profileId == null ? profileId : String(raw.profileId);
            const layer = Number(raw.layer ?? tile.tileId.layer);
            const detourBase64 = raw.detourBase64 == null ? null : String(raw.detourBase64);
            tiles.push(tile);
            payloads.push({
                key: navPayloadKey(tile.tileId.chunkX, tile.tileId.chunkY, layer, rawProfileId),
                layer,
                profileId: rawProfileId,
                base64: raw.base64,
                detourBase64,
                source: raw.source ?? FLAT_GRID_BASELINE_SOURCE,
            });
        }

        if (tiles.length > 0) {
            mergeBakedNavTiles(tiles, payloads);
            if (!showNavMesh) toggleNavMesh();
        }

        return getQueryablePayloads(profileId, navQueryLayer);
    };

    const cloneJson = <T,>(value: T): T => JSON.parse(JSON.stringify(value ?? null));

    const mutateNavigationConfig = (mutator: (draft: NavigationConfigPayload) => void) => {
        const current = (navigationConfig ?? { agentProfiles: [], navmesh: {} }) as NavigationConfigPayload;
        const draft: NavigationConfigPayload = cloneJson({
            ...current,
            agentProfiles: Array.isArray(current.agentProfiles) ? current.agentProfiles : [],
            navmesh: current.navmesh && typeof current.navmesh === 'object' ? current.navmesh : {},
        });
        mutator(draft);
        setNavigationConfig(draft);
    };

    const updateAgentProfileField = (index: number, field: string, value: string, numeric = true) => {
        mutateNavigationConfig((draft) => {
            const profiles = Array.isArray(draft.agentProfiles) ? draft.agentProfiles : [];
            if (!profiles[index]) return;
            profiles[index][field] = numeric ? Number(value) : value;
            draft.agentProfiles = profiles;
        });
    };

    const updateBakeProfileField = (index: number, field: string, value: string, numeric = true) => {
        mutateNavigationConfig((draft) => {
            const profiles = Array.isArray(draft.navmesh.profiles) ? draft.navmesh.profiles : [];
            if (!profiles[index]) return;
            profiles[index][field] = numeric ? Number(value) : value;
            draft.navmesh.profiles = profiles;
        });
    };

    const updateNavLayerField = (index: number, field: string, value: string, numeric = true) => {
        mutateNavigationConfig((draft) => {
            const layers = Array.isArray(draft.navmesh.layers) ? draft.navmesh.layers : [];
            if (!layers[index]) return;
            layers[index][field] = numeric ? Number(value) : value;
            draft.navmesh.layers = layers;
        });
    };

    const updateNavAreaField = (index: number, field: string, value: string, numeric = true) => {
        mutateNavigationConfig((draft) => {
            const areas = Array.isArray(draft.navmesh.areas) ? draft.navmesh.areas : [];
            if (!areas[index]) return;
            areas[index][field] = numeric ? Number(value) : value;
            draft.navmesh.areas = areas;
        });
    };

    const updateRuntimeIncrementalField = (field: string, value: string | boolean, numeric = true) => {
        mutateNavigationConfig((draft) => {
            draft.navmesh.runtimeIncremental = draft.navmesh.runtimeIncremental ?? {};
            draft.navmesh.runtimeIncremental[field] = typeof value === 'boolean' ? value : (numeric ? Number(value) : value);
        });
    };

    const addAgentProfile = () => mutateNavigationConfig((draft) => {
        const profiles = Array.isArray(draft.agentProfiles) ? draft.agentProfiles : [];
        profiles.push({ id: `agent_${profiles.length + 1}`, radiusCm: 30, heightCm: 180, clearanceCm: 40, mass: 1, layer: 0 });
        draft.agentProfiles = profiles;
    });

    const addBakeProfile = () => mutateNavigationConfig((draft) => {
        const agentProfiles = Array.isArray(draft.agentProfiles) ? draft.agentProfiles : [];
        const profiles = Array.isArray(draft.navmesh.profiles) ? draft.navmesh.profiles : [];
        profiles.push({ id: String(agentProfiles[0]?.id ?? `agent_${profiles.length + 1}`), maxClimbCm: 40, maxSlopeDeg: 45 });
        draft.navmesh.profiles = profiles;
    });

    const addNavLayer = () => mutateNavigationConfig((draft) => {
        const layers = Array.isArray(draft.navmesh.layers) ? draft.navmesh.layers : [];
        layers.push({ id: `Layer${layers.length}`, layer: layers.length });
        draft.navmesh.layers = layers;
    });

    const addNavArea = () => mutateNavigationConfig((draft) => {
        const areas = Array.isArray(draft.navmesh.areas) ? draft.navmesh.areas : [];
        areas.push({ id: `Area${areas.length + 1}`, areaId: areas.length + 1, cost: 1 });
        draft.navmesh.areas = areas;
    });

    const handleSaveNavigationConfig = async () => {
        try {
            setLoading(true, 'Saving Navigation Config...', 40);
            await saveNavigationConfig();
            setNavEstimate(null);
            setNavBakeState(idleNavBakeState);
        } catch (err: unknown) {
            alert(`Navigation config save failed: ${errorMessage(err)}`);
        } finally {
            setLoading(false);
        }
    };

    const handleReloadNavigationConfig = async () => {
        try {
            setLoading(true, 'Loading Navigation Config...', 30);
            await loadNavigationConfig();
            setNavEstimate(null);
            setNavBakeState(idleNavBakeState);
        } catch (err: unknown) {
            alert(`Navigation config load failed: ${errorMessage(err)}`);
        } finally {
            setLoading(false);
        }
    };

    const collectViewportSeedChunks = () => {
        const seed = new Set<string>();
        const target = controlsRef.current?.target ?? cameraRef.current?.position ?? null;
        const chunkSize = boardMetrics.chunkSizeCells;
        let cx = Math.floor(terrain.widthChunks / 2);
        let cy = Math.floor(terrain.heightChunks / 2);
        if (target) {
            const cell = worldPointToCell(Number(target.x ?? 0), Number(target.z ?? 0), boardMetrics);
            cx = Math.floor(cell.col / chunkSize);
            cy = Math.floor(cell.row / chunkSize);
        }

        cx = Math.max(0, Math.min(terrain.widthChunks - 1, cx));
        cy = Math.max(0, Math.min(terrain.heightChunks - 1, cy));
        seed.add(`${cx},${cy}`);
        return seed;
    };

    const appendNavBakeFormFields = (form: FormData, dirtySet: Set<string>, dirtyCount: number) => {
        if (!bakeMapId) throw new Error('Open a map board before baking.');
        form.append('map', buildMapBlob(), 'map_data.bin');
        form.append('mapId', bakeMapId);
        if (selectedModId) form.append('modId', selectedModId);
        if (bakeBoardName) form.append('boardName', bakeBoardName);

        if (navScope === 'dirty') {
            if (dirtyCount === 0) {
                if (bakedNavTilePayloads.length === 0) {
                    const seed = collectViewportSeedChunks();
                    for (const key of seed) dirtySet.add(key);
                    dirtyCount = dirtySet.size;
                } else {
                    throw new Error('No nav dirty chunks. Paint terrain, area, blockers, or obstacles first; otherwise switch scope to Full.');
                }
            }
            const dirtyChunks = Array.from(dirtySet.values());
            form.append('dirty', JSON.stringify(dirtyChunks));
            form.append('dirtyOnly', 'true');
        }

        form.append('includeNeighbors', navIncludeNeighbors ? 'true' : 'false');
        form.append('parallel', navParallel ? 'true' : 'false');
        form.append('heightScale', String(navHeightScale));
        form.append('minUpDot', String(navMinUpDot));
        form.append('cliffThreshold', String(navCliffThreshold));
        form.append('maxDegree', String(navMaxDegree));
    };

    const collectDirtyChunks = () => {
        const dirtySet = new Set<string>();
        for (const k of navDirtyChunks.values()) dirtySet.add(k);
        return dirtySet;
    };

    const fetchNavEstimate = async () => {
        if (!selectedNavReady) {
            throw new Error(navDisabledReason);
        }
        const endpoint = `${bridgeBaseUrl}/api/nav/estimate-recast-react`;
        const form = new FormData();
        const dirtySet = collectDirtyChunks();
        const dirtyCount = dirtySet.size;

        appendNavBakeFormFields(form, dirtySet, dirtyCount);
        const res = await fetch(endpoint, { method: 'POST', body: form });
        if (!res.ok) {
            const text = await res.text();
            throw new Error(`Bridge error ${res.status}: ${text}`);
        }
        const json = await res.json();
        return json.estimate as NavBakeEstimateReport;
    };

    const handleEstimateNavTilesLocal = async () => {
        try {
            setNavEstimateError(null);
            setNavBakeState({
                phase: 'estimating',
                title: 'Estimating',
                message: 'Sending terrain, dirty scope, and nav config to the Bridge estimator.',
                progress: 35,
            });
            setLoading(true, 'Estimating NavTiles...', 45);
            const estimate = await fetchNavEstimate();
            setNavEstimate(estimate);
            setNavBakeState({
                phase: 'estimated',
                title: estimate.budgetStatusText === 'large' ? 'Large budget estimated' : estimate.budgetStatusText === 'reject' ? 'Budget rejected' : 'Estimate ready',
                message: estimate.budgetStatusText === 'large'
                    ? 'Large means this bake is expensive. Press Bake to run it with the displayed estimate hash.'
                    : estimate.budgetStatusText === 'reject'
                        ? 'This bake exceeds the hard budget and will not run from the editor.'
                        : 'Estimate is inside the normal editor budget.',
                progress: 100,
            });
        } catch (err: unknown) {
            const message = errorMessage(err);
            setNavEstimate(null);
            setNavEstimateError(message);
            setNavBakeState({
                phase: 'error',
                title: 'Estimate failed',
                message,
                progress: 100,
            });
            alert(`Nav estimate failed.\n\nStart Bridge first:\n  dotnet run --project .\\src\\Tools\\Ludots.Editor.Bridge\\Ludots.Editor.Bridge.csproj\n\nError: ${message}`);
        } finally {
            setLoading(false);
        }
    };

    const handleBakeNavTilesLocal = async () => {
        if (!selectedNavReady) {
            alert(navDisabledReason);
            return;
        }
        const endpoint = `${bridgeBaseUrl}/api/nav/bake-recast-react`;
        const form = new FormData();
        const dirtySet = collectDirtyChunks();
        const dirtyCount = dirtySet.size;

        try {
            appendNavBakeFormFields(form, dirtySet, dirtyCount);
        } catch (err: unknown) {
            const message = errorMessage(err);
            setNavBakeState({
                phase: 'blocked',
                title: 'Bake blocked',
                message,
                progress: 100,
            });
            return;
        }
        form.append('artifact', 'false');
        const effectiveDirtyCount = dirtySet.size;
        const isIncrementalBake = navScope === 'dirty';

        let timeoutId: number | null = null;
        try {
            setNavBakeState({
                phase: 'estimating',
                title: 'Preparing bake',
                message: 'Estimating budget before submitting the bake request.',
                progress: 20,
            });
            const estimate = await fetchNavEstimate();
            setNavEstimate(estimate);
            setNavEstimateError(null);
            if (estimate.budgetStatusText === 'reject') {
                setNavBakeState({
                    phase: 'blocked',
                    title: 'Bake rejected',
                    message: formatEstimateBudgetDetail(estimate),
                    progress: 100,
                });
                return;
            }
            if (estimate.budgetStatusText === 'large') {
                form.append('largeBake', 'true');
                form.append('estimateHash', estimate.estimateHash);
            }

            navAbortRef.current?.abort();
            navAbortRef.current = new AbortController();
            const scopeLabel = isIncrementalBake ? `Dirty(${effectiveDirtyCount})${navIncludeNeighbors ? '+N' : ''}` : 'Full';
            setNavBakeState({
                phase: 'baking',
                title: estimate.budgetStatusText === 'large' ? 'Baking large budget job' : 'Baking NavTiles',
                message: `${scopeLabel}: submitted to Bridge. Waiting for baked tiles.`,
                progress: 55,
            });
            setLoading(true, `Baking NavTiles: ${scopeLabel}...`, 30);
            timeoutId = window.setTimeout(() => navAbortRef.current?.abort(), 120000);
            const res = await fetch(endpoint, { method: 'POST', body: form, signal: navAbortRef.current.signal });
            if (!res.ok) {
                const text = await res.text();
                throw new Error(`Bridge error ${res.status}: ${text}`);
            }
            const json = await res.json();
            setNavBakeState({
                phase: 'baking',
                title: 'Decoding NavTiles',
                message: 'Bridge returned tile payloads. Decoding and installing into the editor.',
                progress: 85,
            });
            const tilesRaw: Array<{ cx?: number; cy?: number; layer?: number; profileId?: string; base64: string; detourBase64?: string; source?: string }> = json.tiles ?? [];
            if (tilesRaw.length === 0) {
                const targetsCount = Number(json.targetsCount ?? 0);
                if (targetsCount === 0) {
                    setNavBakeState({
                        phase: 'complete',
                        title: 'Nothing to bake',
                        message: 'No target chunks need baking. Dirty scope is empty.',
                        progress: 100,
                    });
                    return;
                }
                throw new Error('No tiles returned.');
            }

            const tiles = [];
            const payloads: BakedNavTilePayload[] = [];
            for (let i = 0; i < tilesRaw.length; i++) {
                const raw = tilesRaw[i];
                const buf = base64ToArrayBuffer(raw.base64);
                const tile = readNavTile(buf);
                tiles.push(tile);
                const profileId = raw.profileId == null ? null : String(raw.profileId);
                const detourBase64 = raw.detourBase64 == null ? null : String(raw.detourBase64);
                payloads.push({
                    key: `${tile.tileId.chunkX},${tile.tileId.chunkY},${tile.tileId.layer},${profileId ?? ''}`,
                    layer: Number(raw.layer ?? tile.tileId.layer),
                    profileId,
                    base64: raw.base64,
                    detourBase64,
                    source: raw.source ?? 'recast',
                });
            }

            if (isIncrementalBake) {
                mergeBakedNavTiles(tiles, payloads);
            } else {
                setBakedNavTiles(tiles, payloads);
            }
            const installedState = useEditorStore.getState();
            const installedVisualTileCount = installedState.bakedNavTiles.size;
            const installedPayloadCount = installedState.bakedNavTilePayloads.length;
            if (!showNavMesh) toggleNavMesh();
            terrain.clearDirty();
            clearNavDirty();
            setNavQueryState(idleNavQueryState);
            setNavBakeState({
                phase: 'complete',
                title: 'Bake complete',
                message: `${isIncrementalBake ? 'Merged' : 'Loaded'} ${tiles.length} NavTile(s) across ${payloads.length} profile/layer payload(s). Editor now has ${installedVisualTileCount} visual tile(s) and ${installedPayloadCount} query payload(s). Navmesh visualization is enabled.`,
                progress: 100,
            });
            setLoading(false);
        } catch (err: unknown) {
            setLoading(false);
            if (err instanceof DOMException && err.name === 'AbortError') {
                setNavBakeState({
                    phase: 'cancelled',
                    title: 'Bake cancelled',
                    message: 'NavTiles bake was cancelled before completion.',
                    progress: 100,
                });
                return;
            }
            setNavBakeState({
                phase: 'error',
                title: 'Bake failed',
                message: errorMessage(err),
                progress: 100,
            });
            alert(`Local Bridge is not running or the request failed.\n\nStart Bridge first:\n  dotnet run --project .\\src\\Tools\\Ludots.Editor.Bridge\\Ludots.Editor.Bridge.csproj\n\nError: ${errorMessage(err)}`);
        } finally {
            if (timeoutId !== null) window.clearTimeout(timeoutId);
            navAbortRef.current = null;
        }
    };

    const clampCell = (value: number, maxExclusive: number) => Math.max(0, Math.min(Math.floor(value || 0), Math.max(0, maxExclusive - 1)));

    const handleSimulateNavPath = async () => {
        if (!navQueryReady) {
            setNavQueryState({
                phase: 'error',
                title: 'Path blocked',
                message: navQueryDisabledReason,
            });
            return;
        }

        try {
            clearNavSimulation();
            const startCell = {
                col: clampCell(navQueryStartCell.col, totalCellsX),
                row: clampCell(navQueryStartCell.row, totalCellsY),
            };
            const goalCell = {
                col: clampCell(navQueryGoalCell.col, totalCellsX),
                row: clampCell(navQueryGoalCell.row, totalCellsY),
            };
            const start = cellToWorldCm(startCell.col, startCell.row, boardMetrics);
            const goal = cellToWorldCm(goalCell.col, goalCell.row, boardMetrics);
            const areaCosts = navAreas
                .map((area: NavAreaPayload) => ({
                    areaId: Number(area?.areaId ?? area?.AreaId ?? NaN),
                    cost: Number(area?.cost ?? area?.Cost ?? NaN),
                }))
                .filter((area: { areaId: number; cost: number }) =>
                    Number.isFinite(area.areaId) && Number.isFinite(area.cost) && area.areaId >= 0 && area.areaId <= 255 && area.cost > 0);

            const queryPayloads = await ensureFlatGridBaselineForRoute(startCell, goalCell);
            if (queryPayloads.length === 0) {
                throw new Error('No Detour tile payloads are available for this profile/layer. Grid boards can create flat baseline tiles automatically; other topology requires Bake.');
            }

            setNavQueryState({
                phase: 'querying',
                title: 'Querying C# nav',
                message: `Sending ${queryPayloads.length} Detour tile payload(s) to the C# Core query engine.`,
            });
            setLoading(true, 'Querying Nav Path...', 60);
            const res = await fetch(`${bridgeBaseUrl}/api/nav/query-recast-react`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    modId: selectedModId,
                    mapId: loadedMapId,
                    boardName: loadedBoardName,
                    profileId: navQueryProfileId,
                    layer: navQueryLayer,
                    start: { xCm: start.xCm, zCm: start.yCm },
                    goal: { xCm: goal.xCm, zCm: goal.yCm },
                    maxPortals: Math.max(1, Math.floor(navQueryMaxPortals || 256)),
                    areaCosts,
                    tiles: queryPayloads.map((tile) => ({
                        profileId: tile.profileId,
                        layer: tile.layer,
                        base64: tile.base64,
                        detourBase64: tile.detourBase64,
                        source: tile.source,
                    })),
                }),
            });
            const json = await res.json().catch(() => null);
            if (!res.ok || json?.ok === false) {
                throw new Error(json?.error ?? `Bridge error ${res.status}`);
            }

            setNavSimulation(json);
            const status = String(json.status ?? 'Unknown');
            setNavQueryState({
                phase: status === 'Ok' ? 'complete' : 'error',
                title: status === 'Ok' ? 'Path complete' : `Path ${status}`,
                message: `${json.algorithmSource ?? 'Core query'}\n${Number(json.elapsedMs ?? 0).toFixed(3)} ms, ${json.points?.length ?? 0} point(s), cost ${Number(json.travelCost ?? 0).toFixed(2)}.`,
            });
        } catch (err: unknown) {
            setNavQueryState({
                phase: 'error',
                title: 'Path query failed',
                message: errorMessage(err),
            });
            clearNavSimulation();
        } finally {
            setLoading(false);
        }
    };

    const handleUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (ev) => {
            const buffer = ev.target?.result as ArrayBuffer;
            if (!buffer) return;

            const view = new DataView(buffer);
            const w = view.getInt32(0, true);
            const h = view.getInt32(4, true);
            const stride = view.getUint8(8);

            if (stride !== REACT_TERRAIN_STRIDE && stride !== REACT_TERRAIN_SPARSE_VERSION) {
                alert(`Invalid map terrain format. Expected ${REACT_TERRAIN_STRIDE} or ${REACT_TERRAIN_SPARSE_VERSION}, got ${stride}. Please recreate map.`);
                return;
            }

            const data = new Uint8Array(buffer.slice(9));
            loadMap(data, w, h, boardMetrics, stride);
        };
        reader.readAsArrayBuffer(file);
    };

    const navEditorConfig = (navigationConfig ?? null) as NavigationConfigPayload | null;
    const navmeshConfig = navEditorConfig?.navmesh ?? {};
    const agentProfiles = Array.isArray(navEditorConfig?.agentProfiles) ? navEditorConfig!.agentProfiles : [];
    const bakeProfiles = Array.isArray(navmeshConfig.profiles) ? navmeshConfig.profiles : [];
    const navLayers = Array.isArray(navmeshConfig.layers) ? navmeshConfig.layers : [];
    const navAreas = Array.isArray(navmeshConfig.areas) ? navmeshConfig.areas : [];
    const runtimeIncremental = navmeshConfig.runtimeIncremental ?? {};
    const selectedBakeProfile = React.useMemo(() => {
        const id = navQueryProfileId.trim();
        return bakeProfiles.find((profile: NavBakeProfilePayload, i: number) => String(profile?.id ?? profile?.Id ?? `profile_${i}`) === id) ?? null;
    }, [bakeProfiles, navQueryProfileId]);
    const selectedAgentProfile = React.useMemo(() => {
        const id = navQueryProfileId.trim();
        return agentProfiles.find((profile: NavAgentProfilePayload, i: number) => String(profile?.id ?? profile?.Id ?? `profile_${i}`) === id) ?? null;
    }, [agentProfiles, navQueryProfileId]);
    const selectedEstimateProfile = React.useMemo(
        () => navEstimate?.profiles?.find((profile) => profile.profileId === navQueryProfileId) ?? null,
        [navEstimate, navQueryProfileId]);
    const selectedAgentRadiusCm = Number(
        selectedAgentProfile?.radiusCm ??
        selectedAgentProfile?.RadiusCm ??
        selectedAgentProfile?.bodyRadiusCm ??
        selectedEstimateProfile?.agentRadiusCm ??
        0);
    const totalCellsX = Math.max(1, terrain.widthChunks * boardMetrics.chunkSizeCells);
    const totalCellsY = Math.max(1, terrain.heightChunks * boardMetrics.chunkSizeCells);
    const availableNavPayloads = React.useMemo(() => {
        const profileId = navQueryProfileId.trim();
        return bakedNavTilePayloads.filter((tile) =>
            tile.profileId === profileId &&
            tile.layer === navQueryLayer &&
            tile.base64.length > 0 &&
            !!tile.detourBase64 &&
            tile.detourBase64.length > 0 &&
            tile.source !== LEGACY_FLAT_GRID_BASELINE_SOURCE);
    }, [bakedNavTilePayloads, navQueryLayer, navQueryProfileId]);
    const flatBaselinePayloadCount = React.useMemo(
        () => bakedNavTilePayloads.filter((tile) => tile.source === FLAT_GRID_BASELINE_SOURCE).length,
        [bakedNavTilePayloads]);
    const gridBaselineAvailable = Boolean(canvasMapLoaded && boardMetrics.topology === 'Grid');
    const navQueryDisabledReason = !canvasMapLoaded
        ? 'Open the selected board from Map And Board before simulating.'
        : !navQueryProfileId
            ? 'Choose a nav profile.'
            : availableNavPayloads.length === 0 && !gridBaselineAvailable
                ? bakedNavTiles.size === 0
                    ? 'Bake NavTiles before simulating this topology.'
                    : bakedNavTilePayloads.length === 0
                        ? 'Session NavTiles have no query payload; run Bridge Bake so profile/layer Detour payloads are available.'
                        : `No Detour tile payloads match profile '${navQueryProfileId}' layer ${navQueryLayer}.`
                : '';
    const navQueryReady = navQueryDisabledReason.length === 0;

    React.useEffect(() => {
        if (!navQueryProfileId && bakeProfiles.length > 0) {
            setNavQueryProfileId(String(bakeProfiles[0]?.id ?? ''));
        }
    }, [bakeProfiles, navQueryProfileId]);

    React.useEffect(() => {
        if (navLayers.length > 0 && !navLayers.some((layer: NavLayerPayload) => Number(layer?.layer ?? 0) === navQueryLayer)) {
            setNavQueryLayer(Number(navLayers[0]?.layer ?? 0));
        }
    }, [navLayers, navQueryLayer]);

    React.useEffect(() => {
        setNavQueryState(idleNavQueryState);
    }, [navQueryStartCell.col, navQueryStartCell.row, navQueryGoalCell.col, navQueryGoalCell.row, navQueryProfileId, navQueryLayer]);

    React.useEffect(() => {
        if (canvasMapLoaded) return;
        setNavEstimate(null);
        setNavEstimateError(null);
        setNavBakeState(idleNavBakeState);
        setNavQueryState(idleNavQueryState);
    }, [canvasMapLoaded, selectedMapId, selectedBoardName, loadedMapId, loadedBoardName]);

    const numberField = (
        label: string,
        value: number,
        onChange: (value: number) => void,
        options: { step?: string; min?: string; max?: string } = {},
    ) => (
        <label className={fieldLabelClass}>
            {label}
            <input
                type="number"
                step={options.step}
                min={options.min}
                max={options.max}
                value={value}
                onChange={(e) => onChange(Number(e.target.value))}
                className={inputClass}
            />
        </label>
    );

    const renderBrushValueControls = () => {
        if (activeCategory === 'Area') {
            return (
                <div className="grid grid-cols-2 gap-2">
                    {[
                        { id: 0, label: '0 Default', color: 'bg-[#8B4513]' },
                        { id: 1, label: '1 Road', color: 'bg-[#9ca3af]' },
                        { id: 2, label: '2 Forest', color: 'bg-[#256d3b]' },
                        { id: 3, label: '3 Swamp', color: 'bg-[#4d5f2f]' },
                        { id: 4, label: '4 Waterbank', color: 'bg-[#2563eb]' },
                        { id: 5, label: '5 Hazard', color: 'bg-[#b45309]' },
                    ].map((area) => (
                        <button
                            key={area.id}
                            onClick={() => {
                                setBrushValue(area.id);
                                setMode('Set');
                            }}
                            className={`rounded border p-2 text-xs font-semibold transition ${brushValue === area.id ? 'border-white shadow-md' : 'border-transparent opacity-75 hover:opacity-100'} ${area.color}`}
                        >
                            {area.label}
                        </button>
                    ))}
                    <input
                        type="range"
                        min="0"
                        max="15"
                        value={brushValue}
                        onChange={(e) => setBrushValue(parseInt(e.target.value))}
                        className="col-span-2 w-full accent-sky-500"
                    />
                    <div className="col-span-2 text-[10px] text-slate-500">Area ID is stored on logic terrain and propagated to baked NavTile triangle areas.</div>
                </div>
            );
        }

        if (activeCategory === 'Blocked') {
            return (
                <div className="grid grid-cols-2 gap-2">
                    <button
                        onClick={() => {
                            setBrushValue(1);
                            setMode('Set');
                        }}
                        className={`rounded border p-2 text-xs font-semibold ${brushValue > 0 ? 'border-red-300 bg-red-700/70 text-red-50' : 'border-slate-700 bg-slate-900 text-slate-400'}`}
                    >
                        Block
                    </button>
                    <button
                        onClick={() => {
                            setBrushValue(0);
                            setMode('Set');
                        }}
                        className={`rounded border p-2 text-xs font-semibold ${brushValue === 0 ? 'border-emerald-300 bg-emerald-700/70 text-emerald-50' : 'border-slate-700 bg-slate-900 text-slate-400'}`}
                    >
                        Clear
                    </button>
                    <div className="col-span-2 text-[10px] text-slate-500">Baked navmesh excludes blocked cells.</div>
                </div>
            );
        }

        if (activeCategory === 'Biome') {
            return (
                <div className="grid grid-cols-2 gap-2">
                    {[
                        { id: 0, label: 'Dirt', color: 'bg-[#8B4513]' },
                        { id: 1, label: 'Sand', color: 'bg-[#F4A460]' },
                        { id: 2, label: 'Rock', color: 'bg-[#808080]' },
                        { id: 3, label: 'Grass', color: 'bg-[#3d6c2e]' },
                        { id: 4, label: 'Wasteland', color: 'bg-[#696969]' },
                        { id: 5, label: 'Swamp', color: 'bg-[#556B2F]' },
                    ].map((biome) => (
                        <button
                            key={biome.id}
                            onClick={() => {
                                setBrushValue(biome.id);
                                setMode('Set');
                            }}
                            className={`rounded border p-2 text-xs font-semibold transition ${brushValue === biome.id ? 'border-white shadow-md' : 'border-transparent opacity-75 hover:opacity-100'} ${biome.color}`}
                        >
                            {biome.label}
                        </button>
                    ))}
                </div>
            );
        }

        if (activeCategory === 'Vegetation') {
            return (
                <div className="grid grid-cols-2 gap-2">
                    {[
                        { id: 0, label: 'None' },
                        { id: 1, label: 'Small Tree' },
                        { id: 2, label: 'Big Tree' },
                        { id: 3, label: 'Dense' },
                    ].map((veg) => (
                        <button
                            key={veg.id}
                            onClick={() => {
                                setBrushValue(veg.id);
                                setMode('Set');
                            }}
                            className={`rounded border p-2 text-xs font-semibold transition ${brushValue === veg.id ? 'border-emerald-500 bg-emerald-600/25 text-emerald-200' : 'border-slate-700 bg-slate-900 text-slate-400 hover:bg-slate-800'}`}
                        >
                            {veg.label}
                        </button>
                    ))}
                </div>
            );
        }

        if (activeCategory === 'Layers') {
            return (
                <div className="space-y-2">
                    {[
                        { id: 'Snow', label: 'Snow', color: 'bg-white text-black' },
                        { id: 'Mud', label: 'Mud', color: 'bg-[#5c4033] text-white' },
                        { id: 'Ice', label: 'Ice', color: 'bg-cyan-200 text-black' },
                    ].map((layer) => (
                        <button
                            key={layer.id}
                            onClick={() => {
                                setActiveLayer(layer.id as TerrainLayerId);
                                setBrushValue(1);
                            }}
                            className={`flex w-full items-center justify-between rounded border p-2 text-xs font-semibold transition ${activeLayer === layer.id ? 'border-sky-400' : 'border-transparent opacity-75 hover:opacity-100'} ${layer.color}`}
                        >
                            <span>{layer.label}</span>
                            {activeLayer === layer.id ? <span className="rounded bg-black/20 px-1 text-[10px]">Active</span> : null}
                        </button>
                    ))}
                    <div className="text-[10px] text-slate-500">Raise adds the layer; Lower removes it.</div>
                </div>
            );
        }

        if (activeCategory === 'Territory') {
            return (
                <div className="space-y-2">
                    <input
                        type="range"
                        min="0"
                        max="255"
                        value={brushValue}
                        onChange={(e) => setBrushValue(parseInt(e.target.value))}
                        className="w-full accent-sky-500"
                    />
                    <div className="flex justify-between text-[10px] text-slate-400">
                        <button onClick={() => setBrushValue(0)} className="hover:text-white">Neutral</button>
                        <button onClick={() => setBrushValue(1)} className="hover:text-white">F1</button>
                        <button onClick={() => setBrushValue(128)} className="hover:text-white">F128</button>
                        <button onClick={() => setBrushValue(255)} className="hover:text-white">F255</button>
                    </div>
                </div>
            );
        }

        if (activeCategory === 'Obstacle') {
            return (
                <div className="space-y-2">
                    <select
                        value={obstacleTemplateId ?? ''}
                        onChange={(e) => setObstacleTemplate(e.target.value.length > 0 ? e.target.value : null)}
                        className={compactInputClass}
                        title="Obstacle template"
                    >
                        {templates.map((template: EntityTemplatePayload, i: number) => {
                            const id = String(template?.Id ?? template?.id ?? `template_${i}`);
                            return <option key={id} value={id}>{id}</option>;
                        })}
                    </select>
                    <div className="grid grid-cols-2 gap-2">
                        <button
                            onClick={() => setObstacleShape('Circle')}
                            className={`inline-flex items-center justify-center gap-1 rounded border p-2 text-xs font-semibold ${obstacleShape === 'Circle' ? 'border-orange-300 bg-orange-700/70 text-orange-50' : 'border-slate-700 bg-slate-900 text-slate-400'}`}
                        >
                            <Circle size={14} /> Circle
                        </button>
                        <button
                            onClick={() => setObstacleShape('Box')}
                            className={`inline-flex items-center justify-center gap-1 rounded border p-2 text-xs font-semibold ${obstacleShape === 'Box' ? 'border-orange-300 bg-orange-700/70 text-orange-50' : 'border-slate-700 bg-slate-900 text-slate-400'}`}
                        >
                            <Square size={14} /> Box
                        </button>
                    </div>
                    {obstacleShape === 'Circle' ? (
                        numberField('Radius cm', obstacleRadiusCm, setObstacleRadiusCm, { min: '1' })
                    ) : (
                        <div className="grid grid-cols-2 gap-2">
                            {numberField('Half W', obstacleHalfWidthCm, (value) => setObstacleHalfSizeCm(value, obstacleHalfHeightCm), { min: '1' })}
                            {numberField('Half H', obstacleHalfHeightCm, (value) => setObstacleHalfSizeCm(obstacleHalfWidthCm, value), { min: '1' })}
                        </div>
                    )}
                    <div className="text-[10px] text-slate-500">Set places or replaces. Lower erases.</div>
                </div>
            );
        }

        if (activeCategory === 'Entities') {
            return (
                <div className="space-y-2">
                    <select
                        value={selectedTemplateId ?? ''}
                        onChange={(e) => selectTemplate(e.target.value.length > 0 ? e.target.value : null)}
                        className={compactInputClass}
                        title="Template"
                    >
                        {templates.map((template: EntityTemplatePayload, i: number) => {
                            const id = String(template?.Id ?? template?.id ?? `template_${i}`);
                            return <option key={id} value={id}>{id}</option>;
                        })}
                    </select>
                    <div className="text-[10px] text-slate-500">Set places. Lower erases. Raise selects.</div>
                    {selectedEntityIndex != null && selectedEntityIndex >= 0 && selectedEntityIndex < spawnEntities.length ? (
                        <div className="space-y-2 rounded border border-slate-700 bg-slate-900/70 p-2">
                            <div className="text-xs text-slate-300">
                                Selected: {spawnEntities[selectedEntityIndex].template} @ ({spawnEntities[selectedEntityIndex].position.x},{spawnEntities[selectedEntityIndex].position.y})
                            </div>
                            <div className="text-[10px] text-slate-500">Overrides (componentName: JSON)</div>
                            {Object.keys(spawnEntities[selectedEntityIndex].overrides ?? {}).length === 0 ? (
                                <div className="text-[10px] text-slate-500">No overrides.</div>
                            ) : (
                                Object.entries(spawnEntities[selectedEntityIndex].overrides ?? {}).map(([key, value]) => (
                                    <div key={key} className="space-y-1">
                                        <div className="flex items-center justify-between">
                                            <div className="text-[11px] text-slate-200">{key}</div>
                                            <button
                                                onClick={() => deleteSelectedEntityOverride(key)}
                                                className="text-[10px] text-red-300 hover:text-red-200"
                                            >
                                                Delete
                                            </button>
                                        </div>
                                        <textarea
                                            className="h-20 w-full rounded border border-slate-700 bg-slate-950 p-1 font-mono text-[10px] text-slate-200"
                                            defaultValue={JSON.stringify(value, null, 2)}
                                            onBlur={(e) => updateSelectedEntityOverridesJson(key, e.target.value)}
                                        />
                                    </div>
                                ))
                            )}
                        </div>
                    ) : (
                        <div className="text-[10px] text-slate-500">No entity selected.</div>
                    )}
                </div>
            );
        }

        return (
            <input
                type="range"
                min="0"
                max="15"
                value={brushValue}
                onChange={(e) => setBrushValue(parseInt(e.target.value))}
                className="w-full accent-sky-500"
            />
        );
    };

    const estimateCard = (
        <div className="space-y-2">
            <div className={`rounded border p-3 text-xs ${
                navBakeState.phase === 'complete'
                    ? 'border-emerald-700/70 bg-emerald-950/40 text-emerald-100'
                    : navBakeState.phase === 'error' || navBakeState.phase === 'blocked'
                        ? 'border-red-700/70 bg-red-950/40 text-red-100'
                        : navBakeState.phase === 'baking' || navBakeState.phase === 'estimating'
                            ? 'border-sky-700/70 bg-sky-950/40 text-sky-100'
                            : 'border-slate-800 bg-slate-900/60 text-slate-300'
            }`}>
                <div className="flex items-center justify-between gap-2">
                    <div className="font-semibold tracking-wide">{navBakeState.title}</div>
                    <div className="text-[10px] uppercase tracking-wide opacity-70">{navBakeState.phase}</div>
                </div>
                <div className="mt-1 whitespace-pre-line text-[11px] opacity-90">{navBakeState.message}</div>
                <div className="mt-3 h-1.5 overflow-hidden rounded-full bg-black/30">
                    <div
                        className={`h-full transition-all duration-200 ${
                            navBakeState.phase === 'complete'
                                ? 'bg-emerald-400'
                                : navBakeState.phase === 'error' || navBakeState.phase === 'blocked'
                                    ? 'bg-red-400'
                                    : 'bg-sky-400'
                        }`}
                        style={{ width: `${Math.max(0, Math.min(100, navBakeState.progress))}%` }}
                    />
                </div>
            </div>
            {navEstimate ? (
                <div className={`rounded border p-3 text-xs ${
                    navEstimate.budgetStatusText === 'ok'
                        ? 'border-emerald-700/70 bg-emerald-950/40 text-emerald-100'
                        : navEstimate.budgetStatusText === 'large'
                            ? 'border-amber-700/70 bg-amber-950/40 text-amber-100'
                            : 'border-red-700/70 bg-red-950/40 text-red-100'
                }`}>
                    <div className="flex items-center justify-between gap-2">
                        <div className="font-semibold tracking-wide">{estimateStatusLabel}</div>
                        <div>{navEstimate.estimatedSecondsLow.toFixed(1)}s - {navEstimate.estimatedSecondsHigh.toFixed(1)}s</div>
                    </div>
                    <div className="mt-1 text-[11px] text-slate-200">{estimateStatusHint}</div>
                    <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-slate-200">
                        <div>tiles {navEstimate.targetTileCount}/{navEstimate.fullTileCount}</div>
                        <div>ops {navEstimate.bakeOperationCount}</div>
                        <div>layers {navEstimate.layerCount}</div>
                        <div>profiles {navEstimate.profileCount}</div>
                        <div>obstacles {navEstimate.obstacleCount}</div>
                        <div>workers {navEstimate.effectiveWorkers}</div>
                        <div>work {navEstimate.budgetWorkUnitCount.toLocaleString()}</div>
                        <div>columns {navEstimate.recastColumnBudgetTotal.toLocaleString()}</div>
                        <div>tile {navEstimate.tileWorldWidthCm}x{navEstimate.tileWorldHeightCm}cm</div>
                        <div>{(navEstimate.estimatedTileBytesLow / 1048576).toFixed(1)}-{(navEstimate.estimatedTileBytesHigh / 1048576).toFixed(1)}MB</div>
                    </div>
                    <div className="mt-2 font-mono text-[10px] text-slate-400">hash {navEstimate.estimateHash.slice(0, 12)}</div>
                    <div className="font-mono text-[10px] text-slate-500">terrain {navEstimate.terrainContentHash.slice(0, 12)}</div>
                    <div className="mt-2 whitespace-pre-line text-slate-300">{formatEstimateBudgetDetail(navEstimate)}</div>
                    {navEstimate.profiles.length > 0 ? (
                        <div className="mt-2 max-h-24 overflow-auto rounded bg-black/20 p-2">
                            {navEstimate.profiles.map((profile) => (
                                <div key={profile.profileId} className="flex justify-between gap-2 text-slate-300">
                                    <span>{profile.profileId}</span>
                                    <span>{profile.recastCellSizeCm.toFixed(1)}cm vox / {profile.maxSlopeDeg}deg</span>
                                </div>
                            ))}
                        </div>
                    ) : null}
                </div>
            ) : null}
            {navEstimateError ? (
                <div className="rounded border border-red-800 bg-red-950/40 p-3 text-xs text-red-100">
                    {navEstimateError}
                </div>
            ) : null}
        </div>
    );

    return (
        <div className="pointer-events-none absolute inset-0 z-40 text-slate-100">
            {loadingState.isLoading ? (
                <div className="pointer-events-auto absolute left-1/2 top-24 z-50 w-80 -translate-x-1/2 rounded-lg border border-slate-700 bg-slate-950/95 p-4 shadow-2xl backdrop-blur">
                    <div className="mb-3 flex items-center gap-3">
                        <div className="h-8 w-8 rounded-full border-4 border-sky-500 border-t-transparent animate-spin" />
                        <div>
                            <div className="text-sm font-semibold text-white">{loadingState.message}</div>
                            <div className="text-[10px] text-slate-500">{loadingState.progress}%</div>
                        </div>
                    </div>
                    <div className="h-2 overflow-hidden rounded-full bg-slate-800">
                        <div className="h-full bg-sky-500 transition-all duration-100" style={{ width: `${loadingState.progress}%` }} />
                    </div>
                    {loadingState.message.startsWith('Baking NavTiles') ? (
                        <button
                            onClick={() => {
                                navAbortRef.current?.abort();
                                navAbortRef.current = null;
                                setLoading(false);
                            }}
                            className="mt-4 w-full rounded bg-red-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-red-600"
                        >
                            Cancel Bake
                        </button>
                    ) : null}
                </div>
            ) : null}

            <header className={`${panelClass} absolute left-4 right-4 top-4 flex min-h-16 items-center gap-3 px-3 py-2`}>
                <div className="mr-1 min-w-36">
                    <div className="text-sm font-semibold text-white">Ludots Editor</div>
                    <div className="text-[10px] text-slate-500">Navigation authoring</div>
                </div>
                <select
                    value={selectedModId ?? ''}
                    onChange={(e) => selectMod(e.target.value).catch((err: unknown) => alert(errorMessage(err)))}
                    className={`${compactInputClass} min-w-36`}
                    title="Mod"
                >
                    {mods.map((mod) => (
                        <option key={mod.id} value={mod.id}>{mod.id}</option>
                    ))}
                </select>
                <select
                    value={selectedMapId ?? ''}
                    onChange={(e) => selectMap(e.target.value)}
                    className={`${compactInputClass} min-w-44 flex-1`}
                    title="Map"
                >
                    {maps.map((id) => (
                        <option key={id} value={id}>
                            {id}{mapInfoById.get(id)?.boards?.length ? ` (${mapInfoById.get(id)?.boards.length} boards)` : ''}{mapInfoById.get(id)?.canBake ? ' nav' : ''}
                        </option>
                    ))}
                </select>
                <select
                    value={selectedBoardName ?? ''}
                    onChange={(e) => selectBoard(e.target.value)}
                    className={`${compactInputClass} min-w-36`}
                    title={`Board stack: ${boardOptionsLabel}`}
                    disabled={!selectedMapId || selectedBoards.length === 0}
                >
                    {selectedBoards.length === 0 ? <option value="">No board</option> : null}
                    {selectedBoards.map((board) => (
                        <option key={board.name} value={board.name}>
                            {board.name} / {board.spatialType ?? 'Unknown'}{board.canBake ? ' / nav' : ''}{board.canEditTerrain ? ' / edit' : ''}
                        </option>
                    ))}
                </select>
                <div className={`hidden min-w-60 rounded border px-2 py-1 text-[10px] xl:block ${canvasMapLoaded ? 'border-sky-700/50 bg-sky-950/30 text-sky-200' : 'border-slate-800 bg-slate-900 text-slate-500'}`}>
                    Canvas: {loadedCanvasLabel}
                </div>
                <button
                    onClick={() => setShowNewMap(true)}
                    className={darkButtonClass}
                    title="Create a new in-editor map"
                >
                    <Plus size={14} className="text-yellow-300" />
                    New
                </button>
                <button
                    onClick={() => loadSelectedMap().catch((err: unknown) => alert(errorMessage(err)))}
                    className={darkButtonClass}
                    title={boardOpenTitle}
                    disabled={boardOpenDisabled}
                >
                    <FolderOpen size={14} className="text-sky-300" />
                    Open
                </button>
                <button
                    onClick={() => saveSelectedMap().catch((err: unknown) => alert(errorMessage(err)))}
                    className={darkButtonClass}
                    title="Save MapConfig and terrain to selected mod via Bridge"
                    disabled={!canvasMapLoaded}
                >
                    <Save size={14} className="text-emerald-300" />
                    Save
                </button>
            </header>

            <aside className={`${panelClass} absolute bottom-4 right-4 top-[92px] flex w-[360px] flex-col overflow-hidden`}>
                <div className="border-b border-slate-800 px-3 py-2">
                    <div className={sectionTitleClass}>Map And Board</div>
                    <div className="mt-1 truncate text-xs text-slate-300">{selectedMapId ?? 'No map selected'}</div>
                </div>
                <div className="space-y-4 overflow-auto p-3">
                    <Minimap embedded className="border-slate-800/90 bg-slate-900/60 shadow-none" />

                    <section className="space-y-2">
                        <div className="flex items-center justify-between">
                            <div className={sectionTitleClass}>Map Properties</div>
                            <HardDrive size={14} className="text-slate-500" />
                        </div>
                        <div className="grid grid-cols-2 gap-2 text-xs">
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Topology</div>
                                <div className="font-medium text-slate-200">{boardPropertyTopology}</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Chunks</div>
                                <div className="font-medium text-slate-200">{boardPropertyChunks}</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Grid cell</div>
                                <div className="font-medium text-slate-200">{boardPropertyCellSizeCm} cm</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Chunk size</div>
                                <div className="font-medium text-slate-200">{boardPropertyChunkSizeCells} cells</div>
                            </div>
                        </div>
                        <div className={`rounded border p-2 text-[11px] ${selectedMapInfo?.canBake ? 'border-emerald-700/60 bg-emerald-950/30 text-emerald-200' : 'border-amber-700/60 bg-amber-950/30 text-amber-200'}`}>
                            <div>{selectedBoardName ?? 'No board'} / {selectedBoardInfo?.spatialType ?? selectedMapInfo?.spatialType ?? 'Unknown'} / {selectedBoardInfo?.reason ?? selectedMapInfo?.reason ?? 'Select a map from the top bar.'}</div>
                            <div className="mt-1 text-slate-400">Nav dirty chunks: {dirtyChunkCount}</div>
                        </div>
                    </section>

                    <section className="space-y-2">
                        <div className="flex items-center justify-between">
                            <div className={sectionTitleClass}>Board Editor</div>
                            <Settings2 size={14} className="text-slate-500" />
                        </div>
                        <div className="grid grid-cols-2 gap-2 text-xs">
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Grid cell</div>
                                <div className="font-medium text-slate-200">{boardPropertyCellSizeCm} cm</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Hex edge</div>
                                <div className="font-medium text-slate-200">{boardPropertyHexEdgeLengthCm} cm</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Topology</div>
                                <div className="font-medium text-slate-200">{boardPropertyTopology}</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                                <div className="text-[10px] text-slate-500">Chunks</div>
                                <div className="font-medium text-slate-200">{boardPropertyChunks}</div>
                            </div>
                        </div>
                        <div className="space-y-2 rounded border border-slate-800 bg-slate-900/45 p-2">
                            <label className={fieldLabelClass}>
                                Grid cell cm
                                <input
                                    type="number"
                                    min="1"
                                    value={editBoardCellSizeCm}
                                    onChange={(e) => setEditBoardCellSizeCm(parseInt(e.target.value) || CellCm)}
                                    className={inputClass}
                                />
                            </label>
                            <label className={fieldLabelClass}>
                                Hex edge cm
                                <input
                                    type="number"
                                    min="1"
                                    value={editBoardHexEdgeLengthCm}
                                    onChange={(e) => setEditBoardHexEdgeLengthCm(parseInt(e.target.value) || DefaultHexEdgeLengthCm)}
                                    className={inputClass}
                                    disabled={boardPropertyTopology !== 'HexGrid'}
                                    title={boardPropertyTopology === 'HexGrid' ? 'Hex edge length for this board.' : 'Hex edge length only applies to HexGrid boards.'}
                                />
                            </label>
                            <div className="grid grid-cols-2 gap-2 rounded border border-slate-800 bg-slate-950/70 p-2 text-[10px] text-slate-400">
                                <div>
                                    <div className="uppercase tracking-wide text-slate-600">Board cells</div>
                                    <div className="font-mono text-slate-200">{boardScalePreviewWidthCells.toLocaleString()} x {boardScalePreviewHeightCells.toLocaleString()}</div>
                                </div>
                                <div>
                                    <div className="uppercase tracking-wide text-slate-600">World extent</div>
                                    <div className="font-mono text-slate-200">{(boardScalePreviewWidthCm / 100).toLocaleString()}m x {(boardScalePreviewHeightCm / 100).toLocaleString()}m</div>
                                </div>
                                <div>
                                    <div className="uppercase tracking-wide text-slate-600">Terrain/NavTile</div>
                                    <div className="font-mono text-slate-200">{boardPropertyChunkSizeCells} cells / {(boardScalePreviewChunkCm / 100).toLocaleString()}m</div>
                                </div>
                                <div>
                                    <div className="uppercase tracking-wide text-slate-600">Hex geometry</div>
                                    <div className="font-mono text-slate-200">{boardPropertyTopology === 'HexGrid' ? `${boardScalePreviewHexEdgeLengthCm}cm edge` : 'not used'}</div>
                                </div>
                                <div className="col-span-2 rounded border border-slate-800 bg-slate-900/60 px-2 py-1 text-slate-500">
                                    {boardScaleCellChanged || boardScaleHexChanged
                                        ? 'Scale change: loaded canvas metrics update, Recast tiles are invalidated, dirty chunks need bake.'
                                        : 'Scale unchanged: Apply only persists changed board toggles.'}
                                </div>
                            </div>
                            <label className="flex items-center gap-2 text-sm text-slate-300">
                                <input
                                    type="checkbox"
                                    checked={editBoardNavigationEnabled}
                                    onChange={(e) => setEditBoardNavigationEnabled(e.target.checked)}
                                />
                                <span>Navigation enabled</span>
                            </label>
                            <button
                                onClick={() => handleUpdateBoard()}
                                className={darkButtonClass}
                                title={!selectedBoardName ? 'Select a board first.' : boardScaleHasChanges ? 'Persist changed board scale and nav settings through Bridge' : 'No board settings changed.'}
                                disabled={!selectedBoardName || !boardScaleHasChanges}
                            >
                                <Save size={13} className="text-emerald-300" />
                                Apply Board Settings
                            </button>
                            <div className="text-[10px] text-slate-500">
                                Board edits rewrite MapConfig only. If the open board matches, the canvas scale and nav cache refresh together.
                            </div>
                        </div>
                    </section>

                    <section className="space-y-2">
                        <div className="flex items-center justify-between">
                            <div className={sectionTitleClass}>Board Session</div>
                            <FolderOpen size={14} className="text-slate-500" />
                        </div>
                        <div className={`rounded border p-2 text-[11px] ${boardSessionTone}`}>
                            <div className="grid grid-cols-[64px_1fr] gap-x-2 gap-y-1">
                                <span className="text-[10px] uppercase tracking-wide text-slate-500">Selected</span>
                                <span className="truncate font-mono">{selectedTargetLabel}</span>
                                <span className="text-[10px] uppercase tracking-wide text-slate-500">Canvas</span>
                                <span className="truncate font-mono">{loadedTargetLabel}</span>
                            </div>
                            <div className="mt-2 text-[10px] opacity-90">{boardSessionMessage}</div>
                        </div>
                        <div className="grid grid-cols-1 gap-2">
                            <button
                                onClick={() => loadSelectedMap().catch((err: unknown) => alert(errorMessage(err)))}
                                className={darkButtonClass}
                                title={boardOpenTitle}
                                disabled={boardOpenDisabled}
                            >
                                <FolderOpen size={13} className="text-sky-300" />
                                Open Selected
                            </button>
                            <div className="rounded border border-slate-800 bg-slate-900/45 px-2 py-1.5 text-[10px] text-slate-500">
                                Repository save is owned by the top bar Save action.
                            </div>
                        </div>
                        <div className="rounded border border-slate-800 bg-slate-900/45 p-2">
                            <div className="mb-2 flex items-center justify-between">
                                <div className={sectionTitleClass}>Terrain Files</div>
                                <span className="text-[10px] text-slate-500">not repo save</span>
                            </div>
                            <div className="grid grid-cols-2 gap-2">
                                <label className={darkButtonClass} title="Import local map_data.bin as a local editor draft. Open a repo board and use top bar Save to write it.">
                                    <Upload size={13} className="text-sky-300" />
                                    Import Bin
                                    <input type="file" className="hidden" onChange={handleUpload} />
                                </label>
                                <button
                                    onClick={handleDownload}
                                    className={darkButtonClass}
                                    title="Export current canvas terrain as map_data.bin. This downloads a file and does not save the repo."
                                    disabled={!canvasHasAnySession}
                                >
                                    <Download size={13} className="text-emerald-300" />
                                    Export Bin
                                </button>
                            </div>
                        </div>
                    </section>

                    <section className="space-y-2">
                        <div className="flex items-center justify-between">
                            <div className={sectionTitleClass}>Board Stack</div>
                            <Layers size={14} className="text-slate-500" />
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                            <button
                                onClick={() => setShowAddBoard(true)}
                                className={darkButtonClass}
                                title="Add a Grid or HexGrid board to the selected map"
                                disabled={!selectedMapId}
                            >
                                <Plus size={13} className="text-sky-300" />
                                Add Board
                            </button>
                            <button
                                onClick={handleDeleteSelectedBoard}
                                className="inline-flex items-center justify-center gap-2 rounded border border-red-900/70 bg-red-950/40 px-2 py-1.5 text-xs font-medium text-red-100 transition hover:bg-red-900/50 disabled:cursor-not-allowed disabled:opacity-40"
                                title={deleteBoardTitle}
                                disabled={deleteBoardDisabled}
                            >
                                <Trash2 size={13} />
                                Delete Selected
                            </button>
                        </div>
                        {selectedBoards.length > 0 ? (
                            <div className="space-y-1.5">
                                {selectedBoards.map((board) => {
                                    const selected = board.name === selectedBoardName;
                                    const loaded = canvasMapLoaded && board.name === loadedBoardName;
                                    return (
                                        <button
                                            key={board.name}
                                            onClick={() => selectBoard(board.name)}
                                            className={`w-full rounded border p-2 text-left transition ${
                                                selected
                                                    ? 'border-sky-600 bg-sky-950/40 text-sky-100'
                                                    : 'border-slate-800 bg-slate-900/60 text-slate-300 hover:border-slate-600'
                                            }`}
                                        >
                                            <div className="flex items-center justify-between gap-2">
                                                <span className="truncate text-xs font-semibold">{board.name}</span>
                                                {loaded ? <span className="rounded bg-sky-600/30 px-1.5 py-0.5 text-[9px] text-sky-100">loaded</span> : null}
                                            </div>
                                            <div className="mt-1 flex flex-wrap gap-1 text-[9px] text-slate-400">
                                                <span>{board.spatialType ?? 'Unknown'}</span>
                                                <span>{board.widthChunks}x{board.heightChunks}</span>
                                                <span>{board.cellSizeCm}cm</span>
                                                <span>{board.hexEdgeLengthCm}cm hex</span>
                                                <span>{board.navigationEnabled ? 'nav' : 'no-nav'}</span>
                                                <span>{board.canEditTerrain ? 'edit' : 'view'}</span>
                                                <span>{board.dataFileExists ? 'data' : 'no-data'}</span>
                                            </div>
                                            <div className="mt-1 truncate text-[9px] text-slate-500">{board.reason}</div>
                                        </button>
                                    );
                                })}
                            </div>
                        ) : (
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-2 text-xs text-slate-500">No boards declared on this map.</div>
                        )}
                    </section>

                    <section className="hidden">
                        <div className={sectionTitleClass}>Brush Inspector</div>
                        {!canvasCanEdit ? (
                            <div className="rounded border border-amber-800/70 bg-amber-950/25 p-2 text-[10px] text-amber-100">
                                Open the selected board in Board Session before editing the 3D canvas.
                            </div>
                        ) : null}
                        <div className="grid grid-cols-5 gap-1">
                            {categories.map((category) => (
                                <button
                                    key={category.id}
                                    onClick={() => canvasCanEdit && setCategory(category.id)}
                                    disabled={!canvasCanEdit}
                                    className={`flex h-11 flex-col items-center justify-center gap-0.5 rounded border px-1 transition ${
                                        activeCategory === category.id
                                            ? 'border-sky-500/70 bg-sky-600/25 text-sky-200'
                                            : 'border-slate-800 bg-slate-900 text-slate-400 hover:border-slate-600 hover:bg-slate-800'
                                    }`}
                                    title={category.id}
                                >
                                    {category.icon}
                                    <span className="text-[9px] font-medium">{category.label}</span>
                                </button>
                            ))}
                        </div>
                        <div className="grid grid-cols-4 gap-1">
                            {modes.map((mode) => (
                                <button
                                    key={mode.id}
                                    onClick={() => canvasCanEdit && setMode(mode.id)}
                                    disabled={!canvasCanEdit}
                                    className={`flex h-10 flex-col items-center justify-center gap-0.5 rounded border px-1 transition ${
                                        activeMode === mode.id
                                            ? 'border-violet-500/70 bg-violet-600/25 text-violet-200'
                                            : 'border-slate-800 bg-slate-900 text-slate-400 hover:border-slate-600 hover:bg-slate-800'
                                    }`}
                                    title={mode.id}
                                >
                                    {mode.icon}
                                    <span className="text-[9px] font-medium">{mode.label}</span>
                                </button>
                            ))}
                        </div>
                        <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                            <div className="mb-2 flex items-center justify-between text-xs text-slate-300">
                                <span>{activeCategory} / {activeMode}</span>
                                <span>Value {brushValue}</span>
                            </div>
                            <label className={fieldLabelClass}>
                                Brush Size: {brushSize}
                                <input
                                    type="range"
                                    min="1"
                                    max="10"
                                    value={brushSize}
                                    onChange={(e) => setBrushSize(parseInt(e.target.value))}
                                    disabled={!canvasCanEdit}
                                    className="mt-2 w-full accent-sky-500"
                                />
                            </label>
                        </div>
                        <fieldset disabled={!canvasCanEdit} className={canvasCanEdit ? '' : 'pointer-events-none'}>
                            {renderBrushValueControls()}
                        </fieldset>
                        <div className="rounded border border-slate-800 bg-slate-900/60 p-2 text-[10px] text-slate-500">
                            {canvasCanEdit
                                ? `Middle click pans. Right click rotates. Left click ${activeCategory === 'Entities' ? 'places, erases, or selects' : activeCategory === 'Obstacle' ? 'places or erases' : 'paints'}.`
                                : 'Canvas editing is locked until the selected board is opened.'}
                        </div>
                    </section>

                    <section className="hidden">
                        <div className={sectionTitleClass}>View</div>
                        <div className="flex gap-2">
                            <button
                                onClick={toggleGrid}
                                className={`${iconToggleClass} ${showGrid ? 'border-violet-500/70 bg-violet-600/25 text-violet-100' : ''}`}
                                title="Toggle Grid"
                            >
                                <Grid size={16} />
                            </button>
                            <button
                                onClick={toggleChunkBorders}
                                className={`${iconToggleClass} ${showChunkBorders ? 'border-violet-500/70 bg-violet-600/25 text-violet-100' : ''}`}
                                title="Toggle Chunk Borders"
                            >
                                <BoxSelect size={16} />
                            </button>
                            <button
                                onClick={toggleNavMesh}
                                className={`${iconToggleClass} ${showNavMesh ? 'border-emerald-500/70 bg-emerald-600/25 text-emerald-100' : ''}`}
                                title="Toggle NavMesh Visualization"
                            >
                                <Eye size={16} />
                            </button>
                        </div>
                    </section>
                </div>
            </aside>

            <aside className={`${panelClass} absolute bottom-4 left-4 top-[92px] flex w-[380px] flex-col overflow-hidden`}>
                <div className="border-b border-slate-800 px-3 py-2">
                    <div className="flex items-center justify-between">
                        <div>
                            <div className={sectionTitleClass}>Navigation SSOT</div>
                            <div className="mt-1 text-xs text-slate-300">{canvasMapLoaded ? `${bakeMapId} / ${bakeBoardName}` : 'No loaded board'}</div>
                        </div>
                        <Footprints size={18} className="text-orange-300" />
                    </div>
                </div>
                <div className="space-y-4 overflow-auto p-3">
                    <section className="space-y-2">
                        <div className="grid grid-cols-3 gap-1 rounded border border-slate-800 bg-slate-900/40 p-1">
                            {[
                                { id: 'bake' as const, label: 'Bake', icon: <Footprints size={13} /> },
                                { id: 'simulation' as const, label: 'Sim', icon: <Route size={13} /> },
                                { id: 'config' as const, label: 'Config', icon: <Settings2 size={13} /> },
                            ].map((tab) => (
                                <button
                                    key={tab.id}
                                    onClick={() => setNavPanelTab(tab.id)}
                                    className={`inline-flex items-center justify-center gap-1 rounded px-2 py-1.5 text-xs font-semibold transition ${
                                        navPanelTab === tab.id
                                            ? 'bg-sky-700 text-white'
                                            : 'text-slate-400 hover:bg-slate-800 hover:text-slate-100'
                                    }`}
                                >
                                    {tab.icon}
                                    {tab.label}
                                </button>
                            ))}
                        </div>
                        <div className="rounded border border-slate-800 bg-slate-900/40 p-2 text-[11px] text-slate-400">
                            SSOT: this panel owns nav bake, simulation, and navigation config. Map/board plus minimap live in the right panel; brush authoring lives in the bottom rail. Visual overlay is baked Recast NavTile (.ntil) triangles; simulation calls C# Core NavQueryService backed by DotRecast Detour.
                        </div>
                    </section>

                    <section className={`space-y-3 ${navPanelTab === 'bake' ? '' : 'hidden'}`}>
                        <div className={sectionTitleClass}>Bake Controls</div>
                        <div className={`rounded border px-2 py-1.5 text-xs ${
                            canvasMapLoaded
                                ? 'border-sky-700/50 bg-sky-950/30 text-sky-100'
                                : 'border-amber-800/70 bg-amber-950/20 text-amber-100'
                        }`}>
                            <div className="grid grid-cols-[72px_1fr] gap-x-2 gap-y-1">
                                <span className="text-[10px] uppercase tracking-wide text-slate-500">Selected</span>
                                <span className="truncate font-mono">{selectedTargetLabel}</span>
                                <span className="text-[10px] uppercase tracking-wide text-slate-500">Loaded</span>
                                <span className="truncate font-mono">{loadedTargetLabel}</span>
                            </div>
                        </div>
                        {!selectedNavReady ? (
                            <div className="rounded border border-amber-800/70 bg-amber-950/30 p-2 text-[11px] text-amber-100">
                                <div>{navDisabledReason}</div>
                                <div className="mt-1 text-[10px] text-amber-100/75">Board open/save lives in the right Map And Board panel.</div>
                            </div>
                        ) : null}
                        <div className="grid grid-cols-2 gap-2">
                            <select
                                value={navScope === 'full' ? 'full' : (navIncludeNeighbors ? 'dirtyN' : 'dirty')}
                                onChange={(e) => {
                                    const value = e.target.value;
                                    if (value === 'full') {
                                        setNavScope('full');
                                        setNavIncludeNeighbors(true);
                                    } else if (value === 'dirtyN') {
                                        setNavScope('dirty');
                                        setNavIncludeNeighbors(true);
                                    } else {
                                        setNavScope('dirty');
                                        setNavIncludeNeighbors(false);
                                    }
                                }}
                                className={compactInputClass}
                                title={`Nav bake scope (dirty=${dirtyChunkCount})`}
                            >
                                <option value="dirtyN">{`Dirty+N (${dirtyChunkCount})`}</option>
                                <option value="dirty">{`Dirty (${dirtyChunkCount})`}</option>
                                <option value="full">Full</option>
                            </select>
                            <button
                                onClick={() => setNavParallel(!navParallel)}
                                className={`rounded border px-2 py-1 text-xs ${navParallel ? 'border-slate-600 bg-slate-800 text-slate-100' : 'border-slate-800 bg-slate-900 text-slate-500'}`}
                                title={`Parallel: ${navParallel ? 'on' : 'off'}`}
                            >
                                Parallel {navParallel ? 'On' : 'Off'}
                            </button>
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                            <button
                                onClick={handleEstimateNavTilesLocal}
                                className="inline-flex items-center justify-center gap-2 rounded bg-sky-700 px-3 py-2 text-xs font-semibold text-white hover:bg-sky-600 disabled:cursor-not-allowed disabled:opacity-45"
                                title={selectedNavReady ? 'Estimate NavTiles via local bridge' : navDisabledReason}
                                disabled={!selectedNavReady}
                            >
                                Estimate
                            </button>
                            <button
                                onClick={handleBakeNavTilesLocal}
                                className="inline-flex items-center justify-center gap-2 rounded bg-orange-700 px-3 py-2 text-xs font-semibold text-white hover:bg-orange-600 disabled:cursor-not-allowed disabled:opacity-45"
                                title={selectedNavReady ? 'Bake NavTiles via local bridge and load into editor' : navDisabledReason}
                                disabled={bakeButtonDisabled}
                            >
                                Bake
                            </button>
                        </div>
                        <div className="rounded border border-slate-800 bg-slate-900/45 p-2">
                            <div className="mb-2 flex items-center justify-between">
                                <div className={sectionTitleClass}>Nav Artifacts</div>
                                <span className="text-[10px] text-slate-500">visual/debug</span>
                            </div>
                            <div className="grid grid-cols-2 gap-2">
                                <button
                                    onClick={handleBakeNavTiles}
                                    className={darkButtonClass}
                                    title={canvasMapLoaded ? 'Export map_data.bin + dirty list, then copy a CLI bake command. This does not save the repo.' : navDisabledReason}
                                    disabled={!canvasMapLoaded}
                                >
                                    <Footprints size={13} className="text-orange-300" />
                                    CLI
                                </button>
                                <button
                                    onClick={clearBakedNavTiles}
                                    className={darkButtonClass}
                                    title={bakedNavTiles.size > 0 ? 'Clear visual/query NavTiles from this editor session. This does not delete repo files.' : 'No NavTiles loaded'}
                                    disabled={bakedNavTiles.size === 0}
                                >
                                    Clear Tiles
                                </button>
                            </div>
                        </div>
                        <div>
                            {estimateCard}
                        </div>
                    </section>

                    <section className={`space-y-3 ${navPanelTab === 'bake' ? '' : 'hidden'}`}>
                        <div className="flex items-center justify-between">
                            <div className={sectionTitleClass}>Bake Params</div>
                            <SlidersHorizontal size={14} className="text-slate-500" />
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                            {numberField('Height Scale', navHeightScale, (value) => setNavHeightScale(value || 0.1), { step: '0.1', min: '0.1' })}
                            {numberField('Min Up Dot', navMinUpDot, setNavMinUpDot, { step: '0.05', min: '-1', max: '1' })}
                            {numberField('Cliff', navCliffThreshold, (value) => setNavCliffThreshold(Math.max(0, Math.floor(value || 0))), { step: '1', min: '0' })}
                            {numberField('Workers', navMaxDegree, (value) => setNavMaxDegree(Math.max(1, Math.floor(value || 1))), { step: '1', min: '1' })}
                            <div className="col-span-2 rounded border border-slate-800 bg-slate-900/50 p-2 text-[10px] text-slate-500">
                                NavTile binary format is fixed by Core. Agent size comes from the selected profile radius.
                            </div>
                        </div>
                    </section>

                    <section className={`space-y-3 ${navPanelTab === 'simulation' ? '' : 'hidden'}`}>
                        <div className="flex items-center justify-between">
                            <div className={sectionTitleClass}>Path Simulation</div>
                            <div className="max-w-44 text-right text-[10px] leading-tight text-slate-500">
                                {availableNavPayloads.length} query tile(s) for {navQueryProfileId || 'profile'} / L{navQueryLayer}; {flatBaselinePayloadCount} flat baseline
                            </div>
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                            <label className={fieldLabelClass}>
                                Profile
                                <select
                                    value={navQueryProfileId}
                                    onChange={(e) => {
                                        setNavQueryProfileId(e.target.value);
                                        clearNavSimulation();
                                        setNavQueryState(idleNavQueryState);
                                    }}
                                    className={inputClass}
                                >
                                    {bakeProfiles.length === 0 ? <option value="">No profiles</option> : null}
                                    {bakeProfiles.map((profile: NavBakeProfilePayload, i: number) => {
                                        const id = String(profile?.id ?? profile?.Id ?? `profile_${i}`);
                                        return <option key={id} value={id}>{id}</option>;
                                    })}
                                </select>
                            </label>
                            <label className={fieldLabelClass}>
                                Layer
                                <select
                                    value={String(navQueryLayer)}
                                    onChange={(e) => {
                                        setNavQueryLayer(Number(e.target.value));
                                        clearNavSimulation();
                                        setNavQueryState(idleNavQueryState);
                                    }}
                                    className={inputClass}
                                >
                                    {navLayers.length === 0 ? <option value="0">0</option> : null}
                                    {navLayers.map((layer: NavLayerPayload, i: number) => {
                                        const value = Number(layer?.layer ?? layer?.Layer ?? 0);
                                        const id = String(layer?.id ?? layer?.Id ?? `Layer${i}`);
                                        return <option key={`${id}-${value}`} value={String(value)}>{id} ({value})</option>;
                                    })}
                                </select>
                            </label>
                        </div>
                        <div className="rounded border border-sky-800/60 bg-sky-950/25 p-2 text-[11px] text-sky-100">
                            <div className="flex items-center justify-between gap-2">
                                <span className="font-semibold">Shown and queried: {navQueryProfileId || 'no profile'} / layer {navQueryLayer}</span>
                                <span>{selectedAgentRadiusCm > 0 ? `${selectedAgentRadiusCm} cm radius` : 'radius unknown'}</span>
                            </div>
                            <div className="mt-1 text-sky-200/75">
                                Recast erodes the mesh by agent radius/climb/slope
                                {selectedBakeProfile
                                    ? ` (climb ${Number(selectedBakeProfile.maxClimbCm ?? selectedBakeProfile.MaxClimbCm ?? 0)} cm, slope ${Number(selectedBakeProfile.maxSlopeDeg ?? selectedBakeProfile.MaxSlopeDeg ?? 0)} deg)`
                                    : ''}
                                , so different profile sizes should have different visible navmesh edges.
                            </div>
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                            <div className="rounded border border-slate-800 bg-slate-900/70 p-2 text-left text-slate-300">
                                <div className="flex items-center justify-between gap-2">
                                    <span className="inline-flex items-center gap-1 text-xs font-semibold"><MapPin size={13} /> Start</span>
                                    <span className="font-mono text-[10px] text-slate-400">{navQueryStartCell.col},{navQueryStartCell.row}</span>
                                </div>
                                <div className="mt-1 text-[10px] text-slate-500">Left-click the canvas in Sim.</div>
                            </div>
                            <div className="rounded border border-slate-800 bg-slate-900/70 p-2 text-left text-slate-300">
                                <div className="flex items-center justify-between gap-2">
                                    <span className="inline-flex items-center gap-1 text-xs font-semibold"><Crosshair size={13} /> Goal</span>
                                    <span className="font-mono text-[10px] text-slate-400">{navQueryGoalCell.col},{navQueryGoalCell.row}</span>
                                </div>
                                <div className="mt-1 text-[10px] text-slate-500">Right-click the canvas in Sim.</div>
                            </div>
                        </div>
                        <div className="grid grid-cols-[1fr_auto] gap-2">
                            <label className={fieldLabelClass}>
                                Max Portals
                                <input type="number" min="1" value={navQueryMaxPortals} onChange={(e) => setNavQueryMaxPortals(Math.max(1, Math.floor(Number(e.target.value) || 1)))} className={inputClass} />
                            </label>
                            <button
                                onClick={handleSimulateNavPath}
                                className="mt-4 inline-flex h-8 items-center justify-center rounded bg-emerald-700 px-3 text-xs font-semibold text-white hover:bg-emerald-600 disabled:cursor-not-allowed disabled:opacity-45"
                                disabled={!navQueryReady || navQueryState.phase === 'querying'}
                                title={navQueryReady ? 'Run real C# Core NavQueryService path query' : navQueryDisabledReason}
                            >
                                <Play size={13} />
                                Simulate Path
                            </button>
                        </div>
                        <div className={`rounded border p-3 text-xs ${
                            navQueryState.phase === 'complete'
                                ? 'border-emerald-700/70 bg-emerald-950/40 text-emerald-100'
                                : navQueryState.phase === 'error'
                                    ? 'border-red-700/70 bg-red-950/40 text-red-100'
                                    : navQueryState.phase === 'querying'
                                        ? 'border-sky-700/70 bg-sky-950/40 text-sky-100'
                                        : 'border-slate-800 bg-slate-900/60 text-slate-300'
                        }`}>
                            <div className="flex items-center justify-between">
                                <span className="font-semibold">{navQueryState.title}</span>
                                {navSimulation ? <span>{Number(navSimulation.elapsedMs ?? 0).toFixed(3)} ms</span> : null}
                            </div>
                            <div className="mt-1 whitespace-pre-line text-[11px] opacity-90">{navQueryState.message}</div>
                            {navSimulation ? (
                                <div className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-[11px] text-slate-200">
                                    <div>Status {navSimulation.status}</div>
                                    <div>Points {navSimulation.points?.length ?? 0}</div>
                                    <div>Cost {Number(navSimulation.travelCost ?? 0).toFixed(2)}</div>
                                    <div>Layer {navSimulation.layer}</div>
                                    <div className="col-span-2 truncate">Engine {navSimulation.engine}</div>
                                </div>
                            ) : null}
                            {!navQueryReady && navQueryState.phase === 'idle' ? (
                                <div className="mt-2 text-[10px] text-slate-500">{navQueryDisabledReason}</div>
                            ) : null}
                        </div>
                    </section>

                    <section className={`space-y-3 ${navPanelTab === 'config' ? '' : 'hidden'}`}>
                        <div className="flex items-center justify-between gap-2">
                            <div className={sectionTitleClass}>Navigation Config</div>
                            <div className="flex gap-1">
                                <button
                                    onClick={handleReloadNavigationConfig}
                                    className="rounded border border-slate-700 bg-slate-900 p-1.5 text-slate-300 hover:bg-slate-800"
                                    title="Reload navigation config"
                                    disabled={!selectedModId}
                                >
                                    <RefreshCw size={13} />
                                </button>
                                <button
                                    onClick={handleSaveNavigationConfig}
                                    className="rounded bg-emerald-700 p-1.5 text-white hover:bg-emerald-600 disabled:opacity-45"
                                    title="Save navigation config"
                                    disabled={!selectedModId || !navEditorConfig}
                                >
                                    <Save size={13} />
                                </button>
                            </div>
                        </div>

                        {navEditorConfig ? (
                            <div className="space-y-3 text-xs">
                                <div className="space-y-2">
                                    <div className="flex items-center justify-between">
                                        <div className="text-[10px] uppercase tracking-wide text-slate-500">Agent Profiles</div>
                                        <button onClick={addAgentProfile} className="rounded border border-slate-700 bg-slate-900 px-2 py-0.5 text-slate-300">+</button>
                                    </div>
                                    {agentProfiles.length === 0 ? <div className="rounded border border-slate-800 bg-slate-900/60 p-2 text-xs text-slate-500">No agent profiles.</div> : null}
                                    {agentProfiles.map((profile: NavAgentProfilePayload, i: number) => (
                                        <div key={`${profile.id ?? i}-agent`} className="grid grid-cols-3 gap-1 rounded border border-slate-800 bg-slate-900/60 p-2">
                                            <input value={textValue(profile.id)} onChange={(e) => updateAgentProfileField(i, 'id', e.target.value, false)} className="col-span-3 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="radiusCm" type="number" value={numericValue(profile.radiusCm, 0)} onChange={(e) => updateAgentProfileField(i, 'radiusCm', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="heightCm" type="number" value={numericValue(profile.heightCm, 0)} onChange={(e) => updateAgentProfileField(i, 'heightCm', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="layer" type="number" value={numericValue(profile.layer, 0)} onChange={(e) => updateAgentProfileField(i, 'layer', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="clearanceCm" type="number" value={numericValue(profile.clearanceCm, 0)} onChange={(e) => updateAgentProfileField(i, 'clearanceCm', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="mass" type="number" step="0.1" value={numericValue(profile.mass, 1)} onChange={(e) => updateAgentProfileField(i, 'mass', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                        </div>
                                    ))}
                                </div>

                                <div className="grid grid-cols-2 gap-2">
                                    <label className={fieldLabelClass}>
                                        Mode
                                        <select
                                            value={String(navmeshConfig.mode ?? 'offline')}
                                            onChange={(e) => mutateNavigationConfig((draft) => { draft.navmesh.mode = e.target.value; })}
                                            className={inputClass}
                                        >
                                            <option value="offline">offline</option>
                                            <option value="runtime-incremental">runtime-incremental</option>
                                        </select>
                                    </label>
                                    <label className={fieldLabelClass}>
                                        Algorithm
                                        <select
                                            value={String(navmeshConfig.algorithm ?? 'recast')}
                                            onChange={(e) => mutateNavigationConfig((draft) => { draft.navmesh.algorithm = e.target.value; })}
                                            className={inputClass}
                                        >
                                            <option value="recast">recast</option>
                                            <option value="cdt">cdt</option>
                                        </select>
                                    </label>
                                </div>

                                {navEditorConfig.validated ? (
                                    <div className="grid grid-cols-4 gap-1 text-[10px] text-slate-400">
                                        <span>A {numericValue(navEditorConfig.validated.profileCount, 0)}</span>
                                        <span>P {numericValue(navEditorConfig.validated.bakeProfileCount, 0)}</span>
                                        <span>L {numericValue(navEditorConfig.validated.layerCount, 0)}</span>
                                        <span>R {numericValue(navEditorConfig.validated.areaCount, 0)}</span>
                                    </div>
                                ) : null}

                                <div className="space-y-2">
                                    <div className="flex items-center justify-between">
                                        <div className="text-[10px] uppercase tracking-wide text-slate-500">Bake Profiles</div>
                                        <button onClick={addBakeProfile} className="rounded border border-slate-700 bg-slate-900 px-2 py-0.5 text-slate-300">+</button>
                                    </div>
                                    {bakeProfiles.map((profile: NavBakeProfilePayload, i: number) => (
                                        <div key={`${profile.id ?? i}-profile`} className="grid grid-cols-3 gap-1 rounded border border-slate-800 bg-slate-900/60 p-2">
                                            <input value={textValue(profile.id)} onChange={(e) => updateBakeProfileField(i, 'id', e.target.value, false)} className="col-span-3 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="maxClimbCm" type="number" value={numericValue(profile.maxClimbCm, 0)} onChange={(e) => updateBakeProfileField(i, 'maxClimbCm', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="maxSlopeDeg" type="number" step="0.5" value={numericValue(profile.maxSlopeDeg, 0)} onChange={(e) => updateBakeProfileField(i, 'maxSlopeDeg', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                        </div>
                                    ))}
                                </div>

                                <div className="space-y-2">
                                    <div className="flex items-center justify-between">
                                        <div className="text-[10px] uppercase tracking-wide text-slate-500">Layers</div>
                                        <button onClick={addNavLayer} className="rounded border border-slate-700 bg-slate-900 px-2 py-0.5 text-slate-300">+</button>
                                    </div>
                                    {navLayers.map((layer: NavLayerPayload, i: number) => (
                                        <div key={`${layer.id ?? i}-layer`} className="grid grid-cols-2 gap-1 rounded border border-slate-800 bg-slate-900/60 p-2">
                                            <input value={textValue(layer.id)} onChange={(e) => updateNavLayerField(i, 'id', e.target.value, false)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input type="number" value={numericValue(layer.layer, 0)} onChange={(e) => updateNavLayerField(i, 'layer', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                        </div>
                                    ))}
                                </div>

                                <div className="space-y-2">
                                    <div className="flex items-center justify-between">
                                        <div className="text-[10px] uppercase tracking-wide text-slate-500">Areas</div>
                                        <button onClick={addNavArea} className="rounded border border-slate-700 bg-slate-900 px-2 py-0.5 text-slate-300">+</button>
                                    </div>
                                    {navAreas.map((area: NavAreaPayload, i: number) => (
                                        <div key={`${area.id ?? i}-area`} className="grid grid-cols-3 gap-1 rounded border border-slate-800 bg-slate-900/60 p-2">
                                            <input value={textValue(area.id)} onChange={(e) => updateNavAreaField(i, 'id', e.target.value, false)} className="col-span-3 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="areaId" type="number" value={numericValue(area.areaId, 0)} onChange={(e) => updateNavAreaField(i, 'areaId', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                            <input title="cost" type="number" step="0.05" value={numericValue(area.cost, 1)} onChange={(e) => updateNavAreaField(i, 'cost', e.target.value)} className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-[11px]" />
                                        </div>
                                    ))}
                                </div>

                                <div className="grid grid-cols-2 gap-2 rounded border border-slate-800 bg-slate-900/60 p-2">
                                    <label className={fieldLabelClass}>
                                        Tick Tiles
                                        <input type="number" value={numericValue(runtimeIncremental.tileBudgetPerFixedTick, 1)} onChange={(e) => updateRuntimeIncrementalField('tileBudgetPerFixedTick', e.target.value)} className={inputClass} />
                                    </label>
                                    <label className={fieldLabelClass}>
                                        Height
                                        <input type="number" step="0.1" value={numericValue(runtimeIncremental.heightScaleMeters, 1)} onChange={(e) => updateRuntimeIncrementalField('heightScaleMeters', e.target.value)} className={inputClass} />
                                    </label>
                                    <label className={fieldLabelClass}>
                                        Up Dot
                                        <input type="number" step="0.05" value={numericValue(runtimeIncremental.minWalkableUpDot, 0.6)} onChange={(e) => updateRuntimeIncrementalField('minWalkableUpDot', e.target.value)} className={inputClass} />
                                    </label>
                                    <label className={fieldLabelClass}>
                                        Cliff
                                        <input type="number" value={numericValue(runtimeIncremental.cliffHeightThreshold, 1)} onChange={(e) => updateRuntimeIncrementalField('cliffHeightThreshold', e.target.value)} className={inputClass} />
                                    </label>
                                    <label className="col-span-2 flex items-center gap-2 text-[10px] text-slate-300">
                                        <input type="checkbox" checked={!!runtimeIncremental.includeNeighborTiles} onChange={(e) => updateRuntimeIncrementalField('includeNeighborTiles', e.target.checked, false)} />
                                        <span>Neighbor tiles</span>
                                    </label>
                                </div>
                            </div>
                        ) : (
                            <div className="rounded border border-slate-800 bg-slate-900/60 p-3 text-xs text-slate-500">
                                No config loaded.
                            </div>
                        )}
                    </section>
                </div>
            </aside>

            <section className={`${panelClass} absolute bottom-4 left-[408px] right-[392px] flex min-h-[136px] flex-col overflow-hidden px-3 py-3`}>
                <div className="mb-2 flex items-center justify-between gap-3">
                    <div>
                        <div className={sectionTitleClass}>Brush Inspector</div>
                        <div className="mt-1 text-xs text-slate-300">
                            {navPanelTab === 'simulation' ? 'Simulation pick mode' : `${activeCategory} / ${activeMode}`}
                        </div>
                    </div>
                    <div className={`rounded border px-2 py-1 text-[10px] ${
                        navPanelTab === 'simulation'
                            ? 'border-sky-700/60 bg-sky-950/30 text-sky-100'
                            : canvasCanEdit
                            ? 'border-emerald-700/60 bg-emerald-950/30 text-emerald-100'
                            : 'border-amber-800/70 bg-amber-950/25 text-amber-100'
                    }`}>
                        {navPanelTab === 'simulation'
                            ? 'Left start / right goal'
                            : canvasCanEdit ? 'Canvas editable' : 'Open selected board before editing'}
                    </div>
                </div>
                <div className="grid grid-cols-[minmax(260px,1.1fr)_minmax(260px,1fr)_auto] gap-3">
                    <div className="space-y-2">
                        <div className="grid grid-cols-5 gap-1">
                            {categories.map((category) => (
                                <button
                                    key={category.id}
                                    onClick={() => !brushInspectorLocked && setCategory(category.id)}
                                    disabled={brushInspectorLocked}
                                    className={`flex h-11 flex-col items-center justify-center gap-0.5 rounded border px-1 transition ${
                                        activeCategory === category.id
                                            ? 'border-sky-500/70 bg-sky-600/25 text-sky-200'
                                            : 'border-slate-800 bg-slate-900 text-slate-400 hover:border-slate-600 hover:bg-slate-800'
                                    }`}
                                    title={category.id}
                                >
                                    {category.icon}
                                    <span className="text-[9px] font-medium">{category.label}</span>
                                </button>
                            ))}
                        </div>
                        <div className="grid grid-cols-4 gap-1">
                            {modes.map((mode) => (
                                <button
                                    key={mode.id}
                                    onClick={() => !brushInspectorLocked && setMode(mode.id)}
                                    disabled={brushInspectorLocked}
                                    className={`flex h-10 flex-col items-center justify-center gap-0.5 rounded border px-1 transition ${
                                        activeMode === mode.id
                                            ? 'border-violet-500/70 bg-violet-600/25 text-violet-200'
                                            : 'border-slate-800 bg-slate-900 text-slate-400 hover:border-slate-600 hover:bg-slate-800'
                                    }`}
                                    title={mode.id}
                                >
                                    {mode.icon}
                                    <span className="text-[9px] font-medium">{mode.label}</span>
                                </button>
                            ))}
                        </div>
                    </div>

                    <div className="min-w-0 space-y-2">
                        <div className="rounded border border-slate-800 bg-slate-900/60 p-2">
                            <div className="mb-2 flex items-center justify-between text-xs text-slate-300">
                                <span>Size {brushSize}</span>
                                <span>Value {brushValue}</span>
                            </div>
                            <label className={fieldLabelClass}>
                                Brush Size
                                <input
                                    type="range"
                                    min="1"
                                    max="10"
                                    value={brushSize}
                                    onChange={(e) => setBrushSize(parseInt(e.target.value))}
                                    disabled={brushInspectorLocked}
                                    className="mt-2 w-full accent-sky-500"
                                />
                            </label>
                        </div>
                        <fieldset disabled={brushInspectorLocked} className={brushInspectorLocked ? 'pointer-events-none opacity-60' : ''}>
                            {renderBrushValueControls()}
                        </fieldset>
                    </div>

                    <div className="flex w-36 flex-col justify-between gap-2">
                        <div>
                            <div className={sectionTitleClass}>View</div>
                            <div className="mt-2 flex gap-2">
                                <button
                                    onClick={toggleGrid}
                                    className={`${iconToggleClass} ${showGrid ? 'border-violet-500/70 bg-violet-600/25 text-violet-100' : ''}`}
                                    title="Toggle Grid"
                                >
                                    <Grid size={16} />
                                </button>
                                <button
                                    onClick={toggleChunkBorders}
                                    className={`${iconToggleClass} ${showChunkBorders ? 'border-violet-500/70 bg-violet-600/25 text-violet-100' : ''}`}
                                    title="Toggle Chunk Borders"
                                >
                                    <BoxSelect size={16} />
                                </button>
                                <button
                                    onClick={toggleNavMesh}
                                    className={`${iconToggleClass} ${showNavMesh ? 'border-emerald-500/70 bg-emerald-600/25 text-emerald-100' : ''}`}
                                    title="Toggle NavMesh Visualization"
                                >
                                    <Eye size={16} />
                                </button>
                            </div>
                        </div>
                        <div className="rounded border border-slate-800 bg-slate-900/60 p-2 text-[10px] leading-snug text-slate-500">
                            {navPanelTab === 'simulation'
                                ? 'Simulation mode: left-click picks start, right-click picks goal. Brush input is suspended.'
                                : canvasCanEdit
                                ? `Middle pans. Right rotates. Left ${activeCategory === 'Entities' ? 'places/selects' : activeCategory === 'Obstacle' ? 'places obstacles' : 'paints'}.`
                                : 'Editing is locked until the selected board is opened.'}
                        </div>
                    </div>
                </div>
            </section>

            {showNewMap ? (
                <div className="pointer-events-auto fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm">
                    <div className="max-h-[calc(100vh-48px)] w-[560px] max-w-[calc(100vw-32px)] overflow-auto rounded-lg border border-slate-700 bg-slate-950 p-5 shadow-2xl">
                        <h3 className="mb-4 text-lg font-semibold text-white">Create New Map</h3>
                        <div className="space-y-4">
                            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                                <label className="block text-sm text-slate-400">
                                    Desired width m
                                    <input
                                        type="text"
                                        inputMode="decimal"
                                        value={newMapWidthMeters}
                                        onChange={(e) => setNewMapWidthMeters(e.target.value)}
                                        className={inputClass}
                                        aria-invalid={!isPositiveFinite(newMapWidthMetersValue)}
                                    />
                                </label>
                                <label className="block text-sm text-slate-400">
                                    Desired height m
                                    <input
                                        type="text"
                                        inputMode="decimal"
                                        value={newMapHeightMeters}
                                        onChange={(e) => setNewMapHeightMeters(e.target.value)}
                                        className={inputClass}
                                        aria-invalid={!isPositiveFinite(newMapHeightMetersValue)}
                                    />
                                </label>
                            </div>
                            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                                <label className="block text-sm text-slate-400">
                                    Grid cell cm
                                    <input
                                        type="text"
                                        inputMode="numeric"
                                        value={newMapCellSizeCm}
                                        onChange={(e) => setNewMapCellSizeCm(e.target.value)}
                                        className={inputClass}
                                        aria-invalid={!isPositiveFinite(newMapCellSizeCmValue)}
                                    />
                                </label>
                                <label className="block text-sm text-slate-400">
                                    Hex edge cm
                                    <input
                                        type="text"
                                        inputMode="numeric"
                                        value={newMapHexEdgeLengthCm}
                                        onChange={(e) => setNewMapHexEdgeLengthCm(e.target.value)}
                                        className={inputClass}
                                        disabled={newTopology !== 'HexGrid'}
                                        aria-invalid={newTopology === 'HexGrid' && !isPositiveFinite(newMapHexEdgeLengthCmValue)}
                                        title={newTopology === 'HexGrid' ? 'Hex edge length for the local HexGrid draft.' : 'Hex edge length only applies to HexGrid.'}
                                    />
                                </label>
                            </div>
                            <label className="block text-sm text-slate-400">
                                Topology
                                <select
                                    value={newTopology}
                                    onChange={(e) => setNewTopology(e.target.value as BoardTopology)}
                                    className={inputClass}
                                >
                                    <option value="Grid">Grid</option>
                                    <option value="HexGrid">HexGrid</option>
                                </select>
                            </label>
                            <div className={`rounded border p-2 text-[10px] leading-snug ${newMapAllocation.exceedsDefaultWorldFootprint ? 'border-sky-800/80 bg-sky-950/30 text-sky-100' : 'border-slate-800 bg-slate-900/60 text-slate-500'}`}>
                                <div className="grid grid-cols-2 gap-2">
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Allocated extent</div>
                                        <div className="font-mono text-slate-200">{formatMeters(newMapAllocation.allocatedWidthMeters)}m x {formatMeters(newMapAllocation.allocatedHeightMeters)}m</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Terrain/NavTiles</div>
                                        <div className="font-mono text-slate-200">{newMapAllocation.widthTerrainChunks} x {newMapAllocation.heightTerrainChunks}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Grid cells</div>
                                        <div className="font-mono text-slate-200">{newMapAllocation.allocatedWidthCells.toLocaleString()} x {newMapAllocation.allocatedHeightCells.toLocaleString()}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">MacroTiles</div>
                                        <div className="font-mono text-slate-200">{newMapAllocation.widthMacroTiles} x {newMapAllocation.heightMacroTiles}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Sparse resident</div>
                                        <div className="font-mono text-slate-200">0 / {newMapAllocation.totalTerrainChunks.toLocaleString()}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Full file equivalent</div>
                                        <div className="font-mono text-slate-200">{formatBytes(newMapAllocation.fullTerrainBytes)}</div>
                                    </div>
                                </div>
                                <div className="mt-2 rounded border border-slate-800 bg-slate-950/70 px-2 py-1 text-slate-400">
                                    {newMapAllocation.snappedToMacroTile
                                        ? 'Local draft allocation snaps upward to whole MacroTiles. Empty terrain chunks are sparse and created only when painted.'
                                        : 'Meters align exactly with MacroTile allocation. Empty terrain chunks are sparse and created only when painted.'}
                                </div>
                                {newMapCreateWarning ? (
                                    <div className="mt-2 rounded border border-sky-700/70 bg-sky-950/50 px-2 py-1 text-sky-100">
                                        {newMapCreateWarning}
                                    </div>
                                ) : null}
                            </div>
                            <div className="flex gap-2 pt-2">
                                <button
                                    onClick={() => setShowNewMap(false)}
                                    className="flex-1 rounded bg-slate-800 py-2 font-medium text-slate-300 hover:bg-slate-700"
                                >
                                    Cancel
                                </button>
                                <button
                                    onClick={handleNewMap}
                                    className="flex-1 rounded bg-sky-700 py-2 font-medium text-white hover:bg-sky-600 disabled:cursor-not-allowed disabled:opacity-40"
                                    disabled={!newMapCanCreate}
                                    title={newMapCanCreate ? (newMapCreateWarning || 'Create local terrain draft from desired meters and SSOT constants') : newMapCreateDisabledReason}
                                >
                                    Create
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            ) : null}

            {showAddBoard ? (
                <div className="pointer-events-auto fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm">
                    <div className="max-h-[calc(100vh-48px)] w-[560px] max-w-[calc(100vw-32px)] overflow-auto rounded-lg border border-slate-700 bg-slate-950 p-5 shadow-2xl">
                        <h3 className="mb-4 text-lg font-semibold text-white">Add Board</h3>
                        <div className="space-y-4">
                            <label className="block text-sm text-slate-400">
                                Name
                                <input
                                    value={newBoardName}
                                    onChange={(e) => setNewBoardName(e.target.value)}
                                    className={inputClass}
                                />
                            </label>
                            <label className="block text-sm text-slate-400">
                                Topology
                                <select
                                    value={newBoardTopology}
                                    onChange={(e) => setNewBoardTopology(e.target.value as BoardTopology)}
                                    className={inputClass}
                                >
                                    <option value="Grid">Grid</option>
                                    <option value="HexGrid">HexGrid</option>
                                </select>
                            </label>
                            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                                <label className="block text-sm text-slate-400">
                                    Desired width m
                                    <input
                                        type="text"
                                        inputMode="decimal"
                                        value={newBoardWidthMeters}
                                        onChange={(e) => setNewBoardWidthMeters(e.target.value)}
                                        className={inputClass}
                                        aria-invalid={!isPositiveFinite(newBoardWidthMetersValue)}
                                    />
                                </label>
                                <label className="block text-sm text-slate-400">
                                    Desired height m
                                    <input
                                        type="text"
                                        inputMode="decimal"
                                        value={newBoardHeightMeters}
                                        onChange={(e) => setNewBoardHeightMeters(e.target.value)}
                                        className={inputClass}
                                        aria-invalid={!isPositiveFinite(newBoardHeightMetersValue)}
                                    />
                                </label>
                            </div>
                            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                                <label className="block text-sm text-slate-400">
                                    Grid cell cm
                                    <input
                                        type="text"
                                        inputMode="numeric"
                                        value={newBoardCellSizeCm}
                                        onChange={(e) => setNewBoardCellSizeCm(e.target.value)}
                                        className={inputClass}
                                        aria-invalid={!isPositiveFinite(newBoardCellSizeCmValue)}
                                    />
                                </label>
                                <label className="block text-sm text-slate-400">
                                    Hex edge cm
                                    <input
                                        type="text"
                                        inputMode="numeric"
                                        value={newBoardHexEdgeLengthCm}
                                        onChange={(e) => setNewBoardHexEdgeLengthCm(e.target.value)}
                                        className={inputClass}
                                        disabled={newBoardTopology !== 'HexGrid'}
                                        aria-invalid={newBoardTopology === 'HexGrid' && !isPositiveFinite(newBoardHexEdgeLengthCmValue)}
                                        title={newBoardTopology === 'HexGrid' ? 'Hex edge length for this HexGrid board.' : 'Hex edge length only applies to HexGrid boards.'}
                                    />
                                </label>
                            </div>
                            <label className="flex items-center gap-2 text-sm text-slate-300">
                                <input
                                    type="checkbox"
                                    checked={newBoardNavigationEnabled}
                                    onChange={(e) => setNewBoardNavigationEnabled(e.target.checked)}
                                />
                                <span>Navigation enabled</span>
                            </label>
                            <div className={`rounded border p-2 text-[10px] leading-snug ${newBoardWithinFullFileBudget ? 'border-slate-800 bg-slate-900/60 text-slate-500' : 'border-sky-800/80 bg-sky-950/30 text-sky-100'}`}>
                                <div className="grid grid-cols-2 gap-2">
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Requested</div>
                                        <div className="font-mono text-slate-200">{formatMeters(newBoardAllocation.requestedWidthMeters)}m x {formatMeters(newBoardAllocation.requestedHeightMeters)}m</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Allocated extent</div>
                                        <div className="font-mono text-slate-200">{formatMeters(newBoardAllocation.allocatedWidthMeters)}m x {formatMeters(newBoardAllocation.allocatedHeightMeters)}m</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Grid cells</div>
                                        <div className="font-mono text-slate-200">{newBoardAllocation.allocatedWidthCells.toLocaleString()} x {newBoardAllocation.allocatedHeightCells.toLocaleString()}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">MacroTiles</div>
                                        <div className="font-mono text-slate-200">{newBoardAllocation.widthMacroTiles} x {newBoardAllocation.heightMacroTiles}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Terrain/NavTiles</div>
                                        <div className="font-mono text-slate-200">{newBoardAllocation.widthTerrainChunks} x {newBoardAllocation.heightTerrainChunks}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Unit constants</div>
                                        <div className="font-mono text-slate-200">{formatMeters(newBoardAllocation.macroTileMeters)}m / {formatMeters(newBoardAllocation.terrainChunkMeters)}m</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Full terrain file</div>
                                        <div className="font-mono text-slate-200">{formatBytes(newBoardAllocation.fullTerrainBytes)}</div>
                                    </div>
                                    <div>
                                        <div className="uppercase tracking-wide text-slate-600">Eager file threshold</div>
                                        <div className="font-mono text-slate-200">{DefaultEditorEagerFullTerrainFileMacroTilesPerAxis} x {DefaultEditorEagerFullTerrainFileMacroTilesPerAxis} MacroTiles</div>
                                    </div>
                                </div>
                                <div className="mt-2 rounded border border-slate-800 bg-slate-950/70 px-2 py-1 text-slate-400">
                                    {newBoardAllocation.snappedToMacroTile
                                        ? 'The editor snaps allocation upward to whole MacroTiles. Large boards are created sparse; first save writes only resident terrain chunks.'
                                        : 'Meters align exactly with MacroTile allocation. Large boards are created sparse; first save writes only resident terrain chunks.'}
                                </div>
                                {!newBoardWithinFullFileBudget ? (
                                    <div className="mt-2 rounded border border-sky-700/70 bg-sky-950/50 px-2 py-1 text-sky-100">
                                        {newBoardCreateWarning}
                                    </div>
                                ) : null}
                            </div>
                            <div className="flex gap-2 pt-2">
                                <button
                                    onClick={() => setShowAddBoard(false)}
                                    className="flex-1 rounded bg-slate-800 py-2 font-medium text-slate-300 hover:bg-slate-700"
                                >
                                    Cancel
                                </button>
                                <button
                                    onClick={handleCreateBoard}
                                    className="flex-1 rounded bg-sky-700 py-2 font-medium text-white hover:bg-sky-600 disabled:cursor-not-allowed disabled:opacity-40"
                                    disabled={!newBoardCanCreate}
                                    title={newBoardCanCreate ? (newBoardCreateWarning || 'Create board from desired meters and SSOT constants') : newBoardCreateDisabledReason}
                                >
                                    Create Board
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            ) : null}
        </div>
    );
};
