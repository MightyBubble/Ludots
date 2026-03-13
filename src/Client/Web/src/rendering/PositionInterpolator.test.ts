import { describe, expect, it } from 'vitest';
import { PositionInterpolator } from './PositionInterpolator';
import type { PrimitiveItem } from '../core/FrameDecoder';

describe('PositionInterpolator', () => {
  it('matches frames by stable id instead of array index', () => {
    const interpolator = new PositionInterpolator();

    interpolator.pushFrame([
      createPrimitive(101, 7, 0, 0),
      createPrimitive(202, 8, 10, 10),
    ], 1000);

    interpolator.pushFrame([
      createPrimitive(202, 8, 14, 14),
      createPrimitive(101, 7, 4, 4),
    ], 1100);

    ((interpolator as unknown) as { _frameDurationMs: number })._frameDurationMs = 100;
    interpolator.tick(0.05);

    const interpolated = interpolator.getInterpolated();
    expect(interpolated.map((item) => item.stableId)).toEqual([202, 101]);
    expect(interpolated[0].posX).toBeCloseTo(12);
    expect(interpolated[1].posX).toBeCloseTo(2);
  });

  it('does not interpolate anonymous primitives across frames', () => {
    const interpolator = new PositionInterpolator();

    interpolator.pushFrame([
      createPrimitive(0, 7, 0, 0),
    ], 1000);

    interpolator.pushFrame([
      createPrimitive(0, 7, 10, 10),
    ], 1100);

    ((interpolator as unknown) as { _frameDurationMs: number })._frameDurationMs = 100;
    interpolator.tick(0.05);

    const interpolated = interpolator.getInterpolated();
    expect(interpolated[0].stableId).toBe(0);
    expect(interpolated[0].posX).toBeCloseTo(10);
    expect(interpolated[0].posZ).toBeCloseTo(10);
  });
});

function createPrimitive(stableId: number, meshAssetId: number, posX: number, posZ: number): PrimitiveItem {
  return {
    meshAssetId,
    stableId,
    posX,
    posY: 0.5,
    posZ,
    scaleX: 1,
    scaleY: 1,
    scaleZ: 1,
    r: 1,
    g: 1,
    b: 1,
    a: 1,
  };
}
