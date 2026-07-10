const CONTROL_CHANNEL = 'ludots.dataplane.control';
const SHARED_BUFFER_CHANNEL = 'ludots.dataplane.sharedBuffer';
const COMPACT_TOPIC = 'webui.minimapMarkers';
const WDMM_MAGIC = 0x4d4d4457;
const MINIMAP_SCHEMA_ID = 2;
const WORLD_HALF_EXTENT_CM = 16_384;
const V8_ACTIVE_STATUS = 'V8 backing store';

const state = {
  compact: createLaneState('compact-canvas'),
  browser: createLaneState('browser-canvas'),
  v8: createLaneState('v8-canvas'),
  browserStatus: 'waiting',
  v8Status: 'probing',
  status: 'booting',
  transportMode: 'pending',
  markerCount: 0,
  sessionId: `minimap-bridge-${Date.now().toString(16)}`,
  requestId: 0,
  nativeV8Unavailable: false,
  pendingDescriptor: null,
  processingDescriptor: false,
  skippedDescriptors: 0
};

const refs = {
  connection: document.getElementById('connection-status'),
  markerCount: document.getElementById('marker-count'),
  transportMode: document.getElementById('transport-mode'),
  compactBytes: document.getElementById('compact-bytes'),
  compactParse: document.getElementById('compact-parse'),
  compactDraw: document.getElementById('compact-draw'),
  compactFrames: document.getElementById('compact-frames'),
  browserBytes: document.getElementById('browser-bytes'),
  browserParse: document.getElementById('browser-parse'),
  browserDraw: document.getElementById('browser-draw'),
  browserStatus: document.getElementById('browser-status'),
  v8Bytes: document.getElementById('v8-bytes'),
  v8Parse: document.getElementById('v8-parse'),
  v8Draw: document.getElementById('v8-draw'),
  v8Status: document.getElementById('v8-status'),
  v8Badge: document.getElementById('v8-badge'),
  v8Message: document.getElementById('v8-message')
};

installResizeHandlers();
start().catch((error) => {
  state.status = error instanceof Error ? error.message : String(error);
  updateChrome();
});

async function start() {
  const transport = await waitForTransport();
  state.transportMode = transport.mode ?? transport.name ?? 'browser bridge';
  window.addEventListener('message', (event) => {
    handleHostMessage(event.data, transport).catch((error) => {
      state.status = error instanceof Error ? error.message : String(error);
      updateChrome();
    });
  });

  await sendControl(transport, 'handshake', 'system', {
    metadata: { showcase: 'browser_minimap_bridge_compare' },
    capabilities: ['handshake', 'subscribe', 'shared-buffer-descriptor', 'minimap-markers.wdmm.v1']
  });
  await sendControl(transport, 'subscribe', COMPACT_TOPIC, {
    subscriptionId: 'compact-markers',
    snapshot: true
  });

  state.status = 'subscribed';
  updateChrome();
  requestAnimationFrame(renderLoop);
}

function createLaneState(canvasId) {
  const canvas = document.getElementById(canvasId);
  return {
    canvas,
    ctx: canvas.getContext('2d', { alpha: true }),
    markers: null,
    payloadBytes: 0,
    parseMs: 0,
    drawMs: 0,
    frames: 0,
    dirty: true
  };
}

function installResizeHandlers() {
  resizeCanvases();
  window.addEventListener('resize', resizeCanvases, { passive: true });
}

function resizeCanvases() {
  for (const lane of [state.compact, state.browser, state.v8]) {
    const rect = lane.canvas.getBoundingClientRect();
    const width = Math.max(260, Math.floor(rect.width * window.devicePixelRatio));
    const height = Math.max(260, Math.floor(rect.height * window.devicePixelRatio));
    if (lane.canvas.width !== width || lane.canvas.height !== height) {
      lane.canvas.width = width;
      lane.canvas.height = height;
      lane.dirty = true;
    }
  }
}

function waitForTransport() {
  if (window.ludotsDataplane) {
    return Promise.resolve(window.ludotsDataplane);
  }

  return new Promise((resolve, reject) => {
    const startedAt = performance.now();
    const timer = window.setInterval(() => {
      if (window.ludotsDataplane) {
        window.clearInterval(timer);
        resolve(window.ludotsDataplane);
        return;
      }

      if (performance.now() - startedAt > 5000) {
        window.clearInterval(timer);
        reject(new Error('window.ludotsDataplane unavailable'));
      }
    }, 40);
  });
}

