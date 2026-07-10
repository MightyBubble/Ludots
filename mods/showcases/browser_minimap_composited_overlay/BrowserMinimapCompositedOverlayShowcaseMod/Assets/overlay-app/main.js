const refs = {
  widget: document.getElementById('minimap-widget'),
  viewport: document.getElementById('minimap-viewport')
};

const SCHEMA_VERSION = 1;
const DEFAULT_PANEL_ID = 'panel.minimap.web-shell';
const FOCUS_MINIMAP_COMMAND = 'focusMinimap';
const NATIVE_CLIP_KIND = 'circle';
const routeParams = new URLSearchParams(window.location.search);
const panelId = routeParams.get('panelId') || DEFAULT_PANEL_ID;
const dataPlaneTopic = routeParams.get('topic');
const dataPlaneSessionId = `minimap-shell-${Date.now().toString(16)}`;
let sequence = 0;
let requestSequence = 0;
let commandSequence = 0;
let dragging = false;
let dragMoved = false;
let lastPointerX = 0;
let lastPointerY = 0;
let rectPostQueued = false;
let rectPostHandle = 0;
let dataPlaneReady = false;
let dataPlaneError = '';

function postHostMessage(payload) {
  if (window.ludotsDataplane && typeof window.ludotsDataplane.postMessage === 'function') {
    window.ludotsDataplane.postMessage(payload);
    return true;
  }

  if (window.ludotsBrowser && typeof window.ludotsBrowser.postMessage === 'function') {
    window.ludotsBrowser.postMessage(payload);
    return true;
  }

  return false;
}

function postDataPlaneEnvelope(kind, topic, payload) {
  if (!dataPlaneTopic) {
    throw new Error('Panel topic is required for minimap DataPlane messages.');
  }

  const envelope = {
    schemaVersion: SCHEMA_VERSION,
    sessionId: dataPlaneSessionId,
    requestId: ++requestSequence,
    kind,
    topic,
    payload
  };

  if (!postHostMessage(envelope)) {
    throw new Error('Ludots DataPlane host bridge is not available.');
  }
}

function updateDataPlaneStatus(phase, error = '') {
  dataPlaneError = error;
  window.__LUDOTS_MINIMAP_DATAPLANE__ = {
    phase,
    panelId,
    topic: dataPlaneTopic,
    command: FOCUS_MINIMAP_COMMAND,
    ready: dataPlaneReady,
    error: dataPlaneError
  };
}

function connectDataPlane() {
  if (!dataPlaneTopic) {
    updateDataPlaneStatus('error', 'Panel topic query parameter is missing.');
    return;
  }

  try {
    postDataPlaneEnvelope('handshake', 'system', {
      app: 'browser-minimap-composited-overlay',
      panelId,
      requiredCapabilities: ['message', 'control', 'reliable-ordered']
    });
    postDataPlaneEnvelope('subscribe', dataPlaneTopic, {
      panelId,
      snapshot: true
    });
    dataPlaneReady = true;
    updateDataPlaneStatus('connected');
  } catch (error) {
    updateDataPlaneStatus('waiting-for-host', error instanceof Error ? error.message : String(error));
  }
}

function resolvePointerX(event) {
  return Number.isFinite(event.clientX) ? event.clientX : 0;
}

function resolvePointerY(event) {
  return Number.isFinite(event.clientY) ? event.clientY : 0;
}

function resolvePointerDelta(event, pointerX, pointerY) {
  return {
    x: pointerX - lastPointerX,
    y: pointerY - lastPointerY
  };
}

function browserCoordinateSpacePayload() {
  const root = document.documentElement;
  return {
    kind: 'browser-css-px',
    width: root.clientWidth || window.innerWidth || refs.widget.getBoundingClientRect().width,
    height: root.clientHeight || window.innerHeight || refs.widget.getBoundingClientRect().height,
    devicePixelRatio: window.devicePixelRatio || 1
  };
}

