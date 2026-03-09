using SkiaSharp;

namespace UiShowcaseCoreMod.Showcase;

internal static class UiShowcaseImageAssets
{
    private static readonly Lazy<string> CoverArtLazy = new(CreateCoverArtDataUri);
    private static readonly Lazy<string> FrameArtLazy = new(CreateFrameArtDataUri);
    private static readonly Lazy<string> BadgeSvgLazy = new(CreateBadgeSvgDataUri);

    internal static string CoverArtDataUri => CoverArtLazy.Value;

    internal static string FrameArtDataUri => FrameArtLazy.Value;

    internal static string BadgeSvgDataUri => BadgeSvgLazy.Value;

    private static string CreateCoverArtDataUri()
    {
        using SKBitmap bitmap = new(192, 128);
        using SKCanvas canvas = new(bitmap);
        using SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

        fill.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0f, 0f),
            new SKPoint(bitmap.Width, bitmap.Height),
            new[] { SKColor.Parse("#2563eb"), SKColor.Parse("#22d3ee"), SKColor.Parse("#f59e0b") },
            new[] { 0f, 0.55f, 1f },
            SKShaderTileMode.Clamp);
        canvas.DrawRect(new SKRect(0f, 0f, bitmap.Width, bitmap.Height), fill);

        fill.Shader = null;
        fill.Color = new SKColor(255, 255, 255, 72);
        canvas.DrawRoundRect(new SKRect(16f, 18f, 176f, 108f), 20f, 20f, fill);

        using SKPaint badge = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#111827").WithAlpha(168) };
        canvas.DrawRoundRect(new SKRect(28f, 76f, 128f, 108f), 16f, 16f, badge);

        using SKPaint sun = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#fef08a") };
        canvas.DrawCircle(148f, 38f, 18f, sun);

        return EncodePngDataUri(bitmap);
    }

    private static string CreateFrameArtDataUri()
    {
        using SKBitmap bitmap = new(72, 72);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        using SKPaint border = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#0f172a") };
        using SKPaint accent = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#38bdf8") };
        using SKPaint center = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#e2e8f0") };

        canvas.DrawRoundRect(new SKRect(0f, 0f, 72f, 72f), 18f, 18f, border);
        canvas.DrawRoundRect(new SKRect(8f, 8f, 64f, 64f), 14f, 14f, accent);
        canvas.DrawRoundRect(new SKRect(18f, 18f, 54f, 54f), 10f, 10f, center);

        return EncodePngDataUri(bitmap);
    }

    private static string EncodePngDataUri(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    private static string CreateBadgeSvgDataUri()
    {
        return "data:image/svg+xml;utf8," + Uri.EscapeDataString(UiShowcaseAssets.GetShowcaseBadgeSvg());
    }
}
