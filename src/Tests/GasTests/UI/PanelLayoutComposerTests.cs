using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Panels;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using NUnit.Framework;

namespace GasTests.UI;

[TestFixture]
public sealed class PanelLayoutComposerTests
{
    [Test]
    public void ComposeControls_PanelAndNarrativeScopesSharePercentProgressInterpretation()
    {
        var control = Control(
            PanelLayoutControlType.ProgressBar,
            current: "current",
            max: "max");
        var composer = new PanelLayoutComposer();

        UiNode panel = Build(composer.ComposeControls(
            new[] { control },
            new DictionaryScope(floats: new Dictionary<string, float>
            {
                ["current"] = 25f,
                ["max"] = 100f,
            }),
            static imageId => throw new InvalidOperationException(imageId)));
        UiNode narrative = Build(composer.ComposeControls(
            new[] { control },
            new DictionaryScope(floats: new Dictionary<string, float>
            {
                ["current"] = 0.25f,
                ["max"] = 1f,
            }),
            static source => source));

        AssertPercentProgress(panel, expectedFillPercent: 25f);
        AssertPercentProgress(narrative, expectedFillPercent: 25f);
    }

    [Test]
    public void ComposeControls_ResolvesPanelImageIdAndAcceptsExplicitNarrativeSource()
    {
        var control = Control(
            PanelLayoutControlType.Image,
            bind: "image",
            width: 64f,
            height: 48f);
        var composer = new PanelLayoutComposer();
        string? resolvedImageId = null;

        UiNode panel = Build(composer.ComposeControls(
            new[] { control },
            new DictionaryScope(text: new Dictionary<string, string>
            {
                ["image"] = "hero.portrait",
            }),
            imageId =>
            {
                resolvedImageId = imageId;
                return "/resolved/hero.png";
            }));
        UiNode narrative = Build(composer.ComposeControls(
            new[] { control },
            new DictionaryScope(text: new Dictionary<string, string>
            {
                ["image"] = "/already/resolved/portrait.png",
            }),
            static resolvedSource => resolvedSource));

        Assert.That(resolvedImageId, Is.EqualTo("hero.portrait"));
        Assert.That(panel.Children[0].Attributes["src"], Is.EqualTo("/resolved/hero.png"));
        Assert.That(narrative.Children[0].Attributes["src"], Is.EqualTo("/already/resolved/portrait.png"));
        Assert.That(panel.Children[0].ClassNames, Does.Contain("control-image"));
        Assert.That(narrative.Children[0].ClassNames, Does.Contain("control-image"));
    }

    [Test]
    public void Compose_UnknownControlTypeThrows()
    {
        var composer = new PanelLayoutComposer();
        PanelLayoutControl control = Control((PanelLayoutControlType)byte.MaxValue);

        Assert.That(
            () => composer.Compose(control, new DictionaryScope(), static source => source),
            Throws.InvalidOperationException.With.Message.Contains("is not supported"));
    }

    [Test]
    public void PanelBindingScope_MissingDeclaredValueThrowsButZeroRemainsValid()
    {
        var values = new PanelVariableSet(
            "tests.panel.bindings",
            new Dictionary<string, float> { ["zero"] = 0f },
            revision: 0);
        var scope = new PanelBindingScope(values);

        Assert.That(scope.ReadFloat("zero"), Is.Zero);
        Assert.That(scope.ReadBool("zero"), Is.False);
        Assert.That(scope.ReadText("zero"), Is.EqualTo("0"));
        Assert.That(scope.IsPresent("zero"), Is.True);
        Assert.That(
            () => scope.ReadText("missing"),
            Throws.InvalidOperationException.With.Message.Contains("missing"));
        Assert.That(
            () => scope.ReadFloat("missing"),
            Throws.InvalidOperationException.With.Message.Contains("missing"));
        Assert.That(
            () => scope.ReadBool("missing"),
            Throws.InvalidOperationException.With.Message.Contains("missing"));
    }

    [Test]
    public void PanelBindingScope_MissingItemValueDoesNotFallBackToHostPin()
    {
        var values = new PanelVariableSet(
            "tests.panel.bindings",
            new Dictionary<string, float> { ["health"] = 100f },
            revision: 0);
        var item = new PanelListItemProjection(
            new Dictionary<string, float>(),
            new Dictionary<string, bool>(),
            new Dictionary<string, string>());
        var scope = new PanelBindingScope(values, item);

        Assert.That(
            () => scope.ReadFloat("health"),
            Throws.InvalidOperationException.With.Message.Contains("health"));
    }

