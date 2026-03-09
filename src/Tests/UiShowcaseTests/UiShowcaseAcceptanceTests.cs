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
        Assert.That(scene.FindByElementId("compose-radio-primary"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-email-input"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-password-input"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-notes-input"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-stats-table"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-frosted-card"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-wrap-demo"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-scroll-host"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-clip-host"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-frosted-card")!.Style.BackdropBlurRadius, Is.GreaterThan(0f));
        Assert.That(scene.FindByElementId("compose-transition-probe"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-rtl-text"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-image-cover"), Is.Not.Null);
        Assert.That(scene.FindByElementId("compose-image-nine"), Is.Not.Null);
        AssertPhaseOneShowcase(scene, "compose");
        AssertPhaseTwoShowcase(scene, "compose");
        AssertPhaseThreeShowcase(scene, "compose");
        AssertPhaseFourShowcase(scene, "compose");
        AssertPhaseFiveShowcase(scene, "compose");
    }

    [Test]
    public void ComposeScene_ScrollHost_WheelScrollUpdatesOffset()
    {
        UiScene scene = UiShowcaseFactory.CreateComposeScene();
        ScrollHost(scene, "compose-scroll-host");
    }

    [Test]
    public void ComposeScene_FormsAndTables_ExposeValidationAndAutoSizedColumns()
    {
        UiScene scene = UiShowcaseFactory.CreateComposeScene();
        scene.Layout(1280, 720);

        AssertShowcaseFormValidation(scene, "compose");
        UiNode table = scene.FindByElementId("compose-stats-table")!;
        AssertTableFirstColumnWider(table);
        AssertTableSpans(table);
    }

    [Test]
    public void ReactiveScene_ClickIncrement_UpdatesCounterNodeText()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        page.Scene.Layout(1280, 720);
        UiNode button = page.Scene.FindByElementId("reactive-inc")!;
        UiNode counterBefore = page.Scene.FindByElementId("reactive-count")!;
        Assert.That(page.Scene.FindByElementId("reactive-radio-primary"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-stats-table"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-email-input"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-password-input"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-notes-input"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-frosted-card"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-wrap-demo"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-scroll-host"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-clip-host"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-transition-probe"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-rtl-text"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-image-cover"), Is.Not.Null);
        Assert.That(page.Scene.FindByElementId("reactive-image-nine"), Is.Not.Null);
        AssertPhaseOneShowcase(page.Scene, "reactive");
        AssertPhaseTwoShowcase(page.Scene, "reactive");
        AssertPhaseThreeShowcase(page.Scene, "reactive");
        AssertPhaseFourShowcase(page.Scene, "reactive");
        AssertPhaseFiveShowcase(page.Scene, "reactive");

        UiEventResult result = page.Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, button.LayoutRect.X + 2, button.LayoutRect.Y + 2, button.Id));
        page.Scene.Layout(1280, 720);
        UiNode counterAfter = page.Scene.FindByElementId("reactive-count")!;

        Assert.That(result.Handled, Is.True);
        Assert.That(counterBefore.TextContent, Is.Not.EqualTo(counterAfter.TextContent));
        Assert.That(counterAfter.TextContent, Does.Contain("4"));
    }

    [Test]
    public void ReactiveScene_ScrollHost_WheelScrollUpdatesOffset()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        ScrollHost(page.Scene, "reactive-scroll-host");
    }

    [Test]
    public void ReactiveScene_FormsAndTables_ExposeValidationAndAutoSizedColumns()
    {
        var page = UiShowcaseFactory.CreateReactivePage();
        page.Scene.Layout(1280, 720);

        AssertShowcaseFormValidation(page.Scene, "reactive");
        UiNode table = page.Scene.FindByElementId("reactive-stats-table")!;
        AssertTableFirstColumnWider(table);
        AssertTableSpans(table);
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
        Assert.That(scene.FindByElementId("markup-email-input"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-password-input"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-notes-input"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-stats-table"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-frosted-card"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-wrap-demo"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-scroll-host"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-clip-host"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-transition-probe"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-rtl-text"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-image-cover"), Is.Not.Null);
        Assert.That(scene.FindByElementId("markup-image-nine"), Is.Not.Null);
        AssertPhaseOneShowcase(scene, "markup");
        AssertPhaseTwoShowcase(scene, "markup");
        AssertPhaseThreeShowcase(scene, "markup");
        AssertPhaseFourShowcase(scene, "markup");
        AssertPhaseFiveShowcase(scene, "markup");
        Assert.That(scene.QuerySelectorAll(".prototype-box").Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void MarkupScene_FormsAndTables_ExposeValidationAndAutoSizedColumns()
    {
        UiScene scene = UiShowcaseFactory.CreateMarkupScene();
        scene.Layout(1280, 720);

        AssertShowcaseFormValidation(scene, "markup");
        UiNode table = scene.FindByElementId("markup-stats-table")!;
        AssertTableFirstColumnWider(table);
        AssertTableSpans(table);
    }

    [Test]
    public void ComposeScene_TransitionProbe_AdvancesInterpolatedRenderStyle()
    {
        UiScene scene = UiShowcaseFactory.CreateComposeScene();
        scene.Layout(1280, 720);
        UiNode probe = scene.FindByElementId("compose-transition-probe")!;
        var startColor = probe.RenderStyle.BackgroundColor;

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, probe.LayoutRect.X + 2f, probe.LayoutRect.Y + 2f, probe.Id));
        scene.Layout(1280, 720);
        bool advanced = scene.AdvanceTime(0.16f);

        Assert.That(result.Handled, Is.True);
        Assert.That(advanced, Is.True);
        Assert.That(probe.RenderStyle.BackgroundColor, Is.Not.EqualTo(startColor));
    }

    [Test]
    public void ComposeScene_PhaseFiveProbe_AdvancesKeyframeRenderStyle()
    {
        UiScene scene = UiShowcaseFactory.CreateComposeScene();
        scene.Layout(1280, 720);
        UiNode pulse = scene.FindByElementId("compose-phase5-pulse")!;
        UiNode breathe = scene.FindByElementId("compose-phase5-breathe")!;
        SKColor startColor = pulse.RenderStyle.BackgroundColor;

        bool advanced = scene.AdvanceTime(0.24f);

        Assert.That(advanced, Is.True);
        Assert.That(pulse.RenderStyle.BackgroundColor, Is.Not.EqualTo(startColor));
        Assert.That(pulse.RenderStyle.Opacity, Is.LessThan(1f));
        Assert.That(breathe.RenderStyle.BackdropBlurRadius, Is.GreaterThan(0f));
    }

    [Test]
    public void MarkupScene_ScrollHost_WheelScrollUpdatesOffset()
    {
        UiScene scene = UiShowcaseFactory.CreateMarkupScene();
        ScrollHost(scene, "markup-scroll-host");
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

    private static void ScrollHost(UiScene scene, string elementId)
    {
        scene.Layout(1280, 720);
        UiNode host = scene.FindByElementId(elementId)!;
        float centerX = host.LayoutRect.X + (host.LayoutRect.Width / 2f);
        float centerY = host.LayoutRect.Y + (host.LayoutRect.Height / 2f);

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, 0, centerX, centerY, host.Id, 0f, 56f));

        Assert.That(result.Handled, Is.True);
        Assert.That(host.ScrollOffsetY, Is.GreaterThan(0f));
    }

    private static void AssertShowcaseFormValidation(UiScene scene, string prefix)
    {
        UiNode email = scene.FindByElementId($"{prefix}-email-input")!;
        UiNode password = scene.FindByElementId($"{prefix}-password-input")!;
        UiNode notes = scene.FindByElementId($"{prefix}-notes-input")!;

        Assert.That(email.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(password.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(notes.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(email.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(password.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(notes.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(email.Style.BorderWidth, Is.EqualTo(2f).Within(0.01f));
        Assert.That(password.Style.BorderWidth, Is.EqualTo(2f).Within(0.01f));
        Assert.That(notes.Style.BorderWidth, Is.EqualTo(2f).Within(0.01f));
    }

    private static void AssertPhaseOneShowcase(UiScene scene, string prefix)
    {
        UiNode selectorLab = scene.FindByElementId($"{prefix}-selector-lab")!;
        UiNode stackFront = scene.FindByElementId($"{prefix}-stack-front")!;
        UiNode stackBack = scene.FindByElementId($"{prefix}-stack-back")!;

        Assert.That(selectorLab, Is.Not.Null);
        Assert.That(scene.FindByElementId($"{prefix}-selector-note"), Is.Not.Null);
        Assert.That(scene.FindByElementId($"{prefix}-stack-host"), Is.Not.Null);
        Assert.That(stackFront.RenderStyle.ZIndex, Is.GreaterThan(stackBack.RenderStyle.ZIndex));
        Assert.That(stackFront.RenderStyle.Transform.HasOperations, Is.True);
        Assert.That(TryHitNodeAtRenderedCenter(scene, stackFront), Is.True);
    }

    private static void AssertPhaseTwoShowcase(UiScene scene, string prefix)
    {
        UiNode panel = scene.FindByElementId($"{prefix}-phase2-panel")!;
        UiNode surface = scene.FindByElementId($"{prefix}-phase2-surface")!;
        UiNode borderChip = scene.FindByElementId($"{prefix}-phase2-border-chip")!;
        UiNode maskHost = scene.FindByElementId($"{prefix}-phase2-mask-host")!;
        UiNode maskLabel = scene.FindByElementId($"{prefix}-phase2-mask-label")!;
        UiNode clipHost = scene.FindByElementId($"{prefix}-phase2-clip-host")!;
        UiNode clipLabel = scene.FindByElementId($"{prefix}-phase2-clip-label")!;
        UiNode clipRibbon = scene.FindByElementId($"{prefix}-phase2-clip-ribbon")!;

        Assert.That(panel, Is.Not.Null);
        Assert.That(surface.Style.BackgroundLayers.Count, Is.GreaterThan(1));
        Assert.That(surface.Style.BoxShadows.Count, Is.GreaterThan(1));
        Assert.That(surface.Style.BorderStyle, Is.EqualTo(UiBorderStyle.Dashed));
        Assert.That(borderChip.Style.BorderStyle, Is.EqualTo(UiBorderStyle.Dotted));
        Assert.That(maskHost.Style.MaskGradient, Is.Not.Null);
        Assert.That(maskLabel, Is.Not.Null);
        Assert.That(clipHost.Style.ClipPath, Is.Not.Null);
        Assert.That(clipHost.Style.ClipPath!.Kind, Is.EqualTo(UiClipPathKind.Circle));
        Assert.That(clipLabel, Is.Not.Null);
        Assert.That(clipRibbon, Is.Not.Null);
    }

    private static void AssertPhaseThreeShowcase(UiScene scene, string prefix)
    {
        UiNode panel = scene.FindByElementId($"{prefix}-phase3-panel")!;
        UiNode multilingual = scene.FindByElementId($"{prefix}-phase3-multilingual")!;
        UiNode rtl = scene.FindByElementId($"{prefix}-phase3-rtl")!;
        UiNode ellipsis = scene.FindByElementId($"{prefix}-phase3-ellipsis")!;
        UiNode decoration = scene.FindByElementId($"{prefix}-phase3-decoration")!;

        Assert.That(panel, Is.Not.Null);
        Assert.That(multilingual, Is.Not.Null);
        Assert.That(rtl.Style.Direction, Is.EqualTo(UiTextDirection.Rtl));
        Assert.That(ellipsis.Style.WhiteSpace, Is.EqualTo(UiWhiteSpace.NoWrap));
        Assert.That(ellipsis.Style.TextOverflow, Is.EqualTo(UiTextOverflow.Ellipsis));
        Assert.That(decoration.Style.TextDecorationLine.HasFlag(UiTextDecorationLine.Underline), Is.True);
        Assert.That(decoration.Style.TextDecorationLine.HasFlag(UiTextDecorationLine.LineThrough), Is.True);
    }

    private static void AssertPhaseFourShowcase(UiScene scene, string prefix)
    {
        UiNode panel = scene.FindByElementId($"{prefix}-phase4-panel")!;
        UiNode backgroundHost = scene.FindByElementId($"{prefix}-phase4-bg-host")!;
        UiNode svgImage = scene.FindByElementId($"{prefix}-phase4-svg-image")!;
        UiNode canvas = scene.FindByElementId($"{prefix}-phase4-canvas")!;

        Assert.That(panel, Is.Not.Null);
        Assert.That(backgroundHost.Style.BackgroundLayers.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(backgroundHost.Style.BackgroundLayers.Any(layer => !string.IsNullOrWhiteSpace(layer.ImageSource)), Is.True);
        Assert.That(backgroundHost.Style.BackgroundSizes.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(backgroundHost.Style.BackgroundSizes[0].Mode, Is.EqualTo(UiBackgroundSizeMode.Cover));
        Assert.That(backgroundHost.Style.BackgroundPositions.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(backgroundHost.Style.BackgroundRepeats.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(backgroundHost.Style.BackgroundRepeats[0], Is.EqualTo(UiBackgroundRepeat.NoRepeat));
        Assert.That(svgImage.Kind, Is.EqualTo(UiNodeKind.Image));
        Assert.That(svgImage.Attributes["src"], Does.StartWith("data:image/svg+xml"));
        Assert.That(canvas.TagName, Is.EqualTo("canvas"));
        Assert.That(canvas.CanvasContent, Is.Not.Null);
    }

    private static void AssertPhaseFiveShowcase(UiScene scene, string prefix)
    {
        UiNode panel = scene.FindByElementId($"{prefix}-phase5-panel")!;
        UiNode pulse = scene.FindByElementId($"{prefix}-phase5-pulse")!;
        UiNode breathe = scene.FindByElementId($"{prefix}-phase5-breathe")!;

        Assert.That(panel, Is.Not.Null);
        Assert.That(pulse.Style.Animation, Is.Not.Null);
        Assert.That(breathe.Style.Animation, Is.Not.Null);
        Assert.That(pulse.RenderStyle.BackgroundColor, Is.Not.EqualTo(SKColors.Transparent));
        Assert.That(breathe.Style.OutlineWidth, Is.GreaterThan(0f));
    }

    private static bool TryHitNodeAtRenderedCenter(UiScene scene, UiNode node)
    {
        SKPoint renderedCenter = MapRenderedCenter(node);
        float[] offsets = [0f, -2f, 2f, -6f, 6f];

        for (int yIndex = 0; yIndex < offsets.Length; yIndex++)
        {
            for (int xIndex = 0; xIndex < offsets.Length; xIndex++)
            {
                UiNode? hit = scene.HitTest(renderedCenter.X + offsets[xIndex], renderedCenter.Y + offsets[yIndex]);
                if (ReferenceEquals(hit, node))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SKPoint MapRenderedCenter(UiNode node)
    {
        UiRect rect = node.LayoutRect;
        SKPoint center = new(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.5f));
        UiTransform transform = node.RenderStyle.Transform;
        if (!transform.HasOperations)
        {
            return center;
        }

        float centerX = rect.X + (rect.Width * 0.5f);
        float centerY = rect.Y + (rect.Height * 0.5f);
        SKMatrix matrix = SKMatrix.Identity;

        for (int i = 0; i < transform.Operations.Count; i++)
        {
            UiTransformOperation operation = transform.Operations[i];
            SKMatrix operationMatrix = operation.Kind switch
            {
                UiTransformOperationKind.Translate => SKMatrix.CreateTranslation(ResolveLength(operation.XLength, rect.Width), ResolveLength(operation.YLength, rect.Height)),
                UiTransformOperationKind.Scale => SKMatrix.CreateScale(operation.ScaleX, operation.ScaleY, centerX, centerY),
                UiTransformOperationKind.Rotate => SKMatrix.CreateRotationDegrees(operation.AngleDegrees, centerX, centerY),
                _ => SKMatrix.Identity
            };

            matrix = SKMatrix.Concat(matrix, operationMatrix);
        }

        return matrix.MapPoint(center);
    }

    private static float ResolveLength(UiLength length, float available)
    {
        return length.IsAuto ? 0f : length.Resolve(available);
    }

    private static void AssertTableFirstColumnWider(UiNode table)
    {
        UiNode headerRow = ResolveTableRows(table)[0];
        UiNode bodyRow = ResolveTableRows(table)[1];
        UiNode headerFirst = ResolveTableCells(headerRow)[0];
        UiNode headerSecond = ResolveTableCells(headerRow)[1];
        UiNode bodyFirst = ResolveTableCells(bodyRow)[0];
        UiNode bodySecond = ResolveTableCells(bodyRow)[1];

        Assert.That(headerFirst.LayoutRect.Width, Is.GreaterThan(headerSecond.LayoutRect.Width));
        Assert.That(bodyFirst.LayoutRect.Width, Is.GreaterThan(bodySecond.LayoutRect.Width));
        Assert.That(bodyFirst.LayoutRect.Width, Is.EqualTo(headerFirst.LayoutRect.Width).Within(2f));
        Assert.That(bodySecond.LayoutRect.Width, Is.EqualTo(headerSecond.LayoutRect.Width).Within(2f));
    }

    private static void AssertTableSpans(UiNode table)
    {
        List<UiNode> rows = ResolveTableRows(table);
        UiNode firstBodyRow = rows[1];
        UiNode secondBodyRow = rows[2];
        UiNode footerRow = rows[3];

        UiNode spanCell = ResolveTableCells(firstBodyRow)[0];
        UiNode firstBodyRole = ResolveTableCells(firstBodyRow)[1];
        UiNode secondBodyRole = ResolveTableCells(secondBodyRow)[0];
        UiNode footerCell = ResolveTableCells(footerRow)[0];

        Assert.That(secondBodyRole.LayoutRect.X, Is.EqualTo(firstBodyRole.LayoutRect.X).Within(2f));
        Assert.That(spanCell.LayoutRect.Bottom, Is.EqualTo(secondBodyRole.LayoutRect.Bottom).Within(2f));
        Assert.That(footerCell.LayoutRect.X, Is.EqualTo(spanCell.LayoutRect.X).Within(2f));
        Assert.That(footerCell.LayoutRect.Width, Is.EqualTo(spanCell.LayoutRect.Width + firstBodyRole.LayoutRect.Width).Within(2f));
    }

    private static List<UiNode> ResolveTableRows(UiNode table)
    {
        List<UiNode> rows = new();
        foreach (UiNode child in table.Children)
        {
            if (child.Kind == UiNodeKind.TableRow)
            {
                rows.Add(child);
                continue;
            }

            if (child.Kind is UiNodeKind.TableHeader or UiNodeKind.TableBody or UiNodeKind.TableFooter)
            {
                foreach (UiNode row in child.Children)
                {
                    if (row.Kind == UiNodeKind.TableRow)
                    {
                        rows.Add(row);
                    }
                }
            }
        }

        return rows;
    }

    private static List<UiNode> ResolveTableCells(UiNode row)
    {
        List<UiNode> cells = new();
        foreach (UiNode child in row.Children)
        {
            if (child.Kind is UiNodeKind.TableHeaderCell or UiNodeKind.TableCell)
            {
                cells.Add(child);
            }
        }

        return cells;
    }
}
