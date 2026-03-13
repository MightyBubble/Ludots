import * as THREE from 'three';
import type { PrimitiveItem } from '../core/FrameProtocol';

const DEFAULT_GEOMETRY = new THREE.BoxGeometry(1, 1, 1);
const SPHERE_GEOMETRY = new THREE.SphereGeometry(0.5, 12, 8);

/**
 * Manages Three.js InstancedMesh objects that represent game entities.
 * Groups primitives by meshAssetId for efficient instanced rendering.
 */
export class EntityInstanceManager {
  private readonly _scene: THREE.Scene;
  private readonly _meshes = new Map<number, THREE.InstancedMesh>();
  private readonly _maxInstances: number;
  private readonly _tempMatrix = new THREE.Matrix4();
  private readonly _tempColor = new THREE.Color();

  constructor(scene: THREE.Scene, maxInstances = 4096) {
    this._scene = scene;
    this._maxInstances = maxInstances;
  }

  update(primitives: PrimitiveItem[]): void {
    const groups = new Map<number, PrimitiveItem[]>();
    for (const p of primitives) {
      let list = groups.get(p.meshAssetId);
      if (!list) {
        list = [];
        groups.set(p.meshAssetId, list);
      }
      list.push(p);
    }

    for (const [, items] of groups) {
      items.sort((a, b) => {
        if (a.stableId <= 0 && b.stableId <= 0) {
          return 0;
        }

        if (a.stableId <= 0) {
          return 1;
        }

        if (b.stableId <= 0) {
          return -1;
        }

        return a.stableId - b.stableId;
      });
    }

    // Hide meshes not present this frame
    for (const [id, mesh] of this._meshes) {
      if (!groups.has(id)) {
        mesh.count = 0;
        mesh.visible = false;
      }
    }

    for (const [meshId, items] of groups) {
      let mesh = this._meshes.get(meshId);
      if (!mesh) {
        mesh = this._createMesh(meshId);
        this._meshes.set(meshId, mesh);
        this._scene.add(mesh);
      }

      const count = Math.min(items.length, this._maxInstances);
      mesh.count = count;
      mesh.visible = true;

      for (let i = 0; i < count; i++) {
        const item = items[i];
        this._tempMatrix.makeScale(item.scaleX, item.scaleY, item.scaleZ);
        this._tempMatrix.setPosition(item.posX, item.posY, item.posZ);
        mesh.setMatrixAt(i, this._tempMatrix);

        this._tempColor.setRGB(item.colorR, item.colorG, item.colorB);
        mesh.setColorAt(i, this._tempColor);
      }

      mesh.instanceMatrix.needsUpdate = true;
      if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    }
  }

  dispose(): void {
    for (const [, mesh] of this._meshes) {
      this._scene.remove(mesh);
      mesh.geometry.dispose();
      (mesh.material as THREE.Material).dispose();
    }
    this._meshes.clear();
  }

  private _createMesh(meshId: number): THREE.InstancedMesh {
    // MeshAssetId 0 = default cube, 1 = sphere, others = cube with different base color
    const geo = meshId === 1 ? SPHERE_GEOMETRY : DEFAULT_GEOMETRY;
    const mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
    const mesh = new THREE.InstancedMesh(geo, mat, this._maxInstances);
    mesh.count = 0;
    mesh.instanceColor = new THREE.InstancedBufferAttribute(
      new Float32Array(this._maxInstances * 3), 3
    );
    mesh.frustumCulled = false;
    return mesh;
  }
}
