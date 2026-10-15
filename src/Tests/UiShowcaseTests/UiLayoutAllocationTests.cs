using System;
using System.Collections.Generic;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiLayoutAllocationTests
{
	[Test]
	public void UiLayout_ResizeOnly_AfterWarmup_StaysUnderAllocationBudget()
	{
		UiScene scene = BuildHundredNodeFlexScene();
		WarmupLayout(scene);

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 20; i++)
		{
			scene.Layout(1280f + i, 720f + i);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.LessThan(4 * 1024),
			$"resize-only layout allocated {allocated} bytes over 20 passes; flex nodes and scratch must be reused");
	}

	[Test]
	public void UiGrid_ResizeOnly_AfterWarmup_StaysUnderAllocationBudget()
	{
		UiScene scene = BuildHundredNodeGridScene();
		WarmupLayout(scene);

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 20; i++)
		{
			scene.Layout(1280f + i, 720f + i);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.LessThan(4 * 1024),
			$"grid resize-only layout allocated {allocated} bytes over 20 passes");
	}

	[Test]
	public void UiStyleResolve_DirtyRelayout_UsesPooledMatchBuffers()
	{
		const string html = """
			<div class="root">
			  <div class="item">A</div>
			  <div class="item">B</div>
			  <div class="item">C</div>
			</div>
			""";
		const string css = """
			.root { display: flex; flex-direction: column; width: 200px; height: 200px; }
			.item { height: 20px; color: #fff; }
			.item:hover { color: #0f0; }
			""";
		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(800f, 600f);
		UiStyleSheet[] sheets = scene.Document!.StyleSheets.ToArray();

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 30; i++)
		{
			scene.SetStyleSheets(sheets);
			scene.Layout(800f, 600f);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.LessThan(512 * 1024),
			$"dirty resolve+layout allocated {allocated} bytes over 30 passes; match/cascade buffers should be pooled");
	}

	[Test]
	public void UiScene_AdvanceTime_AfterTransitionSettles_AllocatesNothing()
	{
		const string html = """
			<div class="root">
			  <div id="probe" class="probe">x</div>
			  <div class="item">A</div>
			  <div class="item">B</div>
			</div>
			""";
		const string css = """
			.root { display: flex; flex-direction: column; width: 200px; height: 200px; }
			.probe { width: 80px; height: 40px; background-color: #202020; transition: background-color 0.2s linear, transform 0.2s linear; transform: rotate(0deg); }
			.probe:hover { background-color: #40a040; transform: rotate(20deg); }
			.item { height: 20px; color: #fff; }
			""";
		UiScene scene = new UiMarkupLoader().LoadScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), html, css);
		scene.Layout(640f, 360f);
		UiNode probe = scene.FindByElementId("probe")!;
		scene.Dispatch(new UiPointerEvent(UiPointerEventType.Move, 0, probe.LayoutRect.X + 2, probe.LayoutRect.Y + 2, probe.Id));
		scene.Layout(640f, 360f);
		Assert.That(scene.AdvanceTime(0.1f), Is.True, "hover should start the transition");
		for (int i = 0; i < 8; i++)
		{
			scene.AdvanceTime(0.1f);
		}
		Assert.That(scene.AdvanceTime(0.016f), Is.False, "transition should have settled");

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 64; i++)
		{
			scene.AdvanceTime(0.016f);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0L), $"idle AdvanceTime allocated {allocated} bytes over 64 frames");
	}

	[Test]
	public void UiSurfaceHost_IdleFrames_WithReactivePages_AllocateNothing()
	{
		var root = new UIRoot(new NullUiRenderer());
		root.Resize(640f, 360f);
		var host = new UiSurfaceHost(root, new ConstantTextMeasurer(), new ConstantImageSizeProvider());
		var hud = new ReactivePage<int>(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), 0,
			context => Ui.Column(Ui.Text("hud").Id("idle-hud")).Width(200f).Height(80f));
		var debug = new ReactivePage<int>(new ConstantTextMeasurer(), new ConstantImageSizeProvider(), 0,
			context => Ui.Column(Ui.Text("debug").Id("idle-debug")).Width(200f).Height(80f));
		host.Publish(host.Acquire(new UiSurfaceLeaseRequest("idle.hud", UiSurfaceSegment.Overlay, priority: 10)), UiSurfaceContribution.FromReactivePage(hud));
		host.Publish(host.Acquire(new UiSurfaceLeaseRequest("idle.debug", UiSurfaceSegment.Debug, priority: 10)), UiSurfaceContribution.FromReactivePage(debug));
		for (int i = 0; i < 4; i++)
		{
			root.Update(0.016f);
			root.Render();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 64; i++)
		{
			root.Update(0.016f);
			root.Render();
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(root.Scene!.FindByElementId("idle-hud"), Is.Not.Null);
		Assert.That(allocated, Is.EqualTo(0L), $"idle surface host frames allocated {allocated} bytes over 64 frames");
	}

	private static void WarmupLayout(UiScene scene)
	{
		for (int i = 0; i < 5; i++)
		{
			scene.Layout(1280f + i, 720f + i);
		}
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	private static UiScene BuildHundredNodeFlexScene()
	{
		List<UiNode> children = new List<UiNode>(100);
		for (int i = 0; i < 100; i++)
		{
			children.Add(new UiNode(
				new UiNodeId(i + 2),
				UiNodeKind.Container,
				UiStyle.Default with { Width = UiLength.Px(10f), Height = UiLength.Px(10f) },
				i.ToString(),
				tagName: "div"));
		}
		UiNode root = new UiNode(
			new UiNodeId(1),
			UiNodeKind.Container,
			UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				FlexWrap = UiFlexWrap.Wrap,
				Width = UiLength.Px(1000f),
				Height = UiLength.Px(1000f),
				Gap = 2f
			},
			null,
			children,
			tagName: "div",
			elementId: "root");
		UiScene scene = new UiScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider());
		scene.Mount(root);
		return scene;
	}

	private static UiScene BuildHundredNodeGridScene()
	{
		UiGridTrack[] columns = new UiGridTrack[10];
		for (int i = 0; i < columns.Length; i++)
		{
			columns[i] = UiGridTrack.Fr(1f);
		}
		List<UiNode> cells = new List<UiNode>(100);
		for (int i = 0; i < 100; i++)
		{
			cells.Add(new UiNode(
				new UiNodeId(i + 2),
				UiNodeKind.Container,
				UiStyle.Default with { Width = UiLength.Px(10f), Height = UiLength.Px(10f) },
				i.ToString(),
				tagName: "div"));
		}
		UiNode grid = new UiNode(
			new UiNodeId(1),
			UiNodeKind.Container,
			UiStyle.Default with
			{
				Display = UiDisplay.Grid,
				GridTemplateColumns = columns,
				Width = UiLength.Px(1000f),
				Height = UiLength.Px(1000f),
				Gap = 2f
			},
			null,
			cells,
			tagName: "div",
			elementId: "grid");
		UiScene scene = new UiScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider());
		scene.Mount(grid);
		return scene;
	}

	private sealed class ConstantTextMeasurer : IUiTextMeasurer
	{
		private readonly Dictionary<string, UiTextLayoutResult> _layoutCache = new();

		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			string key = text ?? string.Empty;
			if (_layoutCache.TryGetValue(key, out UiTextLayoutResult cached))
			{
				return cached;
			}

			float width = key.Length * style.FontSize * 0.5f;
			float lineHeight = style.FontSize * 1.4f;
			var result = new UiTextLayoutResult(new[] { key }, width, lineHeight, lineHeight, style.FontSize, Math.Max(0f, lineHeight - style.FontSize));
			_layoutCache[key] = result;
			return result;
		}

		public float MeasureWidth(string? text, UiStyle style) => (text?.Length ?? 0) * style.FontSize * 0.5f;

		public UiTextSize MeasureSize(ReadOnlySpan<char> text, UiStyle style, float availableWidth, bool constrainWidth)
			=> new UiTextSize(text.Length * style.FontSize * 0.5f, style.FontSize * 1.4f);
	}

	private sealed class NullUiRenderer : IUiRenderer
	{
		public void Render(UiScene scene, float width, float height)
		{
		}
	}

	private sealed class ConstantImageSizeProvider : IUiImageSizeProvider
	{
		public bool TryGetSize(string? source, out float width, out float height)
		{
			width = 16f;
			height = 16f;
			return true;
		}
	}
}
