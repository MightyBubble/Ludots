using System.Diagnostics;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiPseudoElementTests
{
	private static readonly IUiTextMeasurer TextMeasurer = new SkiaTextMeasurer();
	private static readonly IUiImageSizeProvider ImageSizeProvider = new SkiaImageSizeProvider();

	private static UiScene BuildScene(string html, string css)
	{
		return new UiMarkupLoader().LoadScene(TextMeasurer, ImageSizeProvider, html, css);
	}

	[Test]
	public void UiSelectorParser_DoubleColonBefore_SetsPseudoElementBefore()
	{
		UiSelector selector = UiSelectorParser.Parse(".icon::before");

		Assert.That(selector.PseudoElement, Is.EqualTo(UiPseudoElement.Before));
		Assert.That(selector.ToString(), Does.Contain("::before"));
	}

	[Test]
	public void UiSelectorParser_SingleColonAfter_SetsPseudoElementAfter()
	{
		UiSelector selector = UiSelectorParser.Parse("span:after");

		Assert.That(selector.PseudoElement, Is.EqualTo(UiPseudoElement.After));
	}

	[Test]
	public void UiPseudoElement_ContentString_GeneratesTextNode()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = ".icon::before { content: \"hi\"; }";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode? before = scene.QuerySelector(".icon::before");

		Assert.That(before, Is.Not.Null);
		Assert.That(before!.PseudoElement, Is.EqualTo(UiPseudoElement.Before));
		Assert.That(before.TextContent, Is.EqualTo("hi"));
		Assert.That(before.Kind, Is.EqualTo(UiNodeKind.Text));
	}

	[Test]
	public void UiPseudoElement_BeforeAndAfter_CoexistInOrder()
	{
		const string html = "<div id=\"host\" class=\"badge\">mid</div>";
		const string css = """
			.badge::before { content: "A"; }
			.badge::after { content: "Z"; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode host = scene.FindByElementId("host")!;

		Assert.That(host.Children.Count, Is.EqualTo(3));
		Assert.That(host.Children[0].PseudoElement, Is.EqualTo(UiPseudoElement.Before));
		Assert.That(host.Children[0].TextContent, Is.EqualTo("A"));
		Assert.That(host.Children[1].PseudoElement, Is.EqualTo(UiPseudoElement.None));
		Assert.That(host.Children[1].TextContent, Is.EqualTo("mid"));
		Assert.That(host.Children[2].PseudoElement, Is.EqualTo(UiPseudoElement.After));
		Assert.That(host.Children[2].TextContent, Is.EqualTo("Z"));
	}

	[Test]
	public void UiPseudoElement_InheritsHostColor_WhenNotOverridden()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = """
			.icon { color: rgb(0, 0, 255); }
			.icon::before { content: "•"; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode before = scene.QuerySelector(".icon::before")!;

		Assert.That(before.Style.Color.R, Is.EqualTo((byte)0));
		Assert.That(before.Style.Color.G, Is.EqualTo((byte)0));
		Assert.That(before.Style.Color.B, Is.EqualTo((byte)255));
	}

	[Test]
	public void UiPseudoElement_RuleWithoutContent_DoesNotGenerateNode()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = ".icon::before { color: red; }";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);

		Assert.That(scene.QuerySelector(".icon::before"), Is.Null);
		Assert.That(scene.FindByElementId("host")!.Children, Is.Empty);
	}

	[Test]
	public void UiPseudoElement_BeforeColor_DoesNotLeakToHost()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = """
			.icon { color: rgb(0, 128, 0); }
			.icon::before { content: "•"; color: rgb(255, 0, 0); }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode host = scene.FindByElementId("host")!;
		UiNode before = scene.QuerySelector(".icon::before")!;

		Assert.That(host.Style.Color.R, Is.EqualTo((byte)0));
		Assert.That(host.Style.Color.G, Is.EqualTo((byte)128));
		Assert.That(host.Style.Color.B, Is.EqualTo((byte)0));
		Assert.That(before.Style.Color.R, Is.EqualTo((byte)255));
		Assert.That(before.Style.Color.G, Is.EqualTo((byte)0));
		Assert.That(before.Style.Color.B, Is.EqualTo((byte)0));
	}

	[Test]
	public void UiPseudoElement_AttrContent_UsesHostAttributeValue()
	{
		const string html = "<div id=\"host\" class=\"icon\" data-label=\"Ready\">X</div>";
		const string css = ".icon::before { content: attr(data-label); }";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode before = scene.QuerySelector(".icon::before")!;

		Assert.That(before.TextContent, Is.EqualTo("Ready"));
	}

	[Test]
	public void UiPseudoElement_AttrContentMissingAttribute_GeneratesEmptyTextNode()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = ".icon::after { content: attr(data-label); }";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode after = scene.QuerySelector(".icon::after")!;

		Assert.That(after, Is.Not.Null);
		Assert.That(after.TextContent, Is.EqualTo(string.Empty));
		Assert.That(after.PseudoElement, Is.EqualTo(UiPseudoElement.After));
	}

	[Test]
	public void UiPseudoElement_UrlContent_IsIgnoredAndDoesNotGenerateNode()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = """
			.icon { color: rgb(0, 128, 0); }
			.icon::before { content: url(icon.png); color: rgb(255, 0, 0); }
			.icon::after { content: "ok"; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode host = scene.FindByElementId("host")!;

		Assert.That(scene.QuerySelector(".icon::before"), Is.Null, "content:url(...) is unsupported in v1 and must not synthesize a node");
		Assert.That(scene.QuerySelector(".icon::after"), Is.Not.Null, "sibling string content still synthesizes, proving url ignore is explicit not a total pseudo failure");
		Assert.That(host.Style.Color.R, Is.EqualTo((byte)0), "url() before rule color must not leak onto the host");
		Assert.That(host.Style.Color.G, Is.EqualTo((byte)128));
	}

	[Test]
	public void UiPseudoElement_ContentNone_DoesNotGenerateNode()
	{
		const string html = "<div id=\"host\" class=\"icon\">X</div>";
		const string css = """
			.icon::before { content: "x"; }
			.icon::before { content: none; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);

		Assert.That(scene.QuerySelector(".icon::before"), Is.Null);
	}

	[Test]
	public void UiPseudoElement_LayoutHundredNodes_CompletesUnderFiveMilliseconds()
	{
		var html = new System.Text.StringBuilder();
		html.Append("<div id=\"root\">");
		for (int i = 0; i < 100; i++)
		{
			html.Append("<span class=\"cell\" data-label=\"n").Append(i).Append("\">").Append(i).Append("</span>");
		}
		html.Append("</div>");
		const string css = """
			.cell::before { content: attr(data-label); color: red; }
			.cell::after { content: "."; }
			""";

		UiScene scene = BuildScene(html.ToString(), css);
		scene.Layout(1280f, 720f);
		scene.Layout(1280f, 720f);
		var sw = Stopwatch.StartNew();
		scene.Layout(1281f, 721f);
		sw.Stop();

		Assert.That(scene.QuerySelectorAll(".cell::before").Count, Is.EqualTo(100));
		Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5.0), $"100-node layout took {sw.Elapsed.TotalMilliseconds:F3}ms");
	}
}
