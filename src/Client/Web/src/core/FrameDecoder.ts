import type { UiScenePayload } from './UiSceneTypes';

const MSG_FULL = 0x01;
const MSG_MESH_MAP = 0x03;
const MSG_MATERIAL_MAP = 0x04;
const MSG_DELTA = 0x05;

const SEC_END = 0x00;
const SEC_CAMERA = 0x01;
const SEC_PRIMITIVES = 0x02;
const SEC_GROUND_OVERLAYS = 0x03;
const SEC_WORLD_HUD = 0x04;
const SEC_SCREEN_HUD = 0x05;
const SEC_UI_SCENE = 0x09;
const SEC_SCREEN_OVERLAY = 0x0a;
const SEC_DEBUG_LINES = 0x10;
const SEC_DEBUG_CIRCLES = 0x11;
const SEC_DEBUG_BOXES = 0x12;
const SEC_PRIMITIVES_DELTA = 0x18;
const SEC_SURFACES = 0x19;

export interface CameraState {
  posX: number; posY: number; posZ: number;
  tgtX: number; tgtY: number; tgtZ: number;
  upX: number; upY: number; upZ: number;
  fov: number;
}

export interface PrimitiveItem {
  meshAssetId: number;
  posX: number; posY: number; posZ: number;
  scaleX: number; scaleY: number; scaleZ: number;
  r: number; g: number; b: number; a: number;
}

export interface MaterialCustomData {
  slotMask: number;
  slots: [
    [number, number, number, number],
    [number, number, number, number],
    [number, number, number, number],
    [number, number, number, number],
  ];
}

export interface SurfaceItem {
  stableId: number;
  meshAssetId: number;
  materialId: number;
  sortId: number;
  posX: number; posY: number; posZ: number;
  rotX: number; rotY: number; rotZ: number; rotW: number;
  scaleX: number; scaleY: number; scaleZ: number;
  visibility: number;
  materialCustomData: MaterialCustomData;
  surfaceLayerKey: string;
}

export interface DebugLine {
  ax: number; ay: number; bx: number; by: number;
  thickness: number;
  r: number; g: number; b: number; a: number;
}

export interface DebugCircle {
  cx: number; cy: number;
  radius: number; thickness: number;
  r: number; g: number; b: number; a: number;
}

export interface DebugBox {
  cx: number; cy: number;
  halfW: number; halfH: number;
  rotation: number; thickness: number;
  r: number; g: number; b: number; a: number;
}

export interface GroundOverlayItem {
  shape: number;
  cx: number; cy: number; cz: number;
  radius: number; innerRadius: number; angle: number;
  rotation: number; length: number; width: number;
  fillR: number; fillG: number; fillB: number; fillA: number;
  borderR: number; borderG: number; borderB: number; borderA: number;
  borderWidth: number;
}

export interface PresentationTextArg {
  type: number;
  format: number;
  raw32: number;
}

export interface PresentationTextPacket {
  tokenId: number;
  argCount: number;
  args: PresentationTextArg[];
}

export interface ScreenHudItem {
  kind: number;
  sx: number; sy: number;
  c0r: number; c0g: number; c0b: number; c0a: number;
  c1r: number; c1g: number; c1b: number; c1a: number;
  width: number; height: number;
  v0: number; v1: number;
  id0: number; id1: number;
  fontSize: number;
  text?: string;
  textPacket?: PresentationTextPacket;
  textTemplate?: string;
}

export interface ScreenOverlayItem {
  kind: number;
  x: number; y: number;
  width: number; height: number;
  fontSize: number;
  cr: number; cg: number; cb: number; ca: number;
  bgr: number; bgg: number; bgb: number; bga: number;
  text: string;
  textPacket?: PresentationTextPacket;
  textTemplate?: string;
}

export interface MeshMapEntry {
  id: number;
  key: string;
}

export interface MaterialMapEntry {
  id: number;
  key: string;
  domain: number;
  flags: number;
  sourceUris: string[];
}

export interface DecodedFrame {
  frameNumber: number;
  simTick: number;
  timestampMs: number;
  camera: CameraState;
  primitives: PrimitiveItem[];
  surfaces: SurfaceItem[];
  debugLines: DebugLine[];
  debugCircles: DebugCircle[];
  debugBoxes: DebugBox[];
  groundOverlays: GroundOverlayItem[];
  screenHud: ScreenHudItem[];
  screenOverlays: ScreenOverlayItem[];
  uiScene?: UiScenePayload;
}

