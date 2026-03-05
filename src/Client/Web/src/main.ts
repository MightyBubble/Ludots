import * as THREE from 'three';
import { FrameDecoder, type DecodedFrame } from './core/FrameDecoder';
import { InputCapture } from './input/InputCapture';
import { EntityManager } from './rendering/EntityManager';
import { HudRenderer } from './rendering/HudRenderer';
import { GroundOverlayRenderer } from './rendering/GroundOverlayRenderer';
import { PositionInterpolator } from './rendering/PositionInterpolator';
import { UiOverlay } from './rendering/UiOverlay';

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x111111);

const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 5000);
camera.position.set(10, 10, 10);
camera.lookAt(0, 0, 0);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
document.body.prepend(renderer.domElement);

const ambientLight = new THREE.AmbientLight(0x888888);
scene.add(ambientLight);
const dirLight = new THREE.DirectionalLight(0xffffff, 0.8);
dirLight.position.set(50, 100, 50);
scene.add(dirLight);

const gridHelper = new THREE.GridHelper(200, 200, 0x555555, 0x333333);
scene.add(gridHelper);

const hudCanvas = document.getElementById('hud-canvas') as HTMLCanvasElement;
const statsEl = document.getElementById('stats')!;

const decoder = new FrameDecoder();
const inputCapture = new InputCapture(renderer.domElement);
const entityManager = new EntityManager(scene);
const hudRenderer = new HudRenderer(hudCanvas);
const groundOverlayRenderer = new GroundOverlayRenderer(scene);
const interpolator = new PositionInterpolator();
const uiOverlay = new UiOverlay();

let _lastFrame: DecodedFrame | null = null;
let _frameCount = 0;
let _bytesReceived = 0;
let _lastStatTime = performance.now();
let _displayFps = 0;
let _displayKbps = 0;
let _meshMapApplied = false;

function connectWebSocket(): void {
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const wsUrl = `${protocol}//${location.host}/ws`;
  const ws = new WebSocket(wsUrl);
  ws.binaryType = 'arraybuffer';

  let inputInterval: ReturnType<typeof setInterval> | null = null;

  ws.addEventListener('open', () => {
    console.log('[WS] Connected');
    inputInterval = setInterval(() => {
      if (ws.readyState !== WebSocket.OPEN) {
        if (inputInterval) { clearInterval(inputInterval); inputInterval = null; }
        return;
      }
      ws.send(inputCapture.encoder.encode());
    }, 50);
  });
  ws.addEventListener('close', () => {
    console.log('[WS] Disconnected, reconnecting in 2s...');
    if (inputInterval) { clearInterval(inputInterval); inputInterval = null; }
    _meshMapApplied = false;
    setTimeout(connectWebSocket, 2000);
  });
  ws.addEventListener('error', () => ws.close());

  ws.addEventListener('message', (ev) => {
    if (!(ev.data instanceof ArrayBuffer)) return;
    _bytesReceived += ev.data.byteLength;

    const frame = decoder.decode(ev.data);

    if (!_meshMapApplied && decoder.meshMap) {
      entityManager.applyMeshMap(decoder.meshMap);
      _meshMapApplied = true;
    }

    if (!frame) return;
    _lastFrame = frame;

    interpolator.pushFrame(frame.primitives, frame.timestampMs);
  });
}

function worldToScreen2D(worldX: number, worldY: number): [number, number] | null {
  if (!_lastFrame) return null;
  const cam = _lastFrame.camera;

  const fovRad = (cam.fov * Math.PI) / 180;
  const forward = new THREE.Vector3(cam.tgtX - cam.posX, cam.tgtY - cam.posY, cam.tgtZ - cam.posZ).normalize();
  const up = new THREE.Vector3(cam.upX, cam.upY, cam.upZ);
  const right = new THREE.Vector3().crossVectors(forward, up).normalize();
  const trueUp = new THREE.Vector3().crossVectors(right, forward).normalize();

  const planeY = 0.35;
  const worldPos = new THREE.Vector3(worldX, planeY, worldY);
  const camPos = new THREE.Vector3(cam.posX, cam.posY, cam.posZ);
  const relative = worldPos.clone().sub(camPos);

  const z = relative.dot(forward);
  if (z <= 0.1) return null;
  const x = relative.dot(right);
  const y = relative.dot(trueUp);

  const aspect = window.innerWidth / window.innerHeight;
  const halfH = Math.tan(fovRad * 0.5) * z;
  const halfW = halfH * aspect;

  const ndcX = x / halfW;
  const ndcY = y / halfH;
  const sx = (ndcX * 0.5 + 0.5) * window.innerWidth;
  const sy = (-ndcY * 0.5 + 0.5) * window.innerHeight;
  return [sx, sy];
}

function animate(): void {
  requestAnimationFrame(animate);

  const dt = 1 / 60;
  interpolator.tick(dt);

  if (_lastFrame) {
    const cam = _lastFrame.camera;
    camera.position.set(cam.posX, cam.posY, cam.posZ);
    camera.lookAt(cam.tgtX, cam.tgtY, cam.tgtZ);
    camera.up.set(cam.upX, cam.upY, cam.upZ);
    camera.fov = cam.fov;
    camera.updateProjectionMatrix();

    entityManager.update(interpolator.getInterpolated());
    groundOverlayRenderer.update(_lastFrame.groundOverlays);

    hudRenderer.clear();
    hudRenderer.drawDebugOverlay(
      _lastFrame.debugLines, _lastFrame.debugCircles, _lastFrame.debugBoxes,
      worldToScreen2D,
    );
    hudRenderer.drawScreenHud(_lastFrame.screenHud);
    hudRenderer.drawScreenOverlays(_lastFrame.screenOverlays);

    uiOverlay.update(_lastFrame.uiHtml);
  }

  renderer.render(scene, camera);
  _frameCount++;

  const now = performance.now();
  if (now - _lastStatTime > 1000) {
    _displayFps = _frameCount;
    _displayKbps = _bytesReceived / 1024;
    _frameCount = 0;
    _bytesReceived = 0;
    _lastStatTime = now;

    const entities = _lastFrame?.primitives.length ?? 0;
    const tick = _lastFrame?.simTick ?? 0;
    statsEl.textContent = `FPS: ${_displayFps} | ${_displayKbps.toFixed(1)} KB/s | Entities: ${entities} | Tick: ${tick}`;
  }
}

function onResize(): void {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
  hudRenderer.resize(window.innerWidth, window.innerHeight);
}

window.addEventListener('resize', onResize);
hudRenderer.resize(window.innerWidth, window.innerHeight);

connectWebSocket();
animate();
