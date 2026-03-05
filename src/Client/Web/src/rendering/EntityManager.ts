import * as THREE from 'three';
import type { PrimitiveItem } from '../core/FrameDecoder';

export class EntityManager {
  private readonly _scene: THREE.Scene;
  private _meshes: THREE.Mesh[] = [];
  private readonly _cubeGeo: THREE.BoxGeometry;
  private readonly _sphereGeo: THREE.SphereGeometry;

  constructor(scene: THREE.Scene) {
    this._scene = scene;
    this._cubeGeo = new THREE.BoxGeometry(1, 1, 1);
    this._sphereGeo = new THREE.SphereGeometry(0.5, 12, 8);
  }

  update(items: PrimitiveItem[]): void {
    while (this._meshes.length < items.length) {
      const mat = new THREE.MeshLambertMaterial();
      const mesh = new THREE.Mesh(this._cubeGeo, mat);
      this._scene.add(mesh);
      this._meshes.push(mesh);
    }

    while (this._meshes.length > items.length) {
      const mesh = this._meshes.pop()!;
      this._scene.remove(mesh);
      mesh.geometry = this._cubeGeo; // reset
      (mesh.material as THREE.Material).dispose();
    }

    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      const mesh = this._meshes[i];

      // meshAssetId: 1=cube, 2=sphere
      const wantSphere = item.meshAssetId === 2;
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
}
