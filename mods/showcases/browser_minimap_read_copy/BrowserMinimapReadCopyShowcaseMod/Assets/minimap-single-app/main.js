const CONTROL_CHANNEL = 'ludots.dataplane.control';
const SHARED_BUFFER_CHANNEL = 'ludots.dataplane.sharedBuffer';
const COMPACT_TOPIC = 'webui.minimapMarkers';
const WDMM_MAGIC = 0x4d4d4457;
const MINIMAP_SCHEMA_ID = 2;
const WORLD_HALF_EXTENT_CM = 16_384;

const MODES = {
  'read-copy': {
    title: 'Read-Copy Buffer',
    subtitle: 'WDMM descriptor through the current shared-buffer read path.',
    badge: 'current',
    badgeClass: 'badge-read',
    readLabel: 'read'
  },
  'browser-arraybuffer': {
    title: 'Browser ArrayBuffer',
    subtitle: 'Current read path plus a browser-owned ArrayBuffer copy before decode.',
    badge: 'browser copy',
    badgeClass: 'badge-browser',
    readLabel: 'read'
  },
  'true-v8': {
    title: 'True V8 Buffer',
    subtitle: 'Native CEF render-process bridge returning a V8 backing-store ArrayBuffer.',
    badge: 'native v8',
    badgeClass: 'badge-v8',
    readLabel: 'acquire'
  }
};

const mode = resolveMode();
const modeInfo = MODES[mode];

const state = {
  markers: null,
  payloadBytes: 0,
  readMs: 0,
  copyMs: null,
  parseMs: 0,
  drawMs: 0,
  frames: 0,
  skipped: 0,
  status: 'booting',
  pathStatus: 'waiting',
  transportMode: 'pending',
  markerCount: 0,
  sessionId: `minimap-${mode}-${Date.now().toString(16)}`,
  requestId: 0,
  pendingDescriptor: null,
  processingDescriptor: false,
  dirty: true
};

const refs = {
  title: document.getElementById('path-title'),
  subtitle: document.getElementById('path-subtitle'),
  badge: document.getElementById('path-badge'),
  connection: document.getElementById('connection-status'),
  markerCount: document.getElementById('marker-count'),
  transportMode: document.getElementById('transport-mode'),
  payloadBytes: document.getElementById('payload-bytes'),
  readLabel: document.getElementById('read-label'),
  readMs: document.getElementById('read-ms'),
  copyMs: document.getElementById('copy-ms'),
  parseMs: document.getElementById('parse-ms'),
  drawMs: document.getElementById('draw-ms'),
  frames: document.getElementById('frames'),
  skipped: document.getElementById('skipped'),
  pathStatus: document.getElementById('path-status'),
  canvas: document.getElementById('minimap-canvas')
};
refs.ctx = refs.canvas.getContext('2d', { alpha: true });

installModeChrome();
installResizeHandlers();
start().catch((error) => {
  state.status = normalizeError(error);
  state.pathStatus = 'failed';
  updateChrome();
});

async function start() {
  const transport = await waitForTransport();
  state.transportMode = transport.mode ?? transport.name ?? 'browser bridge';
  window.addEventListener('message', (event) => {
    handleHostMessage(event.data, transport).catch((error) => {
      state.status = normalizeError(error);
      updateChrome();
    });
  });

  await sendControl(transport, 'handshake', 'system', {
    metadata: { showcase: `browser_minimap_${mode}` },
    capabilities: ['handshake', 'subscribe', 'shared-buffer-descriptor', 'minimap-markers.wdmm.v1']
  });
  await sendControl(transport, 'subscribe', COMPACT_TOPIC, {
    subscriptionId: `${mode}-markers`,
    snapshot: true
  });

  state.status = 'subscribed';
  updateChrome();
  requestAnimationFrame(renderLoop);
  window.__LUDOTS_MINIMAP_SINGLE_READY__ = { mode };
}

function resolveMode() {
  const queryMode = new URLSearchParams(window.location.search).get('mode');
  return Object.prototype.hasOwnProperty.call(MODES, queryMode) ? queryMode : 'read-copy';
}

