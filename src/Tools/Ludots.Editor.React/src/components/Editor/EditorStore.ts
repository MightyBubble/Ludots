import { create } from 'zustand';
import { TerrainStore } from '../../Core/Map/TerrainStore';
import {
    DEFAULT_BOARD_METRICS,
    type BoardMetrics,
    type BoardTopology,
    type SpatialTopology,
    cellToWorldCm,
    normalizeBoardMetrics,
    normalizeSpatialTopology,
    normalizeTopology,
    worldCmToCell,
} from '../../Core/Map/TopologyMetrics';
import type { Camera, PerspectiveCamera } from 'three';
import type { OrbitControls } from 'three-stdlib';
import type { NavTile } from '../../Core/NavMesh/NavTileBinary';

export type JsonRecord = Record<string, unknown>;
export type EntityTemplatePayload = JsonRecord;
export type PerformerPayload = JsonRecord;

export type ToolCategory = 'Height' | 'Water' | 'Area' | 'Blocked' | 'Biome' | 'Vegetation' | 'Ramp' | 'Layers' | 'Territory' | 'Entities' | 'Obstacle';
export type ToolMode = 'Set' | 'Raise' | 'Lower' | 'Smooth' | 'Bucket'; // Added Bucket
export type NavPanelTab = 'bake' | 'simulation' | 'config';
export type CanvasSessionKind = 'empty' | 'local' | 'repo';

export interface NavQueryCell {
    col: number;
    row: number;
}

export interface BoardInfo {
    name: string;
    spatialType: SpatialTopology | null;
    widthChunks: number;
    heightChunks: number;
    cellSizeCm: number;
    hexEdgeLengthCm: number;
    chunkSizeCells: number;
    navigationEnabled: boolean;
    hasDataFile: boolean;
    dataFileExists: boolean;
    dataFile: string | null;
    canEditTerrain: boolean;
    canBake: boolean;
    reason: string;
}

export interface BoardCreateRequest {
    name: string;
    spatialType: BoardTopology;
    widthInMacroTiles: number;
    heightInMacroTiles: number;
    cellSizeCm: number;
    hexEdgeLengthCm?: number;
    navigationEnabled: boolean;
}

export interface BoardUpdateRequest {
    cellSizeCm?: number;
    hexEdgeLengthCm?: number;
    navigationEnabled?: boolean;
}

export interface MapInfo extends BoardInfo {
    id: string;
    found: boolean;
    hasBoards: boolean;
    boardName: string | null;
    boards: BoardInfo[];
}

export interface BakedNavTilePayload {
    key: string;
    layer: number;
    profileId: string | null;
    base64: string;
    detourBase64: string | null;
    source?: string | null;
}

export interface BakedNavTileVisual {
    key: string;
    layer: number;
    profileId: string | null;
    tile: NavTile;
}

export interface NavSimulationState {
    status: string;
    points: Array<{ xCm: number; zCm: number }>;
    travelCost: number;
    elapsedMs: number;
    engine: string;
    algorithmSource: string;
    profileId: string;
    layer: number;
    tileSource: string;
    warning?: string | null;
}

export interface EditorState {
    terrain: TerrainStore;
    boardMetrics: BoardMetrics;

    bridgeBaseUrl: string;
    mods: Array<{ id: string; name: string; version: string; priority: number }>;
    selectedModId: string | null;
    maps: string[];
    mapInfos: MapInfo[];
    selectedMapId: string | null;
    selectedMapInfo: MapInfo | null;
    selectedBoardName: string | null;
    selectedBoardInfo: BoardInfo | null;
    loadedModId: string | null;
    loadedMapId: string | null;
    loadedMapInfo: MapInfo | null;
    loadedBoardName: string | null;
    loadedBoardInfo: BoardInfo | null;
    canvasSessionKind: CanvasSessionKind;
    canvasSessionLabel: string | null;
    mapConfig: JsonRecord | null;
    templates: EntityTemplatePayload[];
    performers: PerformerPayload[];
    navigationConfig: JsonRecord | null;
    navigationConfigVersion: number;
    selectedTemplateId: string | null;
    obstacleTemplateId: string | null;
    obstacleShape: 'Circle' | 'Box';
    obstacleRadiusCm: number;
    obstacleHalfWidthCm: number;
    obstacleHalfHeightCm: number;
    spawnEntities: Array<{ template: string; position: { x: number; y: number }; overrides: Record<string, unknown> }>;
    selectedEntityIndex: number | null;
    entitiesVersion: number;
    
    // Tool State
    activeCategory: ToolCategory;
    activeMode: ToolMode;
    brushSize: number;
    brushValue: number; // For Set mode or ID for Biome/Veg
    
    // Dynamic Layers State
    activeLayer: 'Snow' | 'Mud' | 'Ice' | null;
    
    // UI State
    showGrid: boolean;
    showChunkBorders: boolean;
    showNavMesh: boolean; // Added NavMesh Toggle
    navMeshBakeVersion: number;
    bakedNavTiles: Map<string, BakedNavTileVisual>;
    bakedNavTilePayloads: BakedNavTilePayload[];
    bakedNavTilesVersion: number;
    navSimulation: NavSimulationState | null;
    navSimulationVersion: number;
    navPanelTab: NavPanelTab;
    navQueryProfileId: string;
    navQueryLayer: number;
    navQueryStartCell: NavQueryCell;
    navQueryGoalCell: NavQueryCell;
    
    // Actions
    setCategory: (c: ToolCategory) => void;
    setMode: (m: ToolMode) => void;
    setBrushSize: (s: number) => void;
    setBrushValue: (v: number) => void;
    setActiveLayer: (l: 'Snow' | 'Mud' | 'Ice' | null) => void;
    toggleGrid: () => void;
    toggleChunkBorders: () => void;
    toggleNavMesh: () => void; // Added Action
    
    // Map Actions
    initMap: (w: number, h: number, metrics?: Partial<BoardMetrics>) => void;
    loadMap: (data: Uint8Array, w: number, h: number, metrics?: Partial<BoardMetrics>) => void;
    refreshMods: () => Promise<void>;
    selectMod: (modId: string) => Promise<void>;
    selectMap: (mapId: string) => void;
    selectBoard: (boardName: string) => void;
    createBoard: (request: BoardCreateRequest) => Promise<void>;
    updateSelectedBoard: (request: BoardUpdateRequest) => Promise<void>;
    deleteSelectedBoard: () => Promise<void>;
    loadSelectedMap: () => Promise<void>;
    saveSelectedMap: () => Promise<void>;
    loadNavigationConfig: () => Promise<void>;
    saveNavigationConfig: () => Promise<void>;
    setNavigationConfig: (config: JsonRecord | null) => void;
    selectTemplate: (templateId: string | null) => void;
    setObstacleTemplate: (templateId: string | null) => void;
    setObstacleShape: (shape: 'Circle' | 'Box') => void;
    setObstacleRadiusCm: (radiusCm: number) => void;
    setObstacleHalfSizeCm: (halfWidthCm: number, halfHeightCm: number) => void;
    placeEntityAt: (c: number, r: number) => void;
    placeObstacleAt: (c: number, r: number) => void;
    removeEntityAt: (c: number, r: number) => void;
    selectEntityAt: (c: number, r: number) => void;
    updateSelectedEntityOverridesJson: (componentName: string, jsonText: string) => void;
    deleteSelectedEntityOverride: (componentName: string) => void;

