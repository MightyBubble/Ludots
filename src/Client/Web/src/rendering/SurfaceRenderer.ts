import * as THREE from 'three';
import type { MaterialCustomData, MaterialMapEntry, SurfaceItem } from '../core/FrameDecoder';

const VISIBILITY_VISIBLE = 1;
const SURFACE_RENDER_ORDER_BASE = 2000;
const MATERIAL_DOMAIN_SURFACE = 1;
const MATERIAL_FLAG_SUPPORTS_PER_INSTANCE_CUSTOM_DATA = 1 << 0;
const WEB_MATERIAL_SCHEME = 'web.material:';
const WEB_SURFACE_MATERIAL_PROGRAM = 'surface.per_instance_quad';

interface SurfaceEntry {
  mesh: THREE.Mesh;
  material: THREE.ShaderMaterial;
  materialId: number;
  lastSeen: number;
}

interface SurfaceMaterialDefinition {
  sourceUri: string;
  baseColor: THREE.Vector3;
  supportsPerInstanceCustomData: boolean;
}

const SURFACE_VERTEX_SHADER = `
varying vec2 vUv;

void main() {
  vUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}
`;

const SURFACE_FRAGMENT_SHADER = `
uniform float uSlotMask;
uniform vec4 uCustom0;
uniform vec4 uCustom1;
uniform vec4 uCustom2;
uniform vec4 uCustom3;
uniform vec3 uLayerColor;
varying vec2 vUv;

float hasSlot(float slot) {
  return mod(floor(uSlotMask / exp2(slot)), 2.0);
}

void main() {
  vec3 color = uLayerColor;
  color = mix(color, clamp(abs(uCustom0.rgb), 0.0, 1.0), hasSlot(0.0));
  color = mix(color, color * (0.75 + clamp(abs(uCustom1.rgb), 0.0, 1.0) * 0.5), hasSlot(1.0));
  color = mix(color, color + clamp(abs(uCustom2.rgb), 0.0, 1.0) * 0.15, hasSlot(2.0));

  float edge = smoothstep(0.46, 0.50, max(abs(vUv.x - 0.5), abs(vUv.y - 0.5)));
  float alpha = mix(0.38, clamp(abs(uCustom3.a), 0.08, 1.0), hasSlot(3.0));
  gl_FragColor = vec4(mix(color, vec3(1.0), edge * 0.35), alpha);
}
`;

export class SurfaceRenderer {
  private readonly _scene: THREE.Scene;
  private readonly _geometry: THREE.BufferGeometry;
  private readonly _entries = new Map<number, SurfaceEntry>();
  private readonly _materials = new Map<number, SurfaceMaterialDefinition>();
  private _serial = 0;

  constructor(scene: THREE.Scene) {
    this._scene = scene;
    this._geometry = createSurfaceGeometry();
  }

  applyMaterialMap(entries: MaterialMapEntry[]): void {
    this._materials.clear();

    for (const entry of entries) {
      if (entry.domain !== MATERIAL_DOMAIN_SURFACE) {
        continue;
      }

      const material = selectSurfaceMaterial(entry);
      this._materials.set(entry.id, {
        sourceUri: material.sourceUri,
        baseColor: material.baseColor,
        supportsPerInstanceCustomData: (entry.flags & MATERIAL_FLAG_SUPPORTS_PER_INSTANCE_CUSTOM_DATA) !== 0,
      });
    }
  }

  update(items: SurfaceItem[]): void {
    this._serial++;

    for (const item of items) {
      const entry = this.getOrCreateEntry(item);
      entry.lastSeen = this._serial;
      entry.mesh.visible = item.visibility === VISIBILITY_VISIBLE;
      entry.mesh.position.set(item.posX, item.posY, item.posZ);
      entry.mesh.quaternion.set(item.rotX, item.rotY, item.rotZ, item.rotW);
      entry.mesh.scale.set(item.scaleX, item.scaleY, item.scaleZ);
      entry.mesh.renderOrder = SURFACE_RENDER_ORDER_BASE + item.sortId;

      applyCustomDataUniforms(entry.material, item.materialCustomData);
    }

    for (const [stableId, entry] of this._entries) {
      if (entry.lastSeen === this._serial) {
        continue;
      }

      this._scene.remove(entry.mesh);
      entry.material.dispose();
      this._entries.delete(stableId);
    }
  }

  dispose(): void {
    for (const entry of this._entries.values()) {
      this._scene.remove(entry.mesh);
      entry.material.dispose();
    }

    this._entries.clear();
    this._geometry.dispose();
  }

  private getOrCreateEntry(item: SurfaceItem): SurfaceEntry {
    const materialDefinition = this.resolveMaterial(item);
    const existing = this._entries.get(item.stableId);
    if (existing && existing.materialId === item.materialId) {
      return existing;
    }

    if (existing) {
      existing.mesh.material = createSurfaceMaterial(materialDefinition);
      existing.material.dispose();
      existing.material = existing.mesh.material as THREE.ShaderMaterial;
      existing.materialId = item.materialId;
      return existing;
    }

    const material = createSurfaceMaterial(materialDefinition);
    const mesh = new THREE.Mesh(this._geometry, material);
    mesh.frustumCulled = false;
    this._scene.add(mesh);

    const entry = { mesh, material, materialId: item.materialId, lastSeen: this._serial };
    this._entries.set(item.stableId, entry);
    return entry;
  }

