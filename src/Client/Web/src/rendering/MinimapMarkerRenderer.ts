import type { MinimapMarkerStream } from '../core/MinimapMarkerStream';

export class MinimapMarkerRenderer {
  private readonly _stream: MinimapMarkerStream;
  private readonly _colorCache = new Map<number, string>();
  private readonly _shadowColorCache = new Map<number, string>();

  constructor(stream: MinimapMarkerStream) {
    this._stream = stream;
  }

  draw(context: CanvasRenderingContext2D): void {
    const count = this._stream.count;
    if (count <= 0) {
      return;
    }

    context.save();
    try {
      this.applyClip(context);
      this.drawOrientationLayer(context, count);
      this.drawMarkerLayer(context, count);
    } finally {
      context.restore();
    }
  }

  private drawOrientationLayer(context: CanvasRenderingContext2D, count: number): void {
    let runStart = 0;
    while (runStart < count) {
      const colorKey = this._stream.colors[runStart];
      const size = this._stream.sizes[runStart];
      const orientation = this._stream.orientations[runStart];
      const orientationLength = this._stream.orientationLengths[runStart];
      const runEnd = this.findRunEnd(runStart, count, colorKey, size, orientation, orientationLength);
      if (orientationLength > 0) {
        const color = this.resolveColor(colorKey);
      const dx = Math.cos(orientation) * orientationLength;
      const dy = Math.sin(orientation) * orientationLength;
      context.beginPath();
        for (let index = runStart; index < runEnd; index++) {
        const x = this._stream.screenX[index];
        const y = this._stream.screenY[index];
        context.moveTo(x, y);
        context.lineTo(x + dx, y + dy);
      }
      context.lineCap = 'round';
      context.strokeStyle = this.resolveShadowColor(colorKey);
      context.lineWidth = Math.max(2, size * 0.3);
      context.stroke();
      context.strokeStyle = color;
      context.lineWidth = Math.max(1, size * 0.16);
      context.stroke();
      }
      runStart = runEnd;
    }
  }

  private drawMarkerLayer(context: CanvasRenderingContext2D, count: number): void {
    let runStart = 0;
    while (runStart < count) {
      const colorKey = this._stream.colors[runStart];
      const size = this._stream.sizes[runStart];
      const orientation = this._stream.orientations[runStart];
      const orientationLength = this._stream.orientationLengths[runStart];
      const runEnd = this.findRunEnd(runStart, count, colorKey, size, orientation, orientationLength);
      const radius = Math.max(0.5, size * 0.5);
      context.beginPath();
      for (let index = runStart; index < runEnd; index++) {
        const x = this._stream.screenX[index];
        const y = this._stream.screenY[index];
        context.moveTo(x + radius, y);
        context.arc(x, y, radius, 0, Math.PI * 2);
      }
      context.fillStyle = this.resolveColor(colorKey);
      context.fill();
      runStart = runEnd;
    }
  }

  private findRunEnd(
    start: number,
    count: number,
    color: number,
    size: number,
    orientation: number,
    orientationLength: number,
  ): number {
    let end = start + 1;
    while (end < count &&
           this._stream.colors[end] === color &&
           this._stream.sizes[end] === size &&
           this._stream.orientations[end] === orientation &&
           this._stream.orientationLengths[end] === orientationLength) {
      end++;
    }
    return end;
  }

  private applyClip(context: CanvasRenderingContext2D): void {
    const kind = this._stream.clipKind;
    const x = this._stream.clipX;
    const y = this._stream.clipY;
    const width = this._stream.clipWidth;
    const height = this._stream.clipHeight;
    if (kind === 0 || width <= 0 || height <= 0) {
      return;
    }

    context.beginPath();
    if (kind === 1) {
      context.rect(x, y, width, height);
    } else if (kind === 2) {
      context.ellipse(x + width * 0.5, y + height * 0.5, width * 0.5, height * 0.5, 0, 0, Math.PI * 2);
    } else {
      context.moveTo(x + width * 0.5, y);
      context.lineTo(x + width, y + height * 0.5);
      context.lineTo(x + width * 0.5, y + height);
      context.lineTo(x, y + height * 0.5);
      context.closePath();
    }
    context.clip();
  }

  private resolveColor(colorKey: number): string {
    const cached = this._colorCache.get(colorKey);
    if (cached) {
      return cached;
    }

    const alpha = ((colorKey >>> 24) & 0xff) / 255;
    const red = (colorKey >>> 16) & 0xff;
    const green = (colorKey >>> 8) & 0xff;
    const blue = colorKey & 0xff;
    const color = `rgba(${red},${green},${blue},${alpha.toFixed(3)})`;
    this._colorCache.set(colorKey, color);
    return color;
  }

  private resolveShadowColor(colorKey: number): string {
    const cached = this._shadowColorCache.get(colorKey);
    if (cached) {
      return cached;
    }

    const alpha = (((colorKey >>> 24) & 0xff) / 255) * (210 / 255);
    const color = `rgba(0,0,0,${alpha.toFixed(3)})`;
    this._shadowColorCache.set(colorKey, color);
    return color;
  }
}
