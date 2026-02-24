import React, { useEffect, useRef, useState } from 'react';
import { useEditorStore } from './EditorStore';
import * as THREE from 'three';
import { HEX_WIDTH, ROW_SPACING } from '../../Core/Map/HexMetrics';

export const Minimap: React.FC = () => {
    const terrainCanvasRef = useRef<HTMLCanvasElement>(null);
    const overlayCanvasRef = useRef<HTMLCanvasElement>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    
    const { terrain, activeCategory, cameraRef, controlsRef } = useEditorStore();
    const [isDragging, setIsDragging] = useState(false);

    // 1. Terrain Render (Cached)
    useEffect(() => {
        const canvas = terrainCanvasRef.current;
        if (!canvas) return;
        
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const w = canvas.width;
        const h = canvas.height;
        
        // Full Redraw if terrain size changes or first load
        // But for MVP let's just redraw fully on change for simplicity
        // Ideally we only redraw dirty chunks.
        
        const mapW = terrain.widthChunks * 64;
        const mapH = terrain.heightChunks * 64;
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
                let r=0, g=0, b=0;
                
                const height = terrain.getHeight(mx, my);
                const water = terrain.getWater(mx, my);
                
                if (water > 0) {
                    r = 0; g = 100 + water * 10; b = 200 + water * 5;
                } else {
                    const val = height * 16;
                    r = val; g = val + 20; b = val; // Greenish
                    
                    const biome = terrain.getBiome(mx, my);
                    if (biome === 1) { r += 50; b += 20; } 
                    if (biome === 2) { g += 40; } 
                }
                
                data[index] = r;
                data[index+1] = g;
                data[index+2] = b;
                data[index+3] = 255;
            }
        }
        ctx.putImageData(imgData, 0, 0);

        // Chunk Grid
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.1)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        const chunkW = 64 * scaleX;
        const chunkH = 64 * scaleY;
        for(let cy=0; cy<=terrain.heightChunks; cy++) {
            const y = cy * chunkH;
            ctx.moveTo(0, y); ctx.lineTo(w, y);
        }
        for(let cx=0; cx<=terrain.widthChunks; cx++) {
            const x = cx * chunkW;
            ctx.moveTo(x, 0); ctx.lineTo(x, h);
        }
        ctx.stroke();

    }, [terrain, terrain.widthChunks, terrain.heightChunks]); // Redraw on load/resize

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
        
        const mapWorldW = terrain.widthChunks * 64 * HEX_WIDTH;
        const mapWorldH = terrain.heightChunks * 64 * ROW_SPACING;
        const scaleX = w / mapWorldW;
        const scaleY = h / mapWorldH;

        const renderLoop = () => {
            const { minimapDirtyChunks, clearMinimapDirty, cameraRef, controlsRef } = useEditorStore.getState();

            ctxOverlay.clearRect(0, 0, w, h);

            // A. Process Dirty Chunks (Update Terrain Canvas + Draw Highlight)
            if (minimapDirtyChunks.size > 0) {
                ctxOverlay.fillStyle = 'rgba(255, 50, 50, 0.5)';
                
                minimapDirtyChunks.forEach(key => {
                    const [cx, cy] = key.split(',').map(Number); // key is "cx,cy"
                    
                    // 1. Update Pixels on Terrain Canvas
                    // Define area on canvas
                    const cxPx = Math.floor(cx * 64 * HEX_WIDTH * scaleX);
                    const cyPx = Math.floor(cy * 64 * ROW_SPACING * scaleY);
                    const cwPx = Math.ceil(64 * HEX_WIDTH * scaleX);
                    const chPx = Math.ceil(64 * ROW_SPACING * scaleY);

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
                            const mx = Math.floor(px / scaleX);
                            const my = Math.floor(py / scaleY);
                            
                            if (mx >= terrain.widthChunks * 64 || my >= terrain.heightChunks * 64) continue;

                            const index = (y * cwPx + x) * 4;
                            
                            const height = terrain.getHeight(mx, my);
                            const water = terrain.getWater(mx, my);
                            const biome = terrain.getBiome(mx, my);
                            
                            let r=0, g=0, b=0;
                            if (water > 0) {
                                r = 0; g = 100 + water * 10; b = 200 + water * 5;
                            } else {
                                const val = height * 16;
                                r = val; g = val + 20; b = val;
                                if (biome === 1) { r += 50; b += 20; } 
                                if (biome === 2) { g += 40; } 
                            }
                            
                            data[index] = r;
                            data[index+1] = g;
                            data[index+2] = b;
                            data[index+3] = 255;
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
                    ctxOverlay.strokeStyle = 'white';
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
    }, [terrain]);

    // 3. Interaction
    const handlePointer = (e: React.PointerEvent) => {
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
        
        const mapWorldW = terrain.widthChunks * 64 * HEX_WIDTH;
        const mapWorldH = terrain.heightChunks * 64 * ROW_SPACING;
        
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
            className="absolute top-4 right-4 bg-gray-900 border border-gray-700 shadow-lg rounded p-1 select-none"
            style={{ width: 200, height: 200 }}
        >
            <canvas 
                ref={terrainCanvasRef} 
                width={200} 
                height={200} 
                className="absolute top-1 left-1"
            />
            <canvas 
                ref={overlayCanvasRef}
                width={200} 
                height={200} 
                className="absolute top-1 left-1 cursor-crosshair z-10"
                onPointerDown={handlePointer}
                onPointerMove={handlePointer}
                onPointerUp={handlePointer}
                onPointerLeave={handlePointer}
            />
            <div className="absolute bottom-1 right-1 text-[10px] text-gray-400 bg-black/50 px-1 rounded pointer-events-none">
                {terrain.widthChunks}x{terrain.heightChunks}
            </div>
        </div>
    );
};
