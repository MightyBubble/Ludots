/** Section type IDs matching the C# FrameProtocol */
export const SECTION = {
  PRIMITIVES: 0x10,
  GROUND_OVERLAYS: 0x11,
  WORLD_HUD: 0x12,
  SCREEN_HUD: 0x13,
  DEBUG_LINES: 0x14,
  DEBUG_CIRCLES: 0x15,
  DEBUG_BOXES: 0x16,
  CAMERA: 0x17,
  END: 0xff,
} as const;

export interface FrameHeader {
  frameNumber: number;
  simTick: number;
  timestampMs: bigint;
}

export interface PrimitiveItem {
  meshAssetId: number;
  stableId: number;
  posX: number; posY: number; posZ: number;
  scaleX: number; scaleY: number; scaleZ: number;
  colorR: number; colorG: number; colorB: number; colorA: number;
}

export interface GroundOverlayItem {
  shape: number;
  centerX: number; centerY: number; centerZ: number;
  radius: number; innerRadius: number;
  angle: number; rotation: number;
  length: number; width: number;
  fillR: number; fillG: number; fillB: number; fillA: number;
  borderR: number; borderG: number; borderB: number; borderA: number;
  borderWidth: number;
}

export interface WorldHudItem {
  kind: number;
  worldX: number; worldY: number; worldZ: number;
  color0R: number; color0G: number; color0B: number; color0A: number;
  color1R: number; color1G: number; color1B: number; color1A: number;
  width: number; height: number;
  value0: number; value1: number;
  id0: number; id1: number;
  fontSize: number;
}

export interface CameraState {
  posX: number; posY: number; posZ: number;
  targetX: number; targetY: number; targetZ: number;
  upX: number; upY: number; upZ: number;
  fovYDeg: number;
}

export interface DebugLine {
  ax: number; ay: number;
  bx: number; by: number;
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
  halfWidth: number; halfHeight: number;
  rotationRadians: number; thickness: number;
  r: number; g: number; b: number; a: number;
}

export interface DecodedFrame {
  header: FrameHeader;
  camera: CameraState | null;
  primitives: PrimitiveItem[];
  groundOverlays: GroundOverlayItem[];
  worldHud: WorldHudItem[];
  debugLines: DebugLine[];
  debugCircles: DebugCircle[];
  debugBoxes: DebugBox[];
}
