using System;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using NUnit.Framework;
using UiShowcaseCoreMod.Showcase;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiNineSlicePanelTests
{
	[Test]
	public void Showcase_LoadsOrnateSheet_WithNineSliceFrames()
	{
		UiScene scene = UiShowcaseFactory.CreateNineSlicePanelScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);

		UiNode sheet = scene.FindByElementId("sheet")!;
		UiNode frame = scene.FindByElementId("sheet-frame")!;
		UiNode name = scene.FindByElementId("sheet-name")!;
		UiNode btnRest = scene.FindByElementId("btn-rest")!;

		Assert.That(sheet.HasClass("size-compact"), Is.True);
		Assert.That(frame.Style.ImageSlice.Left, Is.EqualTo(56f));
		Assert.That(frame.Style.ImageSlice.Top, Is.EqualTo(56f));
		Assert.That(frame.Attributes["src"], Does.StartWith("data:image/png;base64,"));
		Assert.That(name.LayoutRect.Width, Is.GreaterThan(80f));
		Assert.That(name.LayoutRect.Bottom, Is.LessThanOrEqualTo(sheet.LayoutRect.Bottom));
		Assert.That(btnRest.Children.Count, Is.GreaterThan(0));
	}

	[Test]
	public void Showcase_SizeChips_ResizeSheet_WithoutCrushingCornersContract()
	{
		UiScene scene = UiShowcaseFactory.CreateNineSlicePanelScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);

		UiRect compact = scene.FindByElementId("sheet")!.LayoutRect;
		Assert.That(Click(scene, "size-wide"), Is.True);
		scene.Layout(1280f, 720f);
		UiNode wideSheet = scene.FindByElementId("sheet")!;
		Assert.That(wideSheet.HasClass("size-wide"), Is.True);
		Assert.That(wideSheet.LayoutRect.Width, Is.GreaterThan(compact.Width + 40f));
		Assert.That(scene.FindByElementId("sheet-frame")!.Style.ImageSlice.Left, Is.EqualTo(56f));

		Assert.That(Click(scene, "size-tall"), Is.True);
		scene.Layout(1280f, 720f);
		UiNode tallSheet = scene.FindByElementId("sheet")!;
		Assert.That(tallSheet.HasClass("size-tall"), Is.True);
		Assert.That(tallSheet.LayoutRect.Height, Is.GreaterThan(compact.Height + 20f));
		Assert.That(scene.FindByElementId("sheet-name")!.LayoutRect.Bottom, Is.LessThanOrEqualTo(tallSheet.LayoutRect.Bottom));
	}

	private static bool Click(UiScene scene, string elementId)
	{
		UiNode node = scene.FindByElementId(elementId)!;
		return scene.Dispatch(new UiPointerEvent(
			UiPointerEventType.Click,
			0,
			node.LayoutRect.X + 2,
			node.LayoutRect.Y + 2,
			node.Id)).Handled;
	}

	private sealed class ConstantTextMeasurer : IUiTextMeasurer
	{
		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			float width = (text?.Length ?? 0) * style.FontSize * 0.5f;
			float lineHeight = style.FontSize * 1.4f;
			return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, lineHeight, lineHeight);
		}

		public float MeasureWidth(string? text, UiStyle style) => (text?.Length ?? 0) * style.FontSize * 0.5f;
	}

	private sealed class ConstantImageSizeProvider : IUiImageSizeProvider
	{
		public bool TryGetSize(string? source, out float width, out float height)
		{
			width = 256f;
			height = 256f;
			return true;
		}
	}
}
