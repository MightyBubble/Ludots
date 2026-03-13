using SkiaSharp;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using NUnit.Framework;
using UiShowcaseCoreMod.Showcase;
using UiSkinClassicMod;
using UiSkinPaperMod;
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
        Assert.That(scene.FindByElementId("compose-radio-primary"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-stats-table"), Is.Not.Null);
    }

    [Test]
    public void ReactiveScene_ClickIncrement_UpdatesCounterNodeText()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        page.Scene.Layout(1280, 720);
        UiNode button = page.Scene.FindByElementId("reactive-inc")!;
        UiNode counterBefore = page.Scene.FindByElementId("reactive-count")!;
        string beforeText = counterBefore.TextContent ?? string.Empty;
        Assert.That(page.Scene.FindByElementId("reactive-radio-primary"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-stats-table"), Is.Not.Null);

        UiEventResult result = page.Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        page.Scene.Layout(1280, 720);
        UiNode counterAfter = page.Scene.FindByElementId("reactive-count")!;

        Assert.That(result.Handled, Is.True);
        Assert.That(beforeText, Is.Not.EqualTo(counterAfter.TextContent));
        Assert.That(counterAfter.TextContent, Does.Contain("4"));
    }

    [Test]
    public void ReactiveScene_ClickIncrement_ReconcilesExistingNodesIncrementally()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        page.Scene.Layout(1280, 720);
        UiNode button = page.Scene.FindByElementId("reactive-inc")!;
        UiNode counterBefore = page.Scene.FindByElementId("reactive-count")!;
        long fullBefore = page.FullRecomposeCount;
        long incrementalBefore = page.IncrementalPatchCount;

        UiEventResult result = page.Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        page.Scene.Layout(1280, 720);
        UiNode counterAfter = page.Scene.FindByElementId("reactive-count")!;

        Assert.That(result.Handled, Is.True);
        Assert.That(ReferenceEquals(counterBefore, counterAfter), Is.True,
            "Incremental reactive updates should preserve existing UiNode instances when the scene shape is unchanged.");
        Assert.That(page.LastUpdateStats.Mode, Is.EqualTo(ReactiveApplyMode.IncrementalPatch));
        Assert.That(page.LastUpdateStats.PatchedNodes, Is.GreaterThan(0));
        Assert.That(page.FullRecomposeCount, Is.EqualTo(fullBefore));
        Assert.That(page.IncrementalPatchCount, Is.EqualTo(incrementalBefore + 1));
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
        Assert.That(scene.FindByElementId("markup-radio-primary"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-stats-table"), Is.Not.Null);
        Assert.That(scene.QuerySelectorAll(".prototype-box").Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void SkinFixture_SameDomDifferentThemes_ProducesDifferentResolvedColors()
    {
        UiScene classic = UiShowcaseFactory.CreateSkinFixtureScene(UiSkinClassicModEntry.Theme);
        UiScene scifi = UiShowcaseFactory.CreateSkinFixtureScene(UiSkinSciFiHudModEntry.Theme);
        UiScene paper = UiShowcaseFactory.CreateSkinFixtureScene(UiSkinPaperModEntry.Theme);
        classic.Layout(1280, 720);
        scifi.Layout(1280, 720);
        paper.Layout(1280, 720);

        UiNode classicRoot = classic.Root!;
        UiNode scifiRoot = scifi.Root!;
        UiNode paperRoot = paper.Root!;
        UiNode classicConfirm = classic.FindByElementId("skin-confirm")!;
        UiNode scifiConfirm = scifi.FindByElementId("skin-confirm")!;
        UiNode paperConfirm = paper.FindByElementId("skin-confirm")!;

        Assert.That(UiDomHasher.Hash(classic), Is.EqualTo(UiDomHasher.Hash(scifi)));
        Assert.That(UiDomHasher.Hash(classic), Is.EqualTo(UiDomHasher.Hash(paper)));
        Assert.That(classicRoot.TagName, Is.EqualTo(scifiRoot.TagName));
        Assert.That(classicRoot.TagName, Is.EqualTo(paperRoot.TagName));
        Assert.That(classicConfirm.TagName, Is.EqualTo(scifiConfirm.TagName));
        Assert.That(classicConfirm.TagName, Is.EqualTo(paperConfirm.TagName));
        Assert.That(classicRoot.Style.BackgroundColor, Is.Not.EqualTo(scifiRoot.Style.BackgroundColor));
        Assert.That(classicConfirm.Style.BackgroundColor, Is.Not.EqualTo(scifiConfirm.Style.BackgroundColor));
        Assert.That(classicRoot.Style.BackgroundColor, Is.Not.EqualTo(paperRoot.Style.BackgroundColor));
        Assert.That(classicConfirm.Style.BackgroundColor, Is.Not.EqualTo(paperConfirm.Style.BackgroundColor));
    }

    [Test]
    public void SkinFixture_PaperTheme_BodyText_HasReadableContrast()
    {
        UiScene paper = UiShowcaseFactory.CreateSkinFixtureScene(UiSkinPaperModEntry.Theme);
        paper.Layout(1280, 720);

        UiNode description = paper.FindByElementId("skin-description")!;
        UiNode card = paper.FindByElementId("skin-fixture-card")!;

        Assert.That(ContrastRatio(description.Style.Color, card.Style.BackgroundColor), Is.GreaterThanOrEqualTo(4.5d));
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

    private static double ContrastRatio(SKColor foreground, SKColor background)
    {
        double fg = RelativeLuminance(foreground);
        double bg = RelativeLuminance(background);
        double lighter = Math.Max(fg, bg);
        double darker = Math.Min(fg, bg);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static double RelativeLuminance(SKColor color)
    {
        static double Linearize(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.03928d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(color.Red))
             + (0.7152d * Linearize(color.Green))
             + (0.0722d * Linearize(color.Blue));
    }
}

