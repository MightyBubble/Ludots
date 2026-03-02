import { create } from 'zustand';
import { TerrainStore } from '../../Core/Map/TerrainStore';
import { hexToWorldCm, worldCmToHex } from '../../Core/Map/HexMetrics';
import type { Camera } from 'three';
import type { NavTile } from '../../Core/NavMesh/NavTileBinary';

export type ToolCategory = 'Height' | 'Water' | 'Biome' | 'Vegetation' | 'Ramp' | 'Layers' | 'Territory' | 'Entities';
export type ToolMode = 'Set' | 'Raise' | 'Lower' | 'Smooth' | 'Bucket'; // Added Bucket

export interface EditorState {
    terrain: TerrainStore;

    bridgeBaseUrl: string;
    mods: Array<{ id: string; name: string; version: string; priority: number }>;
    selectedModId: string | null;
    maps: string[];
    selectedMapId: string | null;
    mapConfig: any | null;
    templates: any[];
    performers: any[];
    selectedTemplateId: string | null;
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
    initMap: (w: number, h: number) => void;
    loadMap: (data: Uint8Array, w: number, h: number) => void;
    refreshMods: () => Promise<void>;
    selectMod: (modId: string) => Promise<void>;
    selectMap: (mapId: string) => void;
    loadSelectedMap: () => Promise<void>;
    saveSelectedMap: () => Promise<void>;
    selectTemplate: (templateId: string | null) => void;
    placeEntityAt: (c: number, r: number) => void;
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

    bridgeBaseUrl: 'http://localhost:5299',
    mods: [],
    selectedModId: null,
    maps: [],
    selectedMapId: null,
    mapConfig: null,
    templates: [],
    performers: [],
    selectedTemplateId: null,
    spawnEntities: [],
    selectedEntityIndex: null,
    entitiesVersion: 0,
    
    activeCategory: 'Height',
    activeMode: 'Raise',
    brushSize: 1,
    brushValue: 1,
    activeLayer: 'Snow', // Default layer

    showGrid: false,
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

    initMap: (w, h) => set({ 
        terrain: new TerrainStore(w, h), 
        minimapDirtyChunks: new Set(),
        navDirtyChunks: new Set(),
        loadingState: { isLoading: true, message: 'Initializing Map...', progress: 0 }
    }),
    loadMap: (data, w, h) => {
        const newTerrain = new TerrainStore(w, h);
        newTerrain.loadFromBytes(w, h, data);
        // Mark all as dirty for minimap
        const allChunks = new Set<string>();
        for(let y=0; y<h; y++) for(let x=0; x<w; x++) allChunks.add(`${x},${y}`);
        
        set({ 
            terrain: newTerrain, 
            minimapDirtyChunks: allChunks,
            navDirtyChunks: new Set(),
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
            selectedMapId: null,
            mapConfig: null,
            templates: [],
            performers: [],
            selectedTemplateId: null,
            spawnEntities: [],
            selectedEntityIndex: null,
            entitiesVersion: Date.now(),
            navDirtyChunks: new Set(),
        });
        const res = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/maps`);
        if (!res.ok) throw new Error(`Bridge error ${res.status}`);
        const json = await res.json();
        const maps = (json.maps ?? []).map((x: any) => String(x));
        const tRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/entity-templates`);
        if (!tRes.ok) throw new Error(`Bridge error ${tRes.status}`);
        const tJson = await tRes.json();
        const templates = tJson.templates ?? [];

