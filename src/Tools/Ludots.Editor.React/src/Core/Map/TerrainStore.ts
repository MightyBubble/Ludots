import { create } from 'zustand';

export const CHUNK_SIZE = 64;
export const CELL_STRIDE = 4; // Upgraded to 4 bytes (32-bit aligned)
export const CHUNK_BYTE_SIZE = CHUNK_SIZE * CHUNK_SIZE * CELL_STRIDE; // 16384 bytes

// Offsets (Logical Bit Packing - 4 Byte Layout)
// Byte 0: [Height:4 (7-4)][Water:4 (3-0)]
// Byte 1: [Biome:4 (7-4)][Veg:4 (3-0)]
// Byte 2: [Ramp:1 (7)][Snow:1 (6)][Mud:1 (5)][Ice:1 (4)][Reserved:4]
// Byte 3: [Territory:8 (0-255)] -> 0 = Neutral, 1-255 = Factions


export type ChunkKey = string; // "col,row"

export class TerrainStore {
    widthChunks: number;
    heightChunks: number;
    chunks: Map<ChunkKey, Uint8Array>;
    dirtyChunks: Set<ChunkKey>;

    constructor(widthChunks: number = 8, heightChunks: number = 8) {
        this.widthChunks = widthChunks;
        this.heightChunks = heightChunks;
        this.chunks = new Map();
        this.dirtyChunks = new Set();
        this.initEmptyChunks();
    }

    private initEmptyChunks() {
        for (let y = 0; y < this.heightChunks; y++) {
            for (let x = 0; x < this.widthChunks; x++) {
                this.createChunk(x, y);
            }
        }
    }

    private createChunk(cx: number, cy: number) {
        const key = `${cx},${cy}`;
        const data = new Uint8Array(CHUNK_BYTE_SIZE);
        this.chunks.set(key, data);
        return data;
    }

    getChunk(cx: number, cy: number): Uint8Array | undefined {
        return this.chunks.get(`${cx},${cy}`);
    }

    isValidChunk(cx: number, cy: number): boolean {
        return cx >= 0 && cx < this.widthChunks && cy >= 0 && cy < this.heightChunks;
    }

    // Global Coordinate Access
    getCellIndex(col: number, row: number): { chunk: Uint8Array, index: number, cx: number, cy: number } | null {
        const cx = Math.floor(col / CHUNK_SIZE);
        const cy = Math.floor(row / CHUNK_SIZE);
        
        if (!this.isValidChunk(cx, cy)) return null;

        const chunk = this.chunks.get(`${cx},${cy}`);
        if (!chunk) return null;

        const localX = col % CHUNK_SIZE;
        const localY = row % CHUNK_SIZE;
        const index = (localY * CHUNK_SIZE + localX) * CELL_STRIDE;

        return { chunk, index, cx, cy };
    }

    // --- Optimized Accessors (Bit Packing v3 - 3 Bytes) ---

    // Height: Byte 0, Bits 7-4 (4 bits, 0-15)
    getHeight(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return (loc.chunk[loc.index] >> 4) & 0x0F;
    }

