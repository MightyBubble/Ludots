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
import type { NavTile } from '../../Core/NavMesh/NavTileBinary';

export type ToolCategory = 'Height' | 'Water' | 'Area' | 'Blocked' | 'Biome' | 'Vegetation' | 'Ramp' | 'Layers' | 'Territory' | 'Entities' | 'Obstacle';
export type ToolMode = 'Set' | 'Raise' | 'Lower' | 'Smooth' | 'Bucket'; // Added Bucket

export interface MapInfo {
    id: string;
    found: boolean;
    hasBoards: boolean;
    boardName: string | null;
    spatialType: SpatialTopology | null;
    widthChunks: number;
    heightChunks: number;
    cellSizeCm: number;
    chunkSizeCells: number;
    navigationEnabled: boolean;
    hasDataFile: boolean;
    dataFileExists: boolean;
    dataFile: string | null;
    canEditTerrain: boolean;
    canBake: boolean;
    reason: string;
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
    loadedModId: string | null;
    loadedMapId: string | null;
    loadedMapInfo: MapInfo | null;
    mapConfig: any | null;
    templates: any[];
    performers: any[];
    navigationConfig: any | null;
    navigationConfigVersion: number;
    selectedTemplateId: string | null;
    obstacleTemplateId: string | null;
    obstacleShape: 'Circle' | 'Box';
    obstacleRadiusCm: number;
    obstacleHalfWidthCm: number;
    obstacleHalfHeightCm: number;
    spawnEntities: Array<{ template: string; position: { x: number; y: number }; overrides: Record<string, any> }>;
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
    bakedNavTiles: Map<string, NavTile>;
    bakedNavTilesVersion: number;
    
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
    loadSelectedMap: () => Promise<void>;
    saveSelectedMap: () => Promise<void>;
    loadNavigationConfig: () => Promise<void>;
    saveNavigationConfig: () => Promise<void>;
    setNavigationConfig: (config: any | null) => void;
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
    clearMinimapDirty: () => void;
    clearNavDirty: () => void;

    // Loading State
    loadingState: { isLoading: boolean, message: string, progress: number };
    setLoading: (isLoading: boolean, message?: string, progress?: number) => void;

    // Camera Bridge (Non-reactive refs for performance)
    cameraRef: { current: Camera | null };
    controlsRef: { current: any | null }; // OrbitControls
    registerCamera: (camera: Camera, controls: any) => void;

    // NavMesh Actions
    bakeNavMesh: () => void;
    setBakedNavTiles: (tiles: NavTile[]) => void;
    clearBakedNavTiles: () => void;
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
    loadedModId: null,
    loadedMapId: null,
    loadedMapInfo: null,
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
    bakedNavTilesVersion: 0,
    
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

    setBakedNavTiles: (tiles) => set(() => {
        const map = new Map<string, NavTile>();
        for (let i = 0; i < tiles.length; i++) {
            const t = tiles[i];
            map.set(`${t.tileId.chunkX},${t.tileId.chunkY},${t.tileId.layer}`, t);
        }
        return { bakedNavTiles: map, bakedNavTilesVersion: Date.now() };
    }),

    clearBakedNavTiles: () => set(() => ({ bakedNavTiles: new Map(), bakedNavTilesVersion: Date.now() })),