export class FrameDecoder {
  private _prevFrame: DecodedFrame | null = null;
  private _meshMap: MeshMapEntry[] | null = null;
  private _materialMap: MaterialMapEntry[] | null = null;
  private _textDecoder = new TextDecoder();

  get meshMap(): MeshMapEntry[] | null { return this._meshMap; }
  get materialMap(): MaterialMapEntry[] | null { return this._materialMap; }

  resetMaps(): void {
    this._meshMap = null;
    this._materialMap = null;
  }

  decode(buffer: ArrayBuffer): DecodedFrame | null {
    const v = new DataView(buffer);
    if (v.byteLength < 3) {
      throw new Error(`FrameDecoder message is too short: ${v.byteLength} bytes.`);
    }

    const msgType = v.getUint8(0);
    if (msgType === MSG_MESH_MAP) {
      this.decodeMeshMap(v);
      return null;
    }
    if (msgType === MSG_MATERIAL_MAP) {
      this.decodeMaterialMap(v);
      return null;
    }
    if (v.byteLength < 17) {
      throw new Error(`FrameDecoder frame message is too short: ${v.byteLength} bytes.`);
    }
    if (msgType === MSG_FULL) return this.decodeFull(v);
    if (msgType === MSG_DELTA) return this.decodeDelta(v);
    throw new Error(`FrameDecoder received unknown message type 0x${msgType.toString(16)}.`);
  }

  private decodeMeshMap(v: DataView): void {
    const count = v.getUint16(1, true);
    const entries: MeshMapEntry[] = [];
    let p = 3;
    for (let i = 0; i < count; i++) {
      const id = v.getInt32(p, true); p += 4;
      const keyLen = v.getUint16(p, true); p += 2;
      const keyBytes = new Uint8Array(v.buffer, v.byteOffset + p, keyLen);
      const key = this._textDecoder.decode(keyBytes);
      p += keyLen;
      entries.push({ id, key });
    }
    this._meshMap = entries;
  }

  private decodeMaterialMap(v: DataView): void {
    const count = v.getUint16(1, true);
    const entries: MaterialMapEntry[] = [];
    let p = 3;
    for (let i = 0; i < count; i++) {
      const id = v.getInt32(p, true); p += 4;
      const domain = v.getUint8(p); p += 1;
      const flags = v.getUint16(p, true); p += 2;
      const keyResult = this.readUtf8String(v, p);
      const key = keyResult.value;
      p = keyResult.next;
      const uriCount = v.getUint16(p, true); p += 2;
      const sourceUris: string[] = [];
      for (let uriIndex = 0; uriIndex < uriCount; uriIndex++) {
        const uriResult = this.readUtf8String(v, p);
        sourceUris.push(uriResult.value);
        p = uriResult.next;
      }

      entries.push({ id, key, domain, flags, sourceUris });
    }

    this._materialMap = entries;
  }

  private decodeFull(v: DataView): DecodedFrame {
    const frame = this.emptyFrame();
    frame.frameNumber = v.getUint32(1, true);
    frame.simTick = v.getInt32(5, true);
    frame.timestampMs = Number(v.getBigInt64(9, true));

    let p = 17;
    while (p < v.byteLength) {
      const secType = v.getUint8(p);
      if (secType === SEC_END) break;
      const itemCount = v.getUint16(p + 1, true);
      const byteLen = v.getInt32(p + 3, true);
      p += 7;

      switch (secType) {
        case SEC_CAMERA: p = this.readCamera(frame, v, p); break;
        case SEC_PRIMITIVES: p = this.readPrimitives(frame, v, p, itemCount); break;
        case SEC_SURFACES: p = this.readSurfaces(frame, v, p, itemCount); break;
        case SEC_GROUND_OVERLAYS: p = this.readGroundOverlays(frame, v, p, itemCount); break;
        case SEC_WORLD_HUD: p += byteLen; break;
        case SEC_SCREEN_HUD: p = this.readScreenHud(frame, v, p, itemCount); break;
        case SEC_UI_SCENE: p = this.readUiScene(frame, v, p); break;
        case SEC_SCREEN_OVERLAY: p = this.readScreenOverlays(frame, v, p, itemCount); break;
        case SEC_DEBUG_LINES: p = this.readDebugLines(frame, v, p, itemCount); break;
        case SEC_DEBUG_CIRCLES: p = this.readDebugCircles(frame, v, p, itemCount); break;
        case SEC_DEBUG_BOXES: p = this.readDebugBoxes(frame, v, p, itemCount); break;
        default: throw new Error(`Unknown frame section 0x${secType.toString(16)}`);
      }
    }

    this._prevFrame = frame;
    return frame;
  }

