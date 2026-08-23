import * as THREE from 'three';
import { TerrainStore, CHUNK_SIZE } from '../Map/TerrainStore';
import { HEX_WIDTH, ROW_SPACING } from '../Map/HexMetrics';
import { ViewFrustum } from './ViewFrustum';
import { ChunkLRUCache } from './ChunkLRUCache';
import { ChunkRenderer } from './ChunkRenderer';

/**
 * ChunkStreamer manages which terrain chunks are visible and loaded in the 3D scene.
 *
 * Large-world contract:
 * - Only chunks within the camera frustum are added to the scene.
 * - An LRU cache holds recently-built chunk meshes (configurable max).
 * - Dirty chunks are rebuilt on next visibility check.
 * - Chunks outside the frustum are removed from scene but may stay in LRU cache.
 */
export class ChunkStreamer {
    /** Chunks currently attached to the scene group */
    private sceneChunks: Set<string> = new Set();

    /** Precomputed chunk world-space AABBs */
    private chunkAABBs: Map<string, { cx: number; cy: number; minX: number; minY: number; minZ: number; maxX: number; maxY: number; maxZ: number }> = new Map();

    /** Reference to the scene's terrain group */
    private terrainGroup: THREE.Group;

    /** Reference to the terrain data store */
    private store: TerrainStore;

    /** Frustum instance (updated each frame) */
    private frustum: ViewFrustum;

    /** LRU mesh cache */
    lruCache: ChunkLRUCache;

    /** Chunk renderer for building mesh geometry */
    private chunkRenderer: ChunkRenderer;

    /** How many extra chunks to load beyond the frustum (padding) */
    private viewPadding: number = 1;

    /** Maximum chunks in LRU cache */
    private maxCacheEntries: number = 256;

    /** Stats for performance panel */
    stats = {
        visibleChunks: 0,
        totalChunks: 0,
        cacheSize: 0,
        cacheHitRate: 0,
        frameBuildCount: 0,
        frameEvictCount: 0,
    };

    constructor(
        store: TerrainStore,
        terrainGroup: THREE.Group,
        camera: THREE.PerspectiveCamera,
        chunkRenderer: ChunkRenderer,
        maxCacheEntries: number = 256,
    ) {
        this.store = store;
        this.terrainGroup = terrainGroup;
        this.frustum = new ViewFrustum(camera);
        this.chunkRenderer = chunkRenderer;
        this.maxCacheEntries = maxCacheEntries;
        this.lruCache = new ChunkLRUCache(maxCacheEntries);
        this.precomputeChunkAABBs();
    }

    /** Recompute all chunk AABBs (call when store dimensions change). */
    precomputeChunkAABBs(): void {
        this.chunkAABBs.clear();

        const cellWidth = HEX_WIDTH;
        const cellHeight = ROW_SPACING;

        for (let cy = 0; cy < this.store.heightChunks; cy++) {
            for (let cx = 0; cx < this.store.widthChunks; cx++) {
                // Compute world-space bounds for this chunk
                // Hex coords: col in [cx*CHUNK_SIZE, (cx+1)*CHUNK_SIZE)
                const startCol = cx * CHUNK_SIZE;
                const endCol = Math.min((cx + 1) * CHUNK_SIZE, this.store.widthChunks * CHUNK_SIZE);
                const startRow = cy * CHUNK_SIZE;
                const endRow = Math.min((cy + 1) * CHUNK_SIZE - 1, this.store.heightChunks * CHUNK_SIZE - 1);

                // Approximate world AABB using hex cell dimensions
                const offsetX = startCol * cellWidth * 0.75;
                const offsetZ = startRow * cellHeight + (startCol % 2) * cellHeight * 0.5;

                const minX = startCol * cellWidth * 0.75 - cellWidth;
                const maxX = endCol * cellWidth * 0.75 + cellWidth;
                const minZ = startRow * cellHeight - cellHeight * 2;
                const maxZ = endRow * cellHeight + cellHeight * 2;

                // Height range: logic terrain max height is 15 * heightStep (2 world units per level)
                const minY = 0;
                const maxY = 15 * 2 + 2; // max terrain height plus padding

                this.chunkAABBs.set(`${cx},${cy}`, {
                    cx, cy,
                    minX, minY, minZ,
                    maxX, maxY, maxZ,
                });
            }
        }
    }

