import * as THREE from 'three';

/**
 * ViewFrustum - efficient frustum culling for chunk-level visibility.
 * Uses the camera's projection matrix to compute world-space frustum planes.
 */
export class ViewFrustum {
    private planes: THREE.Plane[] = [];
    private tmpBox = new THREE.Box3();
    private camera: THREE.PerspectiveCamera;

    constructor(camera: THREE.PerspectiveCamera) {
        this.camera = camera;
        this.update();
    }

    /** Recompute frustum planes from current camera state. */
    update(): void {
        const projScreenMatrix = new THREE.Matrix4();
        projScreenMatrix.multiplyMatrices(
            this.camera.projectionMatrix,
            this.camera.matrixWorldInverse
        );
        const frustum = new THREE.Frustum();
        frustum.setFromProjectionMatrix(projScreenMatrix);
        this.planes = frustum.planes.slice();
    }

    /**
     * Test if a world-space AABB (min, max) is visible.
     * Returns true if the box intersects or is inside the frustum.
     */
    intersectsBox(minX: number, minY: number, minZ: number, maxX: number, maxY: number, maxZ: number): boolean {
        this.tmpBox.min.set(minX, minY, minZ);
        this.tmpBox.max.set(maxX, maxY, maxZ);

        for (let i = 0; i < 6; i++) {
            const plane = this.planes[i];
            const nx = plane.normal.x;
            const ny = plane.normal.y;
            const nz = plane.normal.z;
            const d = plane.constant;

            const cx = nx > 0 ? this.tmpBox.max.x : this.tmpBox.min.x;
            const cy = ny > 0 ? this.tmpBox.max.y : this.tmpBox.min.y;
            const cz = nz > 0 ? this.tmpBox.max.z : this.tmpBox.min.z;

            if (nx * cx + ny * cy + nz * cz + d < 0) {
                return false; // outside this plane
            }
        }
        return true; // inside or intersecting all planes
    }

    /**
     * Test multiple chunk AABBs at once for bulk culling.
     * @param boxes Array of [cx, cy, minX, minY, minZ, maxX, maxY, maxZ]
     * @returns Set of visible chunk keys "cx,cy"
     */
    cullChunks(boxes: Array<{ cx: number; cy: number; minX: number; minY: number; minZ: number; maxX: number; maxY: number; maxZ: number }>): Set<string> {
        const visible = new Set<string>();
        for (let i = 0; i < boxes.length; i++) {
            const b = boxes[i];
            if (this.intersectsBox(b.minX, b.minY, b.minZ, b.maxX, b.maxY, b.maxZ)) {
                visible.add(`${b.cx},${b.cy}`);
            }
        }
        return visible;
    }
}
