const MSG_INPUT = 0x80;
const MSG_VIEWPORT = 0x82;

/**
 * Key path to bit index mapping.
 * Must match the C# WebInputBackend.KeyPaths array exactly.
 */
const KEY_MAP: Record<string, number> = {
  'KeyW': 0, 'KeyA': 1, 'KeyS': 2, 'KeyD': 3,
  'KeyQ': 4, 'KeyE': 5, 'KeyR': 6, 'KeyF': 7,
  'Space': 8, 'ShiftLeft': 9, 'ControlLeft': 10, 'Tab': 11,
  'Digit1': 12, 'Digit2': 13, 'Digit3': 14, 'Digit4': 15,
  'Digit5': 16, 'Digit6': 17, 'Digit7': 18, 'Digit8': 19,
  'Digit9': 20, 'Digit0': 21, 'Escape': 22, 'Enter': 23,
  'Backspace': 24, 'Delete': 25, 'AltLeft': 26,
  'KeyZ': 27, 'KeyX': 28, 'KeyC': 29, 'KeyV': 30, 'KeyB': 31,
  // KeyMask1 (bit 32+)
  'KeyG': 32, 'KeyH': 33, 'KeyJ': 34, 'KeyK': 35,
  'KeyL': 36, 'KeyU': 37, 'KeyY': 38, 'KeyI': 39,
  'KeyO': 40, 'KeyP': 41, 'KeyT': 42, 'KeyN': 43,
  'KeyM': 44,
};

export class InputEncoder {
  private readonly _buffer = new ArrayBuffer(1 + 24); // MsgType + WireInputState
  private readonly _view: DataView;
  private readonly _bytes: Uint8Array;

  private _mouseX = 0;
  private _mouseY = 0;
  private _mouseWheel = 0;
  private _buttonMask = 0;
  private _keyMask0 = 0;
  private _keyMask1 = 0;
  private readonly _keysDown = new Set<string>();

  constructor() {
    this._view = new DataView(this._buffer);
    this._bytes = new Uint8Array(this._buffer);
  }

  onMouseMove(x: number, y: number): void {
    this._mouseX = x;
    this._mouseY = y;
  }

  onMouseButton(button: number, down: boolean): void {
    // Browser: 0=left, 1=middle, 2=right
    // Server:  bit0=left, bit1=right, bit2=middle
    const bit = button === 1 ? 4 : button === 2 ? 2 : (1 << button);
    if (down) this._buttonMask |= bit;
    else this._buttonMask &= ~bit;
  }

  onMouseWheel(deltaY: number): void {
    this._mouseWheel = -deltaY / 100;
  }

  onKey(code: string, down: boolean): void {
    if (down) this._keysDown.add(code);
    else this._keysDown.delete(code);
    this._rebuildKeyMasks();
  }

  /** Encode current input state into a binary message ready for WebSocket send. */
  encode(): ArrayBuffer {
    let p = 0;
    this._bytes[p++] = MSG_INPUT;
    this._view.setFloat32(p, this._mouseX, true); p += 4;
    this._view.setFloat32(p, this._mouseY, true); p += 4;
    this._view.setFloat32(p, this._mouseWheel, true); p += 4;
    this._view.setUint32(p, this._buttonMask, true); p += 4;
    this._view.setUint32(p, this._keyMask0, true); p += 4;
    this._view.setUint32(p, this._keyMask1, true);

    this._mouseWheel = 0;
    return this._buffer;
  }

  encodeViewport(width: number, height: number, fov: number): ArrayBuffer {
    const buf = new ArrayBuffer(1 + 12);
    const v = new DataView(buf);
    const b = new Uint8Array(buf);
    b[0] = MSG_VIEWPORT;
    v.setFloat32(1, width, true);
    v.setFloat32(5, height, true);
    v.setFloat32(9, fov, true);
    return buf;
  }

  private _rebuildKeyMasks(): void {
    let m0 = 0, m1 = 0;
    for (const code of this._keysDown) {
      const idx = KEY_MAP[code];
      if (idx === undefined) continue;
      if (idx < 32) m0 |= (1 << idx);
      else m1 |= (1 << (idx - 32));
    }
    this._keyMask0 = m0;
    this._keyMask1 = m1;
  }
}
