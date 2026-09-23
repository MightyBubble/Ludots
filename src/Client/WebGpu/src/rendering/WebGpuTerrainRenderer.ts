import type { TerrainChunkSnapshot, TerrainSnapshot } from '../core/TerrainSnapshot';

const POSITION_NORMAL_FLOATS = 6;
const POSITION_NORMAL_BYTES = POSITION_NORMAL_FLOATS * Float32Array.BYTES_PER_ELEMENT;
const COLOR_BYTES = 4;
const VISIBLE_RADIUS_METERS = 1200;

const TERRAIN_SHADER = `
struct CameraUniform {
  viewProjection: mat4x4<f32>,
};

struct VertexInput {
  @location(0) position: vec3<f32>,
  @location(1) normal: vec3<f32>,
  @location(2) color: vec4<f32>,
};

struct VertexOutput {
  @builtin(position) position: vec4<f32>,
  @location(0) worldPosition: vec3<f32>,
  @location(1) normal: vec3<f32>,
  @location(2) color: vec4<f32>,
};

@group(0) @binding(0) var<uniform> camera: CameraUniform;

@vertex
fn vsMain(input: VertexInput) -> VertexOutput {
  var output: VertexOutput;
  output.position = camera.viewProjection * vec4<f32>(input.position, 1.0);
  output.worldPosition = input.position;
  output.normal = normalize(input.normal);
  output.color = input.color;
  return output;
}

@fragment
fn fsMain(input: VertexOutput) -> @location(0) vec4<f32> {
  let lightPosition = vec3<f32>(50.0, 200.0, 100.0);
  let lightDirection = normalize(lightPosition - input.worldPosition);
  let diffuse = abs(dot(normalize(input.normal), lightDirection));
  let lit = input.color.rgb * (0.45 + 0.55 * diffuse);
  return vec4<f32>(clamp(lit, vec3<f32>(0.0), vec3<f32>(1.0)), input.color.a);
}
`;

interface TerrainChunkGpu {
  chunkX: number;
  chunkY: number;
  revision: number;
  leftMeters: number;
  topMeters: number;
  rightMeters: number;
  bottomMeters: number;
  vertexCount: number;
  indexCount: number;
  positionNormalBuffer: GPUBuffer;
  colorBuffer: GPUBuffer;
  indexBuffer: GPUBuffer;
}

export interface TerrainRenderStats {
  revision: number | null;
  chunks: number;
  visibleChunks: number;
  vertices: number;
  visibleVertices: number;
}

export class WebGpuTerrainRenderer {
  private readonly device: GPUDevice;
  private readonly pipeline: GPURenderPipeline;
  private readonly cameraBindGroup: GPUBindGroup;
  private chunks: TerrainChunkGpu[] = [];
  private revision: number | null = null;
  private targetX = 0;
  private targetZ = 0;
  private totalVertices = 0;
  private visibleChunks = 0;
  private visibleVertices = 0;

  private constructor(
    device: GPUDevice,
    pipeline: GPURenderPipeline,
    cameraBindGroup: GPUBindGroup,
  ) {
    this.device = device;
    this.pipeline = pipeline;
    this.cameraBindGroup = cameraBindGroup;
  }

