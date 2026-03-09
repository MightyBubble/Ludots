using Ludots.UI.Runtime;
using Ludots.UI.HtmlEngine.Markup;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.UIRuntime;

[TestFixture]
public sealed class UiRenderingTests
{
    [Test]
    public void UiSceneRenderer_RendersGradientOutline_AndShadow()
    {
        UiStyle cardStyle = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(20f),
            Top = UiLength.Px(20f),
            Width = UiLength.Px(80f),
            Height = UiLength.Px(60f),
            BackgroundGradient = new UiLinearGradient(90f, new[]
            {
                new UiGradientStop(0f, SKColor.Parse("#ff0000")),
                new UiGradientStop(1f, SKColor.Parse("#0000ff"))
            }),
            OutlineWidth = 4f,
            OutlineColor = SKColor.Parse("#00ff00"),
            BoxShadow = new UiShadow(8f, 8f, 10f, 0f, new SKColor(0, 0, 0, 180))
        };

        UiNode card = new(new UiNodeId(2), UiNodeKind.Card, style: cardStyle);
        UiNode root = new(new UiNodeId(1), UiNodeKind.Container, style: UiStyle.Default with { BackgroundColor = SKColors.Transparent }, children: new[] { card });
        UiScene scene = new();
        scene.Mount(root);

        using SKBitmap bitmap = new(160, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        UiSceneRenderer renderer = new();
        renderer.Render(scene, canvas, 160, 140);

        SKColor topPixel = bitmap.GetPixel(60, 28);
        SKColor bottomPixel = bitmap.GetPixel(60, 74);
        SKColor outlinePixel = bitmap.GetPixel(18, 50);
        SKColor shadowPixel = bitmap.GetPixel(106, 88);

        Assert.That(topPixel.Red, Is.GreaterThan(topPixel.Blue));
        Assert.That(bottomPixel.Blue, Is.GreaterThan(bottomPixel.Red));
        Assert.That(outlinePixel.Green, Is.GreaterThan(200));
        Assert.That(shadowPixel.Alpha, Is.GreaterThan(0));
    }

    [Test]
    public void UiLayoutEngine_WrapsText_WhenWidthIsConstrained()
    {
        UiNode wrapped = new(
            new UiNodeId(2),
            UiNodeKind.Text,
            style: UiStyle.Default with
            {
                Width = UiLength.Px(72f),
                FontSize = 16f,
                WhiteSpace = UiWhiteSpace.Normal,
                Padding = UiThickness.All(4f)
            },
            textContent: "Alpha beta gamma delta epsilon");

        UiNode singleLine = new(
            new UiNodeId(3),
            UiNodeKind.Text,
            style: UiStyle.Default with
            {
                Width = UiLength.Px(72f),
                FontSize = 16f,
                WhiteSpace = UiWhiteSpace.NoWrap,
                Padding = UiThickness.All(4f)
            },
            textContent: "Alpha beta gamma delta epsilon");

        UiNode root = new(new UiNodeId(1), UiNodeKind.Column, children: new[] { wrapped, singleLine });
        UiScene scene = new();
        scene.Mount(root);
        scene.Layout(120, 220);

        Assert.That(wrapped.LayoutRect.Height, Is.GreaterThan(singleLine.LayoutRect.Height));
        Assert.That(wrapped.LayoutRect.Height, Is.GreaterThan(40f));
    }

    [Test]
    public void UiLayoutEngine_FlexWrap_MovesOverflowingChildrenToNextLine()
    {
        UiNode row = new(
            new UiNodeId(1),
            UiNodeKind.Row,
            style: UiStyle.Default with
            {
                Width = UiLength.Px(170f),
                FlexDirection = UiFlexDirection.Row,
                FlexWrap = UiFlexWrap.Wrap,
                AlignContent = UiAlignContent.Start,
                Gap = 8f,
                RowGap = 8f,
                ColumnGap = 8f
            },
            children: new[]
            {
                new UiNode(new UiNodeId(2), UiNodeKind.Button, style: UiStyle.Default with { Width = UiLength.Px(80f), Height = UiLength.Px(32f) }, textContent: "A"),
                new UiNode(new UiNodeId(3), UiNodeKind.Button, style: UiStyle.Default with { Width = UiLength.Px(80f), Height = UiLength.Px(32f) }, textContent: "B"),
                new UiNode(new UiNodeId(4), UiNodeKind.Button, style: UiStyle.Default with { Width = UiLength.Px(80f), Height = UiLength.Px(32f) }, textContent: "C")
            });

        UiScene scene = new();
        scene.Mount(row);
        scene.Layout(200, 200);

        UiNode second = scene.FindNode(new UiNodeId(3))!;
        UiNode third = scene.FindNode(new UiNodeId(4))!;

        Assert.That(third.LayoutRect.Y, Is.GreaterThan(second.LayoutRect.Y));
    }

