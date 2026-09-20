using System.Diagnostics;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiInlineFlowTests
{
	private static readonly IUiTextMeasurer TextMeasurer = new SkiaTextMeasurer();
	private static readonly IUiImageSizeProvider ImageSizeProvider = new SkiaImageSizeProvider();

	private static UiScene BuildScene(string html, string css)
	{
		return new UiMarkupLoader().LoadScene(TextMeasurer, ImageSizeProvider, html, css);
	}

	[Test]
	public void UiInlineFlow_MixedStrongEm_FitsOnSingleLine()
	{
		const string html = """
			<p id="line">Hello <strong id="s">bold</strong> and <em id="e">italic</em></p>
			""";
		const string css = """
			p { display: block; width: 600px; font-size: 16px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode strong = scene.FindByElementId("s")!;
		UiNode em = scene.FindByElementId("e")!;

		Assert.That(strong.Style.Display, Is.EqualTo(UiDisplay.Inline));
		Assert.That(em.Style.Display, Is.EqualTo(UiDisplay.Inline));
		Assert.That(strong.LayoutRect.Y, Is.EqualTo(em.LayoutRect.Y).Within(1f));
		Assert.That(em.LayoutRect.X, Is.GreaterThan(strong.LayoutRect.X));
	}

	[Test]
	public void UiInlineFlow_InsufficientWidth_WrapsToMultipleLines()
	{
		const string html = """
			<p id="line"><span id="a">AAAA</span><span id="b">BBBB</span><span id="c">CCCC</span></p>
			""";
		const string css = """
			p { display: block; width: 40px; font-size: 16px; }
			span { display: inline; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode a = scene.FindByElementId("a")!;
		UiNode c = scene.FindByElementId("c")!;

		Assert.That(c.LayoutRect.Y, Is.GreaterThan(a.LayoutRect.Y + 1f), "inline items must wrap onto later line boxes when width is insufficient");
	}

	[Test]
	public void UiInlineFlow_InlineImage_SharesLineWithText()
	{
		const string html = """
			<p id="line"><span id="t">Hi</span><img id="i" src="about:blank" style="width: 12px; height: 12px; display: inline" /></p>
			""";
		const string css = """
			p { display: block; width: 200px; font-size: 16px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode text = scene.FindByElementId("t")!;
		UiNode image = scene.FindByElementId("i")!;

		Assert.That(image.Style.Display, Is.EqualTo(UiDisplay.Inline));
		Assert.That(image.LayoutRect.Y, Is.EqualTo(text.LayoutRect.Y).Within(8f));
		Assert.That(image.LayoutRect.X, Is.GreaterThan(text.LayoutRect.X));
	}

	[Test]
	public void UiInlineFlow_BlockSibling_StacksWithoutInlineContamination()
	{
		const string html = """
			<div id="root">
			  <p id="p">inline <strong>x</strong></p>
			  <div id="block" style="display:block; height: 24px; width: 100px">block</div>
			</div>
			""";
		const string css = """
			#root { display: block; width: 200px; }
			p { display: block; width: 200px; font-size: 16px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode paragraph = scene.FindByElementId("p")!;
		UiNode block = scene.FindByElementId("block")!;

		Assert.That(block.LayoutRect.Y, Is.GreaterThanOrEqualTo(paragraph.LayoutRect.Bottom - 1.5f));
		Assert.That(block.LayoutRect.Height, Is.EqualTo(24f).Within(0.5f));
	}

	[Test]
	public void UiInlineFlow_Float_IsIgnoredAndStaysInNormalFlow()
	{
		const string html = """
			<p id="line"><span id="a">A</span><span id="b" style="float: left">B</span></p>
			""";
		const string css = """
			p { display: block; width: 200px; font-size: 16px; }
			span { display: inline; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode a = scene.FindByElementId("a")!;
		UiNode b = scene.FindByElementId("b")!;

		Assert.That(b.LayoutRect.X, Is.GreaterThanOrEqualTo(a.LayoutRect.Right - 0.5f), "float is unsupported and must not pull the span out of inline flow");
		Assert.That(b.LayoutRect.Y, Is.EqualTo(a.LayoutRect.Y).Within(1f));
	}

	[Test]
	public void UiInlineFlow_DisplayInlineNotCollapsedToFlexItem()
	{
		const string html = """
			<p id="line"><strong id="s">S</strong><em id="e">E</em></p>
			""";
		const string css = "p { display: block; width: 200px; }";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);

		Assert.That(scene.FindByElementId("s")!.Style.Display, Is.EqualTo(UiDisplay.Inline));
		Assert.That(scene.FindByElementId("e")!.Style.Display, Is.EqualTo(UiDisplay.Inline));
		Assert.That(UiInlineFlowEngine.IsInlineFormattingContext(scene.FindByElementId("line")!), Is.True);
	}

	[Test]
	public void UiInlineFlow_LayoutHundredNodes_CompletesUnderFiveMilliseconds()
	{
		List<UiNode> spans = new List<UiNode>(100);
		for (int i = 0; i < 100; i++)
		{
			spans.Add(new UiNode(
				new UiNodeId(i + 2),
				UiNodeKind.Text,
				UiStyle.Default with { Display = UiDisplay.Inline, FontSize = 12f },
				"x",
				tagName: "span"));
		}
		UiNode line = new UiNode(
			new UiNodeId(1),
			UiNodeKind.Text,
			UiStyle.Default with { Display = UiDisplay.Block, Width = UiLength.Px(320f), FontSize = 12f },
			null,
			spans,
			tagName: "p",
			elementId: "line");
		UiScene scene = new UiScene(new ConstantTextMeasurer(), new ConstantImageSizeProvider());
		scene.Mount(line);
		for (int i = 0; i < 3; i++)
		{
			scene.Layout(800f + i, 600f + i);
		}
		var sw = Stopwatch.StartNew();
		scene.Layout(820f, 620f);
		sw.Stop();

		Assert.That(scene.FindByElementId("line")!.Children.Count, Is.EqualTo(100));
		Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5.0), $"100-node inline layout took {sw.Elapsed.TotalMilliseconds:F3}ms");
	}

	private sealed class ConstantTextMeasurer : IUiTextMeasurer
	{
		public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
		{
			float width = (text?.Length ?? 0) * style.FontSize * 0.5f;
			if (constrainWidth && availableWidth > 0f && width > availableWidth)
			{
				int lines = Math.Max(1, (int)Math.Ceiling(width / availableWidth));
				float lineHeight = style.FontSize * 1.4f;
				return new UiTextLayoutResult(new[] { text ?? string.Empty }, availableWidth, lineHeight * lines, lineHeight, style.FontSize, Math.Max(0f, lineHeight - style.FontSize));
			}
			float single = style.FontSize * 1.4f;
			return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, single, single, style.FontSize, Math.Max(0f, single - style.FontSize));
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
