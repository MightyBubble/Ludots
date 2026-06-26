export const CHUNK_SIZE = 64;
export const CELL_STRIDE = 4; // Upgraded to 4 bytes (32-bit aligned)
export const CHUNK_BYTE_SIZE = CHUNK_SIZE * CHUNK_SIZE * CELL_STRIDE; // 16384 bytes
export const LOGIC_TERRAIN_BINARY_VERSION = 2;
const LTRN_HEADER_BYTES = 52;
const LTRN_CHECKSUM_OFFSET = 8;
const LTRN_CHECKSUM_LENGTH = 8;
const LTRN_LAYER_LAYOUT_HEIGHT_WATER_AREA_FLAGS = 1;
const LTRN_FILE_COMPRESSION_NONE = 0;
const LTRN_DENSE_BYTES_PER_CELL = 4;
const LTRN_COMPRESSION_RAW = 0;
const LTRN_COMPRESSION_RLE = 1;
const LTRN_COMPRESSION_PALETTE = 2;
const LTRN_COMPRESSION_DELTA = 3;

// Offsets (Logical Bit Packing - 4 Byte Layout)
// Byte 0: [Height:4 (7-4)][Water:4 (3-0)]
// Byte 1: [Biome:4 (7-4)][Veg:4 (3-0)]
// Byte 2: [Ramp:1 (7)][Snow:1 (6)][Mud:1 (5)][Ice:1 (4)][Blocked:1 (3)][Reserved:3]
// Byte 3: [AreaId:8 (0-255)] -> topology-neutral terrain classification for nav/gameplay consumers


export type ChunkKey = string; // "col,row"

export type TerrainStoreOptions = {
    initializeChunks?: boolean;
};

export class TerrainStore {
    widthChunks: number;
    heightChunks: number;
    chunks: Map<ChunkKey, Uint8Array>;
    dirtyChunks: Set<ChunkKey>;

