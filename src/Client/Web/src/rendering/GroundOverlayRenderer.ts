import * as THREE from 'three';
import type { GroundOverlayItem } from '../core/FrameProtocol';

/**
 * Renders ground-projected overlay shapes (circles, rings, lines).
 */
export class GroundOverlayRenderer {
  private readonly _scene: THREE.Scene;
  private _mesh: THREE.LineSegments | null = null;
  private _geo: THREE.BufferGeometry | null = null;
  private readonly _mat = new THREE.LineBasicMaterial({ vertexColors: true });

  constructor(scene: THREE.Scene) {
    this._scene = scene;
  }

  update(overlays: GroundOverlayItem[]): void {
    this._clear();
    if (overlays.length === 0) return;

    const segs = 48;
    let totalSegments = 0;
    for (const ov of overlays) {
      if (ov.shape === 0 || ov.shape === 3) totalSegments += segs;
      else if (ov.shape === 2) totalSegments += 1;
    }
    if (totalSegments === 0) return;

    const positions = new Float32Array(totalSegments * 6);
    const colors = new Float32Array(totalSegments * 6);
    let idx = 0;

    for (const ov of overlays) {
      const r = ov.borderR, g = ov.borderG, b = ov.borderB;

      if (ov.shape === 0 || ov.shape === 3) {
        const step = (Math.PI * 2) / segs;
        for (let s = 0; s < segs; s++) {
          const a0 = s * step, a1 = (s + 1) * step;
          positions[idx] = ov.centerX + Math.cos(a0) * ov.radius;
          positions[idx + 1] = ov.centerY;
          positions[idx + 2] = ov.centerZ + Math.sin(a0) * ov.radius;
          positions[idx + 3] = ov.centerX + Math.cos(a1) * ov.radius;
          positions[idx + 4] = ov.centerY;
          positions[idx + 5] = ov.centerZ + Math.sin(a1) * ov.radius;
          colors[idx] = r; colors[idx + 1] = g; colors[idx + 2] = b;
          colors[idx + 3] = r; colors[idx + 4] = g; colors[idx + 5] = b;
          idx += 6;
        }
      } else if (ov.shape === 2) {
        const dx = Math.cos(ov.rotation) * ov.length;
        const dz = Math.sin(ov.rotation) * ov.length;
        positions[idx] = ov.centerX; positions[idx + 1] = ov.centerY; positions[idx + 2] = ov.centerZ;
        positions[idx + 3] = ov.centerX + dx; positions[idx + 4] = ov.centerY; positions[idx + 5] = ov.centerZ + dz;
        colors[idx] = r; colors[idx + 1] = g; colors[idx + 2] = b;
        colors[idx + 3] = r; colors[idx + 4] = g; colors[idx + 5] = b;
        idx += 6;
      }
    }

    this._geo = new THREE.BufferGeometry();
    this._geo.setAttribute('position', new THREE.BufferAttribute(positions.slice(0, idx), 3));
    this._geo.setAttribute('color', new THREE.BufferAttribute(colors.slice(0, idx), 3));
    this._mesh = new THREE.LineSegments(this._geo, this._mat);
    this._scene.add(this._mesh);
  }

  dispose(): void {
    this._clear();
    this._mat.dispose();
  }

  private _clear(): void {
    if (this._mesh) {
      this._scene.remove(this._mesh);
      this._mesh = null;
    }
    if (this._geo) {
      this._geo.dispose();
      this._geo = null;
    }
  }
}
