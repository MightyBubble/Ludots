import React, { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three-stdlib';
import { useEditorStore, type BakedNavTileVisual, type EntityTemplatePayload, type JsonRecord } from './EditorStore';
import { ChunkRenderer } from '../../Core/Render/ChunkRenderer';
import {
    cellToWorldPosition,
    getBrushVisualRadius,
    getMapWorldSizeM,
    getTopologyNeighbors,
    worldPointToCell,
} from '../../Core/Map/TopologyMetrics';
import type { NavTile } from '../../Core/NavMesh/NavTileBinary';

const FULL_RENDER_CHUNK_LIMIT = 64;
const MEDIUM_RENDER_CHUNK_LIMIT = 1024;
const INITIAL_RENDER_FRAME_BUDGET_MS = 12;
type MouseAction = (typeof THREE.MOUSE)[keyof typeof THREE.MOUSE];
const DISABLED_MOUSE_BUTTON = -1 as MouseAction;

type RenderableObject = THREE.Object3D & {
    geometry?: { dispose?: () => void };
    material?: THREE.Material | THREE.Material[];
};

function asRecord(value: unknown): JsonRecord | null {
    return value != null && typeof value === 'object' && !Array.isArray(value)
        ? value as JsonRecord
        : null;
}

const disposeThreeObject = (object: THREE.Object3D) => {
    object.traverse((child) => {
        const renderable = child as RenderableObject;
        renderable.geometry?.dispose?.();
        const material = renderable.material;
        if (Array.isArray(material)) {
            for (const mat of material) mat?.dispose?.();
        } else {
            material?.dispose?.();
        }
    });
};

export const HexRenderer: React.FC = () => {
    const containerRef = useRef<HTMLDivElement>(null);
    const { terrain, boardMetrics, activeCategory, activeMode, brushSize, brushValue, activeLayer, showGrid, showChunkBorders, showNavMesh, bakedNavTiles, bakedNavTilesVersion, navSimulation, navSimulationVersion, navPanelTab, navQueryProfileId, navQueryLayer, navQueryStartCell, navQueryGoalCell, navigationConfig, navigationConfigVersion, selectedModId, selectedMapId, selectedBoardName, loadedModId, loadedMapId, loadedBoardName, loadedBoardInfo, canvasSessionKind, canvasSessionLabel, setNavQueryStartCell, setNavQueryGoalCell, registerCamera, reportDirtyChunks, reportMinimapDirtyChunks, setLoading, placeEntityAt, placeObstacleAt, removeEntityAt, selectEntityAt, templates, spawnEntities, selectedEntityIndex, entitiesVersion } = useEditorStore();
    const canvasHasRepoSession = Boolean(canvasSessionKind === 'repo' && loadedModId && loadedMapId && loadedBoardName);
    const canvasHasVisibleSession = canvasSessionKind === 'local' || canvasHasRepoSession;
    const canvasMapLoaded = Boolean(canvasHasRepoSession && selectedModId && selectedMapId && selectedBoardName && loadedModId === selectedModId && loadedMapId === selectedMapId && loadedBoardName === selectedBoardName);
    const canvasEditable = Boolean(canvasSessionKind === 'local' || (canvasMapLoaded && loadedBoardInfo?.canEditTerrain));
    const canvasCanSim = canvasMapLoaded;
    const canvasInputLocked = navPanelTab === 'simulation' ? !canvasCanSim : !canvasEditable;
    const canvasLockTitle = canvasSessionKind === 'local' && navPanelTab === 'simulation'
        ? 'Simulation needs an opened repo board'
        : canvasHasRepoSession && !canvasMapLoaded
            ? 'Selected board is not open'
            : canvasSessionKind === 'empty'
                ? 'No board open'
                : 'Canvas is read-only';
    const canvasLockMessage = canvasSessionKind === 'local' && navPanelTab === 'simulation'
        ? `${canvasSessionLabel ?? 'Local draft'} can be edited and exported. Open a repo board to run C# nav simulation.`
        : canvasHasRepoSession && !canvasMapLoaded
            ? `Canvas still contains ${loadedMapId}/${loadedBoardName}. Open ${selectedMapId ?? 'a map'}/${selectedBoardName ?? 'a board'} from Map And Board before editing or simulating.`
            : canvasSessionKind === 'empty'
                ? 'Select a map and board, then open it from Map And Board before editing.'
                : 'This board is loaded for viewing, but terrain edits are disabled by its board metadata.';
    const visibleBakedNavTiles = React.useMemo(() => {
        const filtered = new Map<string, BakedNavTileVisual>();
        bakedNavTiles.forEach((visual, key) => {
            if (visual.profileId === null) {
                filtered.set(key, visual);
                return;
            }
            if (visual.profileId === navQueryProfileId && visual.layer === navQueryLayer) {
                filtered.set(key, visual);
            }
        });
        return filtered;
    }, [bakedNavTiles, bakedNavTilesVersion, navQueryProfileId, navQueryLayer]);
    const selectedAgentRadiusCm = React.useMemo(() => {
        const profiles = Array.isArray(navigationConfig?.agentProfiles)
            ? navigationConfig.agentProfiles.filter((p): p is JsonRecord => p != null && typeof p === 'object' && !Array.isArray(p))
            : [];
        const profile = profiles.find((p, i: number) => String(p?.id ?? p?.Id ?? `profile_${i}`) === navQueryProfileId);
        return Number(profile?.radiusCm ?? profile?.RadiusCm ?? profile?.bodyRadiusCm ?? 0);
    }, [navigationConfig, navigationConfigVersion, navQueryProfileId]);
    
    // Refs for mutable state in animation loop
    const sceneRef = useRef<THREE.Scene | null>(null);
    const rendererRef = useRef<THREE.WebGLRenderer | null>(null);
    const chunksRef = useRef<Map<string, THREE.Group>>(new Map());
    const navMeshRef = useRef<THREE.Group | null>(null);
    const cameraRef = useRef<THREE.PerspectiveCamera | null>(null);
    const controlsRef = useRef<OrbitControls | null>(null);
    const chunkRendererRef = useRef<ChunkRenderer | null>(null);
    const terrainLodRef = useRef<THREE.Group | null>(null);
    const terrainGroupRef = useRef<THREE.Group | null>(null);
    const cursorMeshRef = useRef<THREE.Mesh | null>(null);
    const entityGroupRef = useRef<THREE.Group | null>(null);
    const navQueryPointGroupRef = useRef<THREE.Group | null>(null);
    const raycasterRef = useRef(new THREE.Raycaster());
    const mouseRef = useRef(new THREE.Vector2());
    const inputPlaneRef = useRef<THREE.Mesh | null>(null);
    const rafRef = useRef<number | null>(null);
    
    // We need to store current terrain in a ref for the animation loop
    // because the animation loop closure captures the initial terrain instance.
    const terrainRef = useRef(terrain);
    const boardMetricsRef = useRef(boardMetrics);
    const viewStateRef = useRef({
        showGrid,
        showChunkBorders,
        showNavMesh,
        hasBakedNavTiles: bakedNavTiles.size > 0,
    });
    // Track initialization progress
    const totalInitChunksRef = useRef(0);
    const renderDirtyChunksRef = useRef<Set<string>>(new Set());
    const renderedWindowKeyRef = useRef<string | null>(null);
    const canvasHasVisibleSessionRef = useRef(canvasHasVisibleSession);

    const getChunkCount = (currentTerrain = terrainRef.current) => currentTerrain.widthChunks * currentTerrain.heightChunks;

    const isFullRenderBoard = (currentTerrain = terrainRef.current) => getChunkCount(currentTerrain) <= FULL_RENDER_CHUNK_LIMIT;

    const getVisibleChunkRadius = (currentTerrain = terrainRef.current) => {
        const chunkCount = getChunkCount(currentTerrain);
        if (chunkCount <= FULL_RENDER_CHUNK_LIMIT) return Math.max(currentTerrain.widthChunks, currentTerrain.heightChunks);
        if (chunkCount <= MEDIUM_RENDER_CHUNK_LIMIT) return 3;
        return 2;
    };

    const getLodColor = (height: number, water: number, biome: number) => {
        const color = new THREE.Color();
        if (water > height) {
            color.setRGB(0.05, Math.min(0.8, 0.35 + water * 0.03), Math.min(1.0, 0.65 + water * 0.025));
            return color;
        }

        switch (biome) {
            case 1: color.setHex(0x9f7a3d); break;
            case 2: color.setHex(0x6f7378); break;
            case 3: color.setHex(0x3d6c2e); break;
            case 4: color.setHex(0x5a5d5a); break;
            case 5: color.setHex(0x435322); break;
            default: color.setHex(0x8b4f24); break;
        }

        const hsl = { h: 0, s: 0, l: 0 };
        color.getHSL(hsl);
        color.setHSL(hsl.h, hsl.s, Math.min(0.76, hsl.l + height * 0.018));
        return color;
    };

    const isValidChunk = (cx: number, cy: number, currentTerrain = terrainRef.current) =>
        Number.isInteger(cx) &&
        Number.isInteger(cy) &&
        cx >= 0 &&
        cy >= 0 &&
        cx < currentTerrain.widthChunks &&
        cy < currentTerrain.heightChunks;

    const enqueueRenderChunk = (cx: number, cy: number, currentTerrain = terrainRef.current) => {
        if (!isValidChunk(cx, cy, currentTerrain)) return;
        renderDirtyChunksRef.current.add(`${cx},${cy}`);
    };

    const getCameraCenterChunk = (currentTerrain = terrainRef.current) => {
        const metrics = boardMetricsRef.current;
        const target = controlsRef.current?.target ?? cameraRef.current?.position ?? new THREE.Vector3(0, 0, 0);
        const cell = worldPointToCell(target.x, target.z, metrics);
        const chunkSize = metrics.chunkSizeCells;
        return {
            cx: Math.max(0, Math.min(currentTerrain.widthChunks - 1, Math.floor(cell.col / chunkSize))),
            cy: Math.max(0, Math.min(currentTerrain.heightChunks - 1, Math.floor(cell.row / chunkSize))),
        };
    };

    const evictChunksOutsideWindow = (center: { cx: number; cy: number }, retainRadius: number) => {
        const group = terrainGroupRef.current;
        if (!group) return;
        const minX = center.cx - retainRadius;
        const maxX = center.cx + retainRadius;
        const minY = center.cy - retainRadius;
        const maxY = center.cy + retainRadius;

        for (const [key, chunk] of chunksRef.current) {
            const [cx, cy] = key.split(',').map(Number);
            if (cx >= minX && cx <= maxX && cy >= minY && cy <= maxY) continue;
            group.remove(chunk);
            disposeThreeObject(chunk);
            chunksRef.current.delete(key);
            renderDirtyChunksRef.current.delete(key);
        }
    };

    const enqueueVisibleRenderWindow = (force: boolean) => {
        const currentTerrain = terrainRef.current;
        if (currentTerrain.widthChunks <= 0 || currentTerrain.heightChunks <= 0) return;

        if (isFullRenderBoard(currentTerrain)) {
            const windowKey = `full:${currentTerrain.widthChunks}x${currentTerrain.heightChunks}:${boardMetricsRef.current.topology}:${boardMetricsRef.current.cellSizeCm}`;
            if (!force && renderedWindowKeyRef.current === windowKey) return;
            renderedWindowKeyRef.current = windowKey;
            for (let cy = 0; cy < currentTerrain.heightChunks; cy++) {
                for (let cx = 0; cx < currentTerrain.widthChunks; cx++) {
                    enqueueRenderChunk(cx, cy, currentTerrain);
                }
            }
            return;
        }

        const center = getCameraCenterChunk(currentTerrain);
        const radius = getVisibleChunkRadius(currentTerrain);
        const windowKey = `${center.cx},${center.cy},r${radius}:${currentTerrain.widthChunks}x${currentTerrain.heightChunks}:${boardMetricsRef.current.topology}:${boardMetricsRef.current.cellSizeCm}`;
        if (!force && renderedWindowKeyRef.current === windowKey) return;
        renderedWindowKeyRef.current = windowKey;

        for (let cy = center.cy - radius; cy <= center.cy + radius; cy++) {
            for (let cx = center.cx - radius; cx <= center.cx + radius; cx++) {
                enqueueRenderChunk(cx, cy, currentTerrain);
            }
        }

        evictChunksOutsideWindow(center, radius + 2);
    };

    const moveTerrainDirtyChunksToRenderQueue = (currentTerrain: typeof terrainRef.current) => {
        if (currentTerrain.dirtyChunks.size === 0) return;
        const authoredKeys = Array.from(currentTerrain.dirtyChunks.values());
        currentTerrain.dirtyChunks.clear();
        for (const key of authoredKeys) {
            const [cx, cy] = key.split(',').map(Number);
            enqueueRenderChunk(cx, cy, currentTerrain);
        }
        rebuildTerrainLod();
    };

    const rebuildTerrainLod = () => {
        const group = terrainLodRef.current;
        if (!group) return;
        group.children.forEach(disposeThreeObject);
        group.clear();

        const currentTerrain = terrainRef.current;
        if (!canvasHasVisibleSessionRef.current || isFullRenderBoard(currentTerrain)) return;

        const metrics = boardMetricsRef.current;
        const chunkSize = metrics.chunkSizeCells;
        const worldSize = getMapWorldSizeM(currentTerrain.widthChunks, currentTerrain.heightChunks, metrics);
        const chunkWorldW = worldSize.width / currentTerrain.widthChunks;
        const chunkWorldH = worldSize.height / currentTerrain.heightChunks;
        const positions: number[] = [];
        const colors: number[] = [];
        const indices: number[] = [];
        const lines: number[] = [];
        const lodCornerHeights = new Map<string, number>();

        const pushVertex = (x: number, y: number, z: number, color: THREE.Color) => {
            const index = positions.length / 3;
            positions.push(x, y, z);
            colors.push(color.r, color.g, color.b);
            return index;
        };

        const sampleGridLodCornerHeight = (cornerC: number, cornerR: number) => {
            const key = `${cornerC},${cornerR}`;
            const cached = lodCornerHeights.get(key);
            if (cached !== undefined) return cached;

            const maxC = currentTerrain.widthChunks * chunkSize;
            const maxR = currentTerrain.heightChunks * chunkSize;
            let sum = 0;
            let count = 0;
            for (let dr = -1; dr <= 0; dr++) {
                for (let dc = -1; dc <= 0; dc++) {
                    const c = cornerC + dc;
                    const r = cornerR + dr;
                    if (c < 0 || r < 0 || c >= maxC || r >= maxR) continue;
                    sum += currentTerrain.getHeight(c, r);
                    count++;
                }
            }

            if (count === 0) {
                throw new Error(`Grid LOD corner (${cornerC},${cornerR}) has no owning cells.`);
            }

            const height = sum / count;
            lodCornerHeights.set(key, height);
            return height;
        };

        for (let cy = 0; cy < currentTerrain.heightChunks; cy++) {
            for (let cx = 0; cx < currentTerrain.widthChunks; cx++) {
                const sampleC = Math.min(currentTerrain.widthChunks * chunkSize - 1, cx * chunkSize + Math.floor(chunkSize / 2));
                const sampleR = Math.min(currentTerrain.heightChunks * chunkSize - 1, cy * chunkSize + Math.floor(chunkSize / 2));
                const height = currentTerrain.getHeight(sampleC, sampleR);
                const water = currentTerrain.getWater(sampleC, sampleR);
                const biome = currentTerrain.getBiome(sampleC, sampleR);
                const color = getLodColor(height, water, biome);
                const x0 = cx * chunkWorldW;
                const x1 = (cx + 1) * chunkWorldW;
                const z0 = cy * chunkWorldH;
                const z1 = (cy + 1) * chunkWorldH;
                const flatY = Math.max(height, water) * 2.0 - 0.18;
                const c0 = cx * chunkSize;
                const c1 = Math.min((cx + 1) * chunkSize, currentTerrain.widthChunks * chunkSize);
                const r0 = cy * chunkSize;
                const r1 = Math.min((cy + 1) * chunkSize, currentTerrain.heightChunks * chunkSize);
                const y00 = metrics.topology === 'Grid' ? sampleGridLodCornerHeight(c0, r0) * 2.0 - 0.18 : flatY;
                const y10 = metrics.topology === 'Grid' ? sampleGridLodCornerHeight(c1, r0) * 2.0 - 0.18 : flatY;
                const y11 = metrics.topology === 'Grid' ? sampleGridLodCornerHeight(c1, r1) * 2.0 - 0.18 : flatY;
                const y01 = metrics.topology === 'Grid' ? sampleGridLodCornerHeight(c0, r1) * 2.0 - 0.18 : flatY;

                const i0 = pushVertex(x0, y00, z0, color);
                const i1 = pushVertex(x1, y10, z0, color);
                const i2 = pushVertex(x1, y11, z1, color);
                const i3 = pushVertex(x0, y01, z1, color);
                indices.push(i0, i1, i2, i0, i2, i3);

                lines.push(
                    x0, y00 + 0.04, z0, x1, y10 + 0.04, z0,
                    x1, y10 + 0.04, z0, x1, y11 + 0.04, z1,
                    x1, y11 + 0.04, z1, x0, y01 + 0.04, z1,
                    x0, y01 + 0.04, z1, x0, y00 + 0.04, z0);
            }
        }

        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
        geometry.setIndex(indices);
        geometry.computeVertexNormals();
        const material = new THREE.MeshBasicMaterial({
            vertexColors: true,
            transparent: true,
            opacity: 0.72,
            side: THREE.DoubleSide,
            depthWrite: false,
        });
        const mesh = new THREE.Mesh(geometry, material);
        mesh.name = 'ChunkLodTerrain';
        mesh.renderOrder = -20;
        group.add(mesh);

        const lineGeometry = new THREE.BufferGeometry();
        lineGeometry.setAttribute('position', new THREE.Float32BufferAttribute(lines, 3));
        const lineMaterial = new THREE.LineBasicMaterial({
            color: 0x00e5ff,
            transparent: true,
            opacity: showChunkBorders ? 0.42 : 0.0,
            depthWrite: false,
        });
        const lineMesh = new THREE.LineSegments(lineGeometry, lineMaterial);
        lineMesh.name = 'ChunkLodGrid';
        lineMesh.renderOrder = -10;
        lineMesh.visible = showChunkBorders;
        group.add(lineMesh);
    };

    useEffect(() => {
        terrainRef.current = terrain;
    }, [terrain, setLoading]);

    useEffect(() => {
        canvasHasVisibleSessionRef.current = canvasHasVisibleSession;
    }, [canvasHasVisibleSession]);

    useEffect(() => {
        boardMetricsRef.current = boardMetrics;
    }, [boardMetrics]);

    useEffect(() => {
        viewStateRef.current = {
            showGrid,
            showChunkBorders,
            showNavMesh,
            hasBakedNavTiles: bakedNavTiles.size > 0,
        };
        if (terrainGroupRef.current) {
            applyChunkLayerVisibility(terrainGroupRef.current);
        }
        if (terrainLodRef.current) {
            const lodGrid = terrainLodRef.current.getObjectByName('ChunkLodGrid');
            if (lodGrid) lodGrid.visible = showChunkBorders;
        }
    }, [showGrid, showChunkBorders, showNavMesh, bakedNavTiles.size]);

    useEffect(() => {
        const controls = controlsRef.current;
        if (!controls) return;
        controls.mouseButtons.LEFT = DISABLED_MOUSE_BUTTON;
        controls.mouseButtons.RIGHT = navPanelTab === 'simulation'
            ? DISABLED_MOUSE_BUTTON
            : THREE.MOUSE.ROTATE;
    }, [navPanelTab]);

    // Interaction State
    const isDraggingRef = useRef(false);
    const lastDragCellRef = useRef<{c: number, r: number} | null>(null);

    // Initial Setup
    useEffect(() => {
        if (!containerRef.current) return;

        // 1. Scene
        const scene = new THREE.Scene();
        scene.background = new THREE.Color(0x222222);
        sceneRef.current = scene;

        // 2. Camera
        const width = containerRef.current.clientWidth;
        const height = containerRef.current.clientHeight;
        const camera = new THREE.PerspectiveCamera(60, width / height, 0.1, 10000);
        camera.position.set(0, 100, 100);
        camera.lookAt(0, 0, 0);
        cameraRef.current = camera;

        // 3. Renderer
        const renderer = new THREE.WebGLRenderer({ 
            antialias: true,
            logarithmicDepthBuffer: true // Fix Z-fighting for huge scenes
        });
        renderer.setSize(width, height);
        containerRef.current.appendChild(renderer.domElement);
        renderer.domElement.style.visibility = canvasHasVisibleSession ? 'visible' : 'hidden';
        rendererRef.current = renderer;

        // 4. Lights
        const ambientLight = new THREE.AmbientLight(0xffffff, 0.8);
        scene.add(ambientLight);
        const dirLight = new THREE.DirectionalLight(0xffffff, 1.0);
        dirLight.position.set(50, 200, 100);
        scene.add(dirLight);

        // 5. Controls
        const controls = new OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.dampingFactor = 0.1; // Snappier
        controls.screenSpacePanning = false; // RTS Style: Pan on XZ plane
        controls.minDistance = 5;
        controls.maxDistance = 800;
        controls.maxPolarAngle = Math.PI / 2 - 0.1; // Don't go below ground
        controls.zoomSpeed = 1.2;
        controls.mouseButtons = {
            LEFT: DISABLED_MOUSE_BUTTON,
            MIDDLE: THREE.MOUSE.PAN,
            RIGHT: useEditorStore.getState().navPanelTab === 'simulation'
                ? DISABLED_MOUSE_BUTTON
                : THREE.MOUSE.ROTATE
        };
        controlsRef.current = controls;

        // Register Camera to Store (for Minimap)
        registerCamera(camera, controls);

        // 6. Input Plane (Invisible plane for raycasting)
        const planeGeo = new THREE.PlaneGeometry(1000, 1000);
        const planeMat = new THREE.MeshBasicMaterial({ visible: false });
        const inputPlane = new THREE.Mesh(planeGeo, planeMat);
        inputPlane.rotation.x = -Math.PI / 2;
        scene.add(inputPlane);
        inputPlaneRef.current = inputPlane;

        // 7. Cursor
        const cursorGeo = new THREE.RingGeometry(2.0, 2.5, 32);
        cursorGeo.rotateX(-Math.PI / 2);
        const cursorMat = new THREE.MeshBasicMaterial({ 
            color: 0xff00ff, 
            transparent: true, 
            opacity: 0.8,
            side: THREE.DoubleSide,
            depthTest: false 
        });
        const cursorMesh = new THREE.Mesh(cursorGeo, cursorMat);
        cursorMesh.renderOrder = 999;
        cursorMesh.visible = false;
        scene.add(cursorMesh);
        cursorMeshRef.current = cursorMesh;

        // 8. Terrain Groups
        const terrainLodGroup = new THREE.Group();
        terrainLodGroup.name = "terrainLodGroup";
        scene.add(terrainLodGroup);
        terrainLodRef.current = terrainLodGroup;

        const terrainGroup = new THREE.Group();
        terrainGroup.name = "terrainGroup";
        scene.add(terrainGroup);
        terrainGroupRef.current = terrainGroup;

        const navMeshGroup = new THREE.Group();
        navMeshGroup.name = "bakedNavMeshGroup";
        scene.add(navMeshGroup);
        navMeshRef.current = navMeshGroup;

        const entityGroup = new THREE.Group();
        entityGroup.name = "entityGroup";
        scene.add(entityGroup);
        entityGroupRef.current = entityGroup;

        const navQueryPointGroup = new THREE.Group();
        navQueryPointGroup.name = "navQueryPointGroup";
        scene.add(navQueryPointGroup);
        navQueryPointGroupRef.current = navQueryPointGroup;

        // 9. Resize Handler
        const handleResize = () => {
            if (!containerRef.current || !cameraRef.current || !rendererRef.current) return;
            const w = containerRef.current.clientWidth;
            const h = containerRef.current.clientHeight;
            cameraRef.current.aspect = w / h;
            cameraRef.current.updateProjectionMatrix();
            rendererRef.current.setSize(w, h);
        };
        window.addEventListener('resize', handleResize);

        // 10. Animation Loop
        const animate = () => {
            rafRef.current = requestAnimationFrame(animate);
            if (controlsRef.current) controlsRef.current.update();
            if (rendererRef.current && sceneRef.current && cameraRef.current) {
                rendererRef.current.render(sceneRef.current, cameraRef.current);
            }
            updateDirtyChunks();
        };
        rafRef.current = requestAnimationFrame(animate);

        return () => {
            window.removeEventListener('resize', handleResize);
            if (rafRef.current) cancelAnimationFrame(rafRef.current);
            if (containerRef.current && rendererRef.current) {
                containerRef.current.removeChild(rendererRef.current.domElement);
            }
            chunksRef.current.forEach(disposeThreeObject);
            chunksRef.current.clear();
            if (terrainLodRef.current) {
                terrainLodRef.current.children.forEach(disposeThreeObject);
                terrainLodRef.current.clear();
            }
            renderer.dispose();
            // Unregister? Maybe not needed as ref will be overwritten or component unmounts
        };
    }, []); // Only run once for setup? No, if terrain changes we need to re-bind? 
    // Actually we want setup once. Data sync is in next useEffect.

    // Sync Terrain Data to 3D
    useEffect(() => {
        if (!terrainGroupRef.current) return;
        
        // Clear old meshes
        chunksRef.current.forEach(disposeThreeObject);
        chunksRef.current.clear();
        if (terrainLodRef.current) {
            terrainLodRef.current.children.forEach(disposeThreeObject);
            terrainLodRef.current.clear();
        }
        terrainGroupRef.current.clear();
        renderDirtyChunksRef.current.clear();
        renderedWindowKeyRef.current = null;
        totalInitChunksRef.current = 0;
        terrain.clearDirty();

        if (!canvasHasVisibleSession) {
            setLoading(false);
            return;
        }
        
        chunkRendererRef.current = new ChunkRenderer(terrain, boardMetrics);
        rebuildTerrainLod();
        
        enqueueVisibleRenderWindow(true);
        const initialChunkCount = renderDirtyChunksRef.current.size;
        totalInitChunksRef.current = initialChunkCount;
        if (initialChunkCount > 0) {
            setLoading(true, `Generating visible terrain... 0%`, 0);
        } else {
            setLoading(false);
        }
        updateDirtyChunks();
        
        // Update Input Plane Size/Pos
        if (inputPlaneRef.current) {
            const worldSize = getMapWorldSizeM(terrain.widthChunks, terrain.heightChunks, boardMetrics);
            const totalW = worldSize.width;
            const totalH = worldSize.height;
            inputPlaneRef.current.scale.set(totalW, totalH, 1);
            inputPlaneRef.current.position.set(totalW/2, 0, totalH/2);
            inputPlaneRef.current.updateMatrixWorld();
        }

    }, [terrain, boardMetrics, canvasHasVisibleSession]); // Re-run when terrain, topology, or session visibility changes

    useEffect(() => {
        if (terrainLodRef.current) terrainLodRef.current.visible = canvasHasVisibleSession && !isFullRenderBoard();
        if (terrainGroupRef.current) terrainGroupRef.current.visible = canvasHasVisibleSession;
        if (entityGroupRef.current) entityGroupRef.current.visible = canvasHasVisibleSession;
        if (inputPlaneRef.current) inputPlaneRef.current.visible = canvasHasVisibleSession;
        if (navQueryPointGroupRef.current) navQueryPointGroupRef.current.visible = canvasCanSim;
        if (cursorMeshRef.current && !canvasHasVisibleSession) cursorMeshRef.current.visible = false;
        if (controlsRef.current) controlsRef.current.enabled = canvasHasVisibleSession;
        if (rendererRef.current) rendererRef.current.domElement.style.visibility = canvasHasVisibleSession ? 'visible' : 'hidden';
    }, [canvasHasVisibleSession, canvasCanSim]);

    useEffect(() => {
        if (!navMeshRef.current) return;
        navMeshRef.current.visible = showNavMesh && canvasCanSim && visibleBakedNavTiles.size > 0;
    }, [showNavMesh, canvasCanSim, visibleBakedNavTiles.size]);

    useEffect(() => {
        if (!navMeshRef.current) return;
        if (!showNavMesh || !canvasCanSim || visibleBakedNavTiles.size === 0) {
            navMeshRef.current.clear();
            return;
        }
        navMeshRef.current.clear();
        navMeshRef.current.add(buildBakedNavMeshGroup(visibleBakedNavTiles, navSimulation, selectedAgentRadiusCm));
    }, [showNavMesh, canvasCanSim, bakedNavTilesVersion, navSimulationVersion, navQueryProfileId, navQueryLayer, selectedAgentRadiusCm]);

    useEffect(() => {
        if (!entityGroupRef.current) return;
        const group = entityGroupRef.current;
        group.clear();

        const templatesById = new Map<string, EntityTemplatePayload>();
        for (let i = 0; i < templates.length; i++) {
            const t = templates[i];
            const id = String(t?.Id ?? t?.id ?? '');
            if (id) templatesById.set(id, t);
        }

        const cubeGeo = new THREE.BoxGeometry(2, 2, 2);
        const sphereGeo = new THREE.SphereGeometry(1.25, 12, 10);

        const getColor = (id: string) => {
            let h = 0;
            for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) | 0;
            const r = (h & 0xff) / 255;
            const g = ((h >> 8) & 0xff) / 255;
            const b = ((h >> 16) & 0xff) / 255;
            return new THREE.Color(0.2 + 0.8 * Math.abs(r), 0.2 + 0.8 * Math.abs(g), 0.2 + 0.8 * Math.abs(b));
        };

        for (let i = 0; i < spawnEntities.length; i++) {
            const e = spawnEntities[i];
            const t = templatesById.get(e.template);
            const components = asRecord(t?.components) ?? asRecord(t?.Components) ?? {};
            const visual = asRecord(e.overrides?.VisualModel) ?? asRecord(components?.VisualModel) ?? asRecord(components?.visualModel);
            const meshId = Number(visual?.MeshId ?? visual?.meshId ?? 0);

            const geo = meshId === 2 ? sphereGeo : cubeGeo;
            const mat = new THREE.MeshStandardMaterial({ color: getColor(e.template) });

            const m = new THREE.Mesh(geo, mat);
            const h = terrain.getHeight(e.position.x, e.position.y);
            const pos = cellToWorldPosition(e.position.x, e.position.y, h, boardMetrics, 2.0);
            m.position.set(pos.x, pos.y + 1.0, pos.z);
            m.renderOrder = 10;

            if (selectedEntityIndex === i) {
                (m.material as THREE.MeshStandardMaterial).emissive = new THREE.Color(0.3, 0.2, 0.8);
                (m.material as THREE.MeshStandardMaterial).emissiveIntensity = 1.0;
            }

            const bindings = asRecord(e.overrides?.PerformerBindings) ?? asRecord(e.overrides?.performerBindings);
            const ids = bindings?.Ids ?? bindings?.ids ?? bindings?.DefinitionIds ?? bindings?.definitionIds ?? null;
            if (Array.isArray(ids) && ids.length > 0) {
                const sprite = buildTextSprite(String(ids[0]));
                sprite.position.set(0, 3.0, 0);
                m.add(sprite);
            }

            group.add(m);
        }
    }, [entitiesVersion, terrain, boardMetrics, templates, spawnEntities, selectedEntityIndex]);

    useEffect(() => {
        if (!navQueryPointGroupRef.current) return;
        const group = navQueryPointGroupRef.current;
        group.clear();
        if (!canvasCanSim) return;

        group.add(buildNavQueryPointMarker(navQueryStartCell.col, navQueryStartCell.row, 'S', 0x38bdf8, selectedAgentRadiusCm));
        group.add(buildNavQueryPointMarker(navQueryGoalCell.col, navQueryGoalCell.row, 'G', 0x34d399, selectedAgentRadiusCm));
    }, [navQueryStartCell, navQueryGoalCell, terrain, boardMetrics, selectedAgentRadiusCm, canvasCanSim]);

    const buildTextSprite = (text: string) => {
        const canvas = document.createElement('canvas');
        canvas.width = 256;
        canvas.height = 128;
        const ctx = canvas.getContext('2d');
        if (ctx) {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            ctx.fillStyle = 'rgba(0,0,0,0.6)';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.fillStyle = '#ffffff';
            ctx.font = '48px sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(text, canvas.width / 2, canvas.height / 2);
        }
        const tex = new THREE.CanvasTexture(canvas);
        const mat = new THREE.SpriteMaterial({ map: tex, transparent: true, depthTest: false });
        const sp = new THREE.Sprite(mat);
        sp.scale.set(8, 4, 1);
        sp.renderOrder = 999;
        return sp;
    };

    const buildNavQueryPointMarker = (col: number, row: number, label: string, color: number, agentRadiusCm: number) => {
        const h = terrain.getHeight(col, row);
        const pos = cellToWorldPosition(col, row, h, boardMetrics, 2.0);
        const group = new THREE.Group();
        group.position.set(pos.x, pos.y + 0.75, pos.z);

        const agentRadiusM = Math.max(0, Number(agentRadiusCm) / 100.0);
        if (agentRadiusM > 0) {
            const footprintGeo = new THREE.CircleGeometry(agentRadiusM, 48);
            footprintGeo.rotateX(-Math.PI / 2);
            const footprintMat = new THREE.MeshBasicMaterial({
                color,
                transparent: true,
                opacity: 0.12,
                depthWrite: false,
                depthTest: false,
                side: THREE.DoubleSide,
            });
            const footprint = new THREE.Mesh(footprintGeo, footprintMat);
            footprint.position.y = 0.02;
            footprint.renderOrder = 998;
            group.add(footprint);

            const radiusGeo = new THREE.RingGeometry(agentRadiusM * 0.96, agentRadiusM, 48);
            radiusGeo.rotateX(-Math.PI / 2);
            const radiusMat = new THREE.MeshBasicMaterial({
                color,
                transparent: true,
                opacity: 0.72,
                depthTest: false,
                side: THREE.DoubleSide,
            });
            const radiusRing = new THREE.Mesh(radiusGeo, radiusMat);
            radiusRing.position.y = 0.04;
            radiusRing.renderOrder = 1000;
            group.add(radiusRing);
        }

        const ringGeo = new THREE.RingGeometry(1.4, 1.8, 28);
        ringGeo.rotateX(-Math.PI / 2);
        const ringMat = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.95, depthTest: false, side: THREE.DoubleSide });
        const ring = new THREE.Mesh(ringGeo, ringMat);
        ring.renderOrder = 1001;
        group.add(ring);

        const poleGeo = new THREE.CylinderGeometry(0.08, 0.08, 2.2, 8);
        const poleMat = new THREE.MeshBasicMaterial({ color, depthTest: false });
        const pole = new THREE.Mesh(poleGeo, poleMat);
        pole.position.y = 1.1;
        pole.renderOrder = 1001;
        group.add(pole);

        const sprite = buildTextSprite(label);
        sprite.position.set(0, 3.0, 0);
        sprite.scale.set(3.5, 2.0, 1);
        group.add(sprite);
        return group;
    };

    const updateDirtyChunks = () => {
        if (!canvasHasVisibleSessionRef.current) return;
        const currentTerrain = terrainRef.current;
        moveTerrainDirtyChunksToRenderQueue(currentTerrain);
        enqueueVisibleRenderWindow(false);
        if (!chunkRendererRef.current || !terrainGroupRef.current || renderDirtyChunksRef.current.size === 0) return;

        const group = terrainGroupRef.current;
        const renderer = chunkRendererRef.current;

        // Time Budget for Frame (e.g. 10ms)
        const startTime = performance.now();

        // We can't easily iterate and delete from Set partially without copying or using iterator.
        // Copying huge Set is expensive.
        // Using iterator is best.
        
        const dirtyIterator = renderDirtyChunksRef.current.values();
        const processedKeys: string[] = [];

        // Note: Set iterator order is insertion order.
        // We will iterate and process until time runs out.
        
        let done = false;
        
        // Notify Store/Minimap BEFORE processing (for highlight) - this is cheap
        // Actually, reportDirtyChunks might trigger re-renders in React, so maybe throttle it?
        // But for now let's keep it.
        // Optimization: Only report what we process? Or report all?
        // If we report all, Minimap will try to render all.
        // Let's report ALL initially (already done by store logic sort of, but store doesn't know about dirtyChunks content automatically).
        // Actually, minimapDirtyChunks in store is separate.
        // We should sync them.
        // For massive init, we don't want to flood the minimap either.
        
        // Let's just process chunks here.
        
        while (!done) {
            const next = dirtyIterator.next();
            if (next.done) {
                done = true;
                break;
            }

            const key = next.value;
            const [cx, cy] = key.split(',').map(Number);
            if (!isValidChunk(cx, cy, currentTerrain)) {
                processedKeys.push(key);
                continue;
            }

            // Remove old chunk
            const oldChunk = chunksRef.current.get(key);
            if (oldChunk) {
                group.remove(oldChunk);
                disposeThreeObject(oldChunk);
                chunksRef.current.delete(key);
            }

            // Generate new
            const newChunk = renderer.generateChunk(cx, cy, 0, 0, 2.0); 

            applyChunkLayerVisibility(newChunk);
            group.add(newChunk);
            chunksRef.current.set(key, newChunk);

            processedKeys.push(key);

            if (performance.now() - startTime > INITIAL_RENDER_FRAME_BUDGET_MS) {
                break;
            }
        }

        // Remove processed from dirty set
        processedKeys.forEach(k => renderDirtyChunksRef.current.delete(k));

        // Sync render rebuilds to the minimap only. Nav dirty is authored by terrain/area/obstacle edits,
        // not by the renderer consuming its own rebuild queue after Open.
        reportMinimapDirtyChunks(processedKeys);

        // Update Progress
        if (totalInitChunksRef.current > 0) {
            const remaining = renderDirtyChunksRef.current.size;
            const total = totalInitChunksRef.current;
            const progress = Math.floor(((total - remaining) / total) * 100);
            
            // Only update React state if changed significantly or finished
            // Throttle this? React state update every frame is bad.
            // But setLoading is bound to zustand, might be okay if selective.
            // Let's rely on requestAnimationFrame nature.
            
            if (remaining === 0) {
                setLoading(false);
                totalInitChunksRef.current = 0;
            } else {
                 // Update every 5% or so?
                 setLoading(true, `Generating visible terrain... ${progress}%`, progress);
            }
        }
    };

    const buildBakedNavMeshGroup = (tiles: Map<string, BakedNavTileVisual>, simulation: { points?: Array<{ xCm: number; zCm: number }> } | null, agentRadiusCm: number) => {
        const group = new THREE.Group();
        group.name = "BakedRecastNavTiles";

        const triMat = new THREE.MeshBasicMaterial({
            transparent: true,
            opacity: 0.46,
            side: THREE.DoubleSide,
            depthWrite: false,
            depthTest: false,
            vertexColors: true,
        });
        const wireMat = new THREE.LineBasicMaterial({
            color: 0xe0f2fe,
            transparent: true,
            opacity: 0.78,
            depthTest: false,
        });
        const boundaryMat = new THREE.LineBasicMaterial({
            color: 0xffffff,
            transparent: true,
            opacity: 0.94,
            depthTest: false,
        });
        const portalMat = new THREE.LineBasicMaterial({
            color: 0xfbbf24,
            transparent: true,
            opacity: 0.95,
            depthTest: false,
        });

        const tileMeshes: THREE.Object3D[] = [];
        tiles.forEach((visual) => {
            const tile = visual.tile;
            const geo = buildTileTriangleGeometry(tile);
            if (geo) {
                const mesh = new THREE.Mesh(geo, triMat);
                mesh.name = `RecastNavTileTriangles_${visual.profileId ?? 'manual'}_${tile.tileId.chunkX}_${tile.tileId.chunkY}_${visual.layer}`;
                mesh.renderOrder = 200;
                tileMeshes.push(mesh);
            }
            const wire = buildTileTriangleWireLines(tile, wireMat);
            if (wire) {
                wire.name = `RecastNavTileTriangleEdges_${visual.profileId ?? 'manual'}_${tile.tileId.chunkX}_${tile.tileId.chunkY}_${visual.layer}`;
                wire.renderOrder = 230;
                tileMeshes.push(wire);
            }
            const boundary = buildTileBoundaryLines(tile, boundaryMat);
            if (boundary) {
                boundary.name = `RecastNavTileBoundary_${visual.profileId ?? 'manual'}_${tile.tileId.chunkX}_${tile.tileId.chunkY}_${visual.layer}`;
                boundary.renderOrder = 240;
                tileMeshes.push(boundary);
            }
            const portals = buildTilePortalLines(tile, portalMat);
            if (portals) {
                portals.name = `RecastNavTilePortals_${visual.profileId ?? 'manual'}_${tile.tileId.chunkX}_${tile.tileId.chunkY}_${visual.layer}`;
                portals.renderOrder = 245;
                tileMeshes.push(portals);
            }
        });

        for (let i = 0; i < tileMeshes.length; i++) group.add(tileMeshes[i]);
        const pathGroup = buildNavSimulationPathGroup(simulation, tiles, agentRadiusCm);
        if (pathGroup) group.add(pathGroup);
        return group;
    };

    const buildNavSimulationPathGroup = (simulation: { points?: Array<{ xCm: number; zCm: number }> } | null, tiles: Map<string, BakedNavTileVisual>, agentRadiusCm: number) => {
        const points = simulation?.points ?? [];
        if (points.length < 2) return null;

        const pathPoints: THREE.Vector3[] = [];
        for (let i = 0; i < points.length; i++) {
            pathPoints.push(new THREE.Vector3(
                points[i].xCm / 100.0,
                sampleBakedNavHeightM(tiles, points[i].xCm, points[i].zCm) + 0.28,
                points[i].zCm / 100.0));
        }

        const group = new THREE.Group();
        group.name = 'NavPathSimulation';

        const agentRadiusM = Math.max(0, Number(agentRadiusCm) / 100.0);
        const radiusWidthM = Math.max(0.55, agentRadiusM * 2.0);
        const radiusGeo = buildPathRibbonGeometry(pathPoints, radiusWidthM);
        if (radiusGeo) {
            const radiusMat = new THREE.MeshBasicMaterial({
                color: 0x22d3ee,
                transparent: true,
                opacity: 0.20,
                depthWrite: false,
                depthTest: false,
                side: THREE.DoubleSide,
            });
            const radiusMesh = new THREE.Mesh(radiusGeo, radiusMat);
            radiusMesh.name = 'NavPathAgentRadiusBand';
            radiusMesh.renderOrder = 260;
            group.add(radiusMesh);
        }

        const centerWidthM = Math.max(0.38, Math.min(1.35, radiusWidthM * 0.32));
        const centerGeo = buildPathRibbonGeometry(pathPoints, centerWidthM);
        if (centerGeo) {
            const centerMat = new THREE.MeshBasicMaterial({
                color: 0xffffff,
                transparent: true,
                opacity: 0.96,
                depthWrite: false,
                depthTest: false,
                side: THREE.DoubleSide,
            });
            const centerMesh = new THREE.Mesh(centerGeo, centerMat);
            centerMesh.name = 'NavPathCenterRibbon';
            centerMesh.renderOrder = 270;
            group.add(centerMesh);
        }

        return group.children.length > 0 ? group : null;
    };

    const buildPathRibbonGeometry = (points: THREE.Vector3[], widthM: number) => {
        if (points.length < 2 || !(widthM > 0)) return null;
        const positions: number[] = [];
        const halfWidth = widthM * 0.5;

        for (let i = 0; i < points.length - 1; i++) {
            const a = points[i];
            const b = points[i + 1];
            const dx = b.x - a.x;
            const dz = b.z - a.z;
            const len = Math.hypot(dx, dz);
            if (len <= 1e-5) continue;

            const nx = -dz / len;
            const nz = dx / len;
            pushRibbonVertex(positions, a.x + nx * halfWidth, a.y, a.z + nz * halfWidth);
            pushRibbonVertex(positions, a.x - nx * halfWidth, a.y, a.z - nz * halfWidth);
            pushRibbonVertex(positions, b.x - nx * halfWidth, b.y, b.z - nz * halfWidth);
            pushRibbonVertex(positions, a.x + nx * halfWidth, a.y, a.z + nz * halfWidth);
            pushRibbonVertex(positions, b.x - nx * halfWidth, b.y, b.z - nz * halfWidth);
            pushRibbonVertex(positions, b.x + nx * halfWidth, b.y, b.z + nz * halfWidth);
        }

        if (positions.length === 0) return null;
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        return geo;
    };

    const pushRibbonVertex = (dst: number[], x: number, y: number, z: number) => {
        dst.push(x, y, z);
    };

    const sampleBakedNavHeightM = (tiles: Map<string, BakedNavTileVisual>, xCm: number, zCm: number) => {
        for (const visual of tiles.values()) {
            const tile = visual.tile;
            const localX = xCm - tile.originXcm;
            const localZ = zCm - tile.originZcm;
            const tCount = tile.triA.length;
            for (let i = 0; i < tCount; i++) {
                const height = sampleTriangleHeightCm(tile, tile.triA[i], tile.triB[i], tile.triC[i], localX, localZ);
                if (height !== null) return height / 100.0;
            }
        }
        return 0.45;
    };

    const sampleTriangleHeightCm = (tile: NavTile, ia: number, ib: number, ic: number, x: number, z: number) => {
        const ax = tile.vertexXcm[ia], az = tile.vertexZcm[ia];
        const bx = tile.vertexXcm[ib], bz = tile.vertexZcm[ib];
        const cx = tile.vertexXcm[ic], cz = tile.vertexZcm[ic];
        const v0x = bx - ax, v0z = bz - az;
        const v1x = cx - ax, v1z = cz - az;
        const v2x = x - ax, v2z = z - az;
        const den = v0x * v1z - v1x * v0z;
        if (Math.abs(den) < 1e-5) return null;
        const u = (v2x * v1z - v1x * v2z) / den;
        const v = (v0x * v2z - v2x * v0z) / den;
        const w = 1 - u - v;
        const eps = -1e-4;
        if (u < eps || v < eps || w < eps) return null;
        return tile.vertexYcm[ia] * w + tile.vertexYcm[ib] * u + tile.vertexYcm[ic] * v;
    };

    const buildTileTriangleGeometry = (tile: NavTile) => {
        const tCount = tile.triA.length;
        if (tCount === 0) return null;
        const pos = new Float32Array(tCount * 3 * 3);
        let w = 0;

        for (let i = 0; i < tCount; i++) {
            w = writeVertex(tile, tile.triA[i], pos, w);
            w = writeVertex(tile, tile.triB[i], pos, w);
            w = writeVertex(tile, tile.triC[i], pos, w);
        }

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        geo.setAttribute('color', new THREE.BufferAttribute(buildTileTriangleColors(tile), 3));
        geo.computeVertexNormals();
        return geo;
    };

    const buildTileTriangleColors = (tile: NavTile) => {
        const tCount = tile.triA.length;
        const color = new Float32Array(tCount * 3 * 3);
        let o = 0;
        for (let i = 0; i < tCount; i++) {
            const c = getAreaColor(tile.triAreaIds[i] ?? 0);
            for (let v = 0; v < 3; v++) {
                color[o++] = c.r;
                color[o++] = c.g;
                color[o++] = c.b;
            }
        }
        return color;
    };

    const getAreaColor = (areaId: number) => {
        switch (areaId) {
            case 0: return new THREE.Color(0x38bdf8);
            case 1: return new THREE.Color(0x9ca3af);
            case 2: return new THREE.Color(0x22d3ee);
            case 3: return new THREE.Color(0xa78bfa);
            case 4: return new THREE.Color(0x60a5fa);
            case 5: return new THREE.Color(0xf59e0b);
            default: {
                const hue = ((areaId * 47) % 360) / 360;
                return new THREE.Color().setHSL(hue, 0.72, 0.55);
            }
        }
    };

    const applyChunkLayerVisibility = (chunk: THREE.Object3D) => {
        const view = viewStateRef.current;
        const fastNavVisible = view.showNavMesh && !view.hasBakedNavTiles;
        chunk.traverse((obj) => {
            if (obj.name === 'NavMesh') obj.visible = fastNavVisible;
            if (obj.name === 'CellGrid' || obj.name === 'CellPoints') obj.visible = view.showGrid;
            if (obj.name === 'ChunkBorder') obj.visible = view.showChunkBorders;
        });
    };

    const buildTilePortalLines = (tile: NavTile, mat: THREE.LineBasicMaterial) => {
        if (!tile.portals || tile.portals.length === 0) return null;
        const pos = new Float32Array(tile.portals.length * 2 * 3);
        let o = 0;
        for (let i = 0; i < tile.portals.length; i++) {
            const p = tile.portals[i];
            const x0 = (tile.originXcm + p.leftXcm) / 100.0;
            const z0 = (tile.originZcm + p.leftZcm) / 100.0;
            const x1 = (tile.originXcm + p.rightXcm) / 100.0;
            const z1 = (tile.originZcm + p.rightZcm) / 100.0;
            const y = 0.08;
            pos[o++] = x0; pos[o++] = y; pos[o++] = z0;
            pos[o++] = x1; pos[o++] = y; pos[o++] = z1;
        }

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        return new THREE.LineSegments(geo, mat);
    };

    const buildTileTriangleWireLines = (tile: NavTile, mat: THREE.LineBasicMaterial) => {
        const tCount = tile.triA.length;
        if (tCount === 0) return null;
        const positions: number[] = [];
        for (let i = 0; i < tCount; i++) {
            pushEdge(tile, tile.triA[i], tile.triB[i], positions);
            pushEdge(tile, tile.triB[i], tile.triC[i], positions);
            pushEdge(tile, tile.triC[i], tile.triA[i], positions);
        }
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        return new THREE.LineSegments(geo, mat);
    };

    const buildTileBoundaryLines = (tile: NavTile, mat: THREE.LineBasicMaterial) => {
        const tCount = tile.triA.length;
        if (tCount === 0) return null;

        const getVx = (idx: number) => (tile.originXcm + tile.vertexXcm[idx]) / 100.0;
        const getVz = (idx: number) => (tile.originZcm + tile.vertexZcm[idx]) / 100.0;

        const edgeKeys = new Set<string>();
        const edges: Array<[number, number]> = [];

        const addEdge = (a: number, b: number) => {
            const lo = a < b ? a : b;
            const hi = a < b ? b : a;
            const key = `${lo},${hi}`;
            if (edgeKeys.has(key)) return;
            edgeKeys.add(key);
            edges.push([lo, hi]);
        };

        for (let i = 0; i < tCount; i++) {
            if (tile.n0[i] === -1) addEdge(tile.triA[i], tile.triB[i]);
            if (tile.n1[i] === -1) addEdge(tile.triB[i], tile.triC[i]);
            if (tile.n2[i] === -1) addEdge(tile.triC[i], tile.triA[i]);
        }

        if (edges.length === 0) return null;

        const adj = new Map<number, number[]>();
        for (let i = 0; i < edges.length; i++) {
            const [a, b] = edges[i];
            let la = adj.get(a);
            if (!la) { la = []; adj.set(a, la); }
            la.push(b);
            let lb = adj.get(b);
            if (!lb) { lb = []; adj.set(b, lb); }
            lb.push(a);
        }

        const used = new Set<string>();
        const loops: number[][] = [];
        for (let i = 0; i < edges.length; i++) {
            const [a0, b0] = edges[i];
            const eKey = `${a0},${b0}`;
            if (used.has(eKey)) continue;

            const loop: number[] = [a0];
            let prev = -1;
            let curr = a0;
            let next = b0;

            while (true) {
                const lo = curr < next ? curr : next;
                const hi = curr < next ? next : curr;
                used.add(`${lo},${hi}`);
                loop.push(next);
                if (next === a0) break;

                const neighbors = adj.get(next) ?? [];
                const nx = getVx(next);
                const nz = getVz(next);
                const cx = getVx(curr);
                const cz = getVz(curr);
                let inDx = nx - cx;
                let inDz = nz - cz;
                const inLen = Math.hypot(inDx, inDz);
                if (inLen > 1e-9) { inDx /= inLen; inDz /= inLen; }

                const pickCandidate = (allowPrev: boolean) => {
                    let best = -1;
                    let bestScore = -Infinity;
                    for (let k = 0; k < neighbors.length; k++) {
                        const n = neighbors[k];
                        if (n === curr) continue;
                        if (!allowPrev && n === prev) continue;
                        const lo2 = next < n ? next : n;
                        const hi2 = next < n ? n : next;
                        if (used.has(`${lo2},${hi2}`)) continue;

                        const ox = getVx(n) - nx;
                        const oz = getVz(n) - nz;
                        const oLen = Math.hypot(ox, oz);
                        if (oLen <= 1e-9) continue;
                        const outDx = ox / oLen;
                        const outDz = oz / oLen;
                        const score = inDx * outDx + inDz * outDz;
                        if (score > bestScore) {
                            bestScore = score;
                            best = n;
                        }
                    }
                    return best;
                };

                let cand = pickCandidate(false);
                if (cand === -1) cand = pickCandidate(true);

                if (cand === -1) break;
                prev = curr;
                curr = next;
                next = cand;
            }

            if (loop.length >= 4 && loop[0] === loop[loop.length - 1]) loops.push(loop);
        }

        if (loops.length === 0) {
            const positions: number[] = [];
            for (let i = 0; i < edges.length; i++) pushEdge(tile, edges[i][0], edges[i][1], positions);
            const geo = new THREE.BufferGeometry();
            geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
            return new THREE.LineSegments(geo, mat);
        }

        const group = new THREE.Group();
        for (let i = 0; i < loops.length; i++) {
            const ring = loops[i];
            const ringOpen = ring.length >= 2 && ring[0] === ring[ring.length - 1] ? ring.slice(0, ring.length - 1) : ring.slice();
            const simplified: number[] = [];
            for (let k = 0; k < ringOpen.length; k++) simplified.push(ringOpen[k]);
            if (simplified.length >= 4) {
                for (let pass = 0; pass < 2; pass++) {
                    let changed = false;
                    for (let k = 0; k < simplified.length; ) {
                        if (simplified.length < 4) break;
                        const a = simplified[(k - 1 + simplified.length) % simplified.length];
                        const b = simplified[k];
                        const c = simplified[(k + 1) % simplified.length];
                        if (a === b || b === c) {
                            simplified.splice(k, 1);
                            changed = true;
                            continue;
                        }

                        const ax = getVx(a), az = getVz(a);
                        const bx = getVx(b), bz = getVz(b);
                        const cx = getVx(c), cz = getVz(c);

                        const abx = bx - ax;
                        const abz = bz - az;
                        const bcx = cx - bx;
                        const bcz = cz - bz;
                        const abLen = Math.hypot(abx, abz);
                        const bcLen = Math.hypot(bcx, bcz);
                        if (abLen < 1e-4 || bcLen < 1e-4) {
                            simplified.splice(k, 1);
                            changed = true;
                            continue;
                        }

                        const cross = abx * bcz - abz * bcx;
                        const dot = abx * bcx + abz * bcz;
                        const sin = Math.abs(cross) / (abLen * bcLen);
                        if (sin < 0.02 && dot > 0) {
                            simplified.splice(k, 1);
                            changed = true;
                            continue;
                        }
                        k++;
                    }
                    if (!changed) break;
                }
            }

            const positions: number[] = [];
            for (let k = 0; k < simplified.length; k++) writeVertexToList(tile, simplified[k], 0.08, positions);
            const geo = new THREE.BufferGeometry();
            geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
            group.add(new THREE.LineLoop(geo, mat));
        }
        return group;
    };

    const pushEdge = (tile: NavTile, ia: number, ib: number, dst: number[]) => {
        const yOffset = 0.08;
        writeVertexToList(tile, ia, yOffset, dst);
        writeVertexToList(tile, ib, yOffset, dst);
    };

    const writeVertex = (tile: NavTile, idx: number, out: Float32Array, o: number) => {
        const x = (tile.originXcm + tile.vertexXcm[idx]) / 100.0;
        const y = (tile.vertexYcm[idx]) / 100.0 + 0.05;
        const z = (tile.originZcm + tile.vertexZcm[idx]) / 100.0;
        out[o++] = x;
        out[o++] = y;
        out[o++] = z;
        return o;
    };

    const writeVertexToList = (tile: NavTile, idx: number, yOffset: number, dst: number[]) => {
        dst.push((tile.originXcm + tile.vertexXcm[idx]) / 100.0);
        dst.push((tile.vertexYcm[idx]) / 100.0 + yOffset);
        dst.push((tile.originZcm + tile.vertexZcm[idx]) / 100.0);
    };

    // Input Handling
    const getCellFromEvent = (clientX: number, clientY: number) => {
        if (!containerRef.current || !cameraRef.current || !inputPlaneRef.current) return null;
        
        const rect = containerRef.current.getBoundingClientRect();
        const x = ((clientX - rect.left) / rect.width) * 2 - 1;
        const y = -((clientY - rect.top) / rect.height) * 2 + 1;

        mouseRef.current.set(x, y);
        raycasterRef.current.setFromCamera(mouseRef.current, cameraRef.current);
        
        const intersects = raycasterRef.current.intersectObject(inputPlaneRef.current);
        if (intersects.length === 0) return null;

        const point = intersects[0].point;
        const cell = worldPointToCell(point.x, point.z, boardMetricsRef.current);
        const c = cell.col;
        const r = cell.row;
        
        return { c, r, point };
    };

    const applyBrush = (c: number, r: number) => {
        if (!canvasEditable) return;
        const size = brushSize;
        const range = size - 1;
        const navTouchedChunks = new Set<string>();
        const markNavDirtyCell = (col: number, row: number, includeNeighborCells = false) => {
            const chunkSize = boardMetricsRef.current.chunkSizeCells;
            const minCol = includeNeighborCells ? col - 1 : col;
            const maxCol = includeNeighborCells ? col + 1 : col;
            const minRow = includeNeighborCells ? row - 1 : row;
            const maxRow = includeNeighborCells ? row + 1 : row;
            for (let yy = minRow; yy <= maxRow; yy++) {
                for (let xx = minCol; xx <= maxCol; xx++) {
                    const cx = Math.floor(xx / chunkSize);
                    const cy = Math.floor(yy / chunkSize);
                    if (terrain.isValidChunk(cx, cy)) navTouchedChunks.add(`${cx},${cy}`);
                }
            }
        };
        
        // Simple circle brush
        for (let dy = -range; dy <= range; dy++) {
            for (let dx = -range; dx <= range; dx++) {
                // Hex distance is tricky, using simple grid distance for MVP
                // Or implementing axial distance?
                // editor_v2 used: if (dx*dx + dy*dy <= range*range + 1)
                // Let's stick to that simple Euclidean approx on grid coords
                if (dx*dx + dy*dy <= range*range + 0.5) {
                    const tc = c + dx;
                    const tr = r + dy;
                    
                    // Boundary Check
                    // terrain store handles this via get/set safely usually, but let's be explicit
                    if (tc < 0 || tr < 0) continue; // max check in store

                    // Apply Logic based on Tool
                    switch (activeCategory) {
                        case 'Height':
                        {
                            const curH = terrain.getHeight(tc, tr);
                            let newH = curH;
                            if (activeMode === 'Set') newH = brushValue;
                            else if (activeMode === 'Raise') newH = Math.min(15, curH + 1); // Clamp to 15
                            else if (activeMode === 'Lower') newH = Math.max(0, curH - 1);
                            
                            if (newH !== curH) {
                                terrain.setHeight(tc, tr, newH);
                                markNavDirtyCell(tc, tr, true);
                            }
                            break;
                        }
                        case 'Water':
                        {
                             // BUCKET TOOL: Fill Water
                             if (activeMode === 'Bucket') {
                                 // Target Water Height is current brush value (or 0 if erasing?)
                                 // If user holds Shift or something? No, let's just use brushValue.
                                 const targetWaterH = Math.min(15, brushValue); // Clamp to 15
                                 
                                 // Flood Fill Algorithm
                                 // Condition: Expand if (TerrainHeight < TargetWaterH)
                                 // Boundary: TerrainHeight >= TargetWaterH
                                 
                                 const visited = new Set<string>();
                                 const queue: {c: number, r: number}[] = [{c: tc, r: tr}];
                                 
                                 // Safety limit
                                 let count = 0;
                                 const MAX_FILL = 2000;

                                 while(queue.length > 0 && count < MAX_FILL) {
                                     const {c, r} = queue.shift()!;
                                     const key = `${c},${r}`;
                                     if (visited.has(key)) continue;
                                     visited.add(key);
                                     count++;

                                     // Check Terrain Height
                                     const h = terrain.getHeight(c, r);
                                     
                                     // If Terrain is higher or equal to target water level, it's a boundary (Shore)
                                     // We do NOT fill this cell (or maybe we do if it's strictly lower?)
                                     // Standard: Water fills only where WaterLevel > TerrainLevel
                                     // But what if we want to fill a pit?
                                     // If h < targetWaterH, we fill.
                                     
                                     if (h < targetWaterH) {
                                         // Set Water
                                         terrain.setWater(c, r, targetWaterH);
                                         markNavDirtyCell(c, r);
                                         
                                         // Neighbors
                                         const neighbors = getTopologyNeighbors(c, r, boardMetricsRef.current);
                                         for(const n of neighbors) {
                                             if (!visited.has(`${n.c},${n.r}`)) {
                                                 queue.push(n);
                                             }
                                         }
                                     }
                                 }
                                 break;
                             }

                             const curW = terrain.getWater(tc, tr);
                             let newW = curW;
                             if (activeMode === 'Set') newW = brushValue;
                             else if (activeMode === 'Raise') newW = Math.min(15, curW + 1); // Clamp to 15
                             else if (activeMode === 'Lower') newW = Math.max(0, curW - 1);
                             if (newW !== curW) {
                                 terrain.setWater(tc, tr, newW);
                                 markNavDirtyCell(tc, tr);
                             }
                             break;
                        }
                        
                        case 'Territory':
                        {
                             const curT = terrain.getTerritory(tc, tr);
                             if (activeMode === 'Set') {
                                 if (curT !== brushValue) {
                                     terrain.setTerritory(tc, tr, brushValue);
                                     markNavDirtyCell(tc, tr);
                                 }
                             }
                             // Territory doesn't make sense to Raise/Lower usually, but maybe cycle IDs?
                             // Let's keep it simple: Set mode paints the ID.
                             break;
                        }
                        case 'Biome':
                            if (activeMode === 'Set') terrain.setBiome(tc, tr, brushValue);
                            break;
                        case 'Area':
                            {
                                const oldArea = terrain.getAreaId(tc, tr);
                                let newArea = oldArea;
                                if (activeMode === 'Set') newArea = brushValue;
                                else if (activeMode === 'Raise') newArea = Math.min(255, oldArea + 1);
                                else if (activeMode === 'Lower') newArea = Math.max(0, oldArea - 1);
                                if (newArea !== oldArea) {
                                    terrain.setAreaId(tc, tr, newArea);
                                    markNavDirtyCell(tc, tr);
                                }
                            }
                            break;
                        case 'Blocked':
                            {
                                let blocked = terrain.getBlocked(tc, tr);
                                if (activeMode === 'Set') blocked = brushValue > 0;
                                else if (activeMode === 'Raise') blocked = true;
                                else if (activeMode === 'Lower') blocked = false;
                                if (blocked !== terrain.getBlocked(tc, tr)) {
                                    terrain.setBlocked(tc, tr, blocked);
                                    markNavDirtyCell(tc, tr);
                                }
                            }
                            break;
                        case 'Vegetation':
                             if (activeMode === 'Set') terrain.setVeg(tc, tr, brushValue);
                             break;
                        case 'Ramp':
                        {
                            // Support Raise/Lower as On/Off shortcut
                            let isRamp = terrain.isRamp(tc, tr);
                            if (activeMode === 'Set') isRamp = brushValue > 0;
                            else if (activeMode === 'Raise') isRamp = true;
                            else if (activeMode === 'Lower') isRamp = false;
                            
                            if (isRamp !== terrain.isRamp(tc, tr)) {
                                terrain.setRamp(tc, tr, isRamp);
                                markNavDirtyCell(tc, tr, true);
                            }
                            break;
                        }
                        case 'Layers':
                        {
                            if (!activeLayer) break;
                            let val = false;
                            
                            // Get current state
                            if (activeLayer === 'Snow') val = terrain.getSnow(tc, tr);
                            else if (activeLayer === 'Mud') val = terrain.getMud(tc, tr);
                            else if (activeLayer === 'Ice') val = terrain.getIce(tc, tr);
                            
                            // Determine target state
                            // Raise/Set(1) = On, Lower/Set(0) = Off
                            let target = val;
                            if (activeMode === 'Set') target = brushValue > 0;
                            else if (activeMode === 'Raise') target = true;
                            else if (activeMode === 'Lower') target = false;
                            
                            if (target !== val) {
                                if (activeLayer === 'Snow') terrain.setSnow(tc, tr, target);
                                else if (activeLayer === 'Mud') terrain.setMud(tc, tr, target);
                                else if (activeLayer === 'Ice') terrain.setIce(tc, tr, target);
                                markNavDirtyCell(tc, tr);
                            }
                            break;
                        }
                    }
                }
            }
        }
        if (navTouchedChunks.size > 0) reportDirtyChunks(navTouchedChunks);
    };

    const handleMouseDown = (e: React.MouseEvent) => {
        if (canvasInputLocked) {
            if (e.button === 0 || e.button === 2) {
                e.preventDefault();
                e.stopPropagation();
            }
            isDraggingRef.current = false;
            lastDragCellRef.current = null;
            return;
        }
        if (navPanelTab === 'simulation') {
            if (e.button !== 0 && e.button !== 2) return;
            e.preventDefault();
            e.stopPropagation();
            const cell = getCellFromEvent(e.clientX, e.clientY);
            if (cell) {
                if (e.button === 0) {
                    setNavQueryStartCell({ col: cell.c, row: cell.r });
                } else {
                    setNavQueryGoalCell({ col: cell.c, row: cell.r });
                }
            }
            isDraggingRef.current = false;
            lastDragCellRef.current = null;
            return;
        }

        if (e.button !== 0) return; // Left Click Only
        const cell = getCellFromEvent(e.clientX, e.clientY);
        if (cell) {
            isDraggingRef.current = true;
            if (activeCategory === 'Entities') {
                if (activeMode === 'Set') placeEntityAt(cell.c, cell.r);
                else if (activeMode === 'Lower') removeEntityAt(cell.c, cell.r);
                else if (activeMode === 'Raise') selectEntityAt(cell.c, cell.r);
            } else if (activeCategory === 'Obstacle') {
                if (activeMode === 'Set' || activeMode === 'Raise') placeObstacleAt(cell.c, cell.r);
                else if (activeMode === 'Lower') removeEntityAt(cell.c, cell.r);
            } else {
                applyBrush(cell.c, cell.r);
            }
            lastDragCellRef.current = cell;
        }
    };

    const handleMouseMove = (e: React.MouseEvent) => {
        const cell = getCellFromEvent(e.clientX, e.clientY);
        
        // Update Cursor
        if (cursorMeshRef.current && (navPanelTab === 'simulation' || !canvasEditable)) {
            cursorMeshRef.current.visible = false;
        } else if (cursorMeshRef.current && cell) {
            const h = terrain.getHeight(cell.c, cell.r);
            const pos = cellToWorldPosition(cell.c, cell.r, h, boardMetricsRef.current, 2.0);
            cursorMeshRef.current.position.set(pos.x, pos.y + 0.2, pos.z);
            const radius = getBrushVisualRadius(boardMetricsRef.current, brushSize);
            cursorMeshRef.current.scale.set(radius, radius, radius);
            cursorMeshRef.current.visible = true;
        } else if (cursorMeshRef.current) {
            cursorMeshRef.current.visible = false;
        }

        // Drag Paint
        if (canvasEditable && navPanelTab !== 'simulation' && isDraggingRef.current && cell) {
            if (!lastDragCellRef.current || lastDragCellRef.current.c !== cell.c || lastDragCellRef.current.r !== cell.r) {
                if (activeCategory === 'Entities') {
                    if (activeMode === 'Set') placeEntityAt(cell.c, cell.r);
                    else if (activeMode === 'Lower') removeEntityAt(cell.c, cell.r);
                } else if (activeCategory === 'Obstacle') {
                    if (activeMode === 'Set' || activeMode === 'Raise') placeObstacleAt(cell.c, cell.r);
                    else if (activeMode === 'Lower') removeEntityAt(cell.c, cell.r);
                } else {
                    applyBrush(cell.c, cell.r);
                }
                lastDragCellRef.current = cell;
            }
        }
    };

    const handleMouseUp = () => {
        isDraggingRef.current = false;
        lastDragCellRef.current = null;
    };

    return (
        <div 
            ref={containerRef} 
            className="w-full h-full relative overflow-hidden"
            onMouseDown={handleMouseDown}
            onMouseMove={handleMouseMove}
            onMouseUp={handleMouseUp}
            onMouseLeave={handleMouseUp}
            onContextMenu={(e) => {
                if (navPanelTab === 'simulation') e.preventDefault();
            }}
        >
            {!canvasHasVisibleSession ? (
                <div className="pointer-events-none absolute inset-0 z-20 flex items-center justify-center bg-slate-950">
                    <div className="max-w-[420px] rounded-lg border border-slate-700 bg-slate-900/95 p-5 text-center text-slate-200 shadow-2xl">
                        <div className="text-sm font-semibold text-white">No Board Open</div>
                        <div className="mt-2 text-xs leading-5 text-slate-400">
                            Select a map and board in the top bar, then use Map And Board / Board Session / Open Selected before editing terrain, baking nav, or running simulation.
                        </div>
                    </div>
                </div>
            ) : canvasInputLocked ? (
                <div className="pointer-events-none absolute left-1/2 top-28 z-20 w-[340px] -translate-x-1/2 rounded-lg border border-amber-700/70 bg-slate-950/90 p-3 text-center text-amber-100 shadow-2xl backdrop-blur-md">
                    <div className="text-xs font-semibold">{canvasLockTitle}</div>
                    <div className="mt-1 text-[11px] leading-4 text-amber-100/80">{canvasLockMessage}</div>
                </div>
            ) : null}
        </div>
    );
};
