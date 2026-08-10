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
	public void Showcase_DefaultMode_IsNineSliceSheet_WithSvgSeal()
	{
		UiScene scene = UiShowcaseFactory.CreateNineSlicePanelScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);

		UiNode ninePanel = scene.FindByElementId("panel-nine")!;
		UiNode frame = scene.FindByElementId("sheet-frame")!;
		UiNode seal = scene.FindByElementId("sheet-seal")!;
		UiNode btnRest = scene.FindByElementId("btn-rest")!;

		Assert.That(ninePanel.HasClass("visible"), Is.True);
		Assert.That(scene.FindByElementId("panel-three")!.HasClass("visible"), Is.False);
		Assert.That(frame.Style.ImageSlice.Left, Is.EqualTo(56f));
		Assert.That(frame.Attributes["src"], Does.StartWith("data:image/png;base64,"));
		Assert.That(seal.Attributes["src"], Does.StartWith("data:image/svg+xml"));
		Assert.That(btnRest.Children.Count, Is.GreaterThan(0));
		AssertChipLabelCentered(scene, "mode-nine", "mode-nine-label");
	}

	[Test]
	public void Showcase_ModeChips_SwitchThreeTwoFourAndMotionPanels()
	{
		UiScene scene = UiShowcaseFactory.CreateNineSlicePanelScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);

		Assert.That(Click(scene, "mode-three"), Is.True);
		scene.Layout(1280f, 720f);
		Assert.That(scene.FindByElementId("panel-three")!.HasClass("visible"), Is.True);
		UiNode ribbon = scene.FindByElementId("ribbon-long-frame")!;
		Assert.That(ribbon.Style.ImageSlice.Left, Is.EqualTo(110f));
		Assert.That(ribbon.Style.ImageSlice.Top, Is.EqualTo(0f));
		Assert.That(ribbon.Style.ImageSlice.Right, Is.EqualTo(110f));
		Assert.That(ribbon.Style.ImageSlice.Bottom, Is.EqualTo(0f));
		Assert.That(scene.FindByElementId("ribbon-long")!.LayoutRect.Width,
			Is.GreaterThan(scene.FindByElementId("ribbon-short")!.LayoutRect.Width + 100f));

		Assert.That(Click(scene, "mode-two"), Is.True);
		scene.Layout(1280f, 720f);
		Assert.That(scene.FindByElementId("panel-two")!.HasClass("visible"), Is.True);
		Assert.That(scene.FindByElementId("strip-h")!.Style.BackgroundRepeats[0], Is.EqualTo(UiBackgroundRepeat.RepeatX));
		Assert.That(scene.FindByElementId("strip-v")!.Style.BackgroundRepeats[0], Is.EqualTo(UiBackgroundRepeat.RepeatY));

		Assert.That(Click(scene, "mode-four"), Is.True);
		scene.Layout(1280f, 720f);
		Assert.That(scene.FindByElementId("panel-four")!.HasClass("visible"), Is.True);
		Assert.That(scene.FindByElementId("tile-floor")!.Style.BackgroundRepeats[0], Is.EqualTo(UiBackgroundRepeat.Repeat));

		Assert.That(Click(scene, "mode-motion"), Is.True);
		scene.Layout(1280f, 720f);
		Assert.That(scene.FindByElementId("panel-motion")!.HasClass("visible"), Is.True);
		Assert.That(scene.FindByElementId("motion-seal")!.Attributes["src"], Does.StartWith("data:image/svg+xml"));
		Assert.That(scene.FindByElementId("motion-brush")!.Attributes["src"], Does.StartWith("data:image/svg+xml"));
	}

	[Test]
	public void Showcase_SvgSeal_BreathesWhenTimeAdvances()
	{
		UiScene scene = UiShowcaseFactory.CreateNineSlicePanelScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);

		UiNode seal = scene.FindByElementId("sheet-seal")!;
		float opacityBefore = seal.RenderStyle.Opacity;
		float angleBefore = ReadRotateDegrees(seal.RenderStyle.Transform);
		Assert.That(scene.AdvanceTime(0.45f), Is.True, "ink breathe animation should dirty render style");
		float opacityAfter = seal.RenderStyle.Opacity;
		float angleAfter = ReadRotateDegrees(seal.RenderStyle.Transform);
		Assert.That(Math.Abs(opacityAfter - opacityBefore), Is.GreaterThan(0.01f));
		Assert.That(Math.Abs(angleAfter - angleBefore), Is.GreaterThan(1f));
	}

	private static float ReadRotateDegrees(UiTransform transform)
	{
		foreach (UiTransformOperation operation in transform.Operations)
		{
			if (operation.Kind == UiTransformOperationKind.Rotate)
			{
				return operation.AngleDegrees;
			}
		}
		Assert.Fail("expected rotate transform on animated seal");
		return 0f;
	}

	[Test]
	public void Showcase_ModeChips_LabelTextIsCenteredInChip()
	{
		UiScene scene = UiShowcaseFactory.CreateNineSlicePanelScene(
			new ConstantTextMeasurer(),
			new ConstantImageSizeProvider());
		scene.Layout(1280f, 720f);

		AssertChipLabelCentered(scene, "mode-nine", "mode-nine-label");
		AssertChipLabelCentered(scene, "mode-three", "mode-three-label");
		AssertChipLabelCentered(scene, "mode-two", "mode-two-label");
		AssertChipLabelCentered(scene, "mode-four", "mode-four-label");
		AssertChipLabelCentered(scene, "mode-motion", "mode-motion-label");
	}

	private static void AssertChipLabelCentered(UiScene scene, string chipId, string labelId)
	{
		UiNode chip = scene.FindByElementId(chipId)!;
		UiNode label = scene.FindByElementId(labelId)!;
		float chipCenterX = chip.LayoutRect.X + chip.LayoutRect.Width * 0.5f;
		float chipCenterY = chip.LayoutRect.Y + chip.LayoutRect.Height * 0.5f;
		float labelCenterX = label.LayoutRect.X + label.LayoutRect.Width * 0.5f;
		float labelCenterY = label.LayoutRect.Y + label.LayoutRect.Height * 0.5f;
		Assert.That(Math.Abs(labelCenterX - chipCenterX), Is.LessThanOrEqualTo(2.5f), chipId + " label X");
		Assert.That(Math.Abs(labelCenterY - chipCenterY), Is.LessThanOrEqualTo(2.5f), chipId + " label Y");
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
