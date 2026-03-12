const MSG_TYPE_INPUT_STATE = 0x81;
const MSG_TYPE_POINTER_EVENT = 0x82;

const POINTER_ACTION_DOWN = 0;
const POINTER_ACTION_MOVE = 1;
const POINTER_ACTION_UP = 2;
const POINTER_ACTION_CANCEL = 3;
const POINTER_ACTION_SCROLL = 4;

const KEY_MAP: Record<string, number> = {
  KeyA: 0, KeyB: 1, KeyC: 2, KeyD: 3, KeyE: 4, KeyF: 5, KeyG: 6, KeyH: 7,
  KeyI: 8, KeyJ: 9, KeyK: 10, KeyL: 11, KeyM: 12, KeyN: 13, KeyO: 14, KeyP: 15,
  KeyQ: 16, KeyR: 17, KeyS: 18, KeyT: 19, KeyU: 20, KeyV: 21, KeyW: 22, KeyX: 23,
  KeyY: 24, KeyZ: 25,
  Digit0: 26, Digit1: 27, Digit2: 28, Digit3: 29, Digit4: 30, Digit5: 31,
  Digit6: 32, Digit7: 33, Digit8: 34, Digit9: 35,
  Space: 36, ShiftLeft: 37, ControlLeft: 38, AltLeft: 39,
  Enter: 40, Escape: 41, Tab: 42, Backspace: 43, Delete: 44,
  ArrowUp: 45, ArrowDown: 46, ArrowLeft: 47, ArrowRight: 48,
  F1: 49, F2: 50, F3: 51, F4: 52, F5: 53,
};

export class InputEncoder {
  private _buttonMask = 0;
  private _mouseX = 0;
  private _mouseY = 0;
  private _mouseWheel = 0;
  private _keyBits = 0n;
  private _hasPointerSample = false;
  private readonly _stateBuffer = new ArrayBuffer(33);
  private readonly _stateView = new DataView(this._stateBuffer);
  private readonly _pointerMessages: ArrayBuffer[] = [];

  onPointerMove(x: number, y: number, viewportWidth: number, viewportHeight: number): void {
    this.setPointerPosition(x, y);
    this.enqueuePointer(POINTER_ACTION_MOVE, x, y, 0, 0, viewportWidth, viewportHeight);
  }

  onPointerButton(button: number, down: boolean, x: number, y: number, viewportWidth: number, viewportHeight: number): void {
    this.setPointerPosition(x, y);

    let bit = 0;
    if (button === 0) bit = 1;
    else if (button === 2) bit = 2;
    else if (button === 1) bit = 4;
    else return;

    if (down) this._buttonMask |= bit;
    else this._buttonMask &= ~bit;

    this.enqueuePointer(down ? POINTER_ACTION_DOWN : POINTER_ACTION_UP, x, y, 0, 0, viewportWidth, viewportHeight);
  }

  onPointerCancel(x: number, y: number, viewportWidth: number, viewportHeight: number): void {
    this.setPointerPosition(x, y);
    this._buttonMask = 0;
    this.enqueuePointer(POINTER_ACTION_CANCEL, x, y, 0, 0, viewportWidth, viewportHeight);
  }

  onWheel(x: number, y: number, deltaX: number, deltaY: number, viewportWidth: number, viewportHeight: number): void {
    const normalizedX = -deltaX / 100;
    const normalizedY = -deltaY / 100;
    this.setPointerPosition(x, y);
    this._mouseWheel += normalizedY;
    this.enqueuePointer(POINTER_ACTION_SCROLL, x, y, normalizedX, normalizedY, viewportWidth, viewportHeight);
  }

  onKey(code: string, down: boolean): void {
    const bit = KEY_MAP[code];
    if (bit === undefined) {
      return;
    }

    const mask = 1n << BigInt(bit);
    if (down) this._keyBits |= mask;
    else this._keyBits &= ~mask;
  }

  clearPointerSample(): void {
    this._hasPointerSample = false;
    this._buttonMask = 0;
  }

  clearKeyboardState(): void {
    this._keyBits = 0n;
  }

  encodeState(viewportWidth: number, viewportHeight: number): ArrayBuffer {
    if (!this._hasPointerSample) {
      this._mouseX = viewportWidth * 0.5;
      this._mouseY = viewportHeight * 0.5;
    }

    this._stateView.setUint8(0, MSG_TYPE_INPUT_STATE);
    this._stateView.setInt32(1, this._buttonMask, true);
    this._stateView.setFloat32(5, this._mouseX, true);
    this._stateView.setFloat32(9, this._mouseY, true);
    this._stateView.setFloat32(13, this._mouseWheel, true);
    this._stateView.setBigUint64(17, BigInt.asUintN(64, this._keyBits), true);
    this._stateView.setInt32(25, viewportWidth, true);
    this._stateView.setInt32(29, viewportHeight, true);
    this._mouseWheel = 0;
    return this._stateBuffer;
  }

  drainPointerMessages(): ArrayBuffer[] {
    if (this._pointerMessages.length === 0) {
      return [];
    }

    return this._pointerMessages.splice(0, this._pointerMessages.length);
  }

  private enqueuePointer(
    action: number,
    x: number,
    y: number,
    deltaX: number,
    deltaY: number,
    viewportWidth: number,
    viewportHeight: number,
  ): void {
    const buffer = new ArrayBuffer(30);
    const view = new DataView(buffer);
    view.setUint8(0, MSG_TYPE_POINTER_EVENT);
    view.setUint8(1, action);
    view.setInt32(2, this._buttonMask, true);
    view.setFloat32(6, x, true);
    view.setFloat32(10, y, true);
    view.setFloat32(14, deltaX, true);
    view.setFloat32(18, deltaY, true);
    view.setInt32(22, viewportWidth, true);
    view.setInt32(26, viewportHeight, true);
    this._pointerMessages.push(buffer);
  }

  private setPointerPosition(x: number, y: number): void {
    this._mouseX = x;
    this._mouseY = y;
    this._hasPointerSample = true;
  }
}