        const pRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(modId)}/performers`);
        if (!pRes.ok) throw new Error(`Bridge error ${pRes.status}`);
        const pJson = await pRes.json();
        const performers = pJson.performers ?? [];

        const defaultTemplateId = templates.length > 0 ? String(templates[0]?.Id ?? templates[0]?.id ?? '') : null;

        set({
            maps,
            selectedMapId: maps.length > 0 ? maps[0] : null,
            templates,
            performers,
            selectedTemplateId: defaultTemplateId && defaultTemplateId.length > 0 ? defaultTemplateId : null,
        });
    },

    selectMap: (mapId: string) => set({ selectedMapId: mapId }),

    loadSelectedMap: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, loadMap, setLoading } = get();
        if (!selectedModId || !selectedMapId) return;
        setLoading(true, 'Loading MapConfig...', 10);
        const mapRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}`);
        if (!mapRes.ok) throw new Error(`Bridge error ${mapRes.status}`);
        const mapJson = await mapRes.json();
        const mapCfg = mapJson.map ?? null;
        const entities = Array.isArray(mapCfg?.Entities) ? mapCfg.Entities : (Array.isArray(mapCfg?.entities) ? mapCfg.entities : []);
        const spawnEntities = entities.map((e: any) => {
            const template = String(e.Template ?? e.template ?? '');
            const overrides = (e.Overrides ?? e.overrides ?? {}) as Record<string, any>;
            const wpcm = overrides?.WorldPositionCm?.Value ?? overrides?.worldPositionCm?.value;
            let posX: number, posY: number;
            if (wpcm && (wpcm.X !== undefined || wpcm.Y !== undefined)) {
                const hex = worldCmToHex(Number(wpcm.X ?? 0), Number(wpcm.Y ?? 0));
                posX = hex.col;
                posY = hex.row;
            } else {
                posX = Number(e.Position?.X ?? e.position?.x ?? 0);
                posY = Number(e.Position?.Y ?? e.position?.y ?? 0);
            }
            return { template, position: { x: posX, y: posY }, overrides };
        });

        set({ mapConfig: mapCfg, spawnEntities, selectedEntityIndex: null, entitiesVersion: Date.now() });

        setLoading(true, 'Loading Terrain...', 40);
        const terrRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/terrain-react`);
        if (!terrRes.ok) throw new Error(`Bridge error ${terrRes.status}`);
        const buf = await terrRes.arrayBuffer();
        const view = new DataView(buf);
        const w = view.getInt32(0, true);
        const h = view.getInt32(4, true);
        const stride = view.getUint8(8);
        if (stride !== 2) throw new Error(`Invalid terrain stride ${stride}`);
        const data = new Uint8Array(buf.slice(9));
        loadMap(data, w, h);
        setLoading(false);
    },

    saveSelectedMap: async () => {
        const { bridgeBaseUrl, selectedModId, selectedMapId, mapConfig, terrain, setLoading, spawnEntities } = get();
        if (!selectedModId || !selectedMapId) return;
        if (!mapConfig) throw new Error('No MapConfig loaded.');

        setLoading(true, 'Saving MapConfig...', 20);
        mapConfig.Id = selectedMapId;
        mapConfig.Entities = spawnEntities.map((e) => {
            const cm = hexToWorldCm(e.position.x, e.position.y);
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
        view.setUint8(8, 2);
        const blob = new Blob([header, terrain.serialize()], { type: 'application/octet-stream' });

        const terrRes = await fetch(`${bridgeBaseUrl}/api/mods/${encodeURIComponent(selectedModId)}/maps/${encodeURIComponent(selectedMapId)}/terrain-react`, {
            method: 'PUT',
            body: blob,
        });
        if (!terrRes.ok) throw new Error(`Bridge error ${terrRes.status}`);
        setLoading(false);
    },

    selectTemplate: (templateId) => set({ selectedTemplateId: templateId }),

    placeEntityAt: (c, r) => set((state) => {
        if (!state.selectedTemplateId) return state;
        const next = state.spawnEntities.slice();
        const idx = next.findIndex((e) => e.position.x === c && e.position.y === r);
        const cm = hexToWorldCm(c, r);
        const overrides: Record<string, any> = {
            WorldPositionCm: { Value: { X: cm.xCm, Y: cm.yCm } },
        };
        const entity = { template: state.selectedTemplateId, position: { x: c, y: r }, overrides };
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
