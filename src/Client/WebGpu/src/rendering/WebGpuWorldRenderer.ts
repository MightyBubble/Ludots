import type { CameraState } from '../../../Web/src/core/FrameDecoder';
import type { TerrainSnapshot } from '../core/TerrainSnapshot';
import type { ScreenHudWireSink } from '../core/WebGpuFrameStream';
import { WebGpuHudRenderer } from './WebGpuHudRenderer';
import { WebGpuTerrainRenderer, type TerrainRenderStats } from './WebGpuTerrainRenderer';

const DEPTH_FORMAT: GPUTextureFormat = 'depth24plus';

const FLOATS_PER_INSTANCE = 10;
const BYTES_PER_INSTANCE = FLOATS_PER_INSTANCE * Float32Array.BYTES_PER_ELEMENT;
const INITIAL_INSTANCE_CAPACITY = 1024;

const CUBE_VERTICES = new Float32Array([
  -0.5, -0.5,  0.5,  0,  0,  1,   0.5, -0.5,  0.5,  0,  0,  1,   0.5,  0.5,  0.5,  0,  0,  1,
  -0.5, -0.5,  0.5,  0,  0,  1,   0.5,  0.5,  0.5,  0,  0,  1,  -0.5,  0.5,  0.5,  0,  0,  1,
   0.5, -0.5, -0.5,  0,  0, -1,  -0.5, -0.5, -0.5,  0,  0, -1,  -0.5,  0.5, -0.5,  0,  0, -1,
   0.5, -0.5, -0.5,  0,  0, -1,  -0.5,  0.5, -0.5,  0,  0, -1,   0.5,  0.5, -0.5,  0,  0, -1,
  -0.5, -0.5, -0.5, -1,  0,  0,  -0.5, -0.5,  0.5, -1,  0,  0,  -0.5,  0.5,  0.5, -1,  0,  0,
  -0.5, -0.5, -0.5, -1,  0,  0,  -0.5,  0.5,  0.5, -1,  0,  0,  -0.5,  0.5, -0.5, -1,  0,  0,
   0.5, -0.5,  0.5,  1,  0,  0,   0.5, -0.5, -0.5,  1,  0,  0,   0.5,  0.5, -0.5,  1,  0,  0,
   0.5, -0.5,  0.5,  1,  0,  0,   0.5,  0.5, -0.5,  1,  0,  0,   0.5,  0.5,  0.5,  1,  0,  0,
  -0.5,  0.5,  0.5,  0,  1,  0,   0.5,  0.5,  0.5,  0,  1,  0,   0.5,  0.5, -0.5,  0,  1,  0,
  -0.5,  0.5,  0.5,  0,  1,  0,   0.5,  0.5, -0.5,  0,  1,  0,  -0.5,  0.5, -0.5,  0,  1,  0,
  -0.5, -0.5, -0.5,  0, -1,  0,   0.5, -0.5, -0.5,  0, -1,  0,   0.5, -0.5,  0.5,  0, -1,  0,
  -0.5, -0.5, -0.5,  0, -1,  0,   0.5, -0.5,  0.5,  0, -1,  0,  -0.5, -0.5,  0.5,  0, -1,  0,
]);

const SHADER = `
struct CameraUniform {
  viewProjection: mat4x4<f32>,
};

struct VertexInput {
  @location(0) localPosition: vec3<f32>,
  @location(1) normal: vec3<f32>,
  @location(2) instancePosition: vec3<f32>,
  @location(3) instanceScale: vec3<f32>,
  @location(4) instanceColor: vec4<f32>,
};

struct VertexOutput {
  @builtin(position) position: vec4<f32>,
  @location(0) color: vec4<f32>,
  @location(1) normal: vec3<f32>,
};

@group(0) @binding(0) var<uniform> camera: CameraUniform;

@vertex
fn vsMain(input: VertexInput) -> VertexOutput {
  let world = input.instancePosition + input.localPosition * input.instanceScale;
  var output: VertexOutput;
  output.position = camera.viewProjection * vec4<f32>(world, 1.0);
  output.color = input.instanceColor;
  output.normal = normalize(input.normal);
  return output;
}

@fragment
fn fsMain(input: VertexOutput) -> @location(0) vec4<f32> {
  let light = normalize(vec3<f32>(0.35, 0.8, 0.45));
  let shade = 0.42 + max(dot(input.normal, light), 0.0) * 0.58;
  return vec4<f32>(input.color.rgb * shade, input.color.a);
}
`;

