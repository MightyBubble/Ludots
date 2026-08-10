using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiStickyPositionTests
{
	[Test]
	public void StickyHeader_OffsetsAfterScrollAndHitTestTracksVisual()
	{
		const string html = """
			<div id="scroller">
			  <div id="header">Header</div>
			  <div id="body">Body</div>
			</div>
			""";
		const string css = """
			#scroller { display: flex; flex-direction: column; overflow: scroll; width: 200px; height: 100px; }
			#header { position: sticky; top: 0px; z-index: 1; width: 200px; height: 30px; background: rgb(200, 20, 20); }
			#body { width: 200px; height: 300px; background: rgb(20, 20, 20); }
			""";

		UiScene scene = new UiMarkupLoader().LoadScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider(), html, css);
		scene.Layout(300f, 200f);
		UiNode scroller = scene.FindByElementId("scroller")!;
		UiNode header = scene.FindByElementId("header")!;

		Assert.That(header.Style.PositionType, Is.EqualTo(UiPositionType.Sticky));
		Assert.That(header.StickyOffsetY, Is.EqualTo(0f).Within(0.01f));

		UiEventResult result = scene.Dispatch(new UiPointerEvent(
			UiPointerEventType.Scroll,
			0,
			scroller.LayoutRect.X + 10f,
			scroller.LayoutRect.Y + 10f,
			scroller.Id,
			0f,
			50f));

		Assert.That(result.Handled, Is.True);
		Assert.That(scroller.ScrollOffsetY, Is.EqualTo(50f).Within(0.5f));
		Assert.That(header.StickyOffsetY, Is.GreaterThan(0f));
		Assert.That(scene.HitTest(scroller.LayoutRect.X + 10f, scroller.LayoutRect.Y + 10f)?.Id, Is.EqualTo(header.Id));
	}
}
