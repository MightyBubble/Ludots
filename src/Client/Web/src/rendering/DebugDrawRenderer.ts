import * as THREE from 'three';
import type { DebugLine, DebugCircle, DebugBox } from '../core/FrameProtocol';

/**
 * Renders debug draw commands (lines, circles, boxes) using Three.js LineSegments.
 * Recreated each frame from server data.
 */
export class DebugDrawRenderer {
  private readonly _scene: THREE.Scene;
  private _linesMesh: THREE.LineSegments | null = null;
  private _lineGeo: THREE.BufferGeometry | null = null;
  private _lineMat: THREE.LineBasicMaterial;
  private readonly _planeY = 0.35;

  constructor(scene: THREE.Scene) {
    this._scene = scene;
    this._lineMat = new THREE.LineBasicMaterial({ vertexColors: true });
  }

  update(lines: DebugLine[], circles: DebugCircle[], boxes: DebugBox[]): void {
    this._clear();

    const totalSegments = lines.length + circles.length * 24 + boxes.length * 4;
    if (totalSegments === 0) return;

    const positions = new Float32Array(totalSegments * 6);
    const colors = new Float32Array(totalSegments * 6);
    let idx = 0;

    for (const line of lines) {
      const r = line.r / 255, g = line.g / 255, b = line.b / 255;
      positions[idx] = line.ax; positions[idx + 1] = this._planeY; positions[idx + 2] = line.ay;
      positions[idx + 3] = line.bx; positions[idx + 4] = this._planeY; positions[idx + 5] = line.by;
      colors[idx] = r; colors[idx + 1] = g; colors[idx + 2] = b;
      colors[idx + 3] = r; colors[idx + 4] = g; colors[idx + 5] = b;
      idx += 6;
    }

    for (const circ of circles) {
      const r = circ.r / 255, g = circ.g / 255, b2 = circ.b / 255;
      const segs = 24;
      const step = (Math.PI * 2) / segs;
      for (let s = 0; s < segs; s++) {
        const a0 = s * step, a1 = (s + 1) * step;
        positions[idx] = circ.cx + Math.cos(a0) * circ.radius;
        positions[idx + 1] = this._planeY;
        positions[idx + 2] = circ.cy + Math.sin(a0) * circ.radius;
        positions[idx + 3] = circ.cx + Math.cos(a1) * circ.radius;
        positions[idx + 4] = this._planeY;
        positions[idx + 5] = circ.cy + Math.sin(a1) * circ.radius;
        colors[idx] = r; colors[idx + 1] = g; colors[idx + 2] = b2;
        colors[idx + 3] = r; colors[idx + 4] = g; colors[idx + 5] = b2;
        idx += 6;
      }
    }

    for (const box of boxes) {
      const r = box.r / 255, g = box.g / 255, b2 = box.b / 255;
      const cos = Math.cos(box.rotationRadians), sin = Math.sin(box.rotationRadians);
      const hw = box.halfWidth, hh = box.halfHeight;
      const corners = [
        [-hw, -hh], [hw, -hh], [hw, hh], [-hw, hh]
      ].map(([lx, ly]) => [
        box.cx + lx * cos - ly * sin,
        box.cy + lx * sin + ly * cos
      ]);
      for (let e = 0; e < 4; e++) {
        const [x0, y0] = corners[e];
        const [x1, y1] = corners[(e + 1) % 4];
        positions[idx] = x0; positions[idx + 1] = this._planeY; positions[idx + 2] = y0;
        positions[idx + 3] = x1; positions[idx + 4] = this._planeY; positions[idx + 5] = y1;
        colors[idx] = r; colors[idx + 1] = g; colors[idx + 2] = b2;
        colors[idx + 3] = r; colors[idx + 4] = g; colors[idx + 5] = b2;
        idx += 6;
      }
    }

    this._lineGeo = new THREE.BufferGeometry();
    this._lineGeo.setAttribute('position', new THREE.BufferAttribute(positions.slice(0, idx), 3));
    this._lineGeo.setAttribute('color', new THREE.BufferAttribute(colors.slice(0, idx), 3));

    this._linesMesh = new THREE.LineSegments(this._lineGeo, this._lineMat);
    this._scene.add(this._linesMesh);
  }

  dispose(): void {
    this._clear();
    this._lineMat.dispose();
  }

  private _clear(): void {
    if (this._linesMesh) {
      this._scene.remove(this._linesMesh);
      this._linesMesh = null;
    }
    if (this._lineGeo) {
      this._lineGeo.dispose();
      this._lineGeo = null;
    }
  }
}
