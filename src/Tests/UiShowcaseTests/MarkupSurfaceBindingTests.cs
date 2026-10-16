using System;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class MarkupSurfaceBindingTests
{
	private const string Html = """
		<div id="root" class="panel">
		  <span class="label">FPS</span>
		  <span id="fps" class="value" ui-text="Fps"></span>
		  <button id="pause" class="btn" ui-click="TogglePause">暂停</button>
		  <canvas id="spark" class="spark" ui-canvas="CreateSpark"></canvas>
		</div>
		""";

	private const string Css = """
		.panel { display: flex; flex-direction: column; width: 320px; height: 200px; }
		.value { font-size: 14px; }
		.btn { width: 120px; height: 32px; }
		.btn.on { background-color: #ff0000; }
		.spark { width: 160px; height: 24px; }
		""";

	[Test]
	public void BoundDocument_PublishedThroughSurfaceHost_CarriesTextClickAndCanvas()
	{
		Fixture fixture = Mount();
		UiScene scene = fixture.Root.Scene!;
		scene.Layout(320f, 200f);

		Assert.That(scene.FindByElementId("fps")!.TextBinding, Is.SameAs(fixture.CodeBehind.Fps));
		Assert.That(scene.FindByElementId("fps")!.TextContent, Is.EqualTo("—"));
		UiNode spark = scene.FindByElementId("spark")!;
		Assert.That(spark.CanvasContent, Is.SameAs(fixture.CodeBehind.Spark));
		Assert.That(spark.LayoutRect.Width, Is.EqualTo(160f).Within(0.01f));

		Click(fixture.Root, scene.FindByElementId("pause")!);
		Assert.That(fixture.CodeBehind.Clicks, Is.EqualTo(1));
		Assert.That(fixture.CodeBehind.LastTarget, Is.EqualTo("pause"));
	}

	[Test]
	public void ClassChangeAndInvalidate_RebuildKeepsBindingsAndHandlers()
	{
		Fixture fixture = Mount();
		UiElement pause = fixture.Document.FindById("pause")!;
		pause.Attributes["class"] = "btn on";
		fixture.Host.Invalidate(fixture.Lease);
		fixture.Root.Render();
		UiScene scene = fixture.Root.Scene!;

		UiNode pauseNode = scene.FindByElementId("pause")!;
		Assert.That(pauseNode.Style.BackgroundColor, Is.EqualTo(new UiColor(255, 0, 0, 255)));
		Click(fixture.Root, pauseNode);
		Assert.That(fixture.CodeBehind.Clicks, Is.EqualTo(1), "the rebuilt node must keep its code-behind handler");

		fixture.CodeBehind.Fps.Set("59.9");
		Assert.That(fixture.Root.IsDirty, Is.True);
		fixture.Root.Render();
		Assert.That(scene.FindByElementId("fps")!.TextContent, Is.EqualTo("59.9"));
	}

	[Test]
	public void GeneratedChildren_ClearedAndRegenerated_ReplaceTheOldNodesOnRebuild()
	{
		Fixture fixture = Mount();
		UiElement root = fixture.Document.FindById("root")!;
		var list = new UiElement("div");
		list.Attributes["id"] = "chips";
		root.AddChild(list);
		int picked = -1;
		for (int i = 0; i < 3; i++)
		{
			int index = i;
			var chip = new UiElement("button", UiNodeKind.Button) { TextContent = "c" + i, ClickHandler = _ => picked = index };
			chip.Attributes["id"] = "chip-" + i;
			list.AddChild(chip);
		}
		fixture.Host.Invalidate(fixture.Lease);
		fixture.Root.Render();
		Assert.That(fixture.Root.Scene!.FindByElementId("chips")!.Children.Count, Is.EqualTo(3));

		UiElement old = list.Children[0];
		list.ClearChildren();
		Assert.That(old.Parent, Is.Null);
		var only = new UiElement("button", UiNodeKind.Button) { TextContent = "only", ClickHandler = _ => picked = 9 };
		only.Attributes["id"] = "chip-only";
		list.AddChild(only);
		fixture.Host.Invalidate(fixture.Lease);
		fixture.Root.Render();

		UiScene scene = fixture.Root.Scene!;
		Assert.That(scene.FindByElementId("chips")!.Children.Count, Is.EqualTo(1));
		Assert.That(scene.FindByElementId("chip-0"), Is.Null);
		Click(fixture.Root, scene.FindByElementId("chip-only")!);
		Assert.That(picked, Is.EqualTo(9));
	}

	[Test]
	public void BoundTextAndCanvasUpdates_AllocateNothingAndSkipFullLayout()
	{
		Fixture fixture = Mount();
		using var surface = SKSurface.Create(new SKImageInfo(320, 200));
		fixture.Renderer.Canvas = surface.Canvas;
		char[] digits = new char[4];
		for (int i = 0; i < 8; i++)
		{
			fixture.CodeBehind.Sample(i, digits);
			fixture.Root.Render();
		}
		int fullLayouts = fixture.Root.Scene!.FullLayoutCount;

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 64; i++)
		{
			fixture.CodeBehind.Sample(i, digits);
			fixture.Root.Render();
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"64 bound samples + Skia frames allocated {allocated} bytes");
		Assert.That(fixture.Root.Scene.FullLayoutCount, Is.EqualTo(fullLayouts));
	}

	[Test]
	public void BoundDocument_MountedDirectlyOnScene_CarriesTheSameBindings()
	{
		UiDocument document = new UiMarkupLoader().LoadDocument(Html, Css);
		var codeBehind = new HudCodeBehind();
		MarkupBinder.Bind(document, codeBehind);
		var scene = new UiScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		scene.MountDocument(document);
		scene.Layout(320f, 200f);

		Assert.That(scene.FindByElementId("fps")!.TextBinding, Is.SameAs(codeBehind.Fps));
		Assert.That(scene.FindByElementId("spark")!.CanvasContent, Is.SameAs(codeBehind.Spark));
		UiNode pause = scene.FindByElementId("pause")!;
		scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, pause.LayoutRect.X + 2f, pause.LayoutRect.Y + 2f, pause.Id));
		Assert.That(codeBehind.Clicks, Is.EqualTo(1));
	}

	[Test]
	public void Binder_RejectsBadTargets()
	{
		var loader = new UiMarkupLoader();
		Assert.Throws<InvalidOperationException>(() =>
			MarkupBinder.Bind(loader.LoadDocument("""<span ui-text="Fps">static</span>"""), new HudCodeBehind()),
			"a bound element cannot also carry static text");
		Assert.Throws<InvalidOperationException>(() =>
			MarkupBinder.Bind(loader.LoadDocument("""<span ui-text="Missing"></span>"""), new HudCodeBehind()));
		Assert.Throws<InvalidOperationException>(() =>
			MarkupBinder.Bind(loader.LoadDocument("""<span ui-text="Clicks"></span>"""), new HudCodeBehind()),
			"a member that is not a UiTextBinding is rejected");
	}

	private static void Click(UIRoot root, UiNode node)
	{
		float x = node.LayoutRect.X + 2f;
		float y = node.LayoutRect.Y + 2f;
		root.HandleInput(new PointerEvent { PointerId = 0, Action = PointerAction.Down, Button = PointerButton.Left, X = x, Y = y });
		root.HandleInput(new PointerEvent { PointerId = 0, Action = PointerAction.Up, Button = PointerButton.Left, X = x, Y = y });
	}

	private static Fixture Mount()
	{
		UiDocument document = new UiMarkupLoader().LoadDocument(Html, Css);
		var codeBehind = new HudCodeBehind();
		MarkupBinder.Bind(document, codeBehind);
		var renderer = new CountingRenderer();
		var root = new UIRoot(renderer);
		var host = new UiSurfaceHost(root, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		root.Resize(320f, 200f);
		UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest("test.markup-bound"));
		host.Publish(lease, UiSurfaceContribution.FromBuilder(() => UiElementBuilder.FromElement(document.Root), styleSheets: document.StyleSheets));
		root.Render();
		return new Fixture(document, codeBehind, root, host, lease, renderer);
	}

	private sealed record Fixture(UiDocument Document, HudCodeBehind CodeBehind, UIRoot Root, UiSurfaceHost Host, UiSurfaceLeaseHandle Lease, CountingRenderer Renderer);

	private sealed class HudCodeBehind
	{
		public readonly UiTextBinding Fps = new UiTextBinding(8);

		public HudCodeBehind()
		{
			Fps.Set("—");
		}

		public readonly UiVectorDrawList Spark = new UiVectorDrawList(2, 16);

		public int Clicks;

		public string? LastTarget;

		private void TogglePause(UiActionContext context)
		{
			Clicks++;
			LastTarget = context.TargetNode.ElementId;
		}

		private UiVectorDrawList CreateSpark() => Spark;

		public void Sample(int i, char[] digits)
		{
			(i % 60).TryFormat(digits, out int written);
			Fps.Set(digits.AsSpan(0, written));
			Spark.Clear();
			Spark.BeginPolyline(new UiColor(0, 128, 255, 255), 1f);
			for (int p = 0; p < 16; p++)
			{
				Spark.AddPoint(p / 15f, ((p + i) % 7) / 7f);
			}
			Spark.EndPath();
		}
	}

	private sealed class CountingRenderer : IUiRenderer
	{
		private readonly SkiaUiRenderer _inner = new SkiaUiRenderer();

		public SKCanvas? Canvas { get; set; }

		public void Render(UiScene scene, float width, float height)
		{
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
