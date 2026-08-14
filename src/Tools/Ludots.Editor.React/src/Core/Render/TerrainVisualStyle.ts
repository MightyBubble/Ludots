import type { BoardMetrics } from '../Map/TopologyMetrics';

export type TerrainRgb = readonly [number, number, number];
export type TerrainViewMode = 'terrain' | 'heightmap';

export type TerrainVisualOptions = {
    terrainViewMode?: TerrainViewMode;
    heightContrast?: number;
};

export const TERRAIN_HEIGHT_CONTRAST_MIN = 0.5;
export const TERRAIN_HEIGHT_CONTRAST_MAX = 3.0;

export const DEFAULT_TERRAIN_VISUAL_OPTIONS: Required<TerrainVisualOptions> = {
    terrainViewMode: 'terrain',
    heightContrast: 1.45,
};

const HEIGHT_LEVEL_COLORS: readonly TerrainRgb[] = [
    [0.09, 0.20, 0.15],
    [0.08, 0.24, 0.19],
    [0.13, 0.36, 0.24],
    [0.22, 0.48, 0.28],
    [0.38, 0.56, 0.31],
    [0.56, 0.59, 0.34],
    [0.67, 0.52, 0.32],
    [0.71, 0.43, 0.35],
    [0.76, 0.38, 0.38],
    [0.81, 0.47, 0.43],
    [0.86, 0.58, 0.51],
    [0.91, 0.70, 0.62],
    [0.95, 0.80, 0.72],
    [0.98, 0.89, 0.82],
    [1.00, 0.95, 0.88],
    [1.00, 1.00, 1.00],
];

export function getTerrainHeightScale(metrics: BoardMetrics): number {
    const cellSizeM = metrics.cellSizeCm / 100;
    if (cellSizeM >= 100) {
        return Math.max(180, Math.min(900, cellSizeM * 0.55));
    }

    return Math.max(2, Math.min(32, cellSizeM * 0.08));
}

export function getTerrainDisplayRgb(height: number, water: number, biome: number, options?: TerrainVisualOptions): TerrainRgb {
    const visualOptions = normalizeTerrainVisualOptions(options);
    const level = Math.max(0, Math.min(15, Math.floor(height)));
    const waterLevel = Math.max(0, Math.min(15, Math.floor(water)));

    if (waterLevel > level) {
        const depth = waterLevel / 15;
        if (visualOptions.terrainViewMode === 'heightmap') {
            return [0.02, 0.08 + depth * 0.16, 0.16 + depth * 0.26];
        }

        return [0.02, 0.28 + depth * 0.22, 0.50 + depth * 0.32];
    }

    if (visualOptions.terrainViewMode === 'heightmap') {
        const tone = 0.08 + applyHeightContrast(level / 15, visualOptions.heightContrast) * 0.88;
        return [tone, tone, tone];
    }

    const base = sampleHeightPalette(level, visualOptions.heightContrast);
    const biomeTint = getBiomeTint(biome);
    if (!biomeTint) return base;

    const tintStrength = 0.18;
    return [
        mix(base[0], biomeTint[0], tintStrength),
        mix(base[1], biomeTint[1], tintStrength),
        mix(base[2], biomeTint[2], tintStrength),
    ];
}

export function getTerrainDisplayByteRgb(height: number, water: number, biome: number, options?: TerrainVisualOptions): readonly [number, number, number] {
    const rgb = getTerrainDisplayRgb(height, water, biome, options);
    return [
        Math.round(rgb[0] * 255),
        Math.round(rgb[1] * 255),
        Math.round(rgb[2] * 255),
    ];
}

export function normalizeTerrainVisualOptions(options?: TerrainVisualOptions): Required<TerrainVisualOptions> {
    return {
        terrainViewMode: options?.terrainViewMode ?? DEFAULT_TERRAIN_VISUAL_OPTIONS.terrainViewMode,
        heightContrast: clamp(
            Number.isFinite(options?.heightContrast) ? Number(options?.heightContrast) : DEFAULT_TERRAIN_VISUAL_OPTIONS.heightContrast,
            TERRAIN_HEIGHT_CONTRAST_MIN,
            TERRAIN_HEIGHT_CONTRAST_MAX),
    };
}

function sampleHeightPalette(level: number, contrast: number): TerrainRgb {
    const adjusted = applyHeightContrast(level / 15, contrast) * (HEIGHT_LEVEL_COLORS.length - 1);
    const lo = Math.max(0, Math.min(HEIGHT_LEVEL_COLORS.length - 1, Math.floor(adjusted)));
    const hi = Math.max(0, Math.min(HEIGHT_LEVEL_COLORS.length - 1, Math.ceil(adjusted)));
    const t = adjusted - lo;
    const a = HEIGHT_LEVEL_COLORS[lo];
    const b = HEIGHT_LEVEL_COLORS[hi];
    return [
        mix(a[0], b[0], t),
        mix(a[1], b[1], t),
        mix(a[2], b[2], t),
    ];
}

function applyHeightContrast(value: number, contrast: number): number {
    return clamp(clamp(value, 0, 1) * contrast, 0, 1);
}

function getBiomeTint(biome: number): TerrainRgb | null {
    switch (Math.floor(biome)) {
        case 1: return [0.90, 0.70, 0.39];
        case 2: return [0.54, 0.56, 0.58];
        case 3: return [0.22, 0.50, 0.24];
        case 4: return [0.40, 0.41, 0.40];
        case 5: return [0.24, 0.32, 0.17];
        default: return null;
    }
}

function mix(a: number, b: number, t: number): number {
    return a + (b - a) * t;
}

function clamp(value: number, min: number, max: number): number {
    return Math.max(min, Math.min(max, value));
}
