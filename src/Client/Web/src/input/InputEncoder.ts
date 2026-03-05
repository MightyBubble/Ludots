const MSG_TYPE_INPUT = 0x81;

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
  private _keyBits = 0n; // BigInt for 64-bit bitfield
  private _buf = new ArrayBuffer(25);
  private _view = new DataView(this._buf);

  onMouseMove(x: number, y: number): void {
    this._mouseX = x;
    this._mouseY = y;
  }

  onMouseButton(button: number, down: boolean): void {
    // Browser: 0=left, 1=middle, 2=right → Server: bit0=left, bit1=right, bit2=middle
    let bit: number;
    if (button === 0) bit = 1;       // left
    else if (button === 2) bit = 2;  // right
    else if (button === 1) bit = 4;  // middle
    else return;

    if (down) this._buttonMask |= bit;
    else this._buttonMask &= ~bit;
  }

  onWheel(deltaY: number): void {
    this._mouseWheel = -deltaY / 100;
  }

  onKey(code: string, down: boolean): void {
    const bit = KEY_MAP[code];
    if (bit === undefined) return;
    const mask = 1n << BigInt(bit);
    if (down) this._keyBits |= mask;
    else this._keyBits &= ~mask;
  }

  encode(): ArrayBuffer {
    this._view.setUint8(0, MSG_TYPE_INPUT);
    this._view.setInt32(1, this._buttonMask, true);
    this._view.setFloat32(5, this._mouseX, true);
    this._view.setFloat32(9, this._mouseY, true);
    this._view.setFloat32(13, this._mouseWheel, true);
    this._view.setBigUint64(17, BigInt.asUintN(64, this._keyBits), true);

    this._mouseWheel = 0; // consumed
    return this._buf;
  }
}
