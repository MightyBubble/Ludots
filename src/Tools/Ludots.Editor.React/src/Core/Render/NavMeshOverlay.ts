import * as THREE from 'three';
import { TerrainStore, CHUNK_SIZE } from '../Map/TerrainStore';
import { HEX_WIDTH, ROW_SPACING, getHexPosition } from '../Map/HexMetrics';
import type { NavTile } from '../NavMesh/NavTileBinary';

/** Area color palette for deterministic area ID visualization */
const AREA_COLORS: number[] = [
    0x4caf50, // 0: Green
    0x2196f3, // 1: Blue
    0xff9800, // 2: Orange
    0xf44336, // 3: Red
    0x9c27b0, // 4: Purple
    0x00bcd4, // 5: Cyan
    0xffeb3b, // 6: Yellow
    0x795548, // 7: Brown
    0x607d8b, // 8: Blue-grey
    0xe91e63, // 9: Pink
    0x3f51b5, // 10: Indigo
    0x8bc34a, // 11: Light green
    0xff5722, // 12: Deep orange
    0x009688, // 13: Teal
    0x673ab7, // 14: Deep purple
    0xcddc39, // 15: Lime
];

function getAreaColorHex(areaId: number): number {
    return AREA_COLORS[areaId % AREA_COLORS.length];
}

/**
 * NavMeshOverlay builds renderable geometry for:
 * 1. Dirty chunk highlighting (wireframe borders around edited chunks)
 * 2. Baked NavTile visualization (area-colored triangles, boundaries, portals)
 * 3. Bake progress indicators
 */
export class NavMeshOverlay {
    private store: TerrainStore;

    /** Cached dirty border line material */
    private dirtyBorderMat: THREE.LineBasicMaterial;

    constructor(store: TerrainStore) {
        this.store = store;
        this.dirtyBorderMat = new THREE.LineBasicMaterial({
            color: 0xff4444,
            transparent: true,
            opacity: 0.8,
            depthTest: false,
        });
    }

    /**
     * Build a wireframe border around a dirty chunk to highlight it.
     */
    buildDirtyChunkBorder(cx: number, cy: number): THREE.LineSegments {
        const startC = cx * CHUNK_SIZE;
        const endC = Math.min((cx + 1) * CHUNK_SIZE, this.store.widthChunks * CHUNK_SIZE) - 1;
        const startR = cy * CHUNK_SIZE;
        const endR = Math.min((cy + 1) * CHUNK_SIZE, this.store.heightChunks * CHUNK_SIZE) - 1;

        // Get corner positions at slightly elevated height
        const corners = [
            getHexPosition(startC, startR, 0, 2.0),
            getHexPosition(endC, startR, 0, 2.0),
            getHexPosition(endC, endR, 0, 2.0),
            getHexPosition(startC, endR, 0, 2.0),
        ];

        const yOffset = 0.3;
        const points: number[] = [];
        for (let i = 0; i < 4; i++) {
            const curr = corners[i];
            const next = corners[(i + 1) % 4];
            points.push(
                curr.x, curr.y + yOffset, curr.z,
                next.x, next.y + yOffset, next.z,
            );
        }

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
        return new THREE.LineSegments(geo, this.dirtyBorderMat);
    }

    /** Get world-space bounds for a chunk (used for frustum culling dirty borders). */
    getChunkWorldBounds(cx: number, cy: number): { minX: number; minZ: number; maxX: number; maxZ: number } {
        const startC = cx * CHUNK_SIZE;
        const endC = Math.min((cx + 1) * CHUNK_SIZE, this.store.widthChunks * CHUNK_SIZE) - 1;
        const startR = cy * CHUNK_SIZE;
        const endR = Math.min((cy + 1) * CHUNK_SIZE, this.store.heightChunks * CHUNK_SIZE) - 1;

        const tl = getHexPosition(startC, startR, 0, 2.0);
        const br = getHexPosition(endC, endR, 0, 2.0);
        return {
            minX: Math.min(tl.x, br.x),
            minZ: Math.min(tl.z, br.z),
            maxX: Math.max(tl.x, br.x),
            maxZ: Math.max(tl.z, br.z),
        };
    }
}

/**
 * Build a Three.js geometry for a single NavTile with area-based vertex coloring.
 */