    // Minimap State
    minimapDirtyChunks: Set<string>;
    navDirtyChunks: Set<string>;
    reportDirtyChunks: (keys: Iterable<string>) => void;
    reportMinimapDirtyChunks: (keys: Iterable<string>) => void;
    clearMinimapDirty: () => void;
    clearNavDirty: () => void;

    // Loading State
    loadingState: { isLoading: boolean, message: string, progress: number };
    setLoading: (isLoading: boolean, message?: string, progress?: number) => void;

    // Camera Bridge (Non-reactive refs for performance)
    cameraRef: { current: Camera | null };
    controlsRef: { current: OrbitControls | null };
    registerCamera: (camera: Camera, controls: OrbitControls) => void;

    // NavMesh Actions
    bakeNavMesh: () => void;
    setBakedNavTiles: (tiles: NavTile[], payloads?: BakedNavTilePayload[]) => void;
    mergeBakedNavTiles: (tiles: NavTile[], payloads?: BakedNavTilePayload[]) => void;
    clearBakedNavTiles: () => void;
    setNavSimulation: (simulation: NavSimulationState | null) => void;
    clearNavSimulation: () => void;
    setNavPanelTab: (tab: NavPanelTab) => void;
    setNavQueryProfileId: (profileId: string) => void;
    setNavQueryLayer: (layer: number) => void;
    setNavQueryStartCell: (cell: NavQueryCell) => void;
    setNavQueryGoalCell: (cell: NavQueryCell) => void;
}