  static create(
    device: GPUDevice,
    format: GPUTextureFormat,
    depthFormat: GPUTextureFormat,
    cameraBindGroupLayout: GPUBindGroupLayout,
    cameraBindGroup: GPUBindGroup,
  ): WebGpuTerrainRenderer {
    const shader = device.createShaderModule({ code: TERRAIN_SHADER });
    const pipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [cameraBindGroupLayout] }),
      vertex: {
        module: shader,
        entryPoint: 'vsMain',
        buffers: [
          {
            arrayStride: POSITION_NORMAL_BYTES,
            attributes: [
              { shaderLocation: 0, offset: 0, format: 'float32x3' },
              { shaderLocation: 1, offset: 12, format: 'float32x3' },
            ],
          },
          {
            arrayStride: COLOR_BYTES,
            attributes: [{ shaderLocation: 2, offset: 0, format: 'unorm8x4' }],
          },
        ],
      },
      fragment: { module: shader, entryPoint: 'fsMain', targets: [{ format }] },
      primitive: { topology: 'triangle-list', cullMode: 'none' },
      depthStencil: {
        format: depthFormat,
        depthWriteEnabled: true,
        depthCompare: 'less',
      },
    });
    return new WebGpuTerrainRenderer(device, pipeline, cameraBindGroup);
  }

  updateSnapshot(snapshot: TerrainSnapshot): void {
    this.clear();
    const chunks = new Array<TerrainChunkGpu>(snapshot.chunks.length);
    let totalVertices = 0;
    try {
      for (let index = 0; index < snapshot.chunks.length; index++) {
        const gpuChunk = this.createChunk(snapshot.chunks[index]);
        chunks[index] = gpuChunk;
        totalVertices += gpuChunk.vertexCount;
      }
    } catch (error) {
      destroyChunks(chunks);
      throw error;
    }

    this.chunks = chunks;
    this.revision = snapshot.revision;
    this.totalVertices = totalVertices;
  }

  clear(): void {
    destroyChunks(this.chunks);
    this.chunks = [];
    this.revision = null;
    this.totalVertices = 0;
    this.visibleChunks = 0;
    this.visibleVertices = 0;
  }

  updateView(targetX: number, targetZ: number): void {
    this.targetX = targetX;
    this.targetZ = targetZ;
  }

  render(pass: GPURenderPassEncoder): void {
    this.visibleChunks = 0;
    this.visibleVertices = 0;
    if (this.chunks.length === 0) {
      return;
    }

    const minX = this.targetX - VISIBLE_RADIUS_METERS;
    const maxX = this.targetX + VISIBLE_RADIUS_METERS;
    const minZ = this.targetZ - VISIBLE_RADIUS_METERS;
    const maxZ = this.targetZ + VISIBLE_RADIUS_METERS;
    pass.setPipeline(this.pipeline);
    pass.setBindGroup(0, this.cameraBindGroup);
    for (let index = 0; index < this.chunks.length; index++) {
      const chunk = this.chunks[index];
      if (chunk.rightMeters < minX || chunk.leftMeters > maxX ||
          chunk.bottomMeters < minZ || chunk.topMeters > maxZ) {
        continue;
      }

      pass.setVertexBuffer(0, chunk.positionNormalBuffer);
      pass.setVertexBuffer(1, chunk.colorBuffer);
      pass.setIndexBuffer(chunk.indexBuffer, 'uint32');
      pass.drawIndexed(chunk.indexCount);
      this.visibleChunks++;
      this.visibleVertices += chunk.vertexCount;
    }
  }

  getStats(): TerrainRenderStats {
    return {
      revision: this.revision,
      chunks: this.chunks.length,
      visibleChunks: this.visibleChunks,
      vertices: this.totalVertices,
      visibleVertices: this.visibleVertices,
    };
  }

  private createChunk(chunk: TerrainChunkSnapshot): TerrainChunkGpu {
    const columns = chunk.sampleColumns;
    const rows = chunk.sampleRows;
    const vertexCount = columns * rows;
    const indexCount = (columns - 1) * (rows - 1) * 6;
    const positionNormals = new Float32Array(vertexCount * POSITION_NORMAL_FLOATS);
    const colors = new Uint8Array(vertexCount * COLOR_BYTES);
    const indices = new Uint32Array(indexCount);

    let minHeightCm = Number.POSITIVE_INFINITY;
    let maxHeightCm = Number.NEGATIVE_INFINITY;
    for (let index = 0; index < chunk.heightsCm.length; index++) {
      const heightCm = chunk.heightsCm[index];
      minHeightCm = Math.min(minHeightCm, heightCm);
      maxHeightCm = Math.max(maxHeightCm, heightCm);
    }
    if (!Number.isFinite(minHeightCm) || !Number.isFinite(maxHeightCm)) {
      throw new Error(`Web terrain chunk (${chunk.chunkX},${chunk.chunkY}) has no finite height range.`);
    }
    const heightRangeCm = Math.max(1, maxHeightCm - minHeightCm);

    for (let y = 0; y < rows; y++) {
      for (let x = 0; x < columns; x++) {
        const vertex = y * columns + x;
        const left = Math.max(0, x - 1);
        const right = Math.min(columns - 1, x + 1);
        const top = Math.max(0, y - 1);
        const bottom = Math.min(rows - 1, y + 1);
        const hLeft = chunk.heightsCm[y * columns + left];
        const hRight = chunk.heightsCm[y * columns + right];
        const hTop = chunk.heightsCm[top * columns + x];
        const hBottom = chunk.heightsCm[bottom * columns + x];
        const dx = Math.max(1, (right - left) * chunk.sampleStepXCm);
        const dz = Math.max(1, (bottom - top) * chunk.sampleStepYCm);
        let normalX = -(hRight - hLeft) / dx;
        let normalY = 1;
        let normalZ = -(hBottom - hTop) / dz;
        const normalLength = Math.hypot(normalX, normalY, normalZ);
        normalX /= normalLength;
        normalY /= normalLength;
        normalZ /= normalLength;

        let write = vertex * POSITION_NORMAL_FLOATS;
        positionNormals[write++] = (chunk.bounds.x + x * chunk.sampleStepXCm) * 0.01;
        positionNormals[write++] = chunk.heightsCm[vertex] * 0.01;
        positionNormals[write++] = (chunk.bounds.y + y * chunk.sampleStepYCm) * 0.01;
        positionNormals[write++] = normalX;
        positionNormals[write++] = normalY;
        positionNormals[write] = normalZ;

        const heightBand = clamp01((chunk.heightsCm[vertex] - minHeightCm) / heightRangeCm);
        const slope = clamp01(1 - normalY);
        write = vertex * COLOR_BYTES;
        resolveTerrainColor(heightBand, slope, colors, write);
        colors[write + 3] = 255;
      }
    }

    let cursor = 0;
    for (let y = 0; y < rows - 1; y++) {
      for (let x = 0; x < columns - 1; x++) {
        const p00 = y * columns + x;
        const p10 = p00 + 1;
        const p01 = p00 + columns;
        const p11 = p01 + 1;
        indices[cursor++] = p00;
        indices[cursor++] = p01;
        indices[cursor++] = p10;
        indices[cursor++] = p11;
        indices[cursor++] = p10;
        indices[cursor++] = p01;
      }
    }

    const positionNormalBuffer = createGpuBuffer(this.device, positionNormals, GPUBufferUsage.VERTEX);
    const colorBuffer = createGpuBuffer(this.device, colors, GPUBufferUsage.VERTEX);
    const indexBuffer = createGpuBuffer(this.device, indices, GPUBufferUsage.INDEX);
    return {
      chunkX: chunk.chunkX,
      chunkY: chunk.chunkY,
      revision: chunk.revision,
      leftMeters: chunk.bounds.x * 0.01,
      topMeters: chunk.bounds.y * 0.01,
      rightMeters: (chunk.bounds.x + chunk.bounds.width) * 0.01,
      bottomMeters: (chunk.bounds.y + chunk.bounds.height) * 0.01,
      vertexCount,
      indexCount,
      positionNormalBuffer,
      colorBuffer,
      indexBuffer,
    };
  }
}

