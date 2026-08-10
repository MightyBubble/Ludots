using System.Diagnostics;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiGridLayoutTests
{
	private static readonly IUiTextMeasurer TextMeasurer = new SkiaTextMeasurer();
	private static readonly IUiImageSizeProvider ImageSizeProvider = new SkiaImageSizeProvider();

	private static UiScene BuildScene(string html, string css)
	{
		return new UiMarkupLoader().LoadScene(TextMeasurer, ImageSizeProvider, html, css);
	}

	[Test]
	public void UiStyleResolver_ParseGridTemplate_FixedTracks()
	{
		Assert.That(UiStyleResolver.TryParseGridTemplate("100px 200px", out IReadOnlyList<UiGridTrack> tracks), Is.True);
		Assert.That(tracks.Count, Is.EqualTo(2));
		Assert.That(tracks[0].Sizing, Is.EqualTo(UiGridTrackSizing.Pixel));
		Assert.That(tracks[0].Value, Is.EqualTo(100f).Within(0.01f));
		Assert.That(tracks[1].Value, Is.EqualTo(200f).Within(0.01f));
	}

	[Test]
	public void UiStyleResolver_ParseGridTemplate_RepeatFr()
	{
		Assert.That(UiStyleResolver.TryParseGridTemplate("repeat(3, 1fr)", out IReadOnlyList<UiGridTrack> tracks), Is.True);
		Assert.That(tracks.Count, Is.EqualTo(3));
		Assert.That(tracks[0].Sizing, Is.EqualTo(UiGridTrackSizing.Fr));
		Assert.That(tracks[2].Value, Is.EqualTo(1f).Within(0.01f));
	}

	[Test]
	public void UiStyleResolver_ParseGridTemplate_MinMax_IsIgnored()
	{
		Assert.That(UiStyleResolver.TryParseGridTemplate("minmax(100px, 1fr) 1fr", out _), Is.False, "minmax() is unsupported in MVP and must fail closed");
	}

	[Test]
	public void UiGrid_FixedTracks_AssignsPixelColumns()
	{
		const string html = """
			<div id="grid">
			  <div id="a">A</div>
			  <div id="b">B</div>
			</div>
			""";
		const string css = """
			#grid { display: grid; grid-template-columns: 100px 200px; width: 300px; height: 40px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode a = scene.FindByElementId("a")!;
		UiNode b = scene.FindByElementId("b")!;

		Assert.That(a.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(b.LayoutRect.Width, Is.EqualTo(200f).Within(0.5f));
		Assert.That(b.LayoutRect.X, Is.EqualTo(a.LayoutRect.Right).Within(0.5f));
	}

	[Test]
	public void UiGrid_FrTracks_AllocateOneToTwo()
	{
		const string html = """
			<div id="grid">
			  <div id="a">A</div>
			  <div id="b">B</div>
			</div>
			""";
		const string css = """
			#grid { display: grid; grid-template-columns: 1fr 2fr; width: 300px; height: 40px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode a = scene.FindByElementId("a")!;
		UiNode b = scene.FindByElementId("b")!;

		Assert.That(a.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(b.LayoutRect.Width, Is.EqualTo(200f).Within(0.5f));
	}

	[Test]
	public void UiGrid_MixedFixedAndFr_ResolvesRemainingSpace()
	{
		const string html = """
			<div id="grid">
			  <div id="a">A</div>
			  <div id="b">B</div>
			  <div id="c">C</div>
			</div>
			""";
		const string css = """
			#grid { display: grid; grid-template-columns: 100px 1fr 2fr; width: 400px; height: 40px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);

		Assert.That(scene.FindByElementId("a")!.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(scene.FindByElementId("b")!.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(scene.FindByElementId("c")!.LayoutRect.Width, Is.EqualTo(200f).Within(0.5f));
	}

	[Test]
	public void UiGrid_Gap_SubtractsBetweenThreeColumns()
	{
		const string html = """
			<div id="grid">
			  <div id="a">A</div>
			  <div id="b">B</div>
			  <div id="c">C</div>
			</div>
			""";
		const string css = """
			#grid { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 10px; width: 320px; height: 40px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode a = scene.FindByElementId("a")!;
		UiNode b = scene.FindByElementId("b")!;
		UiNode c = scene.FindByElementId("c")!;

		Assert.That(a.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(b.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(c.LayoutRect.Width, Is.EqualTo(100f).Within(0.5f));
		Assert.That(b.LayoutRect.X - a.LayoutRect.Right, Is.EqualTo(10f).Within(0.5f));
		Assert.That(c.LayoutRect.X - b.LayoutRect.Right, Is.EqualTo(10f).Within(0.5f));
	}

	[Test]
	public void UiGrid_GridColumnSpan_OccupiesTwoTracks()
	{
		const string html = """
			<div id="grid">
			  <div id="a">A</div>
			  <div id="span">S</div>
			</div>
			""";
		const string css = """
			#grid { display: grid; grid-template-columns: 100px 100px 100px; width: 300px; height: 80px; }
			#span { grid-column: 2 / span 2; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode span = scene.FindByElementId("span")!;

		Assert.That(span.LayoutRect.X, Is.EqualTo(scene.FindByElementId("grid")!.LayoutRect.X + 100f).Within(1f));
		Assert.That(span.LayoutRect.Width, Is.EqualTo(200f).Within(0.5f));
	}

	[Test]
	public void UiGrid_UnplacedItems_AutoPlaceInDocumentOrder()
	{
		const string html = """
			<div id="grid">
			  <div id="a">A</div>
			  <div id="b">B</div>
			  <div id="c">C</div>
			</div>
			""";
		const string css = """
			#grid { display: grid; grid-template-columns: 50px 50px; width: 100px; height: 100px; }
			""";

		UiScene scene = BuildScene(html, css);
		scene.Layout(800f, 600f);
		UiNode a = scene.FindByElementId("a")!;
		UiNode b = scene.FindByElementId("b")!;
		UiNode c = scene.FindByElementId("c")!;

		Assert.That(a.LayoutRect.X, Is.EqualTo(b.LayoutRect.X - 50f).Within(1f));
		Assert.That(a.LayoutRect.Y, Is.EqualTo(b.LayoutRect.Y).Within(1f));
		Assert.That(c.LayoutRect.Y, Is.GreaterThan(a.LayoutRect.Y + 1f), "third item wraps to next row by document-order auto-placement");
		Assert.That(c.LayoutRect.X, Is.EqualTo(a.LayoutRect.X).Within(1f));
	}

	[Test]
	public void UiGrid_LayoutHundredNodes_CompletesUnderFiveMilliseconds()
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
		for (int i = 0; i < 3; i++)
		{
			scene.Layout(1280f + i, 720f + i);
		}
		var sw = Stopwatch.StartNew();
		scene.Layout(1290f, 730f);
		sw.Stop();

		Assert.That(scene.FindByElementId("grid")!.Children.Count, Is.EqualTo(100));
		Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(5.0), $"100-node grid layout took {sw.Elapsed.TotalMilliseconds:F3}ms");
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
