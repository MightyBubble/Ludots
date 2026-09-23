import { FrameDecoder, type DecodedFrame } from '../../Web/src/core/FrameDecoder';
import { MinimapMarkerStream } from '../../Web/src/core/MinimapMarkerStream';
import { InputCapture } from '../../Web/src/input/InputCapture';
import { HudRenderer } from '../../Web/src/rendering/HudRenderer';
import { MinimapMarkerRenderer } from '../../Web/src/rendering/MinimapMarkerRenderer';
import { UiSceneOverlay } from '../../Web/src/rendering/UiSceneOverlay';
import { WebGpuFrameStream } from './core/WebGpuFrameStream';
import { decodeTerrainMessage } from './core/TerrainSnapshot';
import { WebGpuWorldRenderer } from './rendering/WebGpuWorldRenderer';

declare global {
  interface Window {
    __ludotsWebGpuQa?: {
      getState: () => {
        renderer: 'webgpu';
        connected: boolean;
        frameNumber: number;
        simTick: number;
        entities: number;
        identityResets: number;
        camera: DecodedFrame['camera'] | null;
        terrain: ReturnType<WebGpuWorldRenderer['getTerrainStats']>;
        hud: ReturnType<WebGpuWorldRenderer['getHudStats']>;
        minimapMarkers: number;
      };
    };
  }
}

const worldCanvas = document.getElementById('webgpu-canvas') as HTMLCanvasElement;
const hudCanvas = document.getElementById('hud-canvas') as HTMLCanvasElement;
const uiCanvas = document.getElementById('ui-canvas') as HTMLCanvasElement;
const statsEl = document.getElementById('stats')!;
const fatalEl = document.getElementById('fatal')!;
const fatalMessageEl = document.getElementById('fatal-message')!;

const decoder = new FrameDecoder({ decodePrimitives: false, decodeScreenHud: false });
const minimapMarkerStream = new MinimapMarkerStream();
const frameStream = new WebGpuFrameStream();
const inputCapture = new InputCapture(worldCanvas);
const hudRenderer = new HudRenderer(hudCanvas);
const minimapMarkerRenderer = new MinimapMarkerRenderer(minimapMarkerStream);
const uiOverlay = new UiSceneOverlay(uiCanvas);

let renderer: WebGpuWorldRenderer;
let socket: WebSocket | null = null;
let lastFrame: DecodedFrame | null = null;
let frameCount = 0;
let receivedFrameCount = 0;
let bytesReceived = 0;
let decodeTimeMs = 0;
let lastStatTime = performance.now();
let lastAnimationTime = lastStatTime;
let displayFps = 0;
let receiveFps = 0;
let displayKbps = 0;
let averageDecodeMs = 0;
let connected = false;
let overlayDirty = true;

window.__ludotsWebGpuQa = {
  getState: () => ({
    renderer: 'webgpu',
    connected,
    frameNumber: frameStream.state.frameNumber,
    simTick: frameStream.state.simTick,
    entities: frameStream.state.primitiveCount,
    identityResets: frameStream.state.identityResets,
    camera: lastFrame?.camera ?? null,
    terrain: renderer?.getTerrainStats() ?? {
      revision: null,
      chunks: 0,
      visibleChunks: 0,
      vertices: 0,
      visibleVertices: 0,
    },
    hud: renderer?.getHudStats() ?? { bars: 0, glyphs: 0, missingGlyphs: 0 },
    minimapMarkers: minimapMarkerStream.count,
  }),
};

function showFatal(message: string): void {
  fatalMessageEl.textContent = message;
  fatalEl.classList.add('visible');
  console.error(`[WebGPU] ${message}`);
}

function connectWebSocket(): void {
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const wsUrl = `${protocol}//${location.host}/ws`;
  const ws = new WebSocket(wsUrl);
  ws.binaryType = 'arraybuffer';

  ws.addEventListener('open', () => {
    socket = ws;
    connected = true;
  });

  ws.addEventListener('close', (event) => {
    if (socket === ws) {
      socket = null;
    }

    connected = false;
    lastFrame = null;
    renderer.clearScreenHud();
    renderer.clearTerrain();
    minimapMarkerStream.clear();
    overlayDirty = true;
    uiOverlay.clear();
    if (event.code === 1008) {
      showFatal(event.reason || 'This game is already open in another browser tab. Close that tab, then reload this page.');
      return;
    }
    setTimeout(connectWebSocket, 2000);
  });

  ws.addEventListener('error', () => ws.close());

  ws.addEventListener('message', (ev) => {
    if (!(ev.data instanceof ArrayBuffer)) {
      return;
    }

    const decodeStart = performance.now();
    try {
      bytesReceived += ev.data.byteLength;
      const terrainMessage = decodeTerrainMessage(ev.data);
      if (terrainMessage) {
        if (terrainMessage.kind === 'snapshot') {
          renderer.updateTerrainSnapshot(terrainMessage.snapshot);
        } else {
          renderer.clearTerrain();
        }
        return;
      }

      const frame = decoder.decode(ev.data);
      if (!frame) {
        return;
      }

      minimapMarkerStream.pushFrame(ev.data);
      frameStream.pushFrame(ev.data, renderer);
      lastFrame = frame;
      overlayDirty = true;
      receivedFrameCount++;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      showFatal(`Frame decode failed: ${message}`);
      ws.close();
    } finally {
      decodeTimeMs += performance.now() - decodeStart;
    }
  });
}

