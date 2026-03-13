import * as THREE from 'three';
import { describe, expect, it } from 'vitest';
import { EntityManager } from './EntityManager';
import type { PrimitiveItem } from '../core/FrameDecoder';

describe('EntityManager stable identity', () => {
  it('keeps the same mesh bound to the same stable ID across reorder and hide/show', () => {
    const scene = new THREE.Scene();
    const manager = new EntityManager(scene);

    manager.update([
      createPrimitive({ stableId: 1, posX: 1 }),
      createPrimitive({ stableId: 2, posX: 2 }),
    ]);

    const meshForStableOne = scene.children[0] as THREE.Mesh;
    const meshForStableTwo = scene.children[1] as THREE.Mesh;

    manager.update([
      createPrimitive({ stableId: 2, posX: 20 }),
      createPrimitive({ stableId: 1, posX: 11 }),
    ]);

    expect(meshForStableOne.position.x).toBe(11);
    expect(meshForStableTwo.position.x).toBe(20);

    manager.update([
      createPrimitive({ stableId: 2, posX: 21 }),
    ]);

    expect(meshForStableOne.visible).toBe(false);
    expect(meshForStableTwo.visible).toBe(true);

    manager.update([
      createPrimitive({ stableId: 1, posX: 12 }),
      createPrimitive({ stableId: 2, posX: 22 }),
    ]);

    expect(meshForStableOne.visible).toBe(true);
    expect(meshForStableOne.position.x).toBe(12);
    expect(meshForStableTwo.position.x).toBe(22);
    expect(scene.children.length).toBe(2);
  });
});

function createPrimitive(overrides: Partial<PrimitiveItem>): PrimitiveItem {
  return {
    meshAssetId: overrides.meshAssetId ?? 1,
    stableId: overrides.stableId ?? 1,
    posX: overrides.posX ?? 0,
    posY: overrides.posY ?? 0,
    posZ: overrides.posZ ?? 0,
    scaleX: overrides.scaleX ?? 1,
    scaleY: overrides.scaleY ?? 1,
    scaleZ: overrides.scaleZ ?? 1,
    r: overrides.r ?? 1,
    g: overrides.g ?? 1,
    b: overrides.b ?? 1,
    a: overrides.a ?? 1,
  };
}
