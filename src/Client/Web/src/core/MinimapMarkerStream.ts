const MSG_FULL = 0x01;
const SEC_END = 0x00;
const SEC_MINIMAP_MARKERS = 0x0b;

const FRAME_HEADER_BYTES = 17;
const SECTION_HEADER_BYTES = 7;
const METADATA_BYTES = 17;
const MARKER_BYTES = 24;

export class MinimapMarkerStream {
  private _screenX: Float32Array<ArrayBuffer> = new Float32Array(0);
  private _screenY: Float32Array<ArrayBuffer> = new Float32Array(0);
  private _colors: Uint32Array<ArrayBuffer> = new Uint32Array(0);
  private _sizes: Float32Array<ArrayBuffer> = new Float32Array(0);
  private _orientations: Float32Array<ArrayBuffer> = new Float32Array(0);
  private _orientationLengths: Float32Array<ArrayBuffer> = new Float32Array(0);
  private _capacity = 0;
  private _count = 0;
  private _clipKind = 0;
  private _clipX = 0;
  private _clipY = 0;
  private _clipWidth = 0;
  private _clipHeight = 0;

  get count(): number { return this._count; }
  get clipKind(): number { return this._clipKind; }
  get clipX(): number { return this._clipX; }
  get clipY(): number { return this._clipY; }
  get clipWidth(): number { return this._clipWidth; }
  get clipHeight(): number { return this._clipHeight; }
  get screenX(): Float32Array { return this._screenX; }
  get screenY(): Float32Array { return this._screenY; }
  get colors(): Uint32Array { return this._colors; }
  get sizes(): Float32Array { return this._sizes; }
  get orientations(): Float32Array { return this._orientations; }
  get orientationLengths(): Float32Array { return this._orientationLengths; }

  clear(): void {
    this._count = 0;
    this._clipKind = 0;
    this._clipX = 0;
    this._clipY = 0;
    this._clipWidth = 0;
    this._clipHeight = 0;
  }

  pushFrame(buffer: ArrayBuffer): void {
    const view = new DataView(buffer);
    if (view.byteLength < FRAME_HEADER_BYTES || view.getUint8(0) !== MSG_FULL) {
      return;
    }

    let cursor = FRAME_HEADER_BYTES;
    let found = false;
    while (cursor < view.byteLength) {
      const sectionType = view.getUint8(cursor);
      if (sectionType === SEC_END) {
        break;
      }
      if (cursor + SECTION_HEADER_BYTES > view.byteLength) {
        throw new Error(`Minimap section header is truncated at byte ${cursor}.`);
      }

      const itemCount = view.getUint16(cursor + 1, true);
      const byteLength = view.getInt32(cursor + 3, true);
      const payloadOffset = cursor + SECTION_HEADER_BYTES;
      const payloadEnd = payloadOffset + byteLength;
      if (byteLength < 0 || payloadEnd > view.byteLength) {
        throw new Error(`Minimap section ${sectionType} exceeds the frame boundary.`);
      }

      if (sectionType === SEC_MINIMAP_MARKERS) {
        this.update(view, payloadOffset, itemCount, byteLength);
        found = true;
      }

      cursor = payloadEnd;
    }

    if (!found) {
      this.clear();
    }
  }

  private update(view: DataView, offset: number, count: number, byteLength: number): void {
    const expectedBytes = METADATA_BYTES + count * MARKER_BYTES;
    if (byteLength !== expectedBytes) {
      throw new Error(
        `Minimap marker section has ${byteLength} bytes; expected ${expectedBytes} for ${count} markers.`,
      );
    }

    const clipKind = view.getUint8(offset);
    if (clipKind > 3) {
      throw new Error(`Minimap marker section declares unsupported clip kind ${clipKind}.`);
    }

    this.ensureCapacity(count);
    this._clipKind = clipKind;
    this._clipX = view.getFloat32(offset + 1, true);
    this._clipY = view.getFloat32(offset + 5, true);
    this._clipWidth = view.getFloat32(offset + 9, true);
    this._clipHeight = view.getFloat32(offset + 13, true);
    this._count = count;

    let cursor = offset + METADATA_BYTES;
    for (let index = 0; index < count; index++) {
      this._screenX[index] = view.getFloat32(cursor, true);
      this._screenY[index] = view.getFloat32(cursor + 4, true);
      this._colors[index] = view.getUint32(cursor + 8, true);
      this._sizes[index] = view.getFloat32(cursor + 12, true);
      this._orientations[index] = view.getFloat32(cursor + 16, true);
      this._orientationLengths[index] = view.getFloat32(cursor + 20, true);
      cursor += MARKER_BYTES;
    }
  }

  private ensureCapacity(required: number): void {
    if (required <= this._capacity) {
      return;
    }

    let capacity = this._capacity === 0 ? 1024 : this._capacity;
    while (capacity < required) {
      capacity *= 2;
    }

    this._screenX = this.growFloat32(this._screenX, capacity);
    this._screenY = this.growFloat32(this._screenY, capacity);
    this._colors = this.growUint32(this._colors, capacity);
    this._sizes = this.growFloat32(this._sizes, capacity);
    this._orientations = this.growFloat32(this._orientations, capacity);
    this._orientationLengths = this.growFloat32(this._orientationLengths, capacity);
    this._capacity = capacity;
  }

  private growFloat32(source: Float32Array<ArrayBuffer>, capacity: number): Float32Array<ArrayBuffer> {
    const next = new Float32Array(capacity);
    next.set(source);
    return next;
  }

  private growUint32(source: Uint32Array<ArrayBuffer>, capacity: number): Uint32Array<ArrayBuffer> {
    const next = new Uint32Array(capacity);
    next.set(source);
    return next;
  }
}