export class WebGpuWorldRenderer implements ScreenHudWireSink {
  private readonly device: GPUDevice;
  private readonly context: GPUCanvasContext;
  private readonly pipeline: GPURenderPipeline;
  private readonly bindGroup: GPUBindGroup;
  private readonly cameraBuffer: GPUBuffer;
  private readonly cubeVertexBuffer: GPUBuffer;
  private readonly hudRenderer: WebGpuHudRenderer;
  private readonly terrainRenderer: WebGpuTerrainRenderer;
  private instanceBuffer: GPUBuffer;
  private instanceCapacity = INITIAL_INSTANCE_CAPACITY;
  private instanceCount = 0;
  private depthTexture: GPUTexture | null = null;
  private readonly viewProjection = new Float32Array(16);
  private readonly view = new Float32Array(16);
  private readonly projection = new Float32Array(16);

  private constructor(
    device: GPUDevice,
    context: GPUCanvasContext,
    pipeline: GPURenderPipeline,
    bindGroup: GPUBindGroup,
    cameraBuffer: GPUBuffer,
    cubeVertexBuffer: GPUBuffer,
    instanceBuffer: GPUBuffer,
    hudRenderer: WebGpuHudRenderer,
    terrainRenderer: WebGpuTerrainRenderer,
  ) {
    this.device = device;
    this.context = context;
    this.pipeline = pipeline;
    this.bindGroup = bindGroup;
    this.cameraBuffer = cameraBuffer;
    this.cubeVertexBuffer = cubeVertexBuffer;
    this.instanceBuffer = instanceBuffer;
    this.hudRenderer = hudRenderer;
    this.terrainRenderer = terrainRenderer;
  }

  static async create(canvas: HTMLCanvasElement): Promise<WebGpuWorldRenderer> {
    if (!navigator.gpu) {
      throw new Error('This browser does not expose navigator.gpu, so the WebGPU adapter cannot start in-page.');
    }

    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) {
      throw new Error('The browser could not provide a WebGPU adapter for this machine.');
    }

    const device = await adapter.requestDevice();
    device.addEventListener('uncapturederror', (event) => {
      console.error(`[WebGPU validation] ${event.error.message}`);
    });
    void device.lost.then((info) => {
      console.error(`[WebGPU device lost] ${info.reason}: ${info.message}`);
    });
    const context = canvas.getContext('webgpu');
    if (!context) {
      throw new Error('The WebGPU canvas context could not be created.');
    }

    const format = navigator.gpu.getPreferredCanvasFormat();
    context.configure({
      device,
      format,
      alphaMode: 'opaque',
    });

    const cameraBuffer = device.createBuffer({
      size: 64,
      usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });

