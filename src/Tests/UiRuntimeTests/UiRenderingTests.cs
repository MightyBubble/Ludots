using Ludots.UI.Runtime;
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
}