export const useEditorStore = create<EditorState>((set, get) => ({
    terrain: new TerrainStore(8, 8), // Default 8x8 chunks
    boardMetrics: DEFAULT_BOARD_METRICS,

    bridgeBaseUrl: 'http://localhost:5299',
    mods: [],
    selectedModId: null,
    maps: [],
    mapInfos: [],
    selectedMapId: null,
    selectedMapInfo: null,
    selectedBoardName: null,
    selectedBoardInfo: null,
    loadedModId: null,
    loadedMapId: null,
    loadedMapInfo: null,
    loadedBoardName: null,
    loadedBoardInfo: null,
    canvasSessionKind: 'empty',
    canvasSessionLabel: null,
    mapConfig: null,
    templates: [],
    performers: [],
    navigationConfig: null,
    navigationConfigVersion: 0,
    selectedTemplateId: null,
    obstacleTemplateId: null,
    obstacleShape: 'Circle',
    obstacleRadiusCm: 300,
    obstacleHalfWidthCm: 300,
    obstacleHalfHeightCm: 300,
    spawnEntities: [],
    selectedEntityIndex: null,
    entitiesVersion: 0,
    
    activeCategory: 'Height',
    activeMode: 'Raise',
    brushSize: 1,
    brushValue: 1,
    activeLayer: 'Snow', // Default layer

    showGrid: true,
    showChunkBorders: true,
    showNavMesh: false, // Default Off
    navMeshBakeVersion: 0,
    bakedNavTiles: new Map(),
    bakedNavTilePayloads: [],
    bakedNavTilesVersion: 0,
    navSimulation: null,
    navSimulationVersion: 0,
    navPanelTab: 'bake',
    navQueryProfileId: '',
    navQueryLayer: 0,
    navQueryStartCell: { col: 4, row: 4 },
    navQueryGoalCell: { col: 28, row: 28 },
    
    minimapDirtyChunks: new Set(),
    navDirtyChunks: new Set(),
    
    loadingState: { isLoading: false, message: '', progress: 0 },

    cameraRef: { current: null },
    controlsRef: { current: null },

    setCategory: (c) => set({ activeCategory: c }),
    setMode: (m) => set({ activeMode: m }),
    setBrushSize: (s) => set({ brushSize: Math.max(1, s) }),
    setBrushValue: (v) => set({ brushValue: v }),
    setActiveLayer: (l) => set({ activeLayer: l }),
    toggleGrid: () => set((state) => ({ showGrid: !state.showGrid })),
    toggleChunkBorders: () => set((state) => ({ showChunkBorders: !state.showChunkBorders })),
    toggleNavMesh: () => set((state) => ({ showNavMesh: !state.showNavMesh })),
    
    bakeNavMesh: () => {
        set((state) => ({ navMeshBakeVersion: state.navMeshBakeVersion + 1 }));
    },

    setBakedNavTiles: (tiles, payloads = []) => set(() => {
        const map = new Map<string, BakedNavTileVisual>();
        for (let i = 0; i < tiles.length; i++) {
            const t = tiles[i];
            const payload = payloads[i];
            const profileId = payload?.profileId ?? null;
            const layer = Number(payload?.layer ?? t.tileId.layer);
            const key = payload?.key ?? `${t.tileId.chunkX},${t.tileId.chunkY},${layer},${profileId ?? ''}`;
            map.set(key, { key, layer, profileId, tile: t });
        }
        return {
            bakedNavTiles: map,
            bakedNavTilePayloads: payloads.slice(),
            bakedNavTilesVersion: Date.now(),
            navSimulation: null,
            navSimulationVersion: Date.now(),
        };
    }),

    mergeBakedNavTiles: (tiles, payloads = []) => set((state) => {
        const map = new Map(state.bakedNavTiles);
        const payloadMap = new Map(state.bakedNavTilePayloads.map((payload) => [payload.key, payload]));
        for (let i = 0; i < tiles.length; i++) {
            const t = tiles[i];
            const payload = payloads[i];
            const profileId = payload?.profileId ?? null;
            const layer = Number(payload?.layer ?? t.tileId.layer);
            const key = payload?.key ?? `${t.tileId.chunkX},${t.tileId.chunkY},${layer},${profileId ?? ''}`;
            map.set(key, { key, layer, profileId, tile: t });
            if (payload) {
                payloadMap.set(key, { ...payload, key, layer, profileId });
            }
        }
        return {
            bakedNavTiles: map,
            bakedNavTilePayloads: Array.from(payloadMap.values()),
            bakedNavTilesVersion: Date.now(),
            navSimulation: null,
            navSimulationVersion: Date.now(),
        };
    }),

    clearBakedNavTiles: () => set(() => ({
        bakedNavTiles: new Map(),
        bakedNavTilePayloads: [],
        bakedNavTilesVersion: Date.now(),
        navSimulation: null,
        navSimulationVersion: Date.now(),
    })),

    setNavSimulation: (simulation) => set({ navSimulation: simulation, navSimulationVersion: Date.now() }),

    clearNavSimulation: () => set({ navSimulation: null, navSimulationVersion: Date.now() }),

    setNavPanelTab: (tab) => set({
        navPanelTab: tab,
    }),

    setNavQueryProfileId: (profileId) => set({
        navQueryProfileId: profileId,
        navSimulation: null,
        navSimulationVersion: Date.now(),
    }),

    setNavQueryLayer: (layer) => set({
        navQueryLayer: Math.floor(Number(layer) || 0),
        navSimulation: null,
        navSimulationVersion: Date.now(),
    }),

    setNavQueryStartCell: (cell) => set((state) => ({
        navQueryStartCell: clampNavQueryCell(cell, state),
        navSimulation: null,
        navSimulationVersion: Date.now(),
    })),

    setNavQueryGoalCell: (cell) => set((state) => ({
        navQueryGoalCell: clampNavQueryCell(cell, state),
        navSimulation: null,
        navSimulationVersion: Date.now(),
    })),

    initMap: (w, h, metrics) => set({
        terrain: new TerrainStore(w, h), 
        boardMetrics: normalizeBoardMetrics(metrics ?? get().boardMetrics),
        canvasSessionKind: 'local',
        canvasSessionLabel: `New ${w}x${h} terrain`,
        loadedModId: null,
        loadedMapId: null,
        loadedMapInfo: null,
        loadedBoardName: null,
        loadedBoardInfo: null,
        minimapDirtyChunks: new Set(),
        navDirtyChunks: new Set(),
        bakedNavTiles: new Map(),
        bakedNavTilePayloads: [],
        bakedNavTilesVersion: Date.now(),
        navSimulation: null,
        navSimulationVersion: Date.now(),
        loadingState: { isLoading: true, message: 'Initializing Map...', progress: 0 }
    }),
    loadMap: (data, w, h, metrics) => {
        const newTerrain = new TerrainStore(w, h);
        newTerrain.loadFromBytes(w, h, data);
        const boardMetrics = normalizeBoardMetrics(metrics ?? get().boardMetrics);
        // Mark all as dirty for minimap
        const allChunks = new Set<string>();
        for(let y=0; y<h; y++) for(let x=0; x<w; x++) allChunks.add(`${x},${y}`);
        
        set({ 
            terrain: newTerrain, 
            boardMetrics,
            canvasSessionKind: 'local',
            canvasSessionLabel: `Local ${w}x${h} terrain`,
            loadedModId: null,
            loadedMapId: null,
            loadedMapInfo: null,
            loadedBoardName: null,
            loadedBoardInfo: null,
            minimapDirtyChunks: allChunks,
            navDirtyChunks: new Set(),
            bakedNavTiles: new Map(),
            bakedNavTilePayloads: [],
            bakedNavTilesVersion: Date.now(),
            navSimulation: null,
            navSimulationVersion: Date.now(),
            loadingState: { isLoading: true, message: 'Loading Map...', progress: 0 }
        });
    },

    refreshMods: async () => {
        const { bridgeBaseUrl } = get();
        const res = await fetch(`${bridgeBaseUrl}/api/mods`);
        if (!res.ok) throw new Error(`Bridge error ${res.status}`);
        const json = await res.json() as JsonRecord;
        const mods = arrayOfRecords(json.mods).map((m) => ({
            id: String(m.id ?? m.Id ?? ''),
            name: String(m.name ?? m.Name ?? ''),
            version: String(m.version ?? m.Version ?? ''),
            priority: Number(m.priority ?? m.Priority ?? 0),
        }));
        set({ mods });
    },

    selectMod: async (modId: string) => {
        const { bridgeBaseUrl } = get();
        set({
            selectedModId: modId,
            maps: [],
            mapInfos: [],
            selectedMapId: null,
            selectedMapInfo: null,
            selectedBoardName: null,
            selectedBoardInfo: null,
            loadedModId: null,
            loadedMapId: null,
            loadedMapInfo: null,
            loadedBoardName: null,
            loadedBoardInfo: null,
            canvasSessionKind: 'empty',
            canvasSessionLabel: null,
            mapConfig: null,
            templates: [],
            performers: [],
            navigationConfig: null,
            navigationConfigVersion: Date.now(),
            selectedTemplateId: null,
            obstacleTemplateId: null,
            spawnEntities: [],
            selectedEntityIndex: null,
            entitiesVersion: Date.now(),
            navDirtyChunks: new Set(),
            bakedNavTiles: new Map(),
            bakedNavTilePayloads: [],
            bakedNavTilesVersion: Date.now(),
            navSimulation: null,
            navSimulationVersion: Date.now(),
        });
        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/maps`);
        if (!res.ok) throw new Error(`Bridge error ${res.status}`);
        const json = await res.json() as JsonRecord;
        const maps = Array.isArray(json.maps) ? json.maps.map((x) => String(x)) : [];
        const mapInfos = arrayOfRecords(json.mapInfos).map(normalizeMapInfo);
        const tRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/entity-templates`);
        if (!tRes.ok) throw new Error(`Bridge error ${tRes.status}`);
        const tJson = await tRes.json() as JsonRecord;
        const templates = arrayOfRecords(tJson.templates);

        const pRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/performers`);
        if (!pRes.ok) throw new Error(`Bridge error ${pRes.status}`);
        const pJson = await pRes.json() as JsonRecord;
        const performers = arrayOfRecords(pJson.performers);

        const defaultTemplateId = templates.length > 0 ? String(templates[0]?.Id ?? templates[0]?.id ?? '') : null;
        const obstacleTemplate = templates.find((t) => {
            const components = asRecord(t.Components) ?? asRecord(t.components) ?? {};
            return Boolean(components.ManifestationObstacleIntent2D || components.manifestationObstacleIntent2D);
        });
        const fallbackBlocker = templates.find((t) => {
            const id = String(t?.Id ?? t?.id ?? '');
            return id.toLowerCase().includes('blocker') || id.toLowerCase().includes('obstacle');
        });
        const obstacleTemplateId = String(
            obstacleTemplate?.Id ?? obstacleTemplate?.id ??
            fallbackBlocker?.Id ?? fallbackBlocker?.id ??
            defaultTemplateId ?? '');

        let navigationConfig: JsonRecord | null = null;
        try {
            const navRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/navigation-config`);
            if (!navRes.ok) throw new Error(`Bridge error ${navRes.status}`);
            navigationConfig = await navRes.json() as JsonRecord;
        } catch {
            navigationConfig = null;
        }

        const defaultMapInfo = mapInfos.find((m) => m.canBake) ?? mapInfos.find((m) => m.canEditTerrain) ?? mapInfos[0] ?? null;
        const defaultMapId = defaultMapInfo?.id ?? (maps.length > 0 ? maps[0] : null);
        const defaultBoardInfo = pickDefaultBoardInfo(defaultMapInfo);

        set({
            maps,
            mapInfos,
            selectedMapId: defaultMapId,
            selectedMapInfo: defaultMapInfo,
            selectedBoardName: defaultBoardInfo?.name ?? null,
            selectedBoardInfo: defaultBoardInfo,
            templates,
            performers,
            navigationConfig,
            navigationConfigVersion: Date.now(),
            selectedTemplateId: defaultTemplateId && defaultTemplateId.length > 0 ? defaultTemplateId : null,
            obstacleTemplateId: obstacleTemplateId.length > 0 ? obstacleTemplateId : null,
        });
    },

    selectMap: (mapId: string) => set((state) => {
        const selectedMapInfo = state.mapInfos.find((m) => m.id === mapId) ?? null;
        const selectedBoardInfo = pickDefaultBoardInfo(selectedMapInfo);
        return {
            selectedMapId: mapId,
            selectedMapInfo,
            selectedBoardName: selectedBoardInfo?.name ?? null,
            selectedBoardInfo,
            navSimulation: null,
            navSimulationVersion: Date.now(),
        };
    }),

    selectBoard: (boardName: string) => set((state) => {
        const selectedBoardInfo = findBoardInfo(state.selectedMapInfo, boardName);
        return {
            selectedBoardName: selectedBoardInfo?.name ?? boardName,
            selectedBoardInfo,
            navSimulation: null,
            navSimulationVersion: Date.now(),
        };
    }),

    createBoard: async (request) => {
        const { bridgeBaseUrl, selectedModId, selectedMapId } = get();
        if (!selectedModId || !selectedMapId) return;
        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/boards`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                name: request.name,
                spatialType: request.spatialType,
                widthInMacroTiles: request.widthInMacroTiles,
                heightInMacroTiles: request.heightInMacroTiles,
                cellSizeCm: request.cellSizeCm,
                hexEdgeLengthCm: request.hexEdgeLengthCm ?? DEFAULT_BOARD_METRICS.hexEdgeLengthCm,
                chunkSizeCells: DEFAULT_BOARD_METRICS.chunkSizeCells,
                navigationEnabled: request.navigationEnabled,
            }),
        });
        const json = await res.json().catch(() => null) as JsonRecord | null;
        if (!res.ok || json?.ok === false) throw new Error(errorMessage(json?.error ?? `Bridge error ${res.status}`));

        const mapInfo = normalizeMapInfo(asRecord(json.mapInfo));
        const boardInfo = findBoardInfo(mapInfo, request.name) ?? pickDefaultBoardInfo(mapInfo);
        set((state) => ({
            maps: state.maps.includes(selectedMapId) ? state.maps : [...state.maps, selectedMapId],
            mapInfos: replaceMapInfo(state.mapInfos, mapInfo),
            selectedMapInfo: mapInfo,
            selectedBoardName: boardInfo?.name ?? null,
            selectedBoardInfo: boardInfo,
            mapConfig: state.loadedMapId === selectedMapId ? (asRecord(json.map) ?? state.mapConfig) : state.mapConfig,
            navSimulation: null,
            navSimulationVersion: Date.now(),
        }));
    },

    updateSelectedBoard: async (request) => {
        const current = get();
        const { bridgeBaseUrl, selectedModId, selectedMapId, selectedBoardName, loadedModId, loadedMapId, loadedBoardName, terrain } = current;
        if (!selectedModId || !selectedMapId || !selectedBoardName) return;

        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/boards/${encodeURIComponent(selectedBoardName)}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                cellSizeCm: request.cellSizeCm,
                hexEdgeLengthCm: request.hexEdgeLengthCm,
                navigationEnabled: request.navigationEnabled,
            }),
        });
        const json = await res.json().catch(() => null) as JsonRecord | null;
        if (!res.ok || json?.ok === false) throw new Error(errorMessage(json?.error ?? `Bridge error ${res.status}`));

        const mapInfo = normalizeMapInfo(asRecord(json.mapInfo));
        const updatedBoardInfo = findBoardInfo(mapInfo, selectedBoardName) ?? pickDefaultBoardInfo(mapInfo);
        const loadedMapSame = loadedModId === selectedModId && loadedMapId === selectedMapId;
        const loadedBoardSame = loadedMapSame && loadedBoardName === selectedBoardName;
        const loadedBoardInfo = loadedMapSame && loadedBoardName ? findBoardInfo(mapInfo, loadedBoardName) ?? updatedBoardInfo : current.loadedBoardInfo;
        const nextMapConfig = loadedMapSame ? (asRecord(json.map) ?? current.mapConfig) : current.mapConfig;
        const nextBoardMetrics = loadedBoardSame
            ? resolveBoardMetricsFromMapConfig(nextMapConfig, mapInfo, loadedBoardName, loadedBoardInfo ?? updatedBoardInfo)
            : current.boardMetrics;
        const scaleChanged = loadedBoardSame && (
            nextBoardMetrics.topology !== current.boardMetrics.topology ||
            nextBoardMetrics.cellSizeCm !== current.boardMetrics.cellSizeCm ||
            nextBoardMetrics.hexEdgeLengthCm !== current.boardMetrics.hexEdgeLengthCm ||
            nextBoardMetrics.chunkSizeCells !== current.boardMetrics.chunkSizeCells
        );

        set((state) => {
            const next: Partial<EditorState> = {
                mapInfos: replaceMapInfo(state.mapInfos, mapInfo),
                selectedMapInfo: mapInfo,
                selectedBoardName: updatedBoardInfo?.name ?? state.selectedBoardName,
                selectedBoardInfo: updatedBoardInfo,
                mapConfig: nextMapConfig,
                navSimulation: null,
                navSimulationVersion: Date.now(),
            };

            if (loadedMapSame) {
                next.loadedMapInfo = mapInfo;
                next.loadedBoardInfo = loadedBoardInfo;
            }

            if (scaleChanged) {
                const dirtyChunks = new Set<string>();
                for (let y = 0; y < terrain.heightChunks; y++) {
                    for (let x = 0; x < terrain.widthChunks; x++) {
                        dirtyChunks.add(`${x},${y}`);
                    }
                }

                next.boardMetrics = nextBoardMetrics;
                next.navDirtyChunks = dirtyChunks;
                next.bakedNavTiles = new Map();
                next.bakedNavTilePayloads = [];
                next.bakedNavTilesVersion = Date.now();
            }

            return next;
        });
    },

    deleteSelectedBoard: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, selectedBoardName, loadedModId, loadedMapId, loadedBoardName } = get();
        if (!selectedModId || !selectedMapId || !selectedBoardName) return;
        if (loadedModId === selectedModId && loadedMapId === selectedMapId && loadedBoardName === selectedBoardName) {
            throw new Error(`Board '${selectedMapId}/${selectedBoardName}' is open on the canvas. Open another board before deleting it.`);
        }

        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/boards/${encodeURIComponent(selectedBoardName)}`, {
            method: 'DELETE',
        });
        const json = await res.json().catch(() => null) as JsonRecord | null;
        if (!res.ok || json?.ok === false) throw new Error(errorMessage(json?.error ?? `Bridge error ${res.status}`));

        const mapInfo = normalizeMapInfo(asRecord(json.mapInfo));
        set((state) => {
            const loadedBoardStillExists = state.loadedModId === selectedModId && state.loadedMapId === selectedMapId && state.loadedBoardName
                ? findBoardInfo(mapInfo, state.loadedBoardName)
                : null;
            const nextBoard = loadedBoardStillExists ?? pickDefaultBoardInfo(mapInfo);
            return {
                mapInfos: replaceMapInfo(state.mapInfos, mapInfo),
                selectedMapInfo: mapInfo,
                selectedBoardName: nextBoard?.name ?? null,
                selectedBoardInfo: nextBoard,
                mapConfig: state.loadedMapId === selectedMapId ? (asRecord(json.map) ?? state.mapConfig) : state.mapConfig,
                navSimulation: null,
                navSimulationVersion: Date.now(),
            };
        });
    },

    loadSelectedMap: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, selectedMapInfo, selectedBoardName, selectedBoardInfo, setLoading } = get();
        if (!selectedModId || !selectedMapId || !selectedBoardName) return;
        if (!selectedBoardInfo) throw new Error(`Selected board '${selectedBoardName}' was not found in map '${selectedMapId}'.`);
        setLoading(true, 'Loading MapConfig...', 10);
        const mapRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}`);
        if (!mapRes.ok) throw new Error(`Bridge error ${mapRes.status}`);
        const mapJson = await mapRes.json() as JsonRecord;
        const mapCfg = asRecord(mapJson.map);
        const boardMetrics = resolveBoardMetricsFromMapConfig(mapCfg, selectedMapInfo, selectedBoardName, selectedBoardInfo);
        const entities = Array.isArray(mapCfg?.Entities) ? mapCfg.Entities : (Array.isArray(mapCfg?.entities) ? mapCfg.entities : []);
        const spawnEntities = arrayOfRecords(entities).map((e) => {
            const template = String(e.Template ?? e.template ?? '');
            const overrides = asRecord(e.Overrides) ?? asRecord(e.overrides) ?? {};
            const worldPositionUpper = asRecord(overrides.WorldPositionCm);
            const worldPositionLower = asRecord(overrides.worldPositionCm);
            const wpcm = asRecord(worldPositionUpper?.Value) ?? asRecord(worldPositionLower?.value);
            let posX: number, posY: number;
            if (wpcm && (wpcm.X !== undefined || wpcm.Y !== undefined)) {
                const cell = worldCmToCell(Number(wpcm.X ?? 0), Number(wpcm.Y ?? 0), boardMetrics);
                posX = cell.col;
                posY = cell.row;
            } else {
                const positionUpper = asRecord(e.Position);
                const positionLower = asRecord(e.position);
                posX = Number(positionUpper?.X ?? positionLower?.x ?? 0);
                posY = Number(positionUpper?.Y ?? positionLower?.y ?? 0);
            }
            return { template, position: { x: posX, y: posY }, overrides };
        });

        set({ mapConfig: mapCfg, boardMetrics, spawnEntities, selectedEntityIndex: null, entitiesVersion: Date.now() });

        // Apply DefaultCamera from map config to editor camera
        const defCam = asRecord(mapCfg?.DefaultCamera) ?? asRecord(mapCfg?.defaultCamera);
        if (defCam) {
            const cam = get().cameraRef.current;
            const controls = get().controlsRef.current;
            if (cam && controls) {
                const yaw = Number(defCam.Yaw ?? defCam.yaw ?? 180) * Math.PI / 180;
                const pitch = Number(defCam.Pitch ?? defCam.pitch ?? 45) * Math.PI / 180;
                const distCm = Number(defCam.DistanceCm ?? defCam.distanceCm ?? 14142);
                const fov = Number(defCam.FovYDeg ?? defCam.fovYDeg ?? 60);
                const txCm = Number(defCam.TargetXCm ?? defCam.targetXCm ?? 0);
                const tyCm = Number(defCam.TargetYCm ?? defCam.targetYCm ?? 0);

                const distM = distCm * 0.01;
                const hDist = distM * Math.cos(pitch);
                const vDist = distM * Math.sin(pitch);
                const targetX = txCm * 0.01;
                const targetZ = tyCm * 0.01;

                const camX = targetX + hDist * Math.sin(yaw);
                const camY = vDist;
                const camZ = targetZ - hDist * Math.cos(yaw);

                cam.position.set(camX, camY, camZ);
                (cam as PerspectiveCamera).fov = fov;
                (cam as PerspectiveCamera).updateProjectionMatrix();
                controls.target.set(targetX, 0, targetZ);
                controls.update();
            }
        }

        setLoading(true, 'Loading Terrain...', 40);
        const terrRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/terrain-react?boardName=${encodeURIComponent(selectedBoardName)}`);
        if (!terrRes.ok) throw new Error(`Bridge error ${terrRes.status}`);
        const buf = await terrRes.arrayBuffer();
        const view = new DataView(buf);
        const w = view.getInt32(0, true);
        const h = view.getInt32(4, true);
        const stride = view.getUint8(8);
        if (stride !== 4) throw new Error(`Invalid terrain stride ${stride}`);
        const data = new Uint8Array(buf.slice(9));
        const newTerrain = new TerrainStore(w, h);
        newTerrain.loadFromBytes(w, h, data);
        const allChunks = new Set<string>();
        for (let y = 0; y < h; y++) {
            for (let x = 0; x < w; x++) allChunks.add(`${x},${y}`);
        }
        set({
            terrain: newTerrain,
            boardMetrics,
            canvasSessionKind: 'repo',
            canvasSessionLabel: null,
            loadedModId: selectedModId,
            loadedMapId: selectedMapId,
            loadedMapInfo: selectedMapInfo,
            loadedBoardName: selectedBoardName,
            loadedBoardInfo: selectedBoardInfo,
            minimapDirtyChunks: allChunks,
            navDirtyChunks: new Set(),
            bakedNavTiles: new Map(),
            bakedNavTilePayloads: [],
            bakedNavTilesVersion: Date.now(),
            navSimulation: null,
            navSimulationVersion: Date.now(),
            loadingState: { isLoading: true, message: 'Loading Map...', progress: 0 },
        });
        setLoading(false);
    },

    saveSelectedMap: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, selectedBoardName, loadedModId, loadedMapId, loadedBoardName, mapConfig, terrain, setLoading, spawnEntities, boardMetrics } = get();
        if (!selectedModId || !selectedMapId || !selectedBoardName) return;
        if (!mapConfig) throw new Error('No MapConfig loaded.');
        if (loadedModId !== selectedModId || loadedMapId !== selectedMapId || loadedBoardName !== selectedBoardName) {
            throw new Error(`Selected board '${selectedMapId}/${selectedBoardName}' is not loaded on the canvas. Click Open before saving.`);
        }

        setLoading(true, 'Saving MapConfig...', 20);
        const mapPayload = JSON.parse(JSON.stringify(mapConfig));
        delete mapPayload.Id;
        delete mapPayload.DefaultCamera;
        delete mapPayload.Entities;
        mapPayload.id = selectedMapId;

        // Save current editor camera as DefaultCamera
        const cam = get().cameraRef.current;
        const controls = get().controlsRef.current;
        if (cam && controls) {
            const target = controls.target;
            const pos = cam.position;
            const dx = pos.x - target.x;
            const dy = pos.y - target.y;
            const dz = pos.z - target.z;
            const dist = Math.sqrt(dx * dx + dy * dy + dz * dz);
            const hDist = Math.sqrt(dx * dx + dz * dz);
            const pitch = Math.atan2(dy, hDist) * 180 / Math.PI;
            const yaw = Math.atan2(dx, -dz) * 180 / Math.PI;
            mapPayload.defaultCamera = {
                targetXCm: Math.round(target.x * 100),
                targetYCm: Math.round(target.z * 100),
                yaw: Math.round(yaw * 10) / 10,
                pitch: Math.round(pitch * 10) / 10,
                distanceCm: Math.round(dist * 100),
                fovYDeg: (cam as PerspectiveCamera).fov,
            };
        }

        mapPayload.entities = spawnEntities.map((e) => {
            const cm = cellToWorldCm(e.position.x, e.position.y, boardMetrics);
            const overrides = { ...(e.overrides ?? {}) };
            overrides['WorldPositionCm'] = { Value: { X: cm.xCm, Y: cm.yCm } };
            return {
                template: e.template,
                position: { x: e.position.x, y: e.position.y },
                overrides,
            };
        });
        const mapRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(mapPayload),
        });
        if (!mapRes.ok) throw new Error(`Bridge error ${mapRes.status}`);

        setLoading(true, 'Saving Terrain...', 60);
        const header = new Uint8Array(9);
        const view = new DataView(header.buffer);
        view.setInt32(0, terrain.widthChunks, true);
        view.setInt32(4, terrain.heightChunks, true);
        view.setUint8(8, 4);
        const blob = new Blob([header, terrain.serialize()], { type: 'application/octet-stream' });

        const terrRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/terrain-react?boardName=${encodeURIComponent(selectedBoardName)}`, {
            method: 'PUT',
            body: blob,
        });
        if (!terrRes.ok) throw new Error(`Bridge error ${terrRes.status}`);
        set({
            canvasSessionKind: 'repo',
            canvasSessionLabel: null,
            loadedModId: selectedModId,
            loadedMapId: selectedMapId,
            loadedMapInfo: get().selectedMapInfo,
            loadedBoardName: selectedBoardName,
            loadedBoardInfo: get().selectedBoardInfo,
            mapConfig: mapPayload,
            minimapDirtyChunks: new Set(),
            navDirtyChunks: new Set(),
        });
        terrain.clearDirty();
        setLoading(false);
    },

    loadNavigationConfig: async () => {
        const { bridgeBaseUrl, selectedModId } = get();
        if (!selectedModId) return;
        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/navigation-config`);
        if (!res.ok) throw new Error(`Bridge error ${res.status}`);
        const json = await res.json();
        set({ navigationConfig: json, navigationConfigVersion: Date.now() });
    },

    saveNavigationConfig: async () => {
        const { bridgeBaseUrl, selectedModId, navigationConfig } = get();
        if (!selectedModId || !navigationConfig) return;
        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/navigation-config`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                agentProfiles: navigationConfig.agentProfiles ?? [],
                navmesh: navigationConfig.navmesh ?? {},
            }),
        });
        const json = await res.json().catch(() => null);
        if (!res.ok || json?.ok === false) {
            throw new Error(json?.error ?? `Bridge error ${res.status}`);
        }
        set({
            navigationConfig: {
                ...navigationConfig,
                paths: json?.paths,
                validated: json?.validated,
            },
            navigationConfigVersion: Date.now(),
        });
    },

    setNavigationConfig: (config) => set({ navigationConfig: config, navigationConfigVersion: Date.now() }),

    selectTemplate: (templateId) => set({ selectedTemplateId: templateId }),
    setObstacleTemplate: (templateId) => set({ obstacleTemplateId: templateId }),
    setObstacleShape: (shape) => set({ obstacleShape: shape }),
    setObstacleRadiusCm: (radiusCm) => set({ obstacleRadiusCm: Math.max(1, Math.floor(radiusCm || 1)) }),
    setObstacleHalfSizeCm: (halfWidthCm, halfHeightCm) => set({
        obstacleHalfWidthCm: Math.max(1, Math.floor(halfWidthCm || 1)),
        obstacleHalfHeightCm: Math.max(1, Math.floor(halfHeightCm || 1)),
    }),

    placeEntityAt: (c, r) => set((state) => {
        if (!state.selectedTemplateId) return state;
        const next = state.spawnEntities.slice();
        const idx = next.findIndex((e) => e.position.x === c && e.position.y === r);
        const cm = cellToWorldCm(c, r, state.boardMetrics);
        const overrides: Record<string, unknown> = {
            WorldPositionCm: { Value: { X: cm.xCm, Y: cm.yCm } },
        };
        const entity = { template: state.selectedTemplateId, position: { x: c, y: r }, overrides };
        if (idx >= 0) next[idx] = entity;
        else next.push(entity);
        return { spawnEntities: next, selectedEntityIndex: idx >= 0 ? idx : next.length - 1, entitiesVersion: Date.now() };
    }),

    placeObstacleAt: (c, r) => set((state) => {
        const template = state.obstacleTemplateId || state.selectedTemplateId;
        if (!template) return state;
        const next = state.spawnEntities.slice();
        const idx = next.findIndex((e) => e.position.x === c && e.position.y === r);
        const cm = cellToWorldCm(c, r, state.boardMetrics);
        const radius = Math.max(1, Math.floor(state.obstacleRadiusCm));
        const halfWidth = Math.max(1, Math.floor(state.obstacleHalfWidthCm));
        const halfHeight = Math.max(1, Math.floor(state.obstacleHalfHeightCm));
        const intent = state.obstacleShape === 'Box'
            ? {
                shape: 'Box',
                sinkPhysicsCollider: false,
                sinkNavigationObstacle: true,
                navRadiusCm: Math.max(halfWidth, halfHeight),
                halfWidthCm: halfWidth,
                halfHeightCm: halfHeight,
                localOffsetXCm: 0,
                localOffsetYCm: 0,
            }
            : {
                shape: 'Circle',
                sinkPhysicsCollider: false,
                sinkNavigationObstacle: true,
                navRadiusCm: radius,
                radiusCm: radius,
                localOffsetXCm: 0,
                localOffsetYCm: 0,
            };
        const overrides: Record<string, unknown> = {
            WorldPositionCm: { Value: { X: cm.xCm, Y: cm.yCm } },
            ManifestationObstacleIntent2D: intent,
        };
        const entity = {
            template,
            position: { x: c, y: r },
            overrides,
        };
        if (idx >= 0) next[idx] = entity;
        else next.push(entity);
        const navDirtyChunks = new Set(state.navDirtyChunks);
        addObstacleFootprintDirtyChunks(navDirtyChunks, c, r, state.boardMetrics, state.terrain.widthChunks, state.terrain.heightChunks, intent.navRadiusCm);
        return { spawnEntities: next, selectedEntityIndex: idx >= 0 ? idx : next.length - 1, entitiesVersion: Date.now(), navDirtyChunks };
    }),

    removeEntityAt: (c, r) => set((state) => {
        const removed = state.spawnEntities.filter((e) => e.position.x === c && e.position.y === r);
        const next = state.spawnEntities.filter((e) => !(e.position.x === c && e.position.y === r));
        const navDirtyChunks = new Set(state.navDirtyChunks);
        for (let i = 0; i < removed.length; i++) {
            const radiusCm = readObstacleNavRadiusCm(removed[i].overrides);
            if (radiusCm != null) addObstacleFootprintDirtyChunks(navDirtyChunks, c, r, state.boardMetrics, state.terrain.widthChunks, state.terrain.heightChunks, radiusCm);
        }
        return { spawnEntities: next, selectedEntityIndex: null, entitiesVersion: Date.now(), navDirtyChunks };
    }),

    selectEntityAt: (c, r) => set((state) => {
        const idx = state.spawnEntities.findIndex((e) => e.position.x === c && e.position.y === r);
        return { selectedEntityIndex: idx >= 0 ? idx : null };
    }),

    updateSelectedEntityOverridesJson: (componentName, jsonText) => set((state) => {
        if (state.selectedEntityIndex == null) return state;
        const idx = state.selectedEntityIndex;
        if (idx < 0 || idx >= state.spawnEntities.length) return state;

        let parsed: unknown;
        try {
            parsed = JSON.parse(jsonText);
        } catch {
            return state;
        }

        const next = state.spawnEntities.slice();
        const cur = next[idx];
        const overrides = { ...(cur.overrides ?? {}) };
        overrides[componentName] = parsed;
        next[idx] = { ...cur, overrides };
        return { spawnEntities: next, entitiesVersion: Date.now() };
    }),

    deleteSelectedEntityOverride: (componentName) => set((state) => {
        if (state.selectedEntityIndex == null) return state;
        const idx = state.selectedEntityIndex;
        if (idx < 0 || idx >= state.spawnEntities.length) return state;
        const next = state.spawnEntities.slice();
        const cur = next[idx];
        const overrides = { ...(cur.overrides ?? {}) };
        delete overrides[componentName];
        next[idx] = { ...cur, overrides };
        return { spawnEntities: next, entitiesVersion: Date.now() };
    }),

    reportDirtyChunks: (keys) => set((state) => {
        const minimapSet = new Set(state.minimapDirtyChunks);
        const navSet = new Set(state.navDirtyChunks);
        for (const k of keys) {
            minimapSet.add(k);
            navSet.add(k);
        }
        return { minimapDirtyChunks: minimapSet, navDirtyChunks: navSet };
    }),

    reportMinimapDirtyChunks: (keys) => set((state) => {
        const minimapSet = new Set(state.minimapDirtyChunks);
        for (const k of keys) minimapSet.add(k);
        return { minimapDirtyChunks: minimapSet };
    }),
    
    clearMinimapDirty: () => set({ minimapDirtyChunks: new Set() }),
    clearNavDirty: () => set({ navDirtyChunks: new Set() }),

    setLoading: (isLoading, message = '', progress = 0) => set({ 
        loadingState: { isLoading, message, progress } 
    }),

    registerCamera: (camera, controls) => {
        const { cameraRef, controlsRef } = get();
        cameraRef.current = camera;
        controlsRef.current = controls;
    }
}));

function normalizeBoardInfo(raw: JsonRecord | null | undefined): BoardInfo {
    const spatialTypeRaw = raw?.spatialType ?? raw?.SpatialType ?? null;
    const spatialType = spatialTypeRaw == null ? null : normalizeSpatialTopology(spatialTypeRaw);
    return {
        name: String(raw?.name ?? raw?.Name ?? ''),
        spatialType,
        widthChunks: numberOr(raw?.widthChunks ?? raw?.WidthChunks, 0),
        heightChunks: numberOr(raw?.heightChunks ?? raw?.HeightChunks, 0),
        cellSizeCm: numberOr(raw?.cellSizeCm ?? raw?.CellSizeCm, DEFAULT_BOARD_METRICS.cellSizeCm),
        hexEdgeLengthCm: numberOr(raw?.hexEdgeLengthCm ?? raw?.HexEdgeLengthCm, DEFAULT_BOARD_METRICS.hexEdgeLengthCm),
        chunkSizeCells: numberOr(raw?.chunkSizeCells ?? raw?.ChunkSizeCells, DEFAULT_BOARD_METRICS.chunkSizeCells),
        navigationEnabled: Boolean(raw?.navigationEnabled ?? raw?.NavigationEnabled ?? false),
        hasDataFile: Boolean(raw?.hasDataFile ?? raw?.HasDataFile ?? false),
        dataFileExists: Boolean(raw?.dataFileExists ?? raw?.DataFileExists ?? false),
        dataFile: stringOrNull(raw?.dataFile ?? raw?.DataFile),
        canEditTerrain: Boolean(raw?.canEditTerrain ?? raw?.CanEditTerrain ?? false),
        canBake: Boolean(raw?.canBake ?? raw?.CanBake ?? false),
        reason: String(raw?.reason ?? raw?.Reason ?? ''),
    };
}

function normalizeMapInfo(raw: JsonRecord | null | undefined): MapInfo {
    const id = String(raw?.id ?? raw?.Id ?? '');
    const primary = normalizeBoardInfo({
        Name: raw?.boardName ?? raw?.BoardName ?? '',
        SpatialType: raw?.spatialType ?? raw?.SpatialType ?? null,
        WidthChunks: raw?.widthChunks ?? raw?.WidthChunks,
        HeightChunks: raw?.heightChunks ?? raw?.HeightChunks,
        CellSizeCm: raw?.cellSizeCm ?? raw?.CellSizeCm,
        HexEdgeLengthCm: raw?.hexEdgeLengthCm ?? raw?.HexEdgeLengthCm,
        ChunkSizeCells: raw?.chunkSizeCells ?? raw?.ChunkSizeCells,
        NavigationEnabled: raw?.navigationEnabled ?? raw?.NavigationEnabled,
        HasDataFile: raw?.hasDataFile ?? raw?.HasDataFile,
        DataFileExists: raw?.dataFileExists ?? raw?.DataFileExists,
        DataFile: raw?.dataFile ?? raw?.DataFile,
        CanEditTerrain: raw?.canEditTerrain ?? raw?.CanEditTerrain,
        CanBake: raw?.canBake ?? raw?.CanBake,
        Reason: raw?.reason ?? raw?.Reason,
    });
    const boardsRaw = Array.isArray(raw?.boards) ? raw.boards : (Array.isArray(raw?.Boards) ? raw.Boards : []);
    const boards = arrayOfRecords(boardsRaw).map(normalizeBoardInfo).filter((b: BoardInfo) => b.name.length > 0);
    const mergedBoards = boards.length > 0 ? boards : (primary.name.length > 0 ? [primary] : []);
    return {
        ...primary,
        id,
        found: Boolean(raw?.found ?? raw?.Found ?? false),
        hasBoards: Boolean(raw?.hasBoards ?? raw?.HasBoards ?? false),
        boardName: stringOrNull(raw?.boardName ?? raw?.BoardName),
        boards: mergedBoards,
    };
}

function resolveBoardMetricsFromMapConfig(
    mapCfg: JsonRecord | null,
    mapInfo: MapInfo | null,
    boardName: string | null,
    boardInfo: BoardInfo | null,
): BoardMetrics {
    const boards = arrayOfRecords(Array.isArray(mapCfg?.Boards) ? mapCfg.Boards : (Array.isArray(mapCfg?.boards) ? mapCfg.boards : []));
    const selectedBoard = boardName
        ? boards.find((b) => String(b?.Name ?? b?.name ?? '') === boardName)
        : pickPrimaryBoard(boards);
    return normalizeBoardMetrics({
        topology: normalizeTopology(
            selectedBoard?.SpatialType ??
            selectedBoard?.spatialType ??
            boardInfo?.spatialType ??
            mapInfo?.spatialType ??
            DEFAULT_BOARD_METRICS.topology),
        cellSizeCm: numberOr(
            selectedBoard?.GridCellSizeCm ??
            selectedBoard?.gridCellSizeCm ??
            boardInfo?.cellSizeCm ??
            mapInfo?.cellSizeCm,
            DEFAULT_BOARD_METRICS.cellSizeCm),
        hexEdgeLengthCm: numberOr(
            selectedBoard?.HexEdgeLengthCm ??
            selectedBoard?.hexEdgeLengthCm ??
            boardInfo?.hexEdgeLengthCm ??
            mapInfo?.hexEdgeLengthCm,
            DEFAULT_BOARD_METRICS.hexEdgeLengthCm),
        chunkSizeCells: numberOr(
            selectedBoard?.ChunkSizeCells ??
            selectedBoard?.chunkSizeCells ??
            boardInfo?.chunkSizeCells ??
            mapInfo?.chunkSizeCells,
            DEFAULT_BOARD_METRICS.chunkSizeCells),
    });
}

function pickDefaultBoardInfo(mapInfo: MapInfo | null): BoardInfo | null {
    if (!mapInfo) return null;
    return mapInfo.boards.find((b) => b.canBake) ??
        mapInfo.boards.find((b) => b.canEditTerrain) ??
        (mapInfo.boardName ? findBoardInfo(mapInfo, mapInfo.boardName) : null) ??
        mapInfo.boards[0] ??
        null;
}

function findBoardInfo(mapInfo: MapInfo | null, boardName: string): BoardInfo | null {
    if (!mapInfo) return null;
    return mapInfo.boards.find((b) => b.name === boardName) ?? null;
}

function replaceMapInfo(mapInfos: MapInfo[], next: MapInfo): MapInfo[] {
    let replaced = false;
    const result = mapInfos.map((info) => {
        if (info.id !== next.id) return info;
        replaced = true;
        return next;
    });
    if (!replaced) result.push(next);
    return result;
}

function pickPrimaryBoard(boards: JsonRecord[]): JsonRecord | null {
    const navigationDefault = boards.find((b) =>
        isNavigationEnabled(b) && String(b?.Name ?? b?.name ?? '').toLowerCase() === 'default');
    if (navigationDefault) return navigationDefault;

    const navigationBoard = boards.find(isNavigationEnabled);
    if (navigationBoard) return navigationBoard;

    const defaultBoard = boards.find((b) => String(b?.Name ?? b?.name ?? '').toLowerCase() === 'default');
    return defaultBoard ?? boards[0] ?? null;
}

function isNavigationEnabled(board: JsonRecord): boolean {
    return Boolean(board?.NavigationEnabled ?? board?.navigationEnabled ?? false);
}

function addObstacleFootprintDirtyChunks(
    dst: Set<string>,
    col: number,
    row: number,
    metrics: BoardMetrics,
    widthChunks: number,
    heightChunks: number,
    radiusCm: number,
) {
    const radiusCells = Math.max(1, Math.ceil(Math.max(1, radiusCm) / Math.max(1, metrics.cellSizeCm)));
    const minCol = col - radiusCells;
    const maxCol = col + radiusCells;
    const minRow = row - radiusCells;
    const maxRow = row + radiusCells;
    for (let y = minRow; y <= maxRow; y++) {
        for (let x = minCol; x <= maxCol; x++) {
            const cx = Math.floor(x / metrics.chunkSizeCells);
            const cy = Math.floor(y / metrics.chunkSizeCells);
            if (cx >= 0 && cx < widthChunks && cy >= 0 && cy < heightChunks) dst.add(`${cx},${cy}`);
        }
    }
}

function readObstacleNavRadiusCm(overrides: Record<string, unknown> | null | undefined): number | null {
    const intent = asRecord(overrides?.ManifestationObstacleIntent2D) ?? asRecord(overrides?.manifestationObstacleIntent2D);
    if (!intent) return null;
    const raw = intent.navRadiusCm ?? intent.NavRadiusCm ?? intent.radiusCm ?? intent.RadiusCm;
    const parsed = Number(raw);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function asRecord(value: unknown): JsonRecord | null {
    return value != null && typeof value === 'object' && !Array.isArray(value)
        ? value as JsonRecord
        : null;
}

function arrayOfRecords(value: unknown): JsonRecord[] {
    return Array.isArray(value)
        ? value.map(asRecord).filter((item): item is JsonRecord => item != null)
        : [];
}

function errorMessage(value: unknown): string {
    if (value instanceof Error) return value.message;
    if (value && typeof value === 'object' && 'message' in value) {
        return String((value as { message?: unknown }).message ?? value);
    }
    return String(value ?? 'Unknown error');
}

function clampNavQueryCell(cell: NavQueryCell, state: EditorState): NavQueryCell {
    const maxCol = Math.max(0, state.terrain.widthChunks * state.boardMetrics.chunkSizeCells - 1);
    const maxRow = Math.max(0, state.terrain.heightChunks * state.boardMetrics.chunkSizeCells - 1);
    return {
        col: Math.max(0, Math.min(Math.floor(cell.col || 0), maxCol)),
        row: Math.max(0, Math.min(Math.floor(cell.row || 0), maxRow)),
    };
}

function numberOr(value: unknown, fallback: number): number {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
}

function stringOrNull(value: unknown): string | null {
    if (value == null) return null;
    const text = String(value);
    return text.length > 0 ? text : null;
}