function sendControl(transport, kind, topic, payload) {
  const requestId = ++state.requestId;
  transport.postMessage({
    schemaVersion: 1,
    sessionId: state.sessionId,
    requestId,
    kind,
    topic,
    payload
  });
  return Promise.resolve(requestId);
}

async function handleHostMessage(raw, transport) {
  const message = normalizeHostMessage(raw);
  if (!message) {
    return;
  }

  if (message.kind === 'sharedBuffer' && message.topic === COMPACT_TOPIC) {
    await handleCompactDescriptor(message, transport);
    return;
  }

  const kind = normalizeKind(message.kind);
  if (kind === 'handshakeAck') {
    state.transportMode = message.payload?.transportMode ?? state.transportMode;
  }
}

async function handleCompactDescriptor(message, transport) {
  const descriptor = normalizeDescriptor(message.payload?.sharedBuffer ?? message.sharedBuffer);
  state.pendingDescriptor = descriptor;
  if (state.processingDescriptor) {
    state.skippedDescriptors += 1;
    state.status = 'latest queued';
    updateChrome();
    return;
  }

  await drainCompactDescriptors(transport);
}

async function drainCompactDescriptors(transport) {
  state.processingDescriptor = true;
  try {
    while (state.pendingDescriptor) {
      const descriptor = state.pendingDescriptor;
      state.pendingDescriptor = null;
      await processCompactDescriptor(descriptor, transport);
    }
  } finally {
    state.processingDescriptor = false;
  }
}

async function processCompactDescriptor(descriptor, transport) {
  const started = performance.now();
  let rawBytes;
  try {
    rawBytes = await transport.readSharedBuffer(descriptor);
  } catch {
    state.skippedDescriptors += 1;
    state.status = 'stale skipped';
    updateChrome();
    return;
  }

  const bytes = normalizeByteView(rawBytes);
  const decoded = decodeWdmm(bytes);
  const parseMs = performance.now() - started;
  state.compact.markers = decoded;
  state.compact.payloadBytes = bytes.byteLength;
  state.compact.parseMs = parseMs;
  state.compact.dirty = true;
  updateBrowserOwnedArrayBufferLane(bytes);
  await updateTrueV8BufferLane(transport, descriptor);
  state.markerCount = decoded.count;
  state.status = state.skippedDescriptors > 0 ? `subscribed, skipped ${state.skippedDescriptors}` : 'subscribed';
  updateChrome();
}

function updateBrowserOwnedArrayBufferLane(sourceBytes) {
  const started = performance.now();
  const ownedBuffer = new ArrayBuffer(sourceBytes.byteLength);
  const ownedBytes = new Uint8Array(ownedBuffer);
  ownedBytes.set(sourceBytes);
  const decoded = decodeWdmm(ownedBytes);
  state.browser.markers = decoded;
  state.browser.payloadBytes = ownedBuffer.byteLength;
  state.browser.parseMs = performance.now() - started;
  state.browser.dirty = true;
  state.browserStatus = 'owned copy';
}

function normalizeHostMessage(raw) {
  if (!raw) {
    return null;
  }

  if (raw.channel === CONTROL_CHANNEL || raw.channel === SHARED_BUFFER_CHANNEL) {
    const packet = typeof raw.payload === 'string' ? parseJson(raw.payload) : raw.payload;
    return normalizeWirePacket(packet);
  }

  return normalizeWirePacket(raw);
}

function normalizeWirePacket(packet) {
  if (!packet || packet.schemaVersion !== 1) {
    return null;
  }

  const payload = packet.payload;
  if (payload?.schemaVersion === 1) {
    return {
      ...payload,
      kind: payload.kind,
      topic: payload.topic ?? packet.topic,
      payload: payload.payload ?? {}
    };
  }

  const kind = normalizeKind(packet.kind);
  if (packet.payload?.sharedBuffer) {
    return { ...packet, kind: 'sharedBuffer' };
  }

  return { ...packet, kind, payload: payload ?? {} };
}