  private resolveMaterial(item: SurfaceItem): SurfaceMaterialDefinition {
    const material = this._materials.get(item.materialId);
    if (!material) {
      throw new Error(`Surface material id ${item.materialId} is not present in the Web material map.`);
    }

    if (!material.supportsPerInstanceCustomData && item.materialCustomData.slotMask !== 0) {
      throw new Error(`Surface material '${material.sourceUri}' does not support per-instance custom data.`);
    }

    return material;
  }
}

function createSurfaceGeometry(): THREE.BufferGeometry {
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute(
    'position',
    new THREE.BufferAttribute(new Float32Array([
      -0.5, 0, -0.5,
       0.5, 0, -0.5,
       0.5, 0,  0.5,
      -0.5, 0,  0.5,
    ]), 3),
  );
  geometry.setAttribute(
    'uv',
    new THREE.BufferAttribute(new Float32Array([
      0, 0,
      1, 0,
      1, 1,
      0, 1,
    ]), 2),
  );
  geometry.setIndex([0, 1, 2, 0, 2, 3]);
  geometry.computeVertexNormals();
  return geometry;
}

function createSurfaceMaterial(definition: SurfaceMaterialDefinition): THREE.ShaderMaterial {
  return new THREE.ShaderMaterial({
    uniforms: {
      uSlotMask: { value: 0 },
      uCustom0: { value: new THREE.Vector4() },
      uCustom1: { value: new THREE.Vector4() },
      uCustom2: { value: new THREE.Vector4() },
      uCustom3: { value: new THREE.Vector4() },
      uLayerColor: { value: definition.baseColor.clone() },
    },
    vertexShader: SURFACE_VERTEX_SHADER,
    fragmentShader: SURFACE_FRAGMENT_SHADER,
    transparent: true,
    depthWrite: false,
    side: THREE.DoubleSide,
  });
}

function applyCustomDataUniforms(material: THREE.ShaderMaterial, data: MaterialCustomData): void {
  material.uniforms.uSlotMask.value = data.slotMask;
  setVector4(material.uniforms.uCustom0.value, data.slots[0]);
  setVector4(material.uniforms.uCustom1.value, data.slots[1]);
  setVector4(material.uniforms.uCustom2.value, data.slots[2]);
  setVector4(material.uniforms.uCustom3.value, data.slots[3]);
}

function setVector4(target: THREE.Vector4, value: [number, number, number, number]): void {
  target.set(value[0], value[1], value[2], value[3]);
}

function selectSurfaceMaterial(entry: MaterialMapEntry): Pick<SurfaceMaterialDefinition, 'sourceUri' | 'baseColor'> {
  for (const uri of entry.sourceUris) {
    if (!uri.startsWith(WEB_MATERIAL_SCHEME)) {
      continue;
    }

    return parseWebSurfaceMaterial(entry, uri);
  }

  throw new Error(`Surface material '${entry.key}' has no Web-supported sourceUri.`);
}

function parseWebSurfaceMaterial(entry: MaterialMapEntry, sourceUri: string): Pick<SurfaceMaterialDefinition, 'sourceUri' | 'baseColor'> {
  const payload = sourceUri.slice(WEB_MATERIAL_SCHEME.length);
  const questionIndex = payload.indexOf('?');
  const program = questionIndex >= 0 ? payload.slice(0, questionIndex) : payload;
  const query = questionIndex >= 0 ? payload.slice(questionIndex + 1) : '';

  if (program !== WEB_SURFACE_MATERIAL_PROGRAM) {
    throw new Error(`Unsupported Web surface material program '${program}' for material '${entry.key}'.`);
  }

  const baseColor = new URLSearchParams(query).get('baseColor');
  if (!baseColor) {
    throw new Error(`Web surface material '${entry.key}' must declare baseColor in '${sourceUri}'.`);
  }

  return {
    sourceUri,
    baseColor: parseBaseColor(entry, sourceUri, baseColor),
  };
}

function parseBaseColor(entry: MaterialMapEntry, sourceUri: string, baseColor: string): THREE.Vector3 {
  const parts = baseColor.split(',');
  if (parts.length !== 3) {
    throw new Error(`Web surface material '${entry.key}' baseColor in '${sourceUri}' must have three comma-separated numbers.`);
  }

  const values = parts.map((part) => Number.parseFloat(part));
  if (values.some((value) => !Number.isFinite(value) || value < 0 || value > 1)) {
    throw new Error(`Web surface material '${entry.key}' baseColor in '${sourceUri}' must contain finite values in [0, 1].`);
  }

  return new THREE.Vector3(values[0], values[1], values[2]);
}
