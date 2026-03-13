import type { PrimitiveItem } from '../core/FrameDecoder';

export class PositionInterpolator {
  private _prev: PrimitiveItem[] = [];
  private _next: PrimitiveItem[] = [];
  private _interpolated: PrimitiveItem[] = [];
  private readonly _prevByStableId = new Map<number, PrimitiveItem>();
  private _factor = 1;
  private _frameDurationMs = 33; // ~30fps default
  private _elapsedMs = 0;
  private _lastPushTs = 0;
  private _lastServerTimestampMs = 0;

  pushFrame(items: PrimitiveItem[], serverTimestampMs: number): void {
    this._prev = this._next;
    this._next = items;
    this._prevByStableId.clear();
    for (const item of this._prev) {
      if (item.stableId > 0 && !this._prevByStableId.has(item.stableId)) {
        this._prevByStableId.set(item.stableId, item);
      }
    }

    const now = performance.now();
    if (this._lastServerTimestampMs > 0 && serverTimestampMs > this._lastServerTimestampMs) {
      this._frameDurationMs = Math.max(8, serverTimestampMs - this._lastServerTimestampMs);
    } else if (this._lastPushTs > 0) {
      this._frameDurationMs = Math.max(8, now - this._lastPushTs);
    }

    this._lastPushTs = now;
    this._lastServerTimestampMs = serverTimestampMs;
    this._elapsedMs = 0;
    this._factor = 0;
  }

  tick(dt: number): void {
    this._elapsedMs += dt * 1000;
    this._factor = Math.min(1, this._elapsedMs / this._frameDurationMs);
  }

  getInterpolated(): PrimitiveItem[] {
    const prev = this._prev;
    const next = this._next;
    const t = this._factor;

    if (prev.length === 0 || t >= 0.99) return next;

    const count = next.length;
    while (this._interpolated.length < count) {
      this._interpolated.push({ meshAssetId: 1, stableId: 0, posX: 0, posY: 0, posZ: 0, scaleX: 1, scaleY: 1, scaleZ: 1, r: 1, g: 1, b: 1, a: 1 });
    }
    this._interpolated.length = count;

    for (let i = 0; i < count; i++) {
      const n = next[i];
      const matchedPrev = n.stableId > 0 ? this._prevByStableId.get(n.stableId) : undefined;
      const p = matchedPrev && matchedPrev.meshAssetId === n.meshAssetId ? matchedPrev : n;
      const out = this._interpolated[i];
      out.meshAssetId = n.meshAssetId;
      out.stableId = n.stableId;
      out.posX = p.posX + (n.posX - p.posX) * t;
      out.posY = p.posY + (n.posY - p.posY) * t;
      out.posZ = p.posZ + (n.posZ - p.posZ) * t;
      out.scaleX = n.scaleX;
      out.scaleY = n.scaleY;
      out.scaleZ = n.scaleZ;
      out.r = n.r; out.g = n.g; out.b = n.b; out.a = n.a;
    }
    return this._interpolated;
  }
}
