import type { UiGradientPayload, UiRect, UiSceneNode, UiScenePayload, UiSceneStyle, UiThickness } from '../core/UiSceneTypes';

const TEXT_ALIGN_MAP: Record<string, CanvasTextAlign> = {
  Left: 'left',
  Center: 'center',
  Right: 'right',
};

export class UiSceneOverlay {
  private readonly _canvas: HTMLCanvasElement;
  private readonly _context: CanvasRenderingContext2D;
  private readonly _imageCache = new Map<string, HTMLImageElement>();
  private _scene: UiScenePayload | null = null;
  private _lastVersion = -1;
  private _lastViewportWidth = 0;
  private _lastViewportHeight = 0;
  private _lastRootPresent = false;
  private _pixelRatio = 1;

  constructor(canvas: HTMLCanvasElement) {
    this._canvas = canvas;
    this._canvas.style.pointerEvents = 'none';
    const context = this._canvas.getContext('2d');
    if (!context) {
      throw new Error('Failed to acquire 2D context for UiScene overlay.');
    }

    this._context = context;
    this.resize(window.innerWidth, window.innerHeight);
  }

  clear(): void {
    this._scene = null;
    this._lastVersion = -1;
    this._lastViewportWidth = 0;
    this._lastViewportHeight = 0;
    this._lastRootPresent = false;
    this.clearCanvas();
  }

  resize(width: number, height: number): void {
    const safeWidth = Math.max(1, Math.round(width));
    const safeHeight = Math.max(1, Math.round(height));
    const pixelRatio = Math.max(1, window.devicePixelRatio || 1);
    const backingWidth = Math.max(1, Math.round(safeWidth * pixelRatio));
    const backingHeight = Math.max(1, Math.round(safeHeight * pixelRatio));

    if (this._canvas.width === backingWidth &&
        this._canvas.height === backingHeight &&
        this._pixelRatio === pixelRatio)
    {
      return;
    }

    this._pixelRatio = pixelRatio;
    this._canvas.width = backingWidth;
    this._canvas.height = backingHeight;
    this._canvas.style.width = `${safeWidth}px`;
    this._canvas.style.height = `${safeHeight}px`;
    this._context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
    this.redraw();
  }

  update(scene: UiScenePayload | undefined): void {
    if (!scene) {
      return;
    }

    const rootPresent = scene.root !== null;
    const viewportChanged =
      scene.viewportWidth !== this._lastViewportWidth ||
      scene.viewportHeight !== this._lastViewportHeight;
    const sceneChanged =
      scene.version !== this._lastVersion ||
      rootPresent !== this._lastRootPresent;

    if (!viewportChanged && !sceneChanged) {
      return;
    }

    this._scene = scene;
    this._lastVersion = scene.version;
    this._lastViewportWidth = scene.viewportWidth;
    this._lastViewportHeight = scene.viewportHeight;
    this._lastRootPresent = rootPresent;

    this.resize(scene.viewportWidth, scene.viewportHeight);
    this.redraw();
  }

  private redraw(): void {
    this.clearCanvas();
    if (!this._scene?.root) {
      return;
    }

    this.renderNode(this._scene.root, 0, 0);
  }

  private clearCanvas(): void {
    this._context.save();
    this._context.setTransform(1, 0, 0, 1, 0, 0);
    this._context.clearRect(0, 0, this._canvas.width, this._canvas.height);
    this._context.restore();
  }

  private renderNode(node: UiSceneNode, parentX: number, parentY: number): void {
    const style = node.style;
    if (style.display === 'None' || !style.visible || node.layoutRect.width <= 0 || node.layoutRect.height <= 0) {
      return;
    }

    const localX = node.layoutRect.x - parentX;
    const localY = node.layoutRect.y - parentY;
    const width = node.layoutRect.width;
    const height = node.layoutRect.height;

    this._context.save();
    this._context.translate(localX, localY);
    this.applyTransform(style.transform, width, height);
    this._context.globalAlpha *= style.opacity;
    this._context.filter = style.filterBlurRadius > 0 ? `blur(${style.filterBlurRadius}px)` : 'none';

    this.drawBoxShadow(style, width, height);
    this.drawBackground(style, width, height);
    this.drawBorder(style, width, height);
    this.drawOutline(style, width, height);

    const clipsContent = style.clipContent || style.overflow === 'Scroll';
    if (clipsContent) {
      this._context.save();
      this.beginRectPath(0, 0, width, height, style.borderRadius);
      this._context.clip();
    }

    if (style.overflow === 'Scroll') {
      this._context.translate(-node.scrollOffsetX, -node.scrollOffsetY);
    }

    this.drawText(node, style, width, height);
    this.drawImage(node, style, width, height);

    for (const child of this.orderChildren(node.children)) {
      this.renderNode(child, node.layoutRect.x, node.layoutRect.y);
    }

    if (clipsContent) {
      this._context.restore();
    }

    if (style.overflow === 'Scroll') {
      this.drawScrollbars(node, style, width, height);
    }

    this._context.restore();
  }