  private decodeDelta(v: DataView): DecodedFrame {
    const frame = this._prevFrame ? this.cloneFrame(this._prevFrame) : this.emptyFrame();
    frame.frameNumber = v.getUint32(1, true);
    frame.simTick = v.getInt32(5, true);
    frame.timestampMs = Number(v.getBigInt64(9, true));

    let p = 17;
    while (p < v.byteLength) {
      const secType = v.getUint8(p);
      if (secType === SEC_END) break;
      const itemCount = v.getUint16(p + 1, true);
      const byteLen = v.getInt32(p + 3, true);
      p += 7;

      switch (secType) {
        case SEC_CAMERA: p = this.readCamera(frame, v, p); break;
        case SEC_PRIMITIVES_DELTA: p = this.applyPrimitiveDelta(frame, v, p, itemCount); break;
        case SEC_PRIMITIVES: p = this.readPrimitives(frame, v, p, itemCount); break;
        case SEC_SURFACES: p = this.readSurfaces(frame, v, p, itemCount); break;
        case SEC_GROUND_OVERLAYS: p = this.readGroundOverlays(frame, v, p, itemCount); break;
        case SEC_WORLD_HUD: p += byteLen; break;
        case SEC_SCREEN_HUD: p = this.readScreenHud(frame, v, p, itemCount); break;
        case SEC_UI_SCENE: p = this.readUiScene(frame, v, p); break;
        case SEC_SCREEN_OVERLAY: p = this.readScreenOverlays(frame, v, p, itemCount); break;
        case SEC_DEBUG_LINES: p = this.readDebugLines(frame, v, p, itemCount); break;
        case SEC_DEBUG_CIRCLES: p = this.readDebugCircles(frame, v, p, itemCount); break;
        case SEC_DEBUG_BOXES: p = this.readDebugBoxes(frame, v, p, itemCount); break;
        default: throw new Error(`Unknown frame section 0x${secType.toString(16)}`);
      }
    }

    this._prevFrame = frame;
    return frame;
  }

  private readCamera(frame: DecodedFrame, v: DataView, p: number): number {
    frame.camera = {
      posX: v.getFloat32(p, true), posY: v.getFloat32(p + 4, true), posZ: v.getFloat32(p + 8, true),
      tgtX: v.getFloat32(p + 12, true), tgtY: v.getFloat32(p + 16, true), tgtZ: v.getFloat32(p + 20, true),
      upX: v.getFloat32(p + 24, true), upY: v.getFloat32(p + 28, true), upZ: v.getFloat32(p + 32, true),
      fov: v.getFloat32(p + 36, true),
    };
    return p + 40;
  }

  private readPrimitives(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    frame.primitives = [];
    for (let i = 0; i < count; i++) {
      frame.primitives.push({
        meshAssetId: v.getInt32(p, true),
        posX: v.getFloat32(p + 4, true), posY: v.getFloat32(p + 8, true), posZ: v.getFloat32(p + 12, true),
        scaleX: v.getFloat32(p + 16, true), scaleY: v.getFloat32(p + 20, true), scaleZ: v.getFloat32(p + 24, true),
        r: v.getFloat32(p + 28, true), g: v.getFloat32(p + 32, true), b: v.getFloat32(p + 36, true), a: v.getFloat32(p + 40, true),
      });
      p += 44;
    }
    return p;
  }

