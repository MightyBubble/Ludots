import * as THREE from 'three';
import type { CameraState } from '../core/FrameProtocol';

/**
 * Manages the Three.js camera.
 * Applies server camera state with smooth interpolation for lag-free visuals.
 * Future: add local camera controls that override server state.
 */
export class ClientCameraController {
  private readonly _camera: THREE.PerspectiveCamera;
  private readonly _targetPos = new THREE.Vector3();
  private readonly _targetLookAt = new THREE.Vector3();
  private _smoothSpeed = 15.0;
  private _hasReceivedState = false;

  constructor(camera: THREE.PerspectiveCamera) {
    this._camera = camera;
  }

  /** Apply server camera state. Smoothly interpolates to new position. */
  applyServerState(state: CameraState): void {
    this._targetPos.set(state.posX, state.posY, state.posZ);
    this._targetLookAt.set(state.targetX, state.targetY, state.targetZ);

    if (state.fovYDeg > 0 && Math.abs(this._camera.fov - state.fovYDeg) > 0.01) {
      this._camera.fov = state.fovYDeg;
      this._camera.updateProjectionMatrix();
    }

    if (!this._hasReceivedState) {
      this._camera.position.copy(this._targetPos);
      this._camera.lookAt(this._targetLookAt);
      this._hasReceivedState = true;
    }
  }

  /** Called each animation frame. Smoothly moves camera toward target. */
  tick(dt: number): void {
    if (!this._hasReceivedState) return;

    const t = Math.min(this._smoothSpeed * dt, 1);
    this._camera.position.lerp(this._targetPos, t);
    // For lookAt interpolation, directly set (Three.js lookAt is immediate)
    this._camera.lookAt(
      THREE.MathUtils.lerp(this._camera.getWorldDirection(new THREE.Vector3()).x, this._targetLookAt.x, t),
      this._targetLookAt.y,
      this._targetLookAt.z,
    );
    this._camera.lookAt(this._targetLookAt);
  }
}