  private drawBackground(style: UiSceneStyle, width: number, height: number): void {
    if (!this.isVisibleColor(style.backgroundColor) && !style.backgroundGradient) {
      return;
    }

    this._context.save();
    this.beginRectPath(0, 0, width, height, style.borderRadius);

    if (this.isVisibleColor(style.backgroundColor)) {
      this._context.fillStyle = style.backgroundColor;
      this._context.fill();
    }

    if (style.backgroundGradient) {
      this._context.fillStyle = this.createGradient(style.backgroundGradient, width, height);
      this._context.fill();
    }

    this._context.restore();
  }

  private drawBorder(style: UiSceneStyle, width: number, height: number): void {
    if (style.borderWidth <= 0 || !this.isVisibleColor(style.borderColor)) {
      return;
    }

    this._context.save();
    this._context.lineWidth = style.borderWidth;
    this._context.strokeStyle = style.borderColor;
    this.beginRectPath(
      style.borderWidth * 0.5,
      style.borderWidth * 0.5,
      Math.max(0, width - style.borderWidth),
      Math.max(0, height - style.borderWidth),
      Math.max(0, style.borderRadius - style.borderWidth * 0.5),
    );
    this._context.stroke();
    this._context.restore();
  }

  private drawOutline(style: UiSceneStyle, width: number, height: number): void {
    if (style.outlineWidth <= 0 || !this.isVisibleColor(style.outlineColor)) {
      return;
    }

    this._context.save();
    this._context.lineWidth = style.outlineWidth;
    this._context.strokeStyle = style.outlineColor;
    const inset = style.outlineWidth * 0.5;
    this.beginRectPath(-inset, -inset, width + style.outlineWidth, height + style.outlineWidth, style.borderRadius + inset);
    this._context.stroke();
    this._context.restore();
  }

  private drawBoxShadow(style: UiSceneStyle, width: number, height: number): void {
    if (!style.boxShadow) {
      return;
    }

    const shadow = style.boxShadow;
    this._context.save();
    this._context.shadowColor = shadow.color;
    this._context.shadowBlur = shadow.blurRadius;
    this._context.shadowOffsetX = shadow.offsetX;
    this._context.shadowOffsetY = shadow.offsetY;
    this._context.fillStyle = shadow.color;
    this.beginRectPath(
      -shadow.spreadRadius,
      -shadow.spreadRadius,
      width + shadow.spreadRadius * 2,
      height + shadow.spreadRadius * 2,
      style.borderRadius + shadow.spreadRadius,
    );
    this._context.fill();
    this._context.restore();
  }

  private drawText(node: UiSceneNode, style: UiSceneStyle, width: number, height: number): void {
    const text = node.textContent?.trim();
    if (!text) {
      return;
    }

    const contentRect = this.getContentRect(width, height, style.padding);
    if (contentRect.width <= 0 || contentRect.height <= 0) {
      return;
    }

    this._context.save();
    this._context.fillStyle = style.color;
    this._context.font = `${style.bold ? '700' : '400'} ${style.fontSize}px ${style.fontFamily ?? 'sans-serif'}`;
    this._context.textBaseline = 'top';
    this._context.textAlign = this.resolveTextAlign(style);
    this._context.direction = style.direction === 'Rtl' ? 'rtl' : 'ltr';

    if (style.textShadow) {
      this._context.shadowColor = style.textShadow.color;
      this._context.shadowBlur = style.textShadow.blurRadius;
      this._context.shadowOffsetX = style.textShadow.offsetX;
      this._context.shadowOffsetY = style.textShadow.offsetY;
    }

    const lines = this.layoutTextLines(text, contentRect.width, style);
    const lineHeight = style.fontSize * 1.2;
    let baselineY = contentRect.y;
    for (const line of lines) {
      if (baselineY + lineHeight > contentRect.y + contentRect.height + 0.01) {
        break;
      }

      this._context.fillText(line, this.resolveTextAnchorX(contentRect, this._context.textAlign), baselineY);
      baselineY += lineHeight;
    }

    this._context.restore();
  }

