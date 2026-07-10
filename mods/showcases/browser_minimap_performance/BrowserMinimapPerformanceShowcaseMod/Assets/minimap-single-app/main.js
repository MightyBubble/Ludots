const CONTROL_CHANNEL = 'ludots.dataplane.control';
const SHARED_BUFFER_CHANNEL = 'ludots.dataplane.sharedBuffer';
const TOPIC = 'webui.minimapRawMarkers';
const WRMM_MAGIC = 0x4d4d5257;
const RAW_SCHEMA_ID = 1002;
const HEADER_BYTES = 20;
const BYTES_PER_MARKER = 36;

const cssColorCache = new Map();

const state = {
  markers: null,
  payloadBytes: 0,
  readMs: 0,
  parseMs: 0,
  drawMs: 0,
  frames: 0,
  skipped: 0,
  status: 'booting',
  pathStatus: 'waiting',
  transportMode: 'pending',
  markerCount: 0,
  droppedCurrent: 0,
  droppedTotal: 0,
  boundsLabel: '-',
  sessionId: `minimap-raw-${Date.now().toString(16)}`,
  requestId: 0,
  pendingDescriptor: null,
  processingDescriptor: false,
  dirty: true
};

const refs = {
  connection: document.getElementById('connection-status'),
  markerCount: document.getElementById('marker-count'),
  transportMode: document.getElementById('transport-mode'),
  payloadBytes: document.getElementById('payload-bytes'),
  readMs: document.getElementById('read-ms'),
  parseMs: document.getElementById('parse-ms'),
  drawMs: document.getElementById('draw-ms'),
  frames: document.getElementById('frames'),
  skipped: document.getElementById('skipped'),
  dropped: document.getElementById('dropped'),
  bounds: document.getElementById('bounds'),
  pathStatus: document.getElementById('path-status'),
  canvas: document.getElementById('minimap-canvas')
};
refs.ctx = refs.canvas.getContext('2d', { alpha: true });

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
    metadata: { showcase: 'browser_minimap_performance' },
    capabilities: ['handshake', 'subscribe', 'shared-buffer-descriptor', 'minimap-markers.wrmm.raw.v1']
  });
  await sendControl(transport, 'subscribe', TOPIC, {
    subscriptionId: 'raw-performance-markers',
    snapshot: true
  });

  state.status = 'subscribed';
  updateChrome();
  requestAnimationFrame(renderLoop);
  window.__LUDOTS_MINIMAP_RAW_PERFORMANCE_READY__ = true;
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

  if (message.kind === 'sharedBuffer' && message.topic === TOPIC) {
    await handleDescriptor(message, transport);
    return;
  }

  if (normalizeKind(message.kind) === 'handshakeAck') {
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
    const readStarted = performance.now();
    const rawBytes = await transport.readSharedBuffer(descriptor);
    const bytes = normalizeByteView(rawBytes);
    state.readMs = performance.now() - readStarted;

    const parseStarted = performance.now();
    const decoded = decodeWrmm(bytes);
    state.parseMs = performance.now() - parseStarted;
    state.markers = decoded;
    state.payloadBytes = bytes.byteLength;
    state.markerCount = decoded.count;
    state.droppedCurrent = decoded.droppedCurrent;
    state.droppedTotal = decoded.droppedTotal;
    state.boundsLabel = formatBounds(decoded.bounds);
    state.pathStatus = 'raw';
    state.dirty = true;
  } catch (error) {
    handleDescriptorError(error);
  }

  state.status = state.skipped > 0 ? `subscribed, skipped ${state.skipped}` : 'subscribed';
  updateChrome();
}

function decodeWrmm(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (view.byteLength < HEADER_BYTES || view.getUint32(0, true) !== WRMM_MAGIC) {
    throw new Error('Invalid WRMM raw minimap marker packet.');
  }

  const schemaId = view.getInt32(4, true);
  if (schemaId !== RAW_SCHEMA_ID) {
    throw new Error(`Unexpected raw minimap schema id ${schemaId}.`);
  }

  const count = view.getInt32(8, true);
  const expectedBytes = HEADER_BYTES + (count * BYTES_PER_MARKER);
  if (count < 0 || view.byteLength < expectedBytes) {
    throw new Error('Truncated WRMM raw minimap marker packet.');
  }

  const x = new Float32Array(count);
  const y = new Float32Array(count);
  const color = new Uint32Array(count);
  const size = new Float32Array(count);
  let minX = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  let offset = HEADER_BYTES;
  for (let i = 0; i < count; i += 1) {
    x[i] = view.getFloat32(offset + 4, true);
    y[i] = view.getFloat32(offset + 8, true);
    color[i] = packRgba(
      view.getFloat32(offset + 12, true),
      view.getFloat32(offset + 16, true),
      view.getFloat32(offset + 20, true),
      view.getFloat32(offset + 24, true));
    size[i] = view.getFloat32(offset + 28, true);
    minX = Math.min(minX, x[i]);
    maxX = Math.max(maxX, x[i]);
    minY = Math.min(minY, y[i]);
    maxY = Math.max(maxY, y[i]);
    offset += BYTES_PER_MARKER;
  }

  return {
    count,
    x,
    y,
    color,
    size,
    bounds: normalizeBounds(minX, maxX, minY, maxY),
    droppedCurrent: view.getInt32(12, true),
    droppedTotal: view.getInt32(16, true)
  };
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
  ctx.globalAlpha = 0.58;
  ctx.strokeStyle = 'rgba(172, 159, 119, 0.14)';
  ctx.lineWidth = Math.max(1, window.devicePixelRatio);
  const step = Math.max(32, Math.floor(Math.min(width, height) / 12));
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
  const fit = fitBounds(markers.bounds, width, height);
  ctx.save();
  ctx.globalCompositeOperation = 'source-over';
  ctx.fillStyle = 'rgba(255, 196, 70, 0.86)';
  const markerSize = Math.max(1, Math.min(1.4, window.devicePixelRatio || 1));
  for (let i = 0; i < markers.count; i += 1) {
    const px = fit.left + ((markers.x[i] - markers.bounds.minX) * fit.scale);
    const py = fit.top + fit.height - ((markers.y[i] - markers.bounds.minY) * fit.scale);
    if (px < -4 || py < -4 || px > width + 4 || py > height + 4) {
      continue;
    }

    ctx.fillRect(px - (markerSize * 0.5), py - (markerSize * 0.5), markerSize, markerSize);
  }
  ctx.restore();
}

