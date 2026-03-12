import type { UiScenePayload } from './UiSceneTypes';

const MSG_FULL = 0x01;
const MSG_MESH_MAP = 0x03;
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

export interface ScreenHudItem {
  kind: number;
  sx: number; sy: number;
  c0r: number; c0g: number; c0b: number; c0a: number;
  c1r: number; c1g: number; c1b: number; c1a: number;
  width: number; height: number;
  v0: number; v1: number;
  id0: number; id1: number;
  fontSize: number;
}

export interface ScreenOverlayItem {
  kind: number;
  x: number; y: number;
  width: number; height: number;
  fontSize: number;
  cr: number; cg: number; cb: number; ca: number;
  bgr: number; bgg: number; bgb: number; bga: number;
  text: string;
}

export interface MeshMapEntry {
  id: number;
  key: string;
}

export interface DecodedFrame {
  frameNumber: number;
  simTick: number;
  timestampMs: number;
  camera: CameraState;
  primitives: PrimitiveItem[];
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
  private _textDecoder = new TextDecoder();

  get meshMap(): MeshMapEntry[] | null { return this._meshMap; }

  decode(buffer: ArrayBuffer): DecodedFrame | null {
    const v = new DataView(buffer);
    if (v.byteLength < 3) return null;

    const msgType = v.getUint8(0);
    if (msgType === MSG_MESH_MAP) {
      this.decodeMeshMap(v);
      return null;
    }
    if (v.byteLength < 17) return null;
    if (msgType === MSG_FULL) return this.decodeFull(v);
    if (msgType === MSG_DELTA) return this.decodeDelta(v);
    return null;
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
        case SEC_GROUND_OVERLAYS: p = this.readGroundOverlays(frame, v, p, itemCount); break;
        case SEC_WORLD_HUD: p += byteLen; break;
        case SEC_SCREEN_HUD: p = this.readScreenHud(frame, v, p, itemCount); break;
        case SEC_UI_SCENE: p = this.readUiScene(frame, v, p); break;
        case SEC_SCREEN_OVERLAY: p = this.readScreenOverlays(frame, v, p, itemCount); break;
        case SEC_DEBUG_LINES: p = this.readDebugLines(frame, v, p, itemCount); break;
        case SEC_DEBUG_CIRCLES: p = this.readDebugCircles(frame, v, p, itemCount); break;
        case SEC_DEBUG_BOXES: p = this.readDebugBoxes(frame, v, p, itemCount); break;
        default: p += byteLen; break;
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
        case SEC_GROUND_OVERLAYS: p = this.readGroundOverlays(frame, v, p, itemCount); break;
        case SEC_WORLD_HUD: p += byteLen; break;
        case SEC_SCREEN_HUD: p = this.readScreenHud(frame, v, p, itemCount); break;
        case SEC_UI_SCENE: p = this.readUiScene(frame, v, p); break;
        case SEC_SCREEN_OVERLAY: p = this.readScreenOverlays(frame, v, p, itemCount); break;
        case SEC_DEBUG_LINES: p = this.readDebugLines(frame, v, p, itemCount); break;
        case SEC_DEBUG_CIRCLES: p = this.readDebugCircles(frame, v, p, itemCount); break;
        case SEC_DEBUG_BOXES: p = this.readDebugBoxes(frame, v, p, itemCount); break;
        default: p += byteLen; break;
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
    frame.screenHud = [];
    for (let i = 0; i < count; i++) {
      frame.screenHud.push({
        kind: v.getUint8(p),
        sx: v.getFloat32(p + 1, true), sy: v.getFloat32(p + 5, true),
        c0r: v.getFloat32(p + 13, true), c0g: v.getFloat32(p + 17, true), c0b: v.getFloat32(p + 21, true), c0a: v.getFloat32(p + 25, true),
        c1r: v.getFloat32(p + 29, true), c1g: v.getFloat32(p + 33, true), c1b: v.getFloat32(p + 37, true), c1a: v.getFloat32(p + 41, true),
        width: v.getFloat32(p + 45, true), height: v.getFloat32(p + 49, true),
        v0: v.getFloat32(p + 53, true), v1: v.getFloat32(p + 57, true),
        id0: v.getInt32(p + 61, true), id1: v.getInt32(p + 65, true),
        fontSize: v.getInt32(p + 69, true),
      });
      p += 73;
    }
    return p;
  }

  private readScreenOverlays(frame: DecodedFrame, v: DataView, p: number, count: number): number {
    const stringIds: number[] = [];
    const items: ScreenOverlayItem[] = [];
    for (let i = 0; i < count; i++) {
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
      });
      stringIds.push(v.getUint16(p + 53, true));
      p += 55;
    }

    const stringCount = v.getUint16(p, true); p += 2;
    const strings: string[] = [];
    for (let i = 0; i < stringCount; i++) {
      const len = v.getUint16(p, true); p += 2;
      const bytes = new Uint8Array(v.buffer, v.byteOffset + p, len);
      strings.push(this._textDecoder.decode(bytes));
      p += len;
    }

    for (let i = 0; i < items.length; i++) {
      const sid = stringIds[i];
      if (items[i].kind === 0 && sid < strings.length) {
        items[i].text = strings[sid];
      }
    }

    frame.screenOverlays = items;
    return p;
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
      primitives: [], debugLines: [], debugCircles: [], debugBoxes: [],
      groundOverlays: [], screenHud: [], screenOverlays: [],
    };
  }

  private cloneFrame(f: DecodedFrame): DecodedFrame {
    return {
      ...f,
      camera: { ...f.camera },
      primitives: f.primitives.map(p => ({ ...p })),
      debugLines: [...f.debugLines],
      debugCircles: [...f.debugCircles],
      debugBoxes: [...f.debugBoxes],
      groundOverlays: [...f.groundOverlays],
      screenHud: [...f.screenHud],
      screenOverlays: [],
      uiScene: f.uiScene,
    };
  }
}
