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

        RenderNode(scene.Root, canvas);
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

    private void RenderNode(UiNode node, SKCanvas canvas)
    {
        UiStyle style = node.Style;
        if (!style.Visible || style.Display == UiDisplay.None)
        {
            return;
        }

        SKRect rect = new(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Right, node.LayoutRect.Bottom);
        bool clipped = false;
        int saveCount = 0;

        if (style.Opacity < 1f)
        {
            saveCount = canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(style.Opacity * 255f, 0f, 255f)) });
        }
        else if (style.ClipContent)
        {
            saveCount = canvas.Save();
        }

        if (style.ClipContent)
        {
            canvas.ClipRect(rect);
            clipped = true;
        }

        DrawBoxShadow(canvas, rect, style);

        if (style.BackgroundColor != SKColors.Transparent)
        {
            using SKPaint fill = new() { Color = style.BackgroundColor, IsAntialias = true, Style = SKPaintStyle.Fill };
            DrawRect(canvas, rect, style.BorderRadius, fill);
        }

        if (style.BackgroundGradient != null)
        {
            using SKPaint gradientFill = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = CreateGradientShader(rect, style.BackgroundGradient) };
            DrawRect(canvas, rect, style.BorderRadius, gradientFill);
        }

        if (style.BorderWidth > 0f && style.BorderColor != SKColors.Transparent)
        {
            using SKPaint stroke = new()
            {
                Color = style.BorderColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = style.BorderWidth
            };
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

        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            DrawText(node.TextContent, rect, style, canvas);
        }

        foreach (UiNode child in node.Children)
        {
            RenderNode(child, canvas);
        }

        if (clipped || style.Opacity < 1f)
        {
            canvas.RestoreToCount(saveCount);
        }
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

    private static void DrawBoxShadow(SKCanvas canvas, SKRect rect, UiStyle style)
    {
        if (style.BoxShadow is not UiShadow shadow || !shadow.IsVisible)
        {
            return;
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

    private static void DrawText(string text, SKRect rect, UiStyle style, SKCanvas canvas)
    {
        float availableWidth = Math.Max(0f, rect.Width - style.Padding.Horizontal);
        UiTextLayoutResult textLayout = UiTextLayout.Measure(text, style, availableWidth, constrainWidth: true);

        using SKPaint textPaint = UiTextLayout.CreatePaint(style);
        using SKFont textFont = UiTextLayout.CreateFont(style);
        using SKPaint? shadowPaint = CreateShadowPaint(style.TextShadow, style);
        using SKFont? shadowFont = shadowPaint != null ? UiTextLayout.CreateFont(style) : null;

        float x = rect.Left + style.Padding.Left;
        float y = rect.Top + style.Padding.Top + style.FontSize;

        for (int i = 0; i < textLayout.Lines.Count; i++)
        {
            string line = textLayout.Lines[i];
            if (shadowPaint != null && style.TextShadow is UiShadow shadow)
            {
                canvas.DrawText(line, x + shadow.OffsetX, y + shadow.OffsetY, SKTextAlign.Left, shadowFont!, shadowPaint);
            }

            canvas.DrawText(line, x, y, SKTextAlign.Left, textFont, textPaint);
            y += textLayout.LineHeight;
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