function installModeChrome() {
  refs.title.textContent = modeInfo.title;
  refs.subtitle.textContent = modeInfo.subtitle;
  refs.badge.textContent = modeInfo.badge;
  refs.badge.className = `badge ${modeInfo.badgeClass}`;
  refs.readLabel.textContent = modeInfo.readLabel;
  document.title = `Ludots ${modeInfo.title}`;
}

function installResizeHandlers() {
  resizeCanvas();
  window.addEventListener('resize', resizeCanvas, { passive: true });
}

function resizeCanvas() {
  const rect = refs.canvas.getBoundingClientRect();
  const width = Math.max(360, Math.floor(rect.width * window.devicePixelRatio));
  const height = Math.max(360, Math.floor(rect.height * window.devicePixelRatio));
  if (refs.canvas.width !== width || refs.canvas.height !== height) {
    refs.canvas.width = width;
    refs.canvas.height = height;
    state.dirty = true;
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
    await handleDescriptor(message, transport);
    return;
  }

  const kind = normalizeKind(message.kind);
  if (kind === 'handshakeAck') {
    state.transportMode = message.payload?.transportMode ?? state.transportMode;
    updateChrome();
  }
}

async function handleDescriptor(message, transport) {
  state.pendingDescriptor = normalizeDescriptor(message.payload?.sharedBuffer ?? message.sharedBuffer);
  if (state.processingDescriptor) {
    state.skipped += 1;
    state.status = 'latest queued';
    updateChrome();
    return;
  }

  await drainDescriptors(transport);
}

async function drainDescriptors(transport) {
  state.processingDescriptor = true;
  try {
    while (state.pendingDescriptor) {
      const descriptor = state.pendingDescriptor;
      state.pendingDescriptor = null;
      await processDescriptor(descriptor, transport);
    }
  } finally {
    state.processingDescriptor = false;
  }
}

async function processDescriptor(descriptor, transport) {
  try {
    if (mode === 'true-v8') {
      await processTrueV8Descriptor(descriptor, transport);
    } else if (mode === 'browser-arraybuffer') {
      await processBrowserArrayBufferDescriptor(descriptor, transport);
    } else {
      await processReadCopyDescriptor(descriptor, transport);
    }
  } catch (error) {
    handleDescriptorError(error);
  }

  state.status = state.skipped > 0 ? `subscribed, skipped ${state.skipped}` : 'subscribed';
  updateChrome();
}

async function processReadCopyDescriptor(descriptor, transport) {
  const readStarted = performance.now();
  const rawBytes = await transport.readSharedBuffer(descriptor);
  const bytes = normalizeByteView(rawBytes);
  state.readMs = performance.now() - readStarted;
  state.copyMs = null;
  decodeIntoState(bytes, 'read-copy');
}

async function processBrowserArrayBufferDescriptor(descriptor, transport) {
  const readStarted = performance.now();
  const rawBytes = await transport.readSharedBuffer(descriptor);
  const sourceBytes = normalizeByteView(rawBytes);
  state.readMs = performance.now() - readStarted;

  const copyStarted = performance.now();
  const ownedBuffer = new ArrayBuffer(sourceBytes.byteLength);
  new Uint8Array(ownedBuffer).set(sourceBytes);
  state.copyMs = performance.now() - copyStarted;
  decodeIntoState(new Uint8Array(ownedBuffer), 'browser-owned');
}

async function processTrueV8Descriptor(descriptor, transport) {
  if (typeof transport.acquireV8Buffer !== 'function') {
    throw new Error('missing acquireV8Buffer');
  }

  const acquireStarted = performance.now();
  const value = await transport.acquireV8Buffer(descriptor);
  state.readMs = performance.now() - acquireStarted;
  state.copyMs = null;
  if (!(value instanceof ArrayBuffer)) {
    throw new TypeError(`not ArrayBuffer: ${describeReadCopy(value)}`);
  }

  decodeIntoState(new Uint8Array(value), 'v8 backing store');
}

function decodeIntoState(bytes, pathStatus) {
  const parseStarted = performance.now();
  const decoded = decodeWdmm(bytes);
  state.parseMs = performance.now() - parseStarted;
  state.markers = decoded;
  state.payloadBytes = bytes.byteLength;
  state.markerCount = decoded.count;
  state.pathStatus = pathStatus;
  state.dirty = true;
}

