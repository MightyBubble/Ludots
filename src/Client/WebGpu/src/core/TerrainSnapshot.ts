const MSG_TERRAIN_SNAPSHOT = 0x06;
const MSG_TERRAIN_CLEAR = 0x07;
const TERRAIN_VERSION = 1;
const HEADER_BYTES = 47;
const CHUNK_HEADER_BYTES = 48;
const HEIGHT_SAMPLE_BYTES = 4;

export interface TerrainBoundsCm {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface TerrainChunkSnapshot {
  chunkX: number;
  chunkY: number;
  revision: number;
  bounds: TerrainBoundsCm;
  sampleColumns: number;
  sampleRows: number;
  sampleStepXCm: number;
  sampleStepYCm: number;
  heightsCm: Float32Array;
}

export interface TerrainSnapshot {
  revision: number;
  bounds: TerrainBoundsCm;
  chunkColumns: number;
  chunkRows: number;
  samplesPerChunkColumn: number;
  samplesPerChunkRow: number;
  defaultLayerIndex: number;
  chunks: TerrainChunkSnapshot[];
}

export type TerrainWireMessage =
  | { kind: 'snapshot'; snapshot: TerrainSnapshot }
  | { kind: 'clear' };

export function decodeTerrainMessage(buffer: ArrayBuffer): TerrainWireMessage | null {
  const view = new DataView(buffer);
  if (view.byteLength < 1) {
    throw new Error('Web terrain message is empty.');
  }

  const messageType = view.getUint8(0);
  if (messageType === MSG_TERRAIN_CLEAR) {
    if (view.byteLength !== 1) {
      throw new Error(`Web terrain clear message has ${view.byteLength - 1} trailing bytes.`);
    }
    return { kind: 'clear' };
  }
  if (messageType !== MSG_TERRAIN_SNAPSHOT) {
    return null;
  }
  if (view.byteLength < HEADER_BYTES) {
    throw new Error(`Web terrain snapshot is truncated: ${view.byteLength}/${HEADER_BYTES} bytes.`);
  }

  const version = view.getUint16(1, true);
  if (version !== TERRAIN_VERSION) {
    throw new Error(`Unsupported web terrain protocol version ${version}; expected ${TERRAIN_VERSION}.`);
  }

  const revision = view.getInt32(3, true);
  const bounds = readBounds(view, 7, 'terrain');
  const chunkColumns = readPositiveInt32(view, 23, 'terrain chunk columns');
  const chunkRows = readPositiveInt32(view, 27, 'terrain chunk rows');
  const samplesPerChunkColumn = readPositiveInt32(view, 31, 'terrain samples per chunk column');
  const samplesPerChunkRow = readPositiveInt32(view, 35, 'terrain samples per chunk row');
  const defaultLayerIndex = view.getInt32(39, true);
  const chunkCount = readPositiveInt32(view, 43, 'terrain chunk count');
  const expectedChunkCount = chunkColumns * chunkRows;
  if (!Number.isSafeInteger(expectedChunkCount) || chunkCount !== expectedChunkCount) {
    throw new Error(
      `Web terrain snapshot declares ${chunkCount} chunks for a ${chunkColumns}x${chunkRows} grid.`,
    );
  }
  if (chunkCount > Math.floor((view.byteLength - HEADER_BYTES) / CHUNK_HEADER_BYTES)) {
    throw new Error(`Web terrain snapshot cannot contain ${chunkCount} chunk headers in ${view.byteLength} bytes.`);
  }

  const chunks = new Array<TerrainChunkSnapshot>(chunkCount);
  let cursor = HEADER_BYTES;
  for (let chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
    if (cursor + CHUNK_HEADER_BYTES > view.byteLength) {
      throw new Error(`Web terrain chunk ${chunkIndex} header is truncated at byte ${cursor}.`);
    }

    const expectedChunkX = chunkIndex % chunkColumns;
    const expectedChunkY = Math.floor(chunkIndex / chunkColumns);
    const chunkX = view.getInt32(cursor, true);
    const chunkY = view.getInt32(cursor + 4, true);
    if (chunkX !== expectedChunkX || chunkY !== expectedChunkY) {
      throw new Error(
        `Web terrain chunk ${chunkIndex} has coordinates (${chunkX},${chunkY}); expected (${expectedChunkX},${expectedChunkY}).`,
      );
    }

    const chunkRevision = view.getInt32(cursor + 8, true);
    const chunkBounds = readBounds(view, cursor + 12, `terrain chunk (${chunkX},${chunkY})`);
    const sampleColumns = readPositiveInt32(view, cursor + 28, `terrain chunk (${chunkX},${chunkY}) sample columns`);
    const sampleRows = readPositiveInt32(view, cursor + 32, `terrain chunk (${chunkX},${chunkY}) sample rows`);
    if (sampleColumns < 2 || sampleRows < 2) {
      throw new Error(`Web terrain chunk (${chunkX},${chunkY}) must contain at least 2x2 samples.`);
    }

    const sampleStepXCm = view.getFloat32(cursor + 36, true);
    const sampleStepYCm = view.getFloat32(cursor + 40, true);
    if (!Number.isFinite(sampleStepXCm) || sampleStepXCm <= 0 ||
        !Number.isFinite(sampleStepYCm) || sampleStepYCm <= 0) {
      throw new Error(`Web terrain chunk (${chunkX},${chunkY}) has invalid sample spacing.`);
    }

    const sampleCount = readPositiveInt32(view, cursor + 44, `terrain chunk (${chunkX},${chunkY}) sample count`);
    const expectedSampleCount = sampleColumns * sampleRows;
    if (!Number.isSafeInteger(expectedSampleCount) || sampleCount !== expectedSampleCount) {
      throw new Error(
        `Web terrain chunk (${chunkX},${chunkY}) declares ${sampleCount} samples for ${sampleColumns}x${sampleRows}.`,
      );
    }

    cursor += CHUNK_HEADER_BYTES;
    const sampleByteLength = sampleCount * HEIGHT_SAMPLE_BYTES;
    if (!Number.isSafeInteger(sampleByteLength) || cursor + sampleByteLength > view.byteLength) {
      throw new Error(`Web terrain chunk (${chunkX},${chunkY}) height payload is truncated.`);
    }

    const heightsCm = new Float32Array(sampleCount);
    for (let sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
      const heightCm = view.getFloat32(cursor + sampleIndex * HEIGHT_SAMPLE_BYTES, true);
      if (!Number.isFinite(heightCm)) {
        throw new Error(`Web terrain chunk (${chunkX},${chunkY}) sample ${sampleIndex} is not finite.`);
      }
      heightsCm[sampleIndex] = heightCm;
    }
    cursor += sampleByteLength;

    chunks[chunkIndex] = {
      chunkX,
      chunkY,
      revision: chunkRevision,
      bounds: chunkBounds,
      sampleColumns,
      sampleRows,
      sampleStepXCm,
      sampleStepYCm,
      heightsCm,
    };
  }

  if (cursor !== view.byteLength) {
    throw new Error(`Web terrain snapshot has ${view.byteLength - cursor} unexplained trailing bytes.`);
  }

  return {
    kind: 'snapshot',
    snapshot: {
      revision,
      bounds,
      chunkColumns,
      chunkRows,
      samplesPerChunkColumn,
      samplesPerChunkRow,
      defaultLayerIndex,
      chunks,
    },
  };
}

function readBounds(view: DataView, offset: number, label: string): TerrainBoundsCm {
  const bounds = {
    x: view.getInt32(offset, true),
    y: view.getInt32(offset + 4, true),
    width: view.getInt32(offset + 8, true),
    height: view.getInt32(offset + 12, true),
  };
  if (bounds.width <= 0 || bounds.height <= 0) {
    throw new Error(`${label} has invalid bounds ${bounds.width}x${bounds.height} cm.`);
  }
  return bounds;
}

function readPositiveInt32(view: DataView, offset: number, label: string): number {
  const value = view.getInt32(offset, true);
  if (value <= 0) {
    throw new Error(`${label} must be positive; received ${value}.`);
  }
  return value;
}
