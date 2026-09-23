const MSG_FULL = 0x01;
const MSG_DELTA = 0x05;

const SEC_END = 0x00;
const SEC_PRIMITIVES = 0x02;
const SEC_SCREEN_HUD = 0x05;

const FRAME_HEADER_BYTES = 17;
const SECTION_HEADER_BYTES = 7;
const PRIMITIVE_WIRE_BYTES = 48;
const FLOATS_PER_INSTANCE = 10;

export interface ScreenHudWireSink {
  updateScreenHud(view: DataView, payloadOffset: number, itemCount: number, byteLength: number): void;
  clearScreenHud(): void;
}

export interface WebGpuFrameStreamState {
  frameNumber: number;
  simTick: number;
  timestampMs: number;
  primitiveCount: number;
  identityResets: number;
}

export class WebGpuFrameStream {
  private _previous = new Float32Array(0);
  private _current = new Float32Array(0);
  private _interpolated = new Float32Array(0);
  private _previousStableIds = new Int32Array(0);
  private _currentStableIds = new Int32Array(0);
  private _capacity = 0;
  private _previousCount = 0;
  private _currentCount = 0;
  private _factor = 1;
  private _frameDurationMs = 33;
  private _elapsedMs = 0;
  private _lastPushTimeMs = 0;
  private readonly _state: WebGpuFrameStreamState = {
    frameNumber: 0,
    simTick: 0,
    timestampMs: 0,
    primitiveCount: 0,
    identityResets: 0,
  };

  get state(): Readonly<WebGpuFrameStreamState> {
    return this._state;
  }

  get interpolationFactor(): number {
    return this._factor;
  }

  pushFrame(buffer: ArrayBuffer, hudSink: ScreenHudWireSink): void {
    const view = new DataView(buffer);
    if (view.byteLength < FRAME_HEADER_BYTES) {
      throw new Error(`WebGPU frame is truncated: ${view.byteLength} bytes.`);
    }

    const messageType = view.getUint8(0);
    if (messageType === MSG_DELTA) {
      throw new Error('WebGPU packed frame stream received an unsupported delta frame.');
    }
    if (messageType !== MSG_FULL) {
      return;
    }

    this._state.frameNumber = view.getUint32(1, true);
    this._state.simTick = view.getInt32(5, true);
    this._state.timestampMs = Number(view.getBigInt64(9, true));

    let cursor = FRAME_HEADER_BYTES;
    let sawPrimitives = false;
    let sawScreenHud = false;
    while (cursor < view.byteLength) {
      const sectionType = view.getUint8(cursor);
      if (sectionType === SEC_END) {
        break;
      }
      if (cursor + SECTION_HEADER_BYTES > view.byteLength) {
        throw new Error(`WebGPU frame section header is truncated at byte ${cursor}.`);
      }

      const itemCount = view.getUint16(cursor + 1, true);
      const byteLength = view.getInt32(cursor + 3, true);
      const payloadOffset = cursor + SECTION_HEADER_BYTES;
      const payloadEnd = payloadOffset + byteLength;
      if (byteLength < 0 || payloadEnd > view.byteLength) {
        throw new Error(
          `WebGPU frame section ${sectionType} exceeds the message boundary: ${payloadEnd}/${view.byteLength}.`,
        );
      }

      if (sectionType === SEC_PRIMITIVES) {
        this.decodePrimitives(view, payloadOffset, itemCount, byteLength);
        sawPrimitives = true;
      } else if (sectionType === SEC_SCREEN_HUD) {
        hudSink.updateScreenHud(view, payloadOffset, itemCount, byteLength);
        sawScreenHud = true;
      }

      cursor = payloadEnd;
    }

    if (!sawPrimitives) {
      this.beginPrimitiveFrame(0);
    }
    if (!sawScreenHud) {
      hudSink.clearScreenHud();
    }

    this._state.primitiveCount = this._currentCount;
    const now = performance.now();
    if (this._lastPushTimeMs > 0) {
      this._frameDurationMs = Math.max(8, now - this._lastPushTimeMs);
    }
    this._lastPushTimeMs = now;
    this._elapsedMs = 0;
    this._factor = 0;
  }

