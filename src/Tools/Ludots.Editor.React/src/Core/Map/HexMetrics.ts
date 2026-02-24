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
