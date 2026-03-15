using System.Collections.Generic;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Input;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using NUnit.Framework;
using UiShowcaseCoreMod.Showcase;
using UiSkinClassicMod;
using UiSkinPaperMod;
using UiSkinSciFiHudMod;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiShowcaseAcceptanceTests
{
    private static readonly IUiTextMeasurer TextMeasurer = new SkiaTextMeasurer();
    private static readonly IUiImageSizeProvider ImageSizeProvider = new SkiaImageSizeProvider();

    [Test]
    public void ComposeScene_RendersOfficialSections_AndResolvesIds()
    {
        UiScene scene = UiShowcaseFactory.CreateComposeScene(TextMeasurer, ImageSizeProvider);
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
        var page = UiShowcaseFactory.CreateReactivePage(TextMeasurer, ImageSizeProvider);
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
        var page = UiShowcaseFactory.CreateReactivePage(TextMeasurer, ImageSizeProvider);
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
    public void ReactiveScene_ScrollVirtualWindow_RefreshesThroughUIRootLifecycle()
    {
        var page = new ReactivePage<int>(new SkiaTextMeasurer(), new SkiaImageSizeProvider(), 0, BuildVirtualizedList);
        var root = new UIRoot(new SkiaUiRenderer());
        root.Resize(1280f, 720f);
        root.MountScene(page.Scene);
        page.Scene.Layout(1280f, 720f);

        Assert.That(page.Scene.TryGetVirtualWindow("ui-showcase-virtual-window", out UiVirtualWindow initialWindow), Is.True);
        Assert.That(initialWindow.VisibleCount, Is.GreaterThan(0));

        UiNode scrollHost = page.Scene.FindByElementId("ui-showcase-virtual-window")!;
        bool handled = root.HandleInput(new PointerEvent
        {
            DeviceType = InputDeviceType.Mouse,
            PointerId = 0,
            Action = PointerAction.Scroll,
            X = scrollHost.LayoutRect.X + 12f,
            Y = scrollHost.LayoutRect.Y + 12f,
            DeltaY = 120f
        });

        Assert.That(handled, Is.True);
        Assert.That(page.LastUpdateMetrics.Reason, Is.EqualTo(UiReactiveUpdateReason.RuntimeWindowChange));
        Assert.That(page.LastUpdateStats.Mode, Is.EqualTo(ReactiveApplyMode.IncrementalPatch));
        Assert.That(page.Scene.TryGetVirtualWindow("ui-showcase-virtual-window", out UiVirtualWindow scrolledWindow), Is.True);
        Assert.That(scrolledWindow.StartIndex, Is.GreaterThan(0));
    }

    [Test]
    public void ReactiveScene_ThemeSwitch_ChangesRootComputedStyle()
    {
        var page = UiShowcaseFactory.CreateReactivePage(TextMeasurer, ImageSizeProvider);
        page.Scene.Layout(1280, 720);
        UiColor before = page.Scene.Root!.Style.BackgroundColor;
        UiNode button = page.Scene.FindByElementId("reactive-theme-light")!;

        UiEventResult result = page.Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        page.Scene.Layout(1280, 720);
        UiColor after = page.Scene.Root!.Style.BackgroundColor;

        Assert.That(result.Handled, Is.True);
        Assert.That(after, Is.Not.EqualTo(before));
    }

    [Test]
    public void MarkupScene_ClickIncrement_RebindsCodeBehindAndUpdatesText()
    {
        UiScene scene = UiShowcaseFactory.CreateMarkupScene(TextMeasurer, ImageSizeProvider);
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
        UiScene scene = UiShowcaseFactory.CreateMarkupScene(TextMeasurer, ImageSizeProvider);
        scene.Layout(1280, 720);

        Assert.That(scene.FindByElementId("markup-prototype"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-radio-primary"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-stats-table"), Is.Not.Null);
        Assert.That(scene.QuerySelectorAll(".prototype-box").Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void SkinFixture_SameDomDifferentThemes_ProducesDifferentResolvedColors()
    {
        UiScene classic = UiShowcaseFactory.CreateSkinFixtureScene(TextMeasurer, ImageSizeProvider, UiSkinClassicModEntry.Theme);
        UiScene scifi = UiShowcaseFactory.CreateSkinFixtureScene(TextMeasurer, ImageSizeProvider, UiSkinSciFiHudModEntry.Theme);
        UiScene paper = UiShowcaseFactory.CreateSkinFixtureScene(TextMeasurer, ImageSizeProvider, UiSkinPaperModEntry.Theme);
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
        UiScene paper = UiShowcaseFactory.CreateSkinFixtureScene(TextMeasurer, ImageSizeProvider, UiSkinPaperModEntry.Theme);
        paper.Layout(1280, 720);

        UiNode description = paper.FindByElementId("skin-description")!;
        UiNode card = paper.FindByElementId("skin-fixture-card")!;

        Assert.That(ContrastRatio(description.Style.Color, card.Style.BackgroundColor), Is.GreaterThanOrEqualTo(4.5d));
    }

    [Test]
    public void SkinShowcase_RuntimeThemeSwitch_PreservesDomHashAndChangesStyle()
    {
        UiScene scene = UiShowcaseFactory.CreateSkinShowcaseScene(TextMeasurer, ImageSizeProvider);
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

    private static double ContrastRatio(UiColor foreground, UiColor background)
    {
        double fg = RelativeLuminance(foreground);
        double bg = RelativeLuminance(background);
        double lighter = Math.Max(fg, bg);
        double darker = Math.Min(fg, bg);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static UiElementBuilder BuildVirtualizedList(ReactiveContext<int> context)
    {
        const string hostId = "ui-showcase-virtual-window";
        const int totalCount = 64;
        const float rowHeight = 22f;
        const float viewportHeight = 180f;

        UiVirtualWindow window = context.GetVerticalVirtualWindow(hostId, totalCount, rowHeight, viewportHeight, overscan: 2);
        var rows = new List<UiElementBuilder>();
        if (window.LeadingSpacerExtent > 0.01f)
        {
            rows.Add(Ui.Spacer(window.LeadingSpacerExtent));
        }

        for (int i = window.StartIndex; i < window.EndIndexExclusive; i++)
        {
            rows.Add(Ui.Text($"Row {i + 1:00}").Id($"ui-showcase-row-{i:00}").FontSize(12f));
        }

        if (window.TrailingSpacerExtent > 0.01f)
        {
            rows.Add(Ui.Spacer(window.TrailingSpacerExtent));
        }

        return Ui.Card(
                Ui.Text("Virtual Window").FontSize(18f).Bold(),
                Ui.ScrollView(rows.ToArray())
                    .Id(hostId)
                    .Height(viewportHeight)
                    .Padding(8f)
                    .Gap(4f))
            .Width(360f)
            .Padding(16f)
            .Gap(10f);
    }

    private static double RelativeLuminance(UiColor color)
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

