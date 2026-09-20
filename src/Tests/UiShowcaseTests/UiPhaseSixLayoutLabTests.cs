using System.Linq;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;
using UiShowcaseCoreMod.Showcase;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiPhaseSixLayoutLabTests
{
	[Test]
	public void Compose_PhaseSix_GridAutoColumnsPreferContentWidth()
	{
		UiScene scene = UiShowcaseFactory.CreateComposeScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		scene.Layout(1280f, 720f);
		AssertPhaseSix(scene, "compose");
	}

	[Test]
	public void Reactive_PhaseSix_GridAutoColumnsPreferContentWidth()
	{
		UiScene scene = UiShowcaseFactory.CreateReactivePage(new SkiaTextMeasurer(), new SkiaImageSizeProvider()).Scene;
		scene.Layout(1280f, 720f);
		AssertPhaseSix(scene, "reactive");
	}

	[Test]
	public void Markup_PhaseSix_GridAutoColumnsPreferContentWidth()
	{
		UiScene scene = UiShowcaseFactory.CreateMarkupScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		scene.Layout(1280f, 720f);
		AssertPhaseSix(scene, "markup");
	}

	private static void AssertPhaseSix(UiScene scene, string prefix)
	{
		UiNode panel = scene.FindByElementId(prefix + "-phase6-panel")!;
		UiNode grid = scene.FindByElementId(prefix + "-phase6-grid")!;
		UiNode shortCell = scene.FindByElementId(prefix + "-phase6-short")!;
		UiNode longCell = scene.FindByElementId(prefix + "-phase6-long")!;
		UiNode sticky = scene.FindByElementId(prefix + "-phase6-sticky")!;
		UiNode icon = scene.FindByElementId(prefix + "-phase6-icon")!;

		Assert.That(panel, Is.Not.Null);
		Assert.That(grid.Style.Display, Is.EqualTo(UiDisplay.Grid));
		Assert.That(grid.Style.GridTemplateColumns.Count, Is.EqualTo(3));
		Assert.That(grid.Style.GridTemplateColumns[0].MaxSizing, Is.EqualTo(UiGridTrackSizing.Auto));
		Assert.That(longCell.LayoutRect.Width, Is.GreaterThan(shortCell.LayoutRect.Width + 24f));
		Assert.That(sticky.Style.PositionType, Is.EqualTo(UiPositionType.Sticky));
		if (prefix == "markup")
		{
			Assert.That(icon.Children.Any(child =>
				child.PseudoElement == UiPseudoElement.Before && child.Kind == UiNodeKind.Image), Is.True,
				"Markup path synthesizes ::before image nodes from content:url");
		}
		else
		{
			UiNode? iconImage = scene.FindByElementId(prefix + "-phase6-icon-img");
			Assert.That(iconImage, Is.Not.Null, "Compose/Reactive demo the icon with an explicit Image node");
			Assert.That(iconImage!.Attributes["src"], Does.StartWith("data:image/svg+xml"));
		}
	}
}
