export type OnFrameCallback = (data: ArrayBuffer) => void;
export type OnStatusCallback = (connected: boolean) => void;

/**
 * Manages the WebSocket connection to the Ludots server.
 * Auto-reconnects on disconnection.
 */
export class WebSocketTransport {
  private _ws: WebSocket | null = null;
  private _url: string;
  private _onFrame: OnFrameCallback;
  private _onStatus: OnStatusCallback;
  private _reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private _disposed = false;

  constructor(url: string, onFrame: OnFrameCallback, onStatus: OnStatusCallback) {
    this._url = url;
    this._onFrame = onFrame;
    this._onStatus = onStatus;
  }

  connect(): void {
    if (this._disposed) return;
    this._cleanup();

    const ws = new WebSocket(this._url);
    ws.binaryType = 'arraybuffer';

    ws.onopen = () => {
      console.log('[WebSocket] Connected');
      this._onStatus(true);
    };

    ws.onmessage = (ev) => {
      if (ev.data instanceof ArrayBuffer) {
        this._onFrame(ev.data);
      }
    };

    ws.onclose = () => {
      console.log('[WebSocket] Disconnected');
      this._onStatus(false);
      this._scheduleReconnect();
    };

    ws.onerror = () => {
      ws.close();
    };

    this._ws = ws;
  }

  send(data: ArrayBuffer): void {
    if (this._ws?.readyState === WebSocket.OPEN) {
      this._ws.send(data);
    }
  }

  dispose(): void {
    this._disposed = true;
    this._cleanup();
    if (this._reconnectTimer) {
      clearTimeout(this._reconnectTimer);
      this._reconnectTimer = null;
    }
  }

  private _cleanup(): void {
    if (this._ws) {
      this._ws.onopen = null;
      this._ws.onmessage = null;
      this._ws.onclose = null;
      this._ws.onerror = null;
      if (this._ws.readyState === WebSocket.OPEN) {
        this._ws.close();
      }
      this._ws = null;
    }
  }

  private _scheduleReconnect(): void {
    if (this._disposed) return;
    this._reconnectTimer = setTimeout(() => {
      this._reconnectTimer = null;
      console.log('[WebSocket] Reconnecting...');
      this.connect();
    }, 2000);
  }
}