  tick(deltaSeconds: number): void {
    this._elapsedMs += deltaSeconds * 1000;
    this._factor = Math.min(1, this._elapsedMs / this._frameDurationMs);
  }

  getSampledInstances(): { data: Float32Array; count: number } {
    const count = this._currentCount;
    if (this._previousCount === 0 || this._factor >= 0.99) {
      return { data: this._current, count };
    }

    const previousCount = this._previousCount;
    const factor = this._factor;
    for (let index = 0; index < count; index++) {
      const offset = index * FLOATS_PER_INSTANCE;
      const stableId = this._currentStableIds[index];
      const identityMatches = stableId > 0 &&
        index < previousCount &&
        this._previousStableIds[index] === stableId;
      if (identityMatches) {
        this._interpolated[offset] = this._previous[offset] +
          (this._current[offset] - this._previous[offset]) * factor;
        this._interpolated[offset + 1] = this._previous[offset + 1] +
          (this._current[offset + 1] - this._previous[offset + 1]) * factor;
        this._interpolated[offset + 2] = this._previous[offset + 2] +
          (this._current[offset + 2] - this._previous[offset + 2]) * factor;
      } else {
        this._interpolated[offset] = this._current[offset];
        this._interpolated[offset + 1] = this._current[offset + 1];
        this._interpolated[offset + 2] = this._current[offset + 2];
      }

      for (let component = 3; component < FLOATS_PER_INSTANCE; component++) {
        this._interpolated[offset + component] = this._current[offset + component];
      }
    }

    return { data: this._interpolated, count };
  }

  private decodePrimitives(view: DataView, offset: number, count: number, byteLength: number): void {
    const expectedBytes = count * PRIMITIVE_WIRE_BYTES;
    if (byteLength !== expectedBytes) {
      throw new Error(
        `WebGPU primitive section has ${byteLength} bytes; expected ${expectedBytes} for ${count} items.`,
      );
    }

    this.beginPrimitiveFrame(count);
    let cursor = offset;
    let write = 0;
    let identityResets = 0;
    for (let index = 0; index < count; index++) {
      cursor += 4; // meshAssetId is not yet used by the cube-only WebGPU vertical slice.
      for (let component = 0; component < FLOATS_PER_INSTANCE; component++) {
        let value = view.getFloat32(cursor, true);
        if (component >= 3 && component <= 5 && Math.abs(value) < 0.0001) {
          value = 1;
        } else if (component === 9 && value <= 0) {
          value = 1;
        }
        this._current[write++] = value;
        cursor += 4;
      }
      const stableId = view.getInt32(cursor, true);
      this._currentStableIds[index] = stableId;
      if (index < this._previousCount &&
          (stableId <= 0 || this._previousStableIds[index] !== stableId)) {
        identityResets++;
      }
      cursor += 4;
    }
    this._state.identityResets = identityResets;
  }

  private beginPrimitiveFrame(count: number): void {
    this.ensureCapacity(count);
    const previous = this._previous;
    this._previous = this._current;
    this._current = previous;
    const previousStableIds = this._previousStableIds;
    this._previousStableIds = this._currentStableIds;
    this._currentStableIds = previousStableIds;
    this._previousCount = this._currentCount;
    this._currentCount = count;
    this._state.identityResets = 0;
  }

  private ensureCapacity(required: number): void {
    if (required <= this._capacity) {
      return;
    }

    let capacity = this._capacity === 0 ? 1024 : this._capacity;
    while (capacity < required) {
      capacity *= 2;
    }

    const floatCapacity = capacity * FLOATS_PER_INSTANCE;
    const previous = new Float32Array(floatCapacity);
    const current = new Float32Array(floatCapacity);
    const interpolated = new Float32Array(floatCapacity);
    const previousStableIds = new Int32Array(capacity);
    const currentStableIds = new Int32Array(capacity);
    previous.set(this._previous);
    current.set(this._current);
    interpolated.set(this._interpolated);
    previousStableIds.set(this._previousStableIds);
    currentStableIds.set(this._currentStableIds);
    this._previous = previous;
    this._current = current;
    this._interpolated = interpolated;
    this._previousStableIds = previousStableIds;
    this._currentStableIds = currentStableIds;
    this._capacity = capacity;
  }
}