    setHeight(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        val = Math.max(0, Math.min(15, Math.floor(val)));
        
        const oldByte = loc.chunk[loc.index];
        // Mask: 1111 0000 = 0xF0. Clear bits 7-4, set new val << 4
        const newByte = (oldByte & 0x0F) | (val << 4);
        
        if (oldByte !== newByte) {
            loc.chunk[loc.index] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Biome: Byte 1, Bits 7-4 (4 bits, 0-15)
    getBiome(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return (loc.chunk[loc.index + 1] >> 4) & 0x0F;
    }

    setBiome(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        val = Math.max(0, Math.min(15, Math.floor(val)));

        const oldByte = loc.chunk[loc.index + 1];
        // Mask: 1111 0000 = 0xF0.
        const newByte = (oldByte & 0x0F) | (val << 4);
        
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 1] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Water: Byte 0, Bits 3-0 (4 bits, 0-15)
    getWater(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return loc.chunk[loc.index] & 0x0F;
    }

    setWater(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        val = Math.max(0, Math.min(15, Math.floor(val)));

        const oldByte = loc.chunk[loc.index];
        const newByte = (oldByte & 0xF0) | val;
        
        if (oldByte !== newByte) {
            loc.chunk[loc.index] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Veg: Byte 1, Bits 3-0 (4 bits, 0-15)
    getVeg(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return loc.chunk[loc.index + 1] & 0x0F;
    }

    setVeg(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        val = Math.max(0, Math.min(15, Math.floor(val)));

        const oldByte = loc.chunk[loc.index + 1];
        const newByte = (oldByte & 0xF0) | val;
        
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 1] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Ramp: Byte 2, Bit 7
    isRamp(col: number, row: number): boolean {
        const loc = this.getCellIndex(col, row);
        if (!loc) return false;
        return ((loc.chunk[loc.index + 2] >> 7) & 0x01) === 1;
    }

    setRamp(col: number, row: number, val: boolean) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        const bit = val ? 1 : 0;

        const oldByte = loc.chunk[loc.index + 2];
        // Bit 7: 1000 0000 = 0x80
        const newByte = (oldByte & 0x7F) | (bit << 7);
        
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 2] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // --- Dynamic Flags ---

    // Snow: Byte 2, Bit 6
    getSnow(col: number, row: number): boolean {
        const loc = this.getCellIndex(col, row);
        if (!loc) return false;
        return ((loc.chunk[loc.index + 2] >> 6) & 0x01) === 1;
    }

    setSnow(col: number, row: number, val: boolean) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        const bit = val ? 1 : 0;
        const oldByte = loc.chunk[loc.index + 2];
        const newByte = (oldByte & 0xBF) | (bit << 6);
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 2] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Mud: Byte 2, Bit 5
    getMud(col: number, row: number): boolean {
        const loc = this.getCellIndex(col, row);
        if (!loc) return false;
        return ((loc.chunk[loc.index + 2] >> 5) & 0x01) === 1;
    }

    setMud(col: number, row: number, val: boolean) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        const bit = val ? 1 : 0;
        const oldByte = loc.chunk[loc.index + 2];
        const newByte = (oldByte & 0xDF) | (bit << 5);
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 2] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Ice: Byte 2, Bit 4
    getIce(col: number, row: number): boolean {
        const loc = this.getCellIndex(col, row);
        if (!loc) return false;
        return ((loc.chunk[loc.index + 2] >> 4) & 0x01) === 1;
    }

    setIce(col: number, row: number, val: boolean) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        const bit = val ? 1 : 0;
        const oldByte = loc.chunk[loc.index + 2];
        const newByte = (oldByte & 0xEF) | (bit << 4);
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 2] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // --- Territory (Byte 3) ---
    // 0 = Neutral
    // 1-255 = Faction IDs
    getTerritory(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return loc.chunk[loc.index + 3];
    }

    setTerritory(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row);
        if (!loc) return;
        val = Math.max(0, Math.min(255, Math.floor(val)));
        
        const oldByte = loc.chunk[loc.index + 3];
        if (oldByte !== val) {
            loc.chunk[loc.index + 3] = val;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // Serialization
    serialize(): Uint8Array {
        const totalSize = this.widthChunks * this.heightChunks * CHUNK_BYTE_SIZE;
        const buffer = new Uint8Array(totalSize);
        
        let offset = 0;
        // Order: Row-major chunks (Chunk 0,0 -> 1,0 -> ... -> 0,1 -> ...)
        // Check user requirement for serialization order.
        // User says: "totalW * totalH * 5".
        // Usually implies iterating global Y then global X.
        // But for chunks, we can just dump chunks in order.
        // Let's assume standard iteration: Chunk Y, then Chunk X.
        
        for (let cy = 0; cy < this.heightChunks; cy++) {
            for (let cx = 0; cx < this.widthChunks; cx++) {
                const chunk = this.chunks.get(`${cx},${cy}`);
                if (chunk) {
                    buffer.set(chunk, offset);
                }
                offset += CHUNK_BYTE_SIZE;
            }
        }
        return buffer;
    }

    loadFromBytes(widthChunks: number, heightChunks: number, bytes: Uint8Array) {
        this.widthChunks = widthChunks;
        this.heightChunks = heightChunks;
        this.chunks.clear();
        this.dirtyChunks.clear();

        const expectedSize = widthChunks * heightChunks * CHUNK_BYTE_SIZE;
        if (bytes.length !== expectedSize) {
            console.error(`Size mismatch: expected ${expectedSize}, got ${bytes.length}`);
            // Fallback: load as much as possible or fail?
            // For now, allow partial load but warn
        }

        let offset = 0;
        for (let cy = 0; cy < heightChunks; cy++) {
            for (let cx = 0; cx < widthChunks; cx++) {
                const chunkData = bytes.slice(offset, offset + CHUNK_BYTE_SIZE);
                this.chunks.set(`${cx},${cy}`, chunkData);
                this.dirtyChunks.add(`${cx},${cy}`); // Mark all dirty for initial render
                offset += CHUNK_BYTE_SIZE;
            }
        }
    }
    
    clearDirty() {
        this.dirtyChunks.clear();
    }
}