    [Test]
    public void ComposeControls_DefaultTextProgressAndBadgeUseSemanticClassesWithoutSkinColors()
    {
        var controls = new[]
        {
            Control(PanelLayoutControlType.Label, bind: "label"),
            Control(PanelLayoutControlType.ProgressBar, current: "current", max: "max"),
            new PanelLayoutControl(
                PanelLayoutControlType.Badge,
                className: null,
                text: "Ready",
                bind: "ready",
                prefix: null,
                current: null,
                max: null,
                showWhen: true)
        };
        var scope = new DictionaryScope(
            text: new Dictionary<string, string> { ["label"] = "Status" },
            floats: new Dictionary<string, float> { ["current"] = 0f, ["max"] = 100f },
            bools: new Dictionary<string, bool> { ["ready"] = true });

        UiNode root = Build(new PanelLayoutComposer().ComposeControls(
            controls,
            scope,
            static source => source));

        UiNode label = root.Children[0];
        UiNode progress = root.Children[1];
        UiNode caption = progress.Children[0];
        UiNode track = progress.Children[1];
        UiNode fill = track.Children[0];
        UiNode badge = root.Children[2];
        Assert.That(label.ClassNames, Does.Contain("control-label"));
        Assert.That(progress.ClassNames, Does.Contain("control-progress"));
        Assert.That(caption.ClassNames, Does.Contain("progress-caption"));
        Assert.That(track.ClassNames, Does.Contain("progress-track"));
        Assert.That(fill.ClassNames, Does.Contain("progress-fill"));
        Assert.That(badge.ClassNames, Does.Contain("control-badge"));
        Assert.That(label.LocalStyle.Color, Is.EqualTo(UiStyle.Default.Color));
        Assert.That(caption.LocalStyle.Color, Is.EqualTo(UiStyle.Default.Color));
        Assert.That(track.LocalStyle.BackgroundColor, Is.EqualTo(UiStyle.Default.BackgroundColor));
        Assert.That(fill.LocalStyle.BackgroundColor, Is.EqualTo(UiStyle.Default.BackgroundColor));
        Assert.That(badge.LocalStyle.Color, Is.EqualTo(UiStyle.Default.Color));
    }

    private static void AssertPercentProgress(UiNode root, float expectedFillPercent)
    {
        UiNode progress = root.Children[0];
        UiNode track = progress.Children[1];
        UiNode fill = track.Children[0];

        Assert.That(progress.LocalStyle.Width, Is.EqualTo(UiLength.Percent(100f)));
        Assert.That(track.LocalStyle.Width, Is.EqualTo(UiLength.Percent(100f)));
        Assert.That(fill.LocalStyle.Width, Is.EqualTo(UiLength.Percent(expectedFillPercent)));
    }

    private static UiNode Build(UiElementBuilder builder)
    {
        var dispatcher = new UiDispatcher();
        int nextId = 1;
        return builder.Build(dispatcher, ref nextId);
    }

    private static PanelLayoutControl Control(
        PanelLayoutControlType type,
        string? bind = null,
        string? current = null,
        string? max = null,
        float? width = null,
        float? height = null)
    {
        return new PanelLayoutControl(
            type,
            className: null,
            text: null,
            bind,
            prefix: null,
            current,
            max,
            showWhen: null,
            width: width,
            height: height);
    }

    private sealed class DictionaryScope : IPanelLayoutBindingScope
    {
        private readonly IReadOnlyDictionary<string, string> _text;
        private readonly IReadOnlyDictionary<string, float> _floats;
        private readonly IReadOnlyDictionary<string, bool> _bools;

        public DictionaryScope(
            IReadOnlyDictionary<string, string>? text = null,
            IReadOnlyDictionary<string, float>? floats = null,
            IReadOnlyDictionary<string, bool>? bools = null)
        {
            _text = text ?? new Dictionary<string, string>();
            _floats = floats ?? new Dictionary<string, float>();
            _bools = bools ?? new Dictionary<string, bool>();
        }

        public string ReadText(string bind) => _text[bind];

        public float ReadFloat(string bind) => _floats[bind];

        public bool ReadBool(string bind) => _bools[bind];

        public IReadOnlyList<PresentationTextRun> ReadTextRuns(string bind)
            => throw new InvalidOperationException(bind);

        public IReadOnlyList<IPanelLayoutBindingScope> ReadList(string bind)
            => throw new InvalidOperationException(bind);

        public bool IsPresent(string bind)
            => _text.TryGetValue(bind, out string? value) && !string.IsNullOrWhiteSpace(value);
    }
}
