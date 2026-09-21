using System;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.UiShowcase;

/// <summary>
/// SkiaUiRenderer 直渲目标面（RenderToSurface / SetTarget）与既有 canvas 路径
/// （RenderToCanvas，带整窗中间面）的像素等价与 blur 行为合同。
/// 直渲路径是宿主 GPU 合成的帧路径（ADR-0005），不得偏离 canvas 路径的视觉输出。
/// </summary>
[TestFixture]
public class SkiaUiRendererSurfaceTargetTests
{
	private const float SceneWidth = 320f;
	private const float SceneHeight = 240f;

	private static UiScene ComposeScene(Func<UiElementBuilder, UiElementBuilder>? mutateRoot = null)
	{
		UiElementBuilder root = new UiElementBuilder(UiNodeKind.Container)
			.Width(SceneWidth)
			.Height(SceneHeight)
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(0f, 0f)
				.Width(160f)
				.Height(SceneHeight)
				.Background("#FF0000"))
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(160f, 0f)
				.Width(160f)
				.Height(SceneHeight)
				.Background("#0000FF"))
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(40f, 60f)
				.Width(140f)
				.Height(80f)
				.Radius(10f)
				.Border(2f, new UiColor(255, 255, 255))
				.Background("#8000FF00"))
			.Child(new UiElementBuilder(UiNodeKind.Text, "span")
				.Text("surface target")
				.Absolute(60f, 90f))
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(220f, 150f)
				.Width(60f)
				.Height(60f)
				.Radius(6f)
				.Background("#FFFF00")
				.Blur(4f));
		if (mutateRoot != null)
		{
			root = mutateRoot(root);
		}

		return UiSceneComposer.Compose(new SkiaTextMeasurer(), new SkiaImageSizeProvider(), root);
	}

	private static SKBitmap RenderViaCanvas(UiScene scene)
	{
		using SKSurface surface = SKSurface.Create(new SKImageInfo((int)SceneWidth, (int)SceneHeight));
		new SkiaUiRenderer().RenderToCanvas(scene, surface.Canvas, SceneWidth, SceneHeight);
		using SKImage snapshot = surface.Snapshot();
		return SKBitmap.FromImage(snapshot);
	}

	private static SKBitmap RenderViaSurface(UiScene scene)
	{
		using SKSurface surface = SKSurface.Create(new SKImageInfo((int)SceneWidth, (int)SceneHeight));
		new SkiaUiRenderer().RenderToSurface(scene, surface, SceneWidth, SceneHeight);
		using SKImage snapshot = surface.Snapshot();
		return SKBitmap.FromImage(snapshot);
	}

	[Test]
	[Category("ci-gate")]
	public void RenderToSurface_MatchesCanvasPath_PixelForPixel()
	{
		UiScene canvasScene = ComposeScene();
		UiScene surfaceScene = ComposeScene();
		canvasScene.Layout(SceneWidth, SceneHeight);
		surfaceScene.Layout(SceneWidth, SceneHeight);

		using SKBitmap viaCanvas = RenderViaCanvas(canvasScene);
		using SKBitmap viaSurface = RenderViaSurface(surfaceScene);

		int differing = 0;
		for (int y = 0; y < (int)SceneHeight; y++)
		{
			for (int x = 0; x < (int)SceneWidth; x++)
			{
				if (viaCanvas.GetPixel(x, y) != viaSurface.GetPixel(x, y))
				{
					differing++;
				}
			}
		}

		Assert.That(differing, Is.EqualTo(0), $"direct surface path must reproduce the canvas path pixel for pixel; differing={differing}");
	}

	[Test]
	[Category("ci-gate")]
	public void SetTarget_RendersIntoTargetSurfaceThroughRender()
	{
		UiScene scene = ComposeScene();
		using SKSurface target = SKSurface.Create(new SKImageInfo((int)SceneWidth, (int)SceneHeight));
		SkiaUiRenderer renderer = new SkiaUiRenderer();
		renderer.SetTarget(target);
		Assert.DoesNotThrow(() => scene.AdvanceTime(0f));
		renderer.Render(scene, SceneWidth, SceneHeight);

		using SKImage snapshot = target.Snapshot();
		using SKBitmap bitmap = SKBitmap.FromImage(snapshot);
		Assert.That(bitmap.GetPixel(20, 20).Red, Is.GreaterThan(200), "left red half must be painted into the target surface");
		Assert.That(bitmap.GetPixel(300, 20).Blue, Is.GreaterThan(200), "right blue half must be painted into the target surface");
	}

	[Test]
	[Category("ci-gate")]
	public void RenderToSurface_BackdropBlur_SamplesContentBehindInTargetSurface()
	{
		UiElementBuilder ComposeFrosted(float blurRadius) => new UiElementBuilder(UiNodeKind.Container)
			.Width(SceneWidth)
			.Height(SceneHeight)
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(0f, 0f)
				.Width(160f)
				.Height(SceneHeight)
				.Background("#FF0000"))
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(160f, 0f)
				.Width(160f)
				.Height(SceneHeight)
				.Background("#0000FF"))
			.Child(new UiElementBuilder(UiNodeKind.Container)
				.Absolute(100f, 60f)
				.Width(120f)
				.Height(120f)
				.BackdropBlur(blurRadius));

		UiScene sharp = UiSceneComposer.Compose(new SkiaTextMeasurer(), new SkiaImageSizeProvider(), ComposeFrosted(0f));
		UiScene blurred = UiSceneComposer.Compose(new SkiaTextMeasurer(), new SkiaImageSizeProvider(), ComposeFrosted(8f));
		sharp.Layout(SceneWidth, SceneHeight);
		blurred.Layout(SceneWidth, SceneHeight);

		using SKBitmap sharpBitmap = RenderViaSurface(sharp);
		using SKBitmap blurredBitmap = RenderViaSurface(blurred);

		// 无 blur：红蓝分界在 x=160 处是硬边（分界左右像素一个纯红一个纯蓝）。
		SKColor sharpLeft = sharpBitmap.GetPixel(158, 120);
		SKColor sharpRight = sharpBitmap.GetPixel(162, 120);
		Assert.That(sharpLeft.Red, Is.GreaterThan(200), "without backdrop blur the boundary stays a hard red edge");
		Assert.That(sharpRight.Blue, Is.GreaterThan(200), "without backdrop blur the boundary stays a hard blue edge");

		// 有 blur：分界中心被磨开，红蓝通道同时显著出现。
		SKColor blurredCenter = blurredBitmap.GetPixel(160, 120);
		Assert.That(blurredCenter.Red, Is.GreaterThan(40), "backdrop blur must smear red across the boundary");
		Assert.That(blurredCenter.Blue, Is.GreaterThan(40), "backdrop blur must smear blue across the boundary");
	}

	[Test]
	[Category("ci-gate")]
	public void RenderToSurface_FilterBlur_ProducesSoftSpillOutsideNodeBounds()
	{
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			new UiElementBuilder(UiNodeKind.Container)
				.Width(SceneWidth)
				.Height(SceneHeight)
				.Child(new UiElementBuilder(UiNodeKind.Container)
					.Absolute(220f, 150f)
					.Width(60f)
					.Height(60f)
					.Background("#FFFF00")
					.Blur(4f)));
		scene.Layout(SceneWidth, SceneHeight);

		using SKBitmap bitmap = RenderViaSurface(scene);

		SKColor center = bitmap.GetPixel(250, 180);
		Assert.That(center.Alpha, Is.EqualTo((byte)255), "filter blur keeps the node core opaque");
		Assert.That(center.Red, Is.GreaterThan(200));
		Assert.That(center.Green, Is.GreaterThan(200));

		SKColor spill = bitmap.GetPixel(284, 180);
		Assert.That(spill.Alpha, Is.GreaterThan(0), "SaveLayer blur must spill soft pixels outside the node bounds");
		Assert.That(spill.Alpha, Is.LessThan((byte)255), "spill right outside the bounds must be partially transparent");

		SKColor far = bitmap.GetPixel(310, 180);
		Assert.That(far.Alpha, Is.EqualTo((byte)0), "beyond the blur padding nothing is drawn");
	}
}
