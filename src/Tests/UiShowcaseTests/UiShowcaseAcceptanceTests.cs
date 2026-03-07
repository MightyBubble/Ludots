using SkiaSharp;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using NUnit.Framework;
using UiShowcaseCoreMod.Showcase;
using UiSkinClassicMod;
using UiSkinSciFiHudMod;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiShowcaseAcceptanceTests
{
    [Test]
    public void ComposeScene_RendersOfficialSections_AndResolvesIds()
    {
        UiScene scene = UiShowcaseFactory.CreateComposeScene();
        scene.Layout(1280, 720);

        Assert.That(scene.FindByElementId("compose-primary"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-form-status"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-selected"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-density"), Is.Not.Null);
    }

    [Test]
    public void ReactiveScene_ClickIncrement_UpdatesCounterNodeText()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        page.Scene.Layout(1280, 720);
        UiNode button = page.Scene.FindByElementId("reactive-inc")!;
        UiNode counterBefore = page.Scene.FindByElementId("reactive-count")!;

        UiEventResult result = page.Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        page.Scene.Layout(1280, 720);
        UiNode counterAfter = page.Scene.FindByElementId("reactive-count")!;

        Assert.That(result.Handled, Is.True);
        Assert.That(counterBefore.TextContent, Is.Not.EqualTo(counterAfter.TextContent));
        Assert.That(counterAfter.TextContent, Does.Contain("4"));
    }

    [Test]
    public void ReactiveScene_ThemeSwitch_ChangesRootComputedStyle()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        page.Scene.Layout(1280, 720);
        SKColor before = page.Scene.Root!.Style.BackgroundColor;
        UiNode button = page.Scene.FindByElementId("reactive-theme-light")!;

        UiEventResult result = page.Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        page.Scene.Layout(1280, 720);
        SKColor after = page.Scene.Root!.Style.BackgroundColor;

        Assert.That(result.Handled, Is.True);
        Assert.That(after, Is.Not.EqualTo(before));
    }

    [Test]
    public void MarkupScene_ClickIncrement_RebindsCodeBehindAndUpdatesText()
    {
        UiScene scene = UiShowcaseFactory.CreateMarkupScene();
        scene.Layout(1280, 720);
        UiNode before = scene.FindByElementId("markup-count")!;
        UiNode button = scene.FindByElementId("markup-inc")!;

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        scene.Layout(1280, 720);
        UiNode after = scene.FindByElementId("markup-count")!;

        Assert.That(result.Handled, Is.True);
        Assert.That(before.TextContent, Is.Not.EqualTo(after.TextContent));
        Assert.That(after.TextContent, Does.Contain("6"));
    }

    [Test]
    public void MarkupScene_PrototypeImportPage_ExposesDiagnostics()
    {
        UiScene scene = UiShowcaseFactory.CreateMarkupScene();
        scene.Layout(1280, 720);

        Assert.That(scene.FindByElementId("markup-prototype"), Is.Not.Null);
        Assert.That(scene.QuerySelectorAll(".prototype-box").Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void SkinFixture_SameDomDifferentThemes_ProducesDifferentResolvedColors()
    {
        UiScene classic = UiShowcaseFactory.CreateSkinFixtureScene(UiSkinClassicModEntry.Theme);
        UiScene scifi = UiShowcaseFactory.CreateSkinFixtureScene(UiSkinSciFiHudModEntry.Theme);
        classic.Layout(1280, 720);
        scifi.Layout(1280, 720);

        UiNode classicRoot = classic.Root!;
        UiNode scifiRoot = scifi.Root!;
        UiNode classicConfirm = classic.FindByElementId("skin-confirm")!;
        UiNode scifiConfirm = scifi.FindByElementId("skin-confirm")!;

        Assert.That(UiDomHasher.Hash(classic), Is.EqualTo(UiDomHasher.Hash(scifi)));
        Assert.That(classicRoot.TagName, Is.EqualTo(scifiRoot.TagName));
        Assert.That(classicConfirm.TagName, Is.EqualTo(scifiConfirm.TagName));
        Assert.That(classicRoot.Style.BackgroundColor, Is.Not.EqualTo(scifiRoot.Style.BackgroundColor));
        Assert.That(classicConfirm.Style.BackgroundColor, Is.Not.EqualTo(scifiConfirm.Style.BackgroundColor));
    }

    [Test]
    public void SkinShowcase_RuntimeThemeSwitch_PreservesDomHashAndChangesStyle()
    {
        UiScene scene = UiShowcaseFactory.CreateSkinShowcaseScene();
        scene.Layout(1280, 720);
        string beforeHash = UiDomHasher.Hash(scene);
        var beforeColor = scene.Root!.Style.BackgroundColor;
        UiNode button = scene.FindByElementId("skin-theme-paper")!;

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        scene.Layout(1280, 720);
        string afterHash = UiDomHasher.Hash(scene);
        var afterColor = scene.Root!.Style.BackgroundColor;

        Assert.That(result.Handled, Is.True);
        Assert.That(afterHash, Is.EqualTo(beforeHash));
        Assert.That(afterColor, Is.Not.EqualTo(beforeColor));
    }
}

