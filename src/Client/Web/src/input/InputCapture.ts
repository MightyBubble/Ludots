import { InputEncoder } from './InputEncoder';

export class InputCapture {
  private readonly _encoder = new InputEncoder();
  private readonly _canvas: HTMLCanvasElement;

  get encoder(): InputEncoder { return this._encoder; }

  constructor(canvas: HTMLCanvasElement) {
    this._canvas = canvas;
    this._canvas.style.touchAction = 'none';
    this._bind();
  }

  private _bind(): void {
    this._canvas.addEventListener('pointerleave', () => {
      this._encoder.clearPointerSample();
    });

    this._canvas.addEventListener('pointermove', (e) => {
      this._encoder.onPointerMove(e.clientX, e.clientY, this.viewportWidth(), this.viewportHeight());
    });

    this._canvas.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      this._canvas.setPointerCapture(e.pointerId);
      this._encoder.onPointerButton(e.button, true, e.clientX, e.clientY, this.viewportWidth(), this.viewportHeight());
    });

    this._canvas.addEventListener('pointerup', (e) => {
      this._encoder.onPointerButton(e.button, false, e.clientX, e.clientY, this.viewportWidth(), this.viewportHeight());
      if (this._canvas.hasPointerCapture(e.pointerId)) {
        this._canvas.releasePointerCapture(e.pointerId);
      }
    });

    this._canvas.addEventListener('pointercancel', (e) => {
      this._encoder.onPointerCancel(e.clientX, e.clientY, this.viewportWidth(), this.viewportHeight());
      if (this._canvas.hasPointerCapture(e.pointerId)) {
        this._canvas.releasePointerCapture(e.pointerId);
      }
    });

    this._canvas.addEventListener('wheel', (e) => {
      e.preventDefault();
      this._encoder.onWheel(e.clientX, e.clientY, e.deltaX, e.deltaY, this.viewportWidth(), this.viewportHeight());
    }, { passive: false });

    this._canvas.addEventListener('contextmenu', (e) => e.preventDefault());

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this._encoder.onKey(e.code, true);
    });

    window.addEventListener('keyup', (e) => {
      this._encoder.onKey(e.code, false);
    });

    window.addEventListener('blur', () => {
      this._encoder.clearPointerSample();
      this._encoder.clearKeyboardState();
    });
  }

  private viewportWidth(): number {
    return Math.max(1, Math.round(window.innerWidth));
  }

  private viewportHeight(): number {
    return Math.max(1, Math.round(window.innerHeight));
  }
}
