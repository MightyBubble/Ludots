import type { ScreenHudItem, ScreenOverlayItem, DebugLine, DebugCircle, DebugBox, PresentationTextPacket } from '../core/FrameDecoder';

export class HudRenderer {
  private static readonly MAX_HUD_TEXT_SPRITES = 4096;
  private static readonly MAX_OVERLAY_TEXT_SPRITES = 256;

  private readonly _canvas: HTMLCanvasElement;
  private readonly _ctx: CanvasRenderingContext2D;
  private readonly _floatBits = new DataView(new ArrayBuffer(4));
  private readonly _hudTextSprites = new Map<string, HTMLCanvasElement>();
  private readonly _overlayTextSprites = new Map<string, HTMLCanvasElement>();

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
      const HUD_BAR = 1;
      const HUD_TEXT = 2;

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
        const sprite = this.resolveHudTextSprite(item);
        if (sprite) {
          ctx.drawImage(sprite, Math.round(item.sx), Math.round(item.sy));
          continue;
        }

        const fontSize = item.fontSize <= 0 ? 16 : item.fontSize;
        ctx.font = `${fontSize}px monospace`;
        ctx.fillStyle = this.rgba(item.c0r, item.c0g, item.c0b, item.c0a);
        const text = this.resolveHudText(item);
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

  drawScreenOverlays(items: ScreenOverlayItem[]): void {
    const ctx = this._ctx;
    const OVERLAY_TEXT = 0;
    const OVERLAY_RECT = 1;

    for (const item of items) {
      if (item.kind === OVERLAY_TEXT) {
        const sprite = this.resolveOverlayTextSprite(item);
        if (sprite) {
          ctx.drawImage(sprite, item.x, item.y);
          continue;
        }

        const fontSize = item.fontSize <= 0 ? 16 : item.fontSize;
        ctx.font = `${fontSize}px monospace`;
        ctx.fillStyle = this.rgba(item.cr, item.cg, item.cb, item.ca);
        const text = this.resolveOverlayText(item);
        if (text) {
          ctx.fillText(text, item.x, item.y + fontSize);
        }
      } else if (item.kind === OVERLAY_RECT) {
        if (item.width <= 0 || item.height <= 0) continue;
        ctx.fillStyle = this.rgba(item.bgr, item.bgg, item.bgb, item.bga);
        ctx.fillRect(item.x, item.y, item.width, item.height);
        if (item.ca > 0.01) {
          ctx.strokeStyle = this.rgba(item.cr, item.cg, item.cb, item.ca);
          ctx.strokeRect(item.x, item.y, item.width, item.height);
        }
      }
    }
  }

  private resolveHudTextSprite(item: ScreenHudItem): HTMLCanvasElement | null {
    const fontSize = item.fontSize <= 0 ? 16 : item.fontSize;
    const color = this.rgba(item.c0r, item.c0g, item.c0b, item.c0a);

    if (item.textPacket && item.textTemplate) {
      const key = this.buildPacketSpriteKey(item.textPacket, item.textTemplate, fontSize, color);
      return this.getOrCreateTextSprite(this._hudTextSprites, key, HudRenderer.MAX_HUD_TEXT_SPRITES, () => this.formatTextPacket(item.textPacket!, item.textTemplate!), fontSize, color);
    }

    if (item.id0 !== 0 && item.text) {
      const key = `legacy|${fontSize}|${color}|${item.text}`;
      return this.getOrCreateTextSprite(this._hudTextSprites, key, HudRenderer.MAX_HUD_TEXT_SPRITES, () => item.text ?? '', fontSize, color);
    }

    if (item.id1 === 1 || item.id1 === 2 || item.id1 === 3) {
      const key = `numeric|${fontSize}|${color}|${item.id1}|${item.v0}|${item.v1}`;
      return this.getOrCreateTextSprite(this._hudTextSprites, key, HudRenderer.MAX_HUD_TEXT_SPRITES, () => this.resolveNumericHudText(item), fontSize, color);
    }

    return null;
  }

  private resolveOverlayTextSprite(item: ScreenOverlayItem): HTMLCanvasElement | null {
    if (!item.textPacket || !item.textTemplate) {
      return null;
    }

    const fontSize = item.fontSize <= 0 ? 16 : item.fontSize;
    const color = this.rgba(item.cr, item.cg, item.cb, item.ca);
    const key = this.buildPacketSpriteKey(item.textPacket, item.textTemplate, fontSize, color);
    return this.getOrCreateTextSprite(this._overlayTextSprites, key, HudRenderer.MAX_OVERLAY_TEXT_SPRITES, () => this.formatTextPacket(item.textPacket!, item.textTemplate!), fontSize, color);
  }

