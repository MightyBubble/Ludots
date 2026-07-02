import { DefaultHexEdgeLengthCm } from '../SpatialScaleDefaults';

export const HEX_EDGE_LENGTH_CM = DefaultHexEdgeLengthCm;
export const HEX_SIZE = HEX_EDGE_LENGTH_CM / 100.0;
export const HEX_WIDTH = Math.sqrt(3) * HEX_SIZE; // ~6.928 for 400cm
export const HEX_HEIGHT = 2.0 * HEX_SIZE; // 8.0 for 400cm
export const COL_SPACING = HEX_WIDTH;
export const ROW_SPACING = 1.5 * HEX_SIZE; // 6.0 for 400cm

export function getHexLayout(edgeLengthCm: number = DefaultHexEdgeLengthCm) {
    const edgeLengthM = edgeLengthCm / 100.0;
    const hexWidth = Math.sqrt(3) * edgeLengthM;
    const hexHeight = 2.0 * edgeLengthM;
    const rowSpacing = 1.5 * edgeLengthM;
    return { edgeLengthM, hexWidth, hexHeight, rowSpacing };
}

export function getHexPosition(
    col: number,
    row: number,
    height: number,
    hScale: number = 2.0,
    offsetX: number = 0,
    offsetZ: number = 0,
    edgeLengthCm: number = DefaultHexEdgeLengthCm,
) {
    const { hexWidth, rowSpacing } = getHexLayout(edgeLengthCm);
    const x = hexWidth * (col + 0.5 * (row & 1)) + offsetX;
    const z = rowSpacing * row + offsetZ;
    const y = height * hScale;
    return { x, y, z };
}

export function hexToWorldCm(col: number, row: number, edgeLengthCm: number = DefaultHexEdgeLengthCm): { xCm: number; yCm: number } {
    const { hexWidth, rowSpacing } = getHexLayout(edgeLengthCm);
    const x = hexWidth * (col + 0.5 * (row & 1));
    const z = rowSpacing * row;
    return { xCm: Math.round(x * 100), yCm: Math.round(z * 100) };
}

export function worldCmToHex(xCm: number, yCm: number, edgeLengthCm: number = DefaultHexEdgeLengthCm): { col: number; row: number } {
    const { hexWidth, rowSpacing } = getHexLayout(edgeLengthCm);
    const xM = xCm * 0.01;
    const zM = yCm * 0.01;
    const row = Math.round(zM / rowSpacing);
    const col = Math.round(xM / hexWidth - 0.5 * (row & 1));
    return { col, row };
}