    const cubeVertexBuffer = device.createBuffer({
      size: CUBE_VERTICES.byteLength,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(cubeVertexBuffer, 0, CUBE_VERTICES);

    const instanceBuffer = device.createBuffer({
      size: INITIAL_INSTANCE_CAPACITY * BYTES_PER_INSTANCE,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });

    const shaderModule = device.createShaderModule({ code: SHADER });
    const bindGroupLayout = device.createBindGroupLayout({
      entries: [
        {
          binding: 0,
          visibility: GPUShaderStage.VERTEX,
          buffer: { type: 'uniform' },
        },
      ],
    });
    const pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });
    const pipeline = device.createRenderPipeline({
      layout: pipelineLayout,
      vertex: {
        module: shaderModule,
        entryPoint: 'vsMain',
        buffers: [
          {
            arrayStride: 24,
            attributes: [
              { shaderLocation: 0, offset: 0, format: 'float32x3' },
              { shaderLocation: 1, offset: 12, format: 'float32x3' },
            ],
          },
          {
            arrayStride: BYTES_PER_INSTANCE,
            stepMode: 'instance',
            attributes: [
              { shaderLocation: 2, offset: 0, format: 'float32x3' },
              { shaderLocation: 3, offset: 12, format: 'float32x3' },
              { shaderLocation: 4, offset: 24, format: 'float32x4' },
            ],
          },
        ],
      },
      fragment: {
        module: shaderModule,
        entryPoint: 'fsMain',
        targets: [{ format }],
      },
      primitive: {
        topology: 'triangle-list',
        cullMode: 'back',
      },
      depthStencil: {
        format: DEPTH_FORMAT,
        depthWriteEnabled: true,
        depthCompare: 'less',
      },
    });
    const bindGroup = device.createBindGroup({
      layout: bindGroupLayout,
      entries: [{ binding: 0, resource: { buffer: cameraBuffer } }],
    });
    device.pushErrorScope('validation');
    const terrainRenderer = WebGpuTerrainRenderer.create(
      device,
      format,
      DEPTH_FORMAT,
      bindGroupLayout,
      bindGroup,
    );
    const hudRenderer = WebGpuHudRenderer.create(device, format, DEPTH_FORMAT);
    const pipelineValidationError = await device.popErrorScope();
    if (pipelineValidationError) {
      throw new Error(`WebGPU render pipeline validation failed: ${pipelineValidationError.message}`);
    }