    /** Update frustum from camera and sync scene. Call every frame. */
    update(): void {
        this.frustum.update();
        this.stats.frameBuildCount = 0;
        this.stats.frameEvictCount = 0;

        // Find which chunks should be visible
        const allAABBs = Array.from(this.chunkAABBs.values());
        const visibleKeys = this.frustum.cullChunks(allAABBs);

        // Expand by padding neighbors
        const expandedVisible = new Set<string>();
        for (const key of visibleKeys) {
            const [cxStr, cyStr] = key.split(',');
            const cx = parseInt(cxStr);
            const cy = parseInt(cyStr);
            for (let dy = -this.viewPadding; dy <= this.viewPadding; dy++) {
                for (let dx = -this.viewPadding; dx <= this.viewPadding; dx++) {
                    const nx = cx + dx;
                    const ny = cy + dy;
                    if (this.store.isValidChunk(nx, ny)) {
                        expandedVisible.add(`${nx},${ny}`);
                    }
                }
            }
        }

        // Remove chunks no longer visible
        const toRemove: string[] = [];
        for (const key of this.sceneChunks) {
            if (!expandedVisible.has(key)) {
                toRemove.push(key);
            }
        }
        for (const key of toRemove) {
            this.removeChunkFromScene(key);
        }

        // Add newly visible chunks
        for (const key of expandedVisible) {
            if (!this.sceneChunks.has(key)) {
                this.addChunkToScene(key);
            }
        }

        // Update stats
        this.stats.visibleChunks = this.sceneChunks.size;
        this.stats.totalChunks = this.chunkAABBs.size;
        this.stats.cacheSize = this.lruCache.size;
        this.stats.cacheHitRate = this.lruCache.stats.totalLoads > 0
            ? this.lruCache.stats.hits / (this.lruCache.stats.hits + this.lruCache.stats.misses)
            : 0;
    }

    /** Mark a chunk as dirty (needs rebuild). Called when terrain is painted. */
    markDirty(cx: number, cy: number): void {
        const key = `${cx},${cy}`;
        // Remove from scene so it gets rebuilt next visibility check
        if (this.sceneChunks.has(key)) {
            this.removeChunkFromScene(key);
        }
        // Evict from cache so stale mesh is discarded
        this.lruCache.evict(cx, cy);
    }

    /** Clear all chunks from scene and cache. Call on map load. */
    reset(): void {
        for (const key of this.sceneChunks) {
            this.removeChunkFromScene(key);
        }
        this.lruCache.clear();
        this.sceneChunks.clear();
        this.chunkAABBs.clear();
        this.precomputeChunkAABBs();
    }

    /** Resize the LRU cache. */
    setCacheSize(maxEntries: number): void {
        this.maxCacheEntries = maxEntries;
        this.lruCache.setMaxEntries(maxEntries);
    }

    /** Set frustum view padding (extra chunk rings beyond frustum). */
    setViewPadding(padding: number): void {
        this.viewPadding = Math.max(0, padding);
    }

    private addChunkToScene(key: string): void {
        const [cxStr, cyStr] = key.split(',');
        const cx = parseInt(cxStr);
        const cy = parseInt(cyStr);

        let group = this.lruCache.get(cx, cy);
        if (!group) {
            group = this.chunkRenderer.buildChunkMesh(cx, cy);
            this.lruCache.put(cx, cy, group);
            this.stats.frameBuildCount++;
        }

        this.terrainGroup.add(group);
        this.sceneChunks.add(key);
    }

    private removeChunkFromScene(key: string): void {
        const [cxStr, cyStr] = key.split(',');
        const cx = parseInt(cxStr);
        const cy = parseInt(cyStr);

        // Find the group by name in the terrainGroup
        const groupName = `chunk_${cx}_${cy}`;
        const existing = this.terrainGroup.getObjectByName(groupName);
        if (existing) {
            this.terrainGroup.remove(existing);
        }

        this.sceneChunks.delete(key);
    }
}