function fitBounds(bounds, width, height) {
  const spanX = Math.max(1, bounds.maxX - bounds.minX);
  const spanY = Math.max(1, bounds.maxY - bounds.minY);
  const scale = Math.min((width * 0.92) / spanX, (height * 0.92) / spanY);
  const fitWidth = spanX * scale;
  const fitHeight = spanY * scale;
  return {
    scale,
    left: (width - fitWidth) * 0.5,
    top: (height - fitHeight) * 0.5,
    width: fitWidth,
    height: fitHeight
  };
}

function normalizeBounds(minX, maxX, minY, maxY) {
  if (!Number.isFinite(minX) || !Number.isFinite(maxX) || !Number.isFinite(minY) || !Number.isFinite(maxY)) {
    return { minX: -1, maxX: 1, minY: -1, maxY: 1 };
  }

  if (Math.abs(maxX - minX) < 1) {
    minX -= 1;
    maxX += 1;
  }

  if (Math.abs(maxY - minY) < 1) {
    minY -= 1;
    maxY += 1;
  }

  return { minX, maxX, minY, maxY };
}

function updateChrome() {
  refs.connection.textContent = state.status;
  refs.markerCount.textContent = `${formatNumber(state.markerCount)} markers`;
  refs.transportMode.textContent = state.transportMode;
  refs.payloadBytes.textContent = formatBytes(state.payloadBytes);
  refs.readMs.textContent = `${state.readMs.toFixed(2)} ms`;
  refs.parseMs.textContent = `${state.parseMs.toFixed(2)} ms`;
  refs.drawMs.textContent = `${state.drawMs.toFixed(2)} ms`;
  refs.frames.textContent = `${state.frames}`;
  refs.skipped.textContent = `${state.skipped}`;
  refs.dropped.textContent = `${state.droppedCurrent}/${state.droppedTotal}`;
  refs.bounds.textContent = state.boundsLabel;
  refs.pathStatus.textContent = state.pathStatus;
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

  state.pathStatus = 'failed';
  if (state.markers) {
    state.skipped += 1;
    state.pathStatus = 'raw';
    return;
  }

  if (!state.markers) {
    state.status = message;
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

function packRgba(r, g, b, a) {
  const rb = Math.round(Math.max(0, Math.min(1, r)) * 255) & 255;
  const gb = Math.round(Math.max(0, Math.min(1, g)) * 255) & 255;
  const bb = Math.round(Math.max(0, Math.min(1, b)) * 255) & 255;
  const ab = Math.round(Math.max(0, Math.min(1, a)) * 255) & 255;
  return ((ab << 24) | (rb << 16) | (gb << 8) | bb) >>> 0;
}

function toRgba(colorKey, alphaScale) {
  const cacheKey = `${colorKey}:${alphaScale}`;
  const cached = cssColorCache.get(cacheKey);
  if (cached) {
    return cached;
  }

  const a = ((colorKey >>> 24) & 255) / 255;
  const r = (colorKey >>> 16) & 255;
  const g = (colorKey >>> 8) & 255;
  const b = colorKey & 255;
  const css = `rgba(${r}, ${g}, ${b}, ${(a * alphaScale).toFixed(3)})`;
  cssColorCache.set(cacheKey, css);
  return css;
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

function formatBounds(bounds) {
  const widthM = (bounds.maxX - bounds.minX) / 100;
  const heightM = (bounds.maxY - bounds.minY) / 100;
  return `${formatMetric(widthM)} x ${formatMetric(heightM)}`;
}

function formatMetric(meters) {
  if (Math.abs(meters) >= 1000) {
    return `${(meters / 1000).toFixed(1)} km`;
  }

  return `${meters.toFixed(0)} m`;
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
