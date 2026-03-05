import type { ScreenHudItem, DebugLine, DebugCircle, DebugBox } from '../core/FrameDecoder';

export class HudRenderer {
  private readonly _canvas: HTMLCanvasElement;
  private readonly _ctx: CanvasRenderingContext2D;

  constructor(canvas: HTMLCanvasElement) {
    this._canvas = canvas;
    this._ctx = canvas.getContext('2d')!;
  }

  resize(w: number, h: number): void {
    this._canvas.width = w;
    this._canvas.height = h;
  }

  clear(): void {
    this._ctx.clearRect(0, 0, this._canvas.width, this._canvas.height);
  }

  drawScreenHud(items: ScreenHudItem[]): void {
    const ctx = this._ctx;
    for (const item of items) {
      const HUD_BAR = 0;
      const HUD_TEXT = 1;

      if (item.kind === HUD_BAR) {
        const x = Math.round(item.sx);
        const y = Math.round(item.sy);
        const w = Math.round(item.width);
        const h = Math.round(item.height);

        ctx.fillStyle = this.rgba(item.c0r, item.c0g, item.c0b, item.c0a);
        ctx.fillRect(x, y, w, h);
        ctx.fillStyle = this.rgba(item.c1r, item.c1g, item.c1b, item.c1a);
        ctx.fillRect(x, y, Math.round(w * item.v0), h);
        ctx.strokeStyle = 'black';
        ctx.strokeRect(x, y, w, h);
      } else if (item.kind === HUD_TEXT) {
        const fontSize = item.fontSize <= 0 ? 16 : item.fontSize;
        ctx.font = `${fontSize}px monospace`;
        ctx.fillStyle = this.rgba(item.c0r, item.c0g, item.c0b, item.c0a);
        let text = '';
        if (item.id1 === 1) text = `${Math.round(item.v0)}/${Math.round(item.v1)}`;
        else if (item.id1 === 2) text = `${Math.round(item.v0)}`;
        else text = `${item.v0.toFixed(1)}`;
        ctx.fillText(text, item.sx, item.sy + fontSize);
      }
    }
  }

  drawDebugOverlay(
    lines: DebugLine[],
    circles: DebugCircle[],
    boxes: DebugBox[],
    worldToScreen: (x: number, y: number) => [number, number] | null,
  ): void {
    const ctx = this._ctx;
    for (const l of lines) {
      const a = worldToScreen(l.ax, l.ay);
      const b = worldToScreen(l.bx, l.by);
      if (!a || !b) continue;
      ctx.strokeStyle = this.rgbaBytes(l.r, l.g, l.b, l.a);
      ctx.lineWidth = Math.max(1, l.thickness * 0.5);
      ctx.beginPath();
      ctx.moveTo(a[0], a[1]);
      ctx.lineTo(b[0], b[1]);
      ctx.stroke();
    }

    for (const c of circles) {
      const center = worldToScreen(c.cx, c.cy);
      const edge = worldToScreen(c.cx + c.radius, c.cy);
      if (!center || !edge) continue;
      const r = Math.abs(edge[0] - center[0]);
      if (r < 1) continue;
      ctx.strokeStyle = this.rgbaBytes(c.r, c.g, c.b, c.a);
      ctx.lineWidth = Math.max(1, c.thickness * 0.5);
      ctx.beginPath();
      ctx.arc(center[0], center[1], r, 0, Math.PI * 2);
      ctx.stroke();
    }

    for (const b of boxes) {
      const center = worldToScreen(b.cx, b.cy);
      if (!center) continue;
      const tl = worldToScreen(b.cx - b.halfW, b.cy - b.halfH);
      const br = worldToScreen(b.cx + b.halfW, b.cy + b.halfH);
      if (!tl || !br) continue;
      ctx.strokeStyle = this.rgbaBytes(b.r, b.g, b.b, b.a);
      ctx.lineWidth = Math.max(1, b.thickness * 0.5);
      ctx.strokeRect(tl[0], tl[1], br[0] - tl[0], br[1] - tl[1]);
    }
  }

  private rgba(r: number, g: number, b: number, a: number): string {
    return `rgba(${(r * 255) | 0},${(g * 255) | 0},${(b * 255) | 0},${a.toFixed(2)})`;
  }

  private rgbaBytes(r: number, g: number, b: number, a: number): string {
    return `rgba(${r},${g},${b},${(a / 255).toFixed(2)})`;
  }
}