function viewportRectPayload(dragDelta = null) {
  const rect = refs.viewport.getBoundingClientRect();
  const payload = {
    type: 'ludots.minimapOverlay.rect',
    sequence: ++sequence,
    coordinateSpace: browserCoordinateSpacePayload(),
    rect: {
      x: rect.left,
      y: rect.top,
      width: rect.width,
      height: rect.height
    },
    clip: {
      kind: NATIVE_CLIP_KIND
    }
  };

  if (dragDelta) {
    payload.dragDelta = dragDelta;
  }

  return payload;
}

function postViewportRect() {
  rectPostQueued = false;
  rectPostHandle = 0;
  postHostMessage(viewportRectPayload());
}

function queueViewportRect() {
  if (rectPostQueued) {
    return;
  }

  rectPostQueued = true;
  rectPostHandle = requestAnimationFrame(postViewportRect);
}

function postViewportRectImmediately() {
  if (rectPostHandle !== 0) {
    cancelAnimationFrame(rectPostHandle);
  }

  rectPostQueued = false;
  rectPostHandle = 0;
  postViewportRect();
}

function postDragDelta(deltaX, deltaY) {
  if (deltaX === 0 && deltaY === 0) {
    return;
  }

  dragMoved = true;
  if (rectPostHandle !== 0) {
    cancelAnimationFrame(rectPostHandle);
  }

  rectPostQueued = false;
  rectPostHandle = 0;
  postHostMessage(viewportRectPayload({
    x: deltaX,
    y: deltaY
  }));
}

function normalizedViewportPoint(event) {
  const rect = refs.viewport.getBoundingClientRect();
  const pointerX = resolvePointerX(event);
  const pointerY = resolvePointerY(event);
  return {
    x: Math.min(1, Math.max(0, (pointerX - rect.left) / Math.max(1, rect.width))),
    y: Math.min(1, Math.max(0, (pointerY - rect.top) / Math.max(1, rect.height)))
  };
}

function postFocusMinimapCommand(event) {
  if (!dataPlaneReady) {
    updateDataPlaneStatus('error', dataPlaneError || 'DataPlane is not connected.');
    return;
  }

  const point = normalizedViewportPoint(event);
  try {
    postDataPlaneEnvelope('command', dataPlaneTopic, {
      name: FOCUS_MINIMAP_COMMAND,
      clientSeq: ++commandSequence,
      entityRefs: [],
      payload: {
        panelId,
        normalizedX: point.x,
        normalizedY: point.y
      }
    });
    updateDataPlaneStatus('command-sent');
  } catch (error) {
    updateDataPlaneStatus('error', error instanceof Error ? error.message : String(error));
  }
}

refs.widget.addEventListener('pointerdown', (event) => {
  if (event.button !== 0) {
    return;
  }

  dragging = true;
  dragMoved = false;
  lastPointerX = resolvePointerX(event);
  lastPointerY = resolvePointerY(event);
  refs.widget.classList.add('is-dragging');
  refs.widget.setPointerCapture(event.pointerId);
  event.preventDefault();
});

refs.widget.addEventListener('pointermove', (event) => {
  if (!dragging) {
    return;
  }

  const pointerX = resolvePointerX(event);
  const pointerY = resolvePointerY(event);
  const delta = resolvePointerDelta(event, pointerX, pointerY);
  lastPointerX = pointerX;
  lastPointerY = pointerY;
  postDragDelta(delta.x, delta.y);
  event.preventDefault();
});

function stopDrag(event) {
  if (!dragging) {
    return;
  }

  dragging = false;
  refs.widget.classList.remove('is-dragging');
  if (refs.widget.hasPointerCapture(event.pointerId)) {
    refs.widget.releasePointerCapture(event.pointerId);
  }
  postViewportRectImmediately();
  if (!dragMoved) {
    postFocusMinimapCommand(event);
  }
}

refs.widget.addEventListener('pointerup', stopDrag);
refs.widget.addEventListener('pointercancel', stopDrag);
window.addEventListener('resize', queueViewportRect);

window.__LUDOTS_MINIMAP_COMPOSITED_OVERLAY_READY__ = true;
updateDataPlaneStatus('boot');
connectDataPlane();
queueViewportRect();