  private drawImage(node: UiSceneNode, style: UiSceneStyle, width: number, height: number): void {
    if (!node.imageSource) {
      return;
    }

    const image = this.getImage(node.imageSource);
    if (!image || !image.complete || image.naturalWidth <= 0 || image.naturalHeight <= 0) {
      return;
    }

    const contentRect = this.getContentRect(width, height, style.padding);
    if (contentRect.width <= 0 || contentRect.height <= 0) {
      return;
    }

    const destination = this.resolveObjectFitRect(
      contentRect,
      image.naturalWidth,
      image.naturalHeight,
      style.objectFit,
    );

    this._context.save();
    this.beginRectPath(contentRect.x, contentRect.y, contentRect.width, contentRect.height, style.borderRadius);
    this._context.clip();
    this._context.drawImage(image, destination.x, destination.y, destination.width, destination.height);
    this._context.restore();
  }

  private drawScrollbars(node: UiSceneNode, style: UiSceneStyle, width: number, height: number): void {
    const hasVertical = node.scrollContentHeight > height + 0.01;
    const hasHorizontal = node.scrollContentWidth > width + 0.01;
    if (!hasVertical && !hasHorizontal) {
      return;
    }

    const trackColor = this.promoteAlpha(style.borderColor, 0.28, 'rgba(255,255,255,0.22)');
    const thumbBase = this.isVisibleColor(style.outlineColor) ? style.outlineColor : style.color;
    const thumbColor = this.promoteAlpha(thumbBase, 0.7, 'rgba(255,255,255,0.7)');

    this._context.save();
    this._context.fillStyle = trackColor;
    this._context.strokeStyle = trackColor;

    if (hasVertical) {
      const trackHeight = Math.max(0, height - 4 - (hasHorizontal ? 10 : 0));
      const trackX = width - 12;
      const trackY = 2;
      this.fillRoundedRect(trackX, trackY, 10, trackHeight, 5, trackColor);

      const thumbHeight = Math.min(trackHeight, Math.max(18, trackHeight * (height / Math.max(height, node.scrollContentHeight))));
      const scrollRange = Math.max(0, node.scrollContentHeight - height);
      const thumbRange = Math.max(0, trackHeight - thumbHeight);
      const thumbY = trackY + (scrollRange <= 0 ? 0 : (node.scrollOffsetY / scrollRange) * thumbRange);
      this.fillRoundedRect(trackX, thumbY, 10, thumbHeight, 5, thumbColor);
    }

    if (hasHorizontal) {
      const trackWidth = Math.max(0, width - 4 - (hasVertical ? 10 : 0));
      const trackX = 2;
      const trackY = height - 12;
      this.fillRoundedRect(trackX, trackY, trackWidth, 10, 5, trackColor);

      const thumbWidth = Math.min(trackWidth, Math.max(18, trackWidth * (width / Math.max(width, node.scrollContentWidth))));
      const scrollRange = Math.max(0, node.scrollContentWidth - width);
      const thumbRange = Math.max(0, trackWidth - thumbWidth);
      const thumbX = trackX + (scrollRange <= 0 ? 0 : (node.scrollOffsetX / scrollRange) * thumbRange);
      this.fillRoundedRect(thumbX, trackY, thumbWidth, 10, 5, thumbColor);
    }

    this._context.restore();
  }

  private layoutTextLines(text: string, maxWidth: number, style: UiSceneStyle): string[] {
    const segments =
      style.whiteSpace === 'PreWrap'
        ? text.replace(/\r\n/g, '\n').split('\n')
        : [text];
    const lines: string[] = [];

    for (const segment of segments) {
      if (style.whiteSpace === 'NoWrap') {
        lines.push(this.normalizeWhitespace(segment));
        continue;
      }

      this.wrapSegment(segment, maxWidth, lines);
    }

    return lines.length > 0 ? lines : [''];
  }

