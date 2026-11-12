using System;
using System.Collections.Generic;
using System.Globalization;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiTextBindingTests
{
	private const float FontSize = 10f;

	private static readonly char[][] Values =
	{
		"7".ToCharArray(),
		"1234".ToCharArray(),
		"12".ToCharArray(),
		"123456".ToCharArray(),
	};

	[Test]
	public void BindingUpdate_InAutoWidthCell_RelaysOutWithoutFullLayoutOrAllocation()
	{
		var binding = new UiTextBinding("0");
		BindingScene fixture = BuildRow(binding, labelWidth: null);
		Warmup(fixture.Scene, binding);
		int fullLayouts = fixture.Scene.FullLayoutCount;
		int textRelayouts = fixture.Scene.TextRelayoutCount;

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 64; i++)
		{
			binding.Set(Values[i % Values.Length]);
			fixture.Scene.Layout(800f, 600f);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"binding update + layout allocated {allocated} bytes over 64 frames");
		Assert.That(fixture.Scene.FullLayoutCount, Is.EqualTo(fullLayouts), "text-only changes must not rebuild the flex tree");
		Assert.That(fixture.Scene.TextRelayoutCount - textRelayouts, Is.EqualTo(64));
		Assert.That(fixture.Scene.LastTextRelayoutDirtyLeaves, Is.EqualTo(1));
		float expectedWidth = Values[63 % Values.Length].Length * FontSize * 0.5f;
		Assert.That(fixture.Label.LayoutRect.Width, Is.EqualTo(expectedWidth).Within(0.001f));
		Assert.That(fixture.Sibling.LayoutRect.X, Is.EqualTo(fixture.Label.LayoutRect.Right + 4f).Within(0.001f),
			"the sibling after an auto-width cell must follow the new text width");
	}

	[Test]
	public void BindingUpdate_MoreNodesChangedThanAnyEarlierFrame_AllocatesNothing()
	{
		const int count = 8;
		var bindings = new UiTextBinding[count];
		var spans = new UiNode[count];
		for (int i = 0; i < count; i++)
		{
			bindings[i] = new UiTextBinding("0");
			spans[i] = new UiNode(new UiNodeId(10 + i), UiNodeKind.Text, UiStyle.Default with { FontSize = FontSize }, textBinding: bindings[i], tagName: "span");
		}
		var root = new UiNode(
			new UiNodeId(1),
			UiNodeKind.Container,
			UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Start,
				Width = UiLength.Px(400f),
				Height = UiLength.Px(300f),
			},
			children: spans,
			tagName: "div");
		var scene = new UiScene(new HalfEmTextMeasurer(), new FixedImageSizeProvider());
		scene.Mount(root);
		Warmup(scene, bindings[0]);
		for (int i = 0; i < count; i++)
		{
			bindings[i].Set(Values[1]);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();
		scene.Layout(800f, 600f);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"first frame changing {count} bound spans allocated {allocated} bytes");
		Assert.That(scene.LastTextRelayoutDirtyLeaves, Is.EqualTo(count));
	}

	[Test]
	public void BindingUpdate_InFixedWidthCell_MarksNothingDirty()
	{
		var binding = new UiTextBinding("0");
		BindingScene fixture = BuildRow(binding, labelWidth: 80f);
		Warmup(fixture.Scene, binding);
		UiRect labelBefore = fixture.Label.LayoutRect;
		UiRect siblingBefore = fixture.Sibling.LayoutRect;
		long sceneVersion = fixture.Scene.Version;

		Assert.That(binding.Set("abcdefgh"), Is.True);
		fixture.Scene.Layout(800f, 600f);

		Assert.That(fixture.Scene.LastTextRelayoutDirtyLeaves, Is.EqualTo(0), "a fixed-width cell keeps its size, so nothing propagates");
		Assert.That(fixture.Label.LayoutRect, Is.EqualTo(labelBefore));
		Assert.That(fixture.Sibling.LayoutRect, Is.EqualTo(siblingBefore));
		Assert.That(fixture.Scene.Version, Is.GreaterThan(sceneVersion), "the text still changed, so the surface must redraw");
	}

	[Test]
	public void BindingRelayout_MatchesFreshFullLayout()
	{
		var binding = new UiTextBinding("0");
		BindingScene incremental = BuildRow(binding, labelWidth: null);
		Warmup(incremental.Scene, binding);
		binding.Set("123456789");
		incremental.Scene.Layout(800f, 600f);

		var reference = BuildRow(new UiTextBinding("123456789"), labelWidth: null);
		reference.Scene.Layout(800f, 600f);

		AssertSameLayout(incremental.Scene.Root!, reference.Scene.Root!);
	}

	[Test]
	public void BindingUpdate_PendingChangeMakesRootDirtyUntilLaidOut()
	{
		var binding = new UiTextBinding("0");
		BindingScene fixture = BuildRow(binding, labelWidth: null);
		fixture.Scene.Layout(800f, 600f);
		Assert.That(fixture.Scene.HasPendingTextChanges, Is.False);

		Assert.That(binding.Set("0"), Is.False, "same content is not a change");
		Assert.That(fixture.Scene.HasPendingTextChanges, Is.False);
		Assert.That(binding.Set("01"), Is.True);
		Assert.That(fixture.Scene.HasPendingTextChanges, Is.True);

		fixture.Scene.Layout(800f, 600f);
		Assert.That(fixture.Scene.HasPendingTextChanges, Is.False);
		Assert.That(fixture.Label.TextContent, Is.EqualTo("01"));
	}

	[Test]
	public void BindingUpdate_LaidOutBeforeRender_KeepsRootDirtyUntilRendered()
	{
		var binding = new UiTextBinding("0");
		var renderer = new LayoutThenCountRenderer();
		var root = new UIRoot(renderer);
		var host = new UiSurfaceHost(root, new HalfEmTextMeasurer(), new FixedImageSizeProvider());
		root.Resize(320f, 200f);
		UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest("test.bound-text"));
		host.Publish(lease, UiSurfaceContribution.FromBuilder(() => Ui.Column(Ui.Text(binding)).Width(320f).Height(200f)));
		root.Render();
		Assert.That(root.IsDirty, Is.False);

		binding.Set("01");
		root.Scene!.Layout(320f, 200f);
		Assert.That(root.Scene.HasPendingTextChanges, Is.False);
		Assert.That(root.IsDirty, Is.True, "text laid out by an input or query pass must still reach the screen");

		root.Render();
		Assert.That(root.IsDirty, Is.False);
		Assert.That(renderer.Renders, Is.EqualTo(2));
	}

	[Test]
	public void SkiaRenderer_BoundTextFrames_AllocateNothingAfterWarmup()
	{
		var binding = new UiTextBinding("0");
		var scene = new UiScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		var label = new UiNode(new UiNodeId(2), UiNodeKind.Text, UiStyle.Default with { FontSize = 14f, Color = UiColor.White }, textBinding: binding, tagName: "span");
		var clipped = new UiNode(
			new UiNodeId(3),
			UiNodeKind.Text,
			UiStyle.Default with
			{
				FontSize = 14f,
				Width = UiLength.Px(60f),
				WhiteSpace = UiWhiteSpace.NoWrap,
				TextOverflow = UiTextOverflow.Ellipsis,
				ClipContent = true,
				BorderRadius = 4f,
				BackgroundColor = new UiColor(20, 20, 20, 200),
				BorderWidth = 1f,
				BorderColor = UiColor.White,
			},
			"a long label that needs an ellipsis",
			tagName: "span");
		var wrapped = new UiNode(
			new UiNodeId(4),
			UiNodeKind.Text,
			UiStyle.Default with { FontSize = 12f, Width = UiLength.Px(90f) },
			"several words that wrap across a few lines in a narrow box",
			tagName: "span");
		var root = new UiNode(
			new UiNodeId(1),
			UiNodeKind.Container,
			UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				Width = UiLength.Px(300f),
				Height = UiLength.Px(200f),
				BackgroundColor = new UiColor(0, 0, 0, 160),
				BorderRadius = 6f,
				Gap = 4f,
			},
			children: new[] { label, clipped, wrapped },
			tagName: "div");
		scene.Mount(root);
		var renderer = new SkiaUiRenderer();
		using var surface = SKSurface.Create(new SKImageInfo(320, 240));
		SKCanvas canvas = surface.Canvas;
		for (int i = 0; i < 8; i++)
		{
			binding.Set(Values[i % Values.Length]);
			canvas.Clear(SKColors.Transparent);
			renderer.RenderToCanvas(scene, canvas, 320f, 240f);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 64; i++)
		{
			binding.Set(Values[i % Values.Length]);
			canvas.Clear(SKColors.Transparent);
			renderer.RenderToCanvas(scene, canvas, 320f, 240f);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"steady-state Skia frames allocated {allocated} bytes over 64 frames");
	}

	[Test]
	public void SkiaTextLayout_WrapsAtWordBoundariesAndEllipsizes()
	{
		UiStyle style = UiStyle.Default with { FontSize = 12f };
		float wordWidth = UiTextLayout.MeasureWidth("aaa bbb", style);

		UiTextLayoutResult wrapped = UiTextLayout.Measure("aaa bbb ccc", style, wordWidth + 0.5f, constrainWidth: true);
		Assert.That(wrapped.Lines, Is.EqualTo(new[] { "aaa bbb", "ccc" }));
		Assert.That(wrapped.Width, Is.EqualTo(wordWidth).Within(0.01f));

		UiTextLayoutResult collapsed = UiTextLayout.Measure("  aaa \t  bbb  \nccc", style, 1000f, constrainWidth: true);
		Assert.That(collapsed.Lines, Is.EqualTo(new[] { "aaa bbb", "ccc" }));

		float narrow = UiTextLayout.MeasureWidth("aaaa", style);
		UiTextLayoutResult broken = UiTextLayout.Measure("aaaaaaaaaa", style, narrow + 0.5f, constrainWidth: true);
		Assert.That(broken.Lines, Is.EqualTo(new[] { "aaaa", "aaaa", "aa" }), "a word wider than the box breaks per grapheme");

		UiStyle ellipsis = style with { WhiteSpace = UiWhiteSpace.NoWrap, TextOverflow = UiTextOverflow.Ellipsis };
		float limit = UiTextLayout.MeasureWidth("abc…", ellipsis) + 0.5f;
		UiTextLayoutResult cut = UiTextLayout.Measure("abc defgh", ellipsis, limit, constrainWidth: true);
		Assert.That(cut.Lines, Is.EqualTo(new[] { "abc…" }));
		Assert.That(cut.Width, Is.LessThanOrEqualTo(limit));
	}

	[Test]
	public void SkiaTextLayout_MeasureSize_IsLinearAndAllocationFree()
	{
		UiStyle style = UiStyle.Default with { FontSize = 12f };
		char[] paragraph = string.Join(' ', Repeat("word", 400)).ToCharArray();
		UiTextLayout.MeasureSize(paragraph, style, 120f, constrainWidth: true);

		long before = GC.GetAllocatedBytesForCurrentThread();
		UiTextSize size = default;
		for (int i = 0; i < 16; i++)
		{
			size = UiTextLayout.MeasureSize(paragraph, style, 120f, constrainWidth: true);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"MeasureSize allocated {allocated} bytes");
		Assert.That(size.Width, Is.LessThanOrEqualTo(120f));
		UiTextLayoutResult reference = UiTextLayout.Measure(new string(paragraph), style, 120f, constrainWidth: true);
		Assert.That(size.Height, Is.EqualTo(reference.Height).Within(0.001f));
		Assert.That(reference.Lines.Count, Is.GreaterThan(50));
	}

	[TestCase("HUD 50000 units · 12.5ms p95")]
	[TestCase("群体仿真 寻路 12 帧（告警）")]
	[TestCase("▁▂▃▄▅▆▇█ → ≤ ±")]
	[TestCase("cafe\u0301 nai\u0308ve")]
	public void SkiaTextLayout_MeasureWidth_EqualsSkiaAdvancesPerGrapheme(string text)
	{
		UiStyle style = UiStyle.Default with { FontSize = 12f };
		float expected = 0f;
		TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
		while (elements.MoveNext())
		{
			string element = elements.GetTextElement();
			SKTypeface typeface = UiFontRegistry.ResolveTypefaceForTextElement(style.FontFamily, style.Bold, element, style.Italic);
			using var font = new SKFont(typeface, style.FontSize);
			expected += font.MeasureText(element);
		}

		Assert.That(UiTextLayout.MeasureWidth(text, style), Is.EqualTo(expected).Within(0.01f));
		Assert.That(UiTextLayout.MeasureWidth(text, style), Is.EqualTo(expected).Within(0.01f), "cached advances must give the same width");
	}

	[Test]
	public void SkiaTextLayout_SingleFontRun_MatchesSkiaWholeRunMeasure()
	{
		UiStyle style = UiStyle.Default with { FontSize = 13f };
		const string text = "Sim 12.5 ms | Avoid 3.25 ms | units 50000";
		using var font = new SKFont(UiFontRegistry.ResolveTypeface(style.FontFamily, style.Bold, style.Italic), style.FontSize);

		Assert.That(UiTextLayout.MeasureWidth(text, style), Is.EqualTo(font.MeasureText(text)).Within(0.01f));
	}

	[Test]
	public void SkiaTextLayout_FittingParagraph_IsOneLineWithItsMeasuredWidth()
	{
		UiStyle style = UiStyle.Default with { FontSize = 12f };
		const string text = "导航 12 帧 · 寻路 3 次 · ▃▅█";
		float width = UiTextLayout.MeasureWidth(text, style);

		UiTextLayoutResult exact = UiTextLayout.Measure(text, style, width, constrainWidth: true);
		Assert.That(exact.Lines, Is.EqualTo(new[] { text }));
		Assert.That(exact.Width, Is.EqualTo(width));

		UiTextLayoutResult narrower = UiTextLayout.Measure(text, style, width - 1f, constrainWidth: true);
		Assert.That(narrower.Lines.Count, Is.EqualTo(2), "one pixel short must fall back to word wrapping");
	}

	[Test]
	public void SkiaTextLayout_MixedScriptMeasure_AllocatesNothingAfterFirstPass()
	{
		UiStyle style = UiStyle.Default with { FontSize = 11f };
		char[] text = "遥测 Sim 12.5ms · 避让 3.2ms ▁▂▃▄▅▆▇█ → ≤ 告警".ToCharArray();
		UiTextLayout.MeasureSize(text, style, 90f, constrainWidth: true);

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 32; i++)
		{
			UiTextLayout.MeasureSize(text, style, 90f, constrainWidth: true);
			UiTextLayout.MeasureSize(text, style, 1000f, constrainWidth: true);
		}
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.That(allocated, Is.EqualTo(0), $"mixed-script MeasureSize allocated {allocated} bytes");
	}

	private static IEnumerable<string> Repeat(string value, int count)
	{
		for (int i = 0; i < count; i++)
		{
			yield return value;
		}
	}

	private static void Warmup(UiScene scene, UiTextBinding binding)
	{
		scene.Layout(800f, 600f);
		for (int i = 0; i < Values.Length * 2; i++)
		{
			binding.Set(Values[i % Values.Length]);
			scene.Layout(800f, 600f);
		}
	}

	private static void AssertSameLayout(UiNode actual, UiNode expected)
	{
		Assert.That(actual.LayoutRect, Is.EqualTo(expected.LayoutRect), $"node {actual.Id}");
		Assert.That(actual.Children.Count, Is.EqualTo(expected.Children.Count));
		for (int i = 0; i < actual.Children.Count; i++)
		{
			AssertSameLayout(actual.Children[i], expected.Children[i]);
		}
	}

	private static BindingScene BuildRow(UiTextBinding binding, float? labelWidth)
	{
		UiStyle labelStyle = UiStyle.Default with { FontSize = FontSize };
		if (labelWidth is { } width)
		{
			labelStyle = labelStyle with { Width = UiLength.Px(width) };
		}
		var label = new UiNode(new UiNodeId(3), UiNodeKind.Text, labelStyle, textBinding: binding, tagName: "span");
		var sibling = new UiNode(new UiNodeId(4), UiNodeKind.Text, UiStyle.Default with { FontSize = FontSize }, "units", tagName: "span");
		var row = new UiNode(
			new UiNodeId(2),
			UiNodeKind.Container,
			UiStyle.Default with { Display = UiDisplay.Flex, FlexDirection = UiFlexDirection.Row, Gap = 4f },
			children: new[] { label, sibling },
			tagName: "div");
		var other = new UiNode(new UiNodeId(5), UiNodeKind.Text, UiStyle.Default with { FontSize = FontSize }, "static footer", tagName: "span");
		var root = new UiNode(
			new UiNodeId(1),
			UiNodeKind.Container,
			UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Start,
				Width = UiLength.Px(400f),
				Height = UiLength.Px(300f),
			},
			children: new[] { row, other },
			tagName: "div");
		var scene = new UiScene(new HalfEmTextMeasurer(), new FixedImageSizeProvider());
		scene.Mount(root);
		return new BindingScene(scene, label, sibling);
	}

	private sealed record BindingScene(UiScene Scene, UiNode Label, UiNode Sibling);

	private sealed class LayoutThenCountRenderer : IUiRenderer
	{
		public int Renders { get; private set; }

		public void Render(UiScene scene, float width, float height)
		{
			scene.Layout(width, height);
			Renders++;
		}
	}

	private sealed class HalfEmTextMeasurer : IUiTextMeasurer
	{
		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			float lineHeight = style.FontSize * 1.4f;
			return new UiTextLayoutResult(new[] { text ?? string.Empty }, (text?.Length ?? 0) * style.FontSize * 0.5f, lineHeight, lineHeight);
		}

		public float MeasureWidth(string? text, UiStyle style) => (text?.Length ?? 0) * style.FontSize * 0.5f;

		public UiTextSize MeasureSize(ReadOnlySpan<char> text, UiStyle style, float availableWidth, bool constrainWidth)
			=> new UiTextSize(text.Length * style.FontSize * 0.5f, style.FontSize * 1.4f);
	}

	private sealed class FixedImageSizeProvider : IUiImageSizeProvider
	{
		public bool TryGetSize(string? source, out float width, out float height)
		{
			width = 16f;
			height = 16f;
			return true;
		}
	}
}