    initMap: (w, h, metrics) => set({
        terrain: new TerrainStore(w, h), 
        boardMetrics: normalizeBoardMetrics(metrics ?? get().boardMetrics),
        loadedModId: null,
        loadedMapId: null,
        loadedMapInfo: null,
        minimapDirtyChunks: new Set(),
        navDirtyChunks: new Set(),
        bakedNavTiles: new Map(),
        bakedNavTilesVersion: Date.now(),
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
            loadedModId: null,
            loadedMapId: null,
            loadedMapInfo: null,
            minimapDirtyChunks: allChunks,
            navDirtyChunks: new Set(),
            bakedNavTiles: new Map(),
            bakedNavTilesVersion: Date.now(),
            loadingState: { isLoading: true, message: 'Loading Map...', progress: 0 }
        });
    },

    refreshMods: async () => {
        const { bridgeBaseUrl } = get();
        const res = await fetch(`${bridgeBaseUrl}/api/mods`);
        if (!res.ok) throw new Error(`Bridge error ${res.status}`);
        const json = await res.json();
        const mods = (json.mods ?? []).map((m: any) => ({
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
            loadedModId: null,
            loadedMapId: null,
            loadedMapInfo: null,
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
        });
        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/maps`);
        if (!res.ok) throw new Error(`Bridge error ${res.status}`);
        const json = await res.json();
        const maps = (json.maps ?? []).map((x: any) => String(x));
        const mapInfos = (json.mapInfos ?? []).map(normalizeMapInfo);
        const tRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/entity-templates`);
        if (!tRes.ok) throw new Error(`Bridge error ${tRes.status}`);
        const tJson = await tRes.json();
        const templates = tJson.templates ?? [];

        const pRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/performers`);
        if (!pRes.ok) throw new Error(`Bridge error ${pRes.status}`);
        const pJson = await pRes.json();
        const performers = pJson.performers ?? [];

        const defaultTemplateId = templates.length > 0 ? String(templates[0]?.Id ?? templates[0]?.id ?? '') : null;
        const obstacleTemplate = templates.find((t: any) => {
            const components = t?.Components ?? t?.components ?? {};
            return components?.ManifestationObstacleIntent2D || components?.manifestationObstacleIntent2D;
        });
        const fallbackBlocker = templates.find((t: any) => {
            const id = String(t?.Id ?? t?.id ?? '');
            return id.toLowerCase().includes('blocker') || id.toLowerCase().includes('obstacle');
        });
        const obstacleTemplateId = String(
            obstacleTemplate?.Id ?? obstacleTemplate?.id ??
            fallbackBlocker?.Id ?? fallbackBlocker?.id ??
            defaultTemplateId ?? '');

        let navigationConfig: any | null = null;
        try {
            const navRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/navigation-config`);
            if (!navRes.ok) throw new Error(`Bridge error ${navRes.status}`);
            navigationConfig = await navRes.json();
        } catch {
            navigationConfig = null;
        }

        const defaultMapInfo = mapInfos.find((m) => m.canBake) ?? mapInfos.find((m) => m.canEditTerrain) ?? mapInfos[0] ?? null;
        const defaultMapId = defaultMapInfo?.id ?? (maps.length > 0 ? maps[0] : null);

        set({
            maps,
            mapInfos,
            selectedMapId: defaultMapId,
            selectedMapInfo: defaultMapInfo,
            templates,
            performers,
            navigationConfig,
            navigationConfigVersion: Date.now(),
            selectedTemplateId: defaultTemplateId && defaultTemplateId.length > 0 ? defaultTemplateId : null,
            obstacleTemplateId: obstacleTemplateId.length > 0 ? obstacleTemplateId : null,
        });
    },

    selectMap: (mapId: string) => set((state) => ({
        selectedMapId: mapId,
        selectedMapInfo: state.mapInfos.find((m) => m.id === mapId) ?? null,
        loadedModId: null,
        loadedMapId: null,
        loadedMapInfo: null,
        mapConfig: null,
        spawnEntities: [],
        selectedEntityIndex: null,
        entitiesVersion: Date.now(),
        navDirtyChunks: new Set(),
        bakedNavTiles: new Map(),
        bakedNavTilesVersion: Date.now(),
    })),

    loadSelectedMap: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, selectedMapInfo, loadMap, setLoading } = get();
        if (!selectedModId || !selectedMapId) return;
        setLoading(true, 'Loading MapConfig...', 10);
        const mapRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}`);
        if (!mapRes.ok) throw new Error(`Bridge error ${mapRes.status}`);
        const mapJson = await mapRes.json();
        const mapCfg = mapJson.map ?? null;
        const boardMetrics = resolveBoardMetricsFromMapConfig(mapCfg, selectedMapInfo);
        const entities = Array.isArray(mapCfg?.Entities) ? mapCfg.Entities : (Array.isArray(mapCfg?.entities) ? mapCfg.entities : []);
        const spawnEntities = entities.map((e: any) => {
            const template = String(e.Template ?? e.template ?? '');
            const overrides = (e.Overrides ?? e.overrides ?? {}) as Record<string, any>;
            const wpcm = overrides?.WorldPositionCm?.Value ?? overrides?.worldPositionCm?.value;
            let posX: number, posY: number;
            if (wpcm && (wpcm.X !== undefined || wpcm.Y !== undefined)) {
                const cell = worldCmToCell(Number(wpcm.X ?? 0), Number(wpcm.Y ?? 0), boardMetrics);
                posX = cell.col;
                posY = cell.row;
            } else {
                posX = Number(e.Position?.X ?? e.position?.x ?? 0);
                posY = Number(e.Position?.Y ?? e.position?.y ?? 0);
            }
            return { template, position: { x: posX, y: posY }, overrides };
        });

        set({ mapConfig: mapCfg, boardMetrics, spawnEntities, selectedEntityIndex: null, entitiesVersion: Date.now() });

        // Apply DefaultCamera from map config to editor camera
        const defCam = mapCfg?.DefaultCamera ?? mapCfg?.defaultCamera;
        if (defCam) {
            const cam = get().cameraRef.current;
            const controls = get().controlsRef.current;
            if (cam && controls) {
                const HEX_W = 6.92820323;
                const ROW_S = 6.0;
                const yaw = (defCam.Yaw ?? defCam.yaw ?? 180) * Math.PI / 180;
                const pitch = (defCam.Pitch ?? defCam.pitch ?? 45) * Math.PI / 180;
                const distCm = defCam.DistanceCm ?? defCam.distanceCm ?? 14142;
                const fov = defCam.FovYDeg ?? defCam.fovYDeg ?? 60;
                const txCm = defCam.TargetXCm ?? defCam.targetXCm ?? 0;
                const tyCm = defCam.TargetYCm ?? defCam.targetYCm ?? 0;

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
        const terrRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/terrain-react`);
        if (!terrRes.ok) throw new Error(`Bridge error ${terrRes.status}`);
        const buf = await terrRes.arrayBuffer();
        const view = new DataView(buf);
        const w = view.getInt32(0, true);
        const h = view.getInt32(4, true);
        const stride = view.getUint8(8);
        if (stride !== 4) throw new Error(`Invalid terrain stride ${stride}`);
        const data = new Uint8Array(buf.slice(9));
        loadMap(data, w, h, boardMetrics);
        set({
            loadedModId: selectedModId,
            loadedMapId: selectedMapId,
            loadedMapInfo: selectedMapInfo,
        });
        setLoading(false);
    },

    saveSelectedMap: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, loadedModId, loadedMapId, mapConfig, terrain, setLoading, spawnEntities, boardMetrics } = get();
        if (!selectedModId || !selectedMapId) return;
        if (!mapConfig) throw new Error('No MapConfig loaded.');
        if (loadedModId !== selectedModId || loadedMapId !== selectedMapId) {
            throw new Error(`Selected map '${selectedMapId}' is not loaded on the canvas. Click Load Repo before saving.`);
        }

        setLoading(true, 'Saving MapConfig...', 20);
        mapConfig.Id = selectedMapId;

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
            mapConfig.DefaultCamera = {
                TargetXCm: Math.round(target.x * 100),
                TargetYCm: Math.round(target.z * 100),
                Yaw: Math.round(yaw * 10) / 10,
                Pitch: Math.round(pitch * 10) / 10,
                DistanceCm: Math.round(dist * 100),
                FovYDeg: (cam as PerspectiveCamera).fov,
            };
        }

        mapConfig.Entities = spawnEntities.map((e) => {
            const cm = cellToWorldCm(e.position.x, e.position.y, boardMetrics);
            const overrides = { ...(e.overrides ?? {}) };
            overrides['WorldPositionCm'] = { Value: { X: cm.xCm, Y: cm.yCm } };
            return {
                Template: e.template,
                Position: { X: e.position.x, Y: e.position.y },
                Overrides: overrides,
            };
        });
        const mapRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(mapConfig),
        });
        if (!mapRes.ok) throw new Error(`Bridge error ${mapRes.status}`);

        setLoading(true, 'Saving Terrain...', 60);
        const header = new Uint8Array(9);
        const view = new DataView(header.buffer);
        view.setInt32(0, terrain.widthChunks, true);
        view.setInt32(4, terrain.heightChunks, true);
        view.setUint8(8, 4);
        const blob = new Blob([header, terrain.serialize()], { type: 'application/octet-stream' });

        const terrRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/terrain-react`, {
            method: 'PUT',
            body: blob,
        });
        if (!terrRes.ok) throw new Error(`Bridge error ${terrRes.status}`);
        set({
            loadedModId: selectedModId,
            loadedMapId: selectedMapId,
            loadedMapInfo: get().selectedMapInfo,
        });
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
        const overrides: Record<string, any> = {
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
        const overrides: Record<string, any> = {
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
        return { spawnEntities: next, selectedEntityIndex: idx >= 0 ? idx : next.length - 1, entitiesVersion: Date.now() };
    }),

    removeEntityAt: (c, r) => set((state) => {
        const next = state.spawnEntities.filter((e) => !(e.position.x === c && e.position.y === r));
        return { spawnEntities: next, selectedEntityIndex: null, entitiesVersion: Date.now() };
    }),

    selectEntityAt: (c, r) => set((state) => {
        const idx = state.spawnEntities.findIndex((e) => e.position.x === c && e.position.y === r);
        return { selectedEntityIndex: idx >= 0 ? idx : null };
    }),

    updateSelectedEntityOverridesJson: (componentName, jsonText) => set((state) => {
        if (state.selectedEntityIndex == null) return state;
        const idx = state.selectedEntityIndex;
        if (idx < 0 || idx >= state.spawnEntities.length) return state;

        let parsed: any;
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

function normalizeMapInfo(raw: any): MapInfo {
    const id = String(raw?.id ?? raw?.Id ?? '');
    const spatialTypeRaw = raw?.spatialType ?? raw?.SpatialType ?? null;
    const spatialType = spatialTypeRaw == null ? null : normalizeSpatialTopology(spatialTypeRaw);
    return {
        id,
        found: Boolean(raw?.found ?? raw?.Found ?? false),
        hasBoards: Boolean(raw?.hasBoards ?? raw?.HasBoards ?? false),
        boardName: stringOrNull(raw?.boardName ?? raw?.BoardName),
        spatialType,
        widthChunks: numberOr(raw?.widthChunks ?? raw?.WidthChunks, 0),
        heightChunks: numberOr(raw?.heightChunks ?? raw?.HeightChunks, 0),
        cellSizeCm: numberOr(raw?.cellSizeCm ?? raw?.CellSizeCm, DEFAULT_BOARD_METRICS.cellSizeCm),
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

function resolveBoardMetricsFromMapConfig(mapCfg: any, mapInfo: MapInfo | null): BoardMetrics {
    const boards = Array.isArray(mapCfg?.Boards) ? mapCfg.Boards : (Array.isArray(mapCfg?.boards) ? mapCfg.boards : []);
    const selectedBoard = pickPrimaryBoard(boards);
    return normalizeBoardMetrics({
        topology: normalizeTopology(
            selectedBoard?.SpatialType ??
            selectedBoard?.spatialType ??
            mapInfo?.spatialType ??
            DEFAULT_BOARD_METRICS.topology),
        cellSizeCm: numberOr(
            selectedBoard?.GridCellSizeCm ??
            selectedBoard?.gridCellSizeCm ??
            mapInfo?.cellSizeCm,
            DEFAULT_BOARD_METRICS.cellSizeCm),
        chunkSizeCells: numberOr(
            selectedBoard?.ChunkSizeCells ??
            selectedBoard?.chunkSizeCells ??
            mapInfo?.chunkSizeCells,
            DEFAULT_BOARD_METRICS.chunkSizeCells),
    });
}

function pickPrimaryBoard(boards: any[]): any | null {
    const navigationDefault = boards.find((b) =>
        isNavigationEnabled(b) && String(b?.Name ?? b?.name ?? '').toLowerCase() === 'default');
    if (navigationDefault) return navigationDefault;

    const navigationBoard = boards.find(isNavigationEnabled);
    if (navigationBoard) return navigationBoard;

    const defaultBoard = boards.find((b) => String(b?.Name ?? b?.name ?? '').toLowerCase() === 'default');
    return defaultBoard ?? boards[0] ?? null;
}

function isNavigationEnabled(board: any): boolean {
    return Boolean(board?.NavigationEnabled ?? board?.navigationEnabled ?? false);
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
