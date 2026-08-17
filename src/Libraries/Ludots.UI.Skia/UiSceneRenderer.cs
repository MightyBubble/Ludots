using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.UI.Runtime;
using SkiaSharp;

namespace Ludots.UI.Skia;

public sealed class SkiaUiRenderer : IUiRenderer
{
	private SKCanvas? _canvas;

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
		int width2 = Math.Max(1, (int)Math.Ceiling(width));
		int height2 = Math.Max(1, (int)Math.Ceiling(height));
		using SKSurface sKSurface = SKSurface.Create(new SKImageInfo(width2, height2));
		SKCanvas canvas2 = sKSurface.Canvas;
		canvas2.Clear(SKColors.Transparent);
		RenderNode(scene.Root, canvas2, sKSurface);
		using SKImage image = sKSurface.Snapshot();
		canvas.DrawImage(image, 0f, 0f);
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

	private void RenderNode(UiNode node, SKCanvas canvas, SKSurface surface)
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
				RenderNodeCore(node, canvas, surface);
			}
		}
	}

	private void RenderNodeCore(UiNode node, SKCanvas canvas, SKSurface surface)
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
			num = canvas.SaveLayer(new SKPaint
			{
				Color = SKColors.White.WithAlpha((byte)Math.Clamp(renderStyle.Opacity * 255f, 0f, 255f))
			});
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
		DrawBackdropBlur(canvas, surface, sKRect, renderStyle);
		DrawBoxShadows(canvas, sKRect, renderStyle);
		DrawBackgrounds(canvas, sKRect, renderStyle);
		if (renderStyle.BorderWidth > 0f && renderStyle.BorderColor != UiColor.Transparent)
		{
			using SKPaint paint = CreateBorderPaint(renderStyle);
			DrawRect(canvas, sKRect, renderStyle.BorderRadius, paint);
		}
		if (renderStyle.OutlineWidth > 0f && renderStyle.OutlineColor != UiColor.Transparent)
		{
			using SKPaint paint2 = new SKPaint
			{
				Color = renderStyle.OutlineColor.ToSKColor(),
				IsAntialias = true,
				Style = SKPaintStyle.Stroke,
				StrokeWidth = renderStyle.OutlineWidth
			};
			SKRect rect = sKRect;
			rect.Inflate(renderStyle.OutlineWidth * 0.5f, renderStyle.OutlineWidth * 0.5f);
			DrawRect(canvas, rect, renderStyle.BorderRadius + renderStyle.OutlineWidth * 0.5f, paint2);
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
		string text = ResolveRenderableText(node);
		if (!string.IsNullOrWhiteSpace(text))
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
		foreach (UiNode item in UiVisualTreeOrdering.BackToFront(node.Children))
		{
			RenderNode(item, canvas, surface);
		}
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
		UiStyle renderStyle = node.RenderStyle;
		SKRect sKRect = new SKRect(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Right, node.LayoutRect.Bottom);
		int num = (int)Math.Ceiling(Math.Max(2f, renderStyle.FilterBlurRadius * 3f));
		int num2 = (int)Math.Floor(sKRect.Left) - num;
		int num3 = (int)Math.Floor(sKRect.Top) - num;
		int width = Math.Max(1, (int)Math.Ceiling(sKRect.Width) + num * 2);
		int height = Math.Max(1, (int)Math.Ceiling(sKRect.Height) + num * 2);
		using SKSurface sKSurface = SKSurface.Create(new SKImageInfo(width, height));
		SKCanvas canvas2 = sKSurface.Canvas;
		canvas2.Clear(SKColors.Transparent);
		canvas2.Translate(-num2, -num3);
		RenderNodeCore(node, canvas2, sKSurface);
		using SKImage image = sKSurface.Snapshot();
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			ImageFilter = SKImageFilter.CreateBlur(ToSigma(renderStyle.FilterBlurRadius), ToSigma(renderStyle.FilterBlurRadius))
		};
		canvas.DrawImage(image, num2, num3, paint);
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

	private static void DrawBackgrounds(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.BackgroundColor != UiColor.Transparent)
		{
			using SKPaint paint = new SKPaint
			{
				Color = style.BackgroundColor.ToSKColor(),
				IsAntialias = true,
				Style = SKPaintStyle.Fill
			};
			DrawRect(canvas, rect, style.BorderRadius, paint);
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
						using SKPaint paint2 = new SKPaint
						{
							Color = uiBackgroundLayer.Color.ToSKColor(),
							IsAntialias = true,
							Style = SKPaintStyle.Fill
						};
						DrawRect(canvas, rect, style.BorderRadius, paint2);
					}
					if (uiBackgroundLayer.Gradient != null)
					{
						using SKPaint paint3 = new SKPaint
						{
							IsAntialias = true,
							Style = SKPaintStyle.Fill,
							Shader = CreateGradientShader(rect, uiBackgroundLayer.Gradient)
						};
						DrawRect(canvas, rect, style.BorderRadius, paint3);
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
			using (SKPaint paint4 = new SKPaint
			{
				IsAntialias = true,
				Style = SKPaintStyle.Fill,
				Shader = CreateGradientShader(rect, style.BackgroundGradient)
			})
			{
				DrawRect(canvas, rect, style.BorderRadius, paint4);
			}
		}
	}

	private static SKPaint CreateBorderPaint(UiStyle style)
	{
		SKPaint sKPaint = new SKPaint
		{
			Color = style.BorderColor.ToSKColor(),
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = style.BorderWidth
		};
		switch (style.BorderStyle)
		{
		case UiBorderStyle.Dashed:
			sKPaint.PathEffect = SKPathEffect.CreateDash(new float[2]
			{
				Math.Max(4f, style.BorderWidth * 3f),
				Math.Max(2f, style.BorderWidth * 2f)
			}, 0f);
			break;
		case UiBorderStyle.Dotted:
			sKPaint.StrokeCap = SKStrokeCap.Round;
			sKPaint.PathEffect = SKPathEffect.CreateDash(new float[2]
			{
				Math.Max(1f, style.BorderWidth),
				Math.Max(2f, style.BorderWidth * 1.8f)
			}, 0f);
			break;
		}
		return sKPaint;
	}

	private static void DrawBoxShadows(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		IReadOnlyList<UiShadow> readOnlyList2;
		if (style.BoxShadows.Count <= 0)
		{
			IReadOnlyList<UiShadow> readOnlyList = ((!style.BoxShadow.HasValue) ? ((IReadOnlyList<UiShadow>)Array.Empty<UiShadow>()) : ((IReadOnlyList<UiShadow>)new UiShadow[1] { style.BoxShadow.Value }));
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = style.BoxShadows;
		}
		IReadOnlyList<UiShadow> readOnlyList3 = readOnlyList2;
		if (readOnlyList3.Count == 0)
		{
			return;
		}
		for (int i = 0; i < readOnlyList3.Count; i++)
		{
			UiShadow uiShadow = readOnlyList3[i];
			if (uiShadow.IsVisible)
			{
				using SKPaint paint = new SKPaint
				{
					Color = uiShadow.Color.ToSKColor(),
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					ImageFilter = ((uiShadow.BlurRadius > 0.01f) ? SKImageFilter.CreateBlur(ToSigma(uiShadow.BlurRadius), ToSigma(uiShadow.BlurRadius)) : null)
				};
				SKRect rect2 = rect;
				rect2.Offset(uiShadow.OffsetX, uiShadow.OffsetY);
				rect2.Inflate(uiShadow.SpreadRadius, uiShadow.SpreadRadius);
				DrawRect(canvas, rect2, style.BorderRadius + uiShadow.SpreadRadius, paint);
			}
		}
	}

	private static void DrawBackdropBlur(SKCanvas canvas, SKSurface surface, SKRect rect, UiStyle style)
	{
		if (style.BackdropBlurRadius <= 0.01f || rect.Width <= 0.01f || rect.Height <= 0.01f)
		{
			return;
		}
		int num = (int)Math.Floor(rect.Left);
		int num2 = (int)Math.Floor(rect.Top);
		int num3 = Math.Max(1, (int)Math.Ceiling(rect.Width));
		int num4 = Math.Max(1, (int)Math.Ceiling(rect.Height));
		SKRectI sKRectI = new SKRectI(num, num2, num + num3, num2 + num4);
		SKRectI deviceClipBounds = surface.Canvas.DeviceClipBounds;
		int num5 = Math.Max(sKRectI.Left, deviceClipBounds.Left);
		int num6 = Math.Max(sKRectI.Top, deviceClipBounds.Top);
		int num7 = Math.Min(sKRectI.Right, deviceClipBounds.Right);
		int num8 = Math.Min(sKRectI.Bottom, deviceClipBounds.Bottom);
		if (num5 >= num7 || num6 >= num8)
		{
			return;
		}
		sKRectI = new SKRectI(num5, num6, num7, num8);
		using SKImage sKImage = surface.Snapshot(sKRectI);
		if (sKImage == null)
		{
			return;
		}
		int count = canvas.Save();
		ClipNodeBounds(canvas, rect, style);
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			BlendMode = SKBlendMode.Src,
			ImageFilter = SKImageFilter.CreateBlur(ToSigma(style.BackdropBlurRadius), ToSigma(style.BackdropBlurRadius))
		};
		canvas.DrawImage(sKImage, sKRectI.Left, sKRectI.Top, paint);
		canvas.RestoreToCount(count);
	}

	private static void DrawMask(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		if (style.MaskGradient == null)
		{
			return;
		}
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			BlendMode = SKBlendMode.DstIn,
			Shader = CreateGradientShader(rect, style.MaskGradient)
		};
		using SKPath path = CreateClipPath(rect, style);
		canvas.DrawPath(path, paint);
	}

	private static void DrawScrollbars(SKCanvas canvas, UiNode node)
	{
		if (node.Style.Overflow != UiOverflow.Scroll)
		{
			return;
		}
		UiStyle renderStyle = node.RenderStyle;
		SKColor color = ((renderStyle.BorderColor.Alpha > 0) ? renderStyle.BorderColor.WithAlpha((byte)Math.Max((int)renderStyle.BorderColor.Alpha, 72)).ToSKColor() : new SKColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, 56));
		SKColor color2 = ((renderStyle.OutlineColor.Alpha > 0) ? renderStyle.OutlineColor.WithAlpha((byte)Math.Max((int)renderStyle.OutlineColor.Alpha, 172)).ToSKColor() : ((renderStyle.Color.Alpha > 0) ? renderStyle.Color.WithAlpha(180).ToSKColor() : new SKColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, 180)));
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = color
		};
		using SKPaint paint2 = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = color2
		};
		if (UiScrollGeometry.HasVerticalScrollbar(node))
		{
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetVerticalTrackRect(node)), 5f, paint);
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetVerticalThumbRect(node)), 5f, paint2);
		}
		if (UiScrollGeometry.HasHorizontalScrollbar(node))
		{
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetHorizontalTrackRect(node)), 5f, paint);
			DrawRect(canvas, ToSkRect(UiScrollGeometry.GetHorizontalThumbRect(node)), 5f, paint2);
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
		UiNodeKind kind = node.Kind;
		if ((kind == UiNodeKind.Input || kind - 12 <= UiNodeKind.Text) ? true : false)
		{
			string text = node.Attributes["value"];
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			string text2 = node.Attributes["placeholder"];
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text2;
			}
		}
		return null;
	}

	private static void DrawText(string text, UiNode node, SKRect borderBox, UiStyle style, SKCanvas canvas)
	{
		SKRect content = ContentBox(borderBox, style);
		float availableWidth = Math.Max(0f, content.Width);
		UiTextLayoutResult uiTextLayoutResult = UiTextLayout.Measure(text, style, availableWidth, constrainWidth: true);
		SKRect textBox = content;
		if (UiFlexAnonymousText.ShouldAlignAsAnonymousFlexItem(node))
		{
			UiRect aligned = UiFlexAnonymousText.ResolveTextBox(
				style,
				new UiRect(borderBox.Left, borderBox.Top, borderBox.Width, borderBox.Height),
				uiTextLayoutResult.Width,
				uiTextLayoutResult.Height);
			textBox = new SKRect(aligned.X, aligned.Y, aligned.Right, aligned.Bottom);
		}
		using SKPaint paint = UiTextLayout.CreatePaint(style);
		using SKPaint sKPaint = CreateShadowPaint(style.TextShadow, style);
		float num = textBox.Top + style.FontSize;
		for (int i = 0; i < uiTextLayoutResult.Lines.Count; i++)
		{
			string text2 = uiTextLayoutResult.Lines[i];
			UiTextDirection direction = UiTextLayout.ResolveDirection(text2, style.Direction);
			string text3 = UiTextLayout.PrepareForRendering(text2, direction);
			SKTextAlign align = ResolveTextAlign(style, direction);
			float anchorX = ResolveTextAnchor(textBox, style, align);
			float num2 = ResolveTextStartX(text3, textBox, style, align);
			IReadOnlyList<UiTextRun> runs = UiTextLayout.CreateRuns(text3, style);
			if (sKPaint != null)
			{
				UiShadow? textShadow = style.TextShadow;
				if (textShadow.HasValue)
				{
					UiShadow valueOrDefault = textShadow.GetValueOrDefault();
					DrawTextRuns(canvas, runs, num2 + valueOrDefault.OffsetX, num + valueOrDefault.OffsetY, style, sKPaint);
				}
			}
			DrawTextRuns(canvas, runs, num2, num, style, paint);
			DrawTextDecorations(canvas, text3, anchorX, num, align, style);
			num += uiTextLayoutResult.LineHeight;
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

	private static void DrawImage(UiNode node, SKRect rect, UiStyle style, SKCanvas canvas)
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

	private static void DrawCanvas(UiNode node, SKRect rect, UiStyle style, SKCanvas canvas)
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

	private static void DrawNineSliceImage(SKCanvas canvas, SKImage image, NineSlicePatchSet patches)
	{
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			FilterQuality = SKFilterQuality.High
		};
		for (int i = 0; i < patches.Count; i++)
		{
			NineSlicePatch patch = patches[i];
			DrawImagePatch(canvas, image, patch.Source, patch.Destination, paint);
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

	private static void DrawBackgroundImageLayer(SKCanvas canvas, SKRect rect, UiStyle style, string imageSource, int layerIndex)
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

	private static void DrawRepeatedBackground(SKCanvas canvas, UiImageSourceCache.UiImageResource resource, SKRect area, SKSize tileSize, UiBackgroundPosition position, UiBackgroundRepeat repeat)
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

	private static void DrawImageResource(SKCanvas canvas, UiImageSourceCache.UiImageResource resource, SKRect destination)
	{
		if (resource.RasterImage != null)
		{
			using (SKPaint paint = new SKPaint
			{
				IsAntialias = true,
				FilterQuality = SKFilterQuality.High
			})
			{
				canvas.DrawImage(resource.RasterImage, destination, paint);
				return;
			}
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

	private static void ClipNodeBounds(SKCanvas canvas, SKRect rect, UiStyle style)
	{
		using SKPath path = CreateClipPath(rect, style);
		canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
	}

	private static SKPath CreateClipPath(SKRect rect, UiStyle style)
	{
		SKPath sKPath = new SKPath();
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

	private static float ResolveTextStartX(string line, SKRect contentRect, UiStyle style, SKTextAlign align)
	{
		float num = ResolveTextAnchor(contentRect, style, align);
		float num2 = UiTextLayout.MeasureWidth(line, style);
		return align switch
		{
			SKTextAlign.Right => num - num2,
			SKTextAlign.Center => num - num2 * 0.5f,
			_ => num,
		};
	}

	private static void DrawTextRuns(SKCanvas canvas, IReadOnlyList<UiTextRun> runs, float startX, float baselineY, UiStyle style, SKPaint paint)
	{
		float num = startX;
		foreach (UiTextRun run in runs)
		{
			using SKFont sKFont = new SKFont(run.Typeface, style.FontSize);
			canvas.DrawText(run.Text, num, baselineY, SKTextAlign.Left, sKFont, paint);
			num += sKFont.MeasureText(run.Text, paint);
		}
	}

	private static void DrawTextDecorations(SKCanvas canvas, string line, float anchorX, float baselineY, SKTextAlign align, UiStyle style)
	{
		if (style.TextDecorationLine == UiTextDecorationLine.None || string.IsNullOrEmpty(line))
		{
			return;
		}
		float num = UiTextLayout.MeasureWidth(line, style);
		if (num <= 0.01f)
		{
			return;
		}
		if (1 == 0)
		{
		}
		float num2 = align switch
		{
			SKTextAlign.Right => anchorX - num, 
			SKTextAlign.Center => anchorX - num * 0.5f, 
			_ => anchorX, 
		};
		if (1 == 0)
		{
		}
		float num3 = num2;
		float x = num3 + num;
		float strokeWidth = Math.Max(1f, style.FontSize * 0.06f);
		using SKPaint paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = strokeWidth,
			Color = style.Color.ToSKColor()
		};
		if ((style.TextDecorationLine & UiTextDecorationLine.Underline) != UiTextDecorationLine.None)
		{
			float num4 = baselineY + Math.Max(1.5f, style.FontSize * 0.12f);
			canvas.DrawLine(num3, num4, x, num4, paint);
		}
		if ((style.TextDecorationLine & UiTextDecorationLine.LineThrough) != UiTextDecorationLine.None)
		{
			float num5 = baselineY - style.FontSize * 0.32f;
			canvas.DrawLine(num3, num5, x, num5, paint);
		}
	}

	private static SKPaint? CreateShadowPaint(UiShadow? shadow, UiStyle style)
	{
		if (shadow.HasValue)
		{
			UiShadow valueOrDefault = shadow.GetValueOrDefault();
			if (valueOrDefault.IsVisible)
			{
				return new SKPaint
				{
					Color = valueOrDefault.Color.ToSKColor(),
					IsAntialias = true,
					ImageFilter = ((valueOrDefault.BlurRadius > 0.01f) ? SKImageFilter.CreateBlur(ToSigma(valueOrDefault.BlurRadius), ToSigma(valueOrDefault.BlurRadius)) : null)
				};
			}
		}
		return null;
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
