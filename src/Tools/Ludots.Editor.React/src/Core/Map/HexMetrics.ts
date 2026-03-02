export const HEX_SIZE = 4.0;
export const HEX_WIDTH = Math.sqrt(3) * HEX_SIZE; // ~6.928
export const HEX_HEIGHT = 2.0 * HEX_SIZE; // 8.0
export const COL_SPACING = HEX_WIDTH;
export const ROW_SPACING = 1.5 * HEX_SIZE; // 6.0

export function getHexPosition(col: number, row: number, height: number, hScale: number = 2.0, offsetX: number = 0, offsetZ: number = 0) {
    const x = HEX_WIDTH * (col + 0.5 * (row & 1)) + offsetX;
    const z = ROW_SPACING * row + offsetZ;
    const y = height * hScale;
    return { x, y, z };
}

export function hexToWorldCm(col: number, row: number): { xCm: number; yCm: number } {
    const x = HEX_WIDTH * (col + 0.5 * (row & 1));
    const z = ROW_SPACING * row;
    return { xCm: Math.round(x * 100), yCm: Math.round(z * 100) };
}

export function worldCmToHex(xCm: number, yCm: number): { col: number; row: number } {
    const xM = xCm * 0.01;
    const zM = yCm * 0.01;
    const row = Math.round(zM / ROW_SPACING);
    const col = Math.round(xM / HEX_WIDTH - 0.5 * (row & 1));
    return { col, row };
}