  private applyPrimitiveDelta(frame: DecodedFrame, v: DataView, p: number, changedCount: number): number {
    const totalCount = v.getUint16(p, true);
    p += 4; // totalCount(2) + reserved(2)

    if (frame.primitives.length > totalCount) frame.primitives.length = totalCount;
    while (frame.primitives.length < totalCount) {
      frame.primitives.push({ meshAssetId: 1, posX: 0, posY: 0, posZ: 0, scaleX: 1, scaleY: 1, scaleZ: 1, r: 1, g: 1, b: 1, a: 1 });
    }

    for (let i = 0; i < changedCount; i++) {
      const idx = v.getUint16(p, true); p += 2;
      frame.primitives[idx] = {
        meshAssetId: v.getInt32(p, true),
        posX: v.getFloat32(p + 4, true), posY: v.getFloat32(p + 8, true), posZ: v.getFloat32(p + 12, true),
        scaleX: v.getFloat32(p + 16, true), scaleY: v.getFloat32(p + 20, true), scaleZ: v.getFloat32(p + 24, true),
        r: v.getFloat32(p + 28, true), g: v.getFloat32(p + 32, true), b: v.getFloat32(p + 36, true), a: v.getFloat32(p + 40, true),
      };
      p += 44;
    }
    return p;
  }

  private readSurfaces(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    frame.surfaces = [];
    for (let i = 0; i < count; i++) {
      const materialCustomData = this.readMaterialCustomData(v, p + 57);
      const layerKeyLengthOffset = p + 125;
      const layerKeyLength = v.getUint16(layerKeyLengthOffset, true);
      const layerKeyOffset = layerKeyLengthOffset + 2;
      const layerKeyBytes = new Uint8Array(v.buffer, v.byteOffset + layerKeyOffset, layerKeyLength);

      frame.surfaces.push({
        stableId: v.getInt32(p, true),
        meshAssetId: v.getInt32(p + 4, true),
        materialId: v.getInt32(p + 8, true),
        sortId: v.getInt32(p + 12, true),
        posX: v.getFloat32(p + 16, true), posY: v.getFloat32(p + 20, true), posZ: v.getFloat32(p + 24, true),
        rotX: v.getFloat32(p + 28, true), rotY: v.getFloat32(p + 32, true), rotZ: v.getFloat32(p + 36, true), rotW: v.getFloat32(p + 40, true),
        scaleX: v.getFloat32(p + 44, true), scaleY: v.getFloat32(p + 48, true), scaleZ: v.getFloat32(p + 52, true),
        visibility: v.getUint8(p + 56),
        materialCustomData,
        surfaceLayerKey: this._textDecoder.decode(layerKeyBytes),
      });
      p = layerKeyOffset + layerKeyLength;
    }

    return p;
  }

  private readMaterialCustomData(v: DataView, p: number): MaterialCustomData {
    return {
      slotMask: v.getUint32(p, true),
      slots: [
        this.readVector4(v, p + 4),
        this.readVector4(v, p + 20),
        this.readVector4(v, p + 36),
        this.readVector4(v, p + 52),
      ],
    };
  }

  private readVector4(v: DataView, p: number): [number, number, number, number] {
    return [
      v.getFloat32(p, true),
      v.getFloat32(p + 4, true),
      v.getFloat32(p + 8, true),
      v.getFloat32(p + 12, true),
    ];
  }

  private readUtf8String(v: DataView, p: number): { value: string; next: number } {
    const byteLength = v.getUint16(p, true);
    p += 2;
    const bytes = new Uint8Array(v.buffer, v.byteOffset + p, byteLength);
    return {
      value: this._textDecoder.decode(bytes),
      next: p + byteLength,
    };
  }

  private readGroundOverlays(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    frame.groundOverlays = [];
    for (let i = 0; i < count; i++) {
      frame.groundOverlays.push({
        shape: v.getUint8(p),
        cx: v.getFloat32(p + 1, true), cy: v.getFloat32(p + 5, true), cz: v.getFloat32(p + 9, true),
        radius: v.getFloat32(p + 13, true), innerRadius: v.getFloat32(p + 17, true), angle: v.getFloat32(p + 21, true),
        rotation: v.getFloat32(p + 25, true), length: v.getFloat32(p + 29, true), width: v.getFloat32(p + 33, true),
        fillR: v.getFloat32(p + 37, true), fillG: v.getFloat32(p + 41, true), fillB: v.getFloat32(p + 45, true), fillA: v.getFloat32(p + 49, true),
        borderR: v.getFloat32(p + 53, true), borderG: v.getFloat32(p + 57, true), borderB: v.getFloat32(p + 61, true), borderA: v.getFloat32(p + 65, true),
        borderWidth: v.getFloat32(p + 69, true),
      });
      p += 73; // 1 + 18*4
    }
    return p;
  }

