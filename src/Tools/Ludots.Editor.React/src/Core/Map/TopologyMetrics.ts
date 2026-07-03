import { CHUNK_SIZE } from './TerrainStore';
import { CellCm, DefaultHexEdgeLengthCm } from '../SpatialScaleDefaults';
import { getHexLayout, getHexPosition, hexToWorldCm, worldCmToHex } from './HexMetrics';

export type BoardTopology = 'HexGrid' | 'Grid';
export type SpatialTopology = BoardTopology | 'NodeGraph';

export interface BoardMetrics {
    topology: BoardTopology;
    cellSizeCm: number;
    hexEdgeLengthCm: number;
    chunkSizeCells: number;
}

export const DEFAULT_BOARD_METRICS: BoardMetrics = {
    topology: 'HexGrid',
    cellSizeCm: CellCm,
    hexEdgeLengthCm: DefaultHexEdgeLengthCm,
    chunkSizeCells: CHUNK_SIZE,
};

export function normalizeSpatialTopology(value: unknown): SpatialTopology {
    const text = String(value ?? '').trim();
    if (text === 'Grid') return 'Grid';
    if (text === 'HexGrid') return 'HexGrid';
    if (text === 'NodeGraph') return 'NodeGraph';
    throw new Error(`Unsupported board SpatialType '${String(value ?? '')}'.`);
}

export function normalizeTopology(value: unknown): BoardTopology {
    const topology = normalizeSpatialTopology(value);
    if (topology === 'NodeGraph') {
        throw new Error('NodeGraph boards do not support terrain editing.');
    }
    return topology;
}

export function normalizeBoardMetrics(input?: Partial<BoardMetrics> | null): BoardMetrics {
    return {
        topology: normalizeTopology(input?.topology ?? DEFAULT_BOARD_METRICS.topology),
        cellSizeCm: positiveInt(input?.cellSizeCm, DEFAULT_BOARD_METRICS.cellSizeCm),
        hexEdgeLengthCm: positiveInt(input?.hexEdgeLengthCm, DEFAULT_BOARD_METRICS.hexEdgeLengthCm),
        chunkSizeCells: positiveInt(input?.chunkSizeCells, DEFAULT_BOARD_METRICS.chunkSizeCells),
    };
}

export function cellToWorldPosition(
    col: number,
    row: number,
    height: number,
    metrics: BoardMetrics,
    hScale: number = 2.0,
    offsetX: number = 0,
    offsetZ: number = 0,
) {
    if (metrics.topology === 'Grid') {
        const cellSizeM = metrics.cellSizeCm / 100.0;
        return {
            x: (col + 0.5) * cellSizeM + offsetX,
            y: height * hScale,
            z: (row + 0.5) * cellSizeM + offsetZ,
        };
    }

    return getHexPosition(col, row, height, hScale, offsetX, offsetZ, metrics.hexEdgeLengthCm);
}

export function cellToWorldCm(col: number, row: number, metrics: BoardMetrics): { xCm: number; yCm: number } {
    if (metrics.topology === 'Grid') {
        const half = Math.floor(metrics.cellSizeCm / 2);
        return {
            xCm: col * metrics.cellSizeCm + half,
            yCm: row * metrics.cellSizeCm + half,
        };
    }

    return hexToWorldCm(col, row, metrics.hexEdgeLengthCm);
}

export function worldCmToCell(xCm: number, yCm: number, metrics: BoardMetrics): { col: number; row: number } {
    if (metrics.topology === 'Grid') {
        return {
            col: Math.floor(xCm / metrics.cellSizeCm),
            row: Math.floor(yCm / metrics.cellSizeCm),
        };
    }

    return worldCmToHex(xCm, yCm, metrics.hexEdgeLengthCm);
}

export function worldPointToCell(x: number, z: number, metrics: BoardMetrics): { col: number; row: number } {
    if (metrics.topology === 'Grid') {
        const cellSizeM = metrics.cellSizeCm / 100.0;
        return {
            col: Math.floor(x / cellSizeM),
            row: Math.floor(z / cellSizeM),
        };
    }

    const { hexWidth, rowSpacing } = getHexLayout(metrics.hexEdgeLengthCm);
    const row = Math.round(z / rowSpacing);
    const col = Math.round(x / hexWidth - 0.5 * (row & 1));
    return { col, row };
}

export function getTopologyNeighbors(col: number, row: number, metrics: BoardMetrics) {
    if (metrics.topology === 'Grid') {
        return [
            { c: col + 1, r: row },
            { c: col, r: row + 1 },
            { c: col - 1, r: row },
            { c: col, r: row - 1 },
        ];
    }

    const isOdd = (row & 1) === 1;
    const offsets = isOdd
        ? [[1, 0], [1, 1], [0, 1], [-1, 0], [0, -1], [1, -1]]
        : [[1, 0], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1]];

    return offsets.map(o => ({ c: col + o[0], r: row + o[1] }));
}

export function getMapWorldSizeM(widthChunks: number, heightChunks: number, metrics: BoardMetrics) {
    const widthCells = widthChunks * metrics.chunkSizeCells;
    const heightCells = heightChunks * metrics.chunkSizeCells;
    if (metrics.topology === 'Grid') {
        const cellSizeM = metrics.cellSizeCm / 100.0;
        return {
            width: widthCells * cellSizeM,
            height: heightCells * cellSizeM,
        };
    }

    const { hexWidth, rowSpacing } = getHexLayout(metrics.hexEdgeLengthCm);
    return {
        width: widthCells * hexWidth,
        height: heightCells * rowSpacing,
    };
}

export function getBrushVisualRadius(metrics: BoardMetrics, brushSize: number): number {
    const radiusCells = Math.max(1, brushSize);
    if (metrics.topology === 'Grid') {
        return radiusCells * (metrics.cellSizeCm / 100.0);
    }

    return radiusCells;
}

function positiveInt(value: unknown, fallback: number): number {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed <= 0) return fallback;
    return Math.floor(parsed);
}
