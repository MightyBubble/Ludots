import type { ScreenHudItem, ScreenOverlayItem, DebugLine, DebugCircle, DebugBox, PresentationTextPacket } from '../core/FrameDecoder';

export class HudRenderer {
  private readonly _canvas: HTMLCanvasElement;
  private readonly _ctx: CanvasRenderingContext2D;
  private readonly _floatBits = new DataView(new ArrayBuffer(4));

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

      const deco = item.deco;
      if (item.kind === HUD_BAR && item.text) {
        const img = this.obtainImage(item.text);
        if (img.complete && img.naturalWidth > 0) {
          ctx.drawImage(img, Math.round(item.sx), Math.round(item.sy), Math.round(item.width), Math.round(item.height));
        }
      } else if (item.kind === HUD_BAR) {
        const x = Math.round(item.sx);
        const y = Math.round(item.sy);
        const w = Math.round(item.width);
        const h = Math.round(item.height);
        const pad = deco ? deco.padding : 0;
        const ix = x + pad, iy = y + pad, iw = Math.max(1, w - 2 * pad), ih = Math.max(1, h - 2 * pad);
        const radius = deco ? deco.radius : 0;

        if (deco && deco.shadowColor && deco.shadowBlur >= 0) {
          ctx.save();
          ctx.shadowColor = this.rgba(...deco.shadowColor);
          ctx.shadowBlur = deco.shadowBlur;
          ctx.shadowOffsetX = deco.shadowOffsetX;
          ctx.shadowOffsetY = deco.shadowOffsetY;
          ctx.fillStyle = this.rgba(item.c0r, item.c0g, item.c0b, Math.max(0.01, item.c0a));
          this.fillRounded(ctx, x, y, w, h, radius, deco?.clipShape ?? 0);
          ctx.restore();
        }

        ctx.fillStyle = this.gradientOrColor(ctx, ix, ix + iw,
          item.c0r, item.c0g, item.c0b, item.c0a, deco?.backgroundGradientTo);
        this.fillRounded(ctx, ix, iy, iw, ih, radius, deco?.clipShape ?? 0);

        const fillW = Math.round(iw * item.v0);
        if (fillW > 0) {
          ctx.save();
          ctx.beginPath();
          this.roundedPath(ctx, ix, iy, iw, ih, radius, deco?.clipShape ?? 0);
          ctx.clip();
          ctx.fillStyle = this.gradientOrColor(ctx, ix, ix + iw,
            item.c1r, item.c1g, item.c1b, item.c1a, deco?.fillGradientTo);
          ctx.fillRect(ix, iy, fillW, ih);
          ctx.restore();
        }

        if (deco && deco.borderWidth > 0) {
          ctx.strokeStyle = this.rgba(...(deco.borderColor ?? [0, 0, 0, 1]));
          ctx.lineWidth = deco.borderWidth;
          this.strokeRounded(ctx, x, y, w, h, radius, deco?.clipShape ?? 0);
          ctx.lineWidth = 1;
        } else {
          ctx.strokeStyle = 'black';
          ctx.lineWidth = 1;
          this.strokeRounded(ctx, x, y, w, h, radius, deco?.clipShape ?? 0);
        }
      } else if (item.kind === HUD_TEXT) {
        const fontSize = item.fontSize <= 0 ? 16 : item.fontSize;
        const bold = deco?.bold ? 'bold ' : '';
        const italic = deco?.italic ? 'italic ' : '';
        ctx.font = `${italic}${bold}${fontSize}px monospace`;
        const text = this.resolveHudText(item);
        const centered = deco?.textAlignCenter === true;
        ctx.textAlign = centered ? 'center' : 'left';

        if (deco && deco.boxBackground) {
          const pad = deco.padding + deco.borderWidth;
          const textW = ctx.measureText(text).width;
          const boxX = (centered ? item.sx - textW / 2 : item.sx) - pad;
          const boxY = item.sy - pad;
          const boxW = textW + 2 * pad;
          const boxH = fontSize * 1.5 + 2 * pad;
          ctx.fillStyle = this.rgba(...deco.boxBackground);
          this.fillRounded(ctx, boxX, boxY, boxW, boxH, deco.radius, deco.clipShape ?? 0);
          if (deco.borderWidth > 0) {
            ctx.strokeStyle = this.rgba(...(deco.borderColor ?? [0, 0, 0, 1]));
            ctx.lineWidth = deco.borderWidth;
            this.strokeRounded(ctx, boxX, boxY, boxW, boxH, deco.radius, deco.clipShape ?? 0);
            ctx.lineWidth = 1;
          }
        }

        if (deco && deco.shadowColor) {
          ctx.save();
          ctx.shadowColor = this.rgba(...deco.shadowColor);
          ctx.shadowBlur = deco.shadowBlur;
          ctx.shadowOffsetX = deco.shadowOffsetX;
          ctx.shadowOffsetY = deco.shadowOffsetY;
          ctx.fillStyle = this.rgba(item.c0r, item.c0g, item.c0b, item.c0a);
          ctx.fillText(text, item.sx, item.sy + fontSize);
          ctx.restore();
        }

        ctx.fillStyle = this.rgba(item.c0r, item.c0g, item.c0b, item.c0a);
        ctx.fillText(text, item.sx, item.sy + fontSize);
        ctx.textAlign = 'left';
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

  private _imageCache = new Map<string, HTMLImageElement>();

  private obtainImage(src: string): HTMLImageElement {
    let img = this._imageCache.get(src);
    if (!img) {
      img = new Image();
      img.src = src;
      this._imageCache.set(src, img);
    }
    return img;
  }

  /** css clip-path 预设的归一化顶点(与 Skia 端 BuildItemPath 同一合同)。 */
  private static readonly CLIP_POINTS: Record<number, number[]> = {
    1: [0, 0, 1, 0, 1, 0.62, 0.5, 1, 0, 0.62],
    2: [0.5, 0, 1, 0.5, 0.5, 1, 0, 0.5],
    3: [0, 0, 1, 0, 1, 0.78, 0.5, 0.55, 0, 0.78],
    4: [0.12, 0, 1, 0, 0.88, 1, 0, 1],
    5: [0, 0, 1, 0, 1, 0.72, 0.5, 1, 0, 0.72],
  };

  private roundedPath(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number, clipShape = 0): void {
    ctx.beginPath();
    const points = HudRenderer.CLIP_POINTS[clipShape];
    if (points) {
      ctx.moveTo(x + points[0] * w, y + points[1] * h);
      for (let i = 2; i < points.length; i += 2) {
        ctx.lineTo(x + points[i] * w, y + points[i + 1] * h);
      }
      ctx.closePath();
      return;
    }
    if (r > 0.5 && ctx.roundRect) {
      ctx.roundRect(x, y, w, h, r);
    } else {
      ctx.rect(x, y, w, h);
    }
  }

  private fillRounded(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number, clipShape = 0): void {
    this.roundedPath(ctx, x, y, w, h, r, clipShape);
    ctx.fill();
  }

  private strokeRounded(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number, clipShape = 0): void {
    this.roundedPath(ctx, x, y, w, h, r, clipShape);
    ctx.stroke();
  }

  private gradientOrColor(
    ctx: CanvasRenderingContext2D,
    x0: number, x1: number,
    r: number, g: number, b: number, a: number,
    to?: [number, number, number, number],
  ): string | CanvasGradient {
    if (to) {
      const grad = ctx.createLinearGradient(x0, 0, x1, 0);
      grad.addColorStop(0, this.rgba(r, g, b, a));
      grad.addColorStop(1, this.rgba(...to));
      return grad;
    }
    return this.rgba(r, g, b, a);
  }

  private rgba(r: number, g: number, b: number, a: number): string {
    return `rgba(${(r * 255) | 0},${(g * 255) | 0},${(b * 255) | 0},${a.toFixed(2)})`;
  }

  private rgbaBytes(r: number, g: number, b: number, a: number): string {
    return `rgba(${r},${g},${b},${(a / 255).toFixed(2)})`;
  }

  private resolveHudText(item: ScreenHudItem): string {
    if (item.textPacket && item.textTemplate) {
      const text = this.formatTextPacket(item.textPacket, item.textTemplate, item.textTemplates);
      if (text) {
        return text;
      }
    }

    if (item.id0 !== 0 && item.text) {
      return item.text;
    }

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

  private resolveOverlayText(item: ScreenOverlayItem): string {
    if (item.textPacket && item.textTemplate) {
      const text = this.formatTextPacket(item.textPacket, item.textTemplate, item.textTemplates);
      if (text) {
        return text;
      }
    }

    return item.text;
  }

  private formatTextPacket(
    packet: PresentationTextPacket,
    template: string,
    templates?: Map<number, string>,
  ): string {
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

        text += this.formatTextArg(packet, argIndex, templates);
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

  private formatTextArg(
    packet: PresentationTextPacket,
    argIndex: number,
    templates?: Map<number, string>,
  ): string {
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
      case 4:
        return this.formatNestedTextToken(templates?.get(arg.raw32), arg.raw32);
      default:
        return '';
    }
  }

  private formatNestedTextToken(source: string | undefined, tokenId: number): string {
    if (source == null) {
      throw new Error(`Presentation text token argument ${tokenId} is missing from the template table.`);
    }

    let text = '';
    for (let i = 0; i < source.length; i++) {
      const ch = source[i];
      if (ch === '{') {
        if (i + 1 < source.length && source[i + 1] === '{') {
          text += '{';
          i++;
          continue;
        }

        throw new Error(`Presentation text token argument ${tokenId} must be a zero-argument template.`);
      }

      if (ch === '}') {
        if (i + 1 < source.length && source[i + 1] === '}') {
          text += '}';
          i++;
          continue;
        }

        throw new Error(`Presentation text token argument ${tokenId} must be a zero-argument template.`);
      }

      text += ch;
    }

    return text;
  }

  private int32BitsToFloat32(raw32: number): number {
    this._floatBits.setInt32(0, raw32, true);
    return this._floatBits.getFloat32(0, true);
  }
}