  private readScreenHud(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    const items: ScreenHudItem[] = [];
    for (let i = 0; i < count; i++) {
      const textPacket = this.readTextPacket(v, p + 73);
      items.push({
        kind: v.getUint8(p),
        sx: v.getFloat32(p + 1, true), sy: v.getFloat32(p + 5, true),
        c0r: v.getFloat32(p + 13, true), c0g: v.getFloat32(p + 17, true), c0b: v.getFloat32(p + 21, true), c0a: v.getFloat32(p + 25, true),
        c1r: v.getFloat32(p + 29, true), c1g: v.getFloat32(p + 33, true), c1b: v.getFloat32(p + 37, true), c1a: v.getFloat32(p + 41, true),
        width: v.getFloat32(p + 45, true), height: v.getFloat32(p + 49, true),
        v0: v.getFloat32(p + 53, true), v1: v.getFloat32(p + 57, true),
        id0: v.getInt32(p + 61, true), id1: v.getInt32(p + 65, true),
        fontSize: v.getInt32(p + 69, true),
        textPacket,
      });
      p += 113;
    }

    const stringCount = v.getUint16(p, true); p += 2;
    const strings: string[] = [];
    for (let i = 0; i < stringCount; i++) {
      const len = v.getUint16(p, true); p += 2;
      const bytes = new Uint8Array(v.buffer, v.byteOffset + p, len);
      strings.push(this._textDecoder.decode(bytes));
      p += len;
    }

    const { templates, nextPos } = this.readTemplateTable(v, p);
    p = nextPos;

    for (const item of items) {
      if (item.textPacket && item.textPacket.tokenId > 0) {
        item.textTemplate = templates.get(item.textPacket.tokenId);
      }

      if (item.id0 > 0 && item.id0 < strings.length) {
        item.text = strings[item.id0];
      }
    }

    frame.screenHud = items;
    return p;
  }

  private readScreenOverlays(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    const stringIds: number[] = [];
    const items: ScreenOverlayItem[] = [];
    for (let i = 0; i < count; i++) {
      const textPacket = this.readTextPacket(v, p + 55);
      items.push({
        kind: v.getUint8(p),
        x: v.getInt32(p + 1, true),
        y: v.getInt32(p + 5, true),
        width: v.getInt32(p + 9, true),
        height: v.getInt32(p + 13, true),
        fontSize: v.getInt32(p + 17, true),
        cr: v.getFloat32(p + 21, true), cg: v.getFloat32(p + 25, true),
        cb: v.getFloat32(p + 29, true), ca: v.getFloat32(p + 33, true),
        bgr: v.getFloat32(p + 37, true), bgg: v.getFloat32(p + 41, true),
        bgb: v.getFloat32(p + 45, true), bga: v.getFloat32(p + 49, true),
        text: '',
        textPacket,
      });
      stringIds.push(v.getUint16(p + 53, true));
      p += 95;
    }

    const stringCount = v.getUint16(p, true); p += 2;
    const strings: string[] = [];
    for (let i = 0; i < stringCount; i++) {
      const len = v.getUint16(p, true); p += 2;
      const bytes = new Uint8Array(v.buffer, v.byteOffset + p, len);
      strings.push(this._textDecoder.decode(bytes));
      p += len;
    }

    const { templates, nextPos } = this.readTemplateTable(v, p);
    p = nextPos;

    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (!item) {
        continue;
      }

      const sid = stringIds[i] ?? -1;
      if (item.textPacket && item.textPacket.tokenId > 0) {
        item.textTemplate = templates.get(item.textPacket.tokenId);
      }

      if (item.kind === 0 && sid >= 0 && sid < strings.length) {
        item.text = strings[sid];
      }
    }

