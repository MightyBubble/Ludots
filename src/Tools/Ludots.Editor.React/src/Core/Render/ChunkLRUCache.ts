import * as THREE from 'three';

/**
 * ChunkLRUCache - LRU cache for Three.js mesh groups keyed by chunk coordinates.
 * Handles disposal of evicted Three.js resources.
 */
export class ChunkLRUCache {
    private cache: Map<string, THREE.Group>;
    private maxEntries: number;
    private accessOrder: string[];

    /** Cache statistics for performance monitoring */
    stats = { hits: 0, misses: 0, evictions: 0, totalLoads: 0 };

    constructor(maxEntries: number = 256) {
        this.cache = new Map();
        this.maxEntries = maxEntries;
        this.accessOrder = [];
    }

    /** Retrieve a cached chunk group, or undefined if not cached. */
    get(cx: number, cy: number): THREE.Group | undefined {
        const key = `${cx},${cy}`;
        const group = this.cache.get(key);
        if (group) {
            // Move to end (most recently used)
            const idx = this.accessOrder.indexOf(key);
            if (idx > -1) {
                this.accessOrder.splice(idx, 1);
                this.accessOrder.push(key);
            }
            this.stats.hits++;
            return group;
        }
        this.stats.misses++;
        return undefined;
    }

    /** Store a chunk group. Evicts LRU entries if at capacity. */
    put(cx: number, cy: number, group: THREE.Group): void {
        const key = `${cx},${cy}`;
        // If already cached, just update position
        if (this.cache.has(key)) {
            const idx = this.accessOrder.indexOf(key);
            if (idx > -1) {
                this.accessOrder.splice(idx, 1);
            }
        }

        // Evict while over capacity
        while (this.accessOrder.length >= this.maxEntries) {
            const evictKey = this.accessOrder.shift();
            if (evictKey) {
                this.evictKey(evictKey);
            }
        }

        this.cache.set(key, group);
        this.accessOrder.push(key);
        this.stats.totalLoads++;
    }

    /** Remove a chunk from cache and dispose its resources. */
    evict(cx: number, cy: number): void {
        const key = `${cx},${cy}`;
        this.evictKey(key);
    }

    /** Check if a chunk is cached. */
    has(cx: number, cy: number): boolean {
        return this.cache.has(`${cx},${cy}`);
    }

    /** Get current cache size. */
    get size(): number {
        return this.cache.size;
    }

    /** Clear all cached entries and dispose resources. */
    clear(): void {
        for (const [, group] of this.cache) {
            this.disposeGroup(group);
        }
        this.cache.clear();
        this.accessOrder = [];
        this.stats = { hits: 0, misses: 0, evictions: 0, totalLoads: 0 };
    }

    /** Resize the cache, evicting LRU entries as needed. */
    setMaxEntries(max: number): void {
        this.maxEntries = max;
        while (this.accessOrder.length > this.maxEntries) {
            const evictKey = this.accessOrder.shift();
            if (evictKey) {
                this.evictKey(evictKey);
            }
        }
    }

    private evictKey(key: string): void {
        const group = this.cache.get(key);
        if (group) {
            this.disposeGroup(group);
            this.cache.delete(key);
            this.stats.evictions++;
        }
    }

    private disposeGroup(group: THREE.Group): void {
        group.traverse((child) => {
            if (child instanceof THREE.Mesh) {
                child.geometry?.dispose();
                if (Array.isArray(child.material)) {
                    child.material.forEach(m => m.dispose());
                } else {
                    child.material?.dispose();
                }
            }
        });
        group.clear();
    }
}
