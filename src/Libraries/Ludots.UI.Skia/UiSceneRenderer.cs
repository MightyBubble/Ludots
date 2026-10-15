using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

/// <summary>
/// 直接画进目标画布。画笔、裁剪路径、模糊滤镜、折行缓冲和子节点排序表都由实例持有并反复使用，
/// 只有渐变、虚线边框首次出现这类少见样式才会分配；同一实例不能跨线程并发渲染。
/// </summary>
public sealed class SkiaUiRenderer : IUiRenderer
{
	private SKCanvas? _canvas;

	private readonly SKPaint _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

	private readonly SKPaint _strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

	private readonly SKPaint _layerPaint = new SKPaint();

	private readonly SKPaint _textPaint = new SKPaint { IsAntialias = true };

	private readonly SKPaint _textShadowPaint = new SKPaint { IsAntialias = true };

	private readonly SKPaint _imagePaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };

	private readonly SKPath _clipPath = new SKPath();

	private readonly UiTextLines _textLines = new UiTextLines();

	private readonly Dictionary<float, SKImageFilter> _blurFilters = new Dictionary<float, SKImageFilter>();

	private readonly Dictionary<(UiBorderStyle Style, float Width), SKPathEffect> _borderDashes = new Dictionary<(UiBorderStyle, float), SKPathEffect>();

	private readonly List<List<UiNode>> _orderedChildren = new List<List<UiNode>>();

	private int _depth;

	public void SetCanvas(SKCanvas canvas) => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

	public void Render(UiScene scene, float width, float height)
	{
		ArgumentNullException.ThrowIfNull(scene, "scene");
		SKCanvas canvas = _canvas ?? throw new InvalidOperationException("SetCanvas must be called before Render.");
		RenderToCanvas(scene, canvas, width, height);
	}

	public void RenderToCanvas(UiScene scene, SKCanvas canvas, float width, float height)
	{
		ArgumentNullException.ThrowIfNull(scene, "scene");
		ArgumentNullException.ThrowIfNull(canvas, "canvas");
		scene.Layout(width, height);
		if (scene.Root == null)
		{
			return;
		}
		int count = canvas.Save();
		canvas.ClipRect(new SKRect(0f, 0f, width, height));
		_depth = 0;
		RenderNode(scene.Root, canvas);
		canvas.RestoreToCount(count);
	}

	public void ExportPng(UiScene scene, string outputPath, int width, int height)
	{
		using SKBitmap bitmap = new SKBitmap(width, height);
		using SKCanvas sKCanvas = new SKCanvas(bitmap);
		sKCanvas.Clear(SKColors.Transparent);
		RenderToCanvas(scene, sKCanvas, width, height);
		using SKImage sKImage = SKImage.FromBitmap(bitmap);
		using SKData sKData = sKImage.Encode(SKEncodedImageFormat.Png, 100);
		using FileStream target = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
		sKData.SaveTo(target);
	}

	private void RenderNode(UiNode node, SKCanvas canvas)
	{
		UiStyle renderStyle = node.RenderStyle;
		if (renderStyle.Visible && renderStyle.Display != UiDisplay.None)
		{
			if (renderStyle.FilterBlurRadius > 0.01f)
			{
				RenderFilteredNode(node, canvas);
			}
			else
			{
				RenderNodeCore(node, canvas);
			}
		}
	}

	private void RenderNodeCore(UiNode node, SKCanvas canvas)
	{
		UiStyle renderStyle = node.RenderStyle;
		if (!renderStyle.Visible || renderStyle.Display == UiDisplay.None)
		{
			return;
		}
		SKRect sKRect = new SKRect(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Right, node.LayoutRect.Bottom);
		bool flag = renderStyle.ClipContent || renderStyle.Overflow == UiOverflow.Scroll;
		int count = canvas.Save();
		int num = -1;
		int num2 = -1;
		int num3 = -1;
		int num4 = -1;
		if (renderStyle.Transform.HasOperations)
		{
			canvas.Concat(UiTransformMath.CreateMatrix(renderStyle, node.LayoutRect).ToSKMatrix());
		}
		if (Math.Abs(node.StickyOffsetX) > 0.01f || Math.Abs(node.StickyOffsetY) > 0.01f)
		{
			canvas.Translate(node.StickyOffsetX, node.StickyOffsetY);
		}
		if (renderStyle.Opacity < 1f)
		{
			_layerPaint.ImageFilter = null;
			_layerPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(renderStyle.Opacity * 255f, 0f, 255f));
			num = canvas.SaveLayer(_layerPaint);
		}
		if (renderStyle.MaskGradient != null)
		{
			num2 = canvas.SaveLayer();
		}
		if (renderStyle.ClipPath != null)
		{
			num3 = canvas.Save();
			ClipNodeBounds(canvas, sKRect, renderStyle);
		}
		DrawBackdropBlur(canvas, sKRect, renderStyle);
		DrawBoxShadows(canvas, sKRect, renderStyle);
		DrawBackgrounds(canvas, sKRect, renderStyle);
		if (renderStyle.BorderWidth > 0f && renderStyle.BorderColor != UiColor.Transparent)
		{
			DrawRect(canvas, sKRect, renderStyle.BorderRadius, BorderPaint(renderStyle));
		}
		if (renderStyle.OutlineWidth > 0f && renderStyle.OutlineColor != UiColor.Transparent)
		{
			SKRect rect = sKRect;
			rect.Inflate(renderStyle.OutlineWidth * 0.5f, renderStyle.OutlineWidth * 0.5f);
			DrawRect(canvas, rect, renderStyle.BorderRadius + renderStyle.OutlineWidth * 0.5f, Stroke(renderStyle.OutlineColor, renderStyle.OutlineWidth));
		}
		if (flag)
		{
			num4 = canvas.Save();
			ClipNodeBounds(canvas, sKRect, renderStyle);
			if (renderStyle.Overflow == UiOverflow.Scroll)
			{
				canvas.Translate(0f - node.ScrollOffsetX, 0f - node.ScrollOffsetY);
			}
		}
		ReadOnlySpan<char> text = ResolveRenderableText(node);
		if (!text.IsWhiteSpace())
		{
			DrawText(text, node, sKRect, renderStyle, canvas);
		}
		if (node.Kind == UiNodeKind.Image || string.Equals(node.TagName, "img", StringComparison.OrdinalIgnoreCase))
		{
			DrawImage(node, sKRect, renderStyle, canvas);
		}
		if (string.Equals(node.TagName, "canvas", StringComparison.OrdinalIgnoreCase))
		{
			DrawCanvas(node, sKRect, renderStyle, canvas);
		}
		RenderChildren(node.Children, canvas);
		if (num4 >= 0)
		{
			canvas.RestoreToCount(num4);
		}
		if (renderStyle.Overflow == UiOverflow.Scroll)
		{
			DrawScrollbars(canvas, node);
		}
		if (num3 >= 0)
		{
			canvas.RestoreToCount(num3);
		}
		if (num2 >= 0)
		{
			DrawMask(canvas, sKRect, renderStyle);
			canvas.RestoreToCount(num2);
		}
		if (num >= 0)
		{
			canvas.RestoreToCount(num);
		}
		canvas.RestoreToCount(count);
	}

	private void RenderFilteredNode(UiNode node, SKCanvas canvas)
	{
		_layerPaint.Color = SKColors.White;
		_layerPaint.ImageFilter = BlurFilter(node.RenderStyle.FilterBlurRadius);
		int count = canvas.SaveLayer(_layerPaint);
		_layerPaint.ImageFilter = null;
		RenderNodeCore(node, canvas);
		canvas.RestoreToCount(count);
	}

	private void RenderChildren(IReadOnlyList<UiNode> children, SKCanvas canvas)
	{
		if (UiVisualTreeOrdering.IsBackToFront(children))
		{
			for (int i = 0; i < children.Count; i++)
			{
				RenderNode(children[i], canvas);
			}
			return;
		}

		if (_depth == _orderedChildren.Count)
		{
			_orderedChildren.Add(new List<UiNode>());
		}
		List<UiNode> ordered = _orderedChildren[_depth++];
		UiVisualTreeOrdering.CopyBackToFront(children, ordered);
		for (int i = 0; i < ordered.Count; i++)
		{
			RenderNode(ordered[i], canvas);
		}
		ordered.Clear();
		_depth--;
	}

	private SKPaint Fill(UiColor color)
	{
		_fillPaint.Shader = null;
		_fillPaint.ImageFilter = null;
		_fillPaint.BlendMode = SKBlendMode.SrcOver;
		_fillPaint.Color = color.ToSKColor();
		return _fillPaint;
	}

	private SKPaint Fill(SKColor color)
	{
		SKPaint paint = Fill(UiColor.Transparent);
		paint.Color = color;
		return paint;
	}

	private SKPaint Stroke(UiColor color, float width)
	{
		_strokePaint.PathEffect = null;
		_strokePaint.StrokeCap = SKStrokeCap.Butt;
		_strokePaint.Color = color.ToSKColor();
		_strokePaint.StrokeWidth = width;
		return _strokePaint;
	}

	private SKImageFilter BlurFilter(float blurRadius)
	{
		float sigma = ToSigma(blurRadius);
		if (!_blurFilters.TryGetValue(sigma, out SKImageFilter? filter))
		{
			filter = SKImageFilter.CreateBlur(sigma, sigma);
			_blurFilters[sigma] = filter;
		}
		return filter;
	}

	private static void DrawRect(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
	{
		if (radius > 0f)
		{
			canvas.DrawRoundRect(rect, radius, radius, paint);
		}
		else
		{
			canvas.DrawRect(rect, paint);
		}
	}

	private void DrawBackgrounds(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.BackgroundColor != UiColor.Transparent)
		{
			DrawRect(canvas, rect, style.BorderRadius, Fill(style.BackgroundColor));
		}
		if (style.BackgroundLayers.Count > 0)
		{
			for (int num = style.BackgroundLayers.Count - 1; num >= 0; num--)
			{
				UiBackgroundLayer uiBackgroundLayer = style.BackgroundLayers[num];
				if (uiBackgroundLayer.IsVisible)
				{
					if (uiBackgroundLayer.Color != UiColor.Transparent)
					{
						DrawRect(canvas, rect, style.BorderRadius, Fill(uiBackgroundLayer.Color));
					}
					if (uiBackgroundLayer.Gradient != null)
					{
						DrawGradientRect(canvas, rect, style.BorderRadius, uiBackgroundLayer.Gradient);
					}
					if (!string.IsNullOrWhiteSpace(uiBackgroundLayer.ImageSource))
					{
						DrawBackgroundImageLayer(canvas, rect, style, uiBackgroundLayer.ImageSource, num);
					}
				}
			}
		}
		else if (style.BackgroundGradient != null)
		{
			DrawGradientRect(canvas, rect, style.BorderRadius, style.BackgroundGradient);
		}
	}

	private void DrawGradientRect(SKCanvas canvas, SKRect rect, float radius, UiLinearGradient gradient)
	{
		using SKShader shader = CreateGradientShader(rect, gradient);
		SKPaint paint = Fill(SKColors.White);
		paint.Shader = shader;
		DrawRect(canvas, rect, radius, paint);
		paint.Shader = null;
	}

	private SKPaint BorderPaint(UiStyle style)
	{
		SKPaint paint = Stroke(style.BorderColor, style.BorderWidth);
		switch (style.BorderStyle)
		{
		case UiBorderStyle.Dashed:
			paint.PathEffect = BorderDash(UiBorderStyle.Dashed, style.BorderWidth, Math.Max(4f, style.BorderWidth * 3f), Math.Max(2f, style.BorderWidth * 2f));
			break;
		case UiBorderStyle.Dotted:
			paint.StrokeCap = SKStrokeCap.Round;
			paint.PathEffect = BorderDash(UiBorderStyle.Dotted, style.BorderWidth, Math.Max(1f, style.BorderWidth), Math.Max(2f, style.BorderWidth * 1.8f));
			break;
		}
		return paint;
	}

	private SKPathEffect BorderDash(UiBorderStyle borderStyle, float borderWidth, float on, float off)
	{
		if (!_borderDashes.TryGetValue((borderStyle, borderWidth), out SKPathEffect? effect))
		{
			effect = SKPathEffect.CreateDash(new float[2] { on, off }, 0f);
			_borderDashes[(borderStyle, borderWidth)] = effect;
		}
		return effect;
	}

	private void DrawBoxShadows(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.BoxShadows.Count > 0)
		{
			for (int i = 0; i < style.BoxShadows.Count; i++)
			{
				DrawBoxShadow(canvas, rect, style, style.BoxShadows[i]);
			}
		}
		else if (style.BoxShadow is { } shadow)
		{
			DrawBoxShadow(canvas, rect, style, shadow);
		}
	}

	private void DrawBoxShadow(SKCanvas canvas, SKRect rect, UiStyle style, UiShadow shadow)
	{
		if (!shadow.IsVisible)
		{
			return;
		}
		SKPaint paint = Fill(shadow.Color);
		paint.ImageFilter = shadow.BlurRadius > 0.01f ? BlurFilter(shadow.BlurRadius) : null;
		SKRect shadowRect = rect;
		shadowRect.Offset(shadow.OffsetX, shadow.OffsetY);
		shadowRect.Inflate(shadow.SpreadRadius, shadow.SpreadRadius);
		DrawRect(canvas, shadowRect, style.BorderRadius + shadow.SpreadRadius, paint);
		paint.ImageFilter = null;
	}

	private void DrawBackdropBlur(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.BackdropBlurRadius <= 0.01f || rect.Width <= 0.01f || rect.Height <= 0.01f)
		{
			return;
		}
		int count = canvas.Save();
		ClipNodeBounds(canvas, rect, style);
		// Bounds 不能填：SKCanvasSaveLayerRec.ToNative 取的是局部变量地址，传进原生层时已经失效。
		canvas.SaveLayer(new SKCanvasSaveLayerRec { Backdrop = BlurFilter(style.BackdropBlurRadius) });
		canvas.RestoreToCount(count);
	}

	private void DrawMask(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.MaskGradient == null)
		{
			return;
		}
		using SKShader shader = CreateGradientShader(rect, style.MaskGradient);
		SKPaint paint = Fill(SKColors.White);
		paint.BlendMode = SKBlendMode.DstIn;
		paint.Shader = shader;
		canvas.DrawPath(BuildClipPath(rect, style), paint);
		paint.Shader = null;
		paint.BlendMode = SKBlendMode.SrcOver;
	}

	private void DrawScrollbars(SKCanvas canvas, UiNode node)
	{
		if (node.Style.Overflow != UiOverflow.Scroll)
		{
			return;
		}
		UiStyle renderStyle = node.RenderStyle;
		SKColor color = ((renderStyle.BorderColor.Alpha > 0) ? renderStyle.BorderColor.WithAlpha((byte)Math.Max((int)renderStyle.BorderColor.Alpha, 72)).ToSKColor() : new SKColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, 56));
		SKColor color2 = ((renderStyle.OutlineColor.Alpha > 0) ? renderStyle.OutlineColor.WithAlpha((byte)Math.Max((int)renderStyle.OutlineColor.Alpha, 172)).ToSKColor() : ((renderStyle.Color.Alpha > 0) ? renderStyle.Color.WithAlpha(180).ToSKColor() : new SKColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, 180)));
		if (UiScrollGeometry.HasVerticalScrollbar(node))
		{
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetVerticalTrackRect(node)), 5f, Fill(color));
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetVerticalThumbRect(node)), 5f, Fill(color2));
		}
		if (UiScrollGeometry.HasHorizontalScrollbar(node))
		{
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetHorizontalTrackRect(node)), 5f, Fill(color));
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetHorizontalThumbRect(node)), 5f, Fill(color2));
		}
	}

	private static SKRect ToSkRect(UiRect rect)
	{
		return new SKRect(rect.X, rect.Y, rect.Right, rect.Bottom);
	}

	private static ReadOnlySpan<char> ResolveRenderableText(UiNode node)
	{
		if (node.HasText)
		{
			return node.Text;
		}
		UiNodeKind kind = node.Kind;
		if ((kind == UiNodeKind.Input || kind - 12 <= UiNodeKind.Text) ? true : false)
		{
			string? text = node.Attributes["value"];
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			string? text2 = node.Attributes["placeholder"];
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text2;
			}
		}
		return default;
	}

	private void DrawText(ReadOnlySpan<char> text, UiNode node, SKRect borderBox, UiStyle style, SKCanvas canvas)
	{
		if (node.TextRuns is { Count: > 0 } styledRuns)
		{
			DrawStyledText(styledRuns, node, borderBox, style, canvas);
			return;
		}

		SKRect content = ContentBox(borderBox, style);
		UiTextLines lines = _textLines;
		UiTextLayout.LayoutLines(text, style, Math.Max(0f, content.Width), constrainWidth: true, lines);
		float lineHeight = style.FontSize * style.LineHeight;
		SKRect textBox = AlignTextBox(node, content, borderBox, style, lines.MaxWidth, lineHeight * lines.Count);
		_textPaint.Color = style.Color.ToSKColor();
		SKPaint? shadowPaint = ConfigureTextShadowPaint(style.TextShadow);
		float baselineY = textBox.Top + style.FontSize;
		for (int i = 0; i < lines.Count; i++)
		{
			ref readonly UiTextLines.Line line = ref lines[i];
			ReadOnlySpan<char> lineText = lines.Text(i);
			UiTextDirection direction = UiTextLayout.ResolveDirection(lineText, style.Direction);
			SKTextAlign align = ResolveTextAlign(style, direction);
			float anchorX = ResolveTextAnchor(textBox, style, align);
			float startX = AlignStart(anchorX, line.Width, align);
			if (shadowPaint != null)
			{
				UiShadow shadow = style.TextShadow!.Value;
				DrawTextLine(canvas, lineText, in line, startX + shadow.OffsetX, baselineY + shadow.OffsetY, style, shadowPaint);
			}
			DrawTextLine(canvas, lineText, in line, startX, baselineY, style, _textPaint);
			DrawTextDecorations(canvas, line.Width, anchorX, baselineY, align, style);
			baselineY += lineHeight;
		}
	}

	private static void DrawTextLine(SKCanvas canvas, ReadOnlySpan<char> text, in UiTextLines.Line line, float x, float baselineY, UiStyle style, SKPaint paint)
	{
		UiTextLayout.DrawRun(canvas, text, x, baselineY, style, paint);
		if (line.HasEllipsis)
		{
			UiTextLayout.DrawEllipsis(canvas, x + line.ContentWidth, baselineY, style, paint);
		}
	}

	private static SKRect AlignTextBox(UiNode node, SKRect content, SKRect borderBox, UiStyle style, float textWidth, float textHeight)
	{
		if (!UiFlexAnonymousText.ShouldAlignAsAnonymousFlexItem(node))
		{
			return content;
		}
		UiRect aligned = UiFlexAnonymousText.ResolveTextBox(
			style,
			new UiRect(borderBox.Left, borderBox.Top, borderBox.Width, borderBox.Height),
			textWidth,
			textHeight);
		return new SKRect(aligned.X, aligned.Y, aligned.Right, aligned.Bottom);
	}

	private static float AlignStart(float anchorX, float width, SKTextAlign align)
	{
		return align switch
		{
			SKTextAlign.Right => anchorX - width,
			SKTextAlign.Center => anchorX - width * 0.5f,
			_ => anchorX,
		};
	}

	private SKPaint? ConfigureTextShadowPaint(UiShadow? shadow)
	{
		if (shadow is not { IsVisible: true } visible)
		{
			return null;
		}
		_textShadowPaint.Color = visible.Color.ToSKColor();
		_textShadowPaint.ImageFilter = visible.BlurRadius > 0.01f ? BlurFilter(visible.BlurRadius) : null;
		return _textShadowPaint;
	}

	private void DrawStyledText(IReadOnlyList<UiStyledTextRun> styledRuns, UiNode node, SKRect borderBox, UiStyle style, SKCanvas canvas)
	{
		SKRect content = ContentBox(borderBox, style);
		float availableWidth = Math.Max(0f, content.Width);
		UiTextLayoutResult layout = UiTextLayout.Measure(styledRuns, style, availableWidth, constrainWidth: true);
		SKRect textBox = AlignTextBox(node, content, borderBox, style, layout.Width, layout.Height);
		_textPaint.Color = style.Color.ToSKColor();
		SKPaint? shadowPaint = ConfigureTextShadowPaint(style.TextShadow);
		float baselineY = textBox.Top + style.FontSize;
		using SKPaint measurePaint = UiTextLayout.CreatePaint(style);
		IReadOnlyList<IReadOnlyList<UiStyledTextRun>> styledLines = UiTextLayout.BreakStyledLines(
			UiStyledTextRunNormalization.NormalizeWordBoundaries(styledRuns),
			style,
			measurePaint,
			availableWidth,
			constrainWidth: true);
		for (int i = 0; i < layout.Lines.Count; i++)
		{
			string lineText = layout.Lines[i];
			UiTextDirection direction = UiTextLayout.ResolveDirection(lineText, style.Direction);
			SKTextAlign align = ResolveTextAlign(style, direction);
			float anchorX = ResolveTextAnchor(textBox, style, align);
			float lineWidth = UiTextLayout.MeasureWidth(lineText, style);
			float startX = AlignStart(anchorX, lineWidth, align);
			IReadOnlyList<UiTextRun> runs = i < styledLines.Count
				? UiTextLayout.CreateRuns(styledLines[i], style)
				: UiTextLayout.CreateRuns(lineText, style);
			if (shadowPaint != null)
			{
				UiShadow shadow = style.TextShadow!.Value;
				DrawTextRuns(canvas, runs, startX + shadow.OffsetX, baselineY + shadow.OffsetY, style, shadowPaint);
			}
			DrawTextRuns(canvas, runs, startX, baselineY, style, _textPaint);
			DrawTextDecorations(canvas, lineWidth, anchorX, baselineY, align, style);
			baselineY += layout.LineHeight;
		}
	}

	private static SKRect ContentBox(SKRect borderBox, UiStyle style)
	{
		float inset = Math.Max(0f, style.BorderWidth);
		return new SKRect(
			borderBox.Left + inset + style.Padding.Left,
			borderBox.Top + inset + style.Padding.Top,
			borderBox.Right - inset - style.Padding.Right,
			borderBox.Bottom - inset - style.Padding.Bottom);
	}

	private void DrawImage(UiNode node, SKRect rect, UiStyle style, SKCanvas canvas)
	{
		if (!UiImageSourceCache.TryGetResource(node.Attributes["src"], out UiImageSourceCache.UiImageResource resource) || resource == null)
		{
			return;
		}
		SKRect sKRect = new SKRect(rect.Left + style.Padding.Left, rect.Top + style.Padding.Top, rect.Right - style.Padding.Right, rect.Bottom - style.Padding.Bottom);
		if (!(sKRect.Width <= 0.01f) && !(sKRect.Height <= 0.01f))
		{
			int count = canvas.Save();
			ClipNodeBounds(canvas, sKRect, style);
			if (HasRequestedImageSlice(style))
			{
				if (resource.RasterImage != null && TryBuildNineSlicePatches(resource.RasterImage.Width, resource.RasterImage.Height, 0f, 0f, sKRect, style.ImageSlice, out NineSlicePatchSet patches))
				{
					DrawNineSliceImage(canvas, resource.RasterImage, patches);
				}
				else if (resource.SvgPicture != null && TryBuildNineSlicePatches(resource.SourceBounds.Width, resource.SourceBounds.Height, resource.SourceBounds.Left, resource.SourceBounds.Top, sKRect, style.ImageSlice, out patches))
				{
					DrawNineSlicePicture(canvas, resource.SvgPicture, patches);
				}
				canvas.RestoreToCount(count);
			}
			else
			{
				SKRect destination = ResolveObjectFitRect(sKRect, resource.Width, resource.Height, style.ObjectFit);
				DrawImageResource(canvas, resource, destination);
				canvas.RestoreToCount(count);
			}
		}
	}

	private void DrawCanvas(UiNode node, SKRect rect, UiStyle style, SKCanvas canvas)
	{
		if (node.CanvasContent is ISkiaUiCanvasContent skiaCanvas)
		{
			SKRect rect2 = new SKRect(rect.Left + style.Padding.Left, rect.Top + style.Padding.Top, rect.Right - style.Padding.Right, rect.Bottom - style.Padding.Bottom);
			if (!(rect2.Width <= 0.01f) && !(rect2.Height <= 0.01f))
			{
				int count = canvas.Save();
				ClipNodeBounds(canvas, rect2, style);
				skiaCanvas.Draw(canvas, rect2);
				canvas.RestoreToCount(count);
			}
		}
	}

	private static bool HasRequestedImageSlice(UiStyle style)
	{
		UiThickness imageSlice = style.ImageSlice;
		return imageSlice.Left > 0f || imageSlice.Top > 0f || imageSlice.Right > 0f || imageSlice.Bottom > 0f;
	}

	private static bool TryBuildNineSlicePatches(float sourceWidth, float sourceHeight, float sourceLeft, float sourceTop, SKRect destination, UiThickness slice, out NineSlicePatchSet patches)
	{
		patches = default;
		if (sourceWidth <= 0.01f || sourceHeight <= 0.01f ||
			slice.Left < 0f || slice.Top < 0f || slice.Right < 0f || slice.Bottom < 0f ||
			slice.Left + slice.Right >= sourceWidth || slice.Top + slice.Bottom >= sourceHeight)
		{
			return false;
		}
		float left = slice.Left;
		float top = slice.Top;
		float right = slice.Right;
		float bottom = slice.Bottom;
		float destinationLeft = Math.Min(left, destination.Width);
		float destinationTop = Math.Min(top, destination.Height);
		float destinationRight = Math.Min(right, Math.Max(0f, destination.Width - destinationLeft));
		float destinationBottom = Math.Min(bottom, Math.Max(0f, destination.Height - destinationTop));
		float sourceCenterWidth = Math.Max(0f, sourceWidth - left - right);
		float sourceCenterHeight = Math.Max(0f, sourceHeight - top - bottom);
		float destinationCenterWidth = Math.Max(0f, destination.Width - destinationLeft - destinationRight);
		float destinationCenterHeight = Math.Max(0f, destination.Height - destinationTop - destinationBottom);
		patches = new NineSlicePatchSet(
			new NineSlicePatch(new SKRect(sourceLeft, sourceTop, sourceLeft + left, sourceTop + top), new SKRect(destination.Left, destination.Top, destination.Left + destinationLeft, destination.Top + destinationTop)),
			new NineSlicePatch(new SKRect(sourceLeft + left, sourceTop, sourceLeft + left + sourceCenterWidth, sourceTop + top), new SKRect(destination.Left + destinationLeft, destination.Top, destination.Right - destinationRight, destination.Top + destinationTop)),
			new NineSlicePatch(new SKRect(sourceLeft + sourceWidth - right, sourceTop, sourceLeft + sourceWidth, sourceTop + top), new SKRect(destination.Right - destinationRight, destination.Top, destination.Right, destination.Top + destinationTop)),
			new NineSlicePatch(new SKRect(sourceLeft, sourceTop + top, sourceLeft + left, sourceTop + top + sourceCenterHeight), new SKRect(destination.Left, destination.Top + destinationTop, destination.Left + destinationLeft, destination.Bottom - destinationBottom)),
			new NineSlicePatch(new SKRect(sourceLeft + left, sourceTop + top, sourceLeft + left + sourceCenterWidth, sourceTop + top + sourceCenterHeight), new SKRect(destination.Left + destinationLeft, destination.Top + destinationTop, destination.Left + destinationLeft + destinationCenterWidth, destination.Top + destinationTop + destinationCenterHeight)),
			new NineSlicePatch(new SKRect(sourceLeft + sourceWidth - right, sourceTop + top, sourceLeft + sourceWidth, sourceTop + top + sourceCenterHeight), new SKRect(destination.Right - destinationRight, destination.Top + destinationTop, destination.Right, destination.Bottom - destinationBottom)),
			new NineSlicePatch(new SKRect(sourceLeft, sourceTop + sourceHeight - bottom, sourceLeft + left, sourceTop + sourceHeight), new SKRect(destination.Left, destination.Bottom - destinationBottom, destination.Left + destinationLeft, destination.Bottom)),
			new NineSlicePatch(new SKRect(sourceLeft + left, sourceTop + sourceHeight - bottom, sourceLeft + left + sourceCenterWidth, sourceTop + sourceHeight), new SKRect(destination.Left + destinationLeft, destination.Bottom - destinationBottom, destination.Right - destinationRight, destination.Bottom)),
			new NineSlicePatch(new SKRect(sourceLeft + sourceWidth - right, sourceTop + sourceHeight - bottom, sourceLeft + sourceWidth, sourceTop + sourceHeight), new SKRect(destination.Right - destinationRight, destination.Bottom - destinationBottom, destination.Right, destination.Bottom)));
		return true;
	}

	private void DrawNineSliceImage(SKCanvas canvas, SKImage image, NineSlicePatchSet patches)
	{
		for (int i = 0; i < patches.Count; i++)
		{
			NineSlicePatch patch = patches[i];
			DrawImagePatch(canvas, image, patch.Source, patch.Destination, _imagePaint);
		}
	}

	private static void DrawImagePatch(SKCanvas canvas, SKImage image, SKRect source, SKRect destination, SKPaint paint)
	{
		if (!(source.Width <= 0.01f) && !(source.Height <= 0.01f) && !(destination.Width <= 0.01f) && !(destination.Height <= 0.01f))
		{
			canvas.DrawImage(image, source, destination, paint);
		}
	}

	private static void DrawNineSlicePicture(SKCanvas canvas, SKPicture picture, NineSlicePatchSet patches)
	{
		for (int i = 0; i < patches.Count; i++)
		{
			NineSlicePatch patch = patches[i];
			DrawPicturePatch(canvas, picture, patch.Source, patch.Destination);
		}
	}

	private static void DrawPicturePatch(SKCanvas canvas, SKPicture picture, SKRect source, SKRect destination)
	{
		if (source.Width <= 0.01f || source.Height <= 0.01f || destination.Width <= 0.01f || destination.Height <= 0.01f)
		{
			return;
		}
		int count = canvas.Save();
		canvas.ClipRect(destination, SKClipOperation.Intersect, antialias: true);
		float scaleX = destination.Width / source.Width;
		float scaleY = destination.Height / source.Height;
		canvas.Translate(destination.Left - source.Left * scaleX, destination.Top - source.Top * scaleY);
		canvas.Scale(scaleX, scaleY);
		canvas.DrawPicture(picture);
		canvas.RestoreToCount(count);
	}

	private readonly struct NineSlicePatch
	{
		internal SKRect Source { get; }

		internal SKRect Destination { get; }

		internal NineSlicePatch(SKRect source, SKRect destination)
		{
			Source = source;
			Destination = destination;
		}
	}

	private readonly struct NineSlicePatchSet
	{
		private readonly NineSlicePatch _p0;
		private readonly NineSlicePatch _p1;
		private readonly NineSlicePatch _p2;
		private readonly NineSlicePatch _p3;
		private readonly NineSlicePatch _p4;
		private readonly NineSlicePatch _p5;
		private readonly NineSlicePatch _p6;
		private readonly NineSlicePatch _p7;
		private readonly NineSlicePatch _p8;

		internal int Count => 9;

		internal NineSlicePatchSet(NineSlicePatch p0, NineSlicePatch p1, NineSlicePatch p2, NineSlicePatch p3, NineSlicePatch p4, NineSlicePatch p5, NineSlicePatch p6, NineSlicePatch p7, NineSlicePatch p8)
		{
			_p0 = p0;
			_p1 = p1;
			_p2 = p2;
			_p3 = p3;
			_p4 = p4;
			_p5 = p5;
			_p6 = p6;
			_p7 = p7;
			_p8 = p8;
		}

		internal NineSlicePatch this[int index] => index switch
		{
			0 => _p0,
			1 => _p1,
			2 => _p2,
			3 => _p3,
			4 => _p4,
			5 => _p5,
			6 => _p6,
			7 => _p7,
			8 => _p8,
			_ => throw new ArgumentOutOfRangeException(nameof(index)),
		};
	}

	private void DrawBackgroundImageLayer(SKCanvas canvas, SKRect rect, UiStyle style, string imageSource, int layerIndex)
	{
		if (UiImageSourceCache.TryGetResource(imageSource, out UiImageSourceCache.UiImageResource resource) && resource != null)
		{
			UiBackgroundSize size = ResolveBackgroundLayerValue(style.BackgroundSizes, layerIndex, UiBackgroundSize.Auto);
			UiBackgroundPosition position = ResolveBackgroundLayerValue(style.BackgroundPositions, layerIndex, UiBackgroundPosition.TopLeft);
			UiBackgroundRepeat repeat = ResolveBackgroundLayerValue(style.BackgroundRepeats, layerIndex, UiBackgroundRepeat.Repeat);
			SKSize tileSize = ResolveBackgroundTileSize(size, rect, resource);
			if (!(tileSize.Width <= 0.01f) && !(tileSize.Height <= 0.01f))
			{
				int count = canvas.Save();
				ClipNodeBounds(canvas, rect, style);
				DrawRepeatedBackground(canvas, resource, rect, tileSize, position, repeat);
				canvas.RestoreToCount(count);
			}
		}
	}

	private void DrawRepeatedBackground(SKCanvas canvas, UiImageSourceCache.UiImageResource resource, SKRect area, SKSize tileSize, UiBackgroundPosition position, UiBackgroundRepeat repeat)
	{
		bool flag = ((repeat == UiBackgroundRepeat.Repeat || repeat == UiBackgroundRepeat.RepeatX) ? true : false);
		bool flag2 = flag;
		flag = ((repeat == UiBackgroundRepeat.Repeat || repeat == UiBackgroundRepeat.RepeatY) ? true : false);
		bool flag3 = flag;
		float num = area.Left + ResolveBackgroundAxisOffset(position.X, area.Width - tileSize.Width);
		float num2 = area.Top + ResolveBackgroundAxisOffset(position.Y, area.Height - tileSize.Height);
		if (flag2)
		{
			num = NormalizeRepeatStart(num, tileSize.Width, area.Left, area.Right);
		}
		if (flag3)
		{
			num2 = NormalizeRepeatStart(num2, tileSize.Height, area.Top, area.Bottom);
		}
		float num3 = (flag2 ? area.Right : (num + tileSize.Width));
		float num4 = (flag3 ? area.Bottom : (num2 + tileSize.Height));
		for (float num5 = num2; num5 < num4; num5 += tileSize.Height)
		{
			for (float num6 = num; num6 < num3; num6 += tileSize.Width)
			{
				SKRect destination = new SKRect(num6, num5, num6 + tileSize.Width, num5 + tileSize.Height);
				DrawImageResource(canvas, resource, destination);
				if (!flag2)
				{
					break;
				}
			}
			if (!flag3)
			{
				break;
			}
		}
	}

	private static SKSize ResolveBackgroundTileSize(UiBackgroundSize size, SKRect area, UiImageSourceCache.UiImageResource resource)
	{
		float num = Math.Max(1f, resource.Width);
		float num2 = Math.Max(1f, resource.Height);
		float val = area.Width / num;
		float val2 = area.Height / num2;
		UiBackgroundSizeMode mode = size.Mode;
		if (1 == 0)
		{
		}
		SKSize result = mode switch
		{
			UiBackgroundSizeMode.Cover => new SKSize(num * Math.Max(val, val2), num2 * Math.Max(val, val2)), 
			UiBackgroundSizeMode.Contain => new SKSize(num * Math.Min(val, val2), num2 * Math.Min(val, val2)), 
			UiBackgroundSizeMode.Explicit => ResolveExplicitBackgroundSize(size, area, num, num2), 
			_ => new SKSize(num, num2), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static SKSize ResolveExplicitBackgroundSize(UiBackgroundSize size, SKRect area, float sourceWidth, float sourceHeight)
	{
		bool isAuto = size.Width.IsAuto;
		bool isAuto2 = size.Height.IsAuto;
		float num = (isAuto ? 0f : ResolveLength(size.Width, area.Width));
		float num2 = (isAuto2 ? 0f : ResolveLength(size.Height, area.Height));
		if (isAuto && isAuto2)
		{
			return new SKSize(sourceWidth, sourceHeight);
		}
		if (isAuto)
		{
			num = num2 * (sourceWidth / Math.Max(0.01f, sourceHeight));
		}
		else if (isAuto2)
		{
			num2 = num * (sourceHeight / Math.Max(0.01f, sourceWidth));
		}
		return new SKSize(Math.Max(0.01f, num), Math.Max(0.01f, num2));
	}

	private static float ResolveBackgroundAxisOffset(UiLength length, float available)
	{
		if (length.IsAuto)
		{
			return 0f;
		}
		float resolved = length.Resolve(available);
		return float.IsNaN(resolved) ? 0f : resolved;
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
		T result;
		if (layerIndex >= values.Count)
		{
			result = values[values.Count - 1];
		}
		else
		{
			result = values[layerIndex];
		}
		return result;
	}

	private void DrawImageResource(SKCanvas canvas, UiImageSourceCache.UiImageResource resource, SKRect destination)
	{
		if (resource.RasterImage != null)
		{
			canvas.DrawImage(resource.RasterImage, destination, _imagePaint);
			return;
		}
		if (resource.SvgPicture != null)
		{
			int count = canvas.Save();
			float num = destination.Width / Math.Max(0.01f, resource.SourceBounds.Width);
			float num2 = destination.Height / Math.Max(0.01f, resource.SourceBounds.Height);
			canvas.Translate(destination.Left - resource.SourceBounds.Left * num, destination.Top - resource.SourceBounds.Top * num2);
			canvas.Scale(num, num2);
			canvas.DrawPicture(resource.SvgPicture);
			canvas.RestoreToCount(count);
		}
	}

	private void ClipNodeBounds(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.ClipPath == null && style.BorderRadius <= 0.01f)
		{
			canvas.ClipRect(rect, SKClipOperation.Intersect, antialias: true);
			return;
		}
		canvas.ClipPath(BuildClipPath(rect, style), SKClipOperation.Intersect, antialias: true);
	}

	private SKPath BuildClipPath(SKRect rect, UiStyle style)
	{
		SKPath sKPath = _clipPath;
		sKPath.Reset();
		if (style.ClipPath != null)
		{
			switch (style.ClipPath.Kind)
			{
			case UiClipPathKind.Inset:
			{
				UiThickness inset = style.ClipPath.Inset;
				SKRect rect2 = new SKRect(rect.Left + inset.Left, rect.Top + inset.Top, rect.Right - inset.Right, rect.Bottom - inset.Bottom);
				if (style.BorderRadius > 0.01f)
				{
					sKPath.AddRoundRect(rect2, style.BorderRadius, style.BorderRadius);
				}
				else
				{
					sKPath.AddRect(rect2);
				}
				return sKPath;
			}
			case UiClipPathKind.Circle:
			{
				float val = ResolveLength(style.ClipPath.Radius, Math.Min(rect.Width, rect.Height));
				float x = rect.Left + ResolveLength(style.ClipPath.CenterX, rect.Width);
				float y = rect.Top + ResolveLength(style.ClipPath.CenterY, rect.Height);
				sKPath.AddCircle(x, y, Math.Max(0f, val));
				return sKPath;
			}
			}
		}
		if (style.BorderRadius > 0.01f)
		{
			sKPath.AddRoundRect(rect, style.BorderRadius, style.BorderRadius);
		}
		else
		{
			sKPath.AddRect(rect);
		}
		return sKPath;
	}

	private static float ResolveLength(UiLength length, float available)
	{
		if (length.IsAuto)
		{
			return 0f;
		}
		float resolved = length.Resolve(available);
		return float.IsNaN(resolved) ? 0f : resolved;
	}

	private static SKRect ResolveObjectFitRect(SKRect contentRect, float sourceWidth, float sourceHeight, UiObjectFit objectFit)
	{
		float val = contentRect.Width / Math.Max(1f, sourceWidth);
		float val2 = contentRect.Height / Math.Max(1f, sourceHeight);
		if (1 == 0)
		{
		}
		float num = objectFit switch
		{
			UiObjectFit.Contain => Math.Min(val, val2), 
			UiObjectFit.Cover => Math.Max(val, val2), 
			UiObjectFit.None => 1f, 
			UiObjectFit.ScaleDown => (sourceWidth <= contentRect.Width && sourceHeight <= contentRect.Height) ? 1f : Math.Min(val, val2), 
			_ => -1f, 
		};
		if (1 == 0)
		{
		}
		float num2 = num;
		if (objectFit == UiObjectFit.Fill || num2 < 0f)
		{
			return contentRect;
		}
		float num3 = sourceWidth * num2;
		float num4 = sourceHeight * num2;
		float num5 = contentRect.Left + (contentRect.Width - num3) * 0.5f;
		float num6 = contentRect.Top + (contentRect.Height - num4) * 0.5f;
		return new SKRect(num5, num6, num5 + num3, num6 + num4);
	}

	private static SKTextAlign ResolveTextAlign(UiStyle style, UiTextDirection direction)
	{
		UiTextAlign textAlign = style.TextAlign;
		if (1 == 0)
		{
		}
		SKTextAlign result = textAlign switch
		{
			UiTextAlign.Left => SKTextAlign.Left, 
			UiTextAlign.Right => SKTextAlign.Right, 
			UiTextAlign.Center => SKTextAlign.Center, 
			UiTextAlign.End => (direction != UiTextDirection.Rtl) ? SKTextAlign.Right : SKTextAlign.Left, 
			_ => (direction == UiTextDirection.Rtl) ? SKTextAlign.Right : SKTextAlign.Left, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static float ResolveTextAnchor(SKRect contentRect, UiStyle style, SKTextAlign align)
	{
		return align switch
		{
			SKTextAlign.Right => contentRect.Right,
			SKTextAlign.Center => contentRect.MidX,
			_ => contentRect.Left,
		};
	}

	private static void DrawTextRuns(SKCanvas canvas, IReadOnlyList<UiTextRun> runs, float startX, float baselineY, UiStyle style, SKPaint paint)
	{
		float num = startX;
		SKColor baseColor = paint.Color;
		foreach (UiTextRun run in runs)
		{
			using SKFont sKFont = new SKFont(run.Typeface, style.FontSize);
			if (run.HasColor)
			{
				paint.Color = run.Color.ToSKColor();
			}
			else
			{
				paint.Color = baseColor;
			}

			canvas.DrawText(run.Text, num, baselineY, SKTextAlign.Left, sKFont, paint);
			num += sKFont.MeasureText(run.Text, paint);
		}

		paint.Color = baseColor;
	}

	private void DrawTextDecorations(SKCanvas canvas, float lineWidth, float anchorX, float baselineY, SKTextAlign align, UiStyle style)
	{
		if (style.TextDecorationLine == UiTextDecorationLine.None || lineWidth <= 0.01f)
		{
			return;
		}
		float startX = AlignStart(anchorX, lineWidth, align);
		float endX = startX + lineWidth;
		SKPaint paint = Stroke(style.Color, Math.Max(1f, style.FontSize * 0.06f));
		if ((style.TextDecorationLine & UiTextDecorationLine.Underline) != UiTextDecorationLine.None)
		{
			float y = baselineY + Math.Max(1.5f, style.FontSize * 0.12f);
			canvas.DrawLine(startX, y, endX, y, paint);
		}
		if ((style.TextDecorationLine & UiTextDecorationLine.LineThrough) != UiTextDecorationLine.None)
		{
			float y = baselineY - style.FontSize * 0.32f;
			canvas.DrawLine(startX, y, endX, y, paint);
		}
	}

	private static SKShader CreateGradientShader(SKRect rect, UiLinearGradient gradient)
	{
		float x = gradient.AngleDegrees * ((float)Math.PI / 180f);
		SKPoint sKPoint = new SKPoint(rect.MidX, rect.MidY);
		SKPoint sKPoint2 = new SKPoint(MathF.Cos(x), MathF.Sin(x));
		float num = MathF.Max(rect.Width, rect.Height);
		SKPoint start = new SKPoint(sKPoint.X - sKPoint2.X * num, sKPoint.Y - sKPoint2.Y * num);
		SKPoint end = new SKPoint(sKPoint.X + sKPoint2.X * num, sKPoint.Y + sKPoint2.Y * num);
		return SKShader.CreateLinearGradient(start, end, gradient.Stops.Select((UiGradientStop stop) => stop.Color.ToSKColor()).ToArray(), gradient.Stops.Select((UiGradientStop stop) => stop.Position).ToArray(), SKShaderTileMode.Clamp);
	}

	private static float ToSigma(float blurRadius)
	{
		return Math.Max(0.01f, blurRadius * 0.5f);
	}
}
