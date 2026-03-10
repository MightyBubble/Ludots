using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using SkiaSharp;

internal sealed class CaptureSuite
{
	private readonly string _title;

	private readonly string _root;

	private readonly string _screens;

	private readonly UiSceneRenderer _renderer;

	private readonly List<TraceEntry> _traces = new List<TraceEntry>();

	private readonly List<string> _reportLines = new List<string>();

	private readonly List<string> _checklistLines = new List<string>();

	public CaptureSuite(string title, string root, UiSceneRenderer renderer)
	{
		_title = title;
		_root = root;
		_renderer = renderer;
		_screens = Path.Combine(root, "screens");
		Directory.CreateDirectory(_root);
		Directory.CreateDirectory(_screens);
	}

	public void Capture(string stepName, UiScene scene, string narrative)
	{
		scene.Layout(1280f, 720f);
		using SKBitmap bitmap = RenderSceneBitmap(scene, 1280, 720);
		string filePath = Path.Combine(_screens, stepName + ".png");
		SaveBitmapAsPng(bitmap, filePath);
		RecordCapture(stepName, narrative, filePath);
	}

	public void CaptureFocus(string stepName, UiScene scene, string elementId, string narrative, float padding = 24f, int minWidth = 520, int minHeight = 240)
	{
		scene.Layout(1280f, 720f);
		UiNode uiNode = scene.FindByElementId(elementId) ?? throw new InvalidOperationException("Element '" + elementId + "' was not found.");
		float val = scene.Root?.LayoutRect.Bottom ?? 720f;
		int num = Math.Max(720, (int)Math.Ceiling(Math.Max(val, uiNode.LayoutRect.Bottom + padding + 1f)));
		scene.Layout(1280f, num);
		UiNode uiNode2 = scene.FindByElementId(elementId) ?? throw new InvalidOperationException("Element '" + elementId + "' was not found after relayout.");
		using SKBitmap sKBitmap = new SKBitmap(1280, num);
		using (SKCanvas sKCanvas = new SKCanvas(sKBitmap))
		{
			sKCanvas.Clear(ResolveSceneBackdrop(scene));
			_renderer.Render(scene, sKCanvas, 1280f, num);
		}
		SKRectI sKRectI = BuildFocusRect(uiNode2.LayoutRect, padding, minWidth, minHeight, sKBitmap.Width, sKBitmap.Height);
		using SKBitmap bitmap = new SKBitmap(sKRectI.Width, sKRectI.Height);
		using (SKCanvas sKCanvas2 = new SKCanvas(bitmap))
		{
			sKCanvas2.Clear(ResolveSceneBackdrop(scene));
			sKCanvas2.DrawBitmap(sKBitmap, sKRectI, new SKRect(0f, 0f, sKRectI.Width, sKRectI.Height));
			if (stepName.Contains("phase5", StringComparison.OrdinalIgnoreCase))
			{
				DrawPhaseFiveOverlay(sKCanvas2, uiNode2.RenderStyle, stepName);
			}
		}
		string filePath = Path.Combine(_screens, stepName + ".png");
		SaveBitmapAsPng(bitmap, filePath);
		RecordCapture(stepName, narrative, filePath);
	}

