using System;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiVectorDrawListTests
{
	private const int Width = 200;
	private const int Height = 100;
	private const int SparkPoints = 90;

	private static readonly UiColor Red = new UiColor(255, 0, 0, 255);
	private static readonly UiColor Green = new UiColor(0, 255, 0, 255);
	private static readonly UiColor Blue = new UiColor(0, 0, 255, 255);

	[Test]
	public void CanvasNode_TakesItsBoxFromCss_AndRedrawsWithoutRelayout()
	{
		var list = new UiVectorDrawList(4, SparkPoints);
		Fixture fixture = Mount(list);
		fixture.Root.Render();
		UiNode canvasNode = fixture.Root.Scene!.FindByElementId("spark")!;
		Assert.That(canvasNode.LayoutRect.Width, Is.EqualTo(160f).Within(0.01f));
		Assert.That(canvasNode.LayoutRect.Height, Is.EqualTo(40f).Within(0.01f));
		Assert.That(fixture.Root.IsDirty, Is.False);
		int fullLayouts = fixture.Root.Scene.FullLayoutCount;
		int textRelayouts = fixture.Root.Scene.TextRelayoutCount;

		WriteSpark(list, 3);
		Assert.That(fixture.Root.Scene.HasPendingVectorChanges, Is.True);
		Assert.That(fixture.Root.IsDirty, Is.True, "a rewritten draw list must reach the screen");
		fixture.Root.Render();

		Assert.That(fixture.Root.IsDirty, Is.False);
		Assert.That(fixture.Root.Scene.HasPendingVectorChanges, Is.False);
		Assert.That(fixture.Root.Scene.FullLayoutCount, Is.EqualTo(fullLayouts), "points are paint-only");
		Assert.That(fixture.Root.Scene.TextRelayoutCount, Is.EqualTo(textRelayouts));
		Assert.That(canvasNode.LayoutRect.Width, Is.EqualTo(160f).Within(0.01f));
	}

	[Test]
	public void SkiaRenderer_DrawsRectsPolylinesAndPolygonsInContentSpace()
	{
		var list = new UiVectorDrawList(4, 8);
		list.FillRect(0f, 0f, 0.5f, 1f, Red);
		list.BeginPolygon(Blue);
		list.AddPoint(0.5f, 0f);
		list.AddPoint(1f, 0f);
		list.AddPoint(1f, 0.5f);
		list.AddPoint(0.5f, 0.5f);
		list.EndPath();
		list.BeginPolyline(Green, 4f);
		list.AddPoint(0f, 0.75f);
		list.AddPoint(1f, 0.75f);
		list.EndPath();
		var scene = new UiScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Custom,
			UiStyle.Default with { Width = UiLength.Px(Width), Height = UiLength.Px(Height), Padding = new UiThickness(10f, 10f, 10f, 10f) },
			canvasContent: list, tagName: "canvas"));

		using SKBitmap bitmap = RenderToBitmap(scene);

		Assert.That(bitmap.GetPixel(5, 5).Alpha, Is.EqualTo(0), "padding is outside the content box");
		Assert.That(bitmap.GetPixel(40, 30), Is.EqualTo(new SKColor(255, 0, 0, 255)));
		Assert.That(bitmap.GetPixel(150, 30), Is.EqualTo(new SKColor(0, 0, 255, 255)));
		Assert.That(bitmap.GetPixel(150, 80).Alpha, Is.EqualTo(0));
		SKColor stroke = bitmap.GetPixel(150, 70);
		Assert.That(stroke.Green, Is.EqualTo(255));
		Assert.That(stroke.Red, Is.EqualTo(0));
	}

	[Test]
	public void PointUpdates_LayoutAndSkiaFrames_AllocateNothingAfterWarmup()
	{
		var list = new UiVectorDrawList(4, SparkPoints);
		Fixture fixture = Mount(list);
		using var surface = SKSurface.Create(new SKImageInfo(320, 200));
		fixture.Renderer.Canvas = surface.Canvas;
		for (int i = 0; i < 8; i++)
		{
			WriteSpark(list, i);
			fixture.Root.Render();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 64; i++)
		{
			WriteSpark(list, i);
			fixture.Root.Render();
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"64 sparkline rewrites + layout + Skia frames allocated {allocated} bytes");
		Assert.That(fixture.Renderer.Renders, Is.EqualTo(72));
	}

	[Test]
	public void Capacity_IsFixed_AndPathMisuseThrows()
	{
		var list = new UiVectorDrawList(1, 2);
		list.BeginPolyline(Green, 1f);
		Assert.Throws<InvalidOperationException>(() => list.FillRect(0f, 0f, 1f, 1f, Red));
		list.AddPoint(0f, 0f);
		list.AddPoint(1f, 1f);
		Assert.Throws<InvalidOperationException>(() => list.AddPoint(0.5f, 0.5f));
		list.EndPath();
		Assert.Throws<InvalidOperationException>(() => list.EndPath());
		Assert.Throws<InvalidOperationException>(() => list.AddPoint(0f, 0f));
		Assert.Throws<InvalidOperationException>(() => list.FillRect(0f, 0f, 1f, 1f, Red));
		Assert.Throws<ArgumentOutOfRangeException>(() => list.BeginPolyline(Green, 0f));
		int version = list.Version;
		list.Clear();
		Assert.That(list.Version, Is.GreaterThan(version));
		Assert.That(list.Commands.Length, Is.Zero);
		Assert.That(list.Points.Length, Is.Zero);
	}

	private static void WriteSpark(UiVectorDrawList list, int phase)
	{
		list.Clear();
		list.FillRect(0f, 0f, (phase % 10) / 10f, 0.2f, Blue);
		list.BeginPolyline(Green, 1f);
		for (int i = 0; i < SparkPoints; i++)
		{
			float x = i / (float)(SparkPoints - 1);
			list.AddPoint(x, 0.5f + 0.4f * MathF.Sin((i + phase) * 0.3f));
		}
		list.EndPath();
	}

	private static Fixture Mount(UiVectorDrawList list)
	{
		var renderer = new CountingSkiaRenderer();
		var root = new UIRoot(renderer);
		var host = new UiSurfaceHost(root, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		root.Resize(320f, 200f);
		UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest("test.vector"));
		host.Publish(lease, UiSurfaceContribution.FromBuilder(() =>
			Ui.Column(
				Ui.Text("fps"),
				Ui.Canvas(list).Id("spark").Width(160f).Height(40f))
			.Width(320f).Height(200f)));
		return new Fixture(root, renderer);
	}

	private static SKBitmap RenderToBitmap(UiScene scene)
	{
		var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.Transparent);
		new SkiaUiRenderer().RenderToCanvas(scene, canvas, Width, Height);
		canvas.Flush();
		return bitmap;
	}

	private sealed record Fixture(UIRoot Root, CountingSkiaRenderer Renderer);

	private sealed class CountingSkiaRenderer : IUiRenderer
	{
		private readonly SkiaUiRenderer _inner = new SkiaUiRenderer();

		public SKCanvas? Canvas { get; set; }

		public int Renders { get; private set; }

		public void Render(UiScene scene, float width, float height)
		{
			Renders++;
			if (Canvas != null)
			{
				Canvas.Clear(SKColors.Transparent);
				_inner.RenderToCanvas(scene, Canvas, width, height);
			}
			else
			{
				scene.Layout(width, height);
			}
		}
	}
}