  private getOrCreateTextSprite(
    cache: Map<string, HTMLCanvasElement>,
    key: string,
    maxEntries: number,
    resolveText: () => string,
    fontSize: number,
    color: string,
  ): HTMLCanvasElement | null {
    const cached = cache.get(key);
    if (cached) {
      return cached;
    }

    const text = resolveText();
    if (!text) {
      return null;
    }

    if (cache.size >= maxEntries) {
      cache.clear();
    }

    const sprite = this.createTextSprite(text, fontSize, color);
    cache.set(key, sprite);
    return sprite;
  }

  private createTextSprite(text: string, fontSize: number, color: string): HTMLCanvasElement {
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d')!;
    ctx.font = `${fontSize}px monospace`;
    const width = Math.max(1, Math.ceil(ctx.measureText(text).width) + 2);
    const height = Math.max(1, Math.ceil(fontSize * 1.35));
    canvas.width = width;
    canvas.height = height;

    const drawCtx = canvas.getContext('2d')!;
    drawCtx.font = `${fontSize}px monospace`;
    drawCtx.textBaseline = 'top';
    drawCtx.fillStyle = color;
    drawCtx.fillText(text, 0, 0);
    return canvas;
  }

  private buildPacketSpriteKey(packet: PresentationTextPacket, template: string, fontSize: number, color: string): string {
    let key = `packet|${fontSize}|${color}|${template}|${packet.tokenId}|${packet.argCount}`;
    for (let i = 0; i < packet.argCount; i++) {
      const arg = packet.args[i];
      key += `|${arg?.type ?? 0}|${arg?.format ?? 0}|${arg?.raw32 ?? 0}`;
    }

    return key;
  }

  private rgba(r: number, g: number, b: number, a: number): string {
    return `rgba(${(r * 255) | 0},${(g * 255) | 0},${(b * 255) | 0},${a.toFixed(2)})`;
  }

  private rgbaBytes(r: number, g: number, b: number, a: number): string {
    return `rgba(${r},${g},${b},${(a / 255).toFixed(2)})`;
  }

  private resolveHudText(item: ScreenHudItem): string {
    if (item.textPacket && item.textTemplate) {
      const text = this.formatTextPacket(item.textPacket, item.textTemplate);
      if (text) {
        return text;
      }
    }

    if (item.id0 !== 0 && item.text) {
      return item.text;
    }

    return this.resolveNumericHudText(item);
  }

  private resolveOverlayText(item: ScreenOverlayItem): string {
    if (item.textPacket && item.textTemplate) {
      const text = this.formatTextPacket(item.textPacket, item.textTemplate);
      if (text) {
        return text;
      }
    }

    return item.text;
  }

  private resolveNumericHudText(item: ScreenHudItem): string {
    switch (item.id1) {
      case 1:
        return `${Math.round(item.v0)}/${Math.round(item.v1)}`;
      case 2:
        return `${Math.round(item.v0)}`;
      case 3:
        return `${item.v0}`;
      default:
        return `${item.v0.toFixed(1)}`;
    }
  }

  private formatTextPacket(packet: PresentationTextPacket, template: string): string {
    if (packet.tokenId <= 0 || !template) {
      return '';
    }

    let text = '';
    for (let i = 0; i < template.length; i++) {
      const ch = template[i];
      if (ch === '{') {
        if (i + 1 < template.length && template[i + 1] === '{') {
          text += '{';
          i++;
          continue;
        }

        const closeIndex = template.indexOf('}', i + 1);
        if (closeIndex < 0) {
          text += '{';
          continue;
        }

        const placeholder = template.substring(i + 1, closeIndex);
        const argIndex = Number.parseInt(placeholder, 10);
        if (Number.isNaN(argIndex)) {
          text += `{${placeholder}}`;
          i = closeIndex;
          continue;
        }

        text += this.formatTextArg(packet, argIndex);
        i = closeIndex;
        continue;
      }

      if (ch === '}') {
        if (i + 1 < template.length && template[i + 1] === '}') {
          text += '}';
          i++;
          continue;
        }
      }

      text += ch;
    }

    return text;
  }

  private formatTextArg(packet: PresentationTextPacket, argIndex: number): string {
    if (argIndex < 0 || argIndex >= packet.argCount || argIndex >= packet.args.length) {
      return '';
    }

    const arg = packet.args[argIndex];
    switch (arg.type) {
      case 1:
        return `${arg.raw32}`;
      case 2: {
        const value = this.int32BitsToFloat32(arg.raw32);
        switch (arg.format) {
          case 1:
            return `${Math.trunc(value)}`;
          case 2:
            return value.toFixed(0);
          case 3:
            return value.toFixed(1);
          case 4:
            return value.toFixed(2);
          default:
            return value.toFixed(3).replace(/\.?0+$/, '');
        }
      }
      default:
        return '';
    }
  }

  private int32BitsToFloat32(raw32: number): number {
    this._floatBits.setInt32(0, raw32, true);
    return this._floatBits.getFloat32(0, true);
  }
}