	public void Click(UiScene scene, string elementId, string narrative)
	{
		scene.Layout(1280f, 720f);
		UiNode uiNode = scene.FindByElementId(elementId) ?? throw new InvalidOperationException("Element '" + elementId + "' was not found.");
		float x = uiNode.LayoutRect.X + uiNode.LayoutRect.Width / 2f;
		float y = uiNode.LayoutRect.Y + uiNode.LayoutRect.Height / 2f;
		if (!scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, x, y, uiNode.Id)).Handled)
		{
			throw new InvalidOperationException("Click on '" + elementId + "' was not handled.");
		}
		scene.Layout(1280f, 720f);
		_traces.Add(new TraceEntry(elementId, "click", narrative, string.Empty));
		_reportLines.Add("- " + elementId + ": " + narrative);
	}

	public void Scroll(UiScene scene, string elementId, float deltaY, string narrative)
	{
		scene.Layout(1280f, 720f);
		UiNode uiNode = scene.FindByElementId(elementId) ?? throw new InvalidOperationException("Element '" + elementId + "' was not found.");
		float x = uiNode.LayoutRect.X + uiNode.LayoutRect.Width / 2f;
		float y = uiNode.LayoutRect.Y + uiNode.LayoutRect.Height / 2f;
		if (!scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, 0, x, y, uiNode.Id, 0f, deltaY)).Handled)
		{
			throw new InvalidOperationException("Scroll on '" + elementId + "' was not handled.");
		}
		scene.Layout(1280f, 720f);
		_traces.Add(new TraceEntry(elementId, "scroll", narrative, string.Empty));
		_reportLines.Add("- " + elementId + ": " + narrative);
	}

	public void Advance(UiScene scene, float deltaSeconds, string narrative)
	{
		if (!scene.AdvanceTime(deltaSeconds))
		{
			throw new InvalidOperationException($"Advance({deltaSeconds}) produced no visual changes.");
		}
		_traces.Add(new TraceEntry(deltaSeconds.ToString("0.###"), "advance", narrative, string.Empty));
		_reportLines.Add($"- advance {deltaSeconds:0.###}s: {narrative}");
	}

	public void WriteReport(IEnumerable<string> verdictLines, string pathDiagram)
	{
		File.WriteAllText(Path.Combine(_root, "trace.jsonl"), string.Join(Environment.NewLine, _traces.Select((TraceEntry trace) => JsonSerializer.Serialize(trace))));
		File.WriteAllText(Path.Combine(_root, "battle-report.md"), BuildReport(verdictLines));
		File.WriteAllText(Path.Combine(_root, "path.mmd"), pathDiagram);
		File.WriteAllText(Path.Combine(_root, "visible-checklist.md"), string.Join(Environment.NewLine, _checklistLines));
	}

	private void RecordCapture(string stepName, string narrative, string filePath)
	{
		string text = Path.GetRelativePath(_root, filePath).Replace('\\', '/');
		_traces.Add(new TraceEntry(stepName, "capture", narrative, text));
		_reportLines.Add("- " + stepName + ": " + narrative);
		_checklistLines.Add("- [x] " + stepName + " -> " + text);
	}

	private SKBitmap RenderSceneBitmap(UiScene scene, int width, int height)
	{
		SKBitmap sKBitmap = new SKBitmap(width, height);
		using SKCanvas sKCanvas = new SKCanvas(sKBitmap);
		sKCanvas.Clear(ResolveSceneBackdrop(scene));
		_renderer.Render(scene, sKCanvas, width, height);
		return sKBitmap;
	}

	private static void DrawPhaseFiveOverlay(SKCanvas canvas, UiStyle style, string stepName)
	{
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = new SKColor(15, 23, 42, 196)
		};
		using SKPaint paint2 = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = SKColors.White
		};
		using SKFont font = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 13f);
		string text = $"{stepName} | bg={FormatColor(style.BackgroundColor)} | opacity={style.Opacity:0.00} | filter={style.FilterBlurRadius:0.0} | backdrop={style.BackdropBlurRadius:0.0}";
		SKRect rect = new SKRect(8f, 8f, Math.Max(180f, canvas.LocalClipBounds.Width - 8f), 34f);
		canvas.DrawRoundRect(rect, 8f, 8f, paint);
		canvas.DrawText(text, 16f, 28f, SKTextAlign.Left, font, paint2);
	}

	private static string FormatColor(SKColor color)
	{
		return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}{color.Alpha:X2}";
	}

	private static SKColor ResolveSceneBackdrop(UiScene scene)
	{
		if (scene.Root == null)
		{
			return SKColors.White;
		}
		SKColor backgroundColor = scene.Root.RenderStyle.BackgroundColor;
		if (backgroundColor != SKColors.Transparent)
		{
			return backgroundColor;
		}
		backgroundColor = scene.Root.Style.BackgroundColor;
		return (backgroundColor != SKColors.Transparent) ? backgroundColor : SKColors.White;
	}

	private string BuildReport(IEnumerable<string> verdictLines)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("# ");
		handler.AppendFormatted(_title);
		handler.AppendLiteral(" Battle Report");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Scenario Card");
		stringBuilder.AppendLine("- Goal: validate visible, interactive, acceptance-ready official UI showcase behavior.");
		stringBuilder.AppendLine("- Viewport: 1280x720 for full-scene captures, focused crops for below-the-fold capability blocks.");
		stringBuilder.AppendLine("- Driver: headless Skia renderer + deterministic click simulation.");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Battle Log");
		foreach (string reportLine in _reportLines)
		{
			stringBuilder.AppendLine(reportLine);
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## Acceptance Verdict");
		foreach (string verdictLine in verdictLines)
		{
			stringBuilder.AppendLine(verdictLine);
		}
		return stringBuilder.ToString();
	}

	private static SKRectI BuildFocusRect(UiRect rect, float padding, int minWidth, int minHeight, int viewportWidth, int viewportHeight)
	{
		float num = Math.Max(minWidth, rect.Width + padding * 2f);
		float num2 = Math.Max(minHeight, rect.Height + padding * 2f);
		float num3 = rect.X + rect.Width * 0.5f;
		float num4 = rect.Y + rect.Height * 0.5f;
		float value = num3 - num * 0.5f;
		float value2 = num4 - num2 * 0.5f;
		value = Math.Clamp(value, 0f, Math.Max(0f, (float)viewportWidth - num));
		value2 = Math.Clamp(value2, 0f, Math.Max(0f, (float)viewportHeight - num2));
		int num5 = Math.Max(0, (int)Math.Floor(value));
		int num6 = Math.Max(0, (int)Math.Floor(value2));
		int num7 = Math.Min(viewportWidth - num5, Math.Max(1, (int)Math.Ceiling(num)));
		int num8 = Math.Min(viewportHeight - num6, Math.Max(1, (int)Math.Ceiling(num2)));
		return new SKRectI(num5, num6, num5 + num7, num6 + num8);
	}

	private static void SaveBitmapAsPng(SKBitmap bitmap, string filePath)
	{
		using SKImage sKImage = SKImage.FromBitmap(bitmap);
		using SKData sKData = sKImage.Encode(SKEncodedImageFormat.Png, 100);
		using FileStream target = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
		sKData.SaveTo(target);
	}
}
