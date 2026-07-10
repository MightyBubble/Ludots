const refs = {
  widget: document.getElementById('minimap-widget'),
  viewport: document.getElementById('minimap-viewport')
};

const NATIVE_CLIP_KIND = 'circle';
let sequence = 0;
let dragging = false;
let lastPointerX = 0;
let lastPointerY = 0;
let rectPostQueued = false;
let rectPostHandle = 0;

function postHostMessage(payload) {
  const text = JSON.stringify(payload);
  if (window.CefSharp && typeof window.CefSharp.PostMessage === 'function') {
    window.CefSharp.PostMessage(text);
    return true;
  }

  if (window.ludotsDataplane && typeof window.ludotsDataplane.postMessage === 'function') {
    window.ludotsDataplane.postMessage(text);
    return true;
  }

  return false;
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

refs.widget.addEventListener('pointerdown', (event) => {
  if (event.button !== 0) {
    return;
  }

  dragging = true;
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
}

refs.widget.addEventListener('pointerup', stopDrag);
refs.widget.addEventListener('pointercancel', stopDrag);
window.addEventListener('resize', queueViewportRect);

window.__LUDOTS_MINIMAP_COMPOSITED_OVERLAY_READY__ = true;
queueViewportRect();