export function buildTileTriangleGeometry(tile: NavTile): THREE.BufferGeometry | null {
    if (tile.triA.length === 0) return null;

    const vCount = tile.triA.length * 3;
    const positions = new Float32Array(vCount * 3);
    const colors = new Float32Array(vCount * 3);

    for (let i = 0; i < tile.triA.length; i++) {
        const a = tile.triA[i];
        const b = tile.triB[i];
        const c = tile.triC[i];

        const ax = tile.vertexXcm[a] / 100;
        const ay = tile.vertexYcm[a] / 100;
        const az = tile.vertexZcm[a] / 100;
        const bx = tile.vertexXcm[b] / 100;
        const by = tile.vertexYcm[b] / 100;
        const bz = tile.vertexZcm[b] / 100;
        const cx = tile.vertexXcm[c] / 100;
        const cy = tile.vertexYcm[c] / 100;
        const cz = tile.vertexZcm[c] / 100;

        const base = i * 9;
        positions[base] = ax; positions[base + 1] = ay; positions[base + 2] = az;
        positions[base + 3] = bx; positions[base + 4] = by; positions[base + 5] = bz;
        positions[base + 6] = cx; positions[base + 7] = cy; positions[base + 8] = cz;

        // Area-based coloring
        const areaId = tile.triAreaIds?.[i] ?? 0;
        const color = new THREE.Color(getAreaColorHex(areaId));
        const cr = color.r, cg = color.g, cb = color.b;
        colors[base] = cr; colors[base + 1] = cg; colors[base + 2] = cb;
        colors[base + 3] = cr; colors[base + 4] = cg; colors[base + 5] = cb;
        colors[base + 6] = cr; colors[base + 7] = cg; colors[base + 8] = cb;
    }

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geo.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
    geo.computeVertexNormals();
    return geo;
}

/**
 * Build tile boundary lines from edge detection on triangle data.
 * Draws a single line loop around the tile's walkable area.
 */
export function buildTileBoundaryLines(tile: NavTile, mat?: THREE.LineBasicMaterial): THREE.LineSegments | null {
    // Collect all triangle edges, count occurrences
    const edgeMap = new Map<string, number>();
    const edgeVertices = new Map<string, [number, number, number, number, number, number]>();

    for (let i = 0; i < tile.triA.length; i++) {
        const tri = [tile.triA[i], tile.triB[i], tile.triC[i]];
        for (let j = 0; j < 3; j++) {
            const v0 = tri[j];
            const v1 = tri[(j + 1) % 3];
            const key = v0 < v1 ? `${v0},${v1}` : `${v1},${v0}`;
            edgeMap.set(key, (edgeMap.get(key) ?? 0) + 1);
            if (!edgeVertices.has(key)) {
                const s = v0 < v1 ? v0 : v1;
                const e = v0 < v1 ? v1 : v0;
                edgeVertices.set(key, [
                    tile.vertexXcm[s] / 100, tile.vertexYcm[s] / 100, tile.vertexZcm[s] / 100,
                    tile.vertexXcm[e] / 100, tile.vertexYcm[e] / 100, tile.vertexZcm[e] / 100,
                ]);
            }
        }
    }

    // Boundary edges are those appearing exactly once
    const boundaryEdges: number[][] = [];
    edgeMap.forEach((count, key) => {
        if (count === 1) {
            const verts = edgeVertices.get(key);
            if (verts) boundaryEdges.push(verts);
        }
    });

    if (boundaryEdges.length === 0) return null;

    const points: number[] = [];
    for (const [x1, y1, z1, x2, y2, z2] of boundaryEdges) {
        points.push(x1, y1 + 0.02, z1, x2, y2 + 0.02, z2);
    }

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
    return new THREE.LineSegments(geo, mat ?? new THREE.LineBasicMaterial({ color: 0x00e5ff }));
}

/**
 * Build portal visualization lines between adjacent tiles.
 */
export function buildTilePortalLines(tile: NavTile, mat?: THREE.LineBasicMaterial): THREE.LineSegments | null {
    if (!tile.portals || tile.portals.length === 0) return null;

    const points: number[] = [];
    for (const p of tile.portals) {
        const x1 = p.leftXcm / 100;
        const z1 = p.leftZcm / 100;
        const x2 = p.rightXcm / 100;
        const z2 = p.rightZcm / 100;
        // Use tile origin as Y reference
        const y = tile.originXcm === undefined ? 0 : 0.05;
        points.push(x1, y, z1, x2, y, z2);
    }

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
    return new THREE.LineSegments(geo, mat ?? new THREE.LineBasicMaterial({ color: 0xffaa00 }));
}

/**
 * Build vertex point cloud for tile vertices (debug visualization).
 */
export function buildTileVertexPoints(tile: NavTile, mat?: THREE.PointsMaterial): THREE.Points | null {
    if (tile.vertexXcm.length === 0) return null;

    const positions = new Float32Array(tile.vertexXcm.length * 3);
    for (let i = 0; i < tile.vertexXcm.length; i++) {
        positions[i * 3] = tile.vertexXcm[i] / 100;
        positions[i * 3 + 1] = tile.vertexYcm[i] / 100;
        positions[i * 3 + 2] = tile.vertexZcm[i] / 100;
    }

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    return new THREE.Points(geo, mat ?? new THREE.PointsMaterial({ color: 0xffff66, size: 0.5 }));
}
