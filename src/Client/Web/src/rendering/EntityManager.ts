import * as THREE from 'three';
import type { PrimitiveItem, MeshMapEntry } from '../core/FrameDecoder';

export class EntityManager {
  private readonly _scene: THREE.Scene;
  private readonly _stableMeshes = new Map<number, THREE.Mesh>();
  private readonly _anonymousMeshes: THREE.Mesh[] = [];
  private readonly _activeStableIds = new Set<number>();
  private readonly _cubeGeo: THREE.BoxGeometry;
  private readonly _sphereGeo: THREE.SphereGeometry;
  private _sphereIds = new Set<number>();

  constructor(scene: THREE.Scene) {
    this._scene = scene;
    this._cubeGeo = new THREE.BoxGeometry(1, 1, 1);
    this._sphereGeo = new THREE.SphereGeometry(0.5, 12, 8);
  }

  applyMeshMap(entries: MeshMapEntry[]): void {
    this._sphereIds.clear();
    for (const e of entries) {
      if (e.key === 'sphere') this._sphereIds.add(e.id);
    }
  }

  update(items: PrimitiveItem[]): void {
    this._activeStableIds.clear();
    this.resetAnonymousMeshes();

    for (const item of items) {
      const canUseStableId = item.stableId > 0 && !this._activeStableIds.has(item.stableId);
      const mesh = canUseStableId
        ? this.getOrCreateStableMesh(item.stableId)
        : this.createAnonymousMesh();

      if (canUseStableId) {
        this._activeStableIds.add(item.stableId);
      }

      this.applyPrimitive(mesh, item);
    }

    for (const [stableId, mesh] of this._stableMeshes) {
      if (!this._activeStableIds.has(stableId)) {
        // Snapshot-only visibility cannot distinguish "hidden" from "despawn", so keep stable meshes parked.
        mesh.visible = false;
      }
    }
  }

  private getOrCreateStableMesh(stableId: number): THREE.Mesh {
    let mesh = this._stableMeshes.get(stableId);
    if (!mesh) {
      mesh = this.createMesh();
      this._stableMeshes.set(stableId, mesh);
      this._scene.add(mesh);
    }

    return mesh;
  }

  private createAnonymousMesh(): THREE.Mesh {
    const mesh = this.createMesh();
    this._anonymousMeshes.push(mesh);
    this._scene.add(mesh);
    return mesh;
  }

  private resetAnonymousMeshes(): void {
    for (const mesh of this._anonymousMeshes) {
      this._scene.remove(mesh);
      (mesh.material as THREE.Material).dispose();
    }
    this._anonymousMeshes.length = 0;
  }

  private createMesh(): THREE.Mesh {
    return new THREE.Mesh(this._cubeGeo, new THREE.MeshLambertMaterial());
  }

  private applyPrimitive(mesh: THREE.Mesh, item: PrimitiveItem): void {
    const wantSphere = this._sphereIds.has(item.meshAssetId);
    const isSphere = mesh.geometry === this._sphereGeo;
    if (wantSphere !== isSphere) {
      mesh.geometry = wantSphere ? this._sphereGeo : this._cubeGeo;
    }

    mesh.position.set(item.posX, item.posY, item.posZ);
    mesh.scale.set(item.scaleX, item.scaleY, item.scaleZ);

    const mat = mesh.material as THREE.MeshLambertMaterial;
    mat.color.setRGB(item.r, item.g, item.b);
    mat.opacity = item.a;
    mat.transparent = item.a < 0.99;
    mesh.visible = true;
  }
}
