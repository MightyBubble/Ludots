import type { ScreenHudWireSink } from '../core/WebGpuFrameStream';

const HUD_ITEM_BYTES = 113;
const HUD_BAR = 1;
const HUD_TEXT = 2;

const BAR_FLOATS = 15;
const BAR_BYTES = BAR_FLOATS * Float32Array.BYTES_PER_ELEMENT;
const GLYPH_FLOATS = 12;
const GLYPH_BYTES = GLYPH_FLOATS * Float32Array.BYTES_PER_ELEMENT;
const INITIAL_BAR_CAPACITY = 1024;
const INITIAL_GLYPH_CAPACITY = 4096;

const FIRST_GLYPH = 32;
const LAST_GLYPH = 126;
const ATLAS_COLUMNS = 16;
const ATLAS_CELL_WIDTH = 16;
const ATLAS_CELL_HEIGHT = 24;
const ATLAS_GLYPH_COUNT = LAST_GLYPH - FIRST_GLYPH + 1;
const ATLAS_ROWS = Math.ceil(ATLAS_GLYPH_COUNT / ATLAS_COLUMNS);

const BAR_SHADER = `
struct ViewportUniform {
  size: vec2<f32>,
  interpolation: f32,
  padding: f32,
};

struct BarInput {
  @location(0) rect: vec4<f32>,
  @location(1) background: vec4<f32>,
  @location(2) foreground: vec4<f32>,
  @location(3) fill: f32,
  @location(4) previousOrigin: vec2<f32>,
};

struct BarOutput {
  @builtin(position) position: vec4<f32>,
  @location(0) uv: vec2<f32>,
  @location(1) background: vec4<f32>,
  @location(2) foreground: vec4<f32>,
  @location(3) fill: f32,
};

@group(0) @binding(0) var<uniform> viewport: ViewportUniform;

@vertex
fn vsMain(input: BarInput, @builtin(vertex_index) vertexIndex: u32) -> BarOutput {
  let corners = array<vec2<f32>, 6>(
    vec2<f32>(0.0, 0.0), vec2<f32>(1.0, 0.0), vec2<f32>(1.0, 1.0),
    vec2<f32>(0.0, 0.0), vec2<f32>(1.0, 1.0), vec2<f32>(0.0, 1.0)
  );
  let uv = corners[vertexIndex];
  let origin = mix(input.previousOrigin, input.rect.xy, viewport.interpolation);
  let pixel = origin + uv * input.rect.zw;
  var output: BarOutput;
  output.position = vec4<f32>(
    pixel.x / viewport.size.x * 2.0 - 1.0,
    1.0 - pixel.y / viewport.size.y * 2.0,
    0.0,
    1.0
  );
  output.uv = uv;
  output.background = input.background;
  output.foreground = input.foreground;
  output.fill = input.fill;
  return output;
}

@fragment
fn fsMain(input: BarOutput) -> @location(0) vec4<f32> {
  let edge = min(min(input.uv.x, 1.0 - input.uv.x), min(input.uv.y, 1.0 - input.uv.y));
  if (edge < 0.08) {
    return vec4<f32>(0.0, 0.0, 0.0, max(input.background.a, input.foreground.a));
  }
  if (input.uv.x <= clamp(input.fill, 0.0, 1.0)) {
    return input.foreground;
  }
  return input.background;
}
`;

