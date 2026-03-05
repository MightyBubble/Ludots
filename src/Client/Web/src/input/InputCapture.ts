import { InputEncoder } from './InputEncoder';

export class InputCapture {
  private readonly _encoder = new InputEncoder();
  private readonly _canvas: HTMLCanvasElement;

  get encoder(): InputEncoder { return this._encoder; }

  constructor(canvas: HTMLCanvasElement) {
    this._canvas = canvas;
    this._bind();
  }

  private _bind(): void {
    this._canvas.addEventListener('mousemove', (e) => {
      this._encoder.onMouseMove(e.clientX, e.clientY);
    });

    this._canvas.addEventListener('mousedown', (e) => {
      e.preventDefault();
      this._encoder.onMouseButton(e.button, true);
    });

    this._canvas.addEventListener('mouseup', (e) => {
      this._encoder.onMouseButton(e.button, false);
    });

    this._canvas.addEventListener('wheel', (e) => {
      e.preventDefault();
      this._encoder.onWheel(e.deltaY);
    }, { passive: false });

    this._canvas.addEventListener('contextmenu', (e) => e.preventDefault());

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this._encoder.onKey(e.code, true);
    });

    window.addEventListener('keyup', (e) => {
      this._encoder.onKey(e.code, false);
    });
  }
}
