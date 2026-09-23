import { dotnet } from './_framework/dotnet.js';

const status = document.querySelector('#boot-status');
const canvas = document.querySelector('#canvas');

canvas.addEventListener('contextmenu', event => event.preventDefault());

function stopStartup(message) {
  document.documentElement.dataset.bootState = 'failed';
  document.documentElement.dataset.bootError = message;
  status.textContent = message;
  status.title = message;
  throw new Error(`[Ludots WebGPU] ${message}`);
}

function describeError(error) {
  if (error instanceof Error) {
    return `${error.name}: ${error.message}`;
  }

  return String(error);
}

if (!globalThis.isSecureContext) {
  stopStartup('A secure browser context is required.');
}

if (!globalThis.crossOriginIsolated) {
  stopStartup('Cross-origin isolation is required for C# worker threads.');
}

if (!navigator.gpu) {
  stopStartup('This browser does not expose WebGPU.');
}

if (!canvas?.transferControlToOffscreen) {
  stopStartup('This browser cannot transfer the render canvas to the C# runtime thread.');
}

let canvasWidth = canvas.width;
let canvasHeight = canvas.height;
let offscreenCanvas = canvas.transferControlToOffscreen();
let canvasTransferred = false;

Object.defineProperty(canvas, 'width', {
  get: () => canvasWidth,
  set: value => { canvasWidth = value; },
});
Object.defineProperty(canvas, 'height', {
  get: () => canvasHeight,
  set: value => { canvasHeight = value; },
});

const originalPostMessage = Worker.prototype.postMessage;
Worker.prototype.postMessage = function (message, transfer = []) {
  if (offscreenCanvas && message?.cmd === 'run') {
    message.offscreenCanvases = {
      canvas: { offscreenCanvas },
    };

    canvasTransferred = true;
    const transferList = [...transfer, offscreenCanvas];
    offscreenCanvas = null;
    Worker.prototype.postMessage = originalPostMessage;
    return originalPostMessage.call(this, message, transferList);
  }

  return originalPostMessage.call(this, message, transfer);
};

try {
  const { runMain } = await dotnet.create();
  await runMain();

  if (!canvasTransferred) {
    stopStartup('The C# runtime thread did not accept the render canvas.');
  }

  document.documentElement.dataset.bootState = 'running';
  status.textContent = 'C# runtime active';
} catch (error) {
  Worker.prototype.postMessage = originalPostMessage;
  if (document.documentElement.dataset.bootState !== 'failed') {
    const detail = describeError(error);
    document.documentElement.dataset.bootState = 'failed';
    document.documentElement.dataset.bootError = detail;
    status.textContent = `C# runtime failed: ${detail}`;
    status.title = detail;
  }
  console.error('[Ludots WebGPU] C# runtime startup failed.', error);
  throw error;
}