function decodeWdmm(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (view.byteLength < 20 || view.getUint32(0, true) !== WDMM_MAGIC) {
    throw new Error('Invalid WDMM minimap marker packet.');
  }

  const schemaId = view.getInt32(4, true);
  if (schemaId !== MINIMAP_SCHEMA_ID) {
    throw new Error(`Unexpected minimap schema id ${schemaId}.`);
  }

  const count = view.getInt32(8, true);
  const stableIds = new Int32Array(count);
  const x = new Float32Array(count);
  const y = new Float32Array(count);
  const color = new Uint32Array(count);
  const size = new Float32Array(count);
  let offset = 20;
  for (let i = 0; i < count; i += 1) {
    stableIds[i] = view.getInt32(offset, true);
    x[i] = view.getFloat32(offset + 4, true);
    y[i] = view.getFloat32(offset + 8, true);
    color[i] = view.getUint32(offset + 12, true);
    size[i] = view.getFloat32(offset + 16, true);
    offset += 24;
  }

  return { count, stableIds, x, y, color, size };
}

function renderLoop() {
  drawLane(state.compact, { emphasize: 'compact' });
  drawLane(state.browser, { emphasize: 'browser' });
  drawLane(state.v8, { emphasize: 'v8' });
  requestAnimationFrame(renderLoop);
}

function drawLane(lane, options) {
  if (!lane.dirty || !lane.markers) {
    return;
  }

  const started = performance.now();
  const canvas = lane.canvas;
  const ctx = lane.ctx;
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  drawGrid(ctx, width, height, options);
  drawMarkers(ctx, lane.markers, width, height, options);
  lane.drawMs = performance.now() - started;
  lane.frames += 1;
  lane.dirty = false;
  updateChrome();
}

function drawGrid(ctx, width, height, options) {
  ctx.save();
  ctx.globalAlpha = options.ghost ? 0.54 : 0.72;
  ctx.strokeStyle = options.emphasize === 'v8' ? 'rgba(126,231,166,0.24)' : 'rgba(110,139,145,0.18)';
  ctx.lineWidth = Math.max(1, window.devicePixelRatio);
  const step = Math.max(26, Math.floor(width / 12));
  for (let x = 0; x <= width; x += step) {
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
    ctx.stroke();
  }
  for (let y = 0; y <= height; y += step) {
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }
  ctx.restore();
}

function drawMarkers(ctx, markers, width, height, options) {
  const scale = Math.min(width, height) / (WORLD_HALF_EXTENT_CM * 2);
  const centerX = width * 0.5;
  const centerY = height * 0.5;
  const alpha = options.ghost ? 0.42 : 0.72;
  ctx.save();
  ctx.globalCompositeOperation = 'source-over';
  for (let i = 0; i < markers.count; i += 1) {
    const px = centerX + (markers.x[i] * scale);
    const py = centerY - (markers.y[i] * scale);
    if (px < -4 || py < -4 || px > width + 4 || py > height + 4) {
      continue;
    }

    const radius = Math.min(1.35, Math.max(0.45, markers.size[i] * window.devicePixelRatio * 0.28));
    ctx.fillStyle = toRgba(markers.color[i], alpha);
    ctx.fillRect(px - (radius * 0.5), py - (radius * 0.5), radius, radius);
  }
  ctx.restore();
}

function toRgba(colorKey, alphaScale) {
  const a = ((colorKey >>> 24) & 255) / 255;
  const r = (colorKey >>> 16) & 255;
  const g = (colorKey >>> 8) & 255;
  const b = colorKey & 255;
  return `rgba(${r}, ${g}, ${b}, ${(a * alphaScale).toFixed(3)})`;
}

function updateChrome() {
  refs.connection.textContent = state.status;
  refs.markerCount.textContent = `${formatNumber(state.markerCount)} markers`;
  refs.transportMode.textContent = state.transportMode;
  refs.compactBytes.textContent = formatBytes(state.compact.payloadBytes);
  refs.compactParse.textContent = `${state.compact.parseMs.toFixed(2)} ms`;
  refs.compactDraw.textContent = `${state.compact.drawMs.toFixed(2)} ms`;
  refs.compactFrames.textContent = `${state.compact.frames}`;
  refs.browserBytes.textContent = formatBytes(state.browser.payloadBytes);
  refs.browserParse.textContent = `${state.browser.parseMs.toFixed(2)} ms`;
  refs.browserDraw.textContent = `${state.browser.drawMs.toFixed(2)} ms`;
  refs.browserStatus.textContent = state.browserStatus;
  refs.v8Bytes.textContent = formatBytes(state.v8.payloadBytes);
  refs.v8Parse.textContent = `${state.v8.parseMs.toFixed(2)} ms`;
  refs.v8Draw.textContent = `${state.v8.drawMs.toFixed(2)} ms`;
  const v8Active = isV8Active();
  const v8Message = shouldShowV8Message() ? summarizeV8Status(state.v8Status) : '';
  refs.v8Status.textContent = summarizeV8Status(state.v8Status);
  refs.v8Message.textContent = v8Message;
  refs.v8Message.hidden = v8Message.length === 0;
  refs.v8Badge.textContent = v8Active ? 'active' : 'required';
  refs.v8Badge.className = v8Active
    ? 'badge badge-good'
    : 'badge badge-caution';
}

