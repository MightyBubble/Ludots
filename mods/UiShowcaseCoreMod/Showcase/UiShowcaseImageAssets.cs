using System;
using SkiaSharp;

namespace UiShowcaseCoreMod.Showcase;

internal static class UiShowcaseImageAssets
{
	private static readonly Lazy<string> CoverArtLazy = new Lazy<string>(CreateCoverArtDataUri);

	private static readonly Lazy<string> FrameArtLazy = new Lazy<string>(CreateFrameArtDataUri);

	private static readonly Lazy<string> BadgeSvgLazy = new Lazy<string>(CreateBadgeSvgDataUri);

	internal static string CoverArtDataUri => CoverArtLazy.Value;

	internal static string FrameArtDataUri => FrameArtLazy.Value;

	internal static string BadgeSvgDataUri => BadgeSvgLazy.Value;

	private static string CreateCoverArtDataUri()
	{
		using SKBitmap sKBitmap = new SKBitmap(192, 128);
		using SKCanvas sKCanvas = new SKCanvas(sKBitmap);
		using SKPaint sKPaint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill
		};
		sKPaint.Shader = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(sKBitmap.Width, sKBitmap.Height), new SKColor[3]
		{
			SKColor.Parse("#2563eb"),
			SKColor.Parse("#22d3ee"),
			SKColor.Parse("#f59e0b")
		}, new float[3] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
		sKCanvas.DrawRect(new SKRect(0f, 0f, sKBitmap.Width, sKBitmap.Height), sKPaint);
		sKPaint.Shader = null;
		sKPaint.Color = new SKColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, 72);
		sKCanvas.DrawRoundRect(new SKRect(16f, 18f, 176f, 108f), 20f, 20f, sKPaint);
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColor.Parse("#111827").WithAlpha(168)
		};
		sKCanvas.DrawRoundRect(new SKRect(28f, 76f, 128f, 108f), 16f, 16f, paint);
		using SKPaint paint2 = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColor.Parse("#fef08a")
		};
		sKCanvas.DrawCircle(148f, 38f, 18f, paint2);
		return EncodePngDataUri(sKBitmap);
	}

	private static string CreateFrameArtDataUri()
	{
		using SKBitmap bitmap = new SKBitmap(72, 72);
		using SKCanvas sKCanvas = new SKCanvas(bitmap);
		sKCanvas.Clear(SKColors.Transparent);
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColor.Parse("#0f172a")
		};
		using SKPaint paint2 = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColor.Parse("#38bdf8")
		};
		using SKPaint paint3 = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColor.Parse("#e2e8f0")
		};
		sKCanvas.DrawRoundRect(new SKRect(0f, 0f, 72f, 72f), 18f, 18f, paint);
		sKCanvas.DrawRoundRect(new SKRect(8f, 8f, 64f, 64f), 14f, 14f, paint2);
		sKCanvas.DrawRoundRect(new SKRect(18f, 18f, 54f, 54f), 10f, 10f, paint3);
		return EncodePngDataUri(bitmap);
	}

	private static string EncodePngDataUri(SKBitmap bitmap)
	{
		using SKImage sKImage = SKImage.FromBitmap(bitmap);
		using SKData sKData = sKImage.Encode(SKEncodedImageFormat.Png, 100);
		return "data:image/png;base64," + Convert.ToBase64String(sKData.ToArray());
	}

	private static string CreateBadgeSvgDataUri()
	{
		return "data:image/svg+xml;utf8," + Uri.EscapeDataString(UiShowcaseAssets.GetShowcaseBadgeSvg());
	}
}