    frame.screenOverlays = items;
    return p;
  }

  private readTextPacket(v: DataView, p: number): PresentationTextPacket {
    return {
      tokenId: v.getInt32(p, true),
      argCount: v.getUint8(p + 4),
      args: [
        this.readTextArg(v, p + 8),
        this.readTextArg(v, p + 16),
        this.readTextArg(v, p + 24),
        this.readTextArg(v, p + 32),
      ],
    };
  }

  private readTextArg(v: DataView, p: number): PresentationTextArg {
    return {
      type: v.getUint8(p),
      format: v.getUint8(p + 1),
      raw32: v.getInt32(p + 4, true),
    };
  }

  private readTemplateTable(v: DataView, p: number): { templates: Map<number, string>; nextPos: number } {
    const templateCount = v.getUint16(p, true); p += 2;
    const templates = new Map<number, string>();

    for (let i = 0; i < templateCount; i++) {
      const tokenId = v.getInt32(p, true); p += 4;
      const len = v.getUint16(p, true); p += 2;
      const bytes = new Uint8Array(v.buffer, v.byteOffset + p, len);
      templates.set(tokenId, this._textDecoder.decode(bytes));
      p += len;
    }

    return { templates, nextPos: p };
  }

  private readUiScene(frame: DecodedFrame, v: DataView, p: number): number {
    const jsonLen = v.getInt32(p, true); p += 4;
    if (jsonLen <= 0) {
      frame.uiScene = undefined;
      return p;
    }

    const bytes = new Uint8Array(v.buffer, v.byteOffset + p, jsonLen);
    frame.uiScene = JSON.parse(this._textDecoder.decode(bytes)) as UiScenePayload;
    p += jsonLen;
    return p;
  }

  private readDebugLines(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    frame.debugLines = [];
    for (let i = 0; i < count; i++) {
      frame.debugLines.push({
        ax: v.getFloat32(p, true), ay: v.getFloat32(p + 4, true),
        bx: v.getFloat32(p + 8, true), by: v.getFloat32(p + 12, true),
        thickness: v.getFloat32(p + 16, true),
        r: v.getUint8(p + 20), g: v.getUint8(p + 21), b: v.getUint8(p + 22), a: v.getUint8(p + 23),
      });
      p += 24;
    }
    return p;
  }

  private readDebugCircles(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    frame.debugCircles = [];
    for (let i = 0; i < count; i++) {
      frame.debugCircles.push({
        cx: v.getFloat32(p, true), cy: v.getFloat32(p + 4, true),
        radius: v.getFloat32(p + 8, true), thickness: v.getFloat32(p + 12, true),
        r: v.getUint8(p + 16), g: v.getUint8(p + 17), b: v.getUint8(p + 18), a: v.getUint8(p + 19),
      });
      p += 20;
    }
    return p;
  }

  private readDebugBoxes(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    frame.debugBoxes = [];
    for (let i = 0; i < count; i++) {
      frame.debugBoxes.push({
        cx: v.getFloat32(p, true), cy: v.getFloat32(p + 4, true),
        halfW: v.getFloat32(p + 8, true), halfH: v.getFloat32(p + 12, true),
        rotation: v.getFloat32(p + 16, true), thickness: v.getFloat32(p + 20, true),
        r: v.getUint8(p + 24), g: v.getUint8(p + 25), b: v.getUint8(p + 26), a: v.getUint8(p + 27),
      });
      p += 28;
    }
    return p;
  }

  private emptyFrame(): DecodedFrame {
    return {
      frameNumber: 0, simTick: 0, timestampMs: 0,
      camera: { posX: 0, posY: 10, posZ: 10, tgtX: 0, tgtY: 0, tgtZ: 0, upX: 0, upY: 1, upZ: 0, fov: 60 },
      primitives: [], surfaces: [], debugLines: [], debugCircles: [], debugBoxes: [],
      groundOverlays: [], screenHud: [], screenOverlays: [],
    };
  }

  private cloneFrame(f: DecodedFrame): DecodedFrame {
    return {
      ...f,
      camera: { ...f.camera },
      primitives: f.primitives.map(p => ({ ...p })),
      surfaces: f.surfaces.map(item => ({
        ...item,
        materialCustomData: {
          slotMask: item.materialCustomData.slotMask,
          slots: item.materialCustomData.slots.map(slot => [...slot]) as MaterialCustomData['slots'],
        },
      })),
      debugLines: [...f.debugLines],
      debugCircles: [...f.debugCircles],
      debugBoxes: [...f.debugBoxes],
      groundOverlays: [...f.groundOverlays],
      screenHud: f.screenHud.map(item => ({
        ...item,
        textPacket: item.textPacket
          ? { ...item.textPacket, args: item.textPacket.args.map(arg => ({ ...arg })) }
          : undefined,
      })),
      screenOverlays: f.screenOverlays.map(item => ({
        ...item,
        textPacket: item.textPacket
          ? { ...item.textPacket, args: item.textPacket.args.map(arg => ({ ...arg })) }
          : undefined,
      })),
      uiScene: f.uiScene,
    };
  }
}