function handleDescriptorError(error) {
  const message = normalizeError(error);
  if (message.includes('descriptor range is no longer active')) {
    state.skipped += 1;
    if (!state.markers) {
      state.pathStatus = 'stale skipped';
    }
    return;
  }

  state.pathStatus = summarizePathError(message);
  if (!state.markers) {
    state.status = state.pathStatus;
  }
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
  draw();
  requestAnimationFrame(renderLoop);
}

function draw() {
  if (!state.dirty || !state.markers) {
    return;
  }

  const started = performance.now();
  const canvas = refs.canvas;
  const ctx = refs.ctx;
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  drawGrid(ctx, width, height);
  drawMarkers(ctx, state.markers, width, height);
  state.drawMs = performance.now() - started;
  state.frames += 1;
  state.dirty = false;
  updateChrome();
}

function drawGrid(ctx, width, height) {
  ctx.save();
  ctx.globalAlpha = mode === 'true-v8' ? 0.78 : 0.68;
  ctx.strokeStyle = mode === 'true-v8' ? 'rgba(126,231,166,0.22)' : 'rgba(110,139,145,0.18)';
  ctx.lineWidth = Math.max(1, window.devicePixelRatio);
  const step = Math.max(30, Math.floor(Math.min(width, height) / 14));
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

function drawMarkers(ctx, markers, width, height) {
  const scale = Math.min(width, height) / (WORLD_HALF_EXTENT_CM * 2);
  const centerX = width * 0.5;
  const centerY = height * 0.5;
  ctx.save();
  ctx.globalCompositeOperation = 'source-over';
  for (let i = 0; i < markers.count; i += 1) {
    const px = centerX + (markers.x[i] * scale);
    const py = centerY - (markers.y[i] * scale);
    if (px < -4 || py < -4 || px > width + 4 || py > height + 4) {
      continue;
    }

    const radius = Math.min(1.35, Math.max(0.45, markers.size[i] * window.devicePixelRatio * 0.28));
    ctx.fillStyle = toRgba(markers.color[i], 0.72);
    ctx.fillRect(px - (radius * 0.5), py - (radius * 0.5), radius, radius);
  }
  ctx.restore();
}

function updateChrome() {
  refs.connection.textContent = state.status;
  refs.markerCount.textContent = `${formatNumber(state.markerCount)} markers`;
  refs.transportMode.textContent = state.transportMode;
  refs.payloadBytes.textContent = formatBytes(state.payloadBytes);
  refs.readMs.textContent = `${state.readMs.toFixed(2)} ms`;
  refs.copyMs.textContent = state.copyMs == null ? '-' : `${state.copyMs.toFixed(2)} ms`;
  refs.parseMs.textContent = `${state.parseMs.toFixed(2)} ms`;
  refs.drawMs.textContent = `${state.drawMs.toFixed(2)} ms`;
  refs.frames.textContent = `${state.frames}`;
  refs.skipped.textContent = `${state.skipped}`;
  refs.pathStatus.textContent = state.pathStatus;
  refs.badge.className = `badge ${state.pathStatus === 'failed' ? 'badge-error' : modeInfo.badgeClass}`;
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

function describeReadCopy(value) {
  if (value instanceof Uint8Array || Array.isArray(value) || (value && typeof value.length === 'number')) {
    return 'copy-read';
  }

  if (value instanceof ArrayBuffer) {
    return 'ArrayBuffer';
  }

  return 'unsupported';
}

function summarizePathError(message) {
  if (message.includes('missing acquireV8Buffer')) {
    return 'missing bridge';
  }

  if (message.includes('not ArrayBuffer')) {
    return 'not ArrayBuffer';
  }

  if (message.includes('backing store')) {
    return 'backing store failed';
  }

  return 'failed';
}

function toRgba(colorKey, alphaScale) {
  const a = ((colorKey >>> 24) & 255) / 255;
  const r = (colorKey >>> 16) & 255;
  const g = (colorKey >>> 8) & 255;
  const b = colorKey & 255;
  return `rgba(${r}, ${g}, ${b}, ${(a * alphaScale).toFixed(3)})`;
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

function normalizeError(error) {
  return error instanceof Error ? error.message : String(error);
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
