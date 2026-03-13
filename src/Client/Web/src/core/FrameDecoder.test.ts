import { describe, expect, it } from 'vitest';
import { FrameDecoder, type PrimitiveItem } from './FrameDecoder';

const MSG_FULL = 0x01;
const MSG_DELTA = 0x05;
const SEC_END = 0x00;
const SEC_CAMERA = 0x01;
const SEC_PRIMITIVES = 0x02;
const SEC_PRIMITIVES_DELTA = 0x18;
const FRAME_HEADER_BYTES = 17;
const SECTION_HEADER_BYTES = 7;
const CAMERA_BYTES = 40;
const PRIMITIVE_BYTES = 48;

describe('FrameDecoder', () => {
  it('decodes full primitive snapshots with stable ids', () => {
    const decoder = new FrameDecoder();
    const frame = decoder.decode(buildFullFrame([
      createPrimitive(101, 7, 1, 1),
      createPrimitive(202, 8, 2, 2),
    ]));

    expect(frame).not.toBeNull();
    expect(frame!.primitives.map((item) => item.stableId)).toEqual([101, 202]);
    expect(frame!.primitives.map((item) => item.meshAssetId)).toEqual([7, 8]);
  });

  it('applies primitive deltas by stable id and current order', () => {
    const decoder = new FrameDecoder();
    decoder.decode(buildFullFrame([
      createPrimitive(101, 7, 1, 1),
      createPrimitive(202, 8, 2, 2),
    ]));

    const reorder = decoder.decode(buildDeltaFrame({
      changed: [],
      removedStableIds: [],
      orderedStableIds: [202, 101],
    }));
    expect(reorder).not.toBeNull();
    expect(reorder!.primitives.map((item) => item.stableId)).toEqual([202, 101]);

    const hide = decoder.decode(buildDeltaFrame({
      changed: [],
      removedStableIds: [101],
      orderedStableIds: [202],
    }));
    expect(hide).not.toBeNull();
    expect(hide!.primitives.map((item) => item.stableId)).toEqual([202]);

    const showAndSpawn = decoder.decode(buildDeltaFrame({
      changed: [
        createPrimitive(101, 7, 5, 5),
        createPrimitive(303, 9, 6, 6),
      ],
      removedStableIds: [],
      orderedStableIds: [101, 202, 303],
    }));

    expect(showAndSpawn).not.toBeNull();
    expect(showAndSpawn!.primitives.map((item) => item.stableId)).toEqual([101, 202, 303]);
    expect(showAndSpawn!.primitives[0].posX).toBeCloseTo(5);
    expect(showAndSpawn!.primitives[2].meshAssetId).toBe(9);
  });
});

function buildFullFrame(primitives: PrimitiveItem[]): ArrayBuffer {
  const primitiveBytes = primitives.length * PRIMITIVE_BYTES;
  const totalBytes = FRAME_HEADER_BYTES
    + SECTION_HEADER_BYTES + CAMERA_BYTES
    + SECTION_HEADER_BYTES + primitiveBytes
    + 1;

  const buffer = new ArrayBuffer(totalBytes);
  const view = new DataView(buffer);
  let p = 0;

  view.setUint8(p, MSG_FULL); p += 1;
  view.setUint32(p, 1, true); p += 4;
  view.setInt32(p, 1, true); p += 4;
  view.setBigInt64(p, BigInt(1000), true); p += 8;

  view.setUint8(p, SEC_CAMERA);
  view.setUint16(p + 1, 1, true);
  view.setInt32(p + 3, CAMERA_BYTES, true);
  p += SECTION_HEADER_BYTES;
  for (let i = 0; i < 10; i++) {
    view.setFloat32(p, i === 9 ? 60 : 0, true);
    p += 4;
  }

  view.setUint8(p, SEC_PRIMITIVES);
  view.setUint16(p + 1, primitives.length, true);
  view.setInt32(p + 3, primitiveBytes, true);
  p += SECTION_HEADER_BYTES;

  for (const primitive of primitives) {
    p = writePrimitive(view, p, primitive);
  }

  view.setUint8(p, SEC_END);
  return buffer;
}

function buildDeltaFrame(options: {
  changed: PrimitiveItem[];
  removedStableIds: number[];
  orderedStableIds: number[];
}): ArrayBuffer {
  const changedBytes = options.changed.length * PRIMITIVE_BYTES;
  const removedBytes = options.removedStableIds.length * 4;
  const orderBytes = options.orderedStableIds.length * 4;
  const deltaBytes = 4 + removedBytes + changedBytes + orderBytes;
  const totalBytes = FRAME_HEADER_BYTES
    + SECTION_HEADER_BYTES + CAMERA_BYTES
    + SECTION_HEADER_BYTES + deltaBytes
    + 1;

  const buffer = new ArrayBuffer(totalBytes);
  const view = new DataView(buffer);
  let p = 0;

  view.setUint8(p, MSG_DELTA); p += 1;
  view.setUint32(p, 2, true); p += 4;
  view.setInt32(p, 2, true); p += 4;
  view.setBigInt64(p, BigInt(2000), true); p += 8;

  view.setUint8(p, SEC_CAMERA);
  view.setUint16(p + 1, 1, true);
  view.setInt32(p + 3, CAMERA_BYTES, true);
  p += SECTION_HEADER_BYTES;
  for (let i = 0; i < 10; i++) {
    view.setFloat32(p, i === 9 ? 60 : 0, true);
    p += 4;
  }

  view.setUint8(p, SEC_PRIMITIVES_DELTA);
  view.setUint16(p + 1, options.changed.length, true);
  view.setInt32(p + 3, deltaBytes, true);
  p += SECTION_HEADER_BYTES;

  view.setUint16(p, options.orderedStableIds.length, true); p += 2;
  view.setUint16(p, options.removedStableIds.length, true); p += 2;

  for (const stableId of options.removedStableIds) {
    view.setInt32(p, stableId, true);
    p += 4;
  }

  for (const primitive of options.changed) {
    p = writePrimitive(view, p, primitive);
  }

  for (const stableId of options.orderedStableIds) {
    view.setInt32(p, stableId, true);
    p += 4;
  }

  view.setUint8(p, SEC_END);
  return buffer;
}

function writePrimitive(view: DataView, p: number, primitive: PrimitiveItem): number {
  view.setInt32(p, primitive.meshAssetId, true);
  view.setInt32(p + 4, primitive.stableId, true);
  view.setFloat32(p + 8, primitive.posX, true);
  view.setFloat32(p + 12, primitive.posY, true);
  view.setFloat32(p + 16, primitive.posZ, true);
  view.setFloat32(p + 20, primitive.scaleX, true);
  view.setFloat32(p + 24, primitive.scaleY, true);
  view.setFloat32(p + 28, primitive.scaleZ, true);
  view.setFloat32(p + 32, primitive.r, true);
  view.setFloat32(p + 36, primitive.g, true);
  view.setFloat32(p + 40, primitive.b, true);
  view.setFloat32(p + 44, primitive.a, true);
  return p + PRIMITIVE_BYTES;
}

function createPrimitive(stableId: number, meshAssetId: number, posX: number, posZ: number): PrimitiveItem {
  return {
    meshAssetId,
    stableId,
    posX,
    posY: 0.5,
    posZ,
    scaleX: 1,
    scaleY: 1,
    scaleZ: 1,
    r: 0.25,
    g: 0.5,
    b: 0.75,
    a: 1,
  };
}