    return new WebGpuWorldRenderer(
      device,
      context,
      pipeline,
      bindGroup,
      cameraBuffer,
      cubeVertexBuffer,
      instanceBuffer,
      hudRenderer,
      terrainRenderer,
    );
  }

  resize(width: number, height: number): void {
    const pixelRatio = Math.max(1, window.devicePixelRatio || 1);
    const backingWidth = Math.max(1, Math.round(width * pixelRatio));
    const backingHeight = Math.max(1, Math.round(height * pixelRatio));
    const canvas = this.context.canvas as HTMLCanvasElement;
    if (canvas.width === backingWidth && canvas.height === backingHeight) {
      return;
    }

    canvas.width = backingWidth;
    canvas.height = backingHeight;
    canvas.style.width = `${Math.max(1, Math.round(width))}px`;
    canvas.style.height = `${Math.max(1, Math.round(height))}px`;
    this.depthTexture?.destroy();
    this.depthTexture = this.device.createTexture({
      size: [backingWidth, backingHeight],
      format: DEPTH_FORMAT,
      usage: GPUTextureUsage.RENDER_ATTACHMENT,
    });
    this.hudRenderer.resize(width, height);
  }

  updateCamera(camera: CameraState, aspect: number): void {
    lookAtRh(
      this.view,
      camera.posX,
      camera.posY,
      camera.posZ,
      camera.tgtX,
      camera.tgtY,
      camera.tgtZ,
      camera.upX,
      camera.upY,
      camera.upZ,
    );
    perspectiveRhZo(this.projection, camera.fov, Math.max(0.01, aspect), 0.05, 5000);
    multiplyMat4(this.viewProjection, this.projection, this.view);
    this.device.queue.writeBuffer(this.cameraBuffer, 0, this.viewProjection);
    this.terrainRenderer.updateView(camera.tgtX, camera.tgtZ);
  }

  updateInstancesPacked(data: Float32Array, count: number): void {
    this.ensureInstanceCapacity(count);
    this.instanceCount = count;
    if (count === 0) {
      return;
    }

    this.device.queue.writeBuffer(
      this.instanceBuffer,
      0,
      data.buffer as ArrayBuffer,
      data.byteOffset,
      count * BYTES_PER_INSTANCE,
    );
  }

  updateScreenHud(view: DataView, payloadOffset: number, itemCount: number, byteLength: number): void {
    this.hudRenderer.updateScreenHud(view, payloadOffset, itemCount, byteLength);
  }

  clearScreenHud(): void {
    this.hudRenderer.clearScreenHud();
  }

  setPresentationInterpolationFactor(factor: number): void {
    this.hudRenderer.setInterpolationFactor(factor);
  }

  updateTerrainSnapshot(snapshot: TerrainSnapshot): void {
    this.terrainRenderer.updateSnapshot(snapshot);
  }

  clearTerrain(): void {
    this.terrainRenderer.clear();
  }

  getTerrainStats(): TerrainRenderStats {
    return this.terrainRenderer.getStats();
  }

  getHudStats(): { bars: number; glyphs: number; missingGlyphs: number } {
    return {
      bars: this.hudRenderer.barCount,
      glyphs: this.hudRenderer.glyphCount,
      missingGlyphs: this.hudRenderer.missingGlyphCount,
    };
  }

  render(): void {
    const depthTexture = this.depthTexture;
    if (!depthTexture) {
      return;
    }

    const encoder = this.device.createCommandEncoder();
    const pass = encoder.beginRenderPass({
      colorAttachments: [
        {
          view: this.context.getCurrentTexture().createView(),
          clearValue: this.instanceCount > 0
            ? { r: 0.055, g: 0.085, b: 0.09, a: 1 }
            : { r: 0.035, g: 0.045, b: 0.055, a: 1 },
          loadOp: 'clear',
          storeOp: 'store',
        },
      ],
      depthStencilAttachment: {
        view: depthTexture.createView(),
        depthClearValue: 1,
        depthLoadOp: 'clear',
        depthStoreOp: 'store',
      },
    });
    this.terrainRenderer.render(pass);
    pass.setPipeline(this.pipeline);
    pass.setBindGroup(0, this.bindGroup);
    pass.setVertexBuffer(0, this.cubeVertexBuffer);
    pass.setVertexBuffer(1, this.instanceBuffer);
    pass.draw(36, this.instanceCount);
    this.hudRenderer.render(pass);
    pass.end();
    this.device.queue.submit([encoder.finish()]);
  }

  worldToScreen(x: number, y: number, z: number, width: number, height: number): [number, number] | null {
    const clipX = this.viewProjection[0] * x + this.viewProjection[4] * y + this.viewProjection[8] * z + this.viewProjection[12];
    const clipY = this.viewProjection[1] * x + this.viewProjection[5] * y + this.viewProjection[9] * z + this.viewProjection[13];
    const clipW = this.viewProjection[3] * x + this.viewProjection[7] * y + this.viewProjection[11] * z + this.viewProjection[15];
    if (clipW <= 0.0001) {
      return null;
    }

    const ndcX = clipX / clipW;
    const ndcY = clipY / clipW;
    return [
      (ndcX * 0.5 + 0.5) * width,
      (-ndcY * 0.5 + 0.5) * height,
    ];
  }

  private ensureInstanceCapacity(required: number): void {
    if (required <= this.instanceCapacity) {
      return;
    }

    this.instanceBuffer.destroy();
    while (this.instanceCapacity < required) {
      this.instanceCapacity *= 2;
    }

    this.instanceBuffer = this.device.createBuffer({
      size: this.instanceCapacity * BYTES_PER_INSTANCE,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
  }
}

function perspectiveRhZo(out: Float32Array, fovYDegrees: number, aspect: number, near: number, far: number): void {
  const f = 1 / Math.tan((fovYDegrees * Math.PI) / 360);
  out.fill(0);
  out[0] = f / aspect;
  out[5] = f;
  out[10] = far / (near - far);
  out[11] = -1;
  out[14] = (far * near) / (near - far);
}

function lookAtRh(
  out: Float32Array,
  eyeX: number,
  eyeY: number,
  eyeZ: number,
  centerX: number,
  centerY: number,
  centerZ: number,
  upX: number,
  upY: number,
  upZ: number,
): void {
  let zx = eyeX - centerX;
  let zy = eyeY - centerY;
  let zz = eyeZ - centerZ;
  let len = Math.hypot(zx, zy, zz);
  if (len <= 0.00001) {
    zz = 1;
    len = 1;
  }
  zx /= len;
  zy /= len;
  zz /= len;

  let xx = upY * zz - upZ * zy;
  let xy = upZ * zx - upX * zz;
  let xz = upX * zy - upY * zx;
  len = Math.hypot(xx, xy, xz);
  if (len <= 0.00001) {
    xx = 1;
    xy = 0;
    xz = 0;
    len = 1;
  }
  xx /= len;
  xy /= len;
  xz /= len;

  const yx = zy * xz - zz * xy;
  const yy = zz * xx - zx * xz;
  const yz = zx * xy - zy * xx;

  out[0] = xx;
  out[1] = yx;
  out[2] = zx;
  out[3] = 0;
  out[4] = xy;
  out[5] = yy;
  out[6] = zy;
  out[7] = 0;
  out[8] = xz;
  out[9] = yz;
  out[10] = zz;
  out[11] = 0;
  out[12] = -(xx * eyeX + xy * eyeY + xz * eyeZ);
  out[13] = -(yx * eyeX + yy * eyeY + yz * eyeZ);
  out[14] = -(zx * eyeX + zy * eyeY + zz * eyeZ);
  out[15] = 1;
}

function multiplyMat4(out: Float32Array, a: Float32Array, b: Float32Array): void {
  const a00 = a[0]; const a01 = a[1]; const a02 = a[2]; const a03 = a[3];
  const a10 = a[4]; const a11 = a[5]; const a12 = a[6]; const a13 = a[7];
  const a20 = a[8]; const a21 = a[9]; const a22 = a[10]; const a23 = a[11];
  const a30 = a[12]; const a31 = a[13]; const a32 = a[14]; const a33 = a[15];
  const b00 = b[0]; const b01 = b[1]; const b02 = b[2]; const b03 = b[3];
  const b10 = b[4]; const b11 = b[5]; const b12 = b[6]; const b13 = b[7];
  const b20 = b[8]; const b21 = b[9]; const b22 = b[10]; const b23 = b[11];
  const b30 = b[12]; const b31 = b[13]; const b32 = b[14]; const b33 = b[15];

  out[0] = b00 * a00 + b01 * a10 + b02 * a20 + b03 * a30;
  out[1] = b00 * a01 + b01 * a11 + b02 * a21 + b03 * a31;
  out[2] = b00 * a02 + b01 * a12 + b02 * a22 + b03 * a32;
  out[3] = b00 * a03 + b01 * a13 + b02 * a23 + b03 * a33;
  out[4] = b10 * a00 + b11 * a10 + b12 * a20 + b13 * a30;
  out[5] = b10 * a01 + b11 * a11 + b12 * a21 + b13 * a31;
  out[6] = b10 * a02 + b11 * a12 + b12 * a22 + b13 * a32;
  out[7] = b10 * a03 + b11 * a13 + b12 * a23 + b13 * a33;
  out[8] = b20 * a00 + b21 * a10 + b22 * a20 + b23 * a30;
  out[9] = b20 * a01 + b21 * a11 + b22 * a21 + b23 * a31;
  out[10] = b20 * a02 + b21 * a12 + b22 * a22 + b23 * a32;
  out[11] = b20 * a03 + b21 * a13 + b22 * a23 + b23 * a33;
  out[12] = b30 * a00 + b31 * a10 + b32 * a20 + b33 * a30;
  out[13] = b30 * a01 + b31 * a11 + b32 * a21 + b33 * a31;
  out[14] = b30 * a02 + b31 * a12 + b32 * a22 + b33 * a32;
  out[15] = b30 * a03 + b31 * a13 + b32 * a23 + b33 * a33;
}
