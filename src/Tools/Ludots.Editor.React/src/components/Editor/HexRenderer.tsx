import React, { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three-stdlib';
import { useEditorStore } from './EditorStore';
import { ChunkRenderer } from '../../Core/Render/ChunkRenderer';
import { HEX_WIDTH, ROW_SPACING, getHexPosition } from '../../Core/Map/HexMetrics';
import { TerrainStore } from '../../Core/Map/TerrainStore';
import type { NavTile } from '../../Core/NavMesh/NavTileBinary';

// Helper for Neighbors (Axial coords)
function getNeighbors(c: number, r: number) {
    const isOdd = (r & 1) === 1;
    const offsets = isOdd 
        ? [[1,0], [1,1], [0,1], [-1,0], [0,-1], [1,-1]] 
        : [[1,0], [0,1], [-1,1], [-1,0], [-1,-1], [0,-1]];
    
    return offsets.map(o => ({ c: c + o[0], r: r + o[1] }));
}

export const HexRenderer: React.FC = () => {
    const containerRef = useRef<HTMLDivElement>(null);
    const { terrain, activeCategory, activeMode, brushSize, brushValue, activeLayer, showGrid, showChunkBorders, showNavMesh, bakedNavTiles, bakedNavTilesVersion, registerCamera, reportDirtyChunks, setLoading, placeEntityAt, removeEntityAt, selectEntityAt, templates, spawnEntities, selectedEntityIndex, entitiesVersion } = useEditorStore();
    
    // Refs for mutable state in animation loop
    const sceneRef = useRef<THREE.Scene | null>(null);
    const rendererRef = useRef<THREE.WebGLRenderer | null>(null);
    const chunksRef = useRef<Map<string, THREE.Group>>(new Map());
    const navMeshRef = useRef<THREE.Group | null>(null);
    const cameraRef = useRef<THREE.PerspectiveCamera | null>(null);
    const controlsRef = useRef<OrbitControls | null>(null);
    const chunkRendererRef = useRef<ChunkRenderer | null>(null);
    const terrainGroupRef = useRef<THREE.Group | null>(null);
    const cursorMeshRef = useRef<THREE.Mesh | null>(null);
    const entityGroupRef = useRef<THREE.Group | null>(null);
    const raycasterRef = useRef(new THREE.Raycaster());
    const mouseRef = useRef(new THREE.Vector2());
    const inputPlaneRef = useRef<THREE.Mesh | null>(null);
    const rafRef = useRef<number | null>(null);
    
    // We need to store current terrain in a ref for the animation loop
    // because the animation loop closure captures the initial terrain instance.
    const terrainRef = useRef(terrain);
    // Track initialization progress
    const totalInitChunksRef = useRef(0);

    useEffect(() => {
        terrainRef.current = terrain;
        // On new terrain, reset init counter if we are in loading state?
        // Actually, initMap sets loadingState. 
        // We can check if dirtyChunks is massive (init scenario)
        if (terrain.dirtyChunks.size > 100) {
            totalInitChunksRef.current = terrain.dirtyChunks.size;
        } else {
            totalInitChunksRef.current = 0;
            setLoading(false); // Clear if small update
        }
    }, [terrain, setLoading]);

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
            LEFT: THREE.MOUSE.ROTATE, 
            MIDDLE: THREE.MOUSE.PAN,
            RIGHT: THREE.MOUSE.ROTATE
        };
        // Disable Left click for OrbitControls so we can use it for painting
        // @ts-ignore
        controls.mouseButtons.LEFT = THREE.MOUSE.UNKNOWN; 
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

        // 8. Terrain Group
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
            renderer.dispose();
            // Unregister? Maybe not needed as ref will be overwritten or component unmounts
        };
    }, []); // Only run once for setup? No, if terrain changes we need to re-bind? 
    // Actually we want setup once. Data sync is in next useEffect.

    // Sync Terrain Data to 3D
    useEffect(() => {
        if (!terrainGroupRef.current) return;
        
        // Clear old meshes
        terrainGroupRef.current.clear();
        
        chunkRendererRef.current = new ChunkRenderer(terrain);
        
        // Initial Full Render
        terrain.clearDirty(); // Reset dirty flags
        // Mark all as dirty to force render
        for(let y=0; y<terrain.heightChunks; y++) {
            for(let x=0; x<terrain.widthChunks; x++) {
                terrain.dirtyChunks.add(`${x},${y}`);
            }
        }
        updateDirtyChunks();
        
        // Update Input Plane Size/Pos
        if (inputPlaneRef.current) {
            const totalW = terrain.widthChunks * 64 * HEX_WIDTH;
            const totalH = terrain.heightChunks * 64 * ROW_SPACING;
            inputPlaneRef.current.scale.set(totalW, totalH, 1);
            inputPlaneRef.current.position.set(totalW/2, 0, totalH/2);
            inputPlaneRef.current.updateMatrixWorld();
        }

    }, [terrain]); // Re-run when terrain object changes (load map)

    useEffect(() => {
        if (!navMeshRef.current) return;
        navMeshRef.current.visible = showNavMesh && bakedNavTiles.size > 0;
    }, [showNavMesh, bakedNavTiles.size]);

    useEffect(() => {
        if (!navMeshRef.current) return;
        if (!showNavMesh || bakedNavTiles.size === 0) {
            navMeshRef.current.clear();
            return;
        }
        navMeshRef.current.clear();
        navMeshRef.current.add(buildBakedNavMeshGroup(bakedNavTiles));
    }, [showNavMesh, bakedNavTilesVersion]);

    useEffect(() => {
        if (!entityGroupRef.current) return;
        const group = entityGroupRef.current;
        group.clear();

        const templatesById = new Map<string, any>();
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
            const components = t?.components ?? t?.Components ?? {};
            const visual = (e.overrides?.VisualModel ?? components?.VisualModel ?? components?.visualModel) ?? null;
            const meshId = Number(visual?.MeshId ?? visual?.meshId ?? 0);

            const geo = meshId === 2 ? sphereGeo : cubeGeo;
            const mat = new THREE.MeshStandardMaterial({ color: getColor(e.template) });

            const m = new THREE.Mesh(geo, mat);
            const h = terrain.getHeight(e.position.x, e.position.y);
            const pos = getHexPosition(e.position.x, e.position.y, h, 2.0);
            m.position.set(pos.x, pos.y + 1.0, pos.z);
            m.renderOrder = 10;

            if (selectedEntityIndex === i) {
                (m.material as THREE.MeshStandardMaterial).emissive = new THREE.Color(0.3, 0.2, 0.8);
                (m.material as THREE.MeshStandardMaterial).emissiveIntensity = 1.0;
            }

            const bindings = e.overrides?.PerformerBindings ?? e.overrides?.performerBindings ?? null;
            const ids = bindings?.Ids ?? bindings?.ids ?? bindings?.DefinitionIds ?? bindings?.definitionIds ?? null;
            if (Array.isArray(ids) && ids.length > 0) {
                const sprite = buildTextSprite(String(ids[0]));
                sprite.position.set(0, 3.0, 0);
                m.add(sprite);
            }

            group.add(m);
        }
    }, [entitiesVersion, terrain, templates, spawnEntities, selectedEntityIndex]);

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

    const updateDirtyChunks = () => {
        const currentTerrain = terrainRef.current;
        if (!chunkRendererRef.current || !terrainGroupRef.current || currentTerrain.dirtyChunks.size === 0) return;

        const group = terrainGroupRef.current;
        const renderer = chunkRendererRef.current;

        // Time Budget for Frame (e.g. 10ms)
        const startTime = performance.now();
        const TIME_BUDGET = 12; // ms

        // We can't easily iterate and delete from Set partially without copying or using iterator.
        // Copying huge Set is expensive.
        // Using iterator is best.
        
        const dirtyIterator = currentTerrain.dirtyChunks.values();
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

            // Remove old chunk
            const oldChunk = group.getObjectByName(`chunk_${cx}_${cy}`);
            if (oldChunk) group.remove(oldChunk);

            // Generate new
            const newChunk = renderer.generateChunk(cx, cy, 0, 0, 2.0); 

            const fastNavVisible = showNavMesh && bakedNavTiles.size === 0;
            newChunk.traverse((obj) => {
                if (obj.name === "NavMesh") obj.visible = fastNavVisible;
                if (obj.type === 'Points') obj.visible = !(showNavMesh && bakedNavTiles.size > 0);
            });
            group.add(newChunk);

            processedKeys.push(key);

            if (performance.now() - startTime > TIME_BUDGET) {
                break;
            }
        }

        // Remove processed from dirty set
        processedKeys.forEach(k => currentTerrain.dirtyChunks.delete(k));

        // Sync to Minimap (Only processed ones, to spread load there too)
        reportDirtyChunks(processedKeys);

        // Update Progress
        if (totalInitChunksRef.current > 0) {
            const remaining = currentTerrain.dirtyChunks.size;
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
                 setLoading(true, `Generating Terrain... ${progress}%`, progress);
            }
        }
    };

    const buildBakedNavMeshGroup = (tiles: Map<string, NavTile>) => {
        const group = new THREE.Group();
        group.name = "bakedNavMesh";

        const triMat = new THREE.MeshBasicMaterial({
            color: 0x00ff66,
            transparent: true,
            opacity: 0.18,
            side: THREE.DoubleSide,
            depthWrite: false,
            depthTest: false,
        });

        const portalMat = new THREE.LineBasicMaterial({
            color: 0xffaa00,
            transparent: true,
            opacity: 0.9,
            depthTest: false,
        });

        const boundaryMat = new THREE.LineBasicMaterial({
            color: 0x00e5ff,
            transparent: true,
            opacity: 0.9,
            depthTest: false,
        });

        const vertexMat = new THREE.PointsMaterial({
            color: 0xffff66,
            size: 1.2,
            depthTest: false,
            transparent: true,
            opacity: 0.95,
        });

        const tileMeshes: THREE.Object3D[] = [];
        tiles.forEach((tile) => {
            const geo = buildTileTriangleGeometry(tile);
            if (geo) {
                const mesh = new THREE.Mesh(geo, triMat);
                mesh.name = `bakedNavTile_${tile.tileId.chunkX}_${tile.tileId.chunkY}_${tile.tileId.layer}`;
                mesh.renderOrder = 200;
                tileMeshes.push(mesh);
            }

            const boundary = buildTileBoundaryLines(tile, boundaryMat);
            if (boundary) {
                boundary.renderOrder = 202;
                tileMeshes.push(boundary);
            }

            const portalLines = buildTilePortalLines(tile, portalMat);
            if (portalLines) {
                portalLines.renderOrder = 201;
                tileMeshes.push(portalLines);
            }

            const points = buildTileVertexPoints(tile, vertexMat);
            if (points) {
                points.renderOrder = 203;
                tileMeshes.push(points);
            }
        });

        for (let i = 0; i < tileMeshes.length; i++) group.add(tileMeshes[i]);
        return group;
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
        geo.computeVertexNormals();
        return geo;
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

    const buildTileVertexPoints = (tile: NavTile, mat: THREE.PointsMaterial) => {
        const vCount = tile.vertexXcm.length;
        if (vCount === 0) return null;
        const pos = new Float32Array(vCount * 3);
        let o = 0;
        for (let i = 0; i < vCount; i++) {
            pos[o++] = (tile.originXcm + tile.vertexXcm[i]) / 100.0;
            pos[o++] = (tile.vertexYcm[i]) / 100.0 + 0.12;
            pos[o++] = (tile.originZcm + tile.vertexZcm[i]) / 100.0;
        }
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        const pts = new THREE.Points(geo, mat);
        pts.name = `bakedNavVerts_${tile.tileId.chunkX}_${tile.tileId.chunkY}_${tile.tileId.layer}`;
        return pts;
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
        
        // Hex logic from editor_v2
        // r = Math.round((z - offsetZ) / ROW_SPACING)
        // c = Math.round((x - offsetX) / HEX_WIDTH - 0.5 * (r & 1))
        
        const r = Math.round(point.z / ROW_SPACING);
        const c = Math.round(point.x / HEX_WIDTH - 0.5 * (r & 1));
        
        return { c, r, point };
    };

    const applyBrush = (c: number, r: number) => {
        const size = brushSize;
        const range = size - 1;
        
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
                            const curH = terrain.getHeight(tc, tr);
                            let newH = curH;
                            if (activeMode === 'Set') newH = brushValue;
                            else if (activeMode === 'Raise') newH = Math.min(15, curH + 1); // Clamp to 15
                            else if (activeMode === 'Lower') newH = Math.max(0, curH - 1);
                            
                            if (newH !== curH) terrain.setHeight(tc, tr, newH);
                            break;
                        case 'Water':
                             // BUCKET TOOL: Fill Water
                             if (activeMode === 'Bucket') {
                                 // Target Water Height is current brush value (or 0 if erasing?)
                                 // If user holds Shift or something? No, let's just use brushValue.
                                 const targetWaterH = Math.min(15, brushValue); // Clamp to 15
                                 
                                 // Flood Fill Algorithm
                                 // Condition: Expand if (TerrainHeight < TargetWaterH)
                                 // Boundary: TerrainHeight >= TargetWaterH
                                 
                                 const startKey = `${tc},${tr}`;
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
                                         
                                         // Neighbors
                                         const neighbors = getNeighbors(c, r);
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
                             if (newW !== curW) terrain.setWater(tc, tr, newW);
                             break;
                        
                        case 'Territory':
                             const curT = terrain.getTerritory(tc, tr);
                             if (activeMode === 'Set') {
                                 if (curT !== brushValue) terrain.setTerritory(tc, tr, brushValue);
                             }
                             // Territory doesn't make sense to Raise/Lower usually, but maybe cycle IDs?
                             // Let's keep it simple: Set mode paints the ID.
                             break;
                        case 'Biome':
                            if (activeMode === 'Set') terrain.setBiome(tc, tr, brushValue);
                            break;
                        case 'Vegetation':
                             if (activeMode === 'Set') terrain.setVeg(tc, tr, brushValue);
                             break;
                        case 'Ramp':
                            // Support Raise/Lower as On/Off shortcut
                            let isRamp = terrain.isRamp(tc, tr);
                            if (activeMode === 'Set') isRamp = brushValue > 0;
                            else if (activeMode === 'Raise') isRamp = true;
                            else if (activeMode === 'Lower') isRamp = false;
                            
                            if (isRamp !== terrain.isRamp(tc, tr)) {
                                terrain.setRamp(tc, tr, isRamp);
                            }
                            break;
                        case 'Layers':
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
                            }
                            break;
                    }
                }
            }
        }
    };

    const handleMouseDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return; // Left Click Only
        isDraggingRef.current = true;
        const cell = getCellFromEvent(e.clientX, e.clientY);
        if (cell) {
            if (activeCategory === 'Entities') {
                if (activeMode === 'Set') placeEntityAt(cell.c, cell.r);
                else if (activeMode === 'Lower') removeEntityAt(cell.c, cell.r);
                else if (activeMode === 'Raise') selectEntityAt(cell.c, cell.r);
            } else {
                applyBrush(cell.c, cell.r);
            }
            lastDragCellRef.current = cell;
        }
    };

    const handleMouseMove = (e: React.MouseEvent) => {
        const cell = getCellFromEvent(e.clientX, e.clientY);
        
        // Update Cursor
        if (cursorMeshRef.current && cell) {
            const h = terrain.getHeight(cell.c, cell.r);
            const pos = getHexPosition(cell.c, cell.r, h, 2.0); // hScale=2
            cursorMeshRef.current.position.set(pos.x, pos.y + 0.2, pos.z);
            cursorMeshRef.current.scale.set(brushSize, brushSize, brushSize);
            cursorMeshRef.current.visible = true;
        } else if (cursorMeshRef.current) {
            cursorMeshRef.current.visible = false;
        }

        // Drag Paint
        if (isDraggingRef.current && cell) {
            if (!lastDragCellRef.current || lastDragCellRef.current.c !== cell.c || lastDragCellRef.current.r !== cell.r) {
                if (activeCategory === 'Entities') {
                    if (activeMode === 'Set') placeEntityAt(cell.c, cell.r);
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
        />
    );
};
