import * as THREE from 'three';
import { TerrainStore, CHUNK_SIZE } from '../Map/TerrainStore';
import { HEX_WIDTH, ROW_SPACING, getHexPosition } from '../Map/HexMetrics';

export type LogicTerrainViewMode = 'heightLevel' | 'surfaceFlags' | 'areaId' | 'combined';

/**
 * LogicTerrainView renders the logical terrain data (height levels, surface flags,
 * area IDs) as a color-coded overlay for level design inspection.
 *
 * This is the "Logic Terrain Editor" visualization mode — it shows what the
 * navmesh baker sees, not the visual terrain.
 */
export class LogicTerrainView {
    private store: TerrainStore;
    private mode: LogicTerrainViewMode = 'heightLevel';
    private opacity: number = 0.7;

    /** Area ID color palette (256 entries, deterministic hash) */
    private areaColorCache: THREE.Color[] = [];

    constructor(store: TerrainStore) {
        this.store = store;
        this.buildAreaPalette();
    }

    setMode(mode: LogicTerrainViewMode): void {
        this.mode = mode;
    }

    setOpacity(opacity: number): void {
        this.opacity = Math.max(0, Math.min(1, opacity));
    }

    /**
     * Build a per-chunk overlay group that color-codes logic terrain cells.
     * Returns a THREE.Group containing one colored quad per cell.
     */
    buildChunkOverlay(cx: number, cy: number): THREE.Group {
        const group = new THREE.Group();
        group.name = `logicTerrain_${cx}_${cy}`;

        const startC = cx * CHUNK_SIZE;
        const endC = Math.min((cx + 1) * CHUNK_SIZE, this.store.widthChunks * CHUNK_SIZE);
        const startR = cy * CHUNK_SIZE;
        const endR = Math.min((cy + 1) * CHUNK_SIZE, this.store.heightChunks * CHUNK_SIZE);

        // Build instanced quads for performance
        const quadsPerChunk = (endC - startC) * (endR - startR);
        if (quadsPerChunk <= 0) return group;

        const positions: number[] = [];
        const colors: number[] = [];
        const indices: number[] = [];

        let vertexIdx = 0;
        const hexWidth = HEX_WIDTH * 0.95; // slight inset to avoid z-fighting
        const rowSpacing = ROW_SPACING * 0.95;

        for (let r = startR; r < endR; r++) {
            for (let c = startC; c < endC; c++) {
                const h = this.store.getHeight(c, r);
                const pos = getHexPosition(c, r, h, 2.0);
                const color = this.getCellColor(c, r, h);

                // Create a small quad at the cell position, raised above terrain
                const yOffset = this.mode === 'combined' ? 0.15 : 0.08;
                const qy = pos.y + yOffset;
                const halfW = hexWidth / 2;
                const halfH = rowSpacing / 2;

                // 4 corners of the quad
                const baseIdx = vertexIdx;
                positions.push(
                    pos.x - halfW, qy, pos.z - halfH,  // 0: top-left
                    pos.x + halfW, qy, pos.z - halfH,  // 1: top-right
                    pos.x + halfW, qy, pos.z + halfH,  // 2: bottom-right
                    pos.x - halfW, qy, pos.z + halfH,  // 3: bottom-left
                );

                // Same color for all 4 vertices
                for (let v = 0; v < 4; v++) {
                    colors.push(color.r, color.g, color.b);
                }

                // Two triangles (0-1-2, 0-2-3)
                indices.push(
                    baseIdx, baseIdx + 1, baseIdx + 2,
                    baseIdx, baseIdx + 2, baseIdx + 3,
                );

                vertexIdx += 4;
            }
        }

        if (positions.length === 0) return group;

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        geo.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
        geo.setIndex(indices);
        geo.computeVertexNormals();

        const mat = new THREE.MeshBasicMaterial({
            vertexColors: true,
            transparent: true,
            opacity: this.opacity,
            side: THREE.DoubleSide,
            depthTest: true,
            depthWrite: false,
        });

        const mesh = new THREE.Mesh(geo, mat);
        mesh.renderOrder = 50; // above terrain, below navmesh
        group.add(mesh);

        return group;
    }

    /** Determine the color for a cell based on the current view mode. */
    private getCellColor(col: number, row: number, heightLevel: number): THREE.Color {
        switch (this.mode) {
            case 'heightLevel':
                return this.heightLevelColor(heightLevel);
            case 'surfaceFlags':
                return this.surfaceFlagsColor(col, row);
            case 'areaId':
                return this.areaIdColor(col, row);
            case 'combined':
                return this.combinedColor(col, row, heightLevel);
        }
    }

    private heightLevelColor(h: number): THREE.Color {
        // 0=dark blue (low), 15=bright red (high)
        const t = h / 15;
        const r = t;
        const g = 0.2 + 0.3 * (1 - Math.abs(t - 0.5) * 2);
        const b = 1 - t;
        return new THREE.Color(r, g, b);
    }

    private surfaceFlagsColor(col: number, row: number): THREE.Color {
        const blocked = this.store.getBlocked(col, row);
        const isRamp = this.store.isRamp(col, row);
        const water = this.store.getWater(col, row);
        const h = this.store.getHeight(col, row);

        if (blocked) return new THREE.Color(1, 0.1, 0.1); // Red
        if (water > h) return new THREE.Color(0.1, 0.4, 1.0); // Blue (submerged)
        if (isRamp) return new THREE.Color(1, 0.9, 0.1); // Yellow
        return new THREE.Color(0.2, 0.7, 0.2); // Green (walkable)
    }

    private areaIdColor(col: number, row: number): THREE.Color {
        const areaId = this.store.getAreaId(col, row);
        if (areaId <= 0) return new THREE.Color(0.5, 0.5, 0.5); // Gray for area 0
        return this.getAreaColor(areaId);
    }

    private combinedColor(col: number, row: number, h: number): THREE.Color {
        // Combined: base=heightLevel, overlay tint from surface flags
        const base = this.heightLevelColor(h);
        const blocked = this.store.getBlocked(col, row);
        if (blocked) {
            // Red stripes via darker red tint
            return new THREE.Color(0.8, 0.15, 0.15);
        }
        const water = this.store.getWater(col, row);
        if (water > h) {
            return new THREE.Color(0.15, 0.3, 0.9);
        }
        const isRamp = this.store.isRamp(col, row);
        if (isRamp) {
            return new THREE.Color(0.9, 0.8, 0.1);
        }
        return base;
    }

    private getAreaColor(areaId: number): THREE.Color {
        if (!this.areaColorCache[areaId]) {
            // Deterministic color from areaId
            let hash = areaId * 2654435761;
            const r = ((hash >> 16) & 0xff) / 255;
            const g = ((hash >> 8) & 0xff) / 255;
            const b = (hash & 0xff) / 255;
            // Ensure colors are vibrant
            const maxC = Math.max(r, g, b);
            const scale = maxC < 0.3 ? 0.3 / Math.max(maxC, 0.001) : 1;
            this.areaColorCache[areaId] = new THREE.Color(
                Math.min(1, r * scale + 0.1),
                Math.min(1, g * scale + 0.1),
                Math.min(1, b * scale + 0.1),
            );
        }
        return this.areaColorCache[areaId];
    }

    private buildAreaPalette(): void {
        // Pre-compute colors for area IDs 0-63 (common range)
        for (let i = 0; i < 64; i++) {
            this.getAreaColor(i);
        }
    }
}