  private wrapSegment(segment: string, maxWidth: number, lines: string[]): void {
    const normalized = this.normalizeWhitespace(segment);
    if (!normalized) {
      lines.push('');
      return;
    }

    if (maxWidth <= 0) {
      lines.push(normalized);
      return;
    }

    const words = normalized.split(' ');
    let currentLine = '';
    for (const word of words) {
      const parts = this.breakLongWord(word, maxWidth);
      for (let index = 0; index < parts.length; index++) {
        const part = parts[index];
        if (index > 0 && currentLine) {
          lines.push(currentLine);
          currentLine = '';
        }

        const candidate = currentLine ? `${currentLine} ${part}` : part;
        if (this._context.measureText(candidate).width <= maxWidth || !currentLine) {
          currentLine = candidate;
          continue;
        }

        lines.push(currentLine);
        currentLine = part;
      }
    }

    if (currentLine) {
      lines.push(currentLine);
    }
  }

  private breakLongWord(word: string, maxWidth: number): string[] {
    if (this._context.measureText(word).width <= maxWidth) {
      return [word];
    }

    const parts: string[] = [];
    let current = '';
    for (const char of word) {
      const candidate = current + char;
      if (current && this._context.measureText(candidate).width > maxWidth) {
        parts.push(current);
        current = char;
        continue;
      }

      current = candidate;
    }

    if (current) {
      parts.push(current);
    }

    return parts.length > 0 ? parts : [word];
  }

  private normalizeWhitespace(text: string): string {
    return text.replace(/\s+/g, ' ').trim();
  }

  private orderChildren(children: UiSceneNode[]): UiSceneNode[] {
    return children
      .map((child, index) => ({ child, index }))
      .sort((a, b) => a.child.style.zIndex - b.child.style.zIndex || a.index - b.index)
      .map((entry) => entry.child);
  }

  private getContentRect(width: number, height: number, padding: UiThickness): UiRect {
    return {
      x: padding.left,
      y: padding.top,
      width: Math.max(0, width - padding.left - padding.right),
      height: Math.max(0, height - padding.top - padding.bottom),
    };
  }

  private resolveTextAlign(style: UiSceneStyle): CanvasTextAlign {
    if (style.textAlign === 'Center') {
      return 'center';
    }

    if (style.textAlign === 'Right') {
      return 'right';
    }

    if (style.textAlign === 'End') {
      return style.direction === 'Rtl' ? 'left' : 'right';
    }

    if (style.textAlign === 'Start') {
      return style.direction === 'Rtl' ? 'right' : 'left';
    }

    return TEXT_ALIGN_MAP[style.textAlign] ?? 'left';
  }

  private resolveTextAnchorX(rect: UiRect, textAlign: CanvasTextAlign): number {
    if (textAlign === 'center') {
      return rect.x + rect.width * 0.5;
    }

    if (textAlign === 'right' || textAlign === 'end') {
      return rect.x + rect.width;
    }

    return rect.x;
  }

  private resolveObjectFitRect(rect: UiRect, sourceWidth: number, sourceHeight: number, objectFit: string): UiRect {
    if (objectFit === 'Fill') {
      return rect;
    }

    const scaleX = rect.width / Math.max(1, sourceWidth);
    const scaleY = rect.height / Math.max(1, sourceHeight);
    const scale =
      objectFit === 'Cover'
        ? Math.max(scaleX, scaleY)
        : objectFit === 'None'
          ? 1
          : objectFit === 'ScaleDown' && sourceWidth <= rect.width && sourceHeight <= rect.height
            ? 1
            : Math.min(scaleX, scaleY);

    const drawWidth = sourceWidth * scale;
    const drawHeight = sourceHeight * scale;
    return {
      x: rect.x + (rect.width - drawWidth) * 0.5,
      y: rect.y + (rect.height - drawHeight) * 0.5,
      width: drawWidth,
      height: drawHeight,
    };
  }

  private createGradient(gradient: UiGradientPayload, width: number, height: number): CanvasGradient {
    const radians = (gradient.angleDegrees * Math.PI) / 180;
    const centerX = width * 0.5;
    const centerY = height * 0.5;
    const directionX = Math.cos(radians);
    const directionY = Math.sin(radians);
    const extent = Math.max(width, height);
    const gradientBrush = this._context.createLinearGradient(
      centerX - directionX * extent,
      centerY - directionY * extent,
      centerX + directionX * extent,
      centerY + directionY * extent,
    );

    for (const stop of gradient.stops) {
      gradientBrush.addColorStop(Math.max(0, Math.min(1, stop.position)), stop.color);
    }

    return gradientBrush;
  }