    [Test]
    public void UiSceneRenderer_FilterBlur_BlursNodePixelsBeyondOriginalEdge()
    {
        UiNode blurred = new(
            new UiNodeId(2),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(30f),
                Top = UiLength.Px(30f),
                Width = UiLength.Px(60f),
                Height = UiLength.Px(40f),
                BackgroundColor = SKColor.Parse("#ff3366"),
                FilterBlurRadius = 10f
            });

        UiNode root = new(new UiNodeId(1), UiNodeKind.Container, children: new[] { blurred });
        UiScene scene = new();
        scene.Mount(root);

        using SKBitmap bitmap = new(160, 120);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        UiSceneRenderer renderer = new();
        renderer.Render(scene, canvas, 160, 120);

        SKColor outsidePixel = bitmap.GetPixel(24, 50);
        Assert.That(outsidePixel.Alpha, Is.GreaterThan(0));
    }

    [Test]
    public void UiSceneRenderer_BackdropBlur_ReplacesUnderlyingRegionWithBlurredPixels()
    {
        UiNode backgroundLeft = new(
            new UiNodeId(2),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(0f),
                Top = UiLength.Px(0f),
                Width = UiLength.Px(60f),
                Height = UiLength.Px(120f),
                BackgroundColor = SKColor.Parse("#ff0000")
            });

        UiNode backgroundRight = new(
            new UiNodeId(3),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(60f),
                Top = UiLength.Px(0f),
                Width = UiLength.Px(60f),
                Height = UiLength.Px(120f),
                BackgroundColor = SKColor.Parse("#0000ff")
            });

        UiNode frosted = new(
            new UiNodeId(4),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(30f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(60f),
                Height = UiLength.Px(80f),
                BackgroundColor = new SKColor(255, 255, 255, 64),
                BackdropBlurRadius = 8f
            });

        UiNode root = new(new UiNodeId(1), UiNodeKind.Container, children: new[] { backgroundLeft, backgroundRight, frosted });
        UiScene scene = new();
        scene.Mount(root);

        using SKBitmap bitmap = new(120, 120);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        UiSceneRenderer renderer = new();
        renderer.Render(scene, canvas, 120, 120);

        SKColor blurredCenter = bitmap.GetPixel(60, 60);
        Assert.That(blurredCenter.Red, Is.GreaterThan(20));
        Assert.That(blurredCenter.Blue, Is.GreaterThan(20));
    }

    [Test]
    public void UiSceneRenderer_BackdropBlur_PartialSurfaceOverlap_DoesNotThrow()
    {
        UiNode background = new(
            new UiNodeId(2),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(0f),
                Top = UiLength.Px(0f),
                Width = UiLength.Px(120f),
                Height = UiLength.Px(80f),
                BackgroundColor = SKColor.Parse("#2563eb")
            });

        UiNode frosted = new(
            new UiNodeId(3),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(92f),
                Top = UiLength.Px(16f),
                Width = UiLength.Px(48f),
                Height = UiLength.Px(40f),
                BackgroundColor = new SKColor(255, 255, 255, 64),
                BackdropBlurRadius = 10f
            });

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { background, frosted }));

        using SKBitmap bitmap = new(120, 80);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        UiSceneRenderer renderer = new();
        Assert.DoesNotThrow(() => renderer.Render(scene, canvas, 120, 80));
    }

    [Test]
    public void UiSceneRenderer_RendersObjectFitContainImageWithinDestinationBounds()
    {
        UiAttributeBag attributes = new();
        attributes["src"] = CreateSolidImageDataUri(120, 60, SKColor.Parse("#ff3355"));

        UiNode image = new(
            new UiNodeId(2),
            UiNodeKind.Image,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(20f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(80f),
                Height = UiLength.Px(80f),
                ObjectFit = UiObjectFit.Contain
            },
            attributes: attributes);

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { image }));

        using SKBitmap bitmap = new(140, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 140, 140);

        SKColor topGapPixel = bitmap.GetPixel(60, 26);
        SKColor centerPixel = bitmap.GetPixel(60, 60);

        Assert.That(topGapPixel.Alpha, Is.EqualTo(0));
        Assert.That(centerPixel.Red, Is.GreaterThan(200));
    }

    [Test]
    public void UiSceneRenderer_RendersNineSliceImagePreservingCornersAndCenter()
    {
        UiAttributeBag attributes = new();
        attributes["src"] = CreateFramedImageDataUri();

        UiNode image = new(
            new UiNodeId(2),
            UiNodeKind.Image,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(20f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(120f),
                Height = UiLength.Px(80f),
                ImageSlice = UiThickness.All(18f)
            },
            attributes: attributes);

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { image }));

        using SKBitmap bitmap = new(180, 120);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 120);

        SKColor cornerPixel = bitmap.GetPixel(24, 24);
        SKColor centerPixel = bitmap.GetPixel(80, 60);

        Assert.That(cornerPixel.Blue, Is.GreaterThan(40));
        Assert.That(centerPixel.Red, Is.GreaterThan(200));
    }

    [Test]
    public void UiScene_AdvanceTime_InterpolatesTransitionedRenderStyle()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            "<button id='probe' class='probe'>Probe</button>",
            ".probe { background-color:#202040; opacity:1; transition: background-color 400ms linear, opacity 400ms linear; } .probe:focus { background-color:#80ff20; opacity:0.5; }");

        scene.Layout(220, 120);
        UiNode probe = scene.FindByElementId("probe")!;
        SKColor startColor = probe.RenderStyle.BackgroundColor;

        scene.Dispatch(new Ludots.UI.Runtime.Events.UiPointerEvent(Ludots.UI.Runtime.Events.UiPointerEventType.Click, 0, probe.LayoutRect.X + 4f, probe.LayoutRect.Y + 4f, probe.Id));
        scene.Layout(220, 120);

        Assert.That(probe.RenderStyle.BackgroundColor, Is.EqualTo(startColor));
        Assert.That(scene.AdvanceTime(0.2f), Is.True);
        Assert.That(probe.RenderStyle.BackgroundColor, Is.Not.EqualTo(startColor));
        Assert.That(probe.RenderStyle.Opacity, Is.EqualTo(0.75f).Within(0.08f));

        scene.AdvanceTime(0.25f);

        Assert.That(probe.RenderStyle.BackgroundColor, Is.EqualTo(probe.Style.BackgroundColor));
        Assert.That(probe.RenderStyle.Opacity, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void UiScene_AdvanceTime_InterpolatesKeyframeAnimationRenderStyle()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            "<div id='probe' class='probe'>Probe</div>",
            """
            @keyframes pulse {
                from { background-color:#202040; opacity:1; filter:blur(0px); }
                to { background-color:#80ff20; opacity:0.5; filter:blur(6px); }
            }

            .probe {
                animation: pulse 400ms linear 0s 1 normal both;
            }
            """);

        scene.Layout(220, 120);
        UiNode probe = scene.FindByElementId("probe")!;

        Assert.That(probe.RenderStyle.BackgroundColor, Is.EqualTo(SKColor.Parse("#202040")));
        Assert.That(probe.RenderStyle.Opacity, Is.EqualTo(1f).Within(0.01f));
        Assert.That(probe.RenderStyle.FilterBlurRadius, Is.EqualTo(0f).Within(0.01f));

        Assert.That(scene.AdvanceTime(0.2f), Is.True);
        Assert.That(probe.RenderStyle.BackgroundColor, Is.Not.EqualTo(SKColor.Parse("#202040")));
        Assert.That(probe.RenderStyle.BackgroundColor, Is.Not.EqualTo(SKColor.Parse("#80ff20")));
        Assert.That(probe.RenderStyle.Opacity, Is.EqualTo(0.75f).Within(0.08f));
        Assert.That(probe.RenderStyle.FilterBlurRadius, Is.EqualTo(3f).Within(0.08f));

        scene.AdvanceTime(0.25f);

        Assert.That(probe.RenderStyle.BackgroundColor, Is.EqualTo(SKColor.Parse("#80ff20")));
        Assert.That(probe.RenderStyle.Opacity, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(probe.RenderStyle.FilterBlurRadius, Is.EqualTo(6f).Within(0.01f));
    }

    [Test]
    public void UiSceneRenderer_RendersHigherZIndexOnTop_AndHitTestHonorsStackOrder()
    {
        UiNode high = new(
            new UiNodeId(2),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(20f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(72f),
                Height = UiLength.Px(72f),
                BackgroundColor = SKColor.Parse("#ef4444"),
                ZIndex = 4
            });

        UiNode low = new(
            new UiNodeId(3),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(40f),
                Top = UiLength.Px(40f),
                Width = UiLength.Px(72f),
                Height = UiLength.Px(72f),
                BackgroundColor = SKColor.Parse("#2563eb"),
                ZIndex = 1
            });

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { high, low }));

        using SKBitmap bitmap = new(140, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 140, 140);

        SKColor overlapPixel = bitmap.GetPixel(62, 62);
        UiNode? hit = scene.HitTest(62f, 62f);

        Assert.That(overlapPixel.Red, Is.GreaterThan(overlapPixel.Blue));
        Assert.That(hit, Is.SameAs(high));
    }

    [Test]
    public void UiSceneRenderer_TranslateTransformAndHitTestStayAligned()
    {
        UiNode transformed = new(
            new UiNodeId(2),
            UiNodeKind.Card,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(20f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(36f),
                Height = UiLength.Px(36f),
                BackgroundColor = SKColor.Parse("#ff5533"),
                Transform = new UiTransform(new[]
                {
                    UiTransformOperation.Translate(UiLength.Px(44f), UiLength.Px(18f))
                })
            });

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { transformed }));

        using SKBitmap bitmap = new(140, 120);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 140, 120);

        SKColor translatedPixel = bitmap.GetPixel(82, 56);
        UiNode? translatedHit = scene.HitTest(82f, 56f);
        UiNode? originalHit = scene.HitTest(38f, 38f);

        Assert.That(translatedPixel.Red, Is.GreaterThan(200));
        Assert.That(translatedHit, Is.SameAs(transformed));
        Assert.That(originalHit, Is.Not.SameAs(transformed));
    }

    private static string CreateSolidImageDataUri(int width, int height, SKColor color)
    {
        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(color);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    private static string CreateFramedImageDataUri()
    {
        using SKBitmap bitmap = new(72, 72);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        using SKPaint border = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#0f172a") };
        using SKPaint accent = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#38bdf8") };
        using SKPaint center = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#f8fafc") };

        canvas.DrawRoundRect(new SKRect(0f, 0f, 72f, 72f), 18f, 18f, border);
        canvas.DrawRoundRect(new SKRect(8f, 8f, 64f, 64f), 14f, 14f, accent);
        canvas.DrawRoundRect(new SKRect(18f, 18f, 54f, 54f), 10f, 10f, center);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    private static string CreateSimpleSvgDataUri()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 80 60\"><rect width=\"80\" height=\"60\" rx=\"10\" fill=\"#2563eb\"/><circle cx=\"58\" cy=\"18\" r=\"10\" fill=\"#f59e0b\"/><rect x=\"12\" y=\"34\" width=\"40\" height=\"12\" rx=\"6\" fill=\"#ffffff\" opacity=\"0.82\"/></svg>";
        return "data:image/svg+xml;utf8," + Uri.EscapeDataString(svg);
    }

    [Test]
    public void UiSceneRenderer_RendersBackgroundImage_WithCoverCenterAndNoRepeat()
    {
        UiStyle style = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(20f),
            Top = UiLength.Px(20f),
            Width = UiLength.Px(120f),
            Height = UiLength.Px(80f),
            BackgroundLayers = new[] { UiBackgroundLayer.FromImage(CreateSimpleSvgDataUri()) },
            BackgroundSizes = new[] { UiBackgroundSize.Cover },
            BackgroundPositions = new[] { UiBackgroundPosition.Center },
            BackgroundRepeats = new[] { UiBackgroundRepeat.NoRepeat }
        };

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Card, style: style)
        }));

        using SKBitmap bitmap = new(180, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 140);

        SKColor centerPixel = bitmap.GetPixel(80, 60);
        Assert.That(centerPixel.Alpha, Is.GreaterThan(0));
        Assert.That(centerPixel.Blue, Is.GreaterThan(80));
    }

    [Test]
    public void UiSceneRenderer_RendersSvgImageNode()
    {
        UiAttributeBag attributes = new();
        attributes["src"] = CreateSimpleSvgDataUri();

        UiNode image = new(
            new UiNodeId(2),
            UiNodeKind.Image,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(20f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(120f),
                Height = UiLength.Px(90f),
                ObjectFit = UiObjectFit.Contain
            },
            attributes: attributes);

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { image }));

        using SKBitmap bitmap = new(180, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 140);

        SKColor pixel = bitmap.GetPixel(76, 58);
        Assert.That(pixel.Alpha, Is.GreaterThan(0));
        Assert.That(pixel.Blue, Is.GreaterThan(80));
    }

    [Test]
    public void UiSceneRenderer_RendersNativeCanvasNode()
    {
        UiNode canvasNode = new(
            new UiNodeId(2),
            UiNodeKind.Custom,
            style: UiStyle.Default with
            {
                PositionType = UiPositionType.Absolute,
                Left = UiLength.Px(20f),
                Top = UiLength.Px(20f),
                Width = UiLength.Px(120f),
                Height = UiLength.Px(80f)
            },
            tagName: "canvas",
            canvasContent: new UiCanvasContent((canvas, rect) =>
            {
                using SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#22c55e") };
                canvas.DrawRoundRect(rect, 10f, 10f, paint);
            }));

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[] { canvasNode }));

        using SKBitmap bitmap = new(180, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 140);

        SKColor pixel = bitmap.GetPixel(78, 60);
        Assert.That(pixel.Green, Is.GreaterThan(140));
        Assert.That(pixel.Alpha, Is.GreaterThan(0));
    }

    [Test]
    public void UiSceneRenderer_RendersMultipleBackgroundLayers_AndMultipleShadows()
    {
        UiStyle layeredStyle = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(20f),
            Top = UiLength.Px(20f),
            Width = UiLength.Px(120f),
            Height = UiLength.Px(80f),
            BackgroundLayers = new UiBackgroundLayer[]
            {
                UiBackgroundLayer.FromColor(new SKColor(64, 128, 255, 160)),
                UiBackgroundLayer.FromColor(new SKColor(255, 64, 64, 255))
            },
            BoxShadows = new UiShadow[]
            {
                new(0f, 6f, 8f, 0f, new SKColor(0, 0, 0, 120)),
                new(12f, 18f, 16f, 0f, new SKColor(37, 99, 235, 96))
            }
        };

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Card, style: layeredStyle)
        }));

        using SKBitmap bitmap = new(180, 150);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 150);

        SKColor centerPixel = bitmap.GetPixel(80, 60);
        SKColor shadowPixel = bitmap.GetPixel(146, 116);

        Assert.That(centerPixel.Red, Is.GreaterThan(80));
        Assert.That(centerPixel.Blue, Is.GreaterThan(80));
        Assert.That(shadowPixel.Alpha, Is.GreaterThan(0));
    }

    [Test]
    public void UiSceneRenderer_RendersDashedBorderStyle_WithVisibleGap()
    {
        UiStyle dashedStyle = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(20f),
            Top = UiLength.Px(20f),
            Width = UiLength.Px(120f),
            Height = UiLength.Px(60f),
            BorderWidth = 4f,
            BorderColor = SKColor.Parse("#22c55e"),
            BorderStyle = UiBorderStyle.Dashed
        };

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Card, style: dashedStyle)
        }));

        using SKBitmap bitmap = new(180, 120);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 120);

        int visibleDashCount = 0;
        int transparentGapCount = 0;
        for (int x = 22; x <= 138; x++)
        {
            SKColor pixel = bitmap.GetPixel(x, 20);
            if (pixel.Green > 120)
            {
                visibleDashCount++;
            }
            else if (pixel.Alpha < 24)
            {
                transparentGapCount++;
            }
        }

        Assert.That(visibleDashCount, Is.GreaterThan(8));
        Assert.That(transparentGapCount, Is.GreaterThan(8));
    }

    [Test]
    public void UiSceneRenderer_ClipPathCircle_ClipsCorners()
    {
        UiStyle clippedStyle = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(20f),
            Top = UiLength.Px(20f),
            Width = UiLength.Px(100f),
            Height = UiLength.Px(100f),
            BackgroundColor = SKColor.Parse("#ff8844"),
            ClipPath = UiClipPath.Circle(UiLength.Percent(40f), UiLength.Percent(50f), UiLength.Percent(50f))
        };

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Card, style: clippedStyle)
        }));

        using SKBitmap bitmap = new(160, 160);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 160, 160);

        SKColor cornerPixel = bitmap.GetPixel(24, 24);
        SKColor centerPixel = bitmap.GetPixel(70, 70);

        Assert.That(cornerPixel.Alpha, Is.EqualTo(0));
        Assert.That(centerPixel.Red, Is.GreaterThan(200));
    }

    [Test]
    public void UiSceneRenderer_MaskGradient_FadesNodeAcrossAxis()
    {
        UiStyle maskedStyle = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(20f),
            Top = UiLength.Px(20f),
            Width = UiLength.Px(120f),
            Height = UiLength.Px(80f),
            BackgroundColor = SKColor.Parse("#3b82f6"),
            MaskGradient = new UiLinearGradient(0f, new[]
            {
                new UiGradientStop(0f, new SKColor(255, 255, 255, 0)),
                new UiGradientStop(1f, new SKColor(255, 255, 255, 255))
            })
        };

        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Card, style: maskedStyle)
        }));

        using SKBitmap bitmap = new(180, 140);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        new UiSceneRenderer().Render(scene, canvas, 180, 140);

        SKColor leftPixel = bitmap.GetPixel(24, 60);
        SKColor rightPixel = bitmap.GetPixel(132, 60);

        Assert.That(leftPixel.Alpha, Is.LessThan(90));
        Assert.That(rightPixel.Blue, Is.GreaterThan(150));
        Assert.That(rightPixel.Alpha, Is.GreaterThan(leftPixel.Alpha));
    }
    [Test]
    public void UiTextLayout_NoWrapEllipsis_TruncatesWithinAvailableWidth()
    {
        UiStyle style = UiStyle.Default with
        {
            FontSize = 18f,
            WhiteSpace = UiWhiteSpace.NoWrap,
            TextOverflow = UiTextOverflow.Ellipsis
        };

        UiTextLayoutResult result = UiTextLayout.Measure("Prototype export keeps labels readable 😀", style, 120f, constrainWidth: true);

        Assert.That(result.Lines.Count, Is.EqualTo(1));
        Assert.That(result.Lines[0], Does.EndWith("…"));
        Assert.That(UiTextLayout.MeasureWidth(result.Lines[0], style), Is.LessThanOrEqualTo(120.5f));
    }

        [Test]
    public void UiSceneRenderer_RendersUnderlineAndLineThroughDecorations()
    {
        UiStyle plainStyle = UiStyle.Default with
        {
            PositionType = UiPositionType.Absolute,
            Left = UiLength.Px(12f),
            Top = UiLength.Px(16f),
            Width = UiLength.Px(160f),
            Height = UiLength.Px(48f),
            FontSize = 24f,
            Color = SKColor.Parse("#38bdf8")
        };

        UiStyle decoratedStyle = plainStyle with
        {
            TextDecorationLine = UiTextDecorationLine.Underline | UiTextDecorationLine.LineThrough
        };

        UiScene plainScene = new();
        plainScene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Text, style: plainStyle, textContent: "Decorated")
        }));

        UiScene decoratedScene = new();
        decoratedScene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container, children: new[]
        {
            new UiNode(new UiNodeId(2), UiNodeKind.Text, style: decoratedStyle, textContent: "Decorated")
        }));

        using SKBitmap plainBitmap = new(220, 96);
        using SKCanvas plainCanvas = new(plainBitmap);
        plainCanvas.Clear(SKColors.Transparent);
        new UiSceneRenderer().Render(plainScene, plainCanvas, 220, 96);

        using SKBitmap decoratedBitmap = new(220, 96);
        using SKCanvas decoratedCanvas = new(decoratedBitmap);
        decoratedCanvas.Clear(SKColors.Transparent);
        new UiSceneRenderer().Render(decoratedScene, decoratedCanvas, 220, 96);

        int plainOpaque = CountOpaquePixelsInBitmap(plainBitmap);
        int decoratedOpaque = CountOpaquePixelsInBitmap(decoratedBitmap);

        Assert.That(decoratedOpaque, Is.GreaterThan(plainOpaque + 80));
    }
private static int CountOpaquePixelsInBitmap(SKBitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            count += CountOpaquePixels(bitmap, 0, bitmap.Width - 1, y);
        }
        return count;
    }

    private static int CountOpaquePixelsInBand(SKBitmap bitmap, int left, int right, int top, int bottom)
    {
        int max = 0;
        for (int y = top; y <= bottom; y++)
        {
            int count = CountOpaquePixels(bitmap, left, right, y);
            if (count > max)
            {
                max = count;
            }
        }
        return max;
    }

    private static int CountOpaquePixels(SKBitmap bitmap, int left, int right, int y)
    {
        int count = 0;
        for (int x = left; x <= right; x++)
        {
            if (bitmap.GetPixel(x, y).Alpha > 10)
            {
                count++;
            }
        }
        return count;
    }
}
