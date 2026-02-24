export enum NavPortalSide {
    West = 0,
    East = 1,
    North = 2,
    South = 3,
}

export type NavTileId = { chunkX: number; chunkY: number; layer: number };

export type NavBorderPortal = {
    side: NavPortalSide;
    u0: number;
    v0: number;
    u1: number;
    v1: number;
    leftXcm: number;
    leftZcm: number;
    rightXcm: number;
    rightZcm: number;
    clearanceCm: number;
};

export type NavTile = {
    tileId: NavTileId;
    tileVersion: number;
    buildConfigHash: bigint;
    checksum: bigint;
    originXcm: number;
    originZcm: number;
    vertexXcm: Int32Array;
    vertexYcm: Int32Array;
    vertexZcm: Int32Array;
    triA: Int32Array;
    triB: Int32Array;
    triC: Int32Array;
    n0: Int32Array;
    n1: Int32Array;
    n2: Int32Array;
    portals: NavBorderPortal[];
};

const MAGIC = 0x4c49544e;
const FORMAT_VERSION = 1;

export function readNavTile(buffer: ArrayBuffer): NavTile {
    const view = new DataView(buffer);
    let o = 0;

    const magic = view.getUint32(o, true); o += 4;
    if (magic !== MAGIC) throw new Error('NavTileBin magic mismatch.');
    const ver = view.getUint16(o, true); o += 2;
    if (ver !== FORMAT_VERSION) throw new Error(`NavTileBin version mismatch: ${ver}.`);
    o += 2;

    const chunkX = view.getInt32(o, true); o += 4;
    const chunkY = view.getInt32(o, true); o += 4;
    const layer = view.getInt32(o, true); o += 4;
    const tileVersion = view.getUint32(o, true); o += 4;
    const buildConfigHash = view.getBigUint64(o, true); o += 8;
    const checksum = view.getBigUint64(o, true); o += 8;
    const originXcm = view.getInt32(o, true); o += 4;
    const originZcm = view.getInt32(o, true); o += 4;

    const checksumOffset = 4 + 2 + 2 + 4 + 4 + 4 + 4 + 8;
    const computed = fnv1a64(new Uint8Array(buffer), checksumOffset, 8);
    if (computed !== checksum) throw new Error('NavTileBin checksum mismatch.');

    const vCount = view.getInt32(o, true); o += 4;
    const vx = new Int32Array(vCount);
    const vy = new Int32Array(vCount);
    const vz = new Int32Array(vCount);
    for (let i = 0; i < vCount; i++) {
        vx[i] = view.getInt32(o, true); o += 4;
        vy[i] = view.getInt32(o, true); o += 4;
        vz[i] = view.getInt32(o, true); o += 4;
    }

    const tCount = view.getInt32(o, true); o += 4;
    const ta = new Int32Array(tCount);
    const tb = new Int32Array(tCount);
    const tc = new Int32Array(tCount);
    for (let i = 0; i < tCount; i++) {
        ta[i] = view.getInt32(o, true); o += 4;
        tb[i] = view.getInt32(o, true); o += 4;
        tc[i] = view.getInt32(o, true); o += 4;
    }

    const nCount = view.getInt32(o, true); o += 4;
    if (nCount !== tCount) throw new Error('NavTileBin neighbor count mismatch.');
    const n0 = new Int32Array(tCount);
    const n1 = new Int32Array(tCount);
    const n2 = new Int32Array(tCount);
    for (let i = 0; i < tCount; i++) {
        n0[i] = view.getInt32(o, true); o += 4;
        n1[i] = view.getInt32(o, true); o += 4;
        n2[i] = view.getInt32(o, true); o += 4;
    }

    const pCount = view.getInt32(o, true); o += 4;
    const portals: NavBorderPortal[] = [];
    for (let i = 0; i < pCount; i++) {
        const side = view.getUint8(o); o += 1;
        const u0 = view.getInt16(o, true); o += 2;
        const v0 = view.getInt16(o, true); o += 2;
        const u1 = view.getInt16(o, true); o += 2;
        const v1 = view.getInt16(o, true); o += 2;
        const leftXcm = view.getInt32(o, true); o += 4;
        const leftZcm = view.getInt32(o, true); o += 4;
        const rightXcm = view.getInt32(o, true); o += 4;
        const rightZcm = view.getInt32(o, true); o += 4;
        const clearanceCm = view.getInt32(o, true); o += 4;
        portals.push({ side: side as NavPortalSide, u0, v0, u1, v1, leftXcm, leftZcm, rightXcm, rightZcm, clearanceCm });
    }

    return {
        tileId: { chunkX, chunkY, layer },
        tileVersion,
        buildConfigHash,
        checksum,
        originXcm,
        originZcm,
        vertexXcm: vx,
        vertexYcm: vy,
        vertexZcm: vz,
        triA: ta,
        triB: tb,
        triC: tc,
        n0,
        n1,
        n2,
        portals,
    };
}

function fnv1a64(data: Uint8Array, checksumOffset: number, checksumLength: number): bigint {
    let h = 1469598103934665603n;
    const prime = 1099511628211n;
    for (let i = 0; i < data.length; i++) {
        if (i >= checksumOffset && i < checksumOffset + checksumLength) continue;
        h ^= BigInt(data[i]);
        h = (h * prime) & 0xffffffffffffffffn;
    }
    return h;
}

