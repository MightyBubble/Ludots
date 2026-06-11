export const SECTION = {
  END: 0x00,
  CAMERA: 0x01,
  PRIMITIVES: 0x02,
  GROUND_OVERLAYS: 0x03,
  WORLD_HUD: 0x04,
  SCREEN_HUD: 0x05,
  UI_SCENE: 0x09,
  SCREEN_OVERLAY: 0x0a,
  DEBUG_LINES: 0x10,
  DEBUG_CIRCLES: 0x11,
  DEBUG_BOXES: 0x12,
  PRIMITIVES_DELTA: 0x18,
  SURFACES: 0x19,
} as const;

export const MESSAGE = {
  FRAME: 0x01,
  MESH_MAP: 0x03,
  MATERIAL_MAP: 0x04,
  DELTA: 0x05,
} as const;

export type {
  CameraState,
  DebugBox,
  DebugCircle,
  DebugLine,
  DecodedFrame,
  GroundOverlayItem,
  MaterialCustomData,
  MaterialMapEntry,
  PrimitiveItem,
  SurfaceItem,
} from './FrameDecoder';