    constructor(widthChunks: number = 8, heightChunks: number = 8, options: TerrainStoreOptions = {}) {
        this.widthChunks = widthChunks;
        this.heightChunks = heightChunks;
        this.chunks = new Map();
        this.dirtyChunks = new Set();
        if (options.initializeChunks !== false) {
            this.initEmptyChunks();
        }
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
    getCellIndex(col: number, row: number, createIfMissing = false): { chunk: Uint8Array, index: number, cx: number, cy: number } | null {
        const cx = Math.floor(col / CHUNK_SIZE);
        const cy = Math.floor(row / CHUNK_SIZE);
        
        if (!this.isValidChunk(cx, cy)) return null;

        let chunk = this.chunks.get(`${cx},${cy}`);
        if (!chunk && createIfMissing) {
            chunk = this.createChunk(cx, cy);
        }
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
        const loc = this.getCellIndex(col, row, true);
        if (!loc) return;
        val = Math.max(0, Math.min(15, Math.floor(val)));
        
        const oldByte = loc.chunk[loc.index];
        // Mask: 1111 0000 = 0xF0. Clear bits 7-4, set new val << 4
        const newByte = (oldByte & 0x0F) | (val << 4);
        
        if (oldByte !== newByte) {
            loc.chunk[loc.index] = newByte;
            this.markNeighborChunksDirty(col, row);
        }
    }

    private markNeighborChunksDirty(col: number, row: number) {
        const minCx = Math.floor((col - 1) / CHUNK_SIZE);
        const maxCx = Math.floor((col + 1) / CHUNK_SIZE);
        const minCy = Math.floor((row - 1) / CHUNK_SIZE);
        const maxCy = Math.floor((row + 1) / CHUNK_SIZE);

        for (let cy = minCy; cy <= maxCy; cy++) {
            for (let cx = minCx; cx <= maxCx; cx++) {
                if (this.isValidChunk(cx, cy)) {
                    this.dirtyChunks.add(`${cx},${cy}`);
                }
            }
        }
    }

    // Biome: Byte 1, Bits 7-4 (4 bits, 0-15)
    getBiome(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return (loc.chunk[loc.index + 1] >> 4) & 0x0F;
    }

    setBiome(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row, true);
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
        const loc = this.getCellIndex(col, row, true);
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
        const loc = this.getCellIndex(col, row, true);
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
        const loc = this.getCellIndex(col, row, true);
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

    // Blocked: Byte 2, Bit 3. This is the logic-terrain walkability blocker used by nav bake.
    getBlocked(col: number, row: number): boolean {
        const loc = this.getCellIndex(col, row);
        if (!loc) return false;
        return ((loc.chunk[loc.index + 2] >> 3) & 0x01) === 1;
    }

    setBlocked(col: number, row: number, val: boolean) {
        const loc = this.getCellIndex(col, row, true);
        if (!loc) return;
        const bit = val ? 1 : 0;

        const oldByte = loc.chunk[loc.index + 2];
        const newByte = (oldByte & 0xF7) | (bit << 3);

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
        const loc = this.getCellIndex(col, row, true);
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
        const loc = this.getCellIndex(col, row, true);
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
        const loc = this.getCellIndex(col, row, true);
        if (!loc) return;
        const bit = val ? 1 : 0;
        const oldByte = loc.chunk[loc.index + 2];
        const newByte = (oldByte & 0xEF) | (bit << 4);
        if (oldByte !== newByte) {
            loc.chunk[loc.index + 2] = newByte;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    // --- AreaId (Byte 3) ---
    getAreaId(col: number, row: number): number {
        const loc = this.getCellIndex(col, row);
        if (!loc) return 0;
        return loc.chunk[loc.index + 3];
    }

    setAreaId(col: number, row: number, val: number) {
        const loc = this.getCellIndex(col, row, true);
        if (!loc) return;
        val = Math.max(0, Math.min(255, Math.floor(val)));
        
        const oldByte = loc.chunk[loc.index + 3];
        if (oldByte !== val) {
            loc.chunk[loc.index + 3] = val;
            this.dirtyChunks.add(`${loc.cx},${loc.cy}`);
        }
    }

    getTerritory(col: number, row: number): number {
        return this.getAreaId(col, row);
    }

    setTerritory(col: number, row: number, val: number) {
        this.setAreaId(col, row, val);
    }

    toLogicTerrainBinary(cellSizeCm = 100): Uint8Array {
        const cellCount = CHUNK_SIZE * CHUNK_SIZE;
        const flagWords = Math.ceil(cellCount / 64);
        const payloadBytes = cellCount + cellCount + (flagWords * 3 * 8);
        const records: { key: bigint, payload: Uint8Array }[] = [];

        for (let cy = 0; cy < this.heightChunks; cy++) {
            for (let cx = 0; cx < this.widthChunks; cx++) {
                const chunk = this.chunks.get(`${cx},${cy}`);
                if (!chunk || chunk.length !== CHUNK_BYTE_SIZE || isReactChunkLogicDefault(chunk)) continue;
                records.push({
                    key: (BigInt(cy) << 32n) | BigInt(cx >>> 0),
                    payload: encodeReactChunkAsLogicPayload(chunk, payloadBytes, flagWords),
                });
            }
        }

        let totalBytes = LTRN_HEADER_BYTES;
        for (const record of records) totalBytes += 8 + 1 + 4 + record.payload.length;

        const bytes = new Uint8Array(totalBytes);
        const view = new DataView(bytes.buffer);
        bytes[0] = 0x4c; // L
        bytes[1] = 0x54; // T
        bytes[2] = 0x52; // R
        bytes[3] = 0x4e; // N
        view.setUint16(4, LOGIC_TERRAIN_BINARY_VERSION, true);
        view.setUint16(6, 0, true);
        view.setInt32(16, this.widthChunks * CHUNK_SIZE, true);
        view.setInt32(20, this.heightChunks * CHUNK_SIZE, true);
        view.setInt32(24, cellSizeCm > 0 ? Math.floor(cellSizeCm) : 100, true);
        view.setInt32(28, CHUNK_SIZE, true);
        bytes[32] = 0; // default height
        bytes[33] = 0; // default water
        bytes[34] = 0; // default flags
        bytes[35] = 0; // default areaId
        bytes[36] = LTRN_LAYER_LAYOUT_HEIGHT_WATER_AREA_FLAGS;
        bytes[37] = LTRN_FILE_COMPRESSION_NONE;
        view.setUint16(38, 0, true);
        view.setInt32(40, LTRN_DENSE_BYTES_PER_CELL, true);
        view.setInt32(44, payloadBytes, true);
        view.setInt32(48, records.length, true);

        let offset = LTRN_HEADER_BYTES;
        for (const record of records) {
            writeU64LE(bytes, offset, record.key);
            offset += 8;
            bytes[offset++] = LTRN_COMPRESSION_RAW;
            view.setInt32(offset, record.payload.length, true);
            offset += 4;
            bytes.set(record.payload, offset);
            offset += record.payload.length;
        }

        writeU64LE(bytes, LTRN_CHECKSUM_OFFSET, fnv1a64(bytes));
        return bytes;
    }

    loadFromLogicTerrainBinary(bytes: Uint8Array) {
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        if (bytes.byteLength < LTRN_HEADER_BYTES) throw new Error('LogicTerrain binary is too small.');
        if (bytes[0] !== 0x4c || bytes[1] !== 0x54 || bytes[2] !== 0x52 || bytes[3] !== 0x4e) {
            throw new Error('LogicTerrain binary magic mismatch.');
        }

        const version = view.getUint16(4, true);
        if (version !== LOGIC_TERRAIN_BINARY_VERSION) throw new Error(`LogicTerrain binary version mismatch: ${version}.`);
        const storedChecksum = readU64LE(bytes, LTRN_CHECKSUM_OFFSET);
        const computedChecksum = fnv1a64(bytes);
        if (storedChecksum !== computedChecksum) throw new Error('LogicTerrain binary checksum mismatch.');

        const widthCells = view.getInt32(16, true);
        const heightCells = view.getInt32(20, true);
        const chunkSize = view.getInt32(28, true);
        const defaultHeight = bytes[32] & 0x0f;
        const defaultWater = bytes[33] & 0x0f;
        const defaultFlags = bytes[34];
        const defaultAreaId = bytes[35];
        const layout = bytes[36];
        const fileCompression = bytes[37];
        const denseBytesPerCell = view.getInt32(40, true);
        const chunkPayloadBytes = view.getInt32(44, true);
        const chunkCount = view.getInt32(48, true);

        if (widthCells <= 0 || heightCells <= 0) throw new Error(`Invalid LogicTerrain dimensions: ${widthCells}x${heightCells}.`);
        if (chunkSize !== CHUNK_SIZE) throw new Error(`Unsupported LogicTerrain chunk size ${chunkSize}; editor expects ${CHUNK_SIZE}.`);
        if (layout !== LTRN_LAYER_LAYOUT_HEIGHT_WATER_AREA_FLAGS) throw new Error(`Unsupported LogicTerrain layer layout ${layout}.`);
        if (fileCompression !== LTRN_FILE_COMPRESSION_NONE) throw new Error(`Unsupported LogicTerrain file compression ${fileCompression}.`);
        if (denseBytesPerCell !== LTRN_DENSE_BYTES_PER_CELL) throw new Error(`Invalid LogicTerrain dense bytes/cell ${denseBytesPerCell}.`);
        if (chunkCount < 0) throw new Error(`Invalid LogicTerrain chunk count ${chunkCount}.`);

        const cellCount = CHUNK_SIZE * CHUNK_SIZE;
        const flagWords = Math.ceil(cellCount / 64);
        const expectedPayloadBytes = cellCount + cellCount + (flagWords * 3 * 8);
        if (chunkPayloadBytes !== expectedPayloadBytes) {
            throw new Error(`LogicTerrain chunk payload bytes ${chunkPayloadBytes} do not match expected ${expectedPayloadBytes}.`);
        }

        this.widthChunks = Math.ceil(widthCells / CHUNK_SIZE);
        this.heightChunks = Math.ceil(heightCells / CHUNK_SIZE);
        this.chunks.clear();
        this.dirtyChunks.clear();

        if (defaultHeight !== 0 || defaultWater !== 0 || defaultFlags !== 0 || defaultAreaId !== 0) {
            for (let cy = 0; cy < this.heightChunks; cy++) {
                for (let cx = 0; cx < this.widthChunks; cx++) {
                    this.chunks.set(`${cx},${cy}`, createDefaultReactChunk(defaultHeight, defaultWater, defaultFlags, defaultAreaId));
                }
            }
        }

        let offset = LTRN_HEADER_BYTES;
        for (let i = 0; i < chunkCount; i++) {
            if (offset + 13 > bytes.byteLength) throw new Error(`LogicTerrain chunk record ${i} is truncated.`);
            const chunkKey = readU64LE(bytes, offset);
            offset += 8;
            const cx = Number(chunkKey & 0xffff_ffffn);
            const cy = Number(chunkKey >> 32n);
            const compression = bytes[offset++];
            const encodedBytes = view.getInt32(offset, true);
            offset += 4;
            if (encodedBytes <= 0 || offset + encodedBytes > bytes.byteLength) {
                throw new Error(`LogicTerrain chunk (${cx},${cy}) encoded payload is invalid.`);
            }

            const encodedPayload = bytes.slice(offset, offset + encodedBytes);
            offset += encodedBytes;
            const rawPayload = decodeLogicPayload(compression, encodedPayload, expectedPayloadBytes);
            if (cx >= 0 && cx < this.widthChunks && cy >= 0 && cy < this.heightChunks) {
                this.chunks.set(`${cx},${cy}`, decodeLogicPayloadToReactChunk(rawPayload, flagWords));
            }
        }

        if (offset !== bytes.byteLength) throw new Error('LogicTerrain binary has trailing bytes.');
    }

    clearDirty() {
        this.dirtyChunks.clear();
    }
}

function isReactChunkLogicDefault(chunk: Uint8Array): boolean {
    for (let cell = 0; cell < CHUNK_SIZE * CHUNK_SIZE; cell++) {
        const i = cell * CELL_STRIDE;
        if (chunk[i] !== 0 || (chunk[i + 2] & 0x88) !== 0 || chunk[i + 3] !== 0) return false;
    }

    return true;
}

function encodeReactChunkAsLogicPayload(chunk: Uint8Array, payloadBytes: number, flagWords: number): Uint8Array {
    const cellCount = CHUNK_SIZE * CHUNK_SIZE;
    const areaOffset = cellCount;
    const flagsOffset = cellCount * 2;
    const payload = new Uint8Array(payloadBytes);

    for (let cell = 0; cell < cellCount; cell++) {
        const i = cell * CELL_STRIDE;
        const height = (chunk[i] >> 4) & 0x0f;
        const water = chunk[i] & 0x0f;
        payload[cell] = (height & 0x0f) | ((water & 0x0f) << 4);
        payload[areaOffset + cell] = chunk[i + 3];

        if (water > height) setLogicFlag(payload, flagsOffset, flagWords, 0, cell);
        if ((chunk[i + 2] & 0x80) !== 0) setLogicFlag(payload, flagsOffset, flagWords, 1, cell);
        if ((chunk[i + 2] & 0x08) !== 0) setLogicFlag(payload, flagsOffset, flagWords, 2, cell);
    }

    return payload;
}

function decodeLogicPayloadToReactChunk(payload: Uint8Array, flagWords: number): Uint8Array {
    const cellCount = CHUNK_SIZE * CHUNK_SIZE;
    const areaOffset = cellCount;
    const flagsOffset = cellCount * 2;
    const chunk = new Uint8Array(CHUNK_BYTE_SIZE);

    for (let cell = 0; cell < cellCount; cell++) {
        const packed = payload[cell];
        const height = packed & 0x0f;
        const water = (packed >> 4) & 0x0f;
        const i = cell * CELL_STRIDE;
        chunk[i] = ((height & 0x0f) << 4) | (water & 0x0f);
        chunk[i + 3] = payload[areaOffset + cell];

        let flags = 0;
        if (getLogicFlag(payload, flagsOffset, flagWords, 1, cell)) flags |= 0x80;
        if (getLogicFlag(payload, flagsOffset, flagWords, 2, cell)) flags |= 0x08;
        chunk[i + 2] = flags;
    }

    return chunk;
}

function createDefaultReactChunk(height: number, water: number, flags: number, areaId: number): Uint8Array {
    const chunk = new Uint8Array(CHUNK_BYTE_SIZE);
    const reactFlags = ((flags & 0x02) !== 0 ? 0x80 : 0) | ((flags & 0x04) !== 0 ? 0x08 : 0);
    for (let cell = 0; cell < CHUNK_SIZE * CHUNK_SIZE; cell++) {
        const i = cell * CELL_STRIDE;
        chunk[i] = ((height & 0x0f) << 4) | (water & 0x0f);
        chunk[i + 2] = reactFlags;
        chunk[i + 3] = areaId & 0xff;
    }

    return chunk;
}

function setLogicFlag(payload: Uint8Array, flagsOffset: number, flagWords: number, plane: number, cell: number) {
    const planeOffset = flagsOffset + plane * flagWords * 8;
    const byteOffset = planeOffset + ((cell >> 6) * 8) + ((cell & 63) >> 3);
    payload[byteOffset] |= 1 << (cell & 7);
}

function getLogicFlag(payload: Uint8Array, flagsOffset: number, flagWords: number, plane: number, cell: number): boolean {
    const planeOffset = flagsOffset + plane * flagWords * 8;
    const byteOffset = planeOffset + ((cell >> 6) * 8) + ((cell & 63) >> 3);
    return (payload[byteOffset] & (1 << (cell & 7))) !== 0;
}

function decodeLogicPayload(compression: number, encodedPayload: Uint8Array, expectedBytes: number): Uint8Array {
    switch (compression) {
        case LTRN_COMPRESSION_RAW:
            if (encodedPayload.length !== expectedBytes) throw new Error('LogicTerrain raw payload size mismatch.');
            return encodedPayload;
        case LTRN_COMPRESSION_RLE:
            return decodeRlePayload(encodedPayload, expectedBytes);
        case LTRN_COMPRESSION_PALETTE:
            return decodePalettePayload(encodedPayload, expectedBytes);
        case LTRN_COMPRESSION_DELTA:
            return decodeDeltaPayload(encodedPayload, expectedBytes);
        default:
            throw new Error(`Unknown LogicTerrain chunk compression mode ${compression}.`);
    }
}

function decodeRlePayload(encoded: Uint8Array, expectedBytes: number): Uint8Array {
    if (encoded.length % 3 !== 0) throw new Error('LogicTerrain RLE payload has an incomplete run.');
    const raw = new Uint8Array(expectedBytes);
    let source = 0;
    let target = 0;
    while (source < encoded.length) {
        const value = encoded[source++];
        const run = encoded[source++] | (encoded[source++] << 8);
        if (run <= 0 || target + run > raw.length) throw new Error('LogicTerrain RLE payload expands past chunk size.');
        raw.fill(value, target, target + run);
        target += run;
    }

    if (target !== raw.length) throw new Error('LogicTerrain RLE payload ended before chunk size.');
    return raw;
}

function decodePalettePayload(encoded: Uint8Array, expectedBytes: number): Uint8Array {
    if (encoded.length < 2) throw new Error('LogicTerrain palette payload is too small.');
    const paletteCount = encoded[0];
    if (paletteCount <= 0 || paletteCount > 16) throw new Error(`Invalid LogicTerrain palette size ${paletteCount}.`);
    const expectedLength = 1 + paletteCount + ((expectedBytes + 1) >> 1);
    if (encoded.length !== expectedLength) throw new Error('LogicTerrain palette payload size mismatch.');

    const raw = new Uint8Array(expectedBytes);
    const packedOffset = 1 + paletteCount;
    for (let i = 0; i < raw.length; i++) {
        const packed = encoded[packedOffset + (i >> 1)];
        const paletteIndex = (i & 1) === 0 ? packed & 0x0f : (packed >> 4) & 0x0f;
        if (paletteIndex >= paletteCount) throw new Error('LogicTerrain palette payload references an invalid index.');
        raw[i] = encoded[1 + paletteIndex];
    }

    return raw;
}

function decodeDeltaPayload(encoded: Uint8Array, expectedBytes: number): Uint8Array {
    if (encoded.length < 1) throw new Error('LogicTerrain delta payload is too small.');
    const raw = new Uint8Array(expectedBytes);
    raw[0] = encoded[0];
    let source = 1;
    let target = 1;
    while (source < encoded.length) {
        if (source + 3 > encoded.length) throw new Error('LogicTerrain delta payload has an incomplete run.');
        const deltaByte = encoded[source++];
        const delta = deltaByte > 127 ? deltaByte - 256 : deltaByte;
        const run = encoded[source++] | (encoded[source++] << 8);
        if (run <= 0 || target + run > raw.length) throw new Error('LogicTerrain delta payload expands past chunk size.');
        for (let i = 0; i < run; i++) {
            raw[target] = (raw[target - 1] + delta) & 0xff;
            target++;
        }
    }

    if (target !== raw.length) throw new Error('LogicTerrain delta payload ended before chunk size.');
    return raw;
}

function fnv1a64(bytes: Uint8Array): bigint {
    let hash = 1469598103934665603n;
    for (let i = 0; i < bytes.length; i++) {
        if (i >= LTRN_CHECKSUM_OFFSET && i < LTRN_CHECKSUM_OFFSET + LTRN_CHECKSUM_LENGTH) continue;
        hash ^= BigInt(bytes[i]);
        hash = (hash * 1099511628211n) & 0xffff_ffff_ffff_ffffn;
    }

    return hash;
}

function readU64LE(bytes: Uint8Array, offset: number): bigint {
    let value = 0n;
    for (let i = 7; i >= 0; i--) {
        value = (value << 8n) | BigInt(bytes[offset + i]);
    }

    return value;
}

function writeU64LE(bytes: Uint8Array, offset: number, value: bigint) {
    let remaining = value;
    for (let i = 0; i < 8; i++) {
        bytes[offset + i] = Number(remaining & 0xffn);
        remaining >>= 8n;
    }
}