function normalizeDescriptor(descriptor) {
  return {
    bufferId: String(descriptor?.bufferId ?? descriptor?.BufferId ?? ''),
    byteOffset: Number(descriptor?.byteOffset ?? descriptor?.ByteOffset ?? 0),
    byteLength: Number(descriptor?.byteLength ?? descriptor?.ByteLength ?? 0),
    sequence: Number(descriptor?.sequence ?? descriptor?.Sequence ?? 0)
  };
}

function normalizeByteView(value) {
  if (value instanceof Uint8Array) {
    return value;
  }

  if (value instanceof ArrayBuffer) {
    return new Uint8Array(value);
  }

  if (Array.isArray(value)) {
    return Uint8Array.from(value);
  }

  if (value && typeof value.length === 'number') {
    return Uint8Array.from(value);
  }

  throw new TypeError('Unsupported shared-buffer read result.');
}

async function updateTrueV8BufferLane(transport, descriptor) {
  if (state.nativeV8Unavailable) {
    return;
  }

  if (typeof transport.acquireV8Buffer !== 'function') {
    state.v8Status = 'missing';
    state.nativeV8Unavailable = true;
    return;
  }

  try {
    const value = await transport.acquireV8Buffer(descriptor);
    if (value instanceof ArrayBuffer) {
      const started = performance.now();
      const bytes = new Uint8Array(value);
      const decoded = decodeWdmm(bytes);
      state.v8.markers = decoded;
      state.v8.payloadBytes = value.byteLength;
      state.v8.parseMs = performance.now() - started;
      state.v8.dirty = true;
      state.v8Status = V8_ACTIVE_STATUS;
      state.markerCount = Math.max(state.markerCount, decoded.count);
      return;
    }

    state.v8Status = `not ArrayBuffer: ${describeReadCopy(value)}`;
  } catch (error) {
    const normalized = normalizeV8Error(error);
    if (normalized === 'stale descriptor') {
      state.skippedDescriptors += 1;
      if (!state.v8.markers) {
        state.v8Status = 'stale skipped';
      }
      return;
    }

    state.v8Status = normalized;
    if (state.v8Status === 'missing') {
      state.nativeV8Unavailable = true;
    }
  }
}

function describeReadCopy(value) {
  if (value instanceof Uint8Array) {
    return 'copy-read';
  }

  if (Array.isArray(value)) {
    return 'copy-read';
  }

  if (value && typeof value.length === 'number') {
    return 'copy-read';
  }

  if (value instanceof ArrayBuffer) {
    return 'ArrayBuffer';
  }

  return 'unsupported';
}

function normalizeV8Error(error) {
  const message = error instanceof Error ? error.message : String(error);
  if (message.includes('descriptor range is no longer active')) {
    return 'stale descriptor';
  }

  return message.includes('missing acquireV8Buffer')
    ? 'missing'
    : message;
}

function summarizeV8Status(status) {
  if (status.includes('backing store could not be created')) {
    return 'backing store unavailable';
  }

  return status;
}

function isV8Active() {
  return state.v8Status === V8_ACTIVE_STATUS;
}

function shouldShowV8Message() {
  return !isV8Active() &&
    state.v8Status !== 'probing' &&
    state.v8Status !== 'stale skipped';
}

function normalizeKind(kind) {
  if (!kind) {
    return 'unknown';
  }

  const text = String(kind);
  return text.charAt(0).toLowerCase() + text.slice(1);
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return '0 B';
  }

  if (bytes < 1024) {
    return `${bytes} B`;
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }

  return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
}

function formatNumber(value) {
  return Number(value || 0).toLocaleString('en-US');
}