function flushOutgoingInput(): void {
  const activeSocket = socket;
  if (!activeSocket || activeSocket.readyState !== WebSocket.OPEN) {
    return;
  }

  for (const message of inputCapture.encoder.drainPointerMessages()) {
    activeSocket.send(message);
  }
  activeSocket.send(inputCapture.encoder.encodeState(window.innerWidth, window.innerHeight));
}

function worldToScreen2D(worldX: number, worldY: number): [number, number] | null {
  if (!lastFrame) {
    return null;
  }

  return renderer.worldToScreen(worldX, 0.35, worldY, window.innerWidth, window.innerHeight);
}

function animate(): void {
  requestAnimationFrame(animate);
  const animationNow = performance.now();
  const deltaSeconds = Math.min(0.1, Math.max(0.001, (animationNow - lastAnimationTime) / 1000));
  lastAnimationTime = animationNow;
  flushOutgoingInput();

  frameStream.tick(deltaSeconds);
  renderer.setPresentationInterpolationFactor(frameStream.interpolationFactor);

  if (lastFrame) {
    renderer.updateCamera(lastFrame.camera, window.innerWidth / Math.max(1, window.innerHeight));
    const instances = frameStream.getSampledInstances();
    renderer.updateInstancesPacked(instances.data, instances.count);

    const hasMovingDebugOverlay =
      lastFrame.debugLines.length > 0 ||
      lastFrame.debugCircles.length > 0 ||
      lastFrame.debugBoxes.length > 0;
    if (overlayDirty || hasMovingDebugOverlay) {
      hudRenderer.clear();
      hudRenderer.drawDebugOverlay(
        lastFrame.debugLines,
        lastFrame.debugCircles,
        lastFrame.debugBoxes,
        worldToScreen2D,
      );
      hudRenderer.drawScreenOverlays(lastFrame.screenOverlays, minimapMarkerRenderer);
      overlayDirty = false;
    }
    uiOverlay.update(lastFrame.uiScene);
  }

  renderer.render();
  frameCount++;

  const now = animationNow;
  if (now - lastStatTime > 1000) {
    const statSeconds = (now - lastStatTime) / 1000;
    displayFps = frameCount / statSeconds;
    receiveFps = receivedFrameCount / statSeconds;
    displayKbps = bytesReceived / 1024 / statSeconds;
    averageDecodeMs = receivedFrameCount > 0 ? decodeTimeMs / receivedFrameCount : 0;
    frameCount = 0;
    receivedFrameCount = 0;
    bytesReceived = 0;
    decodeTimeMs = 0;
    lastStatTime = now;

    const entities = frameStream.state.primitiveCount;
    const tick = frameStream.state.simTick;
    const camera = lastFrame?.camera;
    const hud = renderer.getHudStats();
    const terrain = renderer.getTerrainStats();
    statsEl.dataset.renderer = 'webgpu';
    statsEl.dataset.connected = connected ? 'true' : 'false';
    statsEl.dataset.entities = `${entities}`;
    statsEl.dataset.identityResets = `${frameStream.state.identityResets}`;
    statsEl.dataset.tick = `${tick}`;
    statsEl.dataset.camera = camera
      ? `${camera.posX.toFixed(4)},${camera.posY.toFixed(4)},${camera.posZ.toFixed(4)},${camera.tgtX.toFixed(4)},${camera.tgtY.toFixed(4)},${camera.tgtZ.toFixed(4)}`
      : '';
    statsEl.dataset.receiveFps = receiveFps.toFixed(1);
    statsEl.dataset.decodeMs = averageDecodeMs.toFixed(2);
    statsEl.dataset.hudBars = `${hud.bars}`;
    statsEl.dataset.hudGlyphs = `${hud.glyphs}`;
    statsEl.dataset.missingGlyphs = `${hud.missingGlyphs}`;
    statsEl.dataset.minimapMarkers = `${minimapMarkerStream.count}`;
    statsEl.dataset.terrainChunks = `${terrain.chunks}`;
    statsEl.dataset.terrainVisibleChunks = `${terrain.visibleChunks}`;
    statsEl.dataset.terrainVertices = `${terrain.vertices}`;
    statsEl.textContent = `WebGPU | ${connected ? 'online' : 'waiting'} | FPS: ${displayFps.toFixed(1)} | RX: ${(displayKbps / 1024).toFixed(1)} MB/s @ ${receiveFps.toFixed(1)} Hz | Decode: ${averageDecodeMs.toFixed(2)} ms | Entities: ${entities} | Terrain: ${terrain.visibleChunks}/${terrain.chunks} | HUD: ${hud.bars}/${hud.glyphs} | Map: ${minimapMarkerStream.count} | Tick: ${tick}`;
  }
}

function resize(): void {
  const width = window.innerWidth;
  const height = window.innerHeight;
  renderer.resize(width, height);
  hudRenderer.resize(width, height);
  uiOverlay.resize(width, height);
  overlayDirty = true;
}

async function boot(): Promise<void> {
  try {
    renderer = await WebGpuWorldRenderer.create(worldCanvas);
    resize();
    window.addEventListener('resize', resize);
    connectWebSocket();
    animate();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    showFatal(message);
  }
}

void boot();