function createGpuBuffer(
  device: GPUDevice,
  data: Float32Array | Uint8Array | Uint32Array,
  usage: GPUBufferUsageFlags,
): GPUBuffer {
  const buffer = device.createBuffer({
    size: data.byteLength,
    usage: usage | GPUBufferUsage.COPY_DST,
  });
  device.queue.writeBuffer(
    buffer,
    0,
    data.buffer as ArrayBuffer,
    data.byteOffset,
    data.byteLength,
  );
  return buffer;
}

function destroyChunks(chunks: readonly (TerrainChunkGpu | undefined)[]): void {
  for (let index = 0; index < chunks.length; index++) {
    const chunk = chunks[index];
    if (!chunk) continue;
    chunk.positionNormalBuffer.destroy();
    chunk.colorBuffer.destroy();
    chunk.indexBuffer.destroy();
  }
}

function resolveTerrainColor(heightBand: number, slope: number, output: Uint8Array, offset: number): void {
  const low = [35, 86, 88] as const;
  const mid = [82, 143, 84] as const;
  const high = [190, 174, 108] as const;
  const peak = [226, 220, 184] as const;
  let from: readonly [number, number, number];
  let to: readonly [number, number, number];
  let factor: number;
  if (heightBand < 0.5) {
    from = low;
    to = mid;
    factor = heightBand * 2;
  } else if (heightBand < 0.82) {
    from = mid;
    to = high;
    factor = (heightBand - 0.5) / 0.32;
  } else {
    from = high;
    to = peak;
    factor = (heightBand - 0.82) / 0.18;
  }

  const shade = 1 - Math.min(0.42, slope * 0.42);
  output[offset] = clampByte((from[0] + (to[0] - from[0]) * factor) * shade);
  output[offset + 1] = clampByte((from[1] + (to[1] - from[1]) * factor) * shade);
  output[offset + 2] = clampByte((from[2] + (to[2] - from[2]) * factor) * shade);
}

function clamp01(value: number): number {
  return Math.max(0, Math.min(1, value));
}

function clampByte(value: number): number {
  return Math.max(0, Math.min(255, Math.round(value)));
}
