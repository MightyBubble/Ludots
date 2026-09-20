using System.Collections.Generic;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Skia;
using NUnit.Framework;
using UiShowcaseCoreMod.Showcase;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class PanelTemplateMarkupRendererTests
{
    [Test]
    public void Render_SubstitutesVariablesIntoMarkup()
    {
        var variables = new PanelVariableSet(
            "tests.panel.resource_bar",
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["ore.total"] = 1200f,
                ["gas.total"] = 450.5f,
            },
            revision: 7);

        var scene = PanelTemplateMarkupRenderer.Render(
            "<div id=\"bar\">矿 {ore.total} 气 {gas.total}</div>",
            "#bar { width: 200px; }",
            variables,
            new SkiaTextMeasurer(),
            new SkiaImageSizeProvider());

        Ludots.UI.Runtime.UiNode bar = scene.FindByElementId("bar");
        Assert.That(bar, Is.Not.Null);
        Assert.That(bar.TextContent, Does.Contain("1200"));
        Assert.That(bar.TextContent, Does.Contain("450.5"));
    }

    [Test]
    public void Render_UnknownToken_FailsNamingVariable()
    {
        var variables = new PanelVariableSet(
            "tests.panel.resource_bar",
            new Dictionary<string, float>(StringComparer.Ordinal) { ["ore.total"] = 1f },
            revision: 1);

        Assert.That(
            () => PanelTemplateMarkupRenderer.Render(
                "<div>{ghost}</div>",
                "",
                variables,
                new SkiaTextMeasurer(),
                new SkiaImageSizeProvider()),
            Throws.InvalidOperationException.With.Message.Contains("ghost"));
    }
}