const GLYPH_SHADER = `
struct ViewportUniform {
  size: vec2<f32>,
  interpolation: f32,
  padding: f32,
};

struct GlyphInput {
  @location(0) rect: vec4<f32>,
  @location(1) uvRect: vec4<f32>,
  @location(2) color: vec4<f32>,
};

struct GlyphOutput {
  @builtin(position) position: vec4<f32>,
  @location(0) uv: vec2<f32>,
  @location(1) color: vec4<f32>,
};

@group(0) @binding(0) var<uniform> viewport: ViewportUniform;
@group(1) @binding(0) var glyphSampler: sampler;
@group(1) @binding(1) var glyphAtlas: texture_2d<f32>;

@vertex
fn vsMain(input: GlyphInput, @builtin(vertex_index) vertexIndex: u32) -> GlyphOutput {
  let corners = array<vec2<f32>, 6>(
    vec2<f32>(0.0, 0.0), vec2<f32>(1.0, 0.0), vec2<f32>(1.0, 1.0),
    vec2<f32>(0.0, 0.0), vec2<f32>(1.0, 1.0), vec2<f32>(0.0, 1.0)
  );
  let corner = corners[vertexIndex];
  let pixel = input.rect.xy + corner * input.rect.zw;
  var output: GlyphOutput;
  output.position = vec4<f32>(
    pixel.x / viewport.size.x * 2.0 - 1.0,
    1.0 - pixel.y / viewport.size.y * 2.0,
    0.0,
    1.0
  );
  output.uv = mix(input.uvRect.xy, input.uvRect.zw, corner);
  output.color = input.color;
  return output;
}

@fragment
fn fsMain(input: GlyphOutput) -> @location(0) vec4<f32> {
  let coverage = textureSample(glyphAtlas, glyphSampler, input.uv).a;
  return vec4<f32>(input.color.rgb, input.color.a * coverage);
}
`;

export class WebGpuHudRenderer implements ScreenHudWireSink {
  private readonly _device: GPUDevice;
  private readonly _barPipeline: GPURenderPipeline;
  private readonly _glyphPipeline: GPURenderPipeline;
  private readonly _viewportBindGroup: GPUBindGroup;
  private readonly _atlasBindGroup: GPUBindGroup;
  private readonly _viewportBuffer: GPUBuffer;
  private readonly _viewportData = new Float32Array(4);
  private readonly _textDecoder = new TextDecoder();
  private readonly _floatBits = new DataView(new ArrayBuffer(4));
  private readonly _digitScratch = new Int32Array(16);
  private readonly _legacyStrings: string[] = [];
  private readonly _templates = new Map<number, string>();

  private _barBuffer: GPUBuffer;
  private _glyphBuffer: GPUBuffer;
  private _barScratch = new Float32Array(INITIAL_BAR_CAPACITY * BAR_FLOATS);
  private _barStableIds = new Int32Array(INITIAL_BAR_CAPACITY);
  private _glyphScratch = new Float32Array(INITIAL_GLYPH_CAPACITY * GLYPH_FLOATS);
  private _barCapacity = INITIAL_BAR_CAPACITY;
  private _glyphCapacity = INITIAL_GLYPH_CAPACITY;
  private _barCount = 0;
  private _glyphCount = 0;
  private _missingGlyphCount = 0;
  private _interpolationFactor = 1;

  private constructor(
    device: GPUDevice,
    barPipeline: GPURenderPipeline,
    glyphPipeline: GPURenderPipeline,
    viewportBindGroup: GPUBindGroup,
    atlasBindGroup: GPUBindGroup,
    viewportBuffer: GPUBuffer,
    barBuffer: GPUBuffer,
    glyphBuffer: GPUBuffer,
  ) {
    this._device = device;
    this._barPipeline = barPipeline;
    this._glyphPipeline = glyphPipeline;
    this._viewportBindGroup = viewportBindGroup;
    this._atlasBindGroup = atlasBindGroup;
    this._viewportBuffer = viewportBuffer;
    this._barBuffer = barBuffer;
    this._glyphBuffer = glyphBuffer;
  }

