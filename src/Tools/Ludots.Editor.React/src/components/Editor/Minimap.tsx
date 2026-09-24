import React, { useEffect, useRef, useState } from 'react';
import { useEditorStore } from './EditorStore';
import * as THREE from 'three';
import { getMapWorldSizeM } from '../../Core/Map/TopologyMetrics';
import { getTerrainDisplayByteRgb, type TerrainVisualOptions } from '../../Core/Render/TerrainVisualStyle';

type MinimapProps = {
    embedded?: boolean;
    className?: string;
};

const MINIMAP_LONG_EDGE_PX = 256;

function paintTerrainPixel(data: Uint8ClampedArray, index: number, height: number, water: number, biome: number, visualOptions: TerrainVisualOptions) {
    const [r, g, b] = getTerrainDisplayByteRgb(height, water, biome, visualOptions);
    data[index] = r;
    data[index + 1] = g;
    data[index + 2] = b;
    data[index + 3] = 255;
}

export const Minimap: React.FC<MinimapProps> = ({ embedded = false, className = '' }) => {
    const terrainCanvasRef = useRef<HTMLCanvasElement>(null);
    const overlayCanvasRef = useRef<HTMLCanvasElement>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    
    const { terrain, boardMetrics, activeCategory, cameraRef, controlsRef, navDirtyChunks, canvasSessionKind, terrainViewMode, terrainHeightContrast } = useEditorStore();
    const hasCanvasSession = canvasSessionKind === 'local' || canvasSessionKind === 'repo';
    const terrainVisualOptions = React.useMemo<TerrainVisualOptions>(() => ({
        terrainViewMode,
        heightContrast: terrainHeightContrast,
    }), [terrainViewMode, terrainHeightContrast]);
    const [isDragging, setIsDragging] = useState(false);
    const [cameraInfo, setCameraInfo] = useState('camera --');
    const chunkAspectRatio = React.useMemo(() => {
        if (!hasCanvasSession || terrain.widthChunks <= 0 || terrain.heightChunks <= 0) return 1;
        return terrain.widthChunks / terrain.heightChunks;
    }, [hasCanvasSession, terrain.widthChunks, terrain.heightChunks]);
    const minimapCanvasWidth = chunkAspectRatio >= 1
        ? MINIMAP_LONG_EDGE_PX
        : Math.max(1, Math.round(MINIMAP_LONG_EDGE_PX * chunkAspectRatio));
    const minimapCanvasHeight = chunkAspectRatio >= 1
        ? Math.max(1, Math.round(MINIMAP_LONG_EDGE_PX / chunkAspectRatio))
        : MINIMAP_LONG_EDGE_PX;

    // 1. Terrain Render (Cached)
    useEffect(() => {
        const canvas = terrainCanvasRef.current;
        if (!canvas) return;
        
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const w = canvas.width;
        const h = canvas.height;
        if (!hasCanvasSession) {
            ctx.clearRect(0, 0, w, h);
            return;
        }
        
        // Full Redraw if terrain size changes or first load
        // But for MVP let's just redraw fully on change for simplicity
        // Ideally we only redraw dirty chunks.
        
        const chunkCells = boardMetrics.chunkSizeCells;
        const mapW = terrain.widthChunks * chunkCells;
        const mapH = terrain.heightChunks * chunkCells;
        const scaleX = w / mapW;
        const scaleY = h / mapH;

        const imgData = ctx.getImageData(0, 0, w, h);
        const data = imgData.data;

        // We can optimize this by only scanning dirty chunks if we track them per-pixel area
        // For now, full scan is fast enough for small maps (200x200 pixels = 40k pixels)
        
        for (let y = 0; y < h; y++) {
            for (let x = 0; x < w; x++) {
                const mx = Math.floor(x / scaleX);
                const my = Math.floor(y / scaleY);
                
                if (mx >= mapW || my >= mapH) continue;

                const index = (y * w + x) * 4;
                const height = terrain.getHeight(mx, my);
                const water = terrain.getWater(mx, my);
                const biome = terrain.getBiome(mx, my);
                paintTerrainPixel(data, index, height, water, biome, terrainVisualOptions);
            }
        }
        ctx.putImageData(imgData, 0, 0);

        // Chunk Grid
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.1)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        const chunkW = chunkCells * scaleX;
        const chunkH = chunkCells * scaleY;
        for(let cy=0; cy<=terrain.heightChunks; cy++) {
            const y = cy * chunkH;
            ctx.moveTo(0, y); ctx.lineTo(w, y);
        }
        for(let cx=0; cx<=terrain.widthChunks; cx++) {
            const x = cx * chunkW;
            ctx.moveTo(x, 0); ctx.lineTo(x, h);
        }
        ctx.stroke();

    }, [terrain, terrain.widthChunks, terrain.heightChunks, boardMetrics, hasCanvasSession, terrainVisualOptions, minimapCanvasWidth, minimapCanvasHeight]); // Redraw on load/resize

    // 2. Animation Loop (Overlay: Camera + Dirty + Interaction)
    useEffect(() => {
        const overlay = overlayCanvasRef.current;
        const terrainCanvas = terrainCanvasRef.current;
        if (!overlay || !terrainCanvas) return;
        
        const ctxOverlay = overlay.getContext('2d');
        const ctxTerrain = terrainCanvas.getContext('2d');
        if (!ctxOverlay || !ctxTerrain) return;

        let frameId = 0;
        const w = overlay.width;
        const h = overlay.height;
        
        const worldSize = getMapWorldSizeM(terrain.widthChunks, terrain.heightChunks, boardMetrics);
        const mapWorldW = worldSize.width;
        const mapWorldH = worldSize.height;
        const scaleX = w / mapWorldW;
        const scaleY = h / mapWorldH;
        const chunkCells = boardMetrics.chunkSizeCells;
        const mapCellsW = terrain.widthChunks * chunkCells;
        const mapCellsH = terrain.heightChunks * chunkCells;
        const cellScaleX = w / mapCellsW;
        const cellScaleY = h / mapCellsH;

        let lastCameraLabelAt = 0;

        const renderLoop = () => {
            const { minimapDirtyChunks, clearMinimapDirty, cameraRef } = useEditorStore.getState();

            ctxOverlay.clearRect(0, 0, w, h);
            if (!hasCanvasSession) {
                frameId = requestAnimationFrame(renderLoop);
                return;
            }

            // A. Process Dirty Chunks (Update Terrain Canvas + Draw Highlight)
            if (minimapDirtyChunks.size > 0) {
                ctxOverlay.fillStyle = 'rgba(255, 50, 50, 0.5)';
                
                minimapDirtyChunks.forEach(key => {
                    const [cx, cy] = key.split(',').map(Number); // key is "cx,cy"
                    
                    // 1. Update Pixels on Terrain Canvas
                    // Define area on canvas
                    const cxPx = Math.floor(cx * chunkCells * cellScaleX);
                    const cyPx = Math.floor(cy * chunkCells * cellScaleY);
                    const cwPx = Math.ceil(chunkCells * cellScaleX);
                    const chPx = Math.ceil(chunkCells * cellScaleY);

                    // We need to re-scan the terrain data for this chunk
                    // Mapping pixels back to terrain cells is tricky due to scaling.
                    // Simpler approach: Iterate the pixels in the target rect and sample terrain.
                    
                    const imgData = ctxTerrain.getImageData(cxPx, cyPx, cwPx, chPx);
                    const data = imgData.data;
                    
                    for (let y = 0; y < chPx; y++) {
                        for (let x = 0; x < cwPx; x++) {
                            // Canvas pixel coord
                            const px = cxPx + x;
                            const py = cyPx + y;
                            
                            // Map to Terrain Coord
                            const mx = Math.floor(px / cellScaleX);
                            const my = Math.floor(py / cellScaleY);
                            
                            if (mx >= mapCellsW || my >= mapCellsH) continue;

                            const index = (y * cwPx + x) * 4;
                            
                            const height = terrain.getHeight(mx, my);
                            const water = terrain.getWater(mx, my);
                            const biome = terrain.getBiome(mx, my);
                            paintTerrainPixel(data, index, height, water, biome, terrainVisualOptions);
                        }
                    }
                    ctxTerrain.putImageData(imgData, cxPx, cyPx);

                    // 2. Draw Highlight on Overlay
                    ctxOverlay.fillRect(cxPx, cyPx, cwPx, chPx);
                });
                
                // Clear dirty flags after processing
                clearMinimapDirty();
            }

            // B. Draw Camera Frustum
            const cam = cameraRef.current;
            if (cam) {
                const now = performance.now();
                if (now - lastCameraLabelAt > 250) {
                    lastCameraLabelAt = now;
                    setCameraInfo(`camera ${cam.position.x.toFixed(1)}, ${cam.position.z.toFixed(1)}`);
                }

                // Project camera frustum to ground plane (y=0)
                // Simplified: Just project 4 corners of screen if possible, 
                // or just camera position + target for now.
                // Accurate Frustum on ground:
                // Unproject (0,0), (1,0), (1,1), (0,1) with z=depth? No.
                // Raycast from camera to ground plane at 4 screen corners.
                
                const corners = [
                    new THREE.Vector3(-1, 1, 0.5), // Top Left
                    new THREE.Vector3(1, 1, 0.5),  // Top Right
                    new THREE.Vector3(1, -1, 0.5), // Bottom Right
                    new THREE.Vector3(-1, -1, 0.5) // Bottom Left
                ];
                
                const groundPoints: {x: number, y: number}[] = [];
                const raycaster = new THREE.Raycaster();
                const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0); // y=0 plane
                
                corners.forEach(ndc => {
                    raycaster.setFromCamera(new THREE.Vector2(ndc.x, ndc.y), cam);
                    const target = new THREE.Vector3();
                    const hit = raycaster.ray.intersectPlane(plane, target);
                    if (hit) {
                        groundPoints.push({
                            x: target.x * scaleX,
                            y: target.z * scaleY
                        });
                    }
                });

                if (groundPoints.length === 4) {
                    ctxOverlay.beginPath();
                    ctxOverlay.moveTo(groundPoints[0].x, groundPoints[0].y);
                    ctxOverlay.lineTo(groundPoints[1].x, groundPoints[1].y);
                    ctxOverlay.lineTo(groundPoints[2].x, groundPoints[2].y);
                    ctxOverlay.lineTo(groundPoints[3].x, groundPoints[3].y);
                    ctxOverlay.closePath();
                    ctxOverlay.strokeStyle = 'rgba(255, 255, 255, 0.95)';
                    ctxOverlay.lineWidth = 2;
                    ctxOverlay.stroke();
                    ctxOverlay.fillStyle = 'rgba(255, 255, 255, 0.1)';
                    ctxOverlay.fill();
                }
            }

            frameId = requestAnimationFrame(renderLoop);
        };
        
        frameId = requestAnimationFrame(renderLoop);
        return () => cancelAnimationFrame(frameId);
    }, [terrain, boardMetrics, hasCanvasSession, terrainVisualOptions, minimapCanvasWidth, minimapCanvasHeight]);

    // 3. Interaction
    const handlePointer = (e: React.PointerEvent) => {
        if (!hasCanvasSession) return;
        if (!isDragging && e.type !== 'pointerdown') return;
        if (e.type === 'pointerdown') setIsDragging(true);
        if (e.type === 'pointerup' || e.type === 'pointerleave') {
            setIsDragging(false);
            return;
        }

        const rect = overlayCanvasRef.current?.getBoundingClientRect();
        if (!rect) return;

        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;
        
        const worldSize = getMapWorldSizeM(terrain.widthChunks, terrain.heightChunks, boardMetrics);
        const mapWorldW = worldSize.width;
        const mapWorldH = worldSize.height;
        
        // Convert canvas pos to world pos
        const worldX = (x / rect.width) * mapWorldW;
        const worldZ = (y / rect.height) * mapWorldH;

        // Move Camera
        const controls = controlsRef.current;
        const camera = cameraRef.current;
        
        if (controls && camera) {
            const offset = new THREE.Vector3().subVectors(camera.position, controls.target);
            controls.target.set(worldX, 0, worldZ);
            camera.position.copy(controls.target).add(offset);
            controls.update();
        }
    };

    return (
        <div
            ref={containerRef}
            className={`${embedded ? 'w-full' : 'absolute right-4 top-4 z-40 w-[260px]'} select-none rounded-lg border border-slate-700/80 bg-slate-950/90 p-3 text-slate-100 shadow-2xl backdrop-blur-md ${className}`}
        >
            <div className="mb-2 flex items-start justify-between gap-2">
                <div>
                    <div className="text-[10px] font-semibold uppercase tracking-[0.16em] text-slate-500">Minimap</div>
                    <div className="text-xs text-slate-300">{hasCanvasSession ? `${terrain.widthChunks}x${terrain.heightChunks} chunks` : 'no board open'}</div>
                </div>
                <div className="rounded border border-amber-700/60 bg-amber-950/40 px-2 py-1 text-[10px] text-amber-100">
                    dirty {navDirtyChunks.size}
                </div>
            </div>
            <div
                className="relative w-full overflow-hidden rounded border border-slate-700 bg-black"
                style={{ aspectRatio: `${terrain.widthChunks || 1} / ${terrain.heightChunks || 1}` }}
            >
                <canvas
                    ref={terrainCanvasRef}
                    width={minimapCanvasWidth}
                    height={minimapCanvasHeight}
                    className="absolute left-0 top-0 h-full w-full"
                />
                <canvas
                    ref={overlayCanvasRef}
                    width={minimapCanvasWidth}
                    height={minimapCanvasHeight}
                    className={`absolute left-0 top-0 z-10 h-full w-full ${hasCanvasSession ? 'cursor-crosshair' : 'cursor-not-allowed'}`}
                    onPointerDown={handlePointer}
                    onPointerMove={handlePointer}
                    onPointerUp={handlePointer}
                    onPointerLeave={handlePointer}
                />
                {!hasCanvasSession ? (
                    <div className="absolute inset-0 flex items-center justify-center bg-black/70 px-4 text-center text-[11px] text-slate-400">
                        Open a board to enable minimap.
                    </div>
                ) : null}
                <div className="pointer-events-none absolute bottom-1 right-1 rounded bg-black/60 px-1 text-[10px] text-slate-300">
                    {hasCanvasSession ? boardMetrics.topology : 'empty'}
                </div>
            </div>
            <div className="mt-2 flex items-center justify-between gap-2 text-[10px] text-slate-400">
                <span>{hasCanvasSession ? cameraInfo : 'canvas empty'}</span>
                <span>{activeCategory}</span>
            </div>
        </div>
    );
};
