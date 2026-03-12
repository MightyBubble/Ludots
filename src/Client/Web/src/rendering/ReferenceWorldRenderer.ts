import * as THREE from 'three';

const GRID_Y = -0.05;
const GRID_HALF_COUNT = 300;
const GRID_SPACING = 1.0;
const GRID_MAJOR_EVERY = 10;
const AXIS_LENGTH = 2.0;

export class ReferenceWorldRenderer {
  private readonly _scene: THREE.Scene;
  private readonly _gridMaterial = new THREE.LineBasicMaterial({ vertexColors: true });
  private readonly _axesMaterial = new THREE.LineBasicMaterial({ vertexColors: true });
  private readonly _targetMaterial = new THREE.MeshBasicMaterial({ color: 0xffffff });
  private readonly _grid = new THREE.LineSegments(new THREE.BufferGeometry(), this._gridMaterial);
  private readonly _axes = new THREE.LineSegments(new THREE.BufferGeometry(), this._axesMaterial);
  private readonly _target = new THREE.Mesh(new THREE.SphereGeometry(0.2, 12, 12), this._targetMaterial);
  private _startX = Number.NaN;
  private _startZ = Number.NaN;

  constructor(scene: THREE.Scene) {
    this._scene = scene;
    this._scene.add(this._grid);
    this._scene.add(this._axes);
    this._scene.add(this._target);
  }

  update(targetX: number, targetY: number, targetZ: number): void {
    this.updateGrid(targetX, targetZ);
    this.updateAxes(targetX, targetY, targetZ);
  }

  dispose(): void {
    this._scene.remove(this._grid);
    this._scene.remove(this._axes);
    this._scene.remove(this._target);
    this._grid.geometry.dispose();
    this._axes.geometry.dispose();
    this._target.geometry.dispose();
    this._gridMaterial.dispose();
    this._axesMaterial.dispose();
    this._targetMaterial.dispose();
  }

  private updateGrid(targetX: number, targetZ: number): void {
    const extent = GRID_HALF_COUNT * GRID_SPACING;
    const minX = targetX - extent;
    const minZ = targetZ - extent;
    const startX = Math.floor(minX / GRID_SPACING) * GRID_SPACING;
    const startZ = Math.floor(minZ / GRID_SPACING) * GRID_SPACING;

    if (startX === this._startX && startZ === this._startZ) {
      return;
    }

    this._startX = startX;
    this._startZ = startZ;

    const endX = startX + 2 * extent;
    const endZ = startZ + 2 * extent;
    const lineCount = GRID_HALF_COUNT * 2;
    const segmentCount = (lineCount + 1) * 2;
    const positions = new Float32Array(segmentCount * 6);
    const colors = new Float32Array(segmentCount * 6);

    const minor = new THREE.Color(80 / 255, 80 / 255, 80 / 255);
    const major = new THREE.Color(130 / 255, 130 / 255, 130 / 255);

    let offset = 0;
    for (let i = 0; i <= lineCount; i++) {
      const x = startX + i * GRID_SPACING;
      const z = startZ + i * GRID_SPACING;
      const xi = Math.round(x / GRID_SPACING);
      const zi = Math.round(z / GRID_SPACING);
      const xColor = GRID_MAJOR_EVERY > 0 && xi % GRID_MAJOR_EVERY === 0 ? major : minor;
      const zColor = GRID_MAJOR_EVERY > 0 && zi % GRID_MAJOR_EVERY === 0 ? major : minor;

      offset = this.writeSegment(positions, colors, offset, x, GRID_Y, startZ, x, GRID_Y, endZ, xColor);
      offset = this.writeSegment(positions, colors, offset, startX, GRID_Y, z, endX, GRID_Y, z, zColor);
    }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    this._grid.geometry.dispose();
    this._grid.geometry = geometry;
  }

  private updateAxes(targetX: number, targetY: number, targetZ: number): void {
    const positions = new Float32Array([
      targetX, targetY, targetZ, targetX + AXIS_LENGTH, targetY, targetZ,
      targetX, targetY, targetZ, targetX, targetY, targetZ + AXIS_LENGTH,
      targetX, targetY, targetZ, targetX, targetY + AXIS_LENGTH, targetZ,
    ]);

    const colors = new Float32Array([
      1, 0, 0, 1, 0, 0,
      0, 0, 1, 0, 0, 1,
      0, 1, 0, 0, 1, 0,
    ]);

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    this._axes.geometry.dispose();
    this._axes.geometry = geometry;

    this._target.position.set(targetX, targetY, targetZ);
  }

  private writeSegment(
    positions: Float32Array,
    colors: Float32Array,
    offset: number,
    ax: number,
    ay: number,
    az: number,
    bx: number,
    by: number,
    bz: number,
    color: THREE.Color,
  ): number {
    positions[offset] = ax;
    positions[offset + 1] = ay;
    positions[offset + 2] = az;
    positions[offset + 3] = bx;
    positions[offset + 4] = by;
    positions[offset + 5] = bz;

    colors[offset] = color.r;
    colors[offset + 1] = color.g;
    colors[offset + 2] = color.b;
    colors[offset + 3] = color.r;
    colors[offset + 4] = color.g;
    colors[offset + 5] = color.b;
    return offset + 6;
  }
}
