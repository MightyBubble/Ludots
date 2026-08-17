using System;
using System.Linq;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiTransformKeyframeTests
{
	[Test]
	public void Keyframes_CompatibleTransform_InterpolatesRotateTranslateScale()
	{
		const string html = """
			<div id="probe" class="probe">seal</div>
			""";
		const string css = """
			.probe {
			  width: 120px;
			  height: 120px;
			  animation: spin-drift 1s linear 0s infinite alternate both;
			}
			@keyframes spin-drift {
			  0% { transform: translateY(0px) rotate(-10deg) scale(1); }
			  100% { transform: translateY(-20px) rotate(10deg) scale(1.2); }
			}
			""";

		UiScene scene = Load(html, css);
		UiNode probe = scene.FindByElementId("probe")!;
		Assert.That(probe.RenderStyle.Transform.HasOperations, Is.True);

		float angleBefore = ReadRotate(probe.RenderStyle.Transform);
		float yBefore = ReadTranslateY(probe.RenderStyle.Transform);
		float scaleBefore = ReadScaleX(probe.RenderStyle.Transform);

		Assert.That(scene.AdvanceTime(0.5f), Is.True);
		float angleAfter = ReadRotate(probe.RenderStyle.Transform);
		float yAfter = ReadTranslateY(probe.RenderStyle.Transform);
		float scaleAfter = ReadScaleX(probe.RenderStyle.Transform);

		Assert.That(Math.Abs(angleAfter - angleBefore), Is.GreaterThan(1f));
		Assert.That(Math.Abs(yAfter - yBefore), Is.GreaterThan(1f));
		Assert.That(Math.Abs(scaleAfter - scaleBefore), Is.GreaterThan(0.01f));
		Assert.That(angleAfter, Is.EqualTo(0f).Within(0.75f));
		Assert.That(yAfter, Is.EqualTo(-10f).Within(0.75f));
		Assert.That(scaleAfter, Is.EqualTo(1.1f).Within(0.02f));
	}

	[Test]
	public void Keyframes_UnsupportedMatrixTransform_DoesNotCreateSilentTrack()
	{
		const string html = """
			<div id="probe" class="probe">x</div>
			""";
		const string css = """
			.probe {
			  width: 80px;
			  height: 80px;
			  transform: rotate(12deg);
			  animation: bad-matrix 1s linear 0s infinite alternate both;
			}
			@keyframes bad-matrix {
			  0% { transform: matrix(1, 0, 0, 1, 0, 0); }
			  100% { transform: matrix(1, 0, 0, 1, 10, 0); }
			}
			""";

		UiScene scene = Load(html, css);
		UiNode probe = scene.FindByElementId("probe")!;
		float before = ReadRotate(probe.RenderStyle.Transform);
		Assert.That(before, Is.EqualTo(12f).Within(0.01f));
		Assert.That(scene.AdvanceTime(0.4f), Is.False);
		Assert.That(ReadRotate(probe.RenderStyle.Transform), Is.EqualTo(12f).Within(0.01f));
	}

	[Test]
	public void Keyframes_IncompatibleTransformLists_DoNotInterpolate()
	{
		const string html = """
			<div id="probe" class="probe">x</div>
			""";
		const string css = """
			.probe {
			  width: 80px;
			  height: 80px;
			  transform: rotate(6deg);
			  animation: mismatch 1s linear 0s infinite alternate both;
			}
			@keyframes mismatch {
			  0% { transform: scale(1); }
			  100% { transform: translateX(12px); }
			}
			""";

		UiScene scene = Load(html, css);
		UiNode probe = scene.FindByElementId("probe")!;
		Assert.That(ReadRotate(probe.RenderStyle.Transform), Is.EqualTo(6f).Within(0.01f));
		Assert.That(scene.AdvanceTime(0.4f), Is.False);
		Assert.That(ReadRotate(probe.RenderStyle.Transform), Is.EqualTo(6f).Within(0.01f));
		Assert.That(probe.RenderStyle.Transform.Operations.Any(op => op.Kind == UiTransformOperationKind.Scale), Is.False);
	}

	[Test]
	public void Transition_CompatibleTransform_AdvancesWithTime()
	{
		const string html = """
			<div id="probe" class="probe">x</div>
			""";
		const string css = """
			.probe {
			  width: 80px;
			  height: 80px;
			  transition: transform 0.4s linear;
			  transform: rotate(0deg);
			}
			.probe:hover {
			  transform: rotate(40deg);
			}
			""";

		UiScene scene = Load(html, css);
		UiNode probe = scene.FindByElementId("probe")!;
		Assert.That(ReadRotate(probe.RenderStyle.Transform), Is.EqualTo(0f).Within(0.01f));
		scene.Dispatch(new Ludots.UI.Runtime.Events.UiPointerEvent(
			Ludots.UI.Runtime.Events.UiPointerEventType.Move,
			0,
			probe.LayoutRect.X + 2,
			probe.LayoutRect.Y + 2,
			probe.Id));
		scene.Layout(640f, 360f);
		Assert.That(ReadRotate(probe.RenderStyle.Transform), Is.EqualTo(0f).Within(0.01f));
		Assert.That(scene.AdvanceTime(0.2f), Is.True);
		float mid = ReadRotate(probe.RenderStyle.Transform);
		Assert.That(mid, Is.GreaterThan(10f));
		Assert.That(mid, Is.LessThan(30f));
	}

	private static UiScene Load(string html, string css)
	{
		UiMarkupLoader loader = new UiMarkupLoader();
		UiScene scene = loader.LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(640f, 360f);
		return scene;
	}

	private static float ReadRotate(UiTransform transform)
	{
		UiTransformOperation op = transform.Operations.First(item => item.Kind == UiTransformOperationKind.Rotate);
		return op.AngleDegrees;
	}

	private static float ReadTranslateY(UiTransform transform)
	{
		UiTransformOperation op = transform.Operations.First(item => item.Kind == UiTransformOperationKind.Translate);
		return op.YLength.Value;
	}

	private static float ReadScaleX(UiTransform transform)
	{
		UiTransformOperation op = transform.Operations.First(item => item.Kind == UiTransformOperationKind.Scale);
		return op.ScaleX;
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
			width = 64f;
			height = 64f;
			return true;
		}
	}
}
