using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed class UiSceneRenderer
{
    public void Render(UiScene scene, SKCanvas canvas, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(canvas);

        scene.Layout(width, height);
        if (scene.Root == null)
        {
            return;
        }

        int surfaceWidth = Math.Max(1, (int)Math.Ceiling(width));
        int surfaceHeight = Math.Max(1, (int)Math.Ceiling(height));
        using SKSurface surface = SKSurface.Create(new SKImageInfo(surfaceWidth, surfaceHeight));
        SKCanvas surfaceCanvas = surface.Canvas;
        surfaceCanvas.Clear(SKColors.Transparent);
        RenderNode(scene.Root, surfaceCanvas, surface);
        using SKImage image = surface.Snapshot();
        canvas.DrawImage(image, 0f, 0f);
    }

    public void ExportPng(UiScene scene, string outputPath, int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);
        Render(scene, canvas, width, height);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(file);
    }

    private void RenderNode(UiNode node, SKCanvas canvas, SKSurface surface)
    {
        UiStyle style = node.RenderStyle;
        if (!style.Visible || style.Display == UiDisplay.None)
        {
            return;
        }

        if (style.FilterBlurRadius > 0.01f)
        {
            RenderFilteredNode(node, canvas);
            return;
        }

        RenderNodeCore(node, canvas, surface);
    }

    private void RenderNodeCore(UiNode node, SKCanvas canvas, SKSurface surface)
    {
        UiStyle style = node.RenderStyle;
        if (!style.Visible || style.Display == UiDisplay.None)
        {
            return;
        }

        SKRect rect = new(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Right, node.LayoutRect.Bottom);
        bool clipContent = style.ClipContent || style.Overflow == UiOverflow.Scroll;
        int nodeSaveCount = canvas.Save();
        int layerSaveCount = -1;
        int maskLayerSaveCount = -1;
        int clipPathSaveCount = -1;
        int contentSaveCount = -1;

        if (style.Transform.HasOperations)
        {
            SKMatrix transform = UiTransformMath.CreateMatrix(style, node.LayoutRect);
            canvas.Concat(ref transform);
        }

        if (style.Opacity < 1f)
        {
            layerSaveCount = canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(style.Opacity * 255f, 0f, 255f)) });
        }

        if (style.MaskGradient != null)
        {
            maskLayerSaveCount = canvas.SaveLayer();
        }

        if (style.ClipPath != null)
        {
            clipPathSaveCount = canvas.Save();
            ClipNodeBounds(canvas, rect, style);
        }

        DrawBackdropBlur(canvas, surface, rect, style);

        DrawBoxShadows(canvas, rect, style);
        DrawBackgrounds(canvas, rect, style);

        if (style.BorderWidth > 0f && style.BorderColor != SKColors.Transparent)
        {
            using SKPaint stroke = CreateBorderPaint(style);
            DrawRect(canvas, rect, style.BorderRadius, stroke);
        }

        if (style.OutlineWidth > 0f && style.OutlineColor != SKColors.Transparent)
        {
            using SKPaint outline = new()
            {
                Color = style.OutlineColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = style.OutlineWidth
            };

            SKRect outlineRect = rect;
            outlineRect.Inflate(style.OutlineWidth * 0.5f, style.OutlineWidth * 0.5f);
            DrawRect(canvas, outlineRect, style.BorderRadius + (style.OutlineWidth * 0.5f), outline);
        }

        if (clipContent)
        {
            contentSaveCount = canvas.Save();
            ClipNodeBounds(canvas, rect, style);
            if (style.Overflow == UiOverflow.Scroll)
            {
                canvas.Translate(-node.ScrollOffsetX, -node.ScrollOffsetY);
            }
        }

        string? renderText = ResolveRenderableText(node);
        if (!string.IsNullOrWhiteSpace(renderText))
        {
            DrawText(renderText, rect, style, canvas);
        }

        if (node.Kind == UiNodeKind.Image || string.Equals(node.TagName, "img", StringComparison.OrdinalIgnoreCase))
        {
            DrawImage(node, rect, style, canvas);
        }

        if (string.Equals(node.TagName, "canvas", StringComparison.OrdinalIgnoreCase))
        {
            DrawCanvas(node, rect, style, canvas);
        }

        foreach (UiNode child in UiVisualTreeOrdering.BackToFront(node.Children))
        {
            RenderNode(child, canvas, surface);
        }

        if (contentSaveCount >= 0)
        {
            canvas.RestoreToCount(contentSaveCount);
        }

        if (style.Overflow == UiOverflow.Scroll)
        {
            DrawScrollbars(canvas, node);
        }

        if (clipPathSaveCount >= 0)
        {
            canvas.RestoreToCount(clipPathSaveCount);
        }

        if (maskLayerSaveCount >= 0)
        {
            DrawMask(canvas, rect, style);
            canvas.RestoreToCount(maskLayerSaveCount);
        }

        if (layerSaveCount >= 0)
        {
            canvas.RestoreToCount(layerSaveCount);
        }

        canvas.RestoreToCount(nodeSaveCount);
    }

    private void RenderFilteredNode(UiNode node, SKCanvas canvas)
    {
        UiStyle style = node.RenderStyle;
        SKRect rect = new(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Right, node.LayoutRect.Bottom);
        int padding = (int)Math.Ceiling(Math.Max(2f, style.FilterBlurRadius * 3f));
        int left = (int)Math.Floor(rect.Left) - padding;
        int top = (int)Math.Floor(rect.Top) - padding;
        int width = Math.Max(1, (int)Math.Ceiling(rect.Width) + (padding * 2));
        int height = Math.Max(1, (int)Math.Ceiling(rect.Height) + (padding * 2));

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas layerCanvas = surface.Canvas;
        layerCanvas.Clear(SKColors.Transparent);
        layerCanvas.Translate(-left, -top);
        RenderNodeCore(node, layerCanvas, surface);

        using SKImage bitmap = surface.Snapshot();

        using SKPaint blurPaint = new()
        {
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(ToSigma(style.FilterBlurRadius), ToSigma(style.FilterBlurRadius))
        };

        canvas.DrawImage(bitmap, left, top, blurPaint);
    }

    private static void DrawRect(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
    {
        if (radius > 0f)
        {
            canvas.DrawRoundRect(rect, radius, radius, paint);
            return;
        }

        canvas.DrawRect(rect, paint);
    }

    private static void DrawBackgrounds(SKCanvas canvas, SKRect rect, UiStyle style)
    {
        if (style.BackgroundColor != SKColors.Transparent)
        {
            using SKPaint fill = new() { Color = style.BackgroundColor, IsAntialias = true, Style = SKPaintStyle.Fill };
            DrawRect(canvas, rect, style.BorderRadius, fill);
        }

        if (style.BackgroundLayers.Count > 0)
        {
            for (int layerIndex = style.BackgroundLayers.Count - 1; layerIndex >= 0; layerIndex--)
            {
                UiBackgroundLayer layer = style.BackgroundLayers[layerIndex];
                if (!layer.IsVisible)
                {
                    continue;
                }

                if (layer.Color != SKColors.Transparent)
                {
                    using SKPaint fill = new() { Color = layer.Color, IsAntialias = true, Style = SKPaintStyle.Fill };
                    DrawRect(canvas, rect, style.BorderRadius, fill);
                }

                if (layer.Gradient != null)
                {
                    using SKPaint gradientFill = new()
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill,
                        Shader = CreateGradientShader(rect, layer.Gradient)
                    };
                    DrawRect(canvas, rect, style.BorderRadius, gradientFill);
                }

                if (!string.IsNullOrWhiteSpace(layer.ImageSource))
                {
                    DrawBackgroundImageLayer(canvas, rect, style, layer.ImageSource, layerIndex);
                }
            }

            return;
        }

        if (style.BackgroundGradient != null)
        {
            using SKPaint gradientFill = new()
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = CreateGradientShader(rect, style.BackgroundGradient)
            };
            DrawRect(canvas, rect, style.BorderRadius, gradientFill);
        }
    }

    private static SKPaint CreateBorderPaint(UiStyle style)
    {
        SKPaint paint = new()
        {
            Color = style.BorderColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = style.BorderWidth
        };

        switch (style.BorderStyle)
        {
            case UiBorderStyle.Dashed:
                paint.PathEffect = SKPathEffect.CreateDash(
                    new[] { Math.Max(4f, style.BorderWidth * 3f), Math.Max(2f, style.BorderWidth * 2f) },
                    0f);
                break;
            case UiBorderStyle.Dotted:
                paint.StrokeCap = SKStrokeCap.Round;
                paint.PathEffect = SKPathEffect.CreateDash(
                    new[] { Math.Max(1f, style.BorderWidth), Math.Max(2f, style.BorderWidth * 1.8f) },
                    0f);
                break;
        }

        return paint;
    }

    private static void DrawBoxShadows(SKCanvas canvas, SKRect rect, UiStyle style)
    {
        IReadOnlyList<UiShadow> shadows = style.BoxShadows.Count > 0
            ? style.BoxShadows
            : (style.BoxShadow != null ? new[] { style.BoxShadow.Value } : Array.Empty<UiShadow>());
        if (shadows.Count == 0)
        {
            return;
        }

        for (int shadowIndex = 0; shadowIndex < shadows.Count; shadowIndex++)
        {
            UiShadow shadow = shadows[shadowIndex];
            if (!shadow.IsVisible)
            {
                continue;
            }

            using SKPaint shadowPaint = new()
            {
                Color = shadow.Color,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                ImageFilter = shadow.BlurRadius > 0.01f ? SKImageFilter.CreateBlur(ToSigma(shadow.BlurRadius), ToSigma(shadow.BlurRadius)) : null
            };

            SKRect shadowRect = rect;
            shadowRect.Offset(shadow.OffsetX, shadow.OffsetY);
            shadowRect.Inflate(shadow.SpreadRadius, shadow.SpreadRadius);
            DrawRect(canvas, shadowRect, style.BorderRadius + shadow.SpreadRadius, shadowPaint);
        }
    }

    private static void DrawBackdropBlur(SKCanvas canvas, SKSurface surface, SKRect rect, UiStyle style)
    {
        if (style.BackdropBlurRadius <= 0.01f || rect.Width <= 0.01f || rect.Height <= 0.01f)
        {
            return;
        }

        int left = (int)Math.Floor(rect.Left);
        int top = (int)Math.Floor(rect.Top);
        int width = Math.Max(1, (int)Math.Ceiling(rect.Width));
        int height = Math.Max(1, (int)Math.Ceiling(rect.Height));

        SKRectI requestedBounds = new(left, top, left + width, top + height);
        SKRectI surfaceBounds = surface.Canvas.DeviceClipBounds;
        int clippedLeft = Math.Max(requestedBounds.Left, surfaceBounds.Left);
        int clippedTop = Math.Max(requestedBounds.Top, surfaceBounds.Top);
        int clippedRight = Math.Min(requestedBounds.Right, surfaceBounds.Right);
        int clippedBottom = Math.Min(requestedBounds.Bottom, surfaceBounds.Bottom);
        if (clippedLeft >= clippedRight || clippedTop >= clippedBottom)
        {
            return;
        }

        requestedBounds = new SKRectI(clippedLeft, clippedTop, clippedRight, clippedBottom);

        using SKImage? snapshot = surface.Snapshot(requestedBounds);
        if (snapshot == null)
        {
            return;
        }

        int saveCount = canvas.Save();
        ClipNodeBounds(canvas, rect, style);

        using SKPaint blurPaint = new()
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Src,
            ImageFilter = SKImageFilter.CreateBlur(ToSigma(style.BackdropBlurRadius), ToSigma(style.BackdropBlurRadius))
        };

        canvas.DrawImage(snapshot, requestedBounds.Left, requestedBounds.Top, blurPaint);
        canvas.RestoreToCount(saveCount);
    }

    private static void DrawMask(SKCanvas canvas, SKRect rect, UiStyle style)
    {
        if (style.MaskGradient == null)
        {
            return;
        }

        using SKPaint maskPaint = new()
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.DstIn,
            Shader = CreateGradientShader(rect, style.MaskGradient)
        };

        using SKPath path = CreateClipPath(rect, style);
        canvas.DrawPath(path, maskPaint);
    }
    private static void DrawScrollbars(SKCanvas canvas, UiNode node)
    {
        if (node.Style.Overflow != UiOverflow.Scroll)
        {
            return;
        }

        UiStyle style = node.RenderStyle;
        SKColor trackColor = style.BorderColor.Alpha > 0
            ? style.BorderColor.WithAlpha((byte)Math.Max((int)style.BorderColor.Alpha, 72))
            : new SKColor(255, 255, 255, 56);
        SKColor thumbColor = style.OutlineColor.Alpha > 0
            ? style.OutlineColor.WithAlpha((byte)Math.Max((int)style.OutlineColor.Alpha, 172))
            : (style.Color.Alpha > 0 ? style.Color.WithAlpha(180) : new SKColor(255, 255, 255, 180));

        using SKPaint trackPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = trackColor };
        using SKPaint thumbPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = thumbColor };

        if (UiScrollGeometry.HasVerticalScrollbar(node))
        {
            DrawRect(canvas, ToSkRect(UiScrollGeometry.GetVerticalTrackRect(node)), UiScrollGeometry.ScrollbarThickness * 0.5f, trackPaint);
            DrawRect(canvas, ToSkRect(UiScrollGeometry.GetVerticalThumbRect(node)), UiScrollGeometry.ScrollbarThickness * 0.5f, thumbPaint);
        }

        if (UiScrollGeometry.HasHorizontalScrollbar(node))
        {
            DrawRect(canvas, ToSkRect(UiScrollGeometry.GetHorizontalTrackRect(node)), UiScrollGeometry.ScrollbarThickness * 0.5f, trackPaint);
            DrawRect(canvas, ToSkRect(UiScrollGeometry.GetHorizontalThumbRect(node)), UiScrollGeometry.ScrollbarThickness * 0.5f, thumbPaint);
        }
    }

    private static SKRect ToSkRect(UiRect rect)
    {
        return new SKRect(rect.X, rect.Y, rect.Right, rect.Bottom);
    }

    private static string? ResolveRenderableText(UiNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            return node.TextContent;
        }

        if (node.Kind is UiNodeKind.Input or UiNodeKind.Select or UiNodeKind.TextArea)
        {
            string? value = node.Attributes["value"];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string? placeholder = node.Attributes["placeholder"];
            if (!string.IsNullOrWhiteSpace(placeholder))
            {
                return placeholder;
            }
        }

        return null;
    }

    private static void DrawText(string text, SKRect rect, UiStyle style, SKCanvas canvas)
    {
        float availableWidth = Math.Max(0f, rect.Width - style.Padding.Horizontal);
        UiTextLayoutResult textLayout = UiTextLayout.Measure(text, style, availableWidth, constrainWidth: true);

        using SKPaint textPaint = UiTextLayout.CreatePaint(style);
        using SKPaint? shadowPaint = CreateShadowPaint(style.TextShadow, style);

        float y = rect.Top + style.Padding.Top + style.FontSize;

        for (int i = 0; i < textLayout.Lines.Count; i++)
        {
            string line = textLayout.Lines[i];
            UiTextDirection direction = UiTextLayout.ResolveDirection(line, style.Direction);
            string renderLine = UiTextLayout.PrepareForRendering(line, direction);
            SKTextAlign align = ResolveTextAlign(style, direction);
            float anchorX = ResolveTextAnchor(rect, style, align);
            float x = ResolveTextStartX(renderLine, rect, style, align);
            IReadOnlyList<UiTextRun> runs = UiTextLayout.CreateRuns(renderLine, style);
            if (shadowPaint != null && style.TextShadow is UiShadow shadow)
            {
                DrawTextRuns(canvas, runs, x + shadow.OffsetX, y + shadow.OffsetY, style, shadowPaint);
            }

            DrawTextRuns(canvas, runs, x, y, style, textPaint);
            DrawTextDecorations(canvas, renderLine, anchorX, y, align, style);
            y += textLayout.LineHeight;
        }
    }

    private static void DrawImage(UiNode node, SKRect rect, UiStyle style, SKCanvas canvas)
    {
        if (!UiImageSourceCache.TryGetResource(node.Attributes["src"], out UiImageSourceCache.UiImageResource? resource) || resource == null)
        {
            return;
        }

        SKRect contentRect = new(
            rect.Left + style.Padding.Left,
            rect.Top + style.Padding.Top,
            rect.Right - style.Padding.Right,
            rect.Bottom - style.Padding.Bottom);

        if (contentRect.Width <= 0.01f || contentRect.Height <= 0.01f)
        {
            return;
        }

        int saveCount = canvas.Save();
        ClipNodeBounds(canvas, contentRect, style);

        if (resource.RasterImage != null && HasNineSlice(style, resource.RasterImage))
        {
            DrawNineSliceImage(canvas, resource.RasterImage, contentRect, style.ImageSlice);
            canvas.RestoreToCount(saveCount);
            return;
        }

        SKRect destinationRect = ResolveObjectFitRect(contentRect, resource.Width, resource.Height, style.ObjectFit);
        DrawImageResource(canvas, resource, destinationRect);
        canvas.RestoreToCount(saveCount);
    }

    private static void DrawCanvas(UiNode node, SKRect rect, UiStyle style, SKCanvas canvas)
    {
        if (node.CanvasContent == null)
        {
            return;
        }

        SKRect contentRect = new(
            rect.Left + style.Padding.Left,
            rect.Top + style.Padding.Top,
            rect.Right - style.Padding.Right,
            rect.Bottom - style.Padding.Bottom);
        if (contentRect.Width <= 0.01f || contentRect.Height <= 0.01f)
        {
            return;
        }

        int saveCount = canvas.Save();
        ClipNodeBounds(canvas, contentRect, style);
        node.CanvasContent.Draw(canvas, contentRect);
        canvas.RestoreToCount(saveCount);
    }

    private static bool HasNineSlice(UiStyle style, SKImage image)
    {
        UiThickness slice = style.ImageSlice;
        return (slice.Left > 0f
            || slice.Top > 0f
            || slice.Right > 0f
            || slice.Bottom > 0f)
            && slice.Left + slice.Right < image.Width
            && slice.Top + slice.Bottom < image.Height;
    }

    private static void DrawNineSliceImage(SKCanvas canvas, SKImage image, SKRect destination, UiThickness slice)
    {
        float srcLeft = Math.Clamp(slice.Left, 0f, image.Width - 1f);
        float srcTop = Math.Clamp(slice.Top, 0f, image.Height - 1f);
        float srcRight = Math.Clamp(slice.Right, 0f, image.Width - srcLeft - 1f);
        float srcBottom = Math.Clamp(slice.Bottom, 0f, image.Height - srcTop - 1f);

        float dstLeft = Math.Min(srcLeft, destination.Width);
        float dstTop = Math.Min(srcTop, destination.Height);
        float dstRight = Math.Min(srcRight, Math.Max(0f, destination.Width - dstLeft));
        float dstBottom = Math.Min(srcBottom, Math.Max(0f, destination.Height - dstTop));

        float srcCenterWidth = Math.Max(0f, image.Width - srcLeft - srcRight);
        float srcCenterHeight = Math.Max(0f, image.Height - srcTop - srcBottom);
        float dstCenterWidth = Math.Max(0f, destination.Width - dstLeft - dstRight);
        float dstCenterHeight = Math.Max(0f, destination.Height - dstTop - dstBottom);

        using SKPaint paint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

        DrawImagePatch(canvas, image, new SKRect(0f, 0f, srcLeft, srcTop), new SKRect(destination.Left, destination.Top, destination.Left + dstLeft, destination.Top + dstTop), paint);
        DrawImagePatch(canvas, image, new SKRect(srcLeft, 0f, srcLeft + srcCenterWidth, srcTop), new SKRect(destination.Left + dstLeft, destination.Top, destination.Right - dstRight, destination.Top + dstTop), paint);
        DrawImagePatch(canvas, image, new SKRect(image.Width - srcRight, 0f, image.Width, srcTop), new SKRect(destination.Right - dstRight, destination.Top, destination.Right, destination.Top + dstTop), paint);

        DrawImagePatch(canvas, image, new SKRect(0f, srcTop, srcLeft, srcTop + srcCenterHeight), new SKRect(destination.Left, destination.Top + dstTop, destination.Left + dstLeft, destination.Bottom - dstBottom), paint);
        DrawImagePatch(canvas, image, new SKRect(srcLeft, srcTop, srcLeft + srcCenterWidth, srcTop + srcCenterHeight), new SKRect(destination.Left + dstLeft, destination.Top + dstTop, destination.Left + dstLeft + dstCenterWidth, destination.Top + dstTop + dstCenterHeight), paint);
        DrawImagePatch(canvas, image, new SKRect(image.Width - srcRight, srcTop, image.Width, srcTop + srcCenterHeight), new SKRect(destination.Right - dstRight, destination.Top + dstTop, destination.Right, destination.Bottom - dstBottom), paint);

        DrawImagePatch(canvas, image, new SKRect(0f, image.Height - srcBottom, srcLeft, image.Height), new SKRect(destination.Left, destination.Bottom - dstBottom, destination.Left + dstLeft, destination.Bottom), paint);
        DrawImagePatch(canvas, image, new SKRect(srcLeft, image.Height - srcBottom, srcLeft + srcCenterWidth, image.Height), new SKRect(destination.Left + dstLeft, destination.Bottom - dstBottom, destination.Right - dstRight, destination.Bottom), paint);
        DrawImagePatch(canvas, image, new SKRect(image.Width - srcRight, image.Height - srcBottom, image.Width, image.Height), new SKRect(destination.Right - dstRight, destination.Bottom - dstBottom, destination.Right, destination.Bottom), paint);
    }

    private static void DrawImagePatch(SKCanvas canvas, SKImage image, SKRect source, SKRect destination, SKPaint paint)
    {
        if (source.Width <= 0.01f || source.Height <= 0.01f || destination.Width <= 0.01f || destination.Height <= 0.01f)
        {
            return;
        }

        canvas.DrawImage(image, source, destination, paint);
    }

    private static void DrawBackgroundImageLayer(SKCanvas canvas, SKRect rect, UiStyle style, string imageSource, int layerIndex)
    {
        if (!UiImageSourceCache.TryGetResource(imageSource, out UiImageSourceCache.UiImageResource? resource) || resource == null)
        {
            return;
        }

        UiBackgroundSize size = ResolveBackgroundLayerValue(style.BackgroundSizes, layerIndex, UiBackgroundSize.Auto);
        UiBackgroundPosition position = ResolveBackgroundLayerValue(style.BackgroundPositions, layerIndex, UiBackgroundPosition.TopLeft);
        UiBackgroundRepeat repeat = ResolveBackgroundLayerValue(style.BackgroundRepeats, layerIndex, UiBackgroundRepeat.Repeat);
        SKSize tileSize = ResolveBackgroundTileSize(size, rect, resource);
        if (tileSize.Width <= 0.01f || tileSize.Height <= 0.01f)
        {
            return;
        }

        int saveCount = canvas.Save();
        ClipNodeBounds(canvas, rect, style);
        DrawRepeatedBackground(canvas, resource, rect, tileSize, position, repeat);
        canvas.RestoreToCount(saveCount);
    }

    private static void DrawRepeatedBackground(
        SKCanvas canvas,
        UiImageSourceCache.UiImageResource resource,
        SKRect area,
        SKSize tileSize,
        UiBackgroundPosition position,
        UiBackgroundRepeat repeat)
    {
        bool repeatX = repeat is UiBackgroundRepeat.Repeat or UiBackgroundRepeat.RepeatX;
        bool repeatY = repeat is UiBackgroundRepeat.Repeat or UiBackgroundRepeat.RepeatY;

        float startX = area.Left + ResolveBackgroundAxisOffset(position.X, area.Width - tileSize.Width);
        float startY = area.Top + ResolveBackgroundAxisOffset(position.Y, area.Height - tileSize.Height);

        if (repeatX)
        {
            startX = NormalizeRepeatStart(startX, tileSize.Width, area.Left, area.Right);
        }

        if (repeatY)
        {
            startY = NormalizeRepeatStart(startY, tileSize.Height, area.Top, area.Bottom);
        }

        float endX = repeatX ? area.Right : startX + tileSize.Width;
        float endY = repeatY ? area.Bottom : startY + tileSize.Height;

        for (float y = startY; y < endY; y += tileSize.Height)
        {
            for (float x = startX; x < endX; x += tileSize.Width)
            {
                SKRect destination = new(x, y, x + tileSize.Width, y + tileSize.Height);
                DrawImageResource(canvas, resource, destination);

                if (!repeatX)
                {
                    break;
                }
            }

            if (!repeatY)
            {
                break;
            }
        }
    }

    private static SKSize ResolveBackgroundTileSize(UiBackgroundSize size, SKRect area, UiImageSourceCache.UiImageResource resource)
    {
        float sourceWidth = Math.Max(1f, resource.Width);
        float sourceHeight = Math.Max(1f, resource.Height);
        float widthScale = area.Width / sourceWidth;
        float heightScale = area.Height / sourceHeight;

        return size.Mode switch
        {
            UiBackgroundSizeMode.Cover => new SKSize(sourceWidth * Math.Max(widthScale, heightScale), sourceHeight * Math.Max(widthScale, heightScale)),
            UiBackgroundSizeMode.Contain => new SKSize(sourceWidth * Math.Min(widthScale, heightScale), sourceHeight * Math.Min(widthScale, heightScale)),
            UiBackgroundSizeMode.Explicit => ResolveExplicitBackgroundSize(size, area, sourceWidth, sourceHeight),
            _ => new SKSize(sourceWidth, sourceHeight)
        };
    }

    private static SKSize ResolveExplicitBackgroundSize(UiBackgroundSize size, SKRect area, float sourceWidth, float sourceHeight)
    {
        bool widthAuto = size.Width.IsAuto;
        bool heightAuto = size.Height.IsAuto;

        float width = widthAuto ? 0f : ResolveLength(size.Width, area.Width);
        float height = heightAuto ? 0f : ResolveLength(size.Height, area.Height);

        if (widthAuto && heightAuto)
        {
            return new SKSize(sourceWidth, sourceHeight);
        }

        if (widthAuto)
        {
            width = height * (sourceWidth / Math.Max(0.01f, sourceHeight));
        }
        else if (heightAuto)
        {
            height = width * (sourceHeight / Math.Max(0.01f, sourceWidth));
        }

        return new SKSize(Math.Max(0.01f, width), Math.Max(0.01f, height));
    }

    private static float ResolveBackgroundAxisOffset(UiLength length, float available)
    {
        return length.Unit switch
        {
            UiLengthUnit.Percent => available * (length.Value / 100f),
            UiLengthUnit.Pixel => length.Value,
            _ => 0f
        };
    }

    private static float NormalizeRepeatStart(float start, float tileSize, float min, float max)
    {
        if (tileSize <= 0.01f)
        {
            return start;
        }

        while (start > min)
        {
            start -= tileSize;
        }

        while (start + tileSize < min)
        {
            start += tileSize;
        }

        return start;
    }

    private static T ResolveBackgroundLayerValue<T>(IReadOnlyList<T> values, int layerIndex, T fallback)
    {
        if (values.Count == 0)
        {
            return fallback;
        }

        return layerIndex < values.Count ? values[layerIndex] : values[^1];
    }

    private static void DrawImageResource(SKCanvas canvas, UiImageSourceCache.UiImageResource resource, SKRect destination)
    {
        if (resource.RasterImage != null)
        {
            using SKPaint paint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            canvas.DrawImage(resource.RasterImage, destination, paint);
            return;
        }

        if (resource.SvgPicture == null)
        {
            return;
        }

        int saveCount = canvas.Save();
        float scaleX = destination.Width / Math.Max(0.01f, resource.SourceBounds.Width);
        float scaleY = destination.Height / Math.Max(0.01f, resource.SourceBounds.Height);
        canvas.Translate(destination.Left - (resource.SourceBounds.Left * scaleX), destination.Top - (resource.SourceBounds.Top * scaleY));
        canvas.Scale(scaleX, scaleY);
        canvas.DrawPicture(resource.SvgPicture);
        canvas.RestoreToCount(saveCount);
    }

    private static void ClipNodeBounds(SKCanvas canvas, SKRect rect, UiStyle style)
    {
        using SKPath path = CreateClipPath(rect, style);
        canvas.ClipPath(path, antialias: true);
    }

    private static SKPath CreateClipPath(SKRect rect, UiStyle style)
    {
        SKPath path = new();
        if (style.ClipPath != null)
        {
            switch (style.ClipPath.Kind)
            {
                case UiClipPathKind.Inset:
                    UiThickness inset = style.ClipPath.Inset;
                    SKRect insetRect = new(
                        rect.Left + inset.Left,
                        rect.Top + inset.Top,
                        rect.Right - inset.Right,
                        rect.Bottom - inset.Bottom);
                    if (style.BorderRadius > 0.01f)
                    {
                        path.AddRoundRect(insetRect, style.BorderRadius, style.BorderRadius);
                    }
                    else
                    {
                        path.AddRect(insetRect);
                    }

                    return path;
                case UiClipPathKind.Circle:
                    float radius = ResolveLength(style.ClipPath.Radius, Math.Min(rect.Width, rect.Height));
                    float centerX = rect.Left + ResolveLength(style.ClipPath.CenterX, rect.Width);
                    float centerY = rect.Top + ResolveLength(style.ClipPath.CenterY, rect.Height);
                    path.AddCircle(centerX, centerY, Math.Max(0f, radius));
                    return path;
            }
        }

        if (style.BorderRadius > 0.01f)
        {
            path.AddRoundRect(rect, style.BorderRadius, style.BorderRadius);
        }
        else
        {
            path.AddRect(rect);
        }

        return path;
    }

    private static float ResolveLength(UiLength length, float available)
    {
        return length.Unit switch
        {
            UiLengthUnit.Pixel => length.Value,
            UiLengthUnit.Percent => available * (length.Value / 100f),
            _ => length.Value
        };
    }
    private static SKRect ResolveObjectFitRect(SKRect contentRect, float sourceWidth, float sourceHeight, UiObjectFit objectFit)
    {
        float widthScale = contentRect.Width / Math.Max(1f, sourceWidth);
        float heightScale = contentRect.Height / Math.Max(1f, sourceHeight);
        float scale = objectFit switch
        {
            UiObjectFit.Contain => Math.Min(widthScale, heightScale),
            UiObjectFit.Cover => Math.Max(widthScale, heightScale),
            UiObjectFit.None => 1f,
            UiObjectFit.ScaleDown => sourceWidth <= contentRect.Width && sourceHeight <= contentRect.Height
                ? 1f
                : Math.Min(widthScale, heightScale),
            _ => -1f
        };

        if (objectFit == UiObjectFit.Fill || scale < 0f)
        {
            return contentRect;
        }

        float drawWidth = sourceWidth * scale;
        float drawHeight = sourceHeight * scale;
        float left = contentRect.Left + ((contentRect.Width - drawWidth) * 0.5f);
        float top = contentRect.Top + ((contentRect.Height - drawHeight) * 0.5f);
        return new SKRect(left, top, left + drawWidth, top + drawHeight);
    }

    private static SKTextAlign ResolveTextAlign(UiStyle style, UiTextDirection direction)
    {
        return style.TextAlign switch
        {
            UiTextAlign.Left => SKTextAlign.Left,
            UiTextAlign.Right => SKTextAlign.Right,
            UiTextAlign.Center => SKTextAlign.Center,
            UiTextAlign.End => direction == UiTextDirection.Rtl ? SKTextAlign.Left : SKTextAlign.Right,
            _ => direction == UiTextDirection.Rtl ? SKTextAlign.Right : SKTextAlign.Left
        };
    }

    private static float ResolveTextAnchor(SKRect rect, UiStyle style, SKTextAlign align)
    {
        return align switch
        {
            SKTextAlign.Right => rect.Right - style.Padding.Right,
            SKTextAlign.Center => rect.Left + style.Padding.Left + ((rect.Width - style.Padding.Horizontal) * 0.5f),
            _ => rect.Left + style.Padding.Left
        };
    }

    private static float ResolveTextStartX(string line, SKRect rect, UiStyle style, SKTextAlign align)
    {
        float anchorX = ResolveTextAnchor(rect, style, align);
        float lineWidth = UiTextLayout.MeasureWidth(line, style);
        return align switch
        {
            SKTextAlign.Right => anchorX - lineWidth,
            SKTextAlign.Center => anchorX - (lineWidth * 0.5f),
            _ => anchorX
        };
    }

    private static void DrawTextRuns(SKCanvas canvas, IReadOnlyList<UiTextRun> runs, float startX, float baselineY, UiStyle style, SKPaint paint)
    {
        float cursorX = startX;
        foreach (UiTextRun run in runs)
        {
            using SKFont font = new(run.Typeface, style.FontSize);
            canvas.DrawText(run.Text, cursorX, baselineY, SKTextAlign.Left, font, paint);
            cursorX += font.MeasureText(run.Text, paint);
        }
    }

    private static void DrawTextDecorations(SKCanvas canvas, string line, float anchorX, float baselineY, SKTextAlign align, UiStyle style)
    {
        if (style.TextDecorationLine == UiTextDecorationLine.None || string.IsNullOrEmpty(line))
        {
            return;
        }

        float lineWidth = UiTextLayout.MeasureWidth(line, style);
        if (lineWidth <= 0.01f)
        {
            return;
        }

        float startX = align switch
        {
            SKTextAlign.Right => anchorX - lineWidth,
            SKTextAlign.Center => anchorX - (lineWidth * 0.5f),
            _ => anchorX
        };
        float endX = startX + lineWidth;
        float strokeWidth = Math.Max(1f, style.FontSize * 0.06f);
        using SKPaint decorationPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            Color = style.Color
        };

        if ((style.TextDecorationLine & UiTextDecorationLine.Underline) != 0)
        {
            float underlineY = baselineY + Math.Max(1.5f, style.FontSize * 0.12f);
            canvas.DrawLine(startX, underlineY, endX, underlineY, decorationPaint);
        }

        if ((style.TextDecorationLine & UiTextDecorationLine.LineThrough) != 0)
        {
            float strikeY = baselineY - (style.FontSize * 0.32f);
            canvas.DrawLine(startX, strikeY, endX, strikeY, decorationPaint);
        }
    }

    private static SKPaint? CreateShadowPaint(UiShadow? shadow, UiStyle style)
    {
        if (shadow is not UiShadow visibleShadow || !visibleShadow.IsVisible)
        {
            return null;
        }

        return new SKPaint
        {
            Color = visibleShadow.Color,
            IsAntialias = true,
            ImageFilter = visibleShadow.BlurRadius > 0.01f ? SKImageFilter.CreateBlur(ToSigma(visibleShadow.BlurRadius), ToSigma(visibleShadow.BlurRadius)) : null
        };
    }

    private static SKShader CreateGradientShader(SKRect rect, UiLinearGradient gradient)
    {
        float radians = gradient.AngleDegrees * (MathF.PI / 180f);
        SKPoint center = new(rect.MidX, rect.MidY);
        SKPoint direction = new(MathF.Cos(radians), MathF.Sin(radians));
        float halfLength = MathF.Max(rect.Width, rect.Height);
        SKPoint start = new(center.X - (direction.X * halfLength), center.Y - (direction.Y * halfLength));
        SKPoint end = new(center.X + (direction.X * halfLength), center.Y + (direction.Y * halfLength));

        return SKShader.CreateLinearGradient(
            start,
            end,
            gradient.Stops.Select(static stop => stop.Color).ToArray(),
            gradient.Stops.Select(static stop => stop.Position).ToArray(),
            SKShaderTileMode.Clamp);
    }

    private static float ToSigma(float blurRadius)
    {
        return Math.Max(0.01f, blurRadius * 0.5f);
    }
}
