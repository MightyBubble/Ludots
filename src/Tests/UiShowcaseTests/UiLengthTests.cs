using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiLengthTests
{
	private static readonly IUiTextMeasurer TextMeasurer = new SkiaTextMeasurer();
	private static readonly IUiImageSizeProvider ImageSizeProvider = new SkiaImageSizeProvider();

	[Test]
	public void UiLength_ResolveVw_ReturnsViewportFraction()
	{
		UiLength length = UiLength.Viewport(50f, UiLengthUnit.Vw);
		float result = length.Resolve(new UiLengthContext(0f, 1000f, 800f));
		Assert.That(result, Is.EqualTo(500f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveVh_ReturnsViewportFraction()
	{
		UiLength length = UiLength.Viewport(25f, UiLengthUnit.Vh);
		float result = length.Resolve(new UiLengthContext(0f, 1000f, 800f));
		Assert.That(result, Is.EqualTo(200f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveVmin_ReturnsSmallerViewportAxis()
	{
		UiLength length = UiLength.Viewport(10f, UiLengthUnit.Vmin);
		float result = length.Resolve(new UiLengthContext(0f, 1000f, 800f));
		Assert.That(result, Is.EqualTo(80f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveVmax_ReturnsLargerViewportAxis()
	{
		UiLength length = UiLength.Viewport(10f, UiLengthUnit.Vmax);
		float result = length.Resolve(new UiLengthContext(0f, 1000f, 800f));
		Assert.That(result, Is.EqualTo(100f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveViewportUnitWithoutContext_Throws()
	{
		UiLength length = UiLength.Viewport(10f, UiLengthUnit.Vw);
		Assert.That(() => length.Resolve(500f), Throws.InvalidOperationException);
	}

	[Test]
	public void UiLength_ResolveCalcMixedUnits_ReturnsPixelTotal()
	{
		UiCalcExpression expression = new UiCalcExpression(
			new UiCalcExpression(UiLength.Percent(100f)),
			UiCalcOperator.Subtract,
			new UiCalcExpression(UiLength.Px(40f)));
		UiLength length = UiLength.CalcExpression(expression);
		float result = length.Resolve(new UiLengthContext(1000f, 0f, 0f));
		Assert.That(result, Is.EqualTo(960f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveCalcNested_ReturnsExpectedTotal()
	{
		UiCalcExpression nested = new UiCalcExpression(
			new UiCalcExpression(UiLength.Px(5f)),
			UiCalcOperator.Multiply,
			new UiCalcExpression(UiLength.Px(2f)));
		UiCalcExpression outer = new UiCalcExpression(
			new UiCalcExpression(UiLength.Px(10f)),
			UiCalcOperator.Add,
			nested);
		UiLength length = UiLength.CalcExpression(outer);
		float result = length.Resolve(new UiLengthContext(0f, 0f, 0f));
		Assert.That(result, Is.EqualTo(20f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveCalcPrecedence_MultiplyBindsBeforeAdd()
	{
		UiCalcExpression expression = new UiCalcExpression(
			new UiCalcExpression(UiLength.Px(10f)),
			UiCalcOperator.Add,
			new UiCalcExpression(
				new UiCalcExpression(UiLength.Px(5f)),
				UiCalcOperator.Multiply,
				new UiCalcExpression(UiLength.Px(2f))));
		UiLength length = UiLength.CalcExpression(expression);
		float result = length.Resolve(new UiLengthContext(0f, 0f, 0f));
		Assert.That(result, Is.EqualTo(20f).Within(0.01f));
	}

	[Test]
	public void UiLength_ResolveCalcDivisionByZero_ReturnsNaN()
	{
		UiCalcExpression expression = new UiCalcExpression(
			new UiCalcExpression(UiLength.Px(100f)),
			UiCalcOperator.Divide,
			new UiCalcExpression(UiLength.Px(0f)));
		UiLength length = UiLength.CalcExpression(expression);
		float result = length.Resolve(new UiLengthContext(0f, 0f, 0f));
		Assert.That(float.IsNaN(result), Is.True);
	}

	private static UiScene BuildScene(string html, string css)
	{
		return new UiMarkupLoader().LoadScene(TextMeasurer, ImageSizeProvider, html, css);
	}

	[Test]
	public void CssScene_CalcWidthMixedUnits_ResolvesAgainstParentContent()
	{
		const string html = "<div id=\"wrap\" style=\"width: 1000px\"><div id=\"box\" style=\"width: calc(100% - 40px)\"></div></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(960f).Within(0.01f));
	}

	[Test]
	public void CssScene_NestedCalcWidth_ResolvesToPixelTotal()
	{
		const string html = "<div id=\"box\" style=\"width: calc(10px + calc(5px * 2))\"></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(20f).Within(0.01f));
	}

	[Test]
	public void CssScene_CalcWithViewportTerm_ResolvesImmediately()
	{
		const string html = "<div id=\"box\" style=\"width: calc(50vw - 20px)\"></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(620f).Within(0.01f));
	}

	[Test]
	public void CssScene_CalcPercentAndViewport_ResolvesAgainstParentAndViewport()
	{
		const string html = "<div id=\"wrap\" style=\"width: 1000px\"><div id=\"box\" style=\"width: calc(50% + 25vw)\"></div></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(820f).Within(0.01f));
	}

	[Test]
	public void CssScene_ViewportUnits_ResolveAgainstSceneSize()
	{
		const string html = "<div id=\"box\" style=\"width: 50vw; height: 25vh\"></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(640f).Within(0.01f));
		Assert.That(box.LayoutRect.Height, Is.EqualTo(180f).Within(0.01f));
	}

	[Test]
	public void CssScene_ViewportUnits_ReResolveAfterResize()
	{
		const string html = "<div id=\"box\" style=\"width: 50vw; height: 25vh\"></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		scene.Layout(800f, 600f);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(400f).Within(0.01f));
		Assert.That(box.LayoutRect.Height, Is.EqualTo(150f).Within(0.01f));
	}

	[Test]
	public void CssScene_MalformedCalc_DropsDeclaration()
	{
		const string html = "<div id=\"wrap\" style=\"width: 1000px; display: flex; flex-direction: column; align-items: flex-start;\">" +
			"<div id=\"box\" style=\"width: calc(10px + )\"></div>" +
			"<div id=\"ctrl\" style=\"width: 10px\"></div></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		UiNode? ctrl = scene.FindByElementId("ctrl");
		Assert.That(box, Is.Not.Null);
		Assert.That(ctrl, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(0f).Within(0.01f));
		Assert.That(ctrl!.LayoutRect.Width, Is.EqualTo(10f).Within(0.01f));
	}

	[Test]
	public void CssScene_CalcDivisionByZero_BehavesAsAuto()
	{
		const string html = "<div id=\"wrap\" style=\"width: 1000px; display: flex; flex-direction: column; align-items: flex-start;\">" +
			"<div id=\"box\" style=\"width: calc(100px / 0)\"></div></div>";
		UiScene scene = BuildScene(html, string.Empty);
		scene.Layout(1280f, 720f);
		UiNode? box = scene.FindByElementId("box");
		Assert.That(box, Is.Not.Null);
		Assert.That(box!.LayoutRect.Width, Is.EqualTo(0f).Within(0.01f));
	}
}