  private beginRectPath(x: number, y: number, width: number, height: number, radius: number): void {
    const safeWidth = Math.max(0, width);
    const safeHeight = Math.max(0, height);
    const safeRadius = Math.max(0, Math.min(radius, safeWidth * 0.5, safeHeight * 0.5));

    this._context.beginPath();
    if (safeRadius <= 0.01) {
      this._context.rect(x, y, safeWidth, safeHeight);
      return;
    }

    this._context.moveTo(x + safeRadius, y);
    this._context.lineTo(x + safeWidth - safeRadius, y);
    this._context.quadraticCurveTo(x + safeWidth, y, x + safeWidth, y + safeRadius);
    this._context.lineTo(x + safeWidth, y + safeHeight - safeRadius);
    this._context.quadraticCurveTo(x + safeWidth, y + safeHeight, x + safeWidth - safeRadius, y + safeHeight);
    this._context.lineTo(x + safeRadius, y + safeHeight);
    this._context.quadraticCurveTo(x, y + safeHeight, x, y + safeHeight - safeRadius);
    this._context.lineTo(x, y + safeRadius);
    this._context.quadraticCurveTo(x, y, x + safeRadius, y);
    this._context.closePath();
  }

  private fillRoundedRect(x: number, y: number, width: number, height: number, radius: number, color: string): void {
    this._context.save();
    this._context.fillStyle = color;
    this.beginRectPath(x, y, width, height, radius);
    this._context.fill();
    this._context.restore();
  }

  private applyTransform(transformText: string, width: number, height: number): void {
    if (!transformText || transformText === 'none') {
      return;
    }

    const operations = [...transformText.matchAll(/([a-zA-Z]+)\(([^)]+)\)/g)];
    if (operations.length === 0) {
      return;
    }

    const pivotX = width * 0.5;
    const pivotY = height * 0.5;
    for (const operation of operations) {
      const op = operation[1].toLowerCase();
      const values = operation[2].split(',').map((value) => value.trim());
      if (op === 'translate') {
        const x = this.resolveLength(values[0], width);
        const y = this.resolveLength(values[1] ?? '0px', height);
        this._context.translate(x, y);
        continue;
      }

      if (op === 'scale') {
        const scaleX = Number.parseFloat(values[0] ?? '1');
        const scaleY = Number.parseFloat(values[1] ?? values[0] ?? '1');
        this._context.translate(pivotX, pivotY);
        this._context.scale(scaleX, scaleY);
        this._context.translate(-pivotX, -pivotY);
        continue;
      }

      if (op === 'rotate') {
        const degrees = Number.parseFloat(values[0]?.replace('deg', '') ?? '0');
        this._context.translate(pivotX, pivotY);
        this._context.rotate((degrees * Math.PI) / 180);
        this._context.translate(-pivotX, -pivotY);
      }
    }
  }

  private resolveLength(value: string, available: number): number {
    if (!value) {
      return 0;
    }

    if (value.endsWith('%')) {
      const percent = Number.parseFloat(value.slice(0, -1));
      return Number.isFinite(percent) ? available * (percent / 100) : 0;
    }

    if (value.endsWith('px')) {
      const px = Number.parseFloat(value.slice(0, -2));
      return Number.isFinite(px) ? px : 0;
    }

    const numeric = Number.parseFloat(value);
    return Number.isFinite(numeric) ? numeric : 0;
  }

  private getImage(source: string): HTMLImageElement | undefined {
    let image = this._imageCache.get(source);
    if (!image) {
      image = new Image();
      image.decoding = 'async';
      image.src = source;
      image.addEventListener('load', () => this.redraw());
      image.addEventListener('error', () => this.redraw());
      this._imageCache.set(source, image);
    }

    return image;
  }

  private isVisibleColor(color: string): boolean {
    const rgbaMatch = color.match(/rgba\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*([0-9.]+)\s*\)/i);
    if (rgbaMatch) {
      return Number.parseFloat(rgbaMatch[1]) > 0.001;
    }

    return color !== 'transparent';
  }

  private promoteAlpha(color: string, minimumAlpha: number, fallback: string): string {
    const rgbaMatch = color.match(/rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([0-9.]+)\s*\)/i);
    if (!rgbaMatch) {
      return fallback;
    }

    const alpha = Math.max(minimumAlpha, Number.parseFloat(rgbaMatch[4]));
    return `rgba(${rgbaMatch[1]},${rgbaMatch[2]},${rgbaMatch[3]},${alpha})`;
  }
}