  static create(
    device: GPUDevice,
    format: GPUTextureFormat,
    depthFormat: GPUTextureFormat,
  ): WebGpuHudRenderer {
    const viewportLayout = device.createBindGroupLayout({
      entries: [{ binding: 0, visibility: GPUShaderStage.VERTEX, buffer: { type: 'uniform' } }],
    });
    const atlasLayout = device.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
        { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
      ],
    });

    const blend: GPUBlendState = {
      color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
      alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' },
    };
    const barModule = device.createShaderModule({ code: BAR_SHADER });
    const glyphModule = device.createShaderModule({ code: GLYPH_SHADER });
    const barPipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [viewportLayout] }),
      vertex: {
        module: barModule,
        entryPoint: 'vsMain',
        buffers: [{
          arrayStride: BAR_BYTES,
          stepMode: 'instance',
          attributes: [
            { shaderLocation: 0, offset: 0, format: 'float32x4' },
            { shaderLocation: 1, offset: 16, format: 'float32x4' },
            { shaderLocation: 2, offset: 32, format: 'float32x4' },
            { shaderLocation: 3, offset: 48, format: 'float32' },
            { shaderLocation: 4, offset: 52, format: 'float32x2' },
          ],
        }],
      },
      fragment: { module: barModule, entryPoint: 'fsMain', targets: [{ format, blend }] },
      primitive: { topology: 'triangle-list' },
      depthStencil: {
        format: depthFormat,
        depthWriteEnabled: false,
        depthCompare: 'always',
      },
    });
    const glyphPipeline = device.createRenderPipeline({
      layout: device.createPipelineLayout({ bindGroupLayouts: [viewportLayout, atlasLayout] }),
      vertex: {
        module: glyphModule,
        entryPoint: 'vsMain',
        buffers: [{
          arrayStride: GLYPH_BYTES,
          stepMode: 'instance',
          attributes: [
            { shaderLocation: 0, offset: 0, format: 'float32x4' },
            { shaderLocation: 1, offset: 16, format: 'float32x4' },
            { shaderLocation: 2, offset: 32, format: 'float32x4' },
          ],
        }],
      },
      fragment: { module: glyphModule, entryPoint: 'fsMain', targets: [{ format, blend }] },
      primitive: { topology: 'triangle-list' },
      depthStencil: {
        format: depthFormat,
        depthWriteEnabled: false,
        depthCompare: 'always',
      },
    });

    const viewportBuffer = device.createBuffer({
      size: 16,
      usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });
    const viewportBindGroup = device.createBindGroup({
      layout: viewportLayout,
      entries: [{ binding: 0, resource: { buffer: viewportBuffer } }],
    });
    const atlas = createGlyphAtlas(device);
    const atlasBindGroup = device.createBindGroup({
      layout: atlasLayout,
      entries: [
        { binding: 0, resource: atlas.sampler },
        { binding: 1, resource: atlas.texture.createView() },
      ],
    });
    const barBuffer = device.createBuffer({
      size: INITIAL_BAR_CAPACITY * BAR_BYTES,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    const glyphBuffer = device.createBuffer({
      size: INITIAL_GLYPH_CAPACITY * GLYPH_BYTES,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });

    return new WebGpuHudRenderer(
      device,
      barPipeline,
      glyphPipeline,
      viewportBindGroup,
      atlasBindGroup,
      viewportBuffer,
      barBuffer,
      glyphBuffer,
    );
  }

  get barCount(): number { return this._barCount; }
  get glyphCount(): number { return this._glyphCount; }
  get missingGlyphCount(): number { return this._missingGlyphCount; }

  resize(width: number, height: number): void {
    this._viewportData[0] = Math.max(1, width);
    this._viewportData[1] = Math.max(1, height);
    this._device.queue.writeBuffer(this._viewportBuffer, 0, this._viewportData);
  }

  setInterpolationFactor(factor: number): void {
    const resolved = Math.max(0, Math.min(1, factor));
    if (resolved === this._interpolationFactor) {
      return;
    }

    this._interpolationFactor = resolved;
    this._viewportData[2] = resolved;
    this._device.queue.writeBuffer(this._viewportBuffer, 0, this._viewportData);
  }

  clearScreenHud(): void {
    this._barCount = 0;
    this._glyphCount = 0;
    this._missingGlyphCount = 0;
  }

  updateScreenHud(view: DataView, payloadOffset: number, itemCount: number, byteLength: number): void {
    const payloadEnd = payloadOffset + byteLength;
    const tableOffset = payloadOffset + itemCount * HUD_ITEM_BYTES;
    if (tableOffset > payloadEnd) {
      throw new Error(
        `Screen HUD section is truncated: ${byteLength} bytes for ${itemCount} fixed records.`,
      );
    }

    this.parseTextTables(view, tableOffset, payloadEnd);
    const previousBarCount = this._barCount;
    const previousInterpolationFactor = this._interpolationFactor;
    this._barCount = 0;
    this._glyphCount = 0;
    this._missingGlyphCount = 0;

    for (let index = 0; index < itemCount; index++) {
      const itemOffset = payloadOffset + index * HUD_ITEM_BYTES;
      const kind = view.getUint8(itemOffset);
      if (kind === HUD_BAR) {
        this.appendBar(view, itemOffset, previousBarCount, previousInterpolationFactor);
      } else if (kind === HUD_TEXT) {
        this.appendText(view, itemOffset);
      }
    }

    this.uploadInstances();
    this.setInterpolationFactor(0);
  }

  render(pass: GPURenderPassEncoder): void {
    if (this._barCount > 0) {
      pass.setPipeline(this._barPipeline);
      pass.setBindGroup(0, this._viewportBindGroup);
      pass.setVertexBuffer(0, this._barBuffer);
      pass.draw(6, this._barCount);
    }
    if (this._glyphCount > 0) {
      pass.setPipeline(this._glyphPipeline);
      pass.setBindGroup(0, this._viewportBindGroup);
      pass.setBindGroup(1, this._atlasBindGroup);
      pass.setVertexBuffer(0, this._glyphBuffer);
      pass.draw(6, this._glyphCount);
    }
  }

  private appendBar(
    view: DataView,
    offset: number,
    previousBarCount: number,
    previousInterpolationFactor: number,
  ): void {
    this.ensureBarCapacity(this._barCount + 1);
    const barIndex = this._barCount;
    const stableId = view.getInt32(offset + 9, true);
    const currentX = view.getFloat32(offset + 1, true);
    const currentY = view.getFloat32(offset + 5, true);
    let previousX = currentX;
    let previousY = currentY;
    if (stableId > 0 &&
        barIndex < previousBarCount &&
        this._barStableIds[barIndex] === stableId) {
      const previousWrite = barIndex * BAR_FLOATS;
      const oldCurrentX = this._barScratch[previousWrite];
      const oldCurrentY = this._barScratch[previousWrite + 1];
      const oldPreviousX = this._barScratch[previousWrite + 13];
      const oldPreviousY = this._barScratch[previousWrite + 14];
      previousX = oldPreviousX + (oldCurrentX - oldPreviousX) * previousInterpolationFactor;
      previousY = oldPreviousY + (oldCurrentY - oldPreviousY) * previousInterpolationFactor;
    }

    let write = barIndex * BAR_FLOATS;
    this._barScratch[write++] = currentX;
    this._barScratch[write++] = currentY;
    this._barScratch[write++] = Math.max(0, view.getFloat32(offset + 45, true));
    this._barScratch[write++] = Math.max(0, view.getFloat32(offset + 49, true));
    for (let component = 0; component < 4; component++) {
      this._barScratch[write++] = view.getFloat32(offset + 13 + component * 4, true);
    }
    for (let component = 0; component < 4; component++) {
      this._barScratch[write++] = view.getFloat32(offset + 29 + component * 4, true);
    }
    this._barScratch[write++] = Math.max(0, Math.min(1, view.getFloat32(offset + 53, true)));
    this._barScratch[write++] = previousX;
    this._barScratch[write] = previousY;
    this._barStableIds[barIndex] = stableId;
    this._barCount++;
  }

  private appendText(view: DataView, offset: number): void {
    let x = view.getFloat32(offset + 1, true);
    const y = view.getFloat32(offset + 5, true);
    const fontSize = Math.max(1, view.getInt32(offset + 69, true) || 16);
    const colorOffset = offset + 13;
    const color = [
      view.getFloat32(colorOffset, true),
      view.getFloat32(colorOffset + 4, true),
      view.getFloat32(colorOffset + 8, true),
      view.getFloat32(colorOffset + 12, true),
    ] as const;

    const tokenId = view.getInt32(offset + 73, true);
    const template = tokenId > 0 ? this._templates.get(tokenId) : undefined;
    if (template) {
      this.appendTemplate(view, offset + 73, template, x, y, fontSize, color);
      return;
    }

    const legacyStringId = view.getInt32(offset + 61, true);
    if (legacyStringId > 0 && legacyStringId < this._legacyStrings.length) {
      this.appendString(this._legacyStrings[legacyStringId] ?? '', x, y, fontSize, color);
      return;
    }

    const value0 = view.getFloat32(offset + 53, true);
    const value1 = view.getFloat32(offset + 57, true);
    const valueMode = view.getInt32(offset + 65, true);
    if (valueMode === 1) {
      x = this.appendInteger(Math.round(value0), x, y, fontSize, color);
      x = this.appendGlyph(47, x, y, fontSize, color);
      this.appendInteger(Math.round(value1), x, y, fontSize, color);
    } else if (valueMode === 2 || valueMode === 3) {
      this.appendInteger(Math.round(value0), x, y, fontSize, color);
    } else {
      this.appendFixed(value0, 1, x, y, fontSize, color);
    }
  }

  private appendTemplate(
    view: DataView,
    packetOffset: number,
    template: string,
    startX: number,
    y: number,
    fontSize: number,
    color: readonly [number, number, number, number],
  ): void {
    let x = startX;
    const argCount = view.getUint8(packetOffset + 4);
    for (let index = 0; index < template.length; index++) {
      const code = template.charCodeAt(index);
      if (code === 123 && index + 1 < template.length && template.charCodeAt(index + 1) === 123) {
        x = this.appendGlyph(123, x, y, fontSize, color);
        index++;
        continue;
      }
      if (code === 125 && index + 1 < template.length && template.charCodeAt(index + 1) === 125) {
        x = this.appendGlyph(125, x, y, fontSize, color);
        index++;
        continue;
      }
      if (code !== 123) {
        x = this.appendGlyph(code, x, y, fontSize, color);
        continue;
      }

      let close = index + 1;
      let argIndex = 0;
      let hasDigit = false;
      while (close < template.length && template.charCodeAt(close) !== 125) {
        const digit = template.charCodeAt(close) - 48;
        if (digit < 0 || digit > 9) {
          hasDigit = false;
          break;
        }
        hasDigit = true;
        argIndex = argIndex * 10 + digit;
        close++;
      }
      if (!hasDigit || close >= template.length || argIndex >= argCount || argIndex >= 4) {
        x = this.appendGlyph(code, x, y, fontSize, color);
        continue;
      }

      x = this.appendTextArg(view, packetOffset + 8 + argIndex * 8, x, y, fontSize, color);
      index = close;
    }
  }

  private appendTextArg(
    view: DataView,
    offset: number,
    x: number,
    y: number,
    fontSize: number,
    color: readonly [number, number, number, number],
  ): number {
    const type = view.getUint8(offset);
    const format = view.getUint8(offset + 1);
    const raw = view.getInt32(offset + 4, true);
    if (type === 1) {
      return this.appendInteger(raw, x, y, fontSize, color);
    }
    if (type !== 2) {
      return x;
    }

    this._floatBits.setInt32(0, raw, true);
    const value = this._floatBits.getFloat32(0, true);
    if (format === 1 || format === 2) {
      return this.appendInteger(Math.trunc(value), x, y, fontSize, color);
    }
    if (format === 3) {
      return this.appendFixed(value, 1, x, y, fontSize, color);
    }
    if (format === 4) {
      return this.appendFixed(value, 2, x, y, fontSize, color);
    }
    return this.appendFixed(value, 3, x, y, fontSize, color);
  }

  private appendInteger(
    value: number,
    x: number,
    y: number,
    fontSize: number,
    color: readonly [number, number, number, number],
  ): number {
    let integer = Number.isFinite(value) ? Math.trunc(value) : 0;
    if (integer < 0) {
      x = this.appendGlyph(45, x, y, fontSize, color);
      integer = -integer;
    }
    if (integer === 0) {
      return this.appendGlyph(48, x, y, fontSize, color);
    }

    let count = 0;
    while (integer > 0 && count < this._digitScratch.length) {
      this._digitScratch[count++] = integer % 10;
      integer = Math.floor(integer / 10);
    }
    for (let index = count - 1; index >= 0; index--) {
      x = this.appendGlyph(48 + this._digitScratch[index], x, y, fontSize, color);
    }
    return x;
  }

  private appendFixed(
    value: number,
    decimals: number,
    x: number,
    y: number,
    fontSize: number,
    color: readonly [number, number, number, number],
  ): number {
    const scale = 10 ** decimals;
    let scaled = Math.round((Number.isFinite(value) ? value : 0) * scale);
    if (scaled < 0) {
      x = this.appendGlyph(45, x, y, fontSize, color);
      scaled = -scaled;
    }
    const integer = Math.floor(scaled / scale);
    x = this.appendInteger(integer, x, y, fontSize, color);
    if (decimals <= 0) {
      return x;
    }
    x = this.appendGlyph(46, x, y, fontSize, color);
    let divisor = scale / 10;
    let fraction = scaled % scale;
    for (let index = 0; index < decimals; index++) {
      const digit = Math.floor(fraction / divisor);
      x = this.appendGlyph(48 + digit, x, y, fontSize, color);
      fraction %= divisor;
      divisor /= 10;
    }
    return x;
  }

  private appendString(
    value: string,
    x: number,
    y: number,
    fontSize: number,
    color: readonly [number, number, number, number],
  ): number {
    for (let index = 0; index < value.length; index++) {
      x = this.appendGlyph(value.charCodeAt(index), x, y, fontSize, color);
    }
    return x;
  }

  private appendGlyph(
    code: number,
    x: number,
    y: number,
    fontSize: number,
    color: readonly [number, number, number, number],
  ): number {
    const advance = fontSize * 0.62;
    if (code === 32) {
      return x + advance;
    }
    if (code < FIRST_GLYPH || code > LAST_GLYPH) {
      this._missingGlyphCount++;
      return x + advance;
    }

    this.ensureGlyphCapacity(this._glyphCount + 1);
    const glyphIndex = code - FIRST_GLYPH;
    const column = glyphIndex % ATLAS_COLUMNS;
    const row = Math.floor(glyphIndex / ATLAS_COLUMNS);
    const atlasWidth = ATLAS_COLUMNS * ATLAS_CELL_WIDTH;
    const atlasHeight = ATLAS_ROWS * ATLAS_CELL_HEIGHT;
    let write = this._glyphCount * GLYPH_FLOATS;
    this._glyphScratch[write++] = x;
    this._glyphScratch[write++] = y;
    this._glyphScratch[write++] = advance;
    this._glyphScratch[write++] = fontSize * 1.25;
    this._glyphScratch[write++] = column * ATLAS_CELL_WIDTH / atlasWidth;
    this._glyphScratch[write++] = row * ATLAS_CELL_HEIGHT / atlasHeight;
    this._glyphScratch[write++] = (column + 1) * ATLAS_CELL_WIDTH / atlasWidth;
    this._glyphScratch[write++] = (row + 1) * ATLAS_CELL_HEIGHT / atlasHeight;
    this._glyphScratch[write++] = color[0];
    this._glyphScratch[write++] = color[1];
    this._glyphScratch[write++] = color[2];
    this._glyphScratch[write] = color[3];
    this._glyphCount++;
    return x + advance;
  }

  private parseTextTables(view: DataView, start: number, end: number): void {
    let cursor = start;
    if (cursor + 2 > end) {
      throw new Error('Screen HUD legacy string table header is missing.');
    }
    const stringCount = view.getUint16(cursor, true);
    cursor += 2;
    this._legacyStrings.length = stringCount;
    for (let index = 0; index < stringCount; index++) {
      if (cursor + 2 > end) {
        throw new Error('Screen HUD legacy string length is truncated.');
      }
      const length = view.getUint16(cursor, true);
      cursor += 2;
      if (cursor + length > end) {
        throw new Error('Screen HUD legacy string bytes are truncated.');
      }
      this._legacyStrings[index] = this._textDecoder.decode(
        new Uint8Array(view.buffer, view.byteOffset + cursor, length),
      );
      cursor += length;
    }

    if (cursor + 2 > end) {
      throw new Error('Screen HUD template table header is missing.');
    }
    const templateCount = view.getUint16(cursor, true);
    cursor += 2;
    this._templates.clear();
    for (let index = 0; index < templateCount; index++) {
      if (cursor + 6 > end) {
        throw new Error('Screen HUD template record is truncated.');
      }
      const tokenId = view.getInt32(cursor, true);
      const length = view.getUint16(cursor + 4, true);
      cursor += 6;
      if (cursor + length > end) {
        throw new Error('Screen HUD template bytes are truncated.');
      }
      this._templates.set(
        tokenId,
        this._textDecoder.decode(new Uint8Array(view.buffer, view.byteOffset + cursor, length)),
      );
      cursor += length;
    }
    if (cursor !== end) {
      throw new Error(`Screen HUD section has ${end - cursor} unexplained trailing bytes.`);
    }
  }

  private uploadInstances(): void {
    this.ensureGpuBarCapacity(this._barCount);
    this.ensureGpuGlyphCapacity(this._glyphCount);
    if (this._barCount > 0) {
      this._device.queue.writeBuffer(
        this._barBuffer,
        0,
        this._barScratch.buffer as ArrayBuffer,
        0,
        this._barCount * BAR_BYTES,
      );
    }
    if (this._glyphCount > 0) {
      this._device.queue.writeBuffer(
        this._glyphBuffer,
        0,
        this._glyphScratch.buffer as ArrayBuffer,
        0,
        this._glyphCount * GLYPH_BYTES,
      );
    }
  }

  private ensureBarCapacity(required: number): void {
    if (required <= this._barCapacity) return;
    while (this._barCapacity < required) this._barCapacity *= 2;
    const next = new Float32Array(this._barCapacity * BAR_FLOATS);
    const nextStableIds = new Int32Array(this._barCapacity);
    next.set(this._barScratch);
    nextStableIds.set(this._barStableIds);
    this._barScratch = next;
    this._barStableIds = nextStableIds;
  }

  private ensureGlyphCapacity(required: number): void {
    if (required <= this._glyphCapacity) return;
    while (this._glyphCapacity < required) this._glyphCapacity *= 2;
    const next = new Float32Array(this._glyphCapacity * GLYPH_FLOATS);
    next.set(this._glyphScratch);
    this._glyphScratch = next;
  }

  private ensureGpuBarCapacity(required: number): void {
    if (required <= this._barBuffer.size / BAR_BYTES) return;
    this._barBuffer.destroy();
    this._barBuffer = this._device.createBuffer({
      size: this._barCapacity * BAR_BYTES,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
  }

  private ensureGpuGlyphCapacity(required: number): void {
    if (required <= this._glyphBuffer.size / GLYPH_BYTES) return;
    this._glyphBuffer.destroy();
    this._glyphBuffer = this._device.createBuffer({
      size: this._glyphCapacity * GLYPH_BYTES,
      usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
  }
}

function createGlyphAtlas(device: GPUDevice): { texture: GPUTexture; sampler: GPUSampler } {
  const width = ATLAS_COLUMNS * ATLAS_CELL_WIDTH;
  const height = ATLAS_ROWS * ATLAS_CELL_HEIGHT;
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) {
    throw new Error('WebGPU HUD could not create its glyph atlas canvas.');
  }
  context.clearRect(0, 0, width, height);
  context.fillStyle = '#ffffff';
  context.font = '18px monospace';
  context.textBaseline = 'top';
  for (let code = FIRST_GLYPH; code <= LAST_GLYPH; code++) {
    const glyphIndex = code - FIRST_GLYPH;
    const column = glyphIndex % ATLAS_COLUMNS;
    const row = Math.floor(glyphIndex / ATLAS_COLUMNS);
    context.fillText(String.fromCharCode(code), column * ATLAS_CELL_WIDTH + 2, row * ATLAS_CELL_HEIGHT + 1);
  }

  const texture = device.createTexture({
    size: [width, height],
    format: 'rgba8unorm',
    usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.RENDER_ATTACHMENT,
  });
  device.queue.copyExternalImageToTexture({ source: canvas }, { texture }, [width, height]);
  const sampler = device.createSampler({
    magFilter: 'linear',
    minFilter: 'linear',
    addressModeU: 'clamp-to-edge',
    addressModeV: 'clamp-to-edge',
  });
  return { texture, sampler };
}
