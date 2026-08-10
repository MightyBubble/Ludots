using System;
using System.Collections.Generic;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
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
		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			float width = (text?.Length ?? 0) * style.FontSize * 0.5f;
			float lineHeight = style.FontSize * 1.4f;
			return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, lineHeight, lineHeight, style.FontSize, Math.Max(0f, lineHeight - style.FontSize));
		}

		public float MeasureWidth(string? text, UiStyle style) => (text?.Length ?? 0) * style.FontSize * 0.5f;
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
