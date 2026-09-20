using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiLineHeightTests
{
    [Test]
    public void CssLineHeight_ControlsMeasuredMultilineText()
    {
        UiScene scene = new UiMarkupLoader().LoadScene(
            new SkiaTextMeasurer(),
            new MissingImageSizeProvider(),
            """<div id="body">first<br>second</div>""",
            """#body { width: 200px; font-size: 20px; line-height: 1.8; }""");

        scene.Layout(320f, 200f);

        UiNode body = scene.FindByElementId("body")!;
        Assert.That(body.Style.LineHeight, Is.EqualTo(1.8f));
        Assert.That(body.LayoutRect.Height, Is.GreaterThanOrEqualTo(72f));
    }

    private sealed class MissingImageSizeProvider : IUiImageSizeProvider
    {
        public bool TryGetSize(string? source, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            return false;
        }
    }
}
